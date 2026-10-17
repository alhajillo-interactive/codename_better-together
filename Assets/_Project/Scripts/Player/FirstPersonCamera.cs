using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Player
{
    /// <summary>
    /// Scene camera that mounts on the local player's camera anchor and handles mouse look.
    /// Exposes <see cref="Yaw"/> so movement can walk relative to where the player is looking.
    /// </summary>
    public class FirstPersonCamera : MonoBehaviour
    {
        public static FirstPersonCamera Instance { get; private set; }

        [Tooltip("Degrees per pixel of mouse delta.")]
        [SerializeField] private float _mouseSensitivity = 0.1f;
        [SerializeField] private float _maxPitch = 85f;

        private Transform _target;
        private InputAction _look;
        private float _pitch;

        public float Yaw { get; private set; }

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
            Yaw = anchor.eulerAngles.y;
            _pitch = 0f;
            SetCursorLocked(true);
        }

        public void Release(Transform anchor)
        {
            if (_target != anchor)
                return;

            _target = null;
            SetCursorLocked(false);
        }

        private void LateUpdate()
        {
            if (_target == null)
                return;

            var cursorLocked = Cursor.lockState == CursorLockMode.Locked;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetCursorLocked(false);
            else if (!cursorLocked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                SetCursorLocked(true);
            else if (cursorLocked)
                ApplyLook();

            transform.SetPositionAndRotation(_target.position, Quaternion.Euler(_pitch, Yaw, 0f));
        }

        private void ApplyLook()
        {
            _look ??= InputSystem.actions.FindAction("Player/Look");
            var delta = _look.ReadValue<Vector2>();

            Yaw += delta.x * _mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - delta.y * _mouseSensitivity, -_maxPitch, _maxPitch);
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
