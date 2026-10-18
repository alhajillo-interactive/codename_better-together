using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Combat;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Match;
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
    /// Only the camper places logs, and a placed log can't be taken out again. Gnomes put a lit fire out by mashing interact;
    /// the camper then lights it again with the matches.
    /// Master-client object: the state authority snaps logs and validates requests.
    /// </summary>
    public class FireplaceManager : NetworkBehaviour, IInteractable, IPinSpot, IMatchResettable
    {
        private const int MaxColliders = 64;
        private const float AreaHeight = 1.5f;
        private const float PlaceFovPunch = 1.5f;
        private const float PlaceFovPunchTime = 0.1f;
        private const float HintPulseHz = 3f;
        private const float HintScale = 1.05f;
        private const float DousePunchFov = 0.5f;
        private const float DousePunchTime = 0.07f;
        private const float DouseSfxInterval = 0.1f;
        private const float DouseFireScale = 0.35f;
        private const float FireScaleSmoothing = 12f;
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
        [Tooltip("A gnome must be closer than this to the fireplace (horizontally) to put it out.")]
        [SerializeField, Min(0f)] private float _douseRange = 3.5f;
        [Tooltip("Presses needed to put the fire out the first time.")]
        [SerializeField, Min(1)] private int _dousePressesBase = 18;
        [Tooltip("Extra presses needed for every time the fire has already been put out this match.")]
        [SerializeField, Min(0)] private int _dousePressesPerExtinguish = 6;
        [Tooltip("The presses needed never go above this.")]
        [SerializeField, Min(1)] private int _dousePressesMax = 36;
        [Tooltip("Presses from the same gnome closer together than this (s) are ignored, to blunt autoclickers.")]
        [SerializeField, Min(0f)] private float _minPressInterval = 0.07f;
        [Tooltip("Seconds without a press before the progress starts to fall back.")]
        [SerializeField, Min(0f)] private float _douseDecayDelay = 0.5f;
        [Tooltip("Presses per second the progress falls once it decays.")]
        [SerializeField, Min(0f)] private float _douseDecayPerSecond = 5f;
        [Tooltip("Seconds after the fire is lit during which it can't be put out.")]
        [SerializeField, Min(0f)] private float _litGraceTime = 2f;
        [Tooltip("Heard by everyone, from the fireplace, when the fire is put out.")]
        [SerializeField] private SoundCue _extinguishSfx;
        [Tooltip("Heard by everyone, from the fireplace, as a gnome press is counted (at most about 10 per second).")]
        [SerializeField] private SoundCue _douseSfx;

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

        private readonly Collider[] _results = new Collider[MaxColliders];
        private readonly HashSet<FireplaceWood> _seen = new();
        private readonly List<Carryable> _pinned = new();
        private Transform[] _slots;
        private Transform[] _ghosts;
        private Renderer[] _ghostRenderers;
        private bool[] _occupied;
        private Vector3 _ghostScale = Vector3.one;
        private MaterialPropertyBlock _block;
        private readonly Dictionary<NetworkBehaviourId, float> _lastPress = new();
        private int _lastLogs;
        private int _lastExtinguishCount;
        private float _lastValidPress = float.NegativeInfinity;
        private float _lastDouseSfx = float.NegativeInfinity;
        private float _lastDouseProgress;
        private float _fireFactor = 1f;
        private Vector3 _fireScale = Vector3.one;
        private bool _isSpawned;

        [Networked, OnChangedRender(nameof(OnLogsChanged))]
        private int LogsInside { get; set; }

        [Networked, OnChangedRender(nameof(OnLitChanged))]
        public NetworkBool IsLit { get; private set; }

        [Networked, OnChangedRender(nameof(OnDouseProgressChanged))]
        private float DouseProgress { get; set; }

        [Networked, OnChangedRender(nameof(OnExtinguishCountChanged))]
        private int ExtinguishCount { get; set; }

        [Networked] private TickTimer LitGrace { get; set; }

        public static FireplaceManager Instance { get; private set; }

        public int RequiredWood => _requiredWood;
        public int WoodCount => _isSpawned ? LogsInside : 0;
        public bool HasEnoughWood => _isSpawned && LogsInside >= _requiredWood;

        /// <summary>Presses gnomes need to put the fire out now; grows each time it has been put out this match.</summary>
        public int RequiredDousePresses =>
            Mathf.Min(_dousePressesMax, _dousePressesBase + _dousePressesPerExtinguish * (_isSpawned ? ExtinguishCount : 0));

        /// <summary>How close the gnomes are to putting the fire out, 0 to 1.</summary>
        public float DouseFraction => _isSpawned && IsLit ? Mathf.Clamp01(DouseProgress / RequiredDousePresses) : 0f;

        /// <summary>True while gnomes are putting the fire out.</summary>
        public bool IsBeingDoused => _isSpawned && IsLit && DouseProgress > 0f;

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

            BuildGhosts();
        }

        public override void Spawned()
        {
            _isSpawned = true;
            Instance = this;
            _lastLogs = LogsInside;
            _lastExtinguishCount = ExtinguishCount;
            _lastDouseProgress = DouseProgress;
            UpdateLabel();
            UpdateFire();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Where the log resting in <paramref name="slot"/> is posed.</summary>
        public Transform GetPinPoint(int slot) => _slots != null && slot >= 0 && slot < _slots.Length ? _slots[slot] : transform;

        /// <summary>Authority only: the fire is out; logs are counted again from the slots.</summary>
        public void ResetForMatch()
        {
            IsLit = false;
            DouseProgress = 0f;
            ExtinguishCount = 0;
            LitGrace = default;
            _lastPress.Clear();
        }

        public string GetPrompt(PlayerInteractor interactor) =>
            interactor.Role == PlayerRole.Gnome ? "Apagar fogata (¡machaca E!)" : "Prender fogata";

        /// <summary>Mirrors the authority's rules using the replicated state.</summary>
        public bool CanInteract(PlayerInteractor interactor) => CanLight(interactor) || CanDouse(interactor, 0f);

        public void Interact(PlayerInteractor interactor)
        {
            if (interactor.Role == PlayerRole.Gnome)
            {
                RPC_RequestDouse(interactor.Id);
                if (CameraPunch.Instance != null)
                    CameraPunch.Instance.PunchFov(DousePunchFov, DousePunchTime);
                return;
            }

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

        // A free-handed gnome close enough to a lit fire can press to put it out.
        private bool CanDouse(PlayerInteractor who, float tolerance)
        {
            if (!_isSpawned || !MatchManager.AllowsInteraction || !IsLit || who.Role != PlayerRole.Gnome || who.Carried != null)
                return false;

            if ((who.TryGetComponent<Petrifiable>(out var petrifiable) && petrifiable.IsStone)
                || (who.TryGetComponent<Kickable>(out var kickable) && kickable.IsStunned))
                return false;

            var offset = who.transform.position - transform.position;
            return new Vector2(offset.x, offset.z).magnitude <= _douseRange + tolerance;
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
            RPC_RequestPlace(who.Id);
            if (CameraPunch.Instance != null)
                CameraPunch.Instance.PunchFov(PlaceFovPunch, PlaceFovPunchTime);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestLight(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanLight(who))
                return;

            IsLit = true;
            DouseProgress = 0f;
            LitGrace = TickTimer.CreateFromSeconds(Runner, _litGraceTime);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestDouse(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanDouse(who, _placeTolerance))
                return;

            if (LitGrace.IsRunning && !LitGrace.Expired(Runner))
                return;

            var now = Time.time;
            if (_lastPress.TryGetValue(interactorId, out var last) && now - last < _minPressInterval)
                return;

            _lastPress[interactorId] = now;
            _lastValidPress = now;
            DouseProgress += 1f;
            if (DouseProgress < RequiredDousePresses)
                return;

            IsLit = false;
            DouseProgress = 0f;
            ExtinguishCount++;
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
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            if (MatchManager.AllowsInteraction)
                SnapRestingLogs();
            Carryable.GetPinnedTo(this, _pinned);
            LogsInside = _pinned.Count;

            if (IsLit && !HasEnoughWood)
                IsLit = false;

            if (!IsLit)
                DouseProgress = 0f;
            else if (DouseProgress > 0f && Time.time - _lastValidPress > _douseDecayDelay)
                DouseProgress = Mathf.Max(0f, DouseProgress - _douseDecayPerSecond * Runner.DeltaTime);
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

                if (!wood.TryGetComponent<Carryable>(out var log) || !log.WasHandled || !log.LastHeldByCamper || log.FreeTime < _settleTime || log.HolderCount > 0 || log.IsPinned || log.Inert
                    || !wood.TryGetComponent<Rigidbody>(out var body) || body.linearVelocity.magnitude >= _restSpeed
                    || !IsInArea(log.transform.position))
                    continue;

                var slot = NearestFreeSlot(log.transform.position, false);
                if (slot < 0)
                    return;

                log.Pin(this, slot);
                _occupied[slot] = true;
            }
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
        }

        private void OnDouseProgressChanged()
        {
            UpdateLabel();

            var progress = DouseProgress;
            if (progress > _lastDouseProgress && Time.time - _lastDouseSfx >= DouseSfxInterval)
            {
                _lastDouseSfx = Time.time;
                _douseSfx.PlayAt(transform.position);
            }

            _lastDouseProgress = progress;
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

            if (IsBeingDoused)
                _amountLabel.text = $"¡Apagando! {Mathf.RoundToInt(DouseFraction * 100f)}%";
            else
                _amountLabel.text = IsLit ? "¡Encendida!" : $"{LogsInside}/{_requiredWood}";
        }

        private void UpdateFire()
        {
            if (_fire != null && _fire.activeSelf != IsLit)
                _fire.SetActive(IsLit);
        }

        // The flames shrink as the gnomes get closer to putting the fire out.
        private void UpdateFireScale()
        {
            if (_fire == null)
                return;

            var target = Mathf.Lerp(1f, DouseFireScale, DouseFraction);
            _fireFactor = Mathf.MoveTowards(_fireFactor, target, FireScaleSmoothing * Time.deltaTime);
            _fire.transform.localScale = _fireScale * _fireFactor;
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
            UpdateFireScale();
            if (_block == null)
                return;

            var hinted = FindHintedSlot();
            if (hinted < 0)
                RefreshOccupied(); // A hint already refreshed it.
            var pulse = Mathf.Sin(Time.time * HintPulseHz * Mathf.PI * 2f) * 0.5f + 0.5f;
            for (var i = 0; i < _ghostRenderers.Length; i++)
            {
                var show = !_occupied[i];
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

            return NearestFreeSlot(local.Carried.transform.position, true);
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
