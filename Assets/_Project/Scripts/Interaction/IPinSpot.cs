using UnityEngine;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// A place where a <see cref="Carryable"/> can rest without a holder, e.g. a grill.
    /// Implemented by a NetworkBehaviour so every peer can find the spot from the item's networked state.
    /// </summary>
    public interface IPinSpot
    {
        /// <summary>Where the pinned item is posed.</summary>
        Transform PinPoint { get; }
    }
}
