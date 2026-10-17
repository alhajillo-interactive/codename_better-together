#if FUSION_WEAVER
using UnityEngine;

namespace BetterTogether.Networking
{
    /// <summary>
    /// Temporary IMGUI panel to create/join a room by code while there is no real lobby UI.
    /// </summary>
    public class DebugConnectionUI : MonoBehaviour
    {
        private string _roomCode = "TEST";

        private void OnGUI()
        {
            var manager = ConnectionManager.Instance;
            if (manager == null)
                return;

            GUILayout.BeginArea(new Rect(10, 10, 260, 200), GUI.skin.box);

            if (manager.IsConnected)
            {
                var runner = manager.Runner;
                GUILayout.Label($"Room: {runner.SessionInfo.Name}");
                GUILayout.Label($"Players: {runner.SessionInfo.PlayerCount}/{runner.SessionInfo.MaxPlayers}");
                GUILayout.Label($"Ping: {runner.GetPlayerRtt(runner.LocalPlayer) * 1000:0} ms");
                if (GUILayout.Button("Disconnect"))
                    _ = manager.Disconnect();
            }
            else if (manager.IsConnecting)
            {
                GUILayout.Label("Connecting...");
            }
            else
            {
                GUILayout.Label("Room code");
                _roomCode = GUILayout.TextField(_roomCode, 12);
                if (GUILayout.Button("Join / Create"))
                    _ = manager.JoinOrCreate(_roomCode);
            }

            GUILayout.EndArea();
        }
    }
}
#endif
