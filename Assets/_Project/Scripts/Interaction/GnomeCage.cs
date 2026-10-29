using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Combat;
using BetterTogether.Diagnostics;
using BetterTogether.Gaze;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using UnityEngine;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// A cage next to the tent where the camper locks a gnome it carries. Master-client object: the state authority
    /// validates caging and freeing with the same predicates the clients use for the prompt (<see cref="CanCage"/>,
    /// <see cref="CanFree"/>) and is the only writer of <see cref="Caged"/> and <see cref="CageTimer"/>.
    /// The caged gnome (its own authority) reads the id and places itself inside, then outside when it is cleared.
    /// Holds one gnome. A teammate frees it by holding the interact key; otherwise the timer frees it.
    /// </summary>
    public class GnomeCage : NetworkBehaviour, IInteractable, IHoldInteractable, IMatchResettable
    {
        [Tooltip("Seconds a gnome stays locked before the cage opens by itself.")]
        [SerializeField, Min(1f)] private float _cageSeconds = 20f;
        [Tooltip("Seconds a free gnome holds the interact key to open the cage.")]
        [SerializeField, Min(0.1f)] private float _freeSeconds = 2f;
        [Tooltip("Distance (m) from the player's eyes to the cage that the authority accepts; a little more than the interaction reach to absorb latency.")]
        [SerializeField, Min(0f)] private float _maxDistance = 2.75f;
        [Tooltip("Where the caged gnome stands (its feet), inside the bars.")]
        [SerializeField] private Transform _inside;
        [Tooltip("Where the gnome appears when the cage opens, outside the bars and in the clear.")]
        [SerializeField] private Transform _exit;
        [Tooltip("Heard by everyone from the cage when a gnome is locked in.")]
        [SerializeField] private SoundCue _lockSfx;
        [Tooltip("Heard by everyone from the cage when the cage opens (freed or timed out).")]
        [SerializeField] private SoundCue _openSfx;

        private static readonly List<GnomeCage> AllCages = new();

        private Collider[] _colliders;
        private bool _isSpawned;
        private bool _lastHeld;

        /// <summary>Id of the gnome locked in (default when empty). Written only by the state authority.</summary>
        [Networked] private NetworkId Caged { get; set; }

        /// <summary>When the cage opens by itself. Written only by the state authority.</summary>
        [Networked] private TickTimer CageTimer { get; set; }

        public float CageSeconds => _cageSeconds;
        public Transform InsidePoint => _inside != null ? _inside : transform;
        public Transform ExitPoint => _exit != null ? _exit : transform;

        /// <summary>The gnome locked in, or null. Safe to read at any time.</summary>
        public GnomeCapture Occupant => _isSpawned && Caged.IsValid ? GnomeCapture.Find(Caged) : null;

        /// <summary>Seconds until the cage opens by itself (0 when empty). Safe to read at any time.</summary>
        public float SecondsLeft => _isSpawned && Caged.IsValid ? CageTimer.RemainingTime(Runner) ?? 0f : 0f;

        /// <summary>The cage that holds the gnome with this id, or null. Safe to call at any time.</summary>
        public static GnomeCage FindHolding(NetworkId gnomeId)
        {
            if (!gnomeId.IsValid)
                return null;

            for (var i = 0; i < AllCages.Count; i++)
            {
                var cage = AllCages[i];
                if (cage._isSpawned && cage.Caged == gnomeId)
                    return cage;
            }

            return null;
        }

        public static bool IsCaged(NetworkId gnomeId) => FindHolding(gnomeId) != null;

        private void Awake()
        {
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        public override void Spawned()
        {
            _isSpawned = true;
            AllCages.Add(this);
            _lastHeld = Caged.IsValid; // A late joiner doesn't hear a lock that already happened.
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            AllCages.Remove(this);
        }

        // ---- Prompt (mirror of the authority's rules) ----

        public string GetPrompt(PlayerInteractor interactor)
        {
            if (interactor.Role == PlayerRole.Camper)
            {
                var carried = interactor.TryGetComponent<GnomeCarrier>(out var carrier) ? carrier.Carried : null;
                return carried != null ? $"Encerrar a {carried.DisplayName}" : "Encerrar";
            }

            var occupant = Occupant;
            return occupant != null ? $"Liberar a {occupant.DisplayName}" : "Liberar";
        }

        public bool CanInteract(PlayerInteractor interactor) =>
            interactor.Role == PlayerRole.Camper ? CanCage(interactor) : CanFree(interactor);

        // Only the gnomes hold; the camper taps.
        public float GetHoldTime(PlayerInteractor interactor) => interactor.Role == PlayerRole.Gnome ? _freeSeconds : 0f;

        public void Interact(PlayerInteractor interactor)
        {
            if (interactor.Role == PlayerRole.Camper)
                RPC_RequestCage(interactor.Id);
        }

        public void OnHoldComplete(PlayerInteractor interactor)
        {
            if (interactor.Role == PlayerRole.Gnome)
                RPC_RequestFree(interactor.Id);
        }

        /// <summary>The camper carries a stone gnome, the cage is empty, and the camper is in reach.</summary>
        public bool CanCage(PlayerInteractor who)
        {
            if (!_isSpawned || Caged.IsValid || who == null || who.Role != PlayerRole.Camper || !MatchManager.AllowsInteraction)
                return false;

            if (!who.TryGetComponent<GnomeCarrier>(out var carrier))
                return false;

            var carried = carrier.Carried;
            return carried != null && carried.IsStoneNow && InReach(who);
        }

        /// <summary>A free, able gnome (not the caged one) in reach of an occupied cage.</summary>
        public bool CanFree(PlayerInteractor who)
        {
            if (!_isSpawned || !Caged.IsValid || who == null || who.Role != PlayerRole.Gnome || !MatchManager.AllowsInteraction)
                return false;

            if (who.Object.Id == Caged)
                return false;

            if (who.TryGetComponent<Kickable>(out var kickable) && kickable.IsStunned)
                return false;

            if (who.TryGetComponent<Petrifiable>(out var petrifiable) && petrifiable.IsHeldStone)
                return false;

            if (who.TryGetComponent<GnomeCapture>(out var capture) && capture.IsCaptured)
                return false;

            return InReach(who);
        }

        private bool InReach(PlayerInteractor who)
        {
            var eye = who.EyePosition;
            var limit = _maxDistance * _maxDistance;
            foreach (var col in _colliders)
            {
                if (col != null && col.enabled && (col.ClosestPoint(eye) - eye).sqrMagnitude <= limit)
                    return true;
            }

            return false;
        }

        // ---- Authority ----

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestCage(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanCage(who))
                return;

            var carried = who.GetComponent<GnomeCarrier>().Carried;
            Caged = carried.Object.Id;
            CageTimer = TickTimer.CreateFromSeconds(Runner, _cageSeconds);
            if (Telemetry.Enabled)
                Telemetry.Begin("gnome_caged", who).Int("gnome", carried.Object.StateAuthority.PlayerId).Send();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestFree(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanFree(who))
                return;

            var gnome = Caged;
            Open();
            if (MatchManager.Instance != null)
                MatchManager.Instance.Award(who.Object.StateAuthority, MatchManager.ScoreReason.FreeTeammate);
            if (Telemetry.Enabled)
                Telemetry.Begin("gnome_freed", who).Str("by", "teammate").Int("gnome", GnomeOwnerId(gnome)).Send();
        }

        // The cage opens when the timer runs out, when the gnome is gone, and between matches (all decided here, by the authority).
        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !Caged.IsValid)
                return;

            var gnome = Caged;
            var occupant = GnomeCapture.Find(gnome);
            var match = MatchManager.Instance;
            string how = null;
            if (occupant == null)
                how = "left";
            else if (CageTimer.ExpiredOrNotRunning(Runner))
                how = "timeout";
            else if (match != null && match.Phase != MatchPhase.Playing)
                how = "reset";

            if (how == null)
                return;

            var owner = occupant != null ? occupant.Object.StateAuthority.PlayerId : -1;
            Open();
            if (Telemetry.Enabled)
                Telemetry.Begin("gnome_freed").Str("by", how).Int("gnome", owner).Send();
        }

        private static int GnomeOwnerId(NetworkId id)
        {
            var gnome = GnomeCapture.Find(id);
            return gnome != null ? gnome.Object.StateAuthority.PlayerId : -1;
        }

        private void Open()
        {
            Caged = default;
            CageTimer = default;
        }

        /// <summary>Authority only: the cage is empty at the start of every match.</summary>
        public void ResetForMatch()
        {
            if (_isSpawned && HasStateAuthority)
                Open();
        }

        // ---- Presentation ----

        // Sounds follow the replicated id: whoever is near hears the lock and the opening, a late joiner does not.
        public override void Render()
        {
            if (!_isSpawned)
                return;

            var held = Caged.IsValid;
            if (held && !_lastHeld)
                _lockSfx.PlayAt(transform.position);
            else if (!held && _lastHeld)
                _openSfx.PlayAt(transform.position);
            _lastHeld = held;
        }
    }
}
