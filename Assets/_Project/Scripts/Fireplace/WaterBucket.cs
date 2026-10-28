using BetterTogether.Audio;
using BetterTogether.Combat;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using UnityEngine;

namespace BetterTogether.Fireplace
{
    /// <summary>
    /// The bucket gnomes fill at a <see cref="WaterSource"/> and pour on the campfire. Sits next to a <see cref="Carryable"/>.
    /// Master-client object: only the state authority writes <see cref="IsFull"/>, either on a validated fill request
    /// or when the fireplace (also master) finishes or cancels a pour, or when the bucket is kicked out of someone's hands.
    /// </summary>
    [RequireComponent(typeof(Carryable))]
    public class WaterBucket : NetworkBehaviour, ICarriedUse, IMatchResettable
    {
        [Tooltip("Child shown only while the bucket is full (e.g. a disc with the water material).")]
        [SerializeField] private GameObject _water;
        [Tooltip("Extra metres of leeway when the authority checks a fill, since the holder it sees lags behind the player who asked.")]
        [SerializeField, Min(0f)] private float _fillTolerance = 0.5f;
        [Tooltip("Heard by everyone, from the bucket, when it is filled.")]
        [SerializeField] private SoundCue _fillSfx;
        [Tooltip("Heard by everyone, from the bucket, when the water leaves it (poured or spilled).")]
        [SerializeField] private SoundCue _emptySfx;

        private Carryable _carryable;
        private bool _isSpawned;

        [Networked, OnChangedRender(nameof(OnFullChanged))]
        private NetworkBool Full { get; set; }

        /// <summary>True while the bucket holds water. False when not spawned.</summary>
        public bool IsFull => _isSpawned && Full;

        public Carryable Carryable => _carryable;

        private void Awake()
        {
            _carryable = GetComponent<Carryable>();
        }

        public override void Spawned()
        {
            _isSpawned = true;
            ApplyWater(); // Late joiners see the water that is already there, without hearing it.
        }

        public override void Despawned(NetworkRunner runner, bool hasState) => _isSpawned = false;

        /// <summary>Authority only: the bucket is empty again at the start of a match.</summary>
        public void ResetForMatch() => Full = false;

        /// <summary>
        /// Mirrors the authority's rules using the replicated state: an empty bucket held by a free gnome that
        /// stands near the water.
        /// </summary>
        public bool CanFill(PlayerInteractor who) => CanFill(who, 0f);

        private bool CanFill(PlayerInteractor who, float tolerance)
        {
            if (!_isSpawned || !MatchManager.AllowsInteraction || Full || who.Role != PlayerRole.Gnome)
                return false;

            if (_carryable.HolderCount != 1 || Carryable.FindHeldBy(who) != _carryable)
                return false;

            if ((who.TryGetComponent<Petrifiable>(out var petrifiable) && petrifiable.IsStone)
                || (who.TryGetComponent<Kickable>(out var kickable) && kickable.IsStunned))
                return false;

            return WaterSource.IsNearAny(who.transform.position, tolerance);
        }

        /// <summary>Fills the bucket when the holder can; called by <see cref="PlayerInteractor"/> when interact is pressed.</summary>
        public bool TryUse(PlayerInteractor who)
        {
            if (!CanFill(who))
                return false;

            RPC_RequestFill(who.Id);
            return true;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestFill(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanFill(who, _fillTolerance))
                return;

            Full = true;
        }

        /// <summary>
        /// Empties the bucket onto the ground (kicked, or the pour was cancelled). Any peer may call it: the request
        /// is only honoured by the authority and only changes a full bucket, so the worst a bad actor can do is waste water.
        /// </summary>
        public void RequestSpill()
        {
            if (!_isSpawned)
                return;

            if (HasStateAuthority)
                Full = false;
            else
                RPC_Spill();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Spill()
        {
            if (Full)
                Full = false;
        }

        // Fires on every peer except at spawn, so the sounds are heard once and late joiners hear nothing.
        private void OnFullChanged()
        {
            ApplyWater();
            (Full ? _fillSfx : _emptySfx).PlayAt(transform.position);
        }

        private void ApplyWater()
        {
            if (_water != null && _water.activeSelf != Full)
                _water.SetActive(Full);
        }
    }
}
