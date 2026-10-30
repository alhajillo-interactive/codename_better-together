using System;
using System.Collections.Generic;
using BetterTogether.Diagnostics;
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
        [Tooltip("Testing only: lets the match start with a single player, ignoring Min Players.")]
        [SerializeField] private bool _allowSoloStart;

        [Tooltip("Start the countdown as soon as there are enough players, without waiting for the lobby button.")]
        [SerializeField] private bool _autoStart;

        [Tooltip("Each rematch the next player (by PlayerId) becomes the camper; off picks a random player every time.")]
        [SerializeField] private bool _rotateCamper = true;

        [Tooltip("Seconds of countdown before the match starts.")]
        [SerializeField] private float _countdown = 3f;

        [Tooltip("Seconds the match lasts; the gnomes win when it runs out.")]
        [Min(10)] [SerializeField] private float _matchDuration = 300f;

        [Tooltip("Seconds the result stays on screen before the next match.")]
        [SerializeField] private float _endScreen = 6f;

        [Header("Gnome scoring")]
        [Tooltip("Points for the gnome that finishes dousing the fire with the bucket.")]
        [SerializeField] private int _pointsDouse = 3;
        [Tooltip("Points for the gnome that fills the bucket.")]
        [SerializeField] private int _pointsFill = 1;
        [Tooltip("Points for the gnome that hides an object.")]
        [SerializeField] private int _pointsHide = 2;
        [Tooltip("Points for a gnome that frees a teammate from the camper's cage.")]
        [SerializeField] private int _pointsFreeTeammate = 2;
        [Tooltip("Points for a gnome that takes an object the camper was last holding.")]
        [SerializeField] private int _pointsSteal = 1;
        [Tooltip("Most bucket fills per player and match that give points (stops fill-spill-fill farming).")]
        [Min(0)] [SerializeField] private int _maxFillAwards = 3;
        [Tooltip("Points every gnome gets when the sausage burns (paid once per match).")]
        [SerializeField] private int _pointsBurntSausage = 3;

        private MatchPhase _lastPhase = MatchPhase.Waiting;
        private bool _isSpawned;
        private bool _roleApplied;

        [Networked] private MatchPhase CurrentPhase { get; set; }
        [Networked] private PlayerRef CurrentCamper { get; set; }
        [Networked] private MatchResult CurrentResult { get; set; }
        [Networked] private TickTimer PhaseTimer { get; set; }
        [Networked] private TickTimer MatchTimer { get; set; }

        // Gnome scores: one row per player (PlayerRef + points), written only by the state authority. A row per player
        // instead of an array indexed by PlayerId because ids keep growing when players rejoin. Rows of players who left stay until the next match.
        [Networked, Capacity(MaxScoreRows)] private NetworkArray<ScoreEntry> Scores => default;

        // Match stats for the end screen. FireSeconds is the match time of the first fire (-1: never lit).
        [Networked] private float FireSeconds { get; set; }
        [Networked] private int HiddenCount { get; set; }

        // The burnt-sausage bonus is paid once per match.
        [Networked] private NetworkBool BurnPaid { get; set; }

        public const int MaxScoreRows = 8;

        /// <summary>One scoreboard row, replicated.</summary>
        public struct ScoreEntry : INetworkStruct
        {
            public PlayerRef Player;
            public short Points;
            public byte Fills;
        }

        /// <summary>What earned points; the value of each is a tooltip field on the match manager.</summary>
        public enum ScoreReason
        {
            Douse,
            FillBucket,
            Hide,
            Steal,
            BurntSausage,
            FreeTeammate,
        }

        /// <summary>Raised on every client when a match starts (the phase becomes Playing).</summary>
        public static event UnityAction OnGameStarted;

        /// <summary>Raised on every client when a match ends.</summary>
        public static event UnityAction OnGameOver;

        /// <summary>Raised on every client when the lobby is shown again (the phase becomes Waiting).</summary>
        public static event UnityAction OnLobby;

        public static MatchManager Instance { get; private set; }

        /// <summary>Elapsed fraction of the match timer: 0 before the match starts, 1 once it has ended.</summary>
        public static float MatchProgress { get; private set; }

        /// <summary>True while connected and waiting for the match to start (lobby). False without a match manager.</summary>
        public static bool InLobby => Instance != null && Instance.Phase == MatchPhase.Waiting;

        /// <summary>True when there is no match system (free play) or the match is running.</summary>
        public static bool AllowsInteraction => Instance == null || Instance.Phase == MatchPhase.Playing;

        /// <summary>Players can't move in the lobby or during the countdown, i.e. until <see cref="OnGameStarted"/>.</summary>
        public static bool AllowsMovement => Instance == null || Instance.Phase == MatchPhase.Playing || Instance.Phase == MatchPhase.Ended;

        public MatchPhase Phase => _isSpawned ? CurrentPhase : MatchPhase.Waiting;

        /// <summary>The player chosen as the camper for the current match; <see cref="PlayerRef.None"/> before the first countdown.</summary>
        public PlayerRef Camper => _isSpawned ? CurrentCamper : PlayerRef.None;

        /// <summary>True when each rematch hands the camper role to the next player instead of picking at random.</summary>
        public bool RotatesCamper => _rotateCamper;

        /// <summary>
        /// Who <see cref="ChooseCamper"/> will pick at the next start, derived from the replicated camper and the active
        /// players with the same rule (next higher PlayerId, wrapping). <see cref="PlayerRef.None"/> when the choice is
        /// random (rotation off or no previous camper). Allocation free, but meant for UI refreshes, not every frame.
        /// </summary>
        public PlayerRef PredictedNextCamper
        {
            get
            {
                var previous = Camper;
                if (!_isSpawned || !_rotateCamper || previous == PlayerRef.None)
                    return PlayerRef.None;

                var lowest = PlayerRef.None;
                var next = PlayerRef.None;
                foreach (var player in Runner.ActivePlayers)
                {
                    if (lowest == PlayerRef.None || player.PlayerId < lowest.PlayerId)
                        lowest = player;

                    if (player.PlayerId > previous.PlayerId && (next == PlayerRef.None || player.PlayerId < next.PlayerId))
                        next = player;
                }

                return next != PlayerRef.None ? next : lowest;
            }
        }

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

        /// <summary>
        /// Network tick at which the current match started (end of the countdown), derived from the replicated timers so
        /// every peer computes the same value (to within a tick); 0 outside the countdown and the match. Telemetry only.
        /// </summary>
        public int StartTick
        {
            get
            {
                if (!_isSpawned)
                    return 0;

                if (CurrentPhase == MatchPhase.Countdown && PhaseTimer.TargetTick.HasValue)
                    return PhaseTimer.TargetTick.Value;

                if (CurrentPhase == MatchPhase.Playing && MatchTimer.TargetTick.HasValue)
                    return MatchTimer.TargetTick.Value - Mathf.RoundToInt(_matchDuration * Runner.TickRate);

                return 0;
            }
        }

        /// <summary>Match seconds at which the fire was first lit, or -1 when it never was (valid on every peer).</summary>
        public float FirstFireSeconds => _isSpawned ? FireSeconds : -1f;

        /// <summary>Objects hidden by gnomes this match.</summary>
        public int ObjectsHidden => _isSpawned ? HiddenCount : 0;

        /// <summary>Times the fire was put out this match.</summary>
        public int FiresExtinguished => FireplaceManager.Instance != null ? FireplaceManager.Instance.Extinguished : 0;

        /// <summary>Points of a player, 0 when it has no row.</summary>
        public int GetScore(PlayerRef player)
        {
            if (!_isSpawned || player == PlayerRef.None)
                return 0;

            var scores = Scores;
            for (var i = 0; i < scores.Length; i++)
            {
                if (scores[i].Player == player)
                    return scores[i].Points;
            }

            return 0;
        }

        /// <summary>Copies the scoreboard rows (players with a row) into <paramref name="results"/>, unsorted. Returns the count.</summary>
        public int GetScoreRows(List<ScoreEntry> results)
        {
            results.Clear();
            if (!_isSpawned)
                return 0;

            var scores = Scores;
            for (var i = 0; i < scores.Length; i++)
            {
                if (scores[i].Player != PlayerRef.None)
                    results.Add(scores[i]);
            }

            return results.Count;
        }

        /// <summary>Authority only (does nothing elsewhere, no RPC needed): adds <paramref name="points"/> to a player while the match runs.</summary>
        public void Award(PlayerRef player, int points) => AwardInternal(player, points, "manual");

        /// <summary>Authority only: awards the points configured for <paramref name="reason"/> to one player.</summary>
        public void Award(PlayerRef player, ScoreReason reason)
        {
            if (!_isSpawned || !HasStateAuthority || CurrentPhase != MatchPhase.Playing)
                return;

            if (reason == ScoreReason.Hide)
                HiddenCount++;

            AwardInternal(player, PointsFor(reason), ReasonName(reason), reason == ScoreReason.FillBucket);
        }

        /// <summary>Authority only: every gnome (everybody but the camper) gets the points configured for <paramref name="reason"/>.</summary>
        public void AwardGnomes(ScoreReason reason)
        {
            if (!_isSpawned || !HasStateAuthority || CurrentPhase != MatchPhase.Playing)
                return;

            if (reason == ScoreReason.BurntSausage)
            {
                if (BurnPaid)
                    return;

                BurnPaid = true;
            }

            var points = PointsFor(reason);
            var reasonName = ReasonName(reason);
            foreach (var player in Runner.ActivePlayers)
            {
                if (player != CurrentCamper)
                    AwardInternal(player, points, reasonName);
            }
        }

        /// <summary>Authority only: remembers when the fire was first lit this match (later lights are ignored).</summary>
        public void NoteFireLit()
        {
            if (!_isSpawned || !HasStateAuthority || CurrentPhase != MatchPhase.Playing || FireSeconds >= 0f)
                return;

            FireSeconds = Mathf.Max(0f, _matchDuration - TimeRemaining);
        }

        private int PointsFor(ScoreReason reason) => reason switch
        {
            ScoreReason.Douse => _pointsDouse,
            ScoreReason.FillBucket => _pointsFill,
            ScoreReason.Hide => _pointsHide,
            ScoreReason.Steal => _pointsSteal,
            ScoreReason.FreeTeammate => _pointsFreeTeammate,
            _ => _pointsBurntSausage,
        };

        private static string ReasonName(ScoreReason reason) => reason switch
        {
            ScoreReason.Douse => "douse",
            ScoreReason.FillBucket => "fill_bucket",
            ScoreReason.Hide => "hide",
            ScoreReason.Steal => "steal",
            ScoreReason.FreeTeammate => "free_teammate",
            _ => "burnt_sausage",
        };

        private void AwardInternal(PlayerRef player, int points, string reason, bool isFill = false)
        {
            if (!_isSpawned || !HasStateAuthority || CurrentPhase != MatchPhase.Playing || player == PlayerRef.None || points == 0)
                return;

            var scores = Scores;
            var free = -1;
            for (var i = 0; i < scores.Length; i++)
            {
                var row = scores[i];
                if (row.Player == player)
                {
                    if (isFill)
                    {
                        if (row.Fills >= _maxFillAwards)
                            return;

                        row.Fills++;
                    }

                    row.Points = (short)Mathf.Clamp(row.Points + points, short.MinValue, short.MaxValue);
                    scores.Set(i, row);
                    LogScore(player, points, reason);
                    return;
                }

                if (free < 0 && row.Player == PlayerRef.None)
                    free = i;
            }

            if (free < 0 || (isFill && _maxFillAwards < 1))
                return;

            scores.Set(free, new ScoreEntry { Player = player, Points = (short)Mathf.Clamp(points, short.MinValue, short.MaxValue), Fills = (byte)(isFill ? 1 : 0) });
            LogScore(player, points, reason);
        }

        private void LogScore(PlayerRef player, int points, string reason)
        {
            if (Telemetry.Enabled) Telemetry.Begin("score").Int("scorer", player.PlayerId).Int("points", points).Str("reason", reason).Send();
        }

        // Authority only: zeroes the scoreboard and stats, then gives every gnome an empty row so the ranking lists them all.
        private void ResetScores()
        {
            var scores = Scores;
            for (var i = 0; i < scores.Length; i++)
                scores.Set(i, default);

            FireSeconds = -1f;
            HiddenCount = 0;
            BurnPaid = false;
            var row = 0;
            foreach (var player in Runner.ActivePlayers)
            {
                if (player == CurrentCamper || row >= scores.Length)
                    continue;

                scores.Set(row++, new ScoreEntry { Player = player });
            }
        }

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

        public int MinPlayers => _allowSoloStart ? 1 : _minPlayers;

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

            // Through the RPC so non-authority clients can ask; on the authority Fusion invokes it locally, in this same call.
            RPC_StartMatch();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_StartMatch() => DoStartMatch();

        private void DoStartMatch()
        {
            if (CurrentPhase == MatchPhase.Waiting && PlayerCount >= MinPlayers)
                BeginCountdown();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            switch (CurrentPhase)
            {
                case MatchPhase.Waiting:
                    if (_autoStart && PlayerCount >= MinPlayers)
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
            if (Telemetry.Enabled)
            {
                var ready = 0;
                foreach (var character in PlayerCharacter.All)
                {
                    if (character.IsReady)
                        ready++;
                }

                Telemetry.Begin("lobby_ready").Int("players", PlayerCount).Int("ready", ready).Send();
            }

            ResetWorld();
            CurrentCamper = ChooseCamper();
            ResetScores();
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
            if (PlayerCount < MinPlayers)
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
            if (Telemetry.Enabled) Telemetry.Begin("match_start").Int("players", PlayerCount).Int("camper", CurrentCamper.PlayerId).Send();
        }

        private void UpdatePlaying()
        {
            if (Sausage.AnyEaten)
                End(MatchResult.CamperWins);
            else if (MatchTimer.Expired(Runner))
                End(MatchResult.GnomesWin);
        }

        private void End(MatchResult result)
        {
            if (Telemetry.Enabled) Telemetry.Begin("match_end").Str("result", result == MatchResult.CamperWins ? "camper_wins" : "gnomes_win").Str("cause", result == MatchResult.CamperWins ? "sausage_eaten" : "timeout").Float("duration", Telemetry.T).Send();
            CurrentResult = result;
            MatchTimer = default;
            CurrentPhase = MatchPhase.Ended;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, _endScreen);
        }

        private void UpdateEnded()
        {
            if (!PhaseTimer.Expired(Runner))
                return;

            if (PlayerCount >= MinPlayers)
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
