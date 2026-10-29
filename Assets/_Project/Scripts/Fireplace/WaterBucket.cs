using BetterTogether.Audio;
using BetterTogether.Combat;
using BetterTogether.Diagnostics;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using PrimeTween;
using UnityEngine;

namespace BetterTogether.Fireplace
{
    /// <summary>
    /// The bucket gnomes fill at a <see cref="WaterSource"/> and pour on the campfire. Sits next to a <see cref="Carryable"/>.
    /// Master-client object: only the state authority writes <see cref="IsFull"/>, either on a validated fill request
    /// or when the fireplace (also master) finishes or cancels a pour, or when the bucket is kicked out of someone's hands.
    /// </summary>
    [RequireComponent(typeof(Carryable))]
    [DefaultExecutionOrder(100)] // Its LateUpdate tilts the water and the model after Carryable has posed the bucket.
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

        [Header("Feel (built by GnoMore/Construir feel de fogata y cubeta; every field is optional)")]
        [Tooltip("The bucket model; it is tipped towards the fire while pouring. Pivots around the bucket root.")]
        [SerializeField] private Transform _model;
        [Tooltip("Splash at the rim: a burst when the bucket is filled or spilled.")]
        [SerializeField] private ParticleSystem _splash;
        [Tooltip("Drops that fall from the rim while a full bucket is carried.")]
        [SerializeField] private ParticleSystem _drips;
        [Tooltip("Jet of water that leaves the rim towards the fire during the pour (world-space particles).")]
        [SerializeField] private ParticleSystem _pourStream;
        [Tooltip("Seconds the water disc takes to rise when the bucket is filled.")]
        [SerializeField, Min(0.01f)] private float _waterRiseTime = 0.35f;
        [Tooltip("Metres below its resting height the water disc starts when filling.")]
        [SerializeField, Min(0f)] private float _waterRiseDepth = 0.12f;
        [Tooltip("Particles in the splash when the bucket is filled.")]
        [SerializeField, Min(0)] private int _fillSplashCount = 14;
        [Tooltip("Particles in the splash when the bucket is spilled on the ground.")]
        [SerializeField, Min(0)] private int _spillSplashCount = 28;
        [Tooltip("Largest tilt (degrees) the water surface leans with the carrier's acceleration.")]
        [SerializeField, Range(0f, 45f)] private float _sloshMaxAngle = 14f;
        [Tooltip("Lean (degrees) of the water per m/s2 of horizontal acceleration of the bucket.")]
        [SerializeField, Min(0f)] private float _sloshGain = 0.8f;
        [Tooltip("Spring stiffness of the water lean (higher = quicker).")]
        [SerializeField, Min(0f)] private float _sloshStiffness = 90f;
        [Tooltip("Spring damping of the water lean (lower = more wobble).")]
        [SerializeField, Min(0f)] private float _sloshDamping = 8f;
        [Tooltip("Seconds between drops while a full bucket is carried.")]
        [SerializeField, Min(0.05f)] private float _dripInterval = 0.4f;
        [Tooltip("Degrees the bucket tips towards the fire at the end of the tilt while pouring.")]
        [SerializeField, Range(0f, 120f)] private float _pourTiltAngle = 75f;
        [Tooltip("Degrees per second the bucket tips towards the fire, and back once the pour is over.")]
        [SerializeField, Min(0f)] private float _pourTiltSpeed = 360f;
        [Tooltip("Metres from the bucket centre, towards the fire, where the jet leaves.")]
        [SerializeField, Min(0f)] private float _rimOffset = 0.12f;
        [Tooltip("Metres above the bucket root where the jet leaves.")]
        [SerializeField] private float _rimHeight = 0.28f;
        [Tooltip("Particles per second in the jet while the bucket is tipped.")]
        [SerializeField, Min(0f)] private float _pourStreamRate = 90f;
        [Tooltip("Fraction of the pour that had run when the water left for it to count as poured (otherwise it was spilled).")]
        [SerializeField, Range(0f, 1f)] private float _pouredThreshold = 0.7f;

        private Carryable _carryable;
        private bool _isSpawned;
        // Presentation only (never networked).
        private Tween _riseTween;
        private float _waterRise = 1f;
        private Vector3 _waterRestPosition;
        private Quaternion _waterRestRotation = Quaternion.identity;
        private Quaternion _modelRestRotation = Quaternion.identity;
        private float _pourAngle;
        private Vector3 _pourDirection = Vector3.forward;
        private float _pourSeen;
        private Vector3 _lastPosition;
        private Vector3 _lastVelocity;
        private bool _hasLast;
        private Vector3 _lean;
        private Vector3 _leanVelocity;
        private float _nextDrip;

        [Networked, OnChangedRender(nameof(OnFullChanged))]
        private NetworkBool Full { get; set; }

        /// <summary>True while the bucket holds water. False when not spawned.</summary>
        public bool IsFull => _isSpawned && Full;

        public Carryable Carryable => _carryable;

        private void Awake()
        {
            _carryable = GetComponent<Carryable>();
            if (_water != null)
            {
                _waterRestPosition = _water.transform.localPosition;
                _waterRestRotation = _water.transform.localRotation;
            }

            if (_model != null)
                _modelRestRotation = _model.localRotation;
        }

        public override void Spawned()
        {
            _isSpawned = true;
            _hasLast = false;
            _pourAngle = 0f;
            _pourSeen = 0f;
            _lean = Vector3.zero;
            _leanVelocity = Vector3.zero;
            ApplyWater(false); // Late joiners see the water that is already there, without hearing it or watching it rise.
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            _riseTween.Stop();
        }

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
            if (Telemetry.Enabled) Telemetry.Begin("bucket_fill", who).Send();
            if (MatchManager.Instance != null) MatchManager.Instance.Award(who.Object.StateAuthority, MatchManager.ScoreReason.FillBucket);
        }

        /// <summary>
        /// Empties the bucket onto the ground (kicked, or the pour was cancelled). On the authority it applies at once;
        /// elsewhere pass the interactor that holds the bucket, since the authority only honours a spill asked by a current holder.
        /// </summary>
        public void RequestSpill(PlayerInteractor holder = null)
        {
            if (!_isSpawned)
                return;

            if (HasStateAuthority)
                Full = false;
            else if (holder != null)
                RPC_Spill(holder.Id);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Spill(NetworkBehaviourId holderId, RpcInfo info = default)
        {
            if (!Full || !Runner.TryFindBehaviour(holderId, out PlayerInteractor holder) || Carryable.FindHeldBy(holder) != _carryable)
                return;

            // Only the holder's own client may spill its bucket. A call made on this very peer carries no remote source.
            var owner = holder.Object.StateAuthority;
            if (info.Source != owner && !(info.Source == PlayerRef.None && Runner.LocalPlayer == owner))
                return;

            Full = false;
        }

        // Fires on every peer except at spawn, so the sounds are heard once and late joiners hear nothing.
        // Outside the Playing phase (the match reset empties the bucket) it stays quiet.
        private void OnFullChanged()
        {
            ApplyWater(true);
            if (!MatchManager.AllowsInteraction)
                return;

            bool full = Full;
            (full ? _fillSfx : _emptySfx).PlayAt(transform.position);
            if (full)
                EmitSplash(_water != null ? transform.TransformPoint(_waterRestPosition) : transform.position, _fillSplashCount);
            else if (_pourSeen < _pouredThreshold)
                EmitSplash(transform.position, _spillSplashCount); // Spilled (kick, cancelled pour): splashes where it stands.
        }

        private void ApplyWater(bool animate)
        {
            bool full = Full;
            if (_water == null || _water.activeSelf == full)
                return;

            _water.SetActive(full);
            _riseTween.Stop();
            if (full && animate)
            {
                _waterRise = 0f;
                _riseTween = Tween.Custom(this, 0f, 1f, _waterRiseTime, static (bucket, value) => bucket._waterRise = value, Ease.OutCubic);
            }
            else
            {
                _waterRise = 1f;
            }
        }

        private void EmitSplash(Vector3 position, int count)
        {
            if (_splash == null || count <= 0)
                return;

            _splash.transform.position = position;
            _splash.Emit(count);
        }

        // Runs after Carryable has posed the bucket: tips the model and the water, and feeds the drops and the jet.
        private void LateUpdate()
        {
            if (!_isSpawned)
                return;

            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f)
                return;

            bool full = Full;
            var pouring = false;
            var fraction = 0f;
            var fire = FireplaceManager.Instance;
            if (full && fire != null && fire.TryGetPour(this, out fraction))
            {
                var toFire = fire.transform.position - transform.position;
                toFire.y = 0f;
                if (toFire.sqrMagnitude > 0.0001f)
                {
                    _pourDirection = toFire.normalized;
                    pouring = true;
                }
            }

            _pourSeen = pouring ? fraction : 0f;
            _pourAngle = Mathf.MoveTowards(_pourAngle, pouring ? _pourTiltAngle : 0f, _pourTiltSpeed * dt);

            UpdateSlosh(dt, full);
            ApplyTilt(full);
            UpdateJet(pouring);
            UpdateDrips(full);
        }

        // The water leans against the bucket's horizontal acceleration, on a spring so it wobbles. The velocity is read from
        // the displayed position, so the owner and the proxies see the same thing.
        private void UpdateSlosh(float dt, bool full)
        {
            var position = transform.position;
            var velocity = _hasLast ? (position - _lastPosition) / dt : Vector3.zero;
            var acceleration = _hasLast ? (velocity - _lastVelocity) / dt : Vector3.zero;
            _lastPosition = position;
            _lastVelocity = velocity;
            _hasLast = true;

            acceleration.y = 0f;
            acceleration = Vector3.ClampMagnitude(acceleration, 40f); // A teleport or blend must not slosh the water over.
            var maxLean = Mathf.Tan(_sloshMaxAngle * Mathf.Deg2Rad);
            var target = full ? Vector3.ClampMagnitude(-acceleration * (_sloshGain * Mathf.Deg2Rad), maxLean) : Vector3.zero;
            _leanVelocity += ((target - _lean) * _sloshStiffness - _leanVelocity * _sloshDamping) * dt;
            _lean = Vector3.ClampMagnitude(_lean + _leanVelocity * dt, maxLean);
        }

        private void ApplyTilt(bool full)
        {
            var rootRotation = transform.rotation;
            var inverseRoot = Quaternion.Inverse(rootRotation);
            var pour = _pourAngle > 0.01f
                ? Quaternion.AngleAxis(_pourAngle, Vector3.Cross(Vector3.up, _pourDirection))
                : Quaternion.identity;

            if (_model != null)
                _model.localRotation = inverseRoot * pour * rootRotation * _modelRestRotation;

            if (_water == null || !full)
                return;

            var surface = Quaternion.FromToRotation(Vector3.up, (Vector3.up + _lean).normalized);
            var local = inverseRoot * pour * surface * rootRotation;
            var restPosition = _waterRestPosition + Vector3.down * (_waterRiseDepth * (1f - _waterRise));
            _water.transform.SetLocalPositionAndRotation(local * restPosition, local * _waterRestRotation);
        }

        private void UpdateJet(bool pouring)
        {
            if (_pourStream == null)
                return;

            var emission = _pourStream.emission;
            var tipped = pouring && _pourAngle >= _pourTiltAngle * 0.5f;
            emission.rateOverTime = tipped ? _pourStreamRate : 0f;
            if (!tipped)
                return;

            var rim = transform.position + _pourDirection * _rimOffset + Vector3.up * _rimHeight;
            _pourStream.transform.SetPositionAndRotation(rim, Quaternion.LookRotation(_pourDirection + Vector3.down * 0.35f));
        }

        // A full bucket in somebody's hands drips: one drop every few tenths of a second (local, nothing replicated).
        private void UpdateDrips(bool full)
        {
            if (_drips == null || !full || !_carryable.IsHeld || Time.time < _nextDrip)
                return;

            _nextDrip = Time.time + _dripInterval;
            _drips.Emit(1);
        }
    }
}
