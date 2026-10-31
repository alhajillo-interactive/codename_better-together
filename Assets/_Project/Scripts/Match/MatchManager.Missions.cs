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
    /// Side missions and per-match variation. One seed decides places, photos and butterflies on every peer; two masks say what is
    /// done. The rewards are timers read only for presentation. Nothing here wins a match.
    /// </summary>
    public partial class MatchManager
    {
        [Header("Missions")]
        [Tooltip("Points for the gnome that catches a butterfly.")]
        [SerializeField] private int _pointsButterfly = 1;
        [Tooltip("Seconds the gnomes show on the camper's plan after all photos are taken.")]
        [SerializeField, Min(0f)] private float _revealSeconds = 6f;
        [Tooltip("Seconds the swarm covers the camper's camera after the gnomes catch their butterflies.")]
        [SerializeField, Min(0f)] private float _swarmSeconds = 6f;
        [Tooltip("Metres added to every distance the authority checks, to absorb latency.")]
        [SerializeField, Min(0f)] private float _missionDistanceMargin = 3f;
        [Tooltip("Farthest a gnome can be from a butterfly to catch it (m).")]
        [SerializeField, Min(0f)] private float _catchDistance = 3.5f;

        private const int AllPhotos = (1 << MissionLayout.PhotoTasks) - 1;

        [Networked] private int MissionSeed { get; set; }
        [Networked] private byte PhotosDone { get; set; }
        [Networked] private int ButterfliesCaught { get; set; }
        [Networked] private TickTimer RevealTimer { get; set; }
        [Networked] private TickTimer SwarmTimer { get; set; }

        private int _appliedSeed;

        /// <summary>This match's seed; 0 in a level without missions.</summary>
        public int Seed => _isSpawned ? MissionSeed : 0;

        /// <summary>True while a match with missions is running.</summary>
        public bool MissionsRunning => _isSpawned && CurrentPhase == MatchPhase.Playing && MissionSeed != 0 && _appliedSeed == MissionSeed;

        /// <summary>True when the photo of this slot is done.</summary>
        public bool IsPhotoDone(int slot) => _isSpawned && (PhotosDone & (1 << slot)) != 0;

        /// <summary>Butterflies caught this match.</summary>
        public int ButterflyTotal => _isSpawned ? CountBits(ButterfliesCaught) : 0;

        /// <summary>True while the butterfly at this spot is out and free.</summary>
        public bool IsButterflyAvailable(int index)
        {
            var layout = MissionLayout.Current;
            return MissionsRunning && layout != null && layout.IsChosen(index) && (ButterfliesCaught & (1 << index)) == 0;
        }

        /// <summary>Seconds left of the camper's reward, 0 when it is not running. Presentation only.</summary>
        public float RevealRemaining => _isSpawned ? RevealTimer.RemainingTime(Runner) ?? 0f : 0f;

        /// <summary>Seconds left of the gnomes' reward, 0 when it is not running. Presentation only.</summary>
        public float SwarmRemaining => _isSpawned ? SwarmTimer.RemainingTime(Runner) ?? 0f : 0f;

        /// <summary>Whether this player may catch this butterfly: the prompt and the authority use this same check.</summary>
        public bool CanCatch(PlayerInteractor interactor, int index)
        {
            if (interactor == null || !IsButterflyAvailable(index) || interactor.CarriedOrPredicted != null)
                return false;

            if (!interactor.TryGetComponent<PlayerCharacter>(out var character) || character.Role != PlayerRole.Gnome)
                return false;

            if (interactor.TryGetComponent<Petrifiable>(out var stone) && (stone.IsStone || stone.IsLockedInCage))
                return false;

            var spot = MissionLayout.Current.Butterfly(index).Position;
            return (interactor.transform.position - spot).sqrMagnitude <= _catchDistance * _catchDistance;
        }

        /// <summary>Asks the master to catch the butterfly.</summary>
        public void RequestCatch(PlayerInteractor interactor, int index) => RPC_Catch(interactor.Id, index);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Catch(NetworkBehaviourId interactorId, int index)
        {
            var layout = MissionLayout.Current;
            if (layout == null || index < 0 || index >= layout.ButterflyCount)
                return;

            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanCatch(who, index))
                return;

            var before = CountBits(ButterfliesCaught);
            ButterfliesCaught |= 1 << index;
            AwardInternal(who.Object.StateAuthority, _pointsButterfly, "butterfly");
            if (Telemetry.Enabled) Telemetry.Begin("butterfly").Int("spot", index).Int("total", before + 1).Send();

            // Only the catch that reaches the goal starts the swarm; the spare butterflies give points and nothing more.
            if (before + 1 == layout.ButterfliesNeeded)
                SwarmTimer = TickTimer.CreateFromSeconds(Runner, _swarmSeconds);
        }

        /// <summary>
        /// Called by the camper's owner after a shot spent a plate: reports every photo of this match the shot satisfies. The owner
        /// checks the view, which only it knows exactly; the master checks the distance with <see cref="IsPhotoInReach"/>.
        /// </summary>
        public void ReportPhoto(CamperGaze gaze)
        {
            var layout = MissionLayout.Current;
            if (layout == null || !MissionsRunning)
                return;

            for (var slot = 0; slot < MissionLayout.PhotoTasks; slot++)
            {
                if (!IsPhotoDone(slot) && IsPhotoInReach(gaze, slot, 0f) && ShowsSubject(gaze, layout, slot))
                    RPC_Photo(gaze.Id, slot);
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Photo(NetworkBehaviourId gazeId, int slot)
        {
            if (slot < 0 || slot >= MissionLayout.PhotoTasks || !MissionsRunning || IsPhotoDone(slot) || MissionLayout.Current == null)
                return;

            if (!Runner.TryFindBehaviour(gazeId, out CamperGaze gaze) || gaze.Object.StateAuthority != CurrentCamper
                || !IsPhotoInReach(gaze, slot, _missionDistanceMargin))
                return;

            PhotosDone = (byte)(PhotosDone | (1 << slot));
            if (Telemetry.Enabled) Telemetry.Begin("mission_photo").Int("slot", slot).Send();
            if (PhotosDone == AllPhotos)
                RevealTimer = TickTimer.CreateFromSeconds(Runner, _revealSeconds);
        }

        // The check both sides share: the subject of the slot is close enough. The gnome slot needs a gnome in the asked state.
        private bool IsPhotoInReach(CamperGaze gaze, int slot, float margin)
        {
            var layout = MissionLayout.Current;
            var position = gaze.transform.position;
            var subject = layout.Subject(slot);
            if (subject != null)
                return WithinFlat(position, subject.Point, subject.MaxDistance + margin);

            return FindGnome(layout, position, layout.GnomePhotoDistance + margin, null) != null;
        }

        private static bool ShowsSubject(CamperGaze gaze, MissionLayout layout, int slot)
        {
            var subject = layout.Subject(slot);
            if (subject != null)
                return gaze.CanPhotograph(subject.Point, subject.transform, subject.MaxDistance);

            return FindGnome(layout, gaze.transform.position, layout.GnomePhotoDistance, gaze) != null;
        }

        // The first gnome in the asked state within reach; with a gaze, it must also be in the picture.
        private static Petrifiable FindGnome(MissionLayout layout, Vector3 position, float distance, CamperGaze gaze)
        {
            var gnomes = Petrifiable.All;
            for (var i = 0; i < gnomes.Count; i++)
            {
                var gnome = gnomes[i];
                if (gnome == null || !WithinFlat(position, gnome.transform.position, distance))
                    continue;

                var matches = layout.GnomePhotoIsStone
                    ? gnome.IsStone
                    : gnome.TryGetComponent<BalloonThrower>(out var thrower) && thrower.HasBalloon;
                if (!matches)
                    continue;

                if (gaze == null || gaze.CanPhotograph(gnome.transform.position + Vector3.up * 0.4f, gnome.transform, distance))
                    return gnome;
            }

            return null;
        }

        private static bool WithinFlat(Vector3 a, Vector3 b, float distance)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz <= distance * distance;
        }

        private static int CountBits(int mask)
        {
            var count = 0;
            for (; mask != 0; mask &= mask - 1)
                count++;

            return count;
        }

        // Authority only, right after the world reset: a new seed, nothing done, and the items on their spots of this match.
        private void BeginMissions()
        {
            PhotosDone = 0;
            ButterfliesCaught = 0;
            RevealTimer = default;
            SwarmTimer = default;

            var layout = MissionLayout.Current;
            if (layout == null)
                return;

            MissionSeed = Random.Range(1, int.MaxValue);
            ApplySeedLocally();
            layout.PlaceItems(MissionSeed);
        }

        // Every peer, once per seed: late joiners and a new master end up with the same start poses without moving anything.
        private void ApplySeedLocally()
        {
            var seed = MissionSeed;
            if (seed == _appliedSeed)
                return;

            var layout = MissionLayout.Current;
            if (layout == null)
                return;

            _appliedSeed = seed;
            if (seed != 0)
                layout.ApplySeed(seed);
        }
    }
}
