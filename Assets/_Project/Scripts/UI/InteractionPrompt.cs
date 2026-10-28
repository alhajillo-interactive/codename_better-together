using System.Collections.Generic;
using BetterTogether.Combat;
using BetterTogether.Fireplace;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// Crosshair and interaction hint for the local player, drawn with uGUI/TMP on the HUD canvas.
    /// The text is resolved a few times per second and only written to the label when it changes.
    /// </summary>
    public class InteractionPrompt : MonoBehaviour
    {
        [Header("References (wired by GameHudBuilder)")]
        [Tooltip("Dot at the centre of the screen.")]
        [SerializeField] private Image _crosshair;
        [Tooltip("Panel under the crosshair (background plus label); hidden when there is nothing to say.")]
        [SerializeField] private GameObject _promptRoot;
        [Tooltip("Label inside the prompt panel.")]
        [SerializeField] private TMP_Text _promptText;
        [Tooltip("Red warning at the top shown to a gnome the camper is looking at.")]
        [SerializeField] private TMP_Text _watchedWarning;

        [Header("Style")]
        [Tooltip("Colour of the prompt text.")]
        [SerializeField] private Color _textColor = Color.white;
        [Tooltip("Colour of the crosshair dot.")]
        [SerializeField] private Color _crosshairColor = new(1f, 1f, 1f, 0.8f);
        [Tooltip("Colour of the 'the camper is looking at you' warning.")]
        [SerializeField] private Color _watchedColor = new(1f, 0.25f, 0.2f);

        [Header("Behaviour")]
        [Tooltip("Seconds between prompt re-evaluations. The text is built from strings, so it is not rebuilt every frame.")]
        [SerializeField] private float _refreshInterval = 0.1f;

        private bool _valid;
        private float _nextRefresh;
        private string _shownText;

        private void Awake()
        {
            _valid = HudSupport.Require(this, _crosshair, nameof(_crosshair))
                     & HudSupport.Require(this, _promptRoot, nameof(_promptRoot))
                     & HudSupport.Require(this, _promptText, nameof(_promptText))
                     & HudSupport.Require(this, _watchedWarning, nameof(_watchedWarning));
            if (!_valid)
                return;

            _crosshair.color = _crosshairColor;
            _crosshair.enabled = false;
            _promptText.color = _textColor;
            _watchedWarning.color = _watchedColor;
            _promptRoot.SetActive(false);
            _watchedWarning.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_valid)
                return;

            var local = PlayerInteractor.Local;
            if (local == null)
            {
                HideAll();
                return;
            }

            HudSupport.SetEnabled(_crosshair, true);

            // Tells a gnome the camper is looking at it, so it knows why it can't move.
            var watched = Petrifiable.Local != null && Petrifiable.Local.IsWatched;
            HudSupport.SetActive(_watchedWarning.gameObject, watched);

            var now = Time.unscaledTime;
            if (now < _nextRefresh)
                return;

            _nextRefresh = now + _refreshInterval;
            ShowText(ResolveText(local));
        }

        private void HideAll()
        {
            HudSupport.SetEnabled(_crosshair, false);
            HudSupport.SetActive(_watchedWarning.gameObject, false);
            HudSupport.SetActive(_promptRoot, false);
            _shownText = null;
            _nextRefresh = 0f;
        }

        private void ShowText(string text)
        {
            if (text == null)
            {
                HudSupport.SetActive(_promptRoot, false);
                _shownText = null;
                return;
            }

            HudSupport.SetActive(_promptRoot, true);
            if (string.Equals(text, _shownText))
                return;

            _shownText = text;
            _promptText.text = text;
        }

        private static string ResolveText(PlayerInteractor local)
        {
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

            return text;
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
            else if (fire != null && fire.PourBlockedMessage(local) is { } pourBlocked)
                lines.Add(pourBlocked);
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

    }
}
