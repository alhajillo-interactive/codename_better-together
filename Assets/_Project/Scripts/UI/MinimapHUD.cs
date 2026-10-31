using BetterTogether.Fireplace;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// Top-right minimap (C9.5), north up. Shows the local player's arrow, the fixed points of the level (fire, cage, pool),
    /// the water bucket for both sides and, for a gnome only, the two teammates. It never shows the camper to a gnome, a gnome
    /// to the camper, nor logs, matches, sausages or hiding spots. Purely presentation: everything is read from replicated state
    /// every frame, so a late joiner is correct at the first frame, a teammate that leaves loses its marker, and when the master
    /// changes the bucket is followed through its own transform. Hidden outside a match and faded while the camera is raised.
    /// </summary>
    public class MinimapHUD : MonoBehaviour
    {
        private const float BucketSearchInterval = 1f;
        private const int ArrowTexturePixels = 64;

        [Header("Wiring")]
        [Tooltip("Visible part of the minimap (frame and map). The script lives on its parent so it keeps running while hidden.")]
        [SerializeField] private GameObject _panel;
        [Tooltip("Group on the panel; its alpha fades the minimap while the camera is raised.")]
        [SerializeField] private CanvasGroup _group;
        [Tooltip("Image that shows the baked map. Markers are its children, anchored to its centre.")]
        [SerializeField] private RawImage _map;
        [Tooltip("Pool of markers for the fixed points of the level.")]
        [SerializeField] private Image[] _fixedMarkers;
        [Tooltip("Marker of the water bucket.")]
        [SerializeField] private Image _bucketMarker;
        [Tooltip("Pool of markers for the teammates (gnome only).")]
        [SerializeField] private Image[] _mateMarkers;
        [Tooltip("Arrow of the local player. Its sprite is generated at start.")]
        [SerializeField] private Image _selfMarker;

        [Header("Feel")]
        [Tooltip("Opacity of the minimap while the camera is fully raised.")]
        [Range(0f, 1f)] [SerializeField] private float _focusedAlpha = 0.25f;
        [Tooltip("Opacity of the marker of a free teammate.")]
        [Range(0f, 1f)] [SerializeField] private float _freeAlpha = 0.9f;

        private sealed class Mate
        {
            public Petrifiable Gnome;
            public GnomeCapture Capture;
        }

        private RectTransform _mapRect;
        private Texture2D _arrowTexture;
        private Sprite _arrowSprite;
        private MinimapArea _shownArea;
        private Mate[] _mates;
        private FireplaceManager _fire;
        private WaterBucket _bucket;
        private float _nextBucketSearch;
        private int _fireMarker = -1;
        private bool _fireShownLit;
        private bool _hasFireShown;
        private bool _hasSelfRole;
        private PlayerRole _selfRole;
        private Camera _camera;

        private static UiTheme Theme => UiTheme.Safe;

        private void Awake()
        {
            var wired = _panel != null && _group != null && _map != null && _fixedMarkers != null && _bucketMarker != null
                        && _mateMarkers != null && _selfMarker != null;
            if (!wired)
            {
                Debug.LogError("[MinimapHUD] Not wired; run GnoMore/Construir HUD de juego.", this);
                enabled = false;
                return;
            }

            _mapRect = _map.rectTransform;
            _mates = new Mate[_mateMarkers.Length];
            BuildArrow();

            _map.raycastTarget = false;
            _bucketMarker.raycastTarget = false;
            _selfMarker.raycastTarget = false;
            for (var i = 0; i < _fixedMarkers.Length; i++)
                _fixedMarkers[i].raycastTarget = false;

            for (var i = 0; i < _mateMarkers.Length; i++)
                _mateMarkers[i].raycastTarget = false;

            HideMarkers();
            _panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_arrowSprite != null)
                Destroy(_arrowSprite);

            if (_arrowTexture != null)
                Destroy(_arrowTexture);
        }

        private void LateUpdate()
        {
            var area = MinimapArea.Current;
            if (!ShouldShow(area))
            {
                if (_panel.activeSelf)
                {
                    _panel.SetActive(false);
                    HideMarkers();
                    ClearMates();
                }

                return;
            }

            if (!_panel.activeSelf)
                _panel.SetActive(true);

            if (area != _shownArea)
                ApplyArea(area);

            var focus = CameraFocus.Instance;
            var amount = focus != null ? focus.Amount : 0f;
            _group.alpha = Mathf.Lerp(1f, _focusedAlpha, amount);

            var local = PlayerCharacter.Local;
            UpdateSelf(area, local);
            UpdateFire();
            UpdateBucket(area);
            UpdateMates(area, local);
        }

        // Only in a running match, with an area in the scene and a spawned local player.
        private static bool ShouldShow(MinimapArea area)
        {
            if (area == null || area.Map == null || PlayerCharacter.Local == null)
                return false;

            var match = MatchManager.Instance;
            return match != null && match.Phase == MatchPhase.Playing;
        }

        #region Area

        // Picture and fixed points: set once per area (the first frame it is shown, also for a late joiner).
        private void ApplyArea(MinimapArea area)
        {
            _shownArea = area;
            _map.texture = area.Map;
            _fire = null;
            _fireMarker = -1;
            _hasFireShown = false;

            var points = area.Points;
            var count = points != null ? points.Count : 0;
            for (var i = 0; i < _fixedMarkers.Length; i++)
            {
                var marker = _fixedMarkers[i];
                if (i >= count)
                {
                    marker.gameObject.SetActive(false);
                    continue;
                }

                var point = points[i];
                var sprite = Theme.Icon(point.Icon);
                marker.sprite = sprite;
                marker.enabled = sprite != null;
                marker.color = Color.white; // ui-color-ok: white is the untinted multiplier of a sprite, not a themed colour
                marker.gameObject.SetActive(true);
                Place(marker.rectTransform, area, point.Position.x, point.Position.y);
                if (point.Icon == UiIcon.Fire)
                    _fireMarker = i;
            }
        }

        // The "Fire" point turns ember while the fire is lit (written only on change).
        private void UpdateFire()
        {
            if (_fireMarker < 0)
                return;

            if (_fire == null)
                _fire = FireplaceManager.Instance;

            var lit = _fire != null && _fire.IsLit;
            if (_hasFireShown && lit == _fireShownLit)
                return;

            _hasFireShown = true;
            _fireShownLit = lit;
            _fixedMarkers[_fireMarker].color = lit ? Theme.ember : Color.white; // ui-color-ok: white is the untinted multiplier of a sprite, not a themed colour
        }

        #endregion

        #region Dynamic markers

        // The local player: an arrow in the colour of the role, turned to where the camera looks (north is up).
        private void UpdateSelf(MinimapArea area, PlayerCharacter local)
        {
            if (!_selfMarker.gameObject.activeSelf)
                _selfMarker.gameObject.SetActive(true);

            if (!_hasSelfRole || _selfRole != local.Role)
            {
                _hasSelfRole = true;
                _selfRole = local.Role;
                _selfMarker.color = local.Role == PlayerRole.Camper ? Theme.roleCamper : Theme.roleGnome;
            }

            var position = local.transform.position;
            Place(_selfMarker.rectTransform, area, position.x, position.z);

            if (_camera == null)
                _camera = Camera.main;

            var forward = _camera != null ? _camera.transform.forward : local.transform.forward;
            if (forward.x * forward.x + forward.z * forward.z > 0.0001f)
            {
                var yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                _selfMarker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -yaw);
            }
        }

        // The bucket is shown to both sides at its real position. The reference is cached and found again (about once a
        // second) only after it is lost, so there is no per-frame search.
        private void UpdateBucket(MinimapArea area)
        {
            if (_bucket == null && Time.unscaledTime >= _nextBucketSearch)
            {
                _nextBucketSearch = Time.unscaledTime + BucketSearchInterval;
                _bucket = FindAnyObjectByType<WaterBucket>();
            }

            var visible = _bucket != null;
            if (_bucketMarker.gameObject.activeSelf != visible)
            {
                _bucketMarker.gameObject.SetActive(visible);
                if (visible)
                {
                    var sprite = Theme.Icon(UiIcon.Bucket);
                    _bucketMarker.sprite = sprite;
                    _bucketMarker.enabled = sprite != null;
                    _bucketMarker.color = Color.white; // ui-color-ok: white is the untinted multiplier of a sprite, not a themed colour
                }
            }

            if (!visible)
                return;

            var position = _bucket.transform.position;
            Place(_bucketMarker.rectTransform, area, position.x, position.z);
        }

        // Teammates (gnome only): same states as TeammatesHUD, free or plaster. Slots keep their gnome until it leaves.
        private void UpdateMates(MinimapArea area, PlayerCharacter local)
        {
            var gnome = local.Role == PlayerRole.Gnome && Petrifiable.Local != null && Petrifiable.Local.IsAvailable;
            if (!gnome)
            {
                ClearMates();
                return;
            }

            DropGoneMates();
            AssignNewMates();

            var theme = Theme;
            for (var i = 0; i < _mates.Length; i++)
            {
                var marker = _mateMarkers[i];
                var mate = _mates[i];
                if (mate == null)
                {
                    if (marker.gameObject.activeSelf)
                        marker.gameObject.SetActive(false);

                    continue;
                }

                if (!marker.gameObject.activeSelf)
                {
                    var sprite = theme.Icon(UiIcon.Gnome);
                    marker.sprite = sprite;
                    marker.enabled = sprite != null;
                    marker.gameObject.SetActive(true);
                }

                var free = !mate.Gnome.IsStone && (mate.Capture == null || (!mate.Capture.IsCaged && !mate.Capture.IsCarried));
                var color = free ? UiTheme.WithAlpha(Color.white, _freeAlpha) : theme.plaster; // ui-color-ok: white is the untinted multiplier of a sprite, not a themed colour
                if (marker.color != color)
                    marker.color = color;

                var position = mate.Gnome.transform.position;
                Place(marker.rectTransform, area, position.x, position.z);
            }
        }

        private void DropGoneMates()
        {
            for (var i = 0; i < _mates.Length; i++)
            {
                var mate = _mates[i];
                if (mate != null && (mate.Gnome == null || !mate.Gnome.IsAvailable || !IsListed(mate.Gnome)))
                    _mates[i] = null;
            }
        }

        private void AssignNewMates()
        {
            var local = Petrifiable.Local;
            var all = Petrifiable.All;
            for (var i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (candidate == null || candidate == local || !candidate.IsAvailable || HasSlot(candidate))
                    continue;

                var free = FreeSlot();
                if (free < 0)
                    return;

                // One small object per teammate that joins, never per frame.
                candidate.TryGetComponent<GnomeCapture>(out var capture);
                _mates[free] = new Mate { Gnome = candidate, Capture = capture };
            }
        }

        private bool HasSlot(Petrifiable gnome)
        {
            for (var i = 0; i < _mates.Length; i++)
            {
                if (_mates[i] != null && _mates[i].Gnome == gnome)
                    return true;
            }

            return false;
        }

        private int FreeSlot()
        {
            for (var i = 0; i < _mates.Length; i++)
            {
                if (_mates[i] == null)
                    return i;
            }

            return -1;
        }

        private static bool IsListed(Petrifiable gnome)
        {
            var all = Petrifiable.All;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] == gnome)
                    return true;
            }

            return false;
        }

        private void ClearMates()
        {
            for (var i = 0; i < _mates.Length; i++)
            {
                _mates[i] = null;
                if (_mateMarkers[i].gameObject.activeSelf)
                    _mateMarkers[i].gameObject.SetActive(false);
            }
        }

        #endregion

        #region Helpers

        // Puts a marker (anchored to the centre of the map image) where a world point falls on the picture, clamped to its edge.
        private void Place(RectTransform marker, MinimapArea area, float worldX, float worldZ)
        {
            var size = area.WorldSize;
            var center = area.WorldCenter;
            var nx = size.x > 0f ? Mathf.Clamp((worldX - center.x) / size.x, -0.5f, 0.5f) : 0f;
            var nz = size.y > 0f ? Mathf.Clamp((worldZ - center.y) / size.y, -0.5f, 0.5f) : 0f;
            var rect = _mapRect.rect;
            marker.anchoredPosition = new Vector2(nx * rect.width, nz * rect.height);
        }

        private void HideMarkers()
        {
            for (var i = 0; i < _fixedMarkers.Length; i++)
                _fixedMarkers[i].gameObject.SetActive(false);

            for (var i = 0; i < _mateMarkers.Length; i++)
                _mateMarkers[i].gameObject.SetActive(false);

            _bucketMarker.gameObject.SetActive(false);
            _selfMarker.gameObject.SetActive(false);
            _hasSelfRole = false;
            _hasFireShown = false;
            _shownArea = null;
        }

        // White upward arrow head (tinted by role), generated once: the project has no arrow sprite.
        private void BuildArrow()
        {
            const int n = ArrowTexturePixels;
            _arrowTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "MinimapArrow", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[n * n];
            for (var y = 0; y < n; y++)
            {
                // Row 0 is the bottom; the tip is at the top. The half width grows linearly from the tip to the base.
                var t = 1f - (y + 0.5f) / n;
                var half = t * n * 0.45f;
                for (var x = 0; x < n; x++)
                {
                    var dx = Mathf.Abs(x + 0.5f - n * 0.5f);
                    pixels[y * n + x] = dx <= half ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0); // ui-color-ok: white mask of a sprite, tinted at runtime
                }
            }

            _arrowTexture.SetPixels32(pixels);
            _arrowTexture.Apply(false, true);
            _arrowSprite = Sprite.Create(_arrowTexture, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            _selfMarker.sprite = _arrowSprite;
            _selfMarker.preserveAspect = true;
            _selfMarker.enabled = true;
        }

        #endregion
    }
}
