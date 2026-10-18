using System;
using BetterTogether.Fireplace;
using BetterTogether.Networking;
using BetterTogether.Player;
using Fusion;
using UnityEngine;
using UnityEngine.Events;

namespace BetterTogether.Match
{
    /// <summary>
    /// Runs the match: waits for the start (button or enough players), counts down, plays until the camper eats the sausage or the match timer runs out,
    /// shows the result and starts over. Master-client object: the state authority drives the phases and every
    /// client reads the replicated state.
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        [Tooltip("Players needed to start a match.")]
        [Min(1)] [SerializeField] private int _minPlayers = 2;

        [Tooltip("Start the countdown as soon as there are enough players, without waiting for the lobby button.")]
        [SerializeField] private bool _autoStart;

        [Tooltip("Seconds of countdown before the match starts.")]
        [SerializeField] private float _countdown = 3f;

        [Tooltip("Seconds the match lasts; the gnomes win when it runs out.")]
        [Min(10)] [SerializeField] private float _matchDuration = 150f;

        [Tooltip("Seconds the result stays on screen before the next match.")]
        [SerializeField] private float _endScreen = 6f;

        private MatchPhase _lastPhase = MatchPhase.Waiting;
        private bool _isSpawned;

        [Networked] private MatchPhase CurrentPhase { get; set; }
        [Networked] private MatchResult CurrentResult { get; set; }
        [Networked] private TickTimer PhaseTimer { get; set; }
        [Networked] private TickTimer MatchTimer { get; set; }

        /// <summary>Raised on every client when a match starts (the phase becomes Playing).</summary>
        public static event UnityAction OnGameStarted;

        /// <summary>Raised on every client when a match ends.</summary>
        public static event UnityAction OnGameOver;

        /// <summary>Raised on every client when the lobby is shown again (the phase becomes Waiting).</summary>
        public static event UnityAction OnLobby;

        public static MatchManager Instance { get; private set; }

        /// <summary>Elapsed fraction of the match timer: 0 before the match starts, 1 once it has ended.</summary>
        public static float MatchProgress { get; private set; }

        /// <summary>True when there is no match system (free play) or the match is running.</summary>
        public static bool AllowsInteraction => Instance == null || Instance.Phase == MatchPhase.Playing;

        public MatchPhase Phase => _isSpawned ? CurrentPhase : MatchPhase.Waiting;

        public MatchResult Result => _isSpawned ? CurrentResult : MatchResult.None;

        /// <summary>Seconds left in the countdown or the end screen, otherwise 0.</summary>
        public float PhaseTimeRemaining
        {
            get
            {
                if (!_isSpawned || (CurrentPhase != MatchPhase.Countdown && CurrentPhase != MatchPhase.Ended))
                    return 0f;

                return PhaseTimer.RemainingTime(Runner) ?? 0f;
            }
        }

        /// <summary>Seconds left on the match timer while playing, otherwise 0.</summary>
        public float TimeRemaining
        {
            get
            {
                if (!_isSpawned || CurrentPhase != MatchPhase.Playing)
                    return 0f;

                return MatchTimer.RemainingTime(Runner) ?? 0f;
            }
        }

        public float MatchDuration => _matchDuration;

        public bool AutoStart => _autoStart;

        public int PlayerCount
        {
            get
            {
                if (!_isSpawned)
                    return 0;

                var count = 0;
                foreach (var _ in Runner.ActivePlayers)
                    count++;
                return count;
            }
        }

        public int MinPlayers => _minPlayers;

        /// <summary>Raised on every client when the replicated phase changes.</summary>
        public event Action<MatchPhase> PhaseChanged;

        public override void Spawned()
        {
            _isSpawned = true;
            Instance = this;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            if (Instance == this)
            {
                Instance = null;
                MatchProgress = 0f;
            }
        }

        /// <summary>Starts the countdown from the lobby when there are enough players. Any client can ask for it.</summary>
        public void StartMatch()
        {
            if (!_isSpawned)
                return;

            if (HasStateAuthority)
                DoStartMatch();
            else
                RPC_StartMatch();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_StartMatch() => DoStartMatch();

        private void DoStartMatch()
        {
            if (CurrentPhase == MatchPhase.Waiting && PlayerCount >= _minPlayers)
                BeginCountdown();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            switch (CurrentPhase)
            {
                case MatchPhase.Waiting:
                    if (_autoStart && PlayerCount >= _minPlayers)
                        BeginCountdown();
                    break;
                case MatchPhase.Countdown:
                    UpdateCountdown();
                    break;
                case MatchPhase.Playing:
                    UpdatePlaying();
                    break;
                case MatchPhase.Ended:
                    UpdateEnded();
                    break;
            }
        }

        private void BeginCountdown()
        {
            ResetWorld();
            CurrentResult = MatchResult.None;
            CurrentPhase = MatchPhase.Countdown;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, _countdown);
        }

        private void UpdateCountdown()
        {
            if (PlayerCount < _minPlayers)
            {
                CurrentPhase = MatchPhase.Waiting;
                PhaseTimer = default;
                return;
            }

            if (!PhaseTimer.Expired(Runner))
                return;

            PhaseTimer = default;
            CurrentPhase = MatchPhase.Playing;
            MatchTimer = TickTimer.CreateFromSeconds(Runner, _matchDuration);
        }

        private void UpdatePlaying()
        {
            if (Sausage.Instance != null && Sausage.Instance.Eaten)
                End(MatchResult.CamperWins);
            else if (MatchTimer.Expired(Runner))
                End(MatchResult.GnomesWin);
        }

        private void End(MatchResult result)
        {
            CurrentResult = result;
            MatchTimer = default;
            CurrentPhase = MatchPhase.Ended;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, _endScreen);
        }

        private void UpdateEnded()
        {
            if (!PhaseTimer.Expired(Runner))
                return;

            if (PlayerCount >= _minPlayers)
            {
                BeginCountdown();
                return;
            }

            ResetWorld();
            CurrentResult = MatchResult.None;
            CurrentPhase = MatchPhase.Waiting;
            PhaseTimer = default;
        }

        /// <summary>Authority only: sends every spawned, owned <see cref="IMatchResettable"/> back to its starting state.</summary>
        private void ResetWorld()
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var behaviour in behaviours)
            {
                if (behaviour is not IMatchResettable resettable)
                    continue;

                if (behaviour is NetworkBehaviour networked
                    && (networked.Object == null || !networked.Object.IsValid || !networked.HasStateAuthority))
                    continue;

                resettable.ResetForMatch();
            }
        }

        public override void Render()
        {
            var phase = CurrentPhase;
            MatchProgress = ComputeProgress(phase);
            if (phase == _lastPhase)
                return;

            _lastPhase = phase;
            if (phase == MatchPhase.Countdown)
                SendLocalPlayerToSpawn();

            PhaseChanged?.Invoke(phase);
            RaiseGlobalEvents(phase);
        }

        private float ComputeProgress(MatchPhase phase)
        {
            // Keep the sky where the match ended (an early camper win shouldn't jump to night).
            if (phase == MatchPhase.Ended)
                return MatchProgress > 0f ? MatchProgress : 1f;

            if (phase != MatchPhase.Playing)
                return 0f;

            var remaining = MatchTimer.RemainingTime(Runner);
            return remaining.HasValue ? Mathf.Clamp01(1f - remaining.Value / _matchDuration) : 0f;
        }

        private static void RaiseGlobalEvents(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Waiting:
                    OnLobby?.Invoke();
                    break;
                case MatchPhase.Playing:
                    OnGameStarted?.Invoke();
                    break;
                case MatchPhase.Ended:
                    OnGameOver?.Invoke();
                    break;
            }
        }

        private void SendLocalPlayerToSpawn()
        {
            var character = PlayerCharacter.Local;
            var connection = ConnectionManager.Instance;
            if (character == null || connection == null || !character.TryGetComponent<PlayerMovement>(out var movement))
                return;

            var position = connection.GetSpawnPoint(Runner.LocalPlayer.PlayerId);
            var rotation = character.transform.rotation;
            var fireplace = FireplaceManager.Instance;
            if (fireplace != null)
            {
                var toFire = Vector3.ProjectOnPlane(fireplace.transform.position - position, Vector3.up);
                if (toFire.sqrMagnitude > 1e-4f)
                    rotation = Quaternion.LookRotation(toFire, Vector3.up);
            }

            movement.Teleport(position, rotation);
        }
    }
}
