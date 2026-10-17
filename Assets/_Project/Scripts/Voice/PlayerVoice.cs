using Fusion;
using Photon.Realtime;
using Photon.Voice.Fusion;
using Photon.Voice.Unity.UtilityScripts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Voice
{
    /// <summary>
    /// Per-player voice setup. Subscribes the local player to the voice interest groups it should hear
    /// (the Recorder's own group is set on the prefab), lets them mute their mic and publishes whether they are talking.
    /// </summary>
    [RequireComponent(typeof(VoiceNetworkObject))]
    public class PlayerVoice : NetworkBehaviour
    {
        [Tooltip("Voice interest groups this player listens to besides group 0 (which everyone hears).")]
        [SerializeField] private byte[] _listenGroups;

        [Tooltip("Loudness and distance tuning shared by every player.")]
        [SerializeField] private VoiceChatSettings _settings;

        [Tooltip("AudioSource on the mouth that plays this player's voice (driven by the Speaker).")]
        [SerializeField] private AudioSource _speakerSource;

        [SerializeField] private Key _toggleMicKey = Key.M;

        private VoiceNetworkObject _voiceObject;
        private FusionVoiceClient _voiceClient;

        public static PlayerVoice Local { get; private set; }

        public VoiceChatSettings Settings => _settings;
        public bool IsMicOn => _voiceObject.RecorderInUse != null && _voiceObject.RecorderInUse.TransmitEnabled;

        [Networked] public NetworkBool IsTalking { get; private set; }

        private void Awake()
        {
            _voiceObject = GetComponent<VoiceNetworkObject>();
            // Must be set before the Recorder creates its voice, hence Awake rather than Spawned.
            if (TryGetComponent(out MicAmplifier micAmplifier))
                micAmplifier.AmplificationFactor = _settings.MicGain;
        }

        public override void Spawned()
        {
            _settings.ApplyToVoice(_speakerSource);

            if (!HasStateAuthority)
                return;

            Local = this;
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
            if (Local == this)
                Local = null;

            if (_voiceClient != null)
                _voiceClient.Client.StateChanged -= OnVoiceStateChanged;
        }

        private void Update()
        {
            if (Local != this || Keyboard.current == null || !Keyboard.current[_toggleMicKey].wasPressedThisFrame)
                return;

            var recorder = _voiceObject.RecorderInUse;
            if (recorder != null)
                recorder.TransmitEnabled = !recorder.TransmitEnabled;
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
