using UnityEngine;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// A place where a <see cref="Carryable"/> can rest without a holder, e.g. a grill.
    /// Implemented by a NetworkBehaviour so every peer can find the spot from the item's networked state.
    /// </summary>
    public interface IPinSpot
    {
        /// <summary>Where the item pinned in <paramref name="slot"/> is posed; spots with a single place ignore the slot.</summary>
        Transform GetPinPoint(int slot);

        /// <summary>Whether a player can take an item resting here directly with the interact key. False for spots that give items back another way (e.g. hiding spots).</summary>
        bool AllowsTakeFromPin => true;
    }
}
