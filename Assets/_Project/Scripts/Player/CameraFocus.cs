using Unity.Cinemachine;
using UnityEngine;

namespace BetterTogether.Player
{
    /// <summary>
    /// Local focus zoom for the camper: while focusing, the field of view narrows and mouse look slows down.
    /// It moves <see cref="CameraPunch.BaseFieldOfView"/>, so punches still add on top.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera), typeof(CameraPunch))]
    [DefaultExecutionOrder(-160)]
    public class CameraFocus : MonoBehaviour
    {
        [Tooltip("Field of view while focusing; the normal one is the camera lens.")]
        [SerializeField] private float _focusFov = 35f;
        [Tooltip("Seconds to ease between normal and focused.")]
        [SerializeField] private float _transitionTime = 0.2f;
        [Tooltip("Mouse sensitivity multiplier while focusing.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _focusSensitivity = 0.55f;

        private CameraPunch _punch;
        private float _normalFov;
        private float _blend;
        private bool _focused;

        public static CameraFocus Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            _punch = GetComponent<CameraPunch>();
            _normalFov = GetComponent<CinemachineCamera>().Lens.FieldOfView;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Starts or ends the focus zoom; call it every frame with the current state.</summary>
        public void SetFocused(bool focused) => _focused = focused;

        private void LateUpdate()
        {
            var wanted = _focused ? 1f : 0f;
            _blend = _transitionTime > 0f ? Mathf.MoveTowards(_blend, wanted, Time.deltaTime / _transitionTime) : wanted;

            var eased = 1f - (1f - _blend) * (1f - _blend);
            _punch.BaseFieldOfView = Mathf.Lerp(_normalFov, _focusFov, eased);
            if (FirstPersonCamera.Instance != null)
                FirstPersonCamera.Instance.SensitivityScale = Mathf.Lerp(1f, _focusSensitivity, eased);
        }
    }
}
