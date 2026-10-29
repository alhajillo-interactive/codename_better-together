using BetterTogether.Audio;
using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// Hover/press feedback for a UI button: scale up on hover, down on press, optional hover/click sounds.
    /// Uses unscaled time and PrimeTween; nothing is allocated per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class UiButtonFeel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Scale")]
        [Tooltip("Scale while the pointer is over the button.")]
        [SerializeField] private float _hoverScale = 1.04f;
        [Tooltip("Scale while the button is pressed.")]
        [SerializeField] private float _pressedScale = 0.97f;
        [Tooltip("Seconds of each scale transition.")]
        [Min(0f)] [SerializeField] private float _duration = 0.08f;

        [Header("Audio (optional)")]
        [Tooltip("Played when the pointer enters. Empty = silent.")]
        [SerializeField] private SoundCue _hoverCue;
        [Tooltip("Played when the button is pressed. Empty = silent.")]
        [SerializeField] private SoundCue _clickCue;

        private Selectable _selectable;
        private Vector3 _baseScale = Vector3.one;
        private bool _hovered;
        private Tween _tween;

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            _baseScale = transform.localScale;
        }

        private void OnDisable()
        {
            _tween.Stop();
            _hovered = false;
            transform.localScale = _baseScale;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!Interactable)
                return;

            _hovered = true;
            ScaleTo(_hoverScale);
            if (_hoverCue != null)
                _hoverCue.Play();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            ScaleTo(1f);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Interactable)
                return;

            ScaleTo(_pressedScale);
            if (_clickCue != null)
                _clickCue.Play();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            ScaleTo(_hovered ? _hoverScale : 1f);
        }

        private bool Interactable => _selectable == null || _selectable.IsInteractable();

        private void ScaleTo(float factor)
        {
            _tween.Stop();
            _tween = Tween.Scale(transform, _baseScale * factor, _duration, Ease.OutQuad, useUnscaledTime: true);
        }
    }
}
