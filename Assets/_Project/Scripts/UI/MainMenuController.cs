using System.Threading.Tasks;
using BetterTogether.Networking;
using BetterTogether.Player;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// Main menu: create or join a room by code, set the player name, credits and quit.
    /// Connects through <see cref="ConnectionManager"/>, which loads the gameplay scene on success.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        private enum JoinMode
        {
            Create,
            Join,
        }

        private const string RoomCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int GeneratedCodeLength = 5;
        private const int MaxRoomCodeLength = 8;
        private const int MinRoomCodeLength = 3;
        private const string LastRoomKey = "BetterTogether.LastRoom";

        private static readonly Color FallbackErrorColor = new Color(1f, 0.45f, 0.4f);
        private static Color ErrorColor => UiTheme.Current != null ? UiTheme.Current.hat : FallbackErrorColor;

        [SerializeField] Button createRoomBTN;
        [SerializeField] Button joinRoomBTN;
        [SerializeField] Button creditsBTN;
        [SerializeField] Button exitBTN;
        [Tooltip("Optional: opens the settings panel (built by GnoMore/Construir ajustes).")]
        [SerializeField] Button settingsBTN;
        [SerializeField] SettingsPanel settingsPanel;
        [Tooltip("Optional: opens the public games panel (built by GnoMore/Construir partidas públicas).")]
        [SerializeField] Button publicBTN;
        [SerializeField] PublicLobbyPanel publicPanel;
        [SerializeField] Button joinBTN;
        [SerializeField] Button creditsCloseBTN;
        [SerializeField] GameObject credits;
        [SerializeField] GameObject mainMenu;
        [SerializeField] GameObject joinUI;
        [SerializeField] TMP_InputField roomName;
        [SerializeField] TMP_InputField playerName;
        [Tooltip("Label for validation, progress and connection errors. Created at runtime next to the join button if left empty.")]
        [SerializeField] TMP_Text _status;

        private bool _busy;

        private void Awake()
        {
            createRoomBTN.onClick.AddListener(OpenCreateMenu);
            joinRoomBTN.onClick.AddListener(OpenJoinMenu);
            creditsBTN.onClick.AddListener(ShowCredits);
            exitBTN.onClick.AddListener(ExitGame);
            joinBTN.onClick.AddListener(OnSubmitClicked);
            creditsCloseBTN.onClick.AddListener(CreditClose);
            if (settingsBTN != null)
                settingsBTN.onClick.AddListener(OpenSettings);
            if (publicBTN != null)
                publicBTN.onClick.AddListener(OpenPublic);
            if (publicPanel != null)
                publicPanel.Closed += OnPublicClosed;

            roomName.characterLimit = MaxRoomCodeLength;
            roomName.onValueChanged.AddListener(OnRoomNameChanged);
            playerName.characterLimit = LocalPlayerProfile.MaxNameLength;

            EnsureStatusLabel();

            mainMenu.SetActive(true);
            joinUI.SetActive(false);
            credits.SetActive(false);
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            var pending = ConnectionManager.PendingMenuMessage;
            if (!string.IsNullOrEmpty(pending))
            {
                ConnectionManager.PendingMenuMessage = null;
                OpenJoinPanel(JoinMode.Join);
                SetStatus(pending, true);
            }
        }

        private void OnDestroy()
        {
            createRoomBTN.onClick.RemoveListener(OpenCreateMenu);
            joinRoomBTN.onClick.RemoveListener(OpenJoinMenu);
            creditsBTN.onClick.RemoveListener(ShowCredits);
            exitBTN.onClick.RemoveListener(ExitGame);
            joinBTN.onClick.RemoveListener(OnSubmitClicked);
            creditsCloseBTN.onClick.RemoveListener(CreditClose);
            if (settingsBTN != null)
                settingsBTN.onClick.RemoveListener(OpenSettings);
            if (publicBTN != null)
                publicBTN.onClick.RemoveListener(OpenPublic);
            if (publicPanel != null)
                publicPanel.Closed -= OnPublicClosed;
            roomName.onValueChanged.RemoveListener(OnRoomNameChanged);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (settingsPanel != null && settingsPanel.IsOpen)
                return; // the panel closes itself with Esc

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (credits.activeSelf)
                    CreditClose();
                else if (publicPanel != null && publicPanel.IsOpen)
                    publicPanel.Close();
                else if (joinUI.activeSelf && !_busy)
                    BackToMainMenu();
            }
            else if (joinUI.activeSelf && !_busy
                && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            {
                OnSubmitClicked();
            }
        }

        private void OpenSettings()
        {
            if (settingsPanel != null)
                settingsPanel.Open();
        }

        private void OpenPublic()
        {
            if (publicPanel == null)
                return;

            mainMenu.SetActive(false);
            publicPanel.Open();
        }

        private void OnPublicClosed() => mainMenu.SetActive(true);

        private void CreditClose() => credits.SetActive(false);

        private void ShowCredits() => credits.SetActive(true);

        private void ExitGame() => Application.Quit();

        private void OpenCreateMenu() => OpenJoinPanel(JoinMode.Create);

        private void OpenJoinMenu() => OpenJoinPanel(JoinMode.Join);

        private void BackToMainMenu()
        {
            joinUI.SetActive(false);
            mainMenu.SetActive(true);
            SetStatus(null);
        }

        /// <summary>Opens the connect panel: Create fills a fresh random code, Join prefills the last used one.</summary>
        private void OpenJoinPanel(JoinMode mode)
        {
            mainMenu.SetActive(false);
            joinUI.SetActive(true);

            roomName.text = mode == JoinMode.Create ? GenerateRoomCode() : PlayerPrefs.GetString(LastRoomKey, "");
            playerName.text = LocalPlayerProfile.Name;
            SetStatus(null);
            SetInteractable(true);
        }

        private static string GenerateRoomCode()
        {
            var chars = new char[GeneratedCodeLength];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = RoomCodeChars[Random.Range(0, RoomCodeChars.Length)];
            return new string(chars);
        }

        private void OnRoomNameChanged(string value)
        {
            var upper = value.ToUpperInvariant();
            if (upper != value)
                roomName.SetTextWithoutNotify(upper);
        }

        private void OnSubmitClicked() => _ = SubmitAsync();

        private async Task SubmitAsync()
        {
            if (_busy)
                return;

            var code = roomName.text.Trim().ToUpperInvariant();
            if (code.Length < MinRoomCodeLength)
            {
                SetStatus("Escribe el código de la sala", true);
                return;
            }

            var manager = ConnectionManager.Instance != null ? ConnectionManager.Instance : FindAnyObjectByType<ConnectionManager>();
            if (manager == null)
            {
                SetStatus("No se encontró el gestor de conexión", true);
                return;
            }

            var chosenName = LocalPlayerProfile.Sanitize(playerName.text);
            if (string.IsNullOrEmpty(chosenName))
                chosenName = $"Duende {Random.Range(1000, 10000)}";
            LocalPlayerProfile.Name = chosenName;
            playerName.SetTextWithoutNotify(LocalPlayerProfile.Name);

            PlayerPrefs.SetString(LastRoomKey, code);
            PlayerPrefs.Save();

            _busy = true;
            SetInteractable(false);
            SetStatus("Conectando…");

            var ok = false;
            try
            {
                ok = await manager.JoinOrCreate(code);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
            }

            // On success the gameplay scene is loading and this menu goes away with it.
            if (this == null || ok)
                return;

            _busy = false;
            SetInteractable(true);
            SetStatus(string.IsNullOrEmpty(manager.LastError) ? "No se pudo conectar" : manager.LastError, true);
        }

        private void SetInteractable(bool value)
        {
            createRoomBTN.interactable = value;
            joinRoomBTN.interactable = value;
            if (publicBTN != null)
                publicBTN.interactable = value;
            creditsBTN.interactable = value;
            exitBTN.interactable = value;
            joinBTN.interactable = value;
            roomName.interactable = value;
            playerName.interactable = value;
        }

        private void SetStatus(string message, bool isError = false)
        {
            if (_status == null)
                return;

            _status.text = message ?? "";
            _status.color = isError ? ErrorColor : Color.white;
            _status.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        /// <summary>Uses the assigned status label or builds one as the last child of the join button's layout group.</summary>
        private void EnsureStatusLabel()
        {
            if (_status == null)
            {
                var go = new GameObject("Status", typeof(RectTransform));
                go.transform.SetParent(joinBTN.transform.parent, false);
                go.transform.SetAsLastSibling();

                var label = go.AddComponent<TextMeshProUGUI>();
                var source = roomName.textComponent;
                if (source != null)
                {
                    label.font = source.font;
                    label.fontSize = source.fontSize;
                }
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.Normal;
                label.raycastTarget = false;

                var layout = go.AddComponent<LayoutElement>();
                layout.preferredHeight = 60f;

                _status = label;
            }

            SetStatus(null);
        }
    }
}
