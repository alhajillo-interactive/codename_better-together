namespace BetterTogether.Interaction
{
    /// <summary>
    /// An item that has its own use when the holder presses interact with nothing else to act on
    /// (e.g. a bucket being filled). Implemented on the same object as the <see cref="Carryable"/>.
    /// </summary>
    public interface ICarriedUse
    {
        /// <summary>Uses the item if <paramref name="who"/> can use it right now; false lets the default action (drop, eat...) run.</summary>
        bool TryUse(PlayerInteractor who);
    }
}
