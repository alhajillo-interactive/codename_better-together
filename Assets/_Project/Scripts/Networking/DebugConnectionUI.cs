#if FUSION_WEAVER
using System.Linq;
using BetterTogether.Audio;
using BetterTogether.Gaze;
using BetterTogether.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Networking
{
    /// <summary>
    /// Temporary IMGUI panel to create/join a room by code while there is no real lobby UI.
    /// In the editor and development builds, F1 also opens a role panel to switch any connected player
    /// between camper and gnome for playtesting.
    /// </summary>
    public class DebugConnectionUI : MonoBehaviour
    {
        private const float RolePanelWidth = 340f;

        private string _roomCode = "TEST";
        private string _playerName = LocalPlayerProfile.Name;
        private bool _rolePanelOpen;

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var connected = ConnectionManager.Instance != null && ConnectionManager.Instance.IsConnected;
            if (!connected)
                SetRolePanelOpen(false);
            else if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                SetRolePanelOpen(!_rolePanelOpen);
#endif
        }

        private void OnDisable() => SetRolePanelOpen(false);

        private void OnGUI()
        {
            var manager = ConnectionManager.Instance;
            if (manager == null)
                return;

            GUILayout.BeginArea(new Rect(10, 10, 260, 200), GUI.skin.box);

            if (manager.IsConnected)
            {
                var runner = manager.Runner;
                GUILayout.Label($"Room: {runner.SessionInfo.Name} ({runner.SessionInfo.Region})");
                GUILayout.Label($"Players: {runner.SessionInfo.PlayerCount}/{runner.SessionInfo.MaxPlayers}");
                GUILayout.Label($"Ping: {runner.GetPlayerRtt(runner.LocalPlayer) * 1000:0} ms");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                GUILayout.Label("[F1] Debug: roles y mirada");
#endif
                if (GUILayout.Button("Disconnect"))
                    _ = manager.Disconnect();
            }
            else if (manager.IsConnecting)
            {
                GUILayout.Label("Connecting...");
            }
            else
            {
                GUILayout.Label("Tu nombre");
                var newName = GUILayout.TextField(_playerName, LocalPlayerProfile.MaxNameLength);
                if (newName != _playerName)
                {
                    _playerName = newName;
                    LocalPlayerProfile.Name = newName;
                }

                GUILayout.Label("Room code");
                _roomCode = GUILayout.TextField(_roomCode, 12);
                if (GUILayout.Button("Join / Create"))
                    _ = manager.JoinOrCreate(_roomCode);
            }

            GUILayout.EndArea();

            if (_rolePanelOpen)
                DrawRolePanel();
        }

        private void DrawRolePanel()
        {
            var players = PlayerCharacter.All
                .Where(p => p != null && p.Object != null && p.Object.IsValid)
                .OrderBy(p => p.Object.StateAuthority.PlayerId)
                .ToList();

            var height = 230f + players.Count * 28f;
            GUILayout.BeginArea(new Rect(Screen.width - RolePanelWidth - 10f, 10f, RolePanelWidth, height), GUI.skin.box);
            GUILayout.Label("Debug — F1 para cerrar");
            DrawGazeToggle();
            DrawVolumeSliders();
            GUILayout.Space(6f);

            foreach (var player in players)
            {
                GUILayout.BeginHorizontal();
                var you = player.HasStateAuthority ? " (tú)" : "";
                GUILayout.Label($"{player.DisplayName}{you}: {RoleName(player.Role)}", GUILayout.Width(170f));
                RoleButton(player, PlayerRole.Camper);
                RoleButton(player, PlayerRole.Gnome);
                GUILayout.EndHorizontal();
            }

            if (players.Count == 0)
                GUILayout.Label("No hay jugadores todavía.");

            GUILayout.EndArea();
        }

        /// <summary>This player's own volume per mixer channel (saved between sessions).</summary>
        private static void DrawVolumeSliders()
        {
            VolumeSlider("General", AudioChannel.Master);
            VolumeSlider("Música", AudioChannel.Music);
            VolumeSlider("Efectos", AudioChannel.Sfx);
            VolumeSlider("Voz", AudioChannel.Voice);
        }

        private static void VolumeSlider(string label, AudioChannel channel)
        {
            var value = AudioManager.GetVolume(channel);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value * 100f:0}%", GUILayout.Width(110f));
            var newValue = GUILayout.HorizontalSlider(value, 0f, 1f);
            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(newValue, value))
                AudioManager.SetVolume(channel, newValue);
        }

        /// <summary>Turns the petrifying gaze of every camper on or off at once.</summary>
        private static void DrawGazeToggle()
        {
            var gazes = CamperGaze.All.Where(g => g != null && g.Object != null && g.Object.IsValid).ToList();

            GUILayout.BeginHorizontal();
            if (gazes.Count == 0)
            {
                GUILayout.Label("Mirada petrificante: (no hay campista)");
            }
            else
            {
                var enabled = gazes.Any(g => g.IsGazeEnabled);
                GUILayout.Label($"Mirada petrificante: {(enabled ? "ON" : "OFF")}", GUILayout.Width(170f));
                if (GUILayout.Button(enabled ? "Desactivar" : "Activar"))
                {
                    foreach (var gaze in gazes)
                        gaze.RPC_SetGazeEnabled(!enabled);
                }
            }
            GUILayout.EndHorizontal();
        }

        private void RoleButton(PlayerCharacter player, PlayerRole role)
        {
            var wasEnabled = GUI.enabled;
            GUI.enabled = player.Role != role;
            if (GUILayout.Button(RoleName(role)))
            {
                player.RPC_RequestRole(role);

                // Switching your own role respawns you: close the panel so the mouse goes back to the camera.
                if (player.HasStateAuthority)
                    SetRolePanelOpen(false);
            }
            GUI.enabled = wasEnabled;
        }

        private static string RoleName(PlayerRole role) => role == PlayerRole.Camper ? "Campista" : "Gnomo";

        private void SetRolePanelOpen(bool open)
        {
            if (open == _rolePanelOpen)
                return;

            _rolePanelOpen = open;
            FirstPersonCamera.CursorFree = open;

            // Back to playing: recapture the mouse instead of waiting for a click (which could also trigger a kick).
            if (!open && PlayerCharacter.Local != null)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
}
#endif
