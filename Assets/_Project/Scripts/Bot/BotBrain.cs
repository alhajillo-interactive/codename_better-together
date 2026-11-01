using UnityEngine;

namespace BetterTogether.Bot
{
    /// <summary>What the bot is trying to do now.</summary>
    public enum BotGoal : byte
    {
        Cook,
        Eat,
        Investigate,
        Chase,
        Carry,
        Recover,
    }

    /// <summary>What the brain needs to know, measured in 0..1 by the bot's body every decision.</summary>
    public struct BotSituation
    {
        public bool SausageReady;
        public bool CarryingGnome;
        public bool Stunned;
        public float ChaseCertainty;
        public bool CageFree;
        public float InvestigateInterest;
        public float ListProgress;
        public float CookProgress;
        public bool InRecovery;
        public bool ChaseOnCooldown;
    }

    /// <summary>
    /// Utility AI: gives every goal a score and takes the best. Momentum, a margin and a minimum time keep the bot from dithering,
    /// and <see cref="Pressure"/>, a small director inspired by Alien: Isolation, makes it calm down after a burst of action. Master
    /// only; it can be rebuilt from nothing after a master change.
    /// </summary>
    public class BotBrain
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("Score added to the current goal, so it is not dropped for a tiny difference.")]
            public float Momentum = 0.15f;
            [Tooltip("How much better another goal must be.")]
            public float SwitchMargin = 0.1f;
            [Tooltip("Seconds a goal lasts at least, unless something urgent appears.")]
            public float MinGoalSeconds = 1f;
            [Tooltip("Pressure gained per second of chase.")]
            public float PressureGain = 0.08f;
            [Tooltip("Pressure lost per second otherwise.")]
            public float PressureLoss = 0.04f;
            [Tooltip("How much a full pressure lowers the wish to chase, leaving the gnomes room to breathe.")]
            public float PressureDamping = 0.35f;
        }

        private static readonly BotGoal[] Goals = { BotGoal.Cook, BotGoal.Eat, BotGoal.Investigate, BotGoal.Chase, BotGoal.Carry, BotGoal.Recover };

        private readonly Settings _settings;
        private readonly float[] _scores = new float[Goals.Length];
        private float _goalSince;

        public BotGoal Goal { get; private set; } = BotGoal.Cook;

        /// <summary>0 calm, 1 worn out by the action.</summary>
        public float Pressure { get; private set; }

        public BotBrain(Settings settings) => _settings = settings;

        /// <summary>Score of a goal in the last decision, for the debug overlay.</summary>
        public float ScoreOf(BotGoal goal) => _scores[(int)goal];

        public void Reset()
        {
            Goal = BotGoal.Cook;
            Pressure = 0f;
        }

        public void Decide(in BotSituation s, float now, float deltaTime)
        {
            var delta = Goal == BotGoal.Chase ? _settings.PressureGain : -_settings.PressureLoss;
            Pressure = Mathf.Clamp01(Pressure + delta * deltaTime);

            Score(s);
            var best = Goal;
            var bestScore = _scores[(int)Goal] + _settings.Momentum;
            var urgent = s.Stunned || s.CarryingGnome || s.SausageReady;
            for (var i = 0; i < Goals.Length; i++)
            {
                if (_scores[i] > bestScore + (urgent ? 0f : _settings.SwitchMargin))
                {
                    best = Goals[i];
                    bestScore = _scores[i];
                }
            }

            if (best == Goal || (!urgent && now - _goalSince < _settings.MinGoalSeconds))
                return;

            Goal = best;
            _goalSince = now;
        }

        private void Score(in BotSituation s)
        {
            // Finishing the list makes the bot hurry over its dinner; a dinner almost done makes it hurry too.
            var hurry = Mathf.Max(s.ListProgress, s.CookProgress);
            _scores[(int)BotGoal.Cook] = 0.45f + 0.2f * s.CookProgress;
            _scores[(int)BotGoal.Eat] = s.SausageReady ? 1.3f : 0f;
            _scores[(int)BotGoal.Carry] = s.CarryingGnome ? 0.95f : 0f;
            _scores[(int)BotGoal.Recover] = s.Stunned ? 2f : 0f;

            var chase = s.ChaseCertainty > 0f && s.CageFree && !s.ChaseOnCooldown ? 0.5f + 0.4f * s.ChaseCertainty : 0f;
            chase -= Pressure * _settings.PressureDamping;
            if (s.InRecovery)
                chase -= 0.3f;

            _scores[(int)BotGoal.Chase] = Mathf.Max(0f, chase);
            _scores[(int)BotGoal.Investigate] = Mathf.Max(0f, s.InvestigateInterest * (0.7f - 0.15f * hurry));
        }
    }
}
