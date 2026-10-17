using Fusion;
using Photon.Realtime;
using Photon.Voice.Fusion;
using UnityEngine;

namespace BetterTogether.Voice
{
    /// <summary>
    /// Per-player voice setup. Subscribes the local player to the voice interest groups it should hear
    /// (the Recorder's own group is set on the prefab) and publishes whether the player is currently talking.
    /// </summary>
    [RequireComponent(typeof(VoiceNetworkObject))]
    public class PlayerVoice : NetworkBehaviour
    {
        [Tooltip("Voice interest groups this player listens to besides group 0 (which everyone hears).")]
        [SerializeField] private byte[] _listenGroups;

        private VoiceNetworkObject _voiceObject;
        private FusionVoiceClient _voiceClient;

        [Networked] public NetworkBool IsTalking { get; private set; }

        private void Awake()
        {
            _voiceObject = GetComponent<VoiceNetworkObject>();
        }

        public override void Spawned()
        {
            if (!HasStateAuthority)
                return;

            _voiceClient = Runner.GetComponent<FusionVoiceClient>();
            if (_voiceClient == null)
            {
                Debug.LogWarning("[PlayerVoice] No FusionVoiceClient on the runner; voice groups will not be set up.");
                return;
            }

            _voiceClient.Client.StateChanged += OnVoiceStateChanged;
            if (_voiceClient.Client.InRoom)
                ListenToGroups();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_voiceClient != null)
                _voiceClient.Client.StateChanged -= OnVoiceStateChanged;
        }

        public override void FixedUpdateNetwork()
        {
            if (HasStateAuthority)
                IsTalking = _voiceObject.IsRecording;
        }

        // Group subscriptions are lost when the voice room is left, so renew them on every (re)join.
        private void OnVoiceStateChanged(ClientState from, ClientState to)
        {
            if (to == ClientState.Joined)
                ListenToGroups();
        }

        private void ListenToGroups()
        {
            if (_listenGroups is { Length: > 0 })
                _voiceClient.Client.OpChangeGroups(null, _listenGroups);
        }
    }
}
