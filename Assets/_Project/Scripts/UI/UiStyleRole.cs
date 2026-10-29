using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>What a UI element is, for the UI style tool.</summary>
    public enum UiRole
    {
        Panel,
        Frame,
        Header,
        Button,
        ButtonConfirm,
        ButtonDanger,
        Slider,
        Input,
        Title,
        Subtitle,
        Body,
        BodyOnPaper,
        Hint,
        Backdrop,
        Skip,
    }

    /// <summary>
    /// Marker read by GnoMore/Aplicar estilo de UI. The tool deduces the role the first time and then respects it,
    /// so a wrong role can be fixed by hand here (use <see cref="UiRole.Skip"/> to exclude an element and its children).
    /// </summary>
    [DisallowMultipleComponent]
    public class UiStyleRole : MonoBehaviour
    {
        [Tooltip("Style role of this element. Edit to correct what the tool deduced.")]
        [SerializeField] private UiRole _role = UiRole.Body;

        /// <summary>Current role.</summary>
        public UiRole Role
        {
            get => _role;
            set => _role = value;
        }
    }
}
