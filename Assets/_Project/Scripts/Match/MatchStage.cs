using BetterTogether.Fireplace;

namespace BetterTogether.Match
{
    /// <summary>
    /// The two milestones of a match, with inverted roles. It is derived, never replicated: while the match is being
    /// played, the stage follows <see cref="FireplaceManager.IsLit"/>, so late joiners and a new master agree by construction.
    /// </summary>
    public static class MatchStage
    {
        public enum Stage
        {
            /// <summary>Not playing, or no fireplace.</summary>
            None,

            /// <summary>Milestone 1: the camper builds and lights the fire, the gnomes hinder.</summary>
            BuildFire,

            /// <summary>Milestone 2: the camper defends the lit fire, the gnomes bring water.</summary>
            DefendFire,
        }

        public static Stage Current
        {
            get
            {
                if (!MatchManager.AllowsInteraction)
                    return Stage.None;

                var fire = FireplaceManager.Instance;
                if (fire == null)
                    return Stage.None;

                return fire.IsLit ? Stage.DefendFire : Stage.BuildFire;
            }
        }
    }
}
