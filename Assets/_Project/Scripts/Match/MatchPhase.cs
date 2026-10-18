namespace BetterTogether.Match
{
    /// <summary>Stage of a match, replicated by <see cref="MatchManager"/>.</summary>
    public enum MatchPhase
    {
        Waiting,
        Countdown,
        Playing,
        Ended,
    }
}
