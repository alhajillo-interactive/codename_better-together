using System.Collections.Generic;
using BetterTogether.Combat;
using BetterTogether.Diagnostics;
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
        [Tooltip("Colour of the crosshair dot.")]
        [SerializeField] private Color _crosshairColor = new(1f, 1f, 1f, 0.8f); // ui-color-ok: serialized default for a world/scene-space graphic (tunable in the prefab), not themed text
        [Tooltip("Colour of the 'the camper is looking at you' warning.")]
        [SerializeField] private Color _watchedColor = new(1f, 0.25f, 0.2f); // ui-color-ok: serialized default for a world/scene-space graphic (tunable in the prefab), not themed text

        [Header("Behaviour")]
        [Tooltip("Seconds between prompt re-evaluations. The text is built from strings, so it is not rebuilt every frame.")]
        [SerializeField] private float _refreshInterval = 0.1f;

        private static readonly List<string> LineBuffer = new(4);

        // Set by ResolveText when the text it returned explains why something cannot be done (usability telemetry).
        private static bool _resolvedDenied;

        private object _lastFocused;
        private Carryable _lastCarried;
        private GnomeStack _lastAimed;
        private bool _lastStunned;
        private bool _lastRiding;
        private bool _lastKickReady;
        private bool _lastConfirmed;
        private int _lastHoldStep = -1;

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
            _promptText.color = UiTheme.Safe.cream; // cream on the dark pill (night80)
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

            // Channel A: one thing in the centre. A petrified or caged gnome sees its own widget only, so no prompt.
            if (Petrifiable.Local is { IsStone: true } or { IsLockedInCage: true })
            {
                HudSupport.SetActive(_promptRoot, false);
                HudSupport.SetActive(_watchedWarning.gameObject, false);
                _shownText = null;
                _nextRefresh = 0f;
                return;
            }

            // C8.2: the "camper is looking at you" text is gone; GazeFeedbackHUD shows the three-level lens glint instead.
            HudSupport.SetActive(_watchedWarning.gameObject, false);

            // A change in anything that decides the prompt forces an immediate refresh.
            if (DecidingStateChanged(local))
                _nextRefresh = 0f;

            var now = Time.unscaledTime;
            if (now < _nextRefresh)
                return;

            _nextRefresh = now + _refreshInterval;
            ShowText(ResolveText(local));
        }

        // Compares the references and flags that pick the branch in ResolveText; allocation-free.
        private bool DecidingStateChanged(PlayerInteractor local)
        {
            var focused = local.Focused;
            var carried = local.CarriedOrPredicted;
            var stunned = local.TryGetComponent<Kickable>(out var kickable) && kickable.IsStunned;
            var stack = GnomeStack.Local;
            var riding = stack != null && stack.IsRiding;
            var aimed = stack != null ? stack.Aimed : null;
            var kickReady = CamperKick.Local is { HasTargetInRange: true, IsReady: true };
            var confirmed = local.Carried != null;
            var holdStep = local.HoldProgress > 0f ? local.HoldPercentStep : -1;

            var changed = holdStep != _lastHoldStep
                          || !ReferenceEquals(focused, _lastFocused)
                          || carried != _lastCarried
                          || stunned != _lastStunned
                          || riding != _lastRiding
                          || aimed != _lastAimed
                          || kickReady != _lastKickReady
                          || confirmed != _lastConfirmed;

            _lastFocused = focused;
            _lastCarried = carried;
            _lastStunned = stunned;
            _lastRiding = riding;
            _lastAimed = aimed;
            _lastKickReady = kickReady;
            _lastConfirmed = confirmed;
            _lastHoldStep = holdStep;
            return changed;
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
            if (Telemetry.Enabled && _resolvedDenied) Telemetry.Begin("prompt_denied").Str("text", text).Send();
        }

        private static string ResolveText(PlayerInteractor local)
        {
            string text = null;
            _resolvedDenied = false;
            var carried = local.CarriedOrPredicted;
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
                _resolvedDenied = !stack.CanClimbAimed;
            }
            else if (carried != null && local.EdibleCarried != null)
            {
                // Mirrors PlayerInteractor: eating the cooked sausage wins over whatever is in focus.
                text = BuildCarriedText(local, carried);
            }
            else if (local.Focused != null && local.Focused.CanInteract(local))
            {
                text = local.Focused is IHoldInteractable hold && hold.GetHoldTime(local) > 0f
                    ? HoldText(local, local.Focused.GetPrompt(local))
                    : "[E] " + local.Focused.GetPrompt(local);
            }
            else if (local.TryGetComponent<GnomeCarrier>(out var gnomeCarrier) && gnomeCarrier.Carried != null)
            {
                text = $"[E] Soltar a {gnomeCarrier.Carried.DisplayName}";
            }
            else if (CamperKick.Local is { HasTargetInRange: true, IsReady: true })
            {
                text = "[Clic] Patear";
            }
            else if (carried != null)
            {
                // Grab not confirmed yet: E does nothing, so no key is shown until the authority answers.
                text = local.Carried == null ? $"Tomando {carried.DisplayName}..." : BuildCarriedText(local, carried);
            }

            return text;
        }

        // "[Mantén E] Registrar arbusto", with the progress in steps of 10 % while the key is held.
        private static string HoldText(PlayerInteractor local, string prompt) =>
            local.HoldProgress > 0f ? $"[Mantén E] {prompt}… {local.HoldPercentStep} %" : "[Mantén E] " + prompt;

        // What to tell a player with something in the hands: status, passing it on (gnomes in a tower), what E does, getting off.
        private static string BuildCarriedText(PlayerInteractor local, Carryable carried)
        {
            var lines = LineBuffer;
            lines.Clear();
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
            {
                lines.Add(pourBlocked);
                _resolvedDenied = true;
            }
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

            _resolvedDenied |= reason != null;
            return reason;
        }

        private static void AddLine(List<string> lines, string line)
        {
            if (line != null)
                lines.Add(line);
        }

    }
}
