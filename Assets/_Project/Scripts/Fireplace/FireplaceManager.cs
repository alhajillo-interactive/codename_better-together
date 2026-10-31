using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Combat;
using BetterTogether.Diagnostics;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Networking;
using BetterTogether.Player;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace BetterTogether.Fireplace
{
    /// <summary>
    /// The campfire. Logs rest in slots arranged like a small teepee; once every slot is taken, a camper holding the matches can light it.
    /// A log is placed by carrying it into the area and pressing interact, or it snaps into a free slot by itself when it lies still inside.
    /// Only the camper places logs, and a placed log can't be taken out again. Gnomes put a lit fire out by pouring a
    /// full <see cref="WaterBucket"/> on it (a short pour that anything can interrupt); a log then jumps out, and the camper
    /// lights it again with the matches. The milestone (see <see cref="MatchStage"/>) is derived from <see cref="IsLit"/>.
    /// Master-client object: the state authority snaps logs and validates requests.
    /// </summary>
    public class FireplaceManager : NetworkBehaviour, IInteractable, IPinSpot, IMatchResettable, IStateAuthorityChanged
    {
        private const int MaxColliders = 64;
        private const float AreaHeight = 1.5f;
        private const float PlaceFovPunch = 1.5f;
        private const float PlaceFovPunchTime = 0.1f;
        private const float HintPulseHz = 3f;
        private const float HintScale = 1.05f;
        private const float PourFovPunch = 1f;
        private const float PourFovPunchTime = 0.1f;
        private const float EjectWaitSeconds = 1f;
        private const float DouseFireScale = 0.35f;
        private const float FireScaleSmoothing = 12f;
        private const string PouringText = "Vertiendo...";
        private const string OtherPouringText = "Otro gnomo está vertiendo";
        private const string GraceText = "Espera: acaba de encenderse";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [FormerlySerializedAs("amountLabel")]
        [SerializeField] private TextMeshProUGUI _amountLabel;
        [FormerlySerializedAs("requiredAmount")]
        [SerializeField] private int _requiredWood = 5;
        [SerializeField] private GameObject _fire;

        [Header("Logs area")]
        [FormerlySerializedAs("logsLayer")]
        [SerializeField] private LayerMask _logsLayer = ~0;
        [Tooltip("Logs closer than this to the fireplace (horizontally, and within 1.5 m vertically) are inside the area.")]
        [SerializeField, Min(0f)] private float _areaRadius = 1.5f;
        [Tooltip("A log lying on the ground snaps into a free slot once it moves slower than this (m/s).")]
        [SerializeField, Min(0f)] private float _restSpeed = 0.25f;
        [Tooltip("A loose log must have been falling or lying free this long (s) before it snaps, so a log just let go of does not snap in mid-air.")]
        [SerializeField, Min(0f)] private float _settleTime = 0.4f;
        [Tooltip("Extra metres of leeway when the authority checks a placement, since the carrier it sees lags behind the player who asked.")]
        [SerializeField, Min(0f)] private float _placeTolerance = 0.5f;

        [Header("Put out")]
        [Tooltip("A gnome with a full bucket must be closer than this to the fireplace (horizontally) to pour it.")]
        [SerializeField, Min(0f)] private float _pourRange = 2f;
        [Tooltip("Extra metres of leeway when the authority checks a pour, since the gnome it sees lags behind the player who asked.")]
        [SerializeField, Min(0f)] private float _pourTolerance = 0.5f;
        [Tooltip("Seconds a pour takes; the flames shrink meanwhile and anything that interrupts it spills the bucket.")]
        [SerializeField, Min(0.1f)] private float _pourTime = 0.8f;
        [Tooltip("Extra metres the pouring gnome may walk away before the pour is cancelled.")]
        [SerializeField, Min(0f)] private float _pourCancelSlack = 0.5f;
        [Tooltip("Seconds after the fire is lit during which it can't be put out, so the change of milestone can be read.")]
        [SerializeField, Min(0f)] private float _litGraceTime = 4f;
        [Tooltip("Seconds after a master change during which the new authority does not snap loose logs into slots, so a log that was just ejected is not put back in by the new master (the ejection is local to the old authority).")]
        [SerializeField, Min(0f)] private float _snapGraceAfterMigration = 3f;

        [Header("Comeback")]
        [Tooltip("From this many put-out fires on (inclusive), each new one gives the camper a comeback: matches back at their feet and a wider gaze.")]
        [SerializeField, Min(1)] private int _comebackFromExtinguish = 2;
        [Tooltip("Seconds the camper's gaze cone stays widened after a comeback.")]
        [SerializeField, Min(0f)] private float _comebackGazeSeconds = 10f;
        [Tooltip("Metres in front of the camper where the matches are set down.")]
        [SerializeField, Min(0f)] private float _comebackMatchesDistance = 1f;
        [Tooltip("Layers the matches can rest on when they are set down (ground only: Environment).")]
        [SerializeField] private LayerMask _comebackGroundMask = 1 << 11;
        [Tooltip("Layers that block the line from the camper's chest to the drop point (Environment, Occluder).")]
        [SerializeField] private LayerMask _comebackBlockMask = (1 << 11) | (1 << 12);
        [Tooltip("Layers the drop point must be free of (Player, Gnome, Item, Environment).")]
        [SerializeField] private LayerMask _comebackClearMask = (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11);
        [Tooltip("Radius of the free-space check at the drop point, in metres.")]
        [SerializeField, Min(0.05f)] private float _comebackClearRadius = 0.3f;
        [Tooltip("Metres to the side of the camper's feet used when the front is blocked.")]
        [SerializeField, Min(0.5f)] private float _comebackSideDistance = 1.1f;
        [Tooltip("Speed (m/s) at which a log jumps out of the fireplace when the fire is put out.")]
        [SerializeField, Min(0f)] private float _logEjectSpeed = 3f;
        [Tooltip("Angle above the ground (degrees) of the jump of that log.")]
        [SerializeField, Range(0f, 89f)] private float _logEjectElevation = 45f;
        [Tooltip("Heard by everyone, from the fireplace, when the fire is put out.")]
        [SerializeField] private SoundCue _extinguishSfx;
        [FormerlySerializedAs("_douseSfx")]
        [Tooltip("Heard by everyone, from the fireplace, when a pour starts (the hiss).")]
        [SerializeField] private SoundCue _pourSfx;

        [Header("Water balloons")]
        [Tooltip("A balloon only counts when it lands within this distance (m, horizontally) of the fire.")]
        [SerializeField, Min(0f)] private float _balloonHitRadius = 2.5f;
        [Tooltip("Seconds the fire stays weakened after the first balloon; a second hit inside this window puts it out.")]
        [SerializeField, Min(1f)] private float _weakenSeconds = 20f;
        [Tooltip("Seconds that must pass between two accepted balloon hits (two gnomes hitting together count once).")]
        [SerializeField, Min(0f)] private float _balloonMinInterval = 0.5f;
        [Tooltip("How much a weakened fire looks doused (0 = like a full fire, 1 = like a finished pour): lower flames, more steam.")]
        [SerializeField, Range(0f, 1f)] private float _weakenedDouse = 0.5f;
        [Tooltip("A balloon hit that arrives before the replicated throw data of its thrower waits this long (s) for it, then is dropped.")]
        [SerializeField, Min(0f)] private float _balloonPendingSeconds = 0.5f;
        [Tooltip("Optional smoke puff played when the fire goes out.")]
        [SerializeField] private ParticleSystem _smokePuff;

        [Header("Feel (built by GnoMore/Construir feel de fogata y cubeta; every field is optional)")]
        [Tooltip("Looping embers, shown while the fire is out; they pick up with every log placed.")]
        [SerializeField] private ParticleSystem _embers;
        [Tooltip("Burst of sparks at the moment the fire is lit.")]
        [SerializeField] private ParticleSystem _ignitionSpark;
        [Tooltip("Always-playing steam; its emission follows the pour and it gets a burst when the fire goes out.")]
        [SerializeField] private ParticleSystem _steam;
        [Tooltip("Point light that follows the fire states and flickers.")]
        [SerializeField] private Light _fireLight;
        [Tooltip("Looping hiss whose volume grows with the pour.")]
        [SerializeField] private AudioSource _hissLoop;
        [Tooltip("Seconds the flame takes to grow from a spark to full size when the fire is lit.")]
        [SerializeField, Min(0.01f)] private float _igniteTime = 0.6f;
        [Tooltip("Flame scale (fraction of full size) at the instant the fire is lit.")]
        [SerializeField, Range(0f, 1f)] private float _igniteStartScale = 0.2f;
        [Tooltip("Embers emitted per second with 0 logs placed (x) and with all logs placed (y).")]
        [SerializeField] private Vector2 _embersRate = new(3f, 14f);
        [Tooltip("Scale of the embers emitter with 0 logs (x) and with all logs (y); it widens as the logs go in.")]
        [SerializeField] private Vector2 _embersScale = new(0.6f, 1.3f);
        [Tooltip("Light intensity with the fire out and no logs (x), out with all logs (y) and fully lit (z).")]
        [SerializeField] private Vector3 _lightIntensity = new(0.05f, 0.35f, 3f);
        [Tooltip("Light range (m) with the fire out and no logs (x), out with all logs (y) and fully lit (z). About 6 m lit so it lights the camp at night.")]
        [SerializeField] private Vector3 _lightRange = new(1f, 2.5f, 6f);
        [Tooltip("Fraction of the light intensity and range that the flicker adds or removes.")]
        [SerializeField, Range(0f, 0.5f)] private float _flickerAmount = 0.15f;
        [Tooltip("Speed of the flicker noise.")]
        [SerializeField, Min(0f)] private float _flickerSpeed = 7f;
        [Tooltip("Fraction of the lit light that is left when the flames are almost gone at the end of the pour.")]
        [SerializeField, Range(0f, 1f)] private float _douseLightFloor = 0.2f;
        [Tooltip("Steam particles per second at the end of the pour.")]
        [SerializeField, Min(0f)] private float _steamMaxRate = 40f;
        [Tooltip("Steam particles emitted at once when the fire goes out.")]
        [SerializeField, Min(0)] private int _extinguishSteamBurst = 30;
        [Tooltip("Volume of the hiss at the end of the pour.")]
        [SerializeField, Range(0f, 1f)] private float _hissMaxVolume = 0.7f;
        [Tooltip("Pitch of the hiss at the start (x) and at the end (y) of the pour.")]
        [SerializeField] private Vector2 _hissPitch = new(0.9f, 1.2f);

        [Header("Slots")]
        [Tooltip("Distance from the fireplace centre to the middle of each log.")]
        [SerializeField, Min(0f)] private float _slotRadius = 0.5f;
        [Tooltip("Height of the middle of each log above the fireplace root.")]
        [SerializeField] private float _slotHeight = 0.34f;
        [Tooltip("Angle between each log and the vertical; the tops lean towards the centre.")]
        [SerializeField, Range(0f, 90f)] private float _slotTiltFromVertical = 62f;
        [Tooltip("Angle of the first slot around the fireplace, in degrees.")]
        [SerializeField] private float _slotAngleOffset = 18f;
        [Tooltip("Euler rotation added to every slot. The log's root has its long axis along X, which already follows the lean.")]
        [SerializeField] private Vector3 _slotRotationOffset = Vector3.zero;

        [Header("Ghosts")]
        [Tooltip("The log prefab; its mesh, and where and how big it sits inside the prefab, are used for the slot ghosts and gizmos.")]
        [SerializeField] private GameObject _logPrefab;
        [Tooltip("Transparent material of the ghosts that show the free slots.")]
        [SerializeField] private Material _ghostMaterial;
        [SerializeField] private Color _ghostColor = new(0.30f, 0.65f, 1f, 0.22f);
        [Tooltip("Alpha range of the ghost that the carried log would snap into.")]
        [SerializeField] private Vector2 _hintAlpha = new(0.40f, 0.65f);

        [Header("Sound")]
        [Tooltip("Heard by everyone, from the fireplace, when a log is placed.")]
        [SerializeField] private SoundCue _snapSfx;
        [Tooltip("Heard by everyone, from the fireplace, when a log is taken out.")]
        [SerializeField] private SoundCue _unsnapSfx;
        [Tooltip("Heard by everyone when the last missing log is placed.")]
        [SerializeField] private SoundCue _readySfx;
        [Tooltip("Heard by everyone, from the fireplace, when the camper lights the fire.")]
        [SerializeField] private SoundCue _lightSfx;

        private readonly Collider[] _results = new Collider[MaxColliders];
        private readonly HashSet<FireplaceWood> _seen = new();
        private readonly List<Carryable> _pinned = new();
        private Transform[] _slots;
        private Transform[] _ghosts;
        private Renderer[] _ghostRenderers;
        private bool[] _occupied;
        private Vector3 _ghostScale = Vector3.one;
        private MaterialPropertyBlock _block;
        private int _lastLogs;
        private int _lastExtinguishCount;
        // Authority only: the log that jumped out and the speed it must get once physics owns it again.
        private Carryable _ejectedLog;
        private Vector3 _ejectVelocity;
        private float _ejectUntil;
        private bool _ejectPending;
        // Authority only: no snapping of loose logs until this time (set when the authority changes).
        private float _snapBlockedUntil;
        private int _labelPercent = -1;
        private float _fireFactor = 1f;
        private Vector3 _fireScale = Vector3.one;
        private bool _isSpawned;
        // Authority only (never networked): tick of the last accepted balloon hit; after a master change the first hit is free.
        private bool _hasBalloonHit;
        private int _lastBalloonHitTick;
        // Authority only: one waiting hit per thrower (fixed array, nothing allocates).
        private struct PendingHit
        {
            public bool Used;
            public NetworkBehaviourId Thrower;
            public Vector3 Point;
            public byte Seq;
            public int Tick;
        }

        private const int MaxPendingHits = 8;
        private readonly PendingHit[] _pendingHits = new PendingHit[MaxPendingHits];
        // Presentation only: when the fire was last lit on this peer (a late joiner sees it fully grown) and the light smoothing.
        private float _igniteStart = float.NegativeInfinity;
        private float _lightLevel;
        private float _lightReach;
        private float _flickerSeed;
        private float _emberLevel = 1f;

        [Networked, OnChangedRender(nameof(OnLogsChanged))]
        private int LogsInside { get; set; }

        [Networked, OnChangedRender(nameof(OnLitChanged))]
        public NetworkBool IsLit { get; private set; }

        [Networked, OnChangedRender(nameof(OnExtinguishCountChanged))]
        private int ExtinguishCount { get; set; }

        [Networked] private TickTimer LitGrace { get; set; }

        // Written only by the master when a comeback starts; the camper's gaze and the HUD read it replicated.
        [Networked] private TickTimer ComebackTimer { get; set; }

        // A pour is replicated as a timer plus who pours what; the progress is derived from the timer (see DouseFraction).
        // Pourer and PourBucket are ids, so a new master can tell whether the pour is still valid.
        [Networked, OnChangedRender(nameof(OnPourChanged))]
        private TickTimer PourTimer { get; set; }

        [Networked] private PlayerInteractor Pourer { get; set; }

        [Networked] private WaterBucket PourBucket { get; set; }

        // Anchor + tick (CLAUDE.md rule 4): the tick of the first balloon hit, 0 when the fire is not weakened. The window is
        // derived from it on every peer, and a new master keeps counting from the same tick.
        [Networked] private int WeakenedTick { get; set; }

        public static FireplaceManager Instance { get; private set; }

        /// <summary>Times the fire has been put out this match (replicated).</summary>
        public int Extinguished => _isSpawned ? ExtinguishCount : 0;

        public int RequiredWood => _requiredWood;
        /// <summary>Whether the camper's comeback bonus (wider gaze) is running.</summary>
        public bool ComebackActive => _isSpawned && Runner != null && !ComebackTimer.ExpiredOrNotRunning(Runner);

        public int WoodCount => _isSpawned ? LogsInside : 0;
        public bool HasEnoughWood => _isSpawned && LogsInside >= _requiredWood;

        /// <summary>How far the pour has gone, 0 to 1; the flames shrink with it. Derived from the pour timer.</summary>
        public float DouseFraction
        {
            get
            {
                if (!IsBeingDoused)
                    return 0f;

                var remaining = PourTimer.RemainingTime(Runner) ?? 0f;
                return Mathf.Clamp01(1f - remaining / _pourTime);
            }
        }

        /// <summary>
        /// True while the fire is lit and a first balloon hit is still inside its window. Derived from the replicated tick,
        /// so a proxy's tick can lag a little: for presentation. The authority decides the second hit with its own tick.
        /// </summary>
        public bool IsWeakened => _isSpawned && IsLit && WeakenedTick != 0 && NetAnchor.Elapsed(Runner, WeakenedTick) < _weakenSeconds;

        // How doused the flames look: the pour progress, or the weakened level if that is higher.
        private float DouseLook => Mathf.Max(DouseFraction, IsWeakened ? _weakenedDouse : 0f);

        /// <summary>True while a gnome is pouring water on the fire.</summary>
        public bool IsBeingDoused => _isSpawned && IsLit && PourTimer.IsRunning;

        /// <summary>
        /// True while <paramref name="bucket"/> is the one being poured on the fire; <paramref name="fraction"/> is the
        /// pour progress, 0 to 1. Derived from replicated state, so every peer sees the same pour.
        /// </summary>
        public bool TryGetPour(WaterBucket bucket, out float fraction)
        {
            fraction = 0f;
            if (!IsBeingDoused || bucket == null || PourBucket != bucket)
                return false;

            fraction = DouseFraction;
            return true;
        }

        private void Awake()
        {
            _requiredWood = Mathf.Max(1, _requiredWood);
            _slots = new Transform[_requiredWood];
            _occupied = new bool[_requiredWood];
            for (var i = 0; i < _requiredWood; i++)
            {
                GetSlotPose(i, out var position, out var rotation);
                var slot = new GameObject($"LogSlot {i}").transform;
                slot.SetParent(transform, false);
                slot.SetLocalPositionAndRotation(position, rotation);
                _slots[i] = slot;
            }

            if (_fire != null)
                _fireScale = _fire.transform.localScale;

            _flickerSeed = UnityEngine.Random.value * 100f;
            if (_hissLoop != null)
            {
                _hissLoop.playOnAwake = false;
                _hissLoop.loop = true;
                _hissLoop.volume = 0f;
                _hissLoop.outputAudioMixerGroup = AudioManager.SfxGroup;
            }

            BuildGhosts();
        }

        public override void Spawned()
        {
            _isSpawned = true;
            Instance = this;
            _lastLogs = LogsInside;
            _lastExtinguishCount = ExtinguishCount;
            UpdateLabel();
            UpdateFire();
            _igniteStart = float.NegativeInfinity; // Late joiners see the fire as it is now, without the ignition.
            _lightLevel = 0f;
            _lightReach = 0f;
        }

        /// <summary>Called on every peer when the master changes; the new master holds off snapping loose logs for a moment.</summary>
        public void StateAuthorityChanged()
        {
            if (!_isSpawned || !HasStateAuthority)
                return;

            _snapBlockedUntil = Time.time + _snapGraceAfterMigration;
            _ejectedLog = null;
            _ejectPending = false;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            if (_hissLoop != null)
                _hissLoop.Stop();
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Where the log resting in <paramref name="slot"/> is posed.</summary>
        public Transform GetPinPoint(int slot) => _slots != null && slot >= 0 && slot < _slots.Length ? _slots[slot] : transform;

        /// <summary>Authority only: the fire is out; logs are counted again from the slots.</summary>
        public void ResetForMatch()
        {
            IsLit = false;
            ExtinguishCount = 0;
            LitGrace = default;
            ComebackTimer = default;
            WeakenedTick = 0;
            _hasBalloonHit = false;
            ClearPour();
            _ejectedLog = null;
            _ejectPending = false;
        }

        public string GetPrompt(PlayerInteractor interactor) =>
            interactor.Role == PlayerRole.Gnome ? "¡Apagar fogata!" : "Prender fogata";

        /// <summary>Mirrors the authority's rules using the replicated state.</summary>
        public bool CanInteract(PlayerInteractor interactor) => CanLight(interactor) || CanPour(interactor, 0f);

        public void Interact(PlayerInteractor interactor)
        {
            if (interactor.Role == PlayerRole.Gnome)
                RequestPour(interactor);
            else
                RPC_RequestLight(interactor.Id);
        }

        // A camper holding the matches can light a ready fire.
        private bool CanLight(PlayerInteractor who)
        {
            if (!_isSpawned || !MatchManager.AllowsInteraction || IsLit || who.Role != PlayerRole.Camper || !HasEnoughWood)
                return false;

            var carried = who.Carried;
            return carried != null && carried.TryGetComponent<Matches>(out _);
        }

        /// <summary>
        /// Why a gnome that stands in reach of the lit fire with a full bucket can't pour right now, in Spanish for the prompt,
        /// or null when it is not blocked (or not in that situation at all). While it is not null the bucket must not be dropped.
        /// Mirrors <see cref="CanPour(PlayerInteractor)"/> using the replicated state.
        /// </summary>
        public string PourBlockedMessage(PlayerInteractor who)
        {
            if (!_isSpawned || !MatchManager.AllowsInteraction || !IsLit || who.Role != PlayerRole.Gnome)
                return null;

            var carried = who.Carried;
            if (carried == null || carried.HolderCount != 1 || !carried.TryGetComponent<WaterBucket>(out var bucket) || !bucket.IsFull)
                return null;

            if (IsDisabled(who) || HorizontalDistance(who.transform.position) > _pourRange)
                return null;

            if (PourTimer.IsRunning)
                return Pourer == who ? PouringText : OtherPouringText;

            return LitGrace.ExpiredOrNotRunning(Runner) ? null : GraceText;
        }

        /// <summary>Whether this gnome can start pouring its full bucket on the fire right now. Mirrors the authority's rules.</summary>
        public bool CanPour(PlayerInteractor who) => CanPour(who, 0f);

        // A gnome carrying a full bucket close enough to a lit fire, once the grace time after lighting is over.
        // LitGrace and PourTimer are read from the replicated state; if a proxy lags a tick the authority simply refuses.
        private bool CanPour(PlayerInteractor who, float tolerance)
        {
            if (!_isSpawned || !MatchManager.AllowsInteraction || !IsLit || who.Role != PlayerRole.Gnome
                || PourTimer.IsRunning || !LitGrace.ExpiredOrNotRunning(Runner))
                return false;

            var carried = who.Carried;
            if (carried == null || carried.HolderCount != 1 || !carried.TryGetComponent<WaterBucket>(out var bucket) || !bucket.IsFull)
                return false;

            if (IsDisabled(who))
                return false;

            return HorizontalDistance(who.transform.position) <= _pourRange + tolerance;
        }

        // Stone or stunned players can't act (and a pour in their hands is interrupted).
        private static bool IsDisabled(PlayerInteractor who) =>
            (who.TryGetComponent<Petrifiable>(out var petrifiable) && petrifiable.IsStone)
            || (who.TryGetComponent<Kickable>(out var kickable) && kickable.IsStunned);

        private float HorizontalDistance(Vector3 position)
        {
            var offset = position - transform.position;
            return new Vector2(offset.x, offset.z).magnitude;
        }

        /// <summary>Asks the authority to start pouring the carried bucket on the fire.</summary>
        public void RequestPour(PlayerInteractor who)
        {
            RPC_RequestPour(who.Id);
            if (CameraPunch.Instance != null)
                CameraPunch.Instance.PunchFov(PourFovPunch, PourFovPunchTime);
        }

        /// <summary>Whether the log this player carries can be put into a free slot right now. Mirrors the authority's rules.</summary>
        public bool CanPlace(PlayerInteractor who) => CanPlace(who, 0f);

        private bool CanPlace(PlayerInteractor who, float tolerance)
        {
            if (!_isSpawned || !MatchManager.AllowsInteraction || who.Role != PlayerRole.Camper)
                return false;

            var carried = who.Carried;
            if (carried == null || carried.HolderCount != 1 || !carried.IsMoving || !carried.TryGetComponent<FireplaceWood>(out _))
                return false;

            return IsInArea(carried.transform.position, tolerance) && NearestFreeSlot(carried.transform.position, true) >= 0;
        }

        /// <summary>Asks the authority to put the carried log into the nearest free slot.</summary>
        public void RequestPlace(PlayerInteractor who)
        {
            // The log starts flying to the slot the ghost shows as soon as the key is pressed; the authority still decides.
            var carried = who.Carried;
            if (carried != null && carried.HasPrediction)
                return;

            RPC_RequestPlace(who.Id);
            if (carried != null && !HasStateAuthority)
            {
                var slot = NearestFreeSlot(carried.transform.position, true);
                if (slot >= 0)
                    carried.PredictPin(this, slot);
            }

            if (CameraPunch.Instance != null)
                CameraPunch.Instance.PunchFov(PlaceFovPunch, PlaceFovPunchTime);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestLight(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanLight(who))
                return;

            IsLit = true;
            LitGrace = TickTimer.CreateFromSeconds(Runner, _litGraceTime);
            WeakenedTick = 0;
            ClearPour();
            if (Telemetry.Enabled) Telemetry.Begin("fire_lit", who).Int("logs", LogsInside).Send();
            if (MatchManager.Instance != null) MatchManager.Instance.NoteFireLit();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestPour(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanPour(who, _pourTolerance))
                return;

            PourBucket = who.Carried.GetComponent<WaterBucket>();
            Pourer = who;
            PourTimer = TickTimer.CreateFromSeconds(Runner, _pourTime);
            if (Telemetry.Enabled) Telemetry.Begin("pour_start", who).Send();
        }

        /// <summary>
        /// A gnome's water balloon landed at <paramref name="point"/>. The authority validates: match running, fire lit and
        /// past its grace, the thrower is a gnome whose own client sent this, the point is within the hit radius, the throw
        /// really landed there and it is the first report of that throw, and no hit was accepted in the last moments.
        /// The first hit weakens the fire; a second one inside the window puts it out through the same path as a pour.
        /// A camper (or anyone who is not that gnome's client) is refused.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_BalloonHit(Vector3 point, NetworkBehaviourId throwerId, byte seq, RpcInfo info = default)
        {
            if (!_isSpawned || !Runner.TryFindBehaviour(throwerId, out BalloonThrower thrower))
                return;

            // Only the thrower's own client may report its hit. A call made on this very peer carries no remote source.
            // Checked here, on arrival, never when a waiting hit is checked again.
            var owner = thrower.Object.StateAuthority;
            if (info.Source != owner && !(info.Source == PlayerRef.None && Runner.LocalPlayer == owner))
                return;

            if (ApplyBalloonHit(thrower, point, seq) != HitResult.Wait)
                return;

            // The RPC beat the replicated throw data (short flight, lost packet): keep it a moment, one per thrower.
            var slot = -1;
            for (var i = 0; i < _pendingHits.Length; i++)
            {
                if (_pendingHits[i].Used && _pendingHits[i].Thrower == throwerId)
                {
                    slot = i;
                    break;
                }

                if (slot < 0 && !_pendingHits[i].Used)
                    slot = i;
            }

            if (slot >= 0)
                _pendingHits[slot] = new PendingHit { Used = true, Thrower = throwerId, Point = point, Seq = seq, Tick = Runner.Tick.Raw };
        }

        private enum HitResult
        {
            Done,
            Wait,
        }

        // Authority only: waiting hits are checked again every tick until their thrower's data arrives or the time is up.
        private void TickPendingHits()
        {
            for (var i = 0; i < _pendingHits.Length; i++)
            {
                ref var pending = ref _pendingHits[i];
                if (!pending.Used)
                    continue;

                if (NetAnchor.Elapsed(Runner, pending.Tick) > _balloonPendingSeconds
                    || !Runner.TryFindBehaviour(pending.Thrower, out BalloonThrower thrower)
                    || ApplyBalloonHit(thrower, pending.Point, pending.Seq) != HitResult.Wait)
                    pending.Used = false;
            }
        }

        // Authority only: every rule of a hit. Wait means the thrower's replicated sequence has not reached the reported one yet
        // (compared by signed difference, so the byte can wrap around).
        private HitResult ApplyBalloonHit(BalloonThrower thrower, Vector3 point, byte seq)
        {
            if (!MatchManager.AllowsInteraction || !IsLit || !LitGrace.ExpiredOrNotRunning(Runner) || !thrower.IsGnome)
                return HitResult.Done;

            if ((sbyte)(seq - thrower.LastThrowSeq) > 0)
                return HitResult.Wait;

            ApplyAcceptedBalloonHit(thrower, point, seq);
            return HitResult.Done;
        }

        private void ApplyAcceptedBalloonHit(BalloonThrower thrower, Vector3 point, byte seq)
        {
            if (HorizontalDistance(point) > _balloonHitRadius || Mathf.Abs(point.y - transform.position.y) > AreaHeight)
                return;

            if (_hasBalloonHit && NetAnchor.Elapsed(Runner, _lastBalloonHitTick) < _balloonMinInterval)
                return;

            if (!thrower.TryAcceptHit(point, seq))
                return;

            _hasBalloonHit = true;
            _lastBalloonHitTick = Runner.Tick.Raw;
            var by = thrower.GetComponent<PlayerInteractor>();
            var weakened = WeakenedTick != 0 && NetAnchor.Elapsed(Runner, WeakenedTick) < _weakenSeconds;
            if (Telemetry.Enabled) Telemetry.Begin("balloon_hit", by).Bool("weakened", weakened).Send();
            if (!weakened)
            {
                WeakenedTick = Runner.Tick.Raw;
                return;
            }

            // The second hit: the fire goes out like a finished pour (a pour in progress is dropped by PutOut).
            var bucket = PourBucket;
            if (bucket != null)
                bucket.RequestSpill();
            PutOut(by);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestPlace(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanPlace(who, _placeTolerance))
                return;

            var log = who.Carried;
            var slot = NearestFreeSlot(log.transform.position, true);
            log.ForceReleaseAll();
            log.Pin(this, slot);
            if (Telemetry.Enabled) LogWood("log_in_zone", log, "place", who);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            if (MatchManager.AllowsInteraction && Time.time >= _snapBlockedUntil)
                SnapRestingLogs();
            Carryable.GetPinnedTo(this, _pinned);
            LogsInside = _pinned.Count;

            TickPour();
            TickPendingHits();
            TickEjectedLog();

            if (IsLit && !HasEnoughWood)
            {
                IsLit = false;
                WeakenedTick = 0;
                ClearPour();
            }
        }

        // Authority only: follows a running pour. Anything that invalidates it cancels it and spills the bucket,
        // including the pourer leaving the game or the master changing (the new master finds a pourer that is gone).
        private void TickPour()
        {
            if (!PourTimer.IsRunning)
                return;

            var bucket = PourBucket;
            var check = IsLit ? CheckPour(bucket) : PourCheck.FireOut;
            if (check != PourCheck.Valid)
            {
                if (Telemetry.Enabled) LogPourCancelled(bucket, check);
                if (bucket != null)
                    bucket.RequestSpill();
                ClearPour();
                return;
            }

            if (PourTimer.Expired(Runner))
                FinishPour(bucket);
        }

        private enum PourCheck
        {
            Valid,
            FireOut,
            PourerLeft,
            BucketGone,
            MatchOver,
            BucketEmpty,
            BucketDropped,
            Disabled,
            TooFar,
        }

        // Why a running pour is (in)valid; the first failing check wins. Valid means the pour goes on.
        private PourCheck CheckPour(WaterBucket bucket)
        {
            var pourer = Pourer;
            if (pourer == null || pourer.Object == null || !pourer.Object.IsValid)
                return PourCheck.PourerLeft;
            if (bucket == null || bucket.Object == null || !bucket.Object.IsValid)
                return PourCheck.BucketGone;
            if (!MatchManager.AllowsInteraction)
                return PourCheck.MatchOver;
            if (!bucket.IsFull)
                return PourCheck.BucketEmpty;
            if (Carryable.FindHeldBy(pourer) != bucket.Carryable)
                return PourCheck.BucketDropped;
            if (IsDisabled(pourer))
                return PourCheck.Disabled;
            return HorizontalDistance(pourer.transform.position) <= _pourRange + _pourCancelSlack ? PourCheck.Valid : PourCheck.TooFar;
        }

        // The water is out: the fire dies, the bucket is empty and one log jumps out, so lighting it again takes a log and matches.
        private void FinishPour(WaterBucket bucket)
        {
            var pourer = Pourer;
            bucket.RequestSpill();
            PutOut(pourer);
        }

        // Authority only: the fire dies for whoever put it out (a pour or a second balloon): points, one log jumps out,
        // and the comeback starts after enough fires put out. Called after the bucket was spilled, if there was one.
        private void PutOut(PlayerInteractor by)
        {
            if (Telemetry.Enabled) LogDoused(by);
            if (MatchManager.Instance != null && by != null && by.Object != null && by.Object.IsValid)
                MatchManager.Instance.Award(by.Object.StateAuthority, MatchManager.ScoreReason.Douse);
            ClearPour();
            IsLit = false;
            LitGrace = default;
            WeakenedTick = 0;
            ExtinguishCount++;
            EjectTopLog();
            if (ExtinguishCount >= _comebackFromExtinguish)
                StartComeback();
        }

        // Authority only: the camper gets a second wind. The matches are set down in front of the camper (whoever carried
        // them lets go) and the gaze bonus timer starts. If there is no camper or no matches (they left), only what exists is applied.
        private void StartComeback()
        {
            ComebackTimer = TickTimer.CreateFromSeconds(Runner, _comebackGazeSeconds);
            if (Telemetry.Enabled) Telemetry.Begin("comeback").Int("count", ExtinguishCount).Send();

            CamperGaze camper = null;
            var gazes = CamperGaze.All;
            for (var g = 0; g < gazes.Count; g++)
            {
                var gaze = gazes[g];
                if (gaze != null && gaze.Object != null && gaze.Object.IsValid)
                {
                    camper = gaze;
                    break;
                }
            }

            if (camper == null)
                return;

            var forward = Vector3.ProjectOnPlane(camper.transform.forward, Vector3.up);
            forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
            var feet = camper.transform.position;
            var right = Vector3.Cross(Vector3.up, forward);
            var front = feet + forward * _comebackMatchesDistance;

            // The front spot is used only when nothing solid lies between the camper's chest and it; otherwise the matches
            // go to a free spot beside the feet. Nothing may end up on a player or inside a prop.
            var point = default(Vector3);
            var found = false;
            if (!Physics.Linecast(feet + Vector3.up * 1.2f, front + Vector3.up * 0.3f, _comebackBlockMask, QueryTriggerInteraction.Ignore))
                found = TryFindDropPoint(front, out point);

            if (!found)
            {
                var side = _comebackSideDistance;
                found = TryFindDropPoint(feet + right * side, out point)
                    || TryFindDropPoint(feet - right * side, out point)
                    || TryFindDropPoint(feet - forward * side, out point);
            }

            if (!found)
                return;

            var matchList = Matches.All;
            for (var i = 0; i < matchList.Count; i++)
            {
                var matches = matchList[i];
                if (matches == null || !matches.TryGetComponent<Carryable>(out var carryable) || !carryable.HasStateAuthority)
                    continue;

                // Someone is carrying them: only a gnome's grip is broken; a camper keeps hers.
                if (carryable.IsHeld && !carryable.IsHeldByGnomes)
                    continue;

                carryable.PlaceAt(point + Vector3.up * 0.1f, Quaternion.LookRotation(forward, Vector3.up));
            }
        }

        // Authority only: finds the ground under the candidate and checks the spot is free of players and props.
        private bool TryFindDropPoint(Vector3 candidate, out Vector3 point)
        {
            point = candidate;
            if (!Physics.Raycast(candidate + Vector3.up * 2f, Vector3.down, out var hit, 6f, _comebackGroundMask, QueryTriggerInteraction.Ignore))
                return false;

            point = hit.point;
            return !Physics.CheckSphere(point + Vector3.up * (_comebackClearRadius + 0.05f), _comebackClearRadius, _comebackClearMask, QueryTriggerInteraction.Ignore);
        }

        private void ClearPour()
        {
            if (PourTimer.IsRunning)
                PourTimer = default;
            Pourer = null;
            PourBucket = null;
        }

        // Authority only: unpins the log of the highest occupied slot. It is kinematic until the Carryable's own rule turns
        // it dynamic (which also zeroes its velocity), so the jump is applied in TickEjectedLog afterwards.
        private void EjectTopLog()
        {
            Carryable.GetPinnedTo(this, _pinned);
            Carryable top = null;
            foreach (var log in _pinned)
            {
                if (top == null || log.PinnedSlot > top.PinnedSlot)
                    top = log;
            }

            if (top == null)
                return;

            var outward = Vector3.ProjectOnPlane(top.transform.position - transform.position, Vector3.up);
            if (outward.sqrMagnitude < 0.0001f)
                outward = transform.forward;

            var elevation = _logEjectElevation * Mathf.Deg2Rad;
            _ejectVelocity = (outward.normalized * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation)) * _logEjectSpeed;
            _ejectedLog = top;
            _ejectPending = true;
            _ejectUntil = Time.time + EjectWaitSeconds;
            top.Unpin();
            if (Telemetry.Enabled) LogWood("log_out_zone", top, "eject", null);
        }

        // Telemetry only (authority). The log count is read from the pinned logs, so it is already the resulting count.
        private void LogWood(string evt, Carryable log, string how, PlayerInteractor actor)
        {
            Carryable.GetPinnedTo(this, _pinned);
            var e = actor != null ? Telemetry.Begin(evt, actor) : Telemetry.Begin(evt);
            e.Int("log", (int)log.Object.Id.Raw).Int("count", _pinned.Count).Str("how", how).Send();
        }

        // Telemetry only (authority): the fire goes out (a finished pour or a second balloon). Called before the pour state is cleared.
        private void LogDoused(PlayerInteractor pourer)
        {
            var valid = pourer != null && pourer.Object != null && pourer.Object.IsValid;
            if (PourTimer.IsRunning)
            {
                var e = valid ? Telemetry.Begin("pour_end", pourer) : Telemetry.Begin("pour_end");
                e.Str("result", "doused").Str("reason", "none").Send();
            }

            var f = valid ? Telemetry.Begin("fire_out", pourer) : Telemetry.Begin("fire_out");
            f.Int("logs", LogsInside).Int("extinguish", ExtinguishCount + 1).Send();
        }

        // Telemetry only (authority): the pour was interrupted; the reason comes from CheckPour.
        private void LogPourCancelled(WaterBucket bucket, PourCheck check)
        {
            var pourer = Pourer;
            var pourerValid = pourer != null && pourer.Object != null && pourer.Object.IsValid;
            var reason = check switch
            {
                PourCheck.FireOut => "fire_out",
                PourCheck.PourerLeft => "pourer_left",
                PourCheck.BucketGone => "bucket_gone",
                PourCheck.MatchOver => "match_over",
                PourCheck.BucketEmpty => "bucket_empty",
                PourCheck.BucketDropped => "bucket_dropped",
                PourCheck.Disabled => "stunned_or_stone",
                _ => "too_far",
            };

            var e = pourerValid ? Telemetry.Begin("pour_end", pourer) : Telemetry.Begin("pour_end");
            e.Str("result", "cancelled").Str("reason", reason).Send();
        }

        // Gives the jump to the ejected log once it is under physics; gives up if that takes too long.
        private void TickEjectedLog()
        {
            if (!_ejectPending)
                return;

            var log = _ejectedLog;
            if (log == null || log.Object == null || !log.Object.IsValid || Time.time > _ejectUntil)
            {
                _ejectPending = false;
                return;
            }

            if (log.IsPinned || log.IsHeld || !log.TryGetComponent<Rigidbody>(out var body))
            {
                _ejectPending = false;
                return;
            }

            if (body.isKinematic)
                return;

            body.linearVelocity = _ejectVelocity;
            _ejectPending = false;
        }

        // Logs that lie still inside the area settle into a free slot; a log with no slot left just stays where it is.
        private void SnapRestingLogs()
        {
            RefreshOccupied();
            var found = Physics.OverlapSphereNonAlloc(transform.position, _areaRadius, _results, _logsLayer);
            _seen.Clear();
            for (var i = 0; i < found; i++)
            {
                if (!_results[i].TryGetComponent<FireplaceWood>(out var wood) || !_seen.Add(wood))
                    continue;

                if (!wood.TryGetComponent<Carryable>(out var log) || IsEjectedAndLoose(log))
                    continue;

                if (!log.WasHandled || !log.LastHeldByCamper || log.FreeTime < _settleTime || log.HolderCount > 0 || log.IsPinned || log.Inert
                    || !wood.TryGetComponent<Rigidbody>(out var body) || body.linearVelocity.magnitude >= _restSpeed
                    || !IsInArea(log.transform.position))
                    continue;

                var slot = NearestFreeSlot(log.transform.position, false);
                if (slot < 0)
                    return;

                log.Pin(this, slot);
                _occupied[slot] = true;
                if (Telemetry.Enabled) LogWood("log_in_zone", log, "snap", null);
            }
        }

        // The log that just jumped out must not snap straight back in; the camper has to pick it up and place it.
        private bool IsEjectedAndLoose(Carryable log)
        {
            if (_ejectedLog != log)
                return false;

            if (log.HolderCount > 0 || !IsInArea(log.transform.position))
            {
                _ejectedLog = null;
                return false;
            }

            return true;
        }

        private bool IsInArea(Vector3 position, float tolerance = 0f)
        {
            var offset = position - transform.position;
            return new Vector2(offset.x, offset.z).magnitude <= _areaRadius + tolerance && Mathf.Abs(offset.y) < AreaHeight + tolerance;
        }

        private void RefreshOccupied()
        {
            System.Array.Clear(_occupied, 0, _occupied.Length);
            Carryable.GetPinnedTo(this, _pinned);
            foreach (var log in _pinned)
            {
                var slot = log.PinnedSlot;
                if (slot >= 0 && slot < _occupied.Length)
                    _occupied[slot] = true;
            }
        }

        // Closest free slot to the position, or -1. Pass refresh = false when the occupancy was just refreshed.
        private int NearestFreeSlot(Vector3 position, bool refresh)
        {
            if (refresh)
                RefreshOccupied();

            var best = -1;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < _slots.Length; i++)
            {
                var distance = (_slots[i].position - position).sqrMagnitude;
                if (!_occupied[i] && distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private void OnLogsChanged()
        {
            UpdateLabel();

            var logs = LogsInside;
            if (logs > _lastLogs)
            {
                _snapSfx.PlayAt(transform.position);
                if (logs >= _requiredWood && _lastLogs < _requiredWood)
                    _readySfx.PlayAt(transform.position);
            }
            else if (logs == _lastLogs - 1) // Several at once is a match reset, which stays quiet.
            {
                _unsnapSfx.PlayAt(transform.position);
            }

            _lastLogs = logs;
        }

        private void OnLitChanged()
        {
            UpdateLabel();
            UpdateFire();

            // OnChangedRender does not fire on spawn, so late joiners don't hear it. A match reset (not Playing) stays quiet.
            if (!MatchManager.AllowsInteraction)
                return;

            if (IsLit)
            {
                _lightSfx.PlayAt(transform.position);
                _igniteStart = Time.time;
                if (_ignitionSpark != null)
                    _ignitionSpark.Play();
            }
            else
            {
                if (_smokePuff != null)
                    _smokePuff.Play();
                if (_steam != null && _extinguishSteamBurst > 0)
                    _steam.Emit(_extinguishSteamBurst);
            }
        }

        // The pour timer starts (hiss) and stops (the label and flames follow DouseFraction on their own in Render).
        private void OnPourChanged()
        {
            UpdateLabel();
            if (PourTimer.IsRunning)
                _pourSfx.PlayAt(transform.position);
        }

        // Only a rise is a fire being put out; the count going back to 0 is a match reset, which stays quiet.
        private void OnExtinguishCountChanged()
        {
            if (ExtinguishCount > _lastExtinguishCount)
                _extinguishSfx.PlayAt(transform.position);

            _lastExtinguishCount = ExtinguishCount;
        }

        private void UpdateLabel()
        {
            if (_amountLabel == null)
                return;

            _labelPercent = DousePercent;
            if (_labelPercent >= 0)
                _amountLabel.SetText("¡Apagando! {0:0}%", _labelPercent); // SetText with a format doesn't allocate a string.
            else if (IsLit)
                _amountLabel.SetText("¡Encendida!");
            else
                _amountLabel.SetText("{0:0}/{1:0}", LogsInside, _requiredWood);
        }

        // Percentage shown while the fire is being put out; -1 when it is not.
        private int DousePercent => IsBeingDoused ? Mathf.RoundToInt(DouseFraction * 100f) : -1;

        // The pour progress is derived from a timer, so follow it here; text is only rebuilt when the shown value moves.
        private void RefreshDouseLabel()
        {
            if (_amountLabel != null && DousePercent != _labelPercent)
                UpdateLabel();
        }

        private void UpdateFire()
        {
            if (_fire != null && _fire.activeSelf != IsLit)
                _fire.SetActive(IsLit);
        }

        // The flames shrink as the water is poured.
        private void UpdateFireScale()
        {
            if (_fire == null)
                return;

            var target = Mathf.Lerp(1f, DouseFireScale, DouseLook);
            _fireFactor = Mathf.MoveTowards(_fireFactor, target, FireScaleSmoothing * Time.deltaTime);
            _fire.transform.localScale = _fireScale * (_fireFactor * Mathf.Lerp(_igniteStartScale, 1f, IgniteProgress));
        }

        // 0 right when the fire is lit, 1 after the ignition time (ease-out, so the flame jumps and then settles).
        private float IgniteProgress
        {
            get
            {
                var t = Mathf.Clamp01((Time.time - _igniteStart) / _igniteTime);
                return 1f - (1f - t) * (1f - t);
            }
        }

        // Light, embers, steam and hiss, all derived from replicated state (lit, logs, pour) and local time.
        private void UpdateFeel()
        {
            bool lit = IsLit;
            var douse = DouseLook;
            var woodFraction = Mathf.Clamp01(LogsInside / (float)_requiredWood);

            UpdateLight(lit, douse, woodFraction);

            if (_embers != null)
            {
                var emission = _embers.emission;
                emission.rateOverTime = lit ? 0f : Mathf.Lerp(_embersRate.x, _embersRate.y, woodFraction);
                var scale = Mathf.Lerp(_embersScale.x, _embersScale.y, woodFraction);
                _emberLevel = Mathf.MoveTowards(_emberLevel, scale, 2f * Time.deltaTime);
                _embers.transform.localScale = Vector3.one * _emberLevel;
            }

            if (_steam != null)
            {
                var emission = _steam.emission;
                emission.rateOverTime = _steamMaxRate * douse;
            }

            if (_hissLoop != null)
                UpdateHiss(douse);
        }

        private void UpdateLight(bool lit, float douse, float woodFraction)
        {
            if (_fireLight == null)
                return;

            float intensity, reach;
            if (lit)
            {
                var burn = IgniteProgress * Mathf.Lerp(1f, _douseLightFloor, douse);
                intensity = _lightIntensity.z * burn;
                reach = _lightRange.z * Mathf.Lerp(0.5f, 1f, burn);
            }
            else
            {
                intensity = Mathf.Lerp(_lightIntensity.x, _lightIntensity.y, woodFraction);
                reach = Mathf.Lerp(_lightRange.x, _lightRange.y, woodFraction);
            }

            _lightLevel = Mathf.MoveTowards(_lightLevel, intensity, 8f * Time.deltaTime * Mathf.Max(1f, _lightIntensity.z));
            _lightReach = Mathf.MoveTowards(_lightReach, reach, 10f * Time.deltaTime * Mathf.Max(1f, _lightRange.z));
            var noise = (Mathf.PerlinNoise(_flickerSeed, Time.time * _flickerSpeed) - 0.5f) * 2f;
            var on = _lightLevel > 0.01f;
            if (_fireLight.enabled != on)
                _fireLight.enabled = on;
            _fireLight.intensity = _lightLevel * (1f + noise * _flickerAmount);
            _fireLight.range = _lightReach * (1f + noise * _flickerAmount * 0.5f);
        }

        // The hiss grows with the pour and stops with it; a late joiner mid-pour just hears the current stretch.
        private void UpdateHiss(float douse)
        {
            if (douse > 0.001f)
            {
                if (!_hissLoop.isPlaying)
                    _hissLoop.Play();
                _hissLoop.volume = _hissMaxVolume * douse;
                _hissLoop.pitch = Mathf.Lerp(_hissPitch.x, _hissPitch.y, douse);
            }
            else if (_hissLoop.isPlaying)
            {
                _hissLoop.Stop();
            }
        }

        // Pose of a slot relative to the fireplace: the log leans with its top towards the centre.
        private void GetSlotPose(int index, out Vector3 position, out Quaternion rotation)
        {
            var count = Mathf.Max(1, _requiredWood);
            var radial = Quaternion.Euler(0f, _slotAngleOffset + index * 360f / count, 0f) * Vector3.forward;
            var tilt = _slotTiltFromVertical * Mathf.Deg2Rad;
            var axis = Vector3.up * Mathf.Cos(tilt) - radial * Mathf.Sin(tilt);
            position = radial * _slotRadius + Vector3.up * _slotHeight;
            rotation = Quaternion.FromToRotation(Vector3.right, axis) * Quaternion.Euler(_slotRotationOffset);
        }

        // Mesh of the log and where it sits inside its prefab, so ghosts and gizmos match the real log.
        private bool TryGetLogVisual(out Mesh mesh, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            mesh = null;
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;
            if (_logPrefab == null)
                return false;

            var filter = _logPrefab.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || filter.sharedMesh == null)
                return false;

            var root = _logPrefab.transform;
            mesh = filter.sharedMesh;
            position = root.InverseTransformPoint(filter.transform.position);
            rotation = Quaternion.Inverse(root.rotation) * filter.transform.rotation;
            scale = filter.transform.lossyScale;
            return true;
        }

        private void BuildGhosts()
        {
            _ghosts = new Transform[_slots.Length];
            _ghostRenderers = new Renderer[_slots.Length];
            if (_ghostMaterial == null || !TryGetLogVisual(out var mesh, out var position, out var rotation, out var scale))
                return;

            _block = new MaterialPropertyBlock();
            _ghostScale = scale;
            for (var i = 0; i < _slots.Length; i++)
            {
                var ghost = new GameObject("Ghost");
                ghost.transform.SetParent(_slots[i], false);
                ghost.transform.SetLocalPositionAndRotation(position, rotation);
                ghost.transform.localScale = scale;
                ghost.AddComponent<MeshFilter>().sharedMesh = mesh;

                var meshRenderer = ghost.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = _ghostMaterial;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                _ghosts[i] = ghost.transform;
                _ghostRenderers[i] = meshRenderer;
            }
        }

        public override void Render()
        {
            UpdateFire();
            RefreshDouseLabel();
            UpdateFireScale();
            UpdateFeel();
            if (_block == null)
                return;

            var hinted = FindHintedSlot();
            if (hinted < 0)
                RefreshOccupied(); // A hint already refreshed it.

            // A log of the local player that is already flying to a slot (predicted) hides that slot's ghost.
            var predictedSlot = -1;
            var localPlayer = PlayerInteractor.Local;
            var flying = localPlayer != null ? localPlayer.Carried : null;
            if (flying != null)
                flying.TryGetPredictedPin(this, out predictedSlot);
            var pulse = Mathf.Sin(Time.time * HintPulseHz * Mathf.PI * 2f) * 0.5f + 0.5f;
            for (var i = 0; i < _ghostRenderers.Length; i++)
            {
                var show = !_occupied[i] && i != predictedSlot;
                if (_ghostRenderers[i].enabled != show)
                    _ghostRenderers[i].enabled = show;
                if (!show)
                    continue;

                var isHint = i == hinted;
                var color = _ghostColor;
                if (isHint)
                    color.a = Mathf.Lerp(_hintAlpha.x, _hintAlpha.y, pulse);

                _block.SetColor(BaseColorId, color);
                _ghostRenderers[i].SetPropertyBlock(_block);
                _ghosts[i].localScale = isHint ? _ghostScale * HintScale : _ghostScale;
            }
        }

        // The slot the local player's carried log would snap into, or -1.
        private int FindHintedSlot()
        {
            var local = PlayerInteractor.Local;
            if (local == null || !CanPlace(local))
                return -1;

            // The log is already flying to its slot (predicted): stop pulsing the hint.
            var carried = local.Carried;
            return carried.HasPrediction ? -1 : NearestFreeSlot(carried.transform.position, true);
        }

        private void OnDrawGizmos()
        {
            var hasMesh = TryGetLogVisual(out var mesh, out var visualPosition, out var visualRotation, out var visualScale);
            Gizmos.color = new Color(0.3f, 0.65f, 1f, 0.8f);
            for (var i = 0; i < Mathf.Max(1, _requiredWood); i++)
            {
                GetSlotPose(i, out var position, out var rotation);
                var worldPosition = transform.TransformPoint(position);
                var worldRotation = transform.rotation * rotation;
                if (hasMesh)
                {
                    Gizmos.DrawWireMesh(mesh, worldPosition + worldRotation * visualPosition,
                        worldRotation * visualRotation, visualScale);
                }
                else
                {
                    var half = worldRotation * Vector3.right * 0.35f;
                    Gizmos.DrawLine(worldPosition - half, worldPosition + half);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, _areaRadius);
        }
    }
}
