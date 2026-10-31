using BetterTogether.Audio;
using BetterTogether.Combat;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Networking;
using BetterTogether.Player;
using BetterTogether.UI;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Fireplace
{
    /// <summary>
    /// The water balloon of a gnome (levels without a <see cref="WaterBucket"/>). A gnome with empty hands fills one at a
    /// <see cref="WaterSource"/>, carries it for a while and throws it in an arc at the campfire.
    /// Player object: its owner is the state authority and the only one who writes. A throw is a replicated event (sequence,
    /// origin, velocity, tick, flight time): nobody spawns a network object, every peer draws the arc from that data. The owner
    /// finds where it lands with raycasts and, when the flight time is over, asks the fireplace's authority to apply the hit.
    /// </summary>
    [DefaultExecutionOrder(100)] // Its LateUpdate poses the balloon after the camera and the carry anchor have moved.
    public class BalloonThrower : NetworkBehaviour
    {
        private const int AimPoints = 12;
        private const int RingPoints = 24;
        private const float StepSeconds = 0.05f;
        private const float MaxFlightSeconds = 3f;
        private const float OriginDistance = 0.4f;
        private const float HitSlackSeconds = 0.25f;
        private const float HitSlackMetres = 1.5f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CullId = Shader.PropertyToID("_Cull");

        [Header("Rules")]
        [Tooltip("Seconds a filled balloon lasts in the hand before it bursts by itself.")]
        [SerializeField, Min(1f)] private float _lifeSeconds = 30f;
        [Tooltip("Launch speed of a thrown balloon (m/s). About 8 m of range with the gravity below.")]
        [SerializeField, Min(1f)] private float _throwSpeed = 9f;
        [Tooltip("Degrees added upwards to the view direction when throwing.")]
        [SerializeField, Range(0f, 45f)] private float _throwUpAngle = 12f;
        [Tooltip("Gravity of the balloon in flight (m/s2).")]
        [SerializeField, Min(0.1f)] private float _gravity = 9.81f;
        [Tooltip("Layers a balloon lands on (Environment). Not GnomeOnly, not players.")]
        [SerializeField] private LayerMask _impactMask = 1 << 11;
        [Tooltip("Seconds that must pass between two throws of the same gnome.")]
        [SerializeField, Min(0f)] private float _minThrowInterval = 0.6f;

        [Header("Presentation")]
        [Tooltip("Shader of the generated balloon, outline, aim line and splash (URP Unlit). The builder assigns it so builds keep it.")]
        [SerializeField] private Shader _shader;
        [Tooltip("Diameter of the balloon (m).")]
        [SerializeField, Min(0.02f)] private float _balloonDiameter = 0.13f;
        [Tooltip("Outline sphere size relative to the balloon.")]
        [SerializeField, Min(1f)] private float _outlineScale = 1.15f;
        [Tooltip("Where the owner sees the balloon, in the camera's space (right, up, forward). Other players see it at the carry anchor.")]
        [SerializeField] private Vector3 _ownerHandOffset = new(0.18f, -0.22f, 0.45f);
        [Tooltip("Radius of the landing ring of the aim line (m).")]
        [SerializeField, Min(0.05f)] private float _ringRadius = 0.35f;
        [Tooltip("Width of the aim line (m).")]
        [SerializeField, Min(0.005f)] private float _lineWidth = 0.03f;
        [Tooltip("Particles of the splash when a balloon lands or bursts.")]
        [SerializeField, Min(0)] private int _splashCount = 18;
        [Tooltip("Fraction of the life left under which the balloon in the hand wobbles to warn it is about to burst.")]
        [SerializeField, Range(0f, 1f)] private float _warnFraction = 0.2f;
        [Tooltip("Heard by everyone when a balloon is filled.")]
        [SerializeField] private SoundCue _fillSfx;
        [Tooltip("Heard by everyone when a balloon is thrown.")]
        [SerializeField] private SoundCue _throwSfx;
        [Tooltip("Heard by everyone where a thrown balloon lands.")]
        [SerializeField] private SoundCue _splashSfx;
        [Tooltip("Heard by everyone when a balloon bursts in the hand.")]
        [SerializeField] private SoundCue _popSfx;

        private PlayerCharacter _character;
        private PlayerInteractor _interactor;
        private PlayerMovement _movement;
        private GnomeStack _stack;
        private Petrifiable _petrifiable;
        private Kickable _kickable;
        private GnomeCapture _capture;
        private InputAction _attack;
        private bool _isSpawned;

        // Owner only: input edges read in Update and consumed in FixedUpdateNetwork.
        private bool _fillRequested;
        private bool _throwRequested;
        // Owner only: the hit of the last throw, sent when its flight time is over (never networked; a player who leaves sends none).
        private bool _hitPending;
        private Vector3 _hitPoint;
        // Fireplace authority only: the last throw of this gnome that was accepted, so a repeated RPC counts once.
        private bool _hasAcceptedHit;
        private byte _acceptedHitSeq;

        // Presentation only (never networked).
        private bool _renderInit;
        private bool _lastHas;
        private byte _lastSeq;
        private bool _flying;
        private Transform _handRoot;
        private Transform _projectile;
        private LineRenderer _aimLine;
        private LineRenderer _ring;
        private ParticleSystem _splash;
        private Material _fillMaterial;
        private Material _outlineMaterial;
        private Material _lineMaterial;
        private Material _splashMaterial;
        private readonly Vector3[] _aimBuffer = new Vector3[AimPoints];
        private readonly Vector3[] _ringBuffer = new Vector3[RingPoints];

        [Networked] private NetworkBool Has { get; set; }

        // Anchor + tick (CLAUDE.md rule 4): the tick when the balloon was filled; its remaining life is derived from it.
        [Networked] private int FilledTick { get; set; }

        // A throw is an event with data, not a spawned object (rule 4 and 6): every peer draws the arc from these.
        [Networked] private byte ThrowSeq { get; set; }
        [Networked] private Vector3 ThrowOrigin { get; set; }
        [Networked] private Vector3 ThrowVelocity { get; set; }
        [Networked] private int ThrowTick { get; set; }
        [Networked] private float ThrowFlight { get; set; }

        /// <summary>The local player's thrower (set on its owner only).</summary>
        public static BalloonThrower Local { get; private set; }

        /// <summary>True while this gnome holds a filled balloon. False when not spawned.</summary>
        public bool HasBalloon => _isSpawned && Has;

        /// <summary>Whether the level uses balloons: they replace the bucket, so a scene that has a bucket does not.</summary>
        public bool Active => WaterBucket.InScene == 0;

        /// <summary>True for a gnome (the camper cannot throw, and its hits are refused).</summary>
        public bool IsGnome => _character != null && _character.Role == PlayerRole.Gnome;

        /// <summary>Life left of the balloon in the hand, 1 (just filled) to 0 (bursts now). Presentation only; 0 without balloon.</summary>
        public float LifeFraction => HasBalloon ? 1f - Mathf.Clamp01(NetAnchor.Elapsed(Runner, FilledTick) / _lifeSeconds) : 0f;

        /// <summary>Where the last throw lands, from the replicated throw data (same value on every peer).</summary>
        private Vector3 Landing => PositionAt(ThrowOrigin, ThrowVelocity, ThrowFlight);

        /// <summary>
        /// Owner only. Mirrors what the authority (the owner itself) checks before filling, and is what the prompt shows:
        /// a gnome with empty hands, free to act, standing at the water.
        /// </summary>
        public bool CanFill
        {
            get
            {
                if (!_isSpawned || !HasStateAuthority || Has || !Active || !MatchManager.AllowsInteraction || !IsGnome)
                    return false;

                if (_interactor.CarriedOrPredicted != null || IsDisabled || (_stack != null && _stack.IsRiding)
                    || (_movement != null && _movement.IsFrozen))
                    return false;

                return WaterSource.IsNearAny(transform.position, 0f);
            }
        }

        // Stone (photographed), stunned (kicked) or captured: the balloon bursts and nothing can be filled or thrown.
        private bool IsDisabled =>
            (_petrifiable != null && _petrifiable.IsStone)
            || (_kickable != null && _kickable.IsStunned)
            || (_capture != null && _capture.IsCaptured);

        private void Awake()
        {
            TryGetComponent(out _character);
            TryGetComponent(out _interactor);
            TryGetComponent(out _movement);
            TryGetComponent(out _stack);
            TryGetComponent(out _petrifiable);
            TryGetComponent(out _kickable);
            TryGetComponent(out _capture);
            BuildPresentation();
        }

        private void OnDestroy()
        {
            if (_handRoot != null)
                Destroy(_handRoot.gameObject);
            if (_projectile != null)
                Destroy(_projectile.gameObject);
            DestroyMaterial(_fillMaterial);
            DestroyMaterial(_outlineMaterial);
            DestroyMaterial(_lineMaterial);
            DestroyMaterial(_splashMaterial);
        }

        public override void Spawned()
        {
            _isSpawned = true;
            _renderInit = false; // Late joiners only take the current sequence, they do not replay old throws.
            _flying = false;
            _hitPending = false;
            _fillRequested = false;
            _throwRequested = false;
            _hasAcceptedHit = false;
            if (!HasStateAuthority)
                return;

            Local = this;
            _attack = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/Attack") : null;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            _hitPending = false; // A thrower who leaves with the balloon in the air sends no hit.
            if (Local == this)
                Local = null;
            SetHandVisible(false);
            SetProjectileVisible(false);
            SetAimVisible(false);
        }

        /// <summary>Owner only: asks to fill a balloon. The write happens in the next network tick, validated again.</summary>
        public bool TryFill()
        {
            if (!CanFill)
                return false;

            _fillRequested = true;
            return true;
        }

        // Input is per frame: only flags are kept here, FixedUpdateNetwork decides and writes (same pattern as CamperGaze).
        private void Update()
        {
            if (!_isSpawned || Local != this || _attack == null)
                return;

            if (Has && _attack.WasPressedThisFrame() && Cursor.lockState == CursorLockMode.Locked && !FirstPersonCamera.IsTyping)
                _throwRequested = true;
        }

        /// <summary>
        /// Owner only. Fills on a validated request, bursts the balloon when it can no longer be held (time, stone, stun,
        /// capture, match over, role change) and throws on a click. Proxies and a new owner just read the state.
        /// </summary>
        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            var fill = _fillRequested;
            var throwing = _throwRequested;
            _fillRequested = false;
            _throwRequested = false;

            if (Has)
            {
                if (!MatchManager.AllowsInteraction || !IsGnome || IsDisabled || NetAnchor.Elapsed(Runner, FilledTick) >= _lifeSeconds)
                    Has = false; // Bursts in the hand: peers see Has fall without a new ThrowSeq.
                else if (throwing && NetAnchor.Elapsed(Runner, ThrowTick) >= _minThrowInterval)
                    Throw();
            }
            else if (fill && CanFill)
            {
                FilledTick = Runner.Tick.Raw;
                Has = true;
            }

            TickHit();
        }

        private void Throw()
        {
            var view = FirstPersonCamera.Instance;
            if (view == null)
                return;

            AimFrom(view.transform, out var origin, out var velocity);
            ComputeFlight(origin, velocity, out var flight, out var point);

            ThrowOrigin = origin;
            ThrowVelocity = velocity;
            ThrowTick = Runner.Tick.Raw;
            ThrowFlight = flight;
            ThrowSeq = (byte)(ThrowSeq + 1);
            Has = false;
            _hitPoint = point;
            _hitPending = true;
        }

        // When the flight time is over, asks the fireplace's authority to apply the hit (it validates everything again).
        private void TickHit()
        {
            if (!_hitPending || NetAnchor.Elapsed(Runner, ThrowTick) < ThrowFlight)
                return;

            _hitPending = false;
            var fire = FireplaceManager.Instance;
            if (fire != null && MatchManager.AllowsInteraction)
                fire.RPC_BalloonHit(_hitPoint, Id);
        }

        /// <summary>
        /// Fireplace authority only: checks that this gnome made a throw that has landed at
        /// <paramref name="point"/> (from the replicated throw data) and that it was not counted yet.
        /// </summary>
        public bool TryAcceptHit(Vector3 point)
        {
            if (!_isSpawned || !IsGnome || ThrowTick == 0)
                return false;

            if (NetAnchor.Elapsed(Runner, ThrowTick) < ThrowFlight - HitSlackSeconds)
                return false;

            if ((Landing - point).sqrMagnitude > HitSlackMetres * HitSlackMetres)
                return false;

            if (_hasAcceptedHit && _acceptedHitSeq == ThrowSeq)
                return false;

            _hasAcceptedHit = true;
            _acceptedHitSeq = ThrowSeq;
            return true;
        }

        // ------------------------------------------------------------------ Trajectory (shared by the throw and the aim line)

        // Launch point and velocity for a throw along the view of the camera.
        private void AimFrom(Transform view, out Vector3 origin, out Vector3 velocity)
        {
            var direction = (Quaternion.AngleAxis(-_throwUpAngle, view.right) * view.forward).normalized;
            var eye = view.position;
            origin = eye + direction * OriginDistance;
            // Never start on the other side of a wall.
            if (Physics.Linecast(eye, origin, _impactMask, QueryTriggerInteraction.Ignore))
                origin = eye;

            velocity = direction * _throwSpeed;
        }

        private Vector3 PositionAt(Vector3 origin, Vector3 velocity, float time) =>
            origin + velocity * time + Vector3.down * (0.5f * _gravity * time * time);

        // Steps the parabola and returns the flight time up to the first thing it lands on (or the maximum flight).
        private void ComputeFlight(Vector3 origin, Vector3 velocity, out float flight, out Vector3 point)
        {
            var previous = origin;
            for (var t = StepSeconds; t <= MaxFlightSeconds + 0.0001f; t += StepSeconds)
            {
                var next = PositionAt(origin, velocity, t);
                var segment = next - previous;
                var length = segment.magnitude;
                if (length > 1e-4f && Physics.Raycast(previous, segment / length, out var hit, length, _impactMask, QueryTriggerInteraction.Ignore))
                {
                    flight = t - StepSeconds + StepSeconds * (hit.distance / length);
                    point = hit.point;
                    return;
                }

                previous = next;
            }

            flight = MaxFlightSeconds;
            point = previous;
        }

        // ------------------------------------------------------------------ Presentation

        // Runs on every peer from the replicated state: sounds and splashes are local, nothing is sent (CLAUDE.md rule 6).
        public override void Render()
        {
            if (!_isSpawned)
                return;

            var has = Has;
            var seq = ThrowSeq;
            if (!_renderInit)
            {
                _renderInit = true;
                _lastHas = has;
                _lastSeq = seq;
            }
            else
            {
                if (seq != _lastSeq)
                {
                    _lastSeq = seq;
                    _flying = true;
                    _throwSfx.PlayAt(ThrowOrigin);
                }
                else if (_lastHas && !has)
                {
                    _popSfx.PlayAt(HandPosition());
                    EmitSplash(HandPosition());
                }

                if (has && !_lastHas)
                    _fillSfx.PlayAt(HandPosition());

                _lastHas = has;
            }

            UpdateFlight();
        }

        private void UpdateFlight()
        {
            if (!_flying)
                return;

            var elapsed = NetAnchor.Elapsed(Runner, ThrowTick);
            var flight = ThrowFlight;
            if (elapsed >= flight)
            {
                _flying = false;
                SetProjectileVisible(false);
                var landing = PositionAt(ThrowOrigin, ThrowVelocity, flight);
                EmitSplash(landing);
                _splashSfx.PlayAt(landing);
                return;
            }

            if (_projectile == null)
                return;

            SetProjectileVisible(true);
            _projectile.position = PositionAt(ThrowOrigin, ThrowVelocity, elapsed);
        }

        // Poses the balloon in the hand and the aim line, after the camera and the carry anchor have moved.
        private void LateUpdate()
        {
            if (!_isSpawned)
                return;

            if (_handRoot == null)
                return; // No shader was found: the state still works, only the drawing is missing.

            var has = Has;
            SetHandVisible(has);
            var owner = Local == this;
            if (has)
            {
                var life = LifeFraction;
                var pulse = life < _warnFraction ? 1f + 0.08f * Mathf.Sin(Time.time * 20f) : 1f;
                _handRoot.position = HandPosition();
                _handRoot.localScale = Vector3.one * pulse;
            }

            if (owner && has)
                UpdateAimLine();
            else
                SetAimVisible(false);
        }

        // In front of the owner's view; for the others (and without a camera) at the carry anchor, which follows the body.
        private Vector3 HandPosition()
        {
            if (Local == this && FirstPersonCamera.Instance != null)
            {
                var view = FirstPersonCamera.Instance.transform;
                return view.position + view.right * _ownerHandOffset.x + view.up * _ownerHandOffset.y + view.forward * _ownerHandOffset.z;
            }

            var anchor = _interactor != null ? _interactor.CarryAnchor : null;
            return anchor != null ? anchor.position : transform.position + Vector3.up * 0.9f + transform.forward * 0.35f;
        }

        // Owner only: the parabola of the next throw (12 points) and a ring where it lands, in cream.
        private void UpdateAimLine()
        {
            var view = FirstPersonCamera.Instance;
            if (view == null || Cursor.lockState != CursorLockMode.Locked)
            {
                SetAimVisible(false);
                return;
            }

            AimFrom(view.transform, out var origin, out var velocity);
            ComputeFlight(origin, velocity, out var flight, out var point);
            for (var i = 0; i < AimPoints; i++)
                _aimBuffer[i] = PositionAt(origin, velocity, flight * i / (AimPoints - 1));

            _aimLine.SetPositions(_aimBuffer);

            var center = point + Vector3.up * 0.03f;
            for (var i = 0; i < RingPoints; i++)
            {
                var angle = i * Mathf.PI * 2f / RingPoints;
                _ringBuffer[i] = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _ringRadius;
            }

            _ring.SetPositions(_ringBuffer);
            SetAimVisible(true);
        }

        private void EmitSplash(Vector3 position)
        {
            if (_splash == null || _splashCount <= 0)
                return;

            _splash.transform.position = position;
            _splash.Emit(_splashCount);
        }

        private void SetHandVisible(bool visible)
        {
            if (_handRoot != null && _handRoot.gameObject.activeSelf != visible)
                _handRoot.gameObject.SetActive(visible);
        }

        private void SetProjectileVisible(bool visible)
        {
            if (_projectile != null && _projectile.gameObject.activeSelf != visible)
                _projectile.gameObject.SetActive(visible);
        }

        private void SetAimVisible(bool visible)
        {
            if (_aimLine != null && _aimLine.enabled != visible)
            {
                _aimLine.enabled = visible;
                _ring.enabled = visible;
            }
        }

        // Everything is generated here once (the Gnome prefab needs no children): balloon with outline, projectile (one per
        // thrower, reused by every throw), aim line, landing ring and splash.
        private void BuildPresentation()
        {
            var theme = UiTheme.Safe;
            var shader = _shader != null ? _shader : Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return;

            _fillMaterial = MakeMaterial(shader, theme.roleGnome, false);
            _outlineMaterial = MakeMaterial(shader, theme.ink, true);
            _lineMaterial = MakeMaterial(shader, theme.cream, false);
            _splashMaterial = MakeMaterial(shader, theme.roleGnome, false);

            _handRoot = BuildBalloon("BalloonHand");
            _projectile = BuildBalloon("BalloonProjectile");
            _handRoot.gameObject.SetActive(false);
            _projectile.gameObject.SetActive(false);

            _aimLine = BuildLine("BalloonAimLine", AimPoints, false);
            _ring = BuildLine("BalloonAimRing", RingPoints, true);
            _aimLine.enabled = false;
            _ring.enabled = false;

            BuildSplash();
        }

        private Transform BuildBalloon(string objectName)
        {
            var root = new GameObject(objectName).transform;
            root.SetParent(transform, false);
            BuildSphere(root, "Fill", _balloonDiameter, _fillMaterial);
            BuildSphere(root, "Outline", _balloonDiameter * _outlineScale, _outlineMaterial);
            root.SetParent(null, true); // Posed in world space each frame; destroyed with this component.
            root.gameObject.hideFlags = HideFlags.DontSave;
            return root;
        }

        private static void BuildSphere(Transform parent, string objectName, float diameter, Material material)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = objectName;
            Destroy(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(parent, false);
            sphere.transform.localScale = Vector3.one * diameter;
            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private LineRenderer BuildLine(string objectName, int points, bool loop)
        {
            var line = new GameObject(objectName).AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = points;
            line.widthMultiplier = _lineWidth;
            line.sharedMaterial = _lineMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private void BuildSplash()
        {
            var go = new GameObject("BalloonSplash");
            go.transform.SetParent(transform, false);
            _splash = go.AddComponent<ParticleSystem>();
            _splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _splash.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.55f;
            main.startSpeed = 2.5f;
            main.startSize = 0.08f;
            main.gravityModifier = 1f;
            main.maxParticles = 64;
            var emission = _splash.emission;
            emission.enabled = false; // Emit(count) is used.
            var shape = _splash.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = _splashMaterial;
        }

        private static Material MakeMaterial(Shader shader, Color color, bool cullFront)
        {
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (material.HasProperty(BaseColorId))
                material.SetColor(BaseColorId, color);
            else
                material.color = color;
            if (cullFront && material.HasProperty(CullId))
                material.SetFloat(CullId, 1f); // Front faces culled: only the rim of the larger sphere shows, as an outline.
            return material;
        }

        private static void DestroyMaterial(Material material)
        {
            if (material != null)
                Destroy(material);
        }

        private void OnDisable()
        {
            SetHandVisible(false);
            SetProjectileVisible(false);
            SetAimVisible(false);
        }
    }
}
