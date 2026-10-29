using BetterTogether.Match;
using BetterTogether.Networking;
using BetterTogether.Player;
using BetterTogether.UI;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Lobby screen: room code (copyable), connected players with "(tú)", who will be the next camper, who is ready, and the
/// start button (master only). The list is rebuilt only when something it shows changes (checked with an int signature,
/// no allocations), plus a slow refresh to pick up renamed players. Everything it shows is already replicated state.
/// </summary>
public class StartMatchUI : MonoBehaviour
{
    [SerializeField] Button startButton;

    [Header("Lobby (built by GnoMore/Construir lobby)")]
    [Tooltip("Label with the start button text.")]
    [SerializeField] TMP_Text startLabel;
    [Tooltip("Big room code text.")]
    [SerializeField] TMP_Text roomCodeText;
    [Tooltip("Copies the room code to the clipboard.")]
    [SerializeField] Button copyButton;
    [Tooltip("Label of the copy button (shows a confirmation for a moment).")]
    [SerializeField] TMP_Text copyLabel;
    [Tooltip("Header: connected players over room size.")]
    [SerializeField] TMP_Text playersHeader;
    [Tooltip("One text per player row, top to bottom; extra rows stay hidden.")]
    [SerializeField] TMP_Text[] playerRows;
    [Tooltip("Says who will be the camper of the next match.")]
    [SerializeField] TMP_Text camperInfo;
    [Tooltip("Shows how many players are missing to start (empty when there are enough).")]
    [SerializeField] TMP_Text statusText;
    [Tooltip("Toggles the local player's ready flag.")]
    [SerializeField] Button readyButton;
    [Tooltip("Label of the ready button.")]
    [SerializeField] TMP_Text readyLabel;

    [Tooltip("Seconds between forced refreshes (catches renamed players, which the signature cannot see).")]
    [Min(0.1f)] [SerializeField] float refreshInterval = 1f;
    [Tooltip("Seconds the copy button shows its confirmation.")]
    [Min(0.1f)] [SerializeField] float copiedSeconds = 1.5f;

    const int MaxRows = 16;
    // Rich-text colours come from the theme (cached strings); the rows sit over the dark lobby backdrop.
    static string CamperColor => UiTheme.Hex(UiTheme.Safe.roleCamper);
    static string ReadyColor => UiTheme.Hex(UiTheme.Safe.leafLight);
    static string WaitColor => UiTheme.Hex(UiTheme.WithAlpha(UiTheme.Safe.cream, 0.7f));

    readonly PlayerCharacter[] _order = new PlayerCharacter[MaxRows];
    readonly int[] _orderIds = new int[MaxRows];
    int _lastSignature;
    bool _hasSignature;
    float _nextRefresh;
    float _copiedUntil = -1f;
    string _roomCode = "";

    void Start()
    {
        startButton.onClick.AddListener(StartMatchBTN);
        if (copyButton != null) copyButton.onClick.AddListener(CopyCode);
        if (readyButton != null) readyButton.onClick.AddListener(ToggleReady);
        MatchManager.OnGameStarted += OnGameStarted;
        MatchManager.OnLobby += OnLobby;
    }

    void OnEnable()
    {
        _hasSignature = false;
        _roomCode = "";
    }

    void Update()
    {
        var manager = MatchManager.Instance;

        // Hide as soon as the countdown starts, not only when playing.
        if (manager != null && manager.Phase != MatchPhase.Waiting)
        {
            gameObject.SetActive(false);
            return;
        }

        if (manager == null)
            return;

        if (!FirstPersonCamera.IsTyping && Keyboard.current != null)
        {
            // Enter starts (master only); R toggles ready. The lobby frees the cursor, so the buttons work too.
            if (Keyboard.current.enterKey.wasPressedThisFrame && IsMaster(manager))
                StartMatchBTN();
            if (Keyboard.current.rKey.wasPressedThisFrame)
                ToggleReady();
        }

        var signature = ComputeSignature(manager);
        if (!_hasSignature || signature != _lastSignature || Time.unscaledTime >= _nextRefresh)
        {
            _hasSignature = true;
            _lastSignature = signature;
            _nextRefresh = Time.unscaledTime + refreshInterval;
            Rebuild(manager);
        }

        if (_copiedUntil > 0f && Time.unscaledTime >= _copiedUntil)
        {
            _copiedUntil = -1f;
            if (copyLabel != null) copyLabel.text = "Copiar código";
        }
    }

    void OnDestroy()
    {
        MatchManager.OnGameStarted -= OnGameStarted;
        MatchManager.OnLobby -= OnLobby;
    }

    private void OnGameStarted()
    {
        gameObject.SetActive(false);
    }

    private void OnLobby()
    {
        gameObject.SetActive(true);
    }

    static bool IsMaster(MatchManager manager) => manager.Runner != null && manager.Runner.IsSharedModeMasterClient;

    // Cheap int digest of everything the lobby shows except names: who is here, who is ready, the camper prediction,
    // whether this client is the master and the room size.
    static int ComputeSignature(MatchManager manager)
    {
        unchecked
        {
            var hash = 17;
            var all = PlayerCharacter.All;
            for (var i = 0; i < all.Count; i++)
            {
                var character = all[i];
                hash = hash * 31 + character.Object.StateAuthority.PlayerId;
                hash = hash * 31 + (character.IsReady ? 1 : 0);
            }

            hash = hash * 31 + manager.PredictedNextCamper.PlayerId;
            hash = hash * 31 + (IsMaster(manager) ? 1 : 0);
            hash = hash * 31 + manager.PlayerCount;
            return hash;
        }
    }

    // Rebuilds every text from the replicated state. Runs only when the signature changes (or on the slow refresh).
    void Rebuild(MatchManager manager)
    {
        // Players sorted by id, one entry per id (a role change can briefly leave two characters for the same player).
        var count = 0;
        var all = PlayerCharacter.All;
        for (var i = 0; i < all.Count && count < MaxRows; i++)
        {
            var character = all[i];
            if (character.Object == null || !character.Object.IsValid)
                continue;

            var id = character.Object.StateAuthority.PlayerId;
            var duplicate = false;
            for (var j = 0; j < count; j++)
                duplicate |= _orderIds[j] == id;
            if (duplicate)
                continue;

            var at = count++;
            while (at > 0 && _orderIds[at - 1] > id)
            {
                _orderIds[at] = _orderIds[at - 1];
                _order[at] = _order[at - 1];
                at--;
            }

            _orderIds[at] = id;
            _order[at] = character;
        }

        var camper = manager.PredictedNextCamper;
        var readyCount = 0;
        var localReady = false;
        for (var i = 0; i < count; i++)
        {
            var character = _order[i];
            var isLocal = character.Object.StateAuthority == manager.Runner.LocalPlayer;
            var ready = character.IsReady;
            if (ready) readyCount++;
            if (isLocal) localReady = ready;

            if (playerRows == null || i >= playerRows.Length || playerRows[i] == null)
                continue;

            var row = playerRows[i];
            row.gameObject.SetActive(true);
            var isCamper = camper != PlayerRef.None && character.Object.StateAuthority == camper;
            row.text = string.Concat(
                character.DisplayName,
                isLocal ? " (tú)" : "",
                isCamper ? $"   <color={CamperColor}>CAMPISTA</color>" : "",
                ready ? $"   <color={ReadyColor}>LISTO</color>" : $"   <color={WaitColor}>esperando</color>");
        }

        if (playerRows != null)
        {
            for (var i = count; i < playerRows.Length; i++)
            {
                if (playerRows[i] != null)
                    playerRows[i].gameObject.SetActive(false);
            }
        }

        // _order holds references to characters; drop them so a despawned player isn't kept alive.
        for (var i = 0; i < count; i++)
            _order[i] = null;

        var max = ConnectionManager.Instance != null ? ConnectionManager.Instance.MaxPlayers : MaxRows;
        var total = manager.PlayerCount;
        var min = manager.MinPlayers;
        var master = IsMaster(manager);

        if (playersHeader != null) playersHeader.text = $"Jugadores {total}/{max}";

        if (camperInfo != null)
        {
            camperInfo.text = camper != PlayerRef.None
                ? $"Campista de la próxima partida: <color={CamperColor}>{NameOf(camper)}</color>"
                : "El campista se sortea al empezar";
        }

        if (statusText != null)
        {
            var missing = min - total;
            statusText.text = missing > 0
                ? $"Faltan {missing} {(missing == 1 ? "jugador" : "jugadores")} para empezar"
                : master ? "" : "Esperando a que el anfitrión empiece";
        }

        startButton.gameObject.SetActive(master);
        startButton.interactable = total >= min;
        if (startLabel != null) startLabel.text = $"Empezar ({readyCount}/{total} listos)";

        if (readyLabel != null) readyLabel.text = localReady ? "Quitar listo [R]" : "Estoy listo [R]";

        if (string.IsNullOrEmpty(_roomCode))
        {
            var runner = ConnectionManager.Instance != null ? ConnectionManager.Instance.Runner : null;
            _roomCode = runner != null && runner.SessionInfo.IsValid ? runner.SessionInfo.Name : "";
            if (roomCodeText != null) roomCodeText.text = string.IsNullOrEmpty(_roomCode) ? "—" : _roomCode;
        }

        if (copyButton != null) copyButton.interactable = !string.IsNullOrEmpty(_roomCode);
    }

    static string NameOf(PlayerRef player)
    {
        var all = PlayerCharacter.All;
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Object != null && all[i].Object.IsValid && all[i].Object.StateAuthority == player)
                return all[i].DisplayName;
        }

        return $"Jugador {player.PlayerId}";
    }

    void CopyCode()
    {
        if (string.IsNullOrEmpty(_roomCode))
            return;

        GUIUtility.systemCopyBuffer = _roomCode;
        if (copyLabel != null) copyLabel.text = "¡Copiado!";
        _copiedUntil = Time.unscaledTime + copiedSeconds;
    }

    void ToggleReady()
    {
        var local = PlayerCharacter.Local;
        if (local == null)
            return;

        // Flip from what is replicated so a double click can't desync the label; the replicated change refreshes it.
        var next = !local.IsReady;
        local.SetReady(next);
        if (readyLabel != null) readyLabel.text = next ? "Quitar listo [R]" : "Estoy listo [R]";
    }

    void StartMatchBTN()
    {
        if (MatchManager.Instance != null)
            MatchManager.Instance.StartMatch();
    }
}
