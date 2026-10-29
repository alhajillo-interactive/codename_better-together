using BetterTogether.Match;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Player
{
    /// <summary>
    /// Drives the scene's Cinemachine camera: mounts on the local player's camera anchor and handles mouse look.
    /// The Unity Camera renders through the CinemachineBrain, which adds effects such as impulse shake on top.
    /// Exposes <see cref="Yaw"/> so movement can walk relative to where the player is looking;
    /// gameplay raycasts use this transform, so shake never affects them.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class FirstPersonCamera : MonoBehaviour
    {
        public static FirstPersonCamera Instance { get; private set; }

        [Tooltip("Degrees per pixel of mouse delta.")]
        [SerializeField] private float _mouseSensitivity = 0.1f;
        [SerializeField] private float _maxPitch = 85f;
        [Tooltip("Seconds the eye takes to move between standing and crouched height.")]
        [SerializeField, Min(0.01f)] private float _crouchEyeSmoothTime = 0.15f;

        [Header("Third person (garden-gnome disguise)")]
        [Tooltip("Distance (m) behind the eye while the local gnome is disguised as an ornament, so it sees itself and around.")]
        [SerializeField, Min(0f)] private float _thirdPersonDistance = 1.1f;
        [Tooltip("Height (m) added above the eye in third person.")]
        [SerializeField] private float _thirdPersonHeight = 0.25f;
        [Tooltip("Seconds to move between first and third person.")]
        [SerializeField, Min(0.01f)] private float _thirdPersonBlendTime = 0.25f;
        [Tooltip("Radius (m) of the camera's collision probe, so it never ends inside walls or scenery.")]
        [SerializeField, Min(0.01f)] private float _thirdPersonProbeRadius = 0.08f;
        [Tooltip("What the third-person camera collides with.")]
        [SerializeField] private LayerMask _thirdPersonBlockers;

        private Transform _target;
        private InputAction _look;
        private float _pitch;
        private bool _wasCursorFree;
        private PlayerMovement _targetMovement;
        private float _standingEyeHeight;
        private float _eyeDrop;
        private float _eyeDropVelocity;
        private GardenDisguise _targetDisguise;
        private LocalPlayerView _targetView;
        private float _thirdPerson;

        public float Yaw { get; private set; }
        public float Pitch => _pitch;

        /// <summary>Movement of the local player this camera is mounted on, or null while it follows nobody.</summary>
        public PlayerMovement TargetMovement => _targetMovement;

        /// <summary>Multiplies the mouse sensitivity, e.g. lowered while the camper focuses.</summary>
        public float SensitivityScale { get; set; } = 1f;

        public const string SensitivityPrefsKey = "gm_sens";

        // -1 = not read yet. Player setting from the settings panel, multiplies the sensitivity above; 1 = unchanged.
        private static float _sensitivityMultiplier = -1f;

        /// <summary>Player mouse sensitivity multiplier (saved in PlayerPrefs «gm_sens»), 1 by default.</summary>
        public static float SensitivityMultiplier
        {
            get
            {
                if (_sensitivityMultiplier < 0f)
                    _sensitivityMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityPrefsKey, 1f), 0.1f, 5f);
                return _sensitivityMultiplier;
            }
            set
            {
                _sensitivityMultiplier = Mathf.Clamp(value, 0.1f, 5f);
                PlayerPrefs.SetFloat(SensitivityPrefsKey, _sensitivityMultiplier);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _sensitivityMultiplier = -1f;

        /// <summary>While true the cursor stays unlocked and mouse look is paused, e.g. while a debug panel is open.</summary>
        public static bool CursorFree { get; set; }

        /// <summary>True while an IMGUI text field has keyboard focus, so hotkeys must be ignored.</summary>
        public static bool IsTyping => GUIUtility.keyboardControl != 0 || UiTyping;

        /// <summary>Set by uGUI text fields (survey, settings) while they have focus, so gameplay keys are ignored.</summary>
        public static bool UiTyping { get; set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Follow(Transform anchor)
        {
            _target = anchor;
            _targetMovement = anchor.GetComponentInParent<PlayerMovement>();
            _standingEyeHeight = _targetMovement != null ? anchor.position.y - _targetMovement.transform.position.y : 0f;
            _eyeDrop = 0f;
            _eyeDropVelocity = 0f;
            _targetDisguise = anchor.GetComponentInParent<GardenDisguise>();
            _targetView = anchor.GetComponentInParent<LocalPlayerView>();
            _thirdPerson = 0f;
            if (_thirdPersonBlockers.value == 0)
                _thirdPersonBlockers = LayerMask.GetMask("Environment", "Occluder");
            Yaw = anchor.eulerAngles.y;
            _pitch = 0f;
            SetCursorLocked(true);
        }

        /// <summary>Turns the view to face <paramref name="yaw"/> degrees (e.g. after a teleport).</summary>
        public void SetYaw(float yaw)
        {
            Yaw = yaw;
        }

        public void Release(Transform anchor)
        {
            if (_target != anchor)
                return;

            _target = null;
            _targetMovement = null;
            _targetDisguise = null;
            _targetView = null;
            _thirdPerson = 0f;
            SetCursorLocked(false);
        }

        private void LateUpdate()
        {
            if (_target == null)
                return;

            var cursorLocked = Cursor.lockState == CursorLockMode.Locked;

            var cursorFree = CursorFree || MatchManager.InLobby;
            var menuClosed = _wasCursorFree && !cursorFree;
            _wasCursorFree = cursorFree;

            if (cursorFree)
            {
                if (cursorLocked)
                    SetCursorLocked(false);
            }
            else if (menuClosed)
                SetCursorLocked(true);
            else if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetCursorLocked(false);
            else if (!cursorLocked && Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
                SetCursorLocked(true);
            else if (cursorLocked)
                ApplyLook();

            var targetDrop = _targetMovement != null && _targetMovement.IsCrouching
                ? _targetMovement.CrouchEyeDrop(_standingEyeHeight)
                : 0f;
            _eyeDrop = Mathf.SmoothDamp(_eyeDrop, targetDrop, ref _eyeDropVelocity, _crouchEyeSmoothTime);

            var eye = _target.position - Vector3.up * _eyeDrop;
            var rotation = Quaternion.Euler(_pitch, Yaw, 0f);
            transform.SetPositionAndRotation(ThirdPersonOffset(eye, rotation), rotation);
        }

        // Orbits behind the eye while the local gnome is a garden ornament, blending in and out; first person otherwise.
        private Vector3 ThirdPersonOffset(Vector3 eye, Quaternion rotation)
        {
            var wanted = _targetDisguise != null && _targetDisguise.IsDisguised ? 1f : 0f;
            _thirdPerson = Mathf.MoveTowards(_thirdPerson, wanted, Time.deltaTime / _thirdPersonBlendTime);
            if (_targetView != null)
                _targetView.SetBodyVisible(_thirdPerson > 0.3f);
            if (_thirdPerson <= 0f)
                return eye;

            var t = _thirdPerson * _thirdPerson * (3f - 2f * _thirdPerson);
            var pivot = eye + Vector3.up * (_thirdPersonHeight * t);
            var back = rotation * Vector3.back;
            var distance = _thirdPersonDistance * t;
            if (Physics.SphereCast(pivot, _thirdPersonProbeRadius, back, out var hit, distance, _thirdPersonBlockers, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(0f, hit.distance - 0.02f);
            return pivot + back * distance;
        }

        private void ApplyLook()
        {
            _look ??= InputSystem.actions.FindAction("Player/Look");
            var delta = _look.ReadValue<Vector2>();

            var sensitivity = _mouseSensitivity * SensitivityScale * SensitivityMultiplier;
            Yaw += delta.x * sensitivity;
            _pitch = Mathf.Clamp(_pitch - delta.y * sensitivity, -_maxPitch, _maxPitch);
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
