using BetterTogether.Fireplace;
using BetterTogether.Match;
using BetterTogether.Player;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.UI
{
    /// <summary>
    /// The camp notebook: same paper style as the plan. Held with the map key in a level without a plan. It tells each team its goal
    /// and tools, and the camper's recipe ticks itself from replicated state. Local presentation, nothing is networked.
    /// </summary>
    public class ManualHUD : MonoBehaviour
    {
        private const string MapActionPath = "Player/Map";
        private const float RefreshSeconds = 0.5f;

        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _body;

        private InputAction _action;
        private float _nextRefresh;

        private void Awake() => _panel.SetActive(false);

        private void LateUpdate()
        {
            var open = WantsOpen();
            if (open != _panel.activeSelf)
            {
                _panel.SetActive(open);
                _nextRefresh = 0f;
            }

            if (!open || Time.unscaledTime < _nextRefresh)
                return;

            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            Refresh();
        }

        private bool WantsOpen()
        {
            var match = MatchManager.Instance;
            if (match == null || !match.TravesurasRunning || PlayerCharacter.Local == null || MinimapArea.Current != null)
                return false;

            _action ??= InputSystem.actions != null ? InputSystem.actions.FindAction(MapActionPath) : null;
            return _action != null && _action.IsPressed() && !FirstPersonCamera.CursorFree && !FirstPersonCamera.IsTyping;
        }

        private void Refresh()
        {
            var match = MatchManager.Instance;
            var needed = TravesuraLayout.Current != null ? TravesuraLayout.Current.Needed : 0;
            if (PlayerCharacter.Local.Role == PlayerRole.Camper)
            {
                var fire = FireplaceManager.Instance;
                var wood = fire != null ? fire.WoodCount : 0;
                var required = fire != null ? fire.RequiredWood : 0;
                var lit = fire != null && fire.IsLit;
                _title.text = "Manual del Campista";
                _body.text = "Tu objetivo: <b>comerte la salchicha</b> antes de que los gnomos completen " + needed + " travesuras.\n\n"
                             + Step(wood >= required, "Lleva " + required + " leños a la fogata (" + wood + " de " + required + ")") + "\n"
                             + Step(lit, "Coge los fósforos y enciende el fuego") + "\n"
                             + Step(false, "Pon la salchicha en la parrilla") + "\n"
                             + Step(false, "Cómetela cuando esté lista, antes de que se queme") + "\n\n"
                             + "Haz clic derecho para levantar la cámara y paraliza a los gnomos.\n"
                             + "Carga al gnomo paralizado hasta la jaula. Con las manos llenas no hay cámara.\n"
                             + "Cuidado: algunas estatuas explotan.";
                return;
            }

            _title.text = "Manual del Gnomo";
            _body.text = "Vuestro objetivo: <b>completar " + needed + " travesuras</b> antes de que el campista cene.\n\n"
                         + "Solo hay una travesura activa. Al principio solo la lee el Jefe: ¡hablad!\n"
                         + "Cada una necesita a dos gnomos distintos.\n\n"
                         + "<b>G</b>  Dejar una estatua tuya\n"
                         + "<b>H</b>  Dejar una estatua explosiva\n"
                         + "<b>C</b>  Agachado en la hierba alta no te ven; quieto, eres un adorno\n"
                         + "<b>Q</b>  Bailar y burlarte\n"
                         + "<b>F</b>  Marcar un lugar\n"
                         + "<b>X</b>  Levantar la sartén justo antes del flash\n"
                         + "<b>E</b>  Saltar por un hueco, con las manos vacías\n"
                         + "Subid a un compañero para hacer una torre y llegar alto.\n"
                         + "Mantén <b>E</b> sobre un compañero en piedra para liberarlo. Si está enjaulado, id a la jaula.";
        }

        private static string Step(bool done, string text) => done ? "<s>" + text + "</s>" : text;
    }
}
