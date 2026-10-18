using Unity.Cinemachine;
using UnityEngine;

namespace BetterTogether.Player
{
    /// <summary>
    /// Short additive kicks on the scene camera's field of view and roll, decaying with an ease-out.
    /// A new punch replaces the running one only if it is stronger than what is left of it.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    [DefaultExecutionOrder(-150)]
    public class CameraPunch : MonoBehaviour
    {
        private struct Punch
        {
            public float Amplitude;
            public float Duration;
            public float Elapsed;

            public float Value
            {
                get
                {
                    if (Duration <= 0f || Elapsed >= Duration)
                        return 0f;

                    var remaining = 1f - Elapsed / Duration;
                    return Amplitude * remaining * remaining;
                }
            }

            public void Start(float amplitude, float duration)
            {
                if (Mathf.Abs(amplitude) < Mathf.Abs(Value))
                    return;

                Amplitude = amplitude;
                Duration = duration;
                Elapsed = 0f;
            }
        }

        public static CameraPunch Instance { get; private set; }

        /// <summary>Field of view the punches add to; <see cref="CameraFocus"/> moves it for the focus zoom.</summary>
        public float BaseFieldOfView { get; set; }

        private CinemachineCamera _camera;
        private float _baseDutch;
        private Punch _fov;
        private Punch _roll;

        private void Awake()
        {
            Instance = this;
            _camera = GetComponent<CinemachineCamera>();
            BaseFieldOfView = _camera.Lens.FieldOfView;
            _baseDutch = _camera.Lens.Dutch;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Widens (positive) or narrows the field of view by <paramref name="degrees"/>, easing back to normal.</summary>
        public void PunchFov(float degrees, float duration) => _fov.Start(degrees, duration);

        /// <summary>Tilts the camera by <paramref name="degrees"/> (sign picks the side), easing back to level.</summary>
        public void PunchRoll(float degrees, float duration) => _roll.Start(degrees, duration);

        private void LateUpdate()
        {
            _fov.Elapsed += Time.deltaTime;
            _roll.Elapsed += Time.deltaTime;
            _camera.Lens.FieldOfView = BaseFieldOfView + _fov.Value;
            _camera.Lens.Dutch = _baseDutch + _roll.Value;
        }
    }
}
