using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.Combat
{
    /// <summary>
    /// Local-only ring around the screen centre that tells the camper whether a kick would land right now:
    /// hidden with nothing in range, bright and pulsing when a gnome is in range and the kick is ready, grey while cooling down.
    /// Owns its own overlay canvas so the HUD is untouched. Added at runtime by <see cref="CamperKick"/> on the local camper.
    /// </summary>
    [RequireComponent(typeof(CamperKick))]
    public class KickReadout : MonoBehaviour
    {
        [SerializeField] private Color _readyColor = new(1f, 0.55f, 0.1f, 0.95f);
        [SerializeField] private Color _cooldownColor = new(0.7f, 0.7f, 0.7f, 0.45f);
        [Tooltip("Ring diameter in pixels: when a target first enters range, and when the kick is ready.")]
        [SerializeField] private Vector2 _size = new(62f, 44f);
        [SerializeField] private float _smoothing = 14f;

        private CamperKick _kick;
        private Image _ring;
        private RectTransform _rect;
        private Sprite _sprite;
        private float _amount;

        private void Awake() => _kick = GetComponent<CamperKick>();

        /// <summary>Gives the ring sprite (serialized on <see cref="CamperKick"/>) and builds the overlay.</summary>
        public void Setup(Sprite ringSprite)
        {
            if (_ring != null || ringSprite == null)
                return;

            _sprite = ringSprite;
            Build();
        }

        private void Build()
        {
            var canvasObject = new GameObject("KickReadoutCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;

            // Same scaler as the rest of the HUD (1920x1080, match 0.5) so the ring keeps its proportion on any monitor.
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var ringObject = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            ringObject.transform.SetParent(canvasObject.transform, false);
            _rect = (RectTransform)ringObject.transform;
            _ring = ringObject.GetComponent<Image>();
            _ring.sprite = _sprite;
            _ring.raycastTarget = false;
            _ring.enabled = false;
        }

        private void Update()
        {
            if (_ring == null)
                return;

            var ready = _kick.IsReady;
            var target = _kick.HasTargetInRange ? 1f : 0f;
            _amount = Mathf.MoveTowards(_amount, target, _smoothing * Time.unscaledDeltaTime);
            _ring.enabled = _amount > 0.01f;
            if (!_ring.enabled)
                return;

            var color = ready ? _readyColor : _cooldownColor;
            color.a *= _amount;
            _ring.color = color;

            var size = Mathf.Lerp(_size.x, _size.y, _amount);
            if (ready)
                size *= 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 18f);
            _rect.sizeDelta = new Vector2(size, size);
        }
    }
}
