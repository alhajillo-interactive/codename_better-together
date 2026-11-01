using BetterTogether.Bot;
using BetterTogether.Diagnostics;
using BetterTogether.Fireplace;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Player;
using Fusion;
using UnityEngine;

namespace BetterTogether.Match
{
    /// <summary>
    /// The list of pranks: one active at a time, so the gnomes go to the same place, and one gnome, the Boss, reads it and has to tell
    /// the others. Finishing the list wins the match for them. Only the master writes this state; a level without a
    /// <see cref="TravesuraLayout"/> never uses it.
    /// </summary>
    public partial class MatchManager
    {
        private const int NoTravesura = -1;

        [Header("Travesuras")]
        [Tooltip("Farthest a gnome can be from a point (m), a little more than the interaction reach to absorb latency.")]
        [SerializeField, Min(0f)] private float _travesuraDistance = 3.5f;
        [Tooltip("Farthest the camper can be from a bomb when the master accepts the explosion (m).")]
        [SerializeField, Min(0f)] private float _bombDistance = 3f;
        [Tooltip("Seconds a prank stays active with no part done before another replaces it, so an unreachable point never freezes the list.")]
        [SerializeField, Min(1f)] private float _travesuraPatience = 75f;
        [Tooltip("Metres at which the bot camper hears a prank in progress.")]
        [SerializeField, Min(0f)] private float _pointNoiseRadius = 14f;
        [Tooltip("Seconds only the Boss reads the prank; after that every gnome sees it, so nobody is stuck with a silent Boss.")]
        [SerializeField, Min(0f)] private float _bossSeconds = 20f;

        [Networked] private byte TravDone { get; set; }
        [Networked] private int TravActive { get; set; }
        [Networked] private byte TravPoints { get; set; }
        [Networked] private PlayerRef TravFirst { get; set; }
        [Networked] private PlayerRef TravBoss { get; set; }
        [Networked] private TickTimer TravWindow { get; set; }
        [Networked] private TickTimer TravPatience { get; set; }
        [Networked] private TickTimer TravBossTimer { get; set; }
        [Networked] private int TravFireOuts { get; set; }

        private int _lastBossId;

        /// <summary>True once the replicated state can be read.</summary>
        public bool IsReady => _isSpawned;

        /// <summary>True while a match with a list of pranks is running.</summary>
        public bool TravesurasRunning => _isSpawned && CurrentPhase == MatchPhase.Playing && TravesuraLayout.Current != null;

        /// <summary>One bit per finished prank.</summary>
        public byte TravesurasDoneMask => _isSpawned ? TravDone : (byte)0;

        /// <summary>The prank in progress, or -1 between pranks.</summary>
        public int ActiveTravesura => TravesurasRunning ? TravActive : NoTravesura;

        /// <summary>Finished pranks.</summary>
        public int TravesurasDone => _isSpawned ? CountBits(TravDone) : 0;

        /// <summary>The gnome who reads the active prank.</summary>
        public PlayerRef TravesuraBoss => _isSpawned ? TravBoss : PlayerRef.None;

        /// <summary>True once every gnome can read the prank. Presentation only.</summary>
        public bool TravesuraOpenToAll => _isSpawned && TravBossTimer.ExpiredOrNotRunning(Runner);

        /// <summary>True when this point of the active prank still has to be used.</summary>
        public bool IsPointNeeded(int travesura, int point) =>
            ActiveTravesura == travesura && (TravPoints & (1 << point)) == 0;

        /// <summary>Whether this gnome may use this point now: the prompt and the master use this same check.</summary>
        public bool CanUsePoint(PlayerInteractor interactor, TravesuraPoint point)
        {
            if (interactor == null || point == null || !IsPointNeeded(point.Travesura, point.Point))
                return false;

            if (!interactor.TryGetComponent<PlayerCharacter>(out var character) || character.Role != PlayerRole.Gnome)
                return false;

            if (interactor.TryGetComponent<Petrifiable>(out var stone) && (stone.IsStone || stone.IsLockedInCage))
                return false;

            // A prank is for a team: the same gnome cannot do both parts, unless there is nobody else in the match.
            if (TravPoints != 0 && interactor.Object.StateAuthority == TravFirst && GnomeCount() > 1)
                return false;

            var carried = interactor.Carried;
            var needsItem = !string.IsNullOrEmpty(point.RequiredItemTag);
            if (needsItem ? carried == null || !carried.CompareTag(point.RequiredItemTag) : carried != null)
                return false;

            return (interactor.transform.position - point.Position).sqrMagnitude <= _travesuraDistance * _travesuraDistance;
        }

        /// <summary>Asks the master to use the point.</summary>
        public void RequestPoint(PlayerInteractor interactor, TravesuraPoint point) =>
            RPC_TravesuraPoint(interactor.Id, point.Travesura, point.Point);

        // The master finds the point again by its indexes and validates against its own copy.
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_TravesuraPoint(NetworkBehaviourId interactorId, int travesura, int pointIndex)
        {
            var point = FindPoint(travesura, pointIndex);
            if (point == null || !Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanUsePoint(who, point))
                return;

            var entry = TravesuraLayout.Current.Get(travesura);
            if (TravPoints == 0)
            {
                TravFirst = who.Object.StateAuthority;
                TravPatience = TickTimer.CreateFromSeconds(Runner, _travesuraPatience);
                TravWindow = entry.WindowSeconds > 0f ? TickTimer.CreateFromSeconds(Runner, entry.WindowSeconds) : default;
            }

            TravPoints = (byte)(TravPoints | (1 << pointIndex));
            BotEars.Report(point.Position, _pointNoiseRadius);
            var item = who.Carried;
            if (item != null && !string.IsNullOrEmpty(point.RequiredItemTag))
                item.PlaceAt(point.Position, item.transform.rotation);

            if (CountBits(TravPoints) >= entry.PointCount)
                FinishTravesura();
        }

        /// <summary>Whether a bomb counts for the list: same check on the camper's client and on the master.</summary>
        public bool CanBomb(PlayerCharacter camper, Vector3 bomb) =>
            ActiveTravesura >= 0 && TravesuraLayout.Current.Get(TravActive).Kind == TravesuraLayout.Kind.Bomb
            && camper != null && camper.Role == PlayerRole.Camper
            && (camper.transform.position - bomb).sqrMagnitude <= _bombDistance * _bombDistance;

        /// <summary>The camper's client reports walking into a bomb. Bombs throw the camper any time, but only count when asked.</summary>
        public void RequestBomb(PlayerCharacter camper, Vector3 bomb)
        {
            if (CanBomb(camper, bomb))
                RPC_TravesuraBomb(camper.Id, bomb);
        }

        /// <summary>Authority only: the bot camper walked into a bomb.</summary>
        public void BotHitBomb()
        {
            if (HasStateAuthority && ActiveTravesura >= 0 && TravesuraLayout.Current.Get(TravActive).Kind == TravesuraLayout.Kind.Bomb)
                FinishTravesura();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_TravesuraBomb(NetworkBehaviourId camperId, Vector3 bomb)
        {
            if (Runner.TryFindBehaviour(camperId, out PlayerCharacter camper) && CanBomb(camper, bomb))
                FinishTravesura();
        }

        private static TravesuraPoint FindPoint(int travesura, int pointIndex)
        {
            var layout = TravesuraLayout.Current;
            if (layout == null)
                return null;

            foreach (var point in layout.GetComponentsInChildren<TravesuraPoint>())
            {
                if (point.Travesura == travesura && point.Point == pointIndex)
                    return point;
            }

            return null;
        }

        private int GnomeCount()
        {
            var count = 0;
            foreach (var player in Runner.ActivePlayers)
            {
                if (player != CurrentCamper)
                    count++;
            }

            return count;
        }

        private void FinishTravesura()
        {
            var done = TravActive;
            TravDone = (byte)(TravDone | (1 << done));
            ClearActiveTravesura();
            if (Telemetry.Enabled) Telemetry.Begin("travesura").Int("index", done).Int("total", TravesurasDone).Send();
        }

        private void ClearActiveTravesura()
        {
            TravActive = NoTravesura;
            TravPatience = default;
            TravBoss = PlayerRef.None;
            TravBossTimer = default;
            ResetPoints();
        }

        private void ResetPoints()
        {
            TravPoints = 0;
            TravFirst = PlayerRef.None;
            TravWindow = default;
        }

        // Authority only: every match starts with an empty list.
        private void BeginTravesuras()
        {
            TravDone = 0;
            ClearActiveTravesura();
        }

        // Authority only, while playing. Returns true when the list is complete.
        private bool UpdateTravesuras()
        {
            var layout = TravesuraLayout.Current;
            if (layout == null)
                return false;

            if (CountBits(TravDone) >= layout.Needed)
                return true;

            if (TravActive == NoTravesura)
            {
                PickTravesura(layout);
                return false;
            }

            if (!BossIsFree())
                TravBoss = NextBoss();

            var fire = FireplaceManager.Instance;
            var kind = layout.Get(TravActive).Kind;
            if (kind == TravesuraLayout.Kind.FireOut && fire != null && fire.Extinguished > TravFireOuts)
                FinishTravesura();
            else if (kind == TravesuraLayout.Kind.FireOut && (fire == null || !fire.IsLit))
                ClearActiveTravesura();
            else if (TravPoints != 0 && TravWindow.Expired(Runner))
                ResetPoints();
            else if ((TravPoints == 0 || !TravWindow.IsRunning) && TravPatience.Expired(Runner))
                ClearActiveTravesura();

            return false;
        }

        // A random prank still to do. Dousing the fire waits until it is lit, or the list could stall on it.
        private void PickTravesura(TravesuraLayout layout)
        {
            var fire = FireplaceManager.Instance;
            var fireLit = fire != null && fire.IsLit;
            var candidates = 0;
            var chosen = NoTravesura;
            for (var i = 0; i < layout.Count; i++)
            {
                if ((TravDone & (1 << i)) != 0 || (layout.Get(i).Kind == TravesuraLayout.Kind.FireOut && !fireLit))
                    continue;

                candidates++;
                if (Random.Range(0, candidates) == 0)
                    chosen = i;
            }

            if (chosen == NoTravesura)
                return;

            TravActive = chosen;
            TravPatience = TickTimer.CreateFromSeconds(Runner, _travesuraPatience);
            TravFireOuts = fire != null ? fire.Extinguished : 0;
            TravBoss = NextBoss();
            TravBossTimer = TickTimer.CreateFromSeconds(Runner, _bossSeconds);
        }

        private bool BossIsFree()
        {
            var gnomes = Petrifiable.All;
            for (var i = 0; i < gnomes.Count; i++)
            {
                var gnome = gnomes[i];
                if (gnome != null && gnome.IsAvailable && gnome.Object.StateAuthority == TravBoss)
                    return !gnome.IsLockedInCage;
            }

            return false;
        }

        // The free gnome with the lowest id above the last Boss, wrapping, so the job rotates.
        private PlayerRef NextBoss()
        {
            var best = PlayerRef.None;
            var lowest = PlayerRef.None;
            var gnomes = Petrifiable.All;
            for (var i = 0; i < gnomes.Count; i++)
            {
                var gnome = gnomes[i];
                if (gnome == null || !gnome.IsAvailable || gnome.IsLockedInCage)
                    continue;

                var player = gnome.Object.StateAuthority;
                if (player == CurrentCamper)
                    continue;

                if (lowest == PlayerRef.None || player.PlayerId < lowest.PlayerId)
                    lowest = player;
                if (player.PlayerId > _lastBossId && (best == PlayerRef.None || player.PlayerId < best.PlayerId))
                    best = player;
            }

            var next = best != PlayerRef.None ? best : lowest;
            if (next != PlayerRef.None)
                _lastBossId = next.PlayerId;

            return next;
        }
    }
}
