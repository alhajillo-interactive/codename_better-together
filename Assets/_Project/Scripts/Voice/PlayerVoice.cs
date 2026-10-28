using BetterTogether.Player;
using Fusion;
using Photon.Realtime;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using Photon.Voice.Unity.UtilityScripts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Voice
{
    /// <summary>
    /// Per-player voice setup. Subscribes the local player to the voice interest groups it should hear
    /// (the Recorder's own group is set on the prefab), lets them mute their mic and publishes whether they are talking.
    /// Also applies the shared voice detection values and the optional push-to-talk key.
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

        // Set with the mute key (M). Independent of push-to-talk, so both can apply at once.
        // Static on purpose: only the local player drives it, and it must survive a role respawn (gnome to camper or back),
        // which creates a new PlayerVoice instance; otherwise a muted player would be unmuted by the respawn.
        private static bool _muted;

        public static PlayerVoice Local { get; private set; }

        public VoiceChatSettings Settings => _settings;
        public bool IsMicOn => _voiceObject.RecorderInUse != null && !_muted;

        [Networked] public NetworkBool IsTalking { get; private set; }

        private void Awake()
        {
            _voiceObject = GetComponent<VoiceNetworkObject>();
            // Must be set before the Recorder creates its voice, hence Awake rather than Spawned.
            if (TryGetComponent(out MicAmplifier micAmplifier))
                micAmplifier.AmplificationFactor = _settings.MicGain;

            // Voice detection values can also be changed at runtime: the Recorder pushes them to its detector on change.
            var recorder = GetComponentInChildren<Recorder>();
            if (recorder != null)
            {
                recorder.VoiceDetectionThreshold = _settings.VoiceDetectionThreshold;
                recorder.VoiceDetectionDelayMs = _settings.VoiceDetectionDelayMs;
            }
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
            if (Local != this || Keyboard.current == null)
                return;

            bool typing = FirstPersonCamera.IsTyping;
            if (!typing && Keyboard.current[_toggleMicKey].wasPressedThisFrame)
                _muted = !_muted;

            var recorder = _voiceObject.RecorderInUse;
            if (recorder == null)
                return;

            // The Recorder only applies a change when the value differs, so this per-frame set is cheap.
            bool transmit = !_muted;
            if (transmit && _settings.PushToTalk)
                transmit = !typing && Keyboard.current[_settings.PushToTalkKey].isPressed;
            recorder.TransmitEnabled = transmit;
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

        // Subscriptions belong to the voice connection, not to this character: when a gnome respawns as the camper
        // (every countdown, or from the F1 panel) the gnome group would stay subscribed and the camper would hear
        // the gnomes' mics. So replace them instead of adding: an empty remove array drops every group, and a null
        // add array adds none (an empty one would subscribe to all groups).
        private void ListenToGroups()
        {
            var add = _listenGroups is { Length: > 0 } ? _listenGroups : null;
            _voiceClient.Client.OpChangeGroups(System.Array.Empty<byte>(), add);
        }
    }
}
