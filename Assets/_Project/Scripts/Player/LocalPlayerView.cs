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

        /// <summary>Root of the visual model (may be null when the prefab has no model root).</summary>
        public Transform Body => _body;

        private Renderer[] _bodyRenderers;
        private bool _bodyVisible;

        /// <summary>
        /// Local player only: shows the own body (e.g. while the camera is in third person) or hides it again (shadows stay).
        /// </summary>
        public void SetBodyVisible(bool visible)
        {
            if (_bodyRenderers == null || _bodyVisible == visible)
                return;

            _bodyVisible = visible;
            var mode = visible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            foreach (var bodyRenderer in _bodyRenderers)
            {
                if (bodyRenderer != null)
                    bodyRenderer.shadowCastingMode = mode;
            }
        }

        public override void Spawned()
        {
            if (!HasStateAuthority)
                return;

            if (FirstPersonCamera.Instance != null)
                FirstPersonCamera.Instance.Follow(_cameraAnchor);
            else
                Debug.LogWarning("[LocalPlayerView] No FirstPersonCamera found in the scene.");

            HideBody(_body);
            _bodyRenderers = _body != null ? _body.GetComponentsInChildren<Renderer>(true) : null;

            // The character prefabs can contain alternative meshes. Hide those too so
            // activating a different model never exposes the local body to the camera.
            foreach (var animator in GetComponentsInChildren<Animator>(true))
            {
                if (animator.transform.parent == transform && animator.transform != _body)
                    HideBody(animator.transform);
            }
        }

        private static void HideBody(Transform body)
        {
            if (body == null)
                return;

            foreach (var bodyRenderer in body.GetComponentsInChildren<Renderer>(true))
                bodyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (FirstPersonCamera.Instance != null)
                FirstPersonCamera.Instance.Release(_cameraAnchor);
        }
    }
}
