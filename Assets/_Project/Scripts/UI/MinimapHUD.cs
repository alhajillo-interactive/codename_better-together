using BetterTogether.Fireplace;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// Camp plan (C9.6, replaces the permanent minimap of C9.5): a hand-drawn paper sheet on the right edge, north up, that only
    /// opens while the map key is held (or toggled, see <see cref="MapSettings"/>) and never while the camera is raised.
    /// It shows the local player's arrow (with an ink outline), the fire, the cage, the six sector names and, for a gnome only,
    /// the teammates that are standing as stone figurines (not carried, not caged) plus how many are caged. It never shows the
    /// camper to a gnome, a gnome to the camper, the bucket, logs, matches, sausages or hiding spots. Purely presentation:
    /// everything is read from replicated state every frame, so a late joiner is correct at the first frame, a teammate that
    /// leaves loses its marker and a new master changes nothing. You can walk while it is open. Hidden outside a match.
    /// </summary>
    public class MinimapHUD : MonoBehaviour
    {
        private const string MapActionPath = "Player/Map";
        private const float ActionSearchInterval = 1f;
        private const int SpriteTexturePixels = 64;
        private const int OutlinePixels = 3;
        private const float OpenScale = 0.96f;

        private static readonly string[] CountTexts = { string.Empty, "1", "2", "3", "4", "5", "6", "7", "8" };

        [Header("Wiring")]
        [Tooltip("Visible sheet (paper and map). The script lives on its parent so it keeps reading the key while the sheet is hidden.")]
        [SerializeField] private GameObject _panel;
        [Tooltip("Group on the sheet; its alpha drives the open and close fade.")]
        [SerializeField] private CanvasGroup _group;
        [Tooltip("Image that shows the drawn map. Markers and sector names are its children, anchored to its centre.")]
        [SerializeField] private RawImage _map;
        [Tooltip("Pool of markers for the fixed points of the level (fire and cage).")]
        [SerializeField] private Image[] _fixedMarkers;
        [Tooltip("Pool of markers for the stone teammates (gnome only).")]
        [SerializeField] private Image[] _mateMarkers;
        [Tooltip("Arrow of the local player. Its sprite is generated at start.")]
        [SerializeField] private Image _selfMarker;
        [Tooltip("Pool of texts for the sector names.")]
        [SerializeField] private TMP_Text[] _labels;
        [Tooltip("Number of caged gnomes, written next to the cage (gnome only).")]
        [SerializeField] private TMP_Text _cageCount;

        [Header("Feel")]
        [Tooltip("Seconds of the open and close transition.")]
        [SerializeField] private float _transitionDuration = 0.15f;
        [Tooltip("Offset in map pixels of the caged-gnomes number from the cage marker.")]
        [SerializeField] private Vector2 _cageCountOffset = new(30f, 22f);

        private sealed class Mate
        {
            public Petrifiable Gnome;
            public GnomeCapture Capture;
        }

        private RectTransform _mapRect;
        private RectTransform _panelRect;
        private Texture2D _arrowTexture;
        private Sprite _arrowSprite;
        private Texture2D _mateTexture;
        private Sprite _mateSprite;
        private Color32[] _pixels;
        private bool[] _mask;
        private MinimapArea _shownArea;
        private Mate[] _mates;
        private FireplaceManager _fire;
        private InputAction _mapAction;
        private float _nextActionSearch;
        private int _fireMarker = -1;
        private int _cageMarker = -1;
        private bool _fireShownLit;
        private bool _hasFireShown;
        private bool _hasSelfRole;
        private PlayerRole _selfRole;
        private Camera _camera;
        private Petrifiable _localGnome;
        private GnomeCapture _localCapture;
        private int _shownCageCount = -1;
        private bool _open;
        private bool _toggled;
        private Tween _fade;
        private Tween _scale;

        private static UiTheme Theme => UiTheme.Safe;

        private void Awake()
        {
            var wired = _panel != null && _group != null && _map != null && _fixedMarkers != null && _mateMarkers != null
                        && _selfMarker != null && _labels != null && _cageCount != null;
            if (!wired)
            {
                Debug.LogError("[MinimapHUD] Not wired; run GnoMore/Construir HUD de juego.", this);
                enabled = false;
                return;
            }

            _mapRect = _map.rectTransform;
            _panelRect = (RectTransform)_panel.transform;
            _mates = new Mate[_mateMarkers.Length];
            _pixels = new Color32[SpriteTexturePixels * SpriteTexturePixels];
            _mask = new bool[SpriteTexturePixels * SpriteTexturePixels];
            BuildMateSprite();

            _map.raycastTarget = false;
            _selfMarker.raycastTarget = false;
            _cageCount.raycastTarget = false;
            for (var i = 0; i < _fixedMarkers.Length; i++)
                _fixedMarkers[i].raycastTarget = false;

            for (var i = 0; i < _mateMarkers.Length; i++)
                _mateMarkers[i].raycastTarget = false;

            for (var i = 0; i < _labels.Length; i++)
                _labels[i].raycastTarget = false;

            HideMarkers();
            _panel.SetActive(false);
        }

        private void OnDisable()
        {
            _fade.Stop();
            _scale.Stop();
            _open = false;
            _toggled = false;
            if (_panel != null)
                _panel.SetActive(false);
        }

        private void OnDestroy()
        {
            DestroySprite(ref _arrowSprite, ref _arrowTexture);
            DestroySprite(ref _mateSprite, ref _mateTexture);
        }

        private void LateUpdate()
        {
            var area = MinimapArea.Current;
            if (!ShouldShow(area))
            {
                _toggled = false;
                if (_open || _panel.activeSelf)
                    CloseNow();

                return;
            }

            var want = WantsOpen();
            if (want != _open)
            {
                if (want)
                    Open(area);
                else
                    Close();
            }

            if (!_panel.activeSelf)
                return;

            var local = PlayerCharacter.Local;
            UpdateSelf(area, local);
            UpdateFire();
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

        #region Open and close

        // Held (default) or toggled by the map key; never while the camera is raised, a menu holds the cursor or the chat is typing.
        private bool WantsOpen()
        {
            var focus = CameraFocus.Instance;
            if (focus != null && focus.Amount > 0f)
            {
                _toggled = false;
                return false;
            }

            if (FirstPersonCamera.CursorFree || FirstPersonCamera.IsTyping)
                return false;

            var action = MapAction();
            if (action == null)
                return false;

            if (MapSettings.Toggle)
            {
                if (action.WasPressedThisFrame())
                    _toggled = !_toggled;

                return _toggled;
            }

            _toggled = false;
            return action.IsPressed();
        }

        private InputAction MapAction()
        {
            if (_mapAction == null && Time.unscaledTime >= _nextActionSearch)
            {
                _nextActionSearch = Time.unscaledTime + ActionSearchInterval;
                _mapAction = InputSystem.actions != null ? InputSystem.actions.FindAction(MapActionPath) : null;
            }

            return _mapAction;
        }

        private void Open(MinimapArea area)
        {
            _open = true;
            _fade.Stop();
            _scale.Stop();
            var startAlpha = _panel.activeSelf ? _group.alpha : 0f;
            var startScale = _panel.activeSelf ? _panelRect.localScale.x : OpenScale;
            if (!_panel.activeSelf)
                _panel.SetActive(true);

            if (area != _shownArea)
                ApplyArea(area);

            _group.alpha = startAlpha;
            _panelRect.localScale = Vector3.one * startScale;
            _fade = Tween.Alpha(_group, startAlpha, 1f, _transitionDuration, Ease.OutQuad, useUnscaledTime: true);
            _scale = Tween.Scale(_panelRect, startScale, 1f, _transitionDuration, Ease.OutQuad, useUnscaledTime: true);
        }

        private void Close()
        {
            _open = false;
            _fade.Stop();
            _scale.Stop();
            _fade = Tween.Alpha(_group, _group.alpha, 0f, _transitionDuration, Ease.InQuad, useUnscaledTime: true)
                .OnComplete(this, self => self.FinishClose());
            _scale = Tween.Scale(_panelRect, _panelRect.localScale.x, OpenScale, _transitionDuration, Ease.InQuad, useUnscaledTime: true);
        }

        // The fade ended: hide the sheet unless it was asked to open again meanwhile.
        private void FinishClose()
        {
            if (_open || _panel == null)
                return;

            _panel.SetActive(false);
            ClearMates();
        }

        // Immediate hide (match ended, scene without plan, spawn lost).
        private void CloseNow()
        {
            _open = false;
            _fade.Stop();
            _scale.Stop();
            _panel.SetActive(false);
            HideMarkers();
            ClearMates();
        }

        #endregion

        #region Area

        // Picture, fixed points and sector names: set once per area (the first time it opens, also for a late joiner).
        private void ApplyArea(MinimapArea area)
        {
            _shownArea = area;
            _map.texture = area.Map;
            _fire = null;
            _fireMarker = -1;
            _cageMarker = -1;
            _hasFireShown = false;
            _shownCageCount = -1;

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
                else if (point.Icon == UiIcon.Cage)
                    _cageMarker = i;
            }

            var labels = area.Labels;
            var labelCount = labels != null ? labels.Count : 0;
            for (var i = 0; i < _labels.Length; i++)
            {
                var label = _labels[i];
                if (i >= labelCount)
                {
                    label.gameObject.SetActive(false);
                    continue;
                }

                label.text = labels[i].Text;
                label.color = Theme.ink;
                Place(label.rectTransform, area, labels[i].Position.x, labels[i].Position.y);
                label.gameObject.SetActive(true);
            }

            _cageCount.gameObject.SetActive(false);
            if (_cageMarker >= 0)
            {
                _cageCount.color = Theme.ink;
                _cageCount.rectTransform.anchoredPosition = _fixedMarkers[_cageMarker].rectTransform.anchoredPosition + _cageCountOffset;
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

        // The local player: an arrow in the colour of the role with an ink outline, turned to where the camera looks (north is up).
        private void UpdateSelf(MinimapArea area, PlayerCharacter local)
        {
            if (!_selfMarker.gameObject.activeSelf)
                _selfMarker.gameObject.SetActive(true);

            if (!_hasSelfRole || _selfRole != local.Role)
            {
                _hasSelfRole = true;
                _selfRole = local.Role;
                BuildArrowSprite(local.Role == PlayerRole.Camper ? Theme.roleCamper : Theme.roleGnome);
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

        // Teammates (gnome only): a figurine only while standing as stone, not carried, not caged. A free gnome is never shown.
        // Next to the cage, how many gnomes are caged (including this one). Slots keep their gnome until it leaves.
        private void UpdateMates(MinimapArea area, PlayerCharacter local)
        {
            var gnome = local.Role == PlayerRole.Gnome && Petrifiable.Local != null && Petrifiable.Local.IsAvailable;
            if (!gnome)
            {
                ClearMates();
                SetCageCount(0);
                return;
            }

            DropGoneMates();
            AssignNewMates();

            if (_localGnome != Petrifiable.Local)
            {
                _localGnome = Petrifiable.Local;
                _localGnome.TryGetComponent(out _localCapture);
            }

            var caged = _localCapture != null && _localCapture.IsCaged ? 1 : 0;
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

                var isCaged = mate.Capture != null && mate.Capture.IsCaged;
                if (isCaged)
                    caged++;

                var standing = mate.Gnome.IsStone && !isCaged && (mate.Capture == null || !mate.Capture.IsCarried);
                if (marker.gameObject.activeSelf != standing)
                {
                    if (standing && marker.sprite == null)
                    {
                        marker.sprite = _mateSprite;
                        marker.color = Color.white; // ui-color-ok: white is the untinted multiplier of a pre-coloured sprite
                        marker.enabled = true;
                    }

                    marker.gameObject.SetActive(standing);
                }

                if (!standing)
                    continue;

                var position = mate.Gnome.transform.position;
                Place(marker.rectTransform, area, position.x, position.z);
            }

            SetCageCount(caged);
        }

        private void SetCageCount(int count)
        {
            if (count == _shownCageCount || _cageMarker < 0)
                return;

            _shownCageCount = count;
            var visible = count > 0;
            if (visible)
                _cageCount.text = CountTexts[Mathf.Min(count, CountTexts.Length - 1)];

            if (_cageCount.gameObject.activeSelf != visible)
                _cageCount.gameObject.SetActive(visible);
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

            _localGnome = null;
            _localCapture = null;
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

            for (var i = 0; i < _labels.Length; i++)
                _labels[i].gameObject.SetActive(false);

            _cageCount.gameObject.SetActive(false);
            _selfMarker.gameObject.SetActive(false);
            _hasSelfRole = false;
            _hasFireShown = false;
            _shownCageCount = -1;
            _shownArea = null;
        }

        private static void DestroySprite(ref Sprite sprite, ref Texture2D texture)
        {
            if (sprite != null)
                Destroy(sprite);

            if (texture != null)
                Destroy(texture);

            sprite = null;
            texture = null;
        }

        // Upward arrow head (kite) filled with the role colour and drawn with an ink outline. The colour is baked into the sprite
        // (the Image tint stays neutral), so it is rebuilt only when the role changes, which is once per match.
        private void BuildArrowSprite(Color fill)
        {
            const float n = SpriteTexturePixels;
            for (var y = 0; y < SpriteTexturePixels; y++)
            {
                for (var x = 0; x < SpriteTexturePixels; x++)
                {
                    var u = (x + 0.5f) / n;
                    var v = (y + 0.5f) / n;
                    _mask[y * SpriteTexturePixels + x] = InTriangle(u, v, 0.5f, 0.88f, 0.16f, 0.12f, 0.5f, 0.3f)
                                                         || InTriangle(u, v, 0.5f, 0.88f, 0.84f, 0.12f, 0.5f, 0.3f);
                }
            }

            PaintMask(fill);
            if (_arrowTexture == null)
            {
                _arrowTexture = NewSpriteTexture("MinimapArrow");
                _arrowSprite = Sprite.Create(_arrowTexture, new Rect(0, 0, SpriteTexturePixels, SpriteTexturePixels), new Vector2(0.5f, 0.5f), 100f);
            }

            _arrowTexture.SetPixels32(_pixels);
            _arrowTexture.Apply(false);
            _selfMarker.sprite = _arrowSprite;
            _selfMarker.color = Color.white; // ui-color-ok: white is the untinted multiplier of a pre-coloured sprite
            _selfMarker.preserveAspect = true;
            _selfMarker.enabled = true;
        }

        // A little gnome figurine (pointed hat over a round body) in plaster with an ink outline: the stone teammate.
        private void BuildMateSprite()
        {
            const float n = SpriteTexturePixels;
            for (var y = 0; y < SpriteTexturePixels; y++)
            {
                for (var x = 0; x < SpriteTexturePixels; x++)
                {
                    var u = (x + 0.5f) / n;
                    var v = (y + 0.5f) / n;
                    var dx = u - 0.5f;
                    var dy = v - 0.34f;
                    _mask[y * SpriteTexturePixels + x] = InTriangle(u, v, 0.5f, 0.9f, 0.27f, 0.5f, 0.73f, 0.5f)
                                                         || dx * dx + dy * dy <= 0.25f * 0.25f;
                }
            }

            PaintMask(Theme.plaster);
            _mateTexture = NewSpriteTexture("MinimapFigurine");
            _mateTexture.SetPixels32(_pixels);
            _mateTexture.Apply(false);
            _mateSprite = Sprite.Create(_mateTexture, new Rect(0, 0, SpriteTexturePixels, SpriteTexturePixels), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Texture2D NewSpriteTexture(string textureName) =>
            new(SpriteTexturePixels, SpriteTexturePixels, TextureFormat.RGBA32, false)
            {
                name = textureName,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

        // Fills _pixels from _mask: the shape in 'fill', a ring of OutlinePixels around it in the ink colour, transparent elsewhere.
        private void PaintMask(Color fill)
        {
            Color32 inside = fill;
            Color32 outline = Theme.ink;
            var none = new Color32(0, 0, 0, 0); // ui-color-ok: fully transparent pixel of a generated sprite
            const int size = SpriteTexturePixels;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    if (_mask[y * size + x])
                    {
                        _pixels[y * size + x] = inside;
                        continue;
                    }

                    _pixels[y * size + x] = NearMask(x, y) ? outline : none;
                }
            }
        }

        private bool NearMask(int x, int y)
        {
            const int size = SpriteTexturePixels;
            for (var oy = -OutlinePixels; oy <= OutlinePixels; oy++)
            {
                var py = y + oy;
                if (py < 0 || py >= size)
                    continue;

                for (var ox = -OutlinePixels; ox <= OutlinePixels; ox++)
                {
                    var px = x + ox;
                    if (px < 0 || px >= size || ox * ox + oy * oy > OutlinePixels * OutlinePixels)
                        continue;

                    if (_mask[py * size + px])
                        return true;
                }
            }

            return false;
        }

        private static bool InTriangle(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            var d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            var d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            var d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            var negative = d1 < 0f || d2 < 0f || d3 < 0f;
            var positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        #endregion
    }
}
