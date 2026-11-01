using BetterTogether.Audio;
using BetterTogether.Bot;
using BetterTogether.Match;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Player
{
    /// <summary>
    /// A frying pan the gnome raises for an instant. If the camera fires then, the flash bounces back: the camper's freeze fails and
    /// the bot camper is left dazzled. The owner writes the timers, so the check on the shot is the owner's own state.
    /// Late joiner: Render shows the timer. Master change: irrelevant. Player leaves: the object despawns.
    /// </summary>
    public class GnomePan : NetworkBehaviour
    {
        [Tooltip("Seconds the pan stays up.")]
        [SerializeField, Min(0f)] private float _upSeconds = 0.7f;
        [Tooltip("Seconds before it can be raised again.")]
        [SerializeField, Min(0f)] private float _cooldown = 6f;
        [Tooltip("The pan, shown while it is up.")]
        [SerializeField] private GameObject _visual;
        [SerializeField] private SoundCue _raiseSfx;
        [SerializeField] private SoundCue _reflectSfx;

        [Networked] private TickTimer UpTimer { get; set; }
        [Networked] private TickTimer CooldownTimer { get; set; }
        [Networked] private byte ReflectSeq { get; set; }

        private InputAction _action;
        private bool _isSpawned;
        private bool _queued;
        private bool _feedbackReady;
        private bool _shownUp;
        private byte _shownReflect;

        /// <summary>True while the pan would bounce a flash.</summary>
        public bool IsUp => _isSpawned && UpTimer.IsRunning && !UpTimer.Expired(Runner);

        public override void Spawned()
        {
            _isSpawned = true;
            if (_visual != null)
                _visual.SetActive(false);
            if (HasStateAuthority)
                _action = InputSystem.actions.FindAction("Player/Pan");
        }

        public override void Despawned(NetworkRunner runner, bool hasState) => _isSpawned = false;

        private void Update()
        {
            if (_isSpawned && HasStateAuthority && _action != null && !FirstPersonCamera.CursorFree && !FirstPersonCamera.IsTyping && _action.WasPressedThisFrame())
                _queued = true;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !_queued)
                return;

            _queued = false;
            if (!MatchManager.AllowsInteraction || !CooldownTimer.ExpiredOrNotRunning(Runner))
                return;

            UpTimer = TickTimer.CreateFromSeconds(Runner, _upSeconds);
            CooldownTimer = TickTimer.CreateFromSeconds(Runner, _cooldown);
        }

        /// <summary>Owner only: the flash hit the pan.</summary>
        public void Reflect()
        {
            ReflectSeq = (byte)(ReflectSeq + 1);
            var bot = CampistaBot.Instance;
            if (bot != null)
                bot.RPC_Dazzle();
        }

        public override void Render()
        {
            if (!_isSpawned)
                return;

            var up = IsUp;
            if (up != _shownUp)
            {
                _shownUp = up;
                // In first person it would fill the screen: its owner hears it and reads it on the HUD.
                if (_visual != null)
                    _visual.SetActive(up && !HasStateAuthority);
                if (up && _feedbackReady)
                    _raiseSfx.PlayAt(transform.position);
            }

            if (_feedbackReady && ReflectSeq != _shownReflect)
                _reflectSfx.PlayAt(transform.position);

            _shownReflect = ReflectSeq;
            _feedbackReady = true;
        }
    }
}
