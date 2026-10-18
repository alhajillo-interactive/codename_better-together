using BetterTogether.Gaze;
using TMPro;
using UnityEngine;

namespace BetterTogether.Player
{
    /// <summary>
    /// Floating name above a gnome, shown only to other gnomes. Faces the camera and fades out with distance.
    /// </summary>
    public class NameTag : MonoBehaviour
    {
        [Tooltip("Character whose name is shown.")]
        [SerializeField] private PlayerCharacter _character;
        [Tooltip("Meters above the root where the text is centred.")]
        [SerializeField] private float _heightOffset = 0.6f;
        [Tooltip("Vertical gap between names when gnomes are stacked.")]
        [SerializeField] private float _towerLineSpacing = 0.18f;
        [Tooltip("TextMeshPro font size; 1.2 reads about 0.12 m tall.")]
        [SerializeField] private float _fontSize = 1.2f;
        [Tooltip("Distance where the name starts to fade.")]
        [SerializeField] private float _fadeStart = 8f;
        [Tooltip("Distance where the name is fully transparent.")]
        [SerializeField] private float _fadeEnd = 14f;
        [Tooltip("Scale up close to the camera.")]
        [SerializeField] private float _minScale = 1f;
        [Tooltip("Scale at and beyond the far scale distance.")]
        [SerializeField] private float _maxScale = 1.8f;
        [Tooltip("Distance where the scale starts to grow.")]
        [SerializeField] private float _scaleNear = 2f;
        [Tooltip("Distance where the scale reaches its maximum.")]
        [SerializeField] private float _scaleFar = 12f;
        [Tooltip("Tint of the petrified suffix.")]
        [SerializeField] private Color _petrifiedColor = new(0.62f, 0.83f, 1f);

        private const string OutlineMaterialPath = "Fonts & Materials/LiberationSans SDF - Outline";

        private TextMeshPro _text;
        private Petrifiable _petrifiable;
        private GnomeStack _stack;
        private bool _styled;

        private void Awake()
        {
            if (_character == null)
                _character = GetComponent<PlayerCharacter>();

            _petrifiable = GetComponent<Petrifiable>();
            TryGetComponent(out _stack); // Only gnomes can stack.

            var child = new GameObject("NameTag");
            child.transform.SetParent(transform, false);
            child.transform.localPosition = Vector3.up * _heightOffset;

            _text = child.AddComponent<TextMeshPro>();
            _text.font = TMP_Settings.defaultFontAsset;
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.fontSize = _fontSize;
            _text.color = Color.white;
            _text.enabled = false;
        }

        private void Start()
        {
            var outline = Resources.Load<Material>(OutlineMaterialPath);
            if (outline != null)
            {
                _text.fontSharedMaterial = outline;
            }
            else
            {
                var material = _text.fontMaterial;
                material.EnableKeyword(ShaderUtilities.Keyword_Outline);
                material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
                material.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            }

            _styled = true;
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            var visible = _styled && cam != null && ShouldShow();
            if (_text.enabled != visible)
                _text.enabled = visible;

            if (!visible)
                return;

            var tagTransform = _text.transform;
            // Raise the name above the gnomes standing on this one, or it hides under their feet.
            if (_stack != null)
                tagTransform.localPosition = Vector3.up * (_heightOffset + _stack.RidersAbove * (_stack.SeatHeight + _towerLineSpacing));

            var offset = tagTransform.position - cam.transform.position;
            var distance = offset.magnitude;
            if (offset.sqrMagnitude > 0.0001f)
                tagTransform.rotation = Quaternion.LookRotation(offset, Vector3.up);

            var scaleT = Mathf.InverseLerp(_scaleNear, _scaleFar, distance);
            tagTransform.localScale = Vector3.one * Mathf.Lerp(_minScale, _maxScale, scaleT);

            _text.text = BuildText();
            _text.alpha = 1f - Mathf.InverseLerp(_fadeStart, _fadeEnd, distance);
        }

        private bool ShouldShow()
        {
            var local = PlayerCharacter.Local;
            return local != null
                && local.Role == PlayerRole.Gnome
                && _character != null
                && _character.Role == PlayerRole.Gnome
                && _character != local;
        }

        private string BuildText()
        {
            var playerName = _character.DisplayName;
            if (_petrifiable == null || !_petrifiable.IsStone)
                return playerName;

            return $"{playerName}<color=#{ColorUtility.ToHtmlStringRGB(_petrifiedColor)}> (petrificado)</color>";
        }
    }
}
