namespace BetterTogether.Match
{
    /// <summary>
    /// Something that goes back to its starting state when a match (re)starts.
    /// <see cref="MatchManager"/> calls it on the state authority only, so implementers write networked state directly.
    /// </summary>
    public interface IMatchResettable
    {
        void ResetForMatch();
    }
}
