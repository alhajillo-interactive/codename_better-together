using Fusion;

namespace BetterTogether.Networking
{
    /// <summary>
    /// Helper for the "anchor + tick" pattern: instead of replicating a value that changes every tick, the authority
    /// replicates the value at the last event (the anchor) and the tick of that event; every peer derives the current
    /// value from the elapsed simulation time. See CLAUDE.md, rule 4.
    /// </summary>
    public static class NetAnchor
    {
        /// <summary>
        /// Seconds of simulation time since <paramref name="anchorTick"/>. Never negative: a proxy's tick can lag
        /// slightly behind the tick the authority wrote. Zero if the runner is not available.
        /// </summary>
        public static float Elapsed(NetworkRunner runner, int anchorTick)
        {
            if (runner == null)
                return 0f;

            var ticks = runner.Tick.Raw - anchorTick;
            return ticks > 0 ? ticks * runner.DeltaTime : 0f;
        }
    }
}
