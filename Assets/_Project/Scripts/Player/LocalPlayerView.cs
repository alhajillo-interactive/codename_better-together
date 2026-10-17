using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterTogether.Player
{
    /// <summary>
    /// First-person presentation for the local player only: mounts the scene camera on the camera anchor
    /// and hides the own body (shadow only). Remote players are left untouched.
    /// </summary>
    public class LocalPlayerView : NetworkBehaviour
    {
        [SerializeField] private Transform _cameraAnchor;
        [Tooltip("Renderers that should not be visible to the local player (they still cast shadows).")]
        [SerializeField] private Renderer[] _hiddenInFirstPerson;

        public override void Spawned()
        {
            name = $"{name} {Object.StateAuthority.PlayerId}{(HasStateAuthority ? " (local)" : "")}";

            if (!HasStateAuthority)
                return;

            if (FirstPersonCamera.Instance != null)
                FirstPersonCamera.Instance.Follow(_cameraAnchor);
            else
                Debug.LogWarning("[LocalPlayerView] No FirstPersonCamera found in the scene.");

            foreach (var hiddenRenderer in _hiddenInFirstPerson)
                hiddenRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (FirstPersonCamera.Instance != null)
                FirstPersonCamera.Instance.Release(_cameraAnchor);
        }
    }
}
