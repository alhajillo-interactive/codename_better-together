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

        private Transform _target;
        private InputAction _look;
        private float _pitch;
        private bool _wasCursorFree;
        private PlayerMovement _targetMovement;
        private float _standingEyeHeight;
        private float _eyeDrop;
        private float _eyeDropVelocity;

        public float Yaw { get; private set; }
        public float Pitch => _pitch;

        /// <summary>Multiplies the mouse sensitivity, e.g. lowered while the camper focuses.</summary>
        public float SensitivityScale { get; set; } = 1f;

        /// <summary>While true the cursor stays unlocked and mouse look is paused, e.g. while a debug panel is open.</summary>
        public static bool CursorFree { get; set; }

        /// <summary>True while an IMGUI text field has keyboard focus, so hotkeys must be ignored.</summary>
        public static bool IsTyping => GUIUtility.keyboardControl != 0;

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

            transform.SetPositionAndRotation(_target.position - Vector3.up * _eyeDrop, Quaternion.Euler(_pitch, Yaw, 0f));
        }

        private void ApplyLook()
        {
            _look ??= InputSystem.actions.FindAction("Player/Look");
            var delta = _look.ReadValue<Vector2>();

            var sensitivity = _mouseSensitivity * SensitivityScale;
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
