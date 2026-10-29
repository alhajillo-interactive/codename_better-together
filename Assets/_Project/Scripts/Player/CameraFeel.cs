using BetterTogether.Gaze;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Player
{
    /// <summary>
    /// Local-only camera feel on the scene's CinemachineCamera: head bob (Cinemachine Noise with one profile per role,
    /// gain driven by the local player's speed), a field-of-view widening while sprinting and a downward bump on landing.
    /// It adds the sprint FOV on top of <see cref="CameraPunch.BaseFieldOfView"/> after <see cref="CameraFocus"/> has set it
    /// (and fades it out while focusing), so the focus zoom wins and punches still add on top.
    /// Purely presentational and driven by the camera's target, which only the owning client sets: no networking at all.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera), typeof(CameraPunch))]
    [DefaultExecutionOrder(-155)]
    public class CameraFeel : MonoBehaviour
    {
        public const string BobPrefsKey = "gm_bob";

        [Header("Head bob")]
        [Tooltip("Noise profile used while playing the camper (about 2.5 cm at 1.8 Hz). Built by GnoMore/Construir feel de cámara.")]
        [SerializeField] private NoiseSettings _camperNoise;
        [Tooltip("Noise profile used while playing a gnome (about 1 cm at 3.5 Hz). Built by GnoMore/Construir feel de cámara.")]
        [SerializeField] private NoiseSettings _gnomeNoise;
        [Tooltip("Accessibility: scales the whole bob (0 = none, 1 = full). Overridden at startup by PlayerPrefs 'gm_bob' when present.")]
        [SerializeField, Range(0f, 1f)] private float _bobScale = 1f;
        [Tooltip("Bob gain while walking.")]
        [SerializeField, Min(0f)] private float _walkGain = 1f;
        [Tooltip("Bob gain while sprinting.")]
        [SerializeField, Min(0f)] private float _sprintGain = 1.3f;
        [Tooltip("Bob gain while crouched and moving.")]
        [SerializeField, Min(0f)] private float _crouchGain = 0.5f;
        [Tooltip("Seconds the bob gain takes to follow changes of state.")]
        [SerializeField, Min(0.01f)] private float _gainSmoothTime = 0.12f;
        [Tooltip("Horizontal speed (m/s) above which the player counts as moving.")]
        [SerializeField, Min(0f)] private float _moveSpeedThreshold = 0.15f;
        [Tooltip("Speed samples above this (m/s) are teleports or mount jumps and are ignored.")]
        [SerializeField, Min(1f)] private float _maxSpeedSample = 20f;

        [Header("Sprint FOV")]
        [Tooltip("Degrees added to the field of view while sprinting.")]
        [SerializeField] private float _sprintFovBonus = 6f;
        [Tooltip("Seconds to blend the sprint FOV in and out.")]
        [SerializeField, Min(0.01f)] private float _sprintFovBlendTime = 0.2f;

        [Header("Landing")]
        [Tooltip("Minimum fall height (m) that causes a landing bump.")]
        [SerializeField, Min(0.01f)] private float _landingMinDrop = 0.6f;
        [Tooltip("Downward camera bump (m) at the minimum fall height; it grows with the fall height.")]
        [SerializeField, Min(0f)] private float _landingPunch = 0.04f;
        [Tooltip("Cap of the landing bump, as a multiple of the base bump.")]
        [SerializeField, Min(1f)] private float _landingMaxScale = 3f;
        [Tooltip("Seconds the landing bump lasts (used when the impulse source is created at startup).")]
        [SerializeField, Min(0.05f)] private float _landingDuration = 0.25f;

        private CameraPunch _punch;
        private CinemachineBasicMultiChannelPerlin _noise;
        private CinemachineImpulseSource _impulse;
        private InputAction _sprint;
        private float _normalFov;

        private PlayerMovement _movement;
        private CharacterController _controller;
        private bool _isGnome;
        private Vector3 _lastPosition;
        private float _speed;
        private float _gain;
        private float _gainVelocity;
        private float _sprintBlend;
        private bool _airborne;
        private float _peakHeight;

        /// <summary>Accessibility scale of the head bob (0..1); the UI of phase 3 sets it and may persist it in PlayerPrefs "gm_bob".</summary>
        public float BobScale
        {
            get => _bobScale;
            set => _bobScale = Mathf.Clamp01(value);
        }

        private void Awake()
        {
            _punch = GetComponent<CameraPunch>();
            _normalFov = GetComponent<CinemachineCamera>().Lens.FieldOfView;
            _noise = GetComponent<CinemachineBasicMultiChannelPerlin>();
            if (_noise == null)
                Debug.LogWarning("[CameraFeel] The CinemachineCamera has no Basic Multi Channel Perlin; run GnoMore/Construir feel de cámara. No head bob.");
            else
                _noise.AmplitudeGain = 0f;

            if (PlayerPrefs.HasKey(BobPrefsKey))
                _bobScale = Mathf.Clamp01(PlayerPrefs.GetFloat(BobPrefsKey, 1f));

            _impulse = GetComponent<CinemachineImpulseSource>();
            if (_impulse == null)
            {
                _impulse = gameObject.AddComponent<CinemachineImpulseSource>();
                var definition = _impulse.ImpulseDefinition;
                definition.ImpulseChannel = 1;
                definition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
                definition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
                definition.ImpulseDuration = _landingDuration;
            }
        }

        private void LateUpdate()
        {
            var movement = FirstPersonCamera.Instance != null ? FirstPersonCamera.Instance.TargetMovement : null;
            if (movement != _movement)
                Bind(movement);

            var dt = Time.deltaTime;
            if (_movement == null || dt <= 0f)
            {
                Rest();
                return;
            }

            SampleSpeed(dt);
            var moving = _speed > _moveSpeedThreshold && !_movement.IsRiding;
            var crouching = _movement.IsCrouching;
            // A rider's controller is off: treat the ride as grounded so leaving the tower is not a fall.
            var grounded = _controller == null || _controller.isGrounded || _movement.IsRiding;
            if (_sprint == null && InputSystem.actions != null)
                _sprint = InputSystem.actions.FindAction("Player/Sprint");
            var sprinting = moving && !crouching && _sprint != null && _sprint.IsPressed();

            UpdateBob(moving && grounded, crouching, sprinting, dt);
            UpdateSprintFov(sprinting, dt);
            UpdateLanding(grounded);
        }

        // Called when the camera starts or stops following a player (also on role change after respawn).
        private void Bind(PlayerMovement movement)
        {
            _movement = movement;
            _controller = movement != null ? movement.GetComponent<CharacterController>() : null;
            _isGnome = movement != null && movement.GetComponent<Petrifiable>() != null;
            _speed = 0f;
            _airborne = false;
            _peakHeight = 0f;
            if (movement != null)
                _lastPosition = movement.transform.position;

            if (_noise != null)
                _noise.NoiseProfile = _isGnome ? _gnomeNoise : _camperNoise;
        }

        private void SampleSpeed(float dt)
        {
            var position = _movement.transform.position;
            var delta = position - _lastPosition;
            _lastPosition = position;
            delta.y = 0f;

            var sample = delta.magnitude / dt;
            if (sample <= _maxSpeedSample)
                _speed = sample;
        }

        private void UpdateBob(bool moving, bool crouching, bool sprinting, float dt)
        {
            if (_noise == null)
                return;

            var target = !moving ? 0f : crouching ? _crouchGain : sprinting ? _sprintGain : _walkGain;
            _gain = Mathf.SmoothDamp(_gain, target, ref _gainVelocity, _gainSmoothTime, Mathf.Infinity, dt);
            _noise.AmplitudeGain = _gain * _bobScale;
        }

        // CameraFocus runs earlier and has just set BaseFieldOfView; the sprint widening fades out as the focus zoom takes over.
        private void UpdateSprintFov(bool sprinting, float dt)
        {
            _sprintBlend = Mathf.MoveTowards(_sprintBlend, sprinting ? 1f : 0f, dt / _sprintFovBlendTime);
            ApplySprintFov();
        }

        private void ApplySprintFov()
        {
            var focus = CameraFocus.Instance;
            var eased = _sprintBlend * _sprintBlend * (3f - 2f * _sprintBlend);
            var bonus = _sprintFovBonus * eased * (1f - (focus != null ? focus.Amount : 0f));
            if (focus != null && focus.isActiveAndEnabled)
                _punch.BaseFieldOfView += bonus;
            else
                _punch.BaseFieldOfView = _normalFov + bonus;
        }

        private void UpdateLanding(bool grounded)
        {
            var height = _movement.transform.position.y;
            if (!grounded)
            {
                if (!_airborne)
                {
                    _airborne = true;
                    _peakHeight = height;
                }
                else if (height > _peakHeight)
                    _peakHeight = height;

                return;
            }

            if (!_airborne)
                return;

            _airborne = false;
            var drop = _peakHeight - height;
            if (drop < _landingMinDrop || _impulse == null)
                return;

            var scale = Mathf.Min(drop / _landingMinDrop, _landingMaxScale);
            _impulse.GenerateImpulseWithVelocity(Vector3.down * (_landingPunch * scale));
        }

        // Nobody to follow: silence the bob and keep the FOV at its base so nothing sticks from the last player.
        private void Rest()
        {
            _sprintBlend = 0f;
            _gain = 0f;
            _gainVelocity = 0f;
            if (_noise != null)
                _noise.AmplitudeGain = 0f;
            if (CameraFocus.Instance == null)
                _punch.BaseFieldOfView = _normalFov;
        }
    }
}
