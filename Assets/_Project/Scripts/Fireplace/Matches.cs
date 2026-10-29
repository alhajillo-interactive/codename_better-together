using System.Collections.Generic;
using BetterTogether.Interaction;
using UnityEngine;

namespace BetterTogether.Fireplace
{
    /// <summary>
    /// Lets the campfire recognise the matches in a camper's hands.
    /// </summary>
    [RequireComponent(typeof(Carryable))]
    public class Matches : MonoBehaviour
    {
        private static readonly List<Matches> _all = new();

        /// <summary>Every live matches item in the scene (registered on enable, so nobody searches the scene).</summary>
        public static IReadOnlyList<Matches> All => _all;

        private void OnEnable() => _all.Add(this);

        private void OnDisable() => _all.Remove(this);
    }
}
