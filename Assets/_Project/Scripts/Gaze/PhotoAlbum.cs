using System;
using BetterTogether.Match;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BetterTogether.Gaze
{
    /// <summary>
    /// The camper's photo album (local only, nothing networked). When the camper's own <see cref="CamperGaze"/> reports a
    /// new shot that hit, a secondary camera that stays disabled renders ONE frame from the player's view into a pooled
    /// RenderTexture and <see cref="PhotoTaken"/> is raised. The HUD (C8.2) subscribes to that event to show the thumbnail.
    /// Only the shooter's own copy acts (HasStateAuthority of the gaze); late joiners and the first frame after the gaze
    /// comes on only take the shot sequence, so old shots are never photographed.
    /// </summary>
    [RequireComponent(typeof(CamperGaze))]
    public class PhotoAlbum : MonoBehaviour
    {
        [SerializeField] private CamperGaze _gaze;
        [Tooltip("Side in pixels of each square photo.")]
        [SerializeField, Min(32)] private int _size = 256;
        [Tooltip("Number of pooled photo textures; the oldest is reused after this many shots.")]
        [SerializeField, Range(1, 8)] private int _poolSize = 4;
        [Tooltip("Layers the photo shows. The UI layer is always removed at runtime.")]
        [SerializeField] private LayerMask _cullingMask = ~0;

        /// <summary>
        /// Raised right after a photo of a hit is rendered. The texture belongs to a pool of <c>_poolSize</c>: use it now
        /// or copy it, never keep it past that many further shots. The HUD (C8.2) connects with
        /// <c>PhotoAlbum.PhotoTaken += GazeFeedbackHUD.ShowPhoto</c>.
        /// </summary>
        public static event Action<Texture> PhotoTaken;

        /// <summary>Hits photographed since the match started.</summary>
        public int PhotosThisMatch { get; private set; }

        private RenderTexture[] _pool;
        private Camera _shotCamera;
        private Camera _viewCamera;
        private int _next;
        private byte _prevShotSeq;
        private bool _ready;

        private void Awake()
        {
            if (_gaze == null)
                _gaze = GetComponent<CamperGaze>();

            _pool = new RenderTexture[_poolSize];
            for (var i = 0; i < _pool.Length; i++)
            {
                _pool[i] = new RenderTexture(_size, _size, 16, RenderTextureFormat.ARGB32)
                {
                    name = "Photo " + i,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                _pool[i].Create();
            }

            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                _cullingMask &= ~(1 << uiLayer);

            var cameraObject = new GameObject("Photo Camera") { hideFlags = HideFlags.HideAndDontSave };
            cameraObject.transform.SetParent(transform, false);
            _shotCamera = cameraObject.AddComponent<Camera>();
            _shotCamera.enabled = false; // Only ever rendered by hand with Render().
            var extra = _shotCamera.GetUniversalAdditionalCameraData();
            extra.renderPostProcessing = false;
            extra.requiresDepthOption = CameraOverrideOption.Off;
            extra.requiresColorOption = CameraOverrideOption.Off;
        }

        private void OnEnable() => MatchManager.OnGameStarted += HandleGameStarted;

        private void OnDisable() => MatchManager.OnGameStarted -= HandleGameStarted;

        private void OnDestroy()
        {
            if (_pool != null)
            {
                for (var i = 0; i < _pool.Length; i++)
                {
                    if (_pool[i] != null)
                    {
                        _pool[i].Release();
                        Destroy(_pool[i]);
                    }
                }
            }

            if (_shotCamera != null)
                Destroy(_shotCamera.gameObject);
        }

        private void HandleGameStarted() => PhotosThisMatch = 0;

        private void LateUpdate()
        {
            if (_gaze == null || !_gaze.IsGazeOn || !_gaze.HasStateAuthority)
            {
                _ready = false;
                return;
            }

            var seq = _gaze.ShotSeq;
            var isNewShot = _ready && seq != _prevShotSeq;
            _prevShotSeq = seq;
            _ready = true;
            if (isNewShot && _gaze.LastShotHit)
                TakePhoto();
        }

        private void TakePhoto()
        {
            if (_viewCamera == null)
                _viewCamera = Camera.main;
            if (_viewCamera == null)
                return;

            var texture = _pool[_next];
            _next = (_next + 1) % _pool.Length;

            _shotCamera.CopyFrom(_viewCamera);
            // Same layers the camper sees, minus the filter (UI is always out).
            _shotCamera.cullingMask = _viewCamera.cullingMask & _cullingMask;
            _shotCamera.targetTexture = texture;
            _shotCamera.aspect = 1f;
            _shotCamera.Render();
            _shotCamera.targetTexture = null;

            PhotosThisMatch++;
            PhotoTaken?.Invoke(texture);
        }
    }
}
