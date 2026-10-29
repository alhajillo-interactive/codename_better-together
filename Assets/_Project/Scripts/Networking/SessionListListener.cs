// Requires Photon Fusion 2. The FUSION_WEAVER define is added automatically when the SDK is imported,
// so this file compiles to nothing until then.
#if FUSION_WEAVER
using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;

namespace BetterTogether.Networking
{
    /// <summary>
    /// Receives the session list of the browsing runner (see <see cref="ConnectionManager.StartBrowsing"/>) and keeps
    /// a cached copy. Plain class: it only forwards <c>OnSessionListUpdated</c>; every other callback is empty.
    /// </summary>
    public sealed class SessionListListener : INetworkRunnerCallbacks
    {
        private readonly List<SessionInfo> _sessions = new List<SessionInfo>(32);

        /// <summary>Last session list received (a reused list; copy it if you keep it).</summary>
        public IReadOnlyList<SessionInfo> Sessions => _sessions;

        /// <summary>Raised each time Photon sends a new list of sessions.</summary>
        public event Action<IReadOnlyList<SessionInfo>> Updated;

        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            _sessions.Clear();
            if (sessionList != null)
                _sessions.AddRange(sessionList);
            Updated?.Invoke(_sessions);
        }

        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
    }
}
#endif
