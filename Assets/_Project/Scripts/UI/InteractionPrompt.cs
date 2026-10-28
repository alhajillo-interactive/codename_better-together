using System.Collections.Generic;
using BetterTogether.Combat;
using BetterTogether.Fireplace;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Player;
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

        [SerializeField] private Color _watchedColor = new(1f, 0.25f, 0.2f);

        private GUIStyle _style;
        private GUIStyle _watchedStyle;

        /// <summary>Tells a gnome the camper is looking at it, so it knows why it can't move.</summary>
        private void DrawWatchedWarning()
        {
            if (Petrifiable.Local == null || !Petrifiable.Local.IsWatched)
                return;

            _watchedStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            _watchedStyle.fontSize = Mathf.RoundToInt(_fontSize * 1.3f);
            _watchedStyle.normal.textColor = _watchedColor;

            GUI.Label(new Rect(Screen.width * 0.5f - 250f, Screen.height * 0.15f, 500f, 40f),
                "¡El campista te está mirando! No te muevas", _watchedStyle);
        }

        // What to tell a player with something in the hands: status, passing it on (gnomes in a tower), what E does, getting off.
        private static string BuildCarriedText(PlayerInteractor local, Carryable carried)
        {
            var lines = new List<string>(4);
            var status = carried.StatusFor(local);
            if (status == null && carried.TryGetComponent<Sausage>(out var sausage))
                status = $"Salchicha: {sausage.StateLabel}";

            var fire = FireplaceManager.Instance;
            if (status == null && fire != null && !fire.IsLit && !fire.HasEnoughWood && carried.TryGetComponent<Matches>(out _))
                status = $"Faltan {fire.RequiredWood - fire.WoodCount} leños";

            var hasBucket = carried.TryGetComponent<WaterBucket>(out var bucket);
            if (status == null && hasBucket)
                status = bucket.IsFull ? "Cubeta: llena" : "Cubeta: vacía";

            if (status != null)
                lines.Add(status);

            var stack = GnomeStack.Local;
            if (stack != null)
            {
                AddLine(lines, PassLine(local, carried, stack.Rider, "[Clic]", "arriba"));
                AddLine(lines, PassLine(local, carried, stack.Mount, "[Clic der.]", "abajo"));
            }

            if (fire != null && fire.CanPlace(local))
                lines.Add($"[E] Colocar {carried.DisplayName} ({fire.WoodCount + 1}/{fire.RequiredWood})");
            else if (hasBucket && bucket.CanFill(local))
                lines.Add("[E] Llenar cubeta");
            else if (fire != null && fire.CanPour(local))
                lines.Add("[E] ¡Apagar fogata!");
            else
                lines.Add(local.EdibleCarried != null ? "[E] Comer salchicha" : "[E] Soltar");

            if (stack != null && stack.IsRiding && Petrifiable.Local is not { IsStone: true })
                lines.Add("[Espacio] Bajarte");

            return string.Join("\n", lines);
        }

        private static string PassLine(PlayerInteractor local, Carryable carried, GnomeStack target, string key, string direction)
        {
            if (target == null || target.Interactor == null)
                return null;

            if (carried.CanHandOff(local, target.Interactor, out var reason))
                return $"{key} Pasar {carried.DisplayName} {direction}";

            return reason;
        }

        private static void AddLine(List<string> lines, string line)
        {
            if (line != null)
                lines.Add(line);
        }


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

            DrawWatchedWarning();

            string text = null;
            var carried = local.Carried;
            if (local.TryGetComponent<Kickable>(out var kickable) && kickable.IsStunned)
            {
                text = "¡Aturdido!";
            }
            else if (carried == null && GnomeStack.Local != null && GnomeStack.Local.IsRiding && Petrifiable.Local is not { IsStone: true })
            {
                text = "[Espacio] Bajarte";
            }
            else if (local.Focused == null && carried == null && GnomeStack.Local != null && GnomeStack.Local.Aimed != null)
            {
                var stack = GnomeStack.Local;
                text = stack.CanClimbAimed
                    ? $"[Clic] Subirte a {stack.Aimed.GetComponent<PlayerCharacter>().DisplayName}"
                    : stack.ClimbBlockReason;
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
                text = BuildCarriedText(local, carried);
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

            GUI.Label(new Rect(center.x - 200f, center.y + _promptOffset - 15f, 400f, 120f), text, _style);
        }
    }
}
