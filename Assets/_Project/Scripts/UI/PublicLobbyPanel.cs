using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterTogether.Networking;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// "Partidas públicas" panel of the main menu (C4.3): lists the open public rooms (name, players, state) with a
    /// Join button each, plus Create public and Quick join. The session list comes from the browsing runner of
    /// <see cref="ConnectionManager"/>, which runs only while this panel is open. Built by GnoMore/Construir partidas públicas.
    /// </summary>
    public class PublicLobbyPanel : MonoBehaviour
    {
        private static Color ErrorColor => UiTheme.Safe.hatDark;

        [Tooltip("Root of the panel; shown by Open and hidden by Close.")]
        [SerializeField] private GameObject _content;
        [Tooltip("Parent of the session rows (a vertical layout group).")]
        [SerializeField] private RectTransform _listRoot;
        [Tooltip("Inactive row to clone: children 'Name', 'Players', 'State' (TMP_Text) and 'Join' (Button).")]
        [SerializeField] private GameObject _rowTemplate;
        [Tooltip("Shown while there are no rooms in the list.")]
        [SerializeField] private TMP_Text _emptyLabel;
        [Tooltip("Progress and error messages (Spanish).")]
        [SerializeField] private TMP_Text _status;
        [SerializeField] private Button _createButton;
        [SerializeField] private Button _quickJoinButton;
        [SerializeField] private Button _backButton;

        private sealed class Row
        {
            public GameObject Root;
            public TMP_Text Name;
            public TMP_Text Players;
            public TMP_Text State;
            public Button Join;
            public SessionInfo Session;
        }

        private readonly List<Row> _rows = new List<Row>(16);
        private bool _busy;
        private bool _open;

        /// <summary>Raised when the player leaves the panel (Back button); the menu shows its main buttons again.</summary>
        public event Action Closed;

        public bool IsOpen => _open;
        public bool IsBusy => _busy;

        private void Awake()
        {
            if (_createButton != null)
                _createButton.onClick.AddListener(OnCreateClicked);
            if (_quickJoinButton != null)
                _quickJoinButton.onClick.AddListener(OnQuickJoinClicked);
            if (_backButton != null)
                _backButton.onClick.AddListener(Close);
            if (_rowTemplate != null)
                _rowTemplate.SetActive(false);
            if (_content != null)
                _content.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_createButton != null)
                _createButton.onClick.RemoveListener(OnCreateClicked);
            if (_quickJoinButton != null)
                _quickJoinButton.onClick.RemoveListener(OnQuickJoinClicked);
            if (_backButton != null)
                _backButton.onClick.RemoveListener(Close);

            // Leaving the menu scene: the browsing runner must not outlive the list that uses it.
            StopBrowsing();
        }

        public void Open()
        {
            _open = true;
            if (_content != null)
                _content.SetActive(true);
            SetStatus(null);
            Refresh(Array.Empty<SessionInfo>());
            SetInteractable(true);
            _ = BeginBrowsing();
        }

        public void Close()
        {
            if (_busy)
                return;

            _open = false;
            StopBrowsing();
            if (_content != null)
                _content.SetActive(false);
            Closed?.Invoke();
        }

        private async Task BeginBrowsing()
        {
            var manager = ConnectionManager.Instance;
            if (manager == null)
            {
                SetStatus("No se encontró el gestor de conexión", true);
                return;
            }

            manager.SessionsUpdated -= Refresh; // never subscribe twice when the list is restarted after an error
            manager.SessionsUpdated += Refresh;
            SetStatus("Buscando partidas…");

            var ok = await manager.StartBrowsing();
            if (this == null || !_open)
                return;

            if (ok)
            {
                SetStatus(null);
                Refresh(manager.Sessions);
            }
            else
            {
                SetStatus(string.IsNullOrEmpty(manager.LastError) ? "No se pudo cargar la lista de partidas" : manager.LastError, true);
            }
        }

        private void StopBrowsing()
        {
            var manager = ConnectionManager.Instance;
            if (manager == null)
                return;

            manager.SessionsUpdated -= Refresh;
            _ = manager.StopBrowsing();
        }

        /// <summary>Fills the pooled rows with the joinable sessions: visible and open, newest names as sent by Photon.</summary>
        private void Refresh(IReadOnlyList<SessionInfo> sessions)
        {
            if (!_open || _listRoot == null || _rowTemplate == null)
                return;

            var shown = 0;
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session == null || !session.IsValid || !session.IsVisible || !session.IsOpen)
                    continue;
                if (session.Properties == null || !session.Properties.ContainsKey(ConnectionManager.PublicKey))
                    continue;

                var row = GetRow(shown++);
                var full = session.PlayerCount >= session.MaxPlayers;
                var inGame = ConnectionManager.IsInGame(session);
                row.Session = session;
                row.Name.text = session.Name;
                row.Players.text = $"{session.PlayerCount}/{session.MaxPlayers}";
                row.State.text = full ? "Llena" : inGame ? "En partida" : "En sala";
                row.Join.interactable = !full && !_busy;
                row.Root.SetActive(true);
            }

            for (var i = shown; i < _rows.Count; i++)
            {
                _rows[i].Session = null;
                _rows[i].Root.SetActive(false);
            }

            if (_emptyLabel != null)
                _emptyLabel.gameObject.SetActive(shown == 0);
        }

        private Row GetRow(int index)
        {
            if (index < _rows.Count)
                return _rows[index];

            var go = Instantiate(_rowTemplate, _listRoot);
            go.name = "Row";
            var row = new Row
            {
                Root = go,
                Name = go.transform.Find("Name").GetComponent<TMP_Text>(),
                Players = go.transform.Find("Players").GetComponent<TMP_Text>(),
                State = go.transform.Find("State").GetComponent<TMP_Text>(),
                Join = go.transform.Find("Join").GetComponent<Button>(),
            };
            row.Join.onClick.AddListener(() => _ = JoinAsync(row));
            _rows.Add(row);
            return row;
        }

        private async Task JoinAsync(Row row)
        {
            if (row.Session == null)
                return;

            var session = row.Session;
            await RunAsync("Entrando…", manager => manager.JoinPublic(session));
        }

        private void OnCreateClicked() => _ = RunAsync("Creando partida…", manager => manager.CreatePublic());

        private void OnQuickJoinClicked() => _ = RunAsync("Buscando partida…", manager => manager.QuickJoin());

        /// <summary>Runs a connection attempt; on success the gameplay scene loads and this menu goes away with it.</summary>
        private async Task RunAsync(string progress, Func<ConnectionManager, Task<bool>> action)
        {
            if (_busy)
                return;

            var manager = ConnectionManager.Instance;
            if (manager == null)
            {
                SetStatus("No se encontró el gestor de conexión", true);
                return;
            }

            _busy = true;
            SetInteractable(false);
            SetStatus(progress);

            var ok = false;
            try
            {
                ok = await action(manager);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }

            if (this == null || ok)
                return;

            _busy = false;
            SetInteractable(true);
            SetStatus(string.IsNullOrEmpty(manager.LastError) ? "No se pudo conectar" : manager.LastError, true);

            // The browsing runner was shut down to start the attempt; bring the list back.
            if (_open)
                _ = BeginBrowsing();
        }

        private void SetInteractable(bool value)
        {
            if (_createButton != null)
                _createButton.interactable = value;
            if (_quickJoinButton != null)
                _quickJoinButton.interactable = value;
            if (_backButton != null)
                _backButton.interactable = value;
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.Session != null)
                    row.Join.interactable = value && row.Session.PlayerCount < row.Session.MaxPlayers;
            }
        }

        private void SetStatus(string message, bool isError = false)
        {
            if (_status == null)
                return;

            _status.text = message ?? "";
            _status.color = isError ? ErrorColor : UiTheme.Safe.ink; // sits on the paper window
            _status.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }
    }
}
