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
        [Tooltip("Root of the visual model; every renderer under it is hidden for the local player (shadows stay).")]
        [SerializeField] private Transform _body;

        public override void Spawned()
        {
            if (!HasStateAuthority)
                return;

            if (FirstPersonCamera.Instance != null)
                FirstPersonCamera.Instance.Follow(_cameraAnchor);
            else
                Debug.LogWarning("[LocalPlayerView] No FirstPersonCamera found in the scene.");

            if (_body == null)
                return;

            foreach (var bodyRenderer in _body.GetComponentsInChildren<Renderer>(true))
                bodyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (FirstPersonCamera.Instance != null)
                FirstPersonCamera.Instance.Release(_cameraAnchor);
        }
    }
}
