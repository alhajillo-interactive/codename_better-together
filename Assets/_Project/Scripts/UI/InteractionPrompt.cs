using BetterTogether.Combat;
using BetterTogether.Interaction;
using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Crosshair and interaction hint for the local player.
    /// IMGUI placeholder until the game has a real HUD.
    /// </summary>
    public class InteractionPrompt : MonoBehaviour
    {
        [SerializeField] private int _fontSize = 20;
        [SerializeField] private Color _textColor = Color.white;
        [SerializeField] private Color _crosshairColor = new(1f, 1f, 1f, 0.8f);
        [SerializeField] private float _crosshairSize = 6f;
        [SerializeField] private float _promptOffset = 40f;

        private GUIStyle _style;

        private void OnGUI()
        {
            var local = PlayerInteractor.Local;
            if (local == null)
                return;

            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var previousColor = GUI.color;
            GUI.color = _crosshairColor;
            GUI.DrawTexture(
                new Rect(center.x - _crosshairSize * 0.5f, center.y - _crosshairSize * 0.5f, _crosshairSize, _crosshairSize),
                Texture2D.whiteTexture);
            GUI.color = previousColor;

            string text = null;
            var carried = local.Carried;
            if (local.TryGetComponent<Kickable>(out var kickable) && kickable.IsStunned)
            {
                text = "¡Aturdido!";
            }
            else if (local.Focused != null && local.Focused.CanInteract(local))
            {
                text = "[E] " + local.Focused.GetPrompt(local);
            }
            else if (CamperKick.Local is { HasTargetInRange: true, IsReady: true })
            {
                text = "[Clic] Patear";
            }
            else if (carried != null)
            {
                var status = carried.StatusFor(local);
                text = status != null ? status + "\n[E] Soltar" : "[E] Soltar";
            }

            if (text == null)
                return;

            _style ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Bold,
            };
            _style.fontSize = _fontSize;
            _style.normal.textColor = _textColor;

            GUI.Label(new Rect(center.x - 200f, center.y + _promptOffset - 15f, 400f, 60f), text, _style);
        }
    }
}
