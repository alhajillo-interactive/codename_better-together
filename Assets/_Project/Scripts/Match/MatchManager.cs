using System;
using System.Collections.Generic;
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

        [Tooltip("Each rematch the next player (by PlayerId) becomes the camper; off picks a random player every time.")]
        [SerializeField] private bool _rotateCamper = true;

        [Tooltip("Seconds of countdown before the match starts.")]
        [SerializeField] private float _countdown = 3f;

        [Tooltip("Seconds the match lasts; the gnomes win when it runs out.")]
        [Min(10)] [SerializeField] private float _matchDuration = 150f;

        [Tooltip("Seconds the result stays on screen before the next match.")]
        [SerializeField] private float _endScreen = 6f;

        private MatchPhase _lastPhase = MatchPhase.Waiting;
        private bool _isSpawned;
        private bool _roleApplied;

        [Networked] private MatchPhase CurrentPhase { get; set; }
        [Networked] private PlayerRef CurrentCamper { get; set; }
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

        /// <summary>Players can't move in the lobby or during the countdown, i.e. until <see cref="OnGameStarted"/>.</summary>
        public static bool AllowsMovement => Instance == null || Instance.Phase == MatchPhase.Playing || Instance.Phase == MatchPhase.Ended;

        public MatchPhase Phase => _isSpawned ? CurrentPhase : MatchPhase.Waiting;

        /// <summary>The player chosen as the camper for the current match; <see cref="PlayerRef.None"/> before the first countdown.</summary>
        public PlayerRef Camper => _isSpawned ? CurrentCamper : PlayerRef.None;

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
            CurrentCamper = ChooseCamper();
            CurrentResult = MatchResult.None;
            CurrentPhase = MatchPhase.Countdown;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, _countdown);
        }

        private PlayerRef ChooseCamper()
        {
            var players = new List<PlayerRef>();
            foreach (var player in Runner.ActivePlayers)
                players.Add(player);

            if (players.Count == 0)
                return PlayerRef.None;

            var previous = CurrentCamper;
            if (!_rotateCamper || previous == PlayerRef.None)
                return players[UnityEngine.Random.Range(0, players.Count)];

            players.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));
            foreach (var player in players)
            {
                if (player.PlayerId > previous.PlayerId)
                    return player;
            }

            return players[0];
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
            if (phase != MatchPhase.Countdown)
                _roleApplied = false;
            else if (!_roleApplied)
                ApplyLocalRole();

            if (phase == _lastPhase)
                return;

            _lastPhase = phase;

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

        /// <summary>
        /// Once per countdown: respawns the local player as camper or gnome at its spawn point. Camper may replicate
        /// a tick after the phase, so this waits until it is known.
        /// </summary>
        private void ApplyLocalRole()
        {
            var camper = CurrentCamper;
            var spawner = PlayerSpawner.Instance;
            if (camper == PlayerRef.None || spawner == null)
                return;

            _roleApplied = true;
            var role = Runner.LocalPlayer == camper ? PlayerRole.Camper : PlayerRole.Gnome;
            spawner.RespawnLocal(role);
        }
    }
}
