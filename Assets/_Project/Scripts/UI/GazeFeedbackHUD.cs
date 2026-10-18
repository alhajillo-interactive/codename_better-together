using BetterTogether.Gaze;
using BetterTogether.Player;
using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Feedback for the petrifying gaze. Gnomes see a vignette that grows while locked, and a stone screen with a
    /// button-mashing counter when petrified; campers see a faint vignette outside their gaze cone that pulses
    /// whenever a gnome is locked.
    /// IMGUI placeholder until the game has a real HUD.
    /// </summary>
    public class GazeFeedbackHUD : MonoBehaviour
    {
        private const int TextureSize = 256;

        [Header("Gnome")]
        [SerializeField] private Color _warningColor = new(0.45f, 0.55f, 0.7f, 0.8f);
        [Tooltip("Cooler tint for being inside a camper's cone without being locked yet.")]
        [SerializeField] private Color _coneWarningColor = new(0.3f, 0.5f, 0.85f, 0.8f);
        [SerializeField] private Color _petrifiedColor = new(0.5f, 0.5f, 0.52f, 0.9f);
        [Range(0f, 1f)]
        [SerializeField] private float _warningInnerRadius = 0.35f;

        [Header("Break free")]
        [SerializeField] private float _shakePixels = 6f;
        [SerializeField] private float _shakeDuration = 0.06f;
        [SerializeField] private Vector2 _barSize = new(360f, 16f);
        [SerializeField] private Color _barColor = new(1f, 1f, 1f, 0.9f);
        [SerializeField] private Color _barBackColor = new(0f, 0f, 0f, 0.5f);
        [SerializeField] private Color _releaseFlashColor = new(1f, 1f, 1f, 0.25f);

        [Header("Camper")]
        [SerializeField] private Color _coneColor = new(0.3f, 0.2f, 0.5f, 1f);
        [SerializeField] private float _coneVignetteAlpha = 0.12f;
        [SerializeField] private float _pulseAlpha = 0.35f;
        [SerializeField] private float _pulseDuration = 0.3f;

        private Texture2D _edgeTexture;
        private Texture2D _coneTexture;
        private float _coneTextureRadius = -1f;
        private float _pulseTime = float.MaxValue;
        private float _shakeTime = float.MaxValue;
        private Petrifiable _trackedGnome;
        private Petrifiable _lastLocked;
        private int _lastPresses;
        private int _lastReleases;
        private GUIStyle _title;
        private GUIStyle _subtitle;

        private void OnDestroy()
        {
            if (_edgeTexture != null)
                Destroy(_edgeTexture);
            if (_coneTexture != null)
                Destroy(_coneTexture);
        }

        private void Update()
        {
            _pulseTime += Time.deltaTime;
            _shakeTime += Time.deltaTime;
            TrackCamperLock();
            TrackGnomeFeedback();
        }

        private void TrackCamperLock()
        {
            var gaze = CamperGaze.Local;
            var locked = gaze != null ? gaze.LockedTarget : null;
            if (locked != null && locked != _lastLocked)
                _pulseTime = 0f;

            _lastLocked = locked;
        }

        private void TrackGnomeFeedback()
        {
            var gnome = Petrifiable.Local;
            if (gnome != _trackedGnome)
            {
                _trackedGnome = gnome;
                _lastPresses = gnome != null ? gnome.PressCount : 0;
                _lastReleases = gnome != null ? gnome.ReleaseCount : 0;
                return;
            }

            if (gnome == null)
                return;

            if (gnome.PressCount != _lastPresses)
            {
                _lastPresses = gnome.PressCount;
                _shakeTime = 0f;
                if (CameraPunch.Instance != null)
                    CameraPunch.Instance.PunchFov(0.6f, 0.08f);
            }

            if (gnome.ReleaseCount == _lastReleases)
                return;

            _lastReleases = gnome.ReleaseCount;
            if (ScreenFlash.Instance != null)
                ScreenFlash.Instance.Flash(_releaseFlashColor, 0.15f);
            if (CameraPunch.Instance != null)
                CameraPunch.Instance.PunchFov(4f, 0.2f);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var gnome = Petrifiable.Local;
            if (gnome != null)
                DrawGnome(gnome);

            var gaze = CamperGaze.Local;
            if (gaze != null)
                DrawCamper(gaze);
        }

        private void DrawGnome(Petrifiable gnome)
        {
            _edgeTexture ??= CreateVignette(_warningInnerRadius);
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);

            if (!gnome.IsStone)
            {
                var color = gnome.IsLockTarget ? _warningColor : _coneWarningColor;
                color.a *= gnome.WarningProgress;
                DrawTexture(screen, _edgeTexture, color);
                return;
            }

            DrawTexture(screen, _edgeTexture, _petrifiedColor);
            DrawBreakFree(gnome);
        }

        private void DrawBreakFree(Petrifiable gnome)
        {
            _title ??= CreateStyle(40, FontStyle.Bold);
            _subtitle ??= CreateStyle(24, FontStyle.Normal);

            var shake = Vector2.zero;
            if (_shakeTime < _shakeDuration)
                shake = new Vector2(Random.Range(-_shakePixels, _shakePixels), Random.Range(-_shakePixels, _shakePixels));

            var top = Screen.height * 0.4f;
            GUI.Label(new Rect(shake.x, top + shake.y, Screen.width, 60f), "¡Petrificado! Machaca E", _title);

            var presses = Mathf.FloorToInt(gnome.BreakProgress);
            var required = Mathf.CeilToInt(gnome.RequiredPresses);
            GUI.Label(new Rect(0f, top + 60f, Screen.width, 30f), $"{presses}/{required}", _subtitle);

            var bar = new Rect((Screen.width - _barSize.x) * 0.5f, top + 100f, _barSize.x, _barSize.y);
            DrawTexture(bar, Texture2D.whiteTexture, _barBackColor);
            bar.width *= gnome.BreakFraction;
            DrawTexture(bar, Texture2D.whiteTexture, _barColor);
        }

        private void DrawCamper(CamperGaze gaze)
        {
            // The texture spans from the screen centre to its corner, so its clear centre can match the cone radius.
            var half = new Vector2(Screen.width, Screen.height).magnitude * 0.5f;
            var radius = ConeRadiusPixels(gaze.HalfAngle);
            if (_coneTexture == null || !Mathf.Approximately(radius, _coneTextureRadius))
            {
                if (_coneTexture != null)
                    Destroy(_coneTexture);

                _coneTexture = CreateVignette(Mathf.Clamp01(radius / half));
                _coneTextureRadius = radius;
            }

            var alpha = _coneVignetteAlpha;
            if (_pulseTime < _pulseDuration)
                alpha = Mathf.Lerp(_pulseAlpha, _coneVignetteAlpha, _pulseTime / _pulseDuration);

            var color = _coneColor;
            color.a = alpha;
            var rect = new Rect(Screen.width * 0.5f - half, Screen.height * 0.5f - half, half * 2f, half * 2f);
            DrawTexture(rect, _coneTexture, color);
        }

        private static float ConeRadiusPixels(float halfAngle)
        {
            var camera = Camera.main;
            var fov = camera != null ? camera.fieldOfView : 60f;
            return Mathf.Tan(halfAngle * Mathf.Deg2Rad) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * Screen.height * 0.5f;
        }

        private static void DrawTexture(Rect rect, Texture2D texture, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);
            GUI.color = previous;
        }

        private static GUIStyle CreateStyle(int size, FontStyle fontStyle) => new(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = size,
            fontStyle = fontStyle,
            normal = { textColor = Color.white },
        };

        /// <summary>White texture, transparent inside <paramref name="inner"/> (fraction of the half size) and opaque at the edge.</summary>
        private static Texture2D CreateVignette(float inner)
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[TextureSize * TextureSize];
            var center = (TextureSize - 1) * 0.5f;
            for (var y = 0; y < TextureSize; y++)
            {
                for (var x = 0; x < TextureSize; x++)
                {
                    var distance = new Vector2(x - center, y - center).magnitude / center;
                    var alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, 1f, distance));
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
