using UnityEngine;

namespace BetterTogether.Environment
{
    /// <summary>
    /// Feeds the sun and moon directions to the BetterTogether/Skybox shader as global vectors.
    /// The sun light is resolved from the assigned field, then RenderSettings.sun.
    /// </summary>
    [ExecuteAlways]
    public class SkyboxController : MonoBehaviour
    {
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int MoonDirId = Shader.PropertyToID("_MoonDir");

        [Tooltip("Optional. Falls back to RenderSettings.sun.")]
        [SerializeField] private Light _sun;
        [Tooltip("Optional. If empty the moon sits opposite the sun.")]
        [SerializeField] private Transform _moon;

        private Light _cachedSun;

        private void LateUpdate()
        {
            if (_cachedSun == null)
                _cachedSun = ResolveSun();

            Vector3 sunDir = _cachedSun != null ? -_cachedSun.transform.forward : Vector3.up;
            Vector3 moonDir = _moon != null ? -_moon.forward : -sunDir;

            Shader.SetGlobalVector(SunDirId, sunDir);
            Shader.SetGlobalVector(MoonDirId, moonDir);
        }

        private Light ResolveSun()
        {
            if (_sun != null)
                return _sun;

            return RenderSettings.sun;
        }
    }
}
