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
    /// The list of pranks: one active at a time, so the gnomes go to the same place. Finishing the list wins the match for them.
    /// Only the master writes this state. A level without a <see cref="TravesuraLayout"/> never uses it.
    /// </summary>
    public partial class MatchManager
    {
        private const int NoTravesura = -1;

        [Header("Travesuras")]
        [Tooltip("Farthest a gnome can be from a point (m), a little more than the interaction reach to absorb latency.")]
        [SerializeField, Min(0f)] private float _travesuraDistance = 3.5f;
        [Tooltip("Farthest the camper can be from the bomb when the master accepts the explosion (m).")]
        [SerializeField, Min(0f)] private float _bombDistance = 3f;
        [Tooltip("Seconds a prank stays active with no part done before another one replaces it, so an unreachable point never freezes the list.")]
        [SerializeField, Min(1f)] private float _travesuraPatience = 75f;

        [Networked] private byte TravDone { get; set; }
        [Networked] private int TravActive { get; set; }
        [Networked] private byte TravPoints { get; set; }
        [Networked] private PlayerRef TravFirst { get; set; }
        [Networked] private TickTimer TravWindow { get; set; }
        [Networked] private int TravFireOuts { get; set; }
        [Networked] private NetworkBool TravBombArmed { get; set; }
        [Networked] private TickTimer TravPatience { get; set; }

        /// <summary>True while a match with a list of pranks is running.</summary>
        public bool TravesurasRunning => _isSpawned && CurrentPhase == MatchPhase.Playing && TravesuraLayout.Current != null;

        /// <summary>One bit per finished prank.</summary>
        public byte TravesurasDoneMask => _isSpawned ? TravDone : (byte)0;

        /// <summary>The prank in progress, or -1 between pranks.</summary>
        public int ActiveTravesura => TravesurasRunning ? TravActive : NoTravesura;

        /// <summary>Finished pranks.</summary>
        public int TravesurasDone => _isSpawned ? CountBits(TravDone) : 0;

        /// <summary>True while the exploding gnome waits for the camper.</summary>
        public bool BombIsArmed => TravesurasRunning && TravBombArmed;

        /// <summary>True when this point of the active prank still has to be used.</summary>
        public bool IsPointNeeded(int travesura, int point) =>
            ActiveTravesura == travesura && (TravPoints & (1 << point)) == 0 && !TravBombArmed;

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

            var carried = interactor.CarriedOrPredicted;
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

            var layout = TravesuraLayout.Current;
            var entry = layout.Get(travesura);
            if (TravPoints == 0)
            {
                TravFirst = who.Object.StateAuthority;
                TravWindow = entry.WindowSeconds > 0f ? TickTimer.CreateFromSeconds(Runner, entry.WindowSeconds) : default;
            }

            TravPoints = (byte)(TravPoints | (1 << pointIndex));
            var item = who.Carried;
            if (item != null && !string.IsNullOrEmpty(point.RequiredItemTag))
                item.PlaceAt(point.Position, item.transform.rotation);

            if (CountBits(TravPoints) < entry.PointCount)
                return;

            if (entry.Kind == TravesuraLayout.Kind.Bomb)
                TravBombArmed = true;
            else
                FinishTravesura();
        }

        /// <summary>Whether the camper can set off the bomb: same check on the master and on the camper's client.</summary>
        public bool CanBomb(PlayerCharacter camper, Vector3 bomb) =>
            BombIsArmed && camper != null && camper.Role == PlayerRole.Camper
            && (camper.transform.position - bomb).sqrMagnitude <= _bombDistance * _bombDistance;

        /// <summary>The camper's client reports walking into the bomb.</summary>
        public void RequestBomb(PlayerCharacter camper, Vector3 bomb) => RPC_TravesuraBomb(camper.Id, bomb);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_TravesuraBomb(NetworkBehaviourId camperId, Vector3 bomb)
        {
            if (!Runner.TryFindBehaviour(camperId, out PlayerCharacter camper) || !CanBomb(camper, bomb))
                return;

            TravBombArmed = false;
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
            TravPoints = 0;
            TravFirst = PlayerRef.None;
            TravWindow = default;
            TravPatience = default;
        }

        // Authority only: every match starts with an empty list.
        private void BeginTravesuras()
        {
            TravDone = 0;
            TravBombArmed = false;
            ClearActiveTravesura();
        }

        // Authority only, while playing. Returns true when the list is complete.
        private bool UpdateTravesuras()
        {
            var layout = TravesuraLayout.Current;
            if (layout == null)
                return false;

            if (CountBits(TravDone) >= layout.Count)
                return true;

            if (TravActive == NoTravesura)
            {
                PickTravesura(layout);
                return false;
            }

            var fire = FireplaceManager.Instance;
            var kind = layout.Get(TravActive).Kind;
            if (kind == TravesuraLayout.Kind.FireOut && fire != null && fire.Extinguished > TravFireOuts)
                FinishTravesura();
            else if (kind == TravesuraLayout.Kind.FireOut && (fire == null || !fire.IsLit))
                ClearActiveTravesura();
            else if (TravBombArmed && TravWindow.Expired(Runner))
                ExpireBomb();
            else if (TravPoints != 0 && !TravBombArmed && TravWindow.Expired(Runner))
                ResetPoints();
            else if (TravPoints == 0 && TravPatience.Expired(Runner))
                ClearActiveTravesura();

            return false;
        }

        // The camper never came: the bomb still counts, so a prank never depends only on the rival.
        private void ExpireBomb()
        {
            TravBombArmed = false;
            FinishTravesura();
        }

        private void ResetPoints()
        {
            TravPoints = 0;
            TravFirst = PlayerRef.None;
            TravWindow = default;
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
        }
    }
}
