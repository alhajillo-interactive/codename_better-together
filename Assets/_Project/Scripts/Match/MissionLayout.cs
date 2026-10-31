using System;
using BetterTogether.Interaction;
using UnityEngine;

namespace BetterTogether.Match
{
    /// <summary>
    /// Everything that changes from match to match, for one level: where each item can start, which places can be photographed
    /// and where butterflies can appear. Scene component without networking; the replicated seed of <see cref="MatchManager"/>
    /// gives the same choice on every peer. A level without this component has no missions and no variation.
    /// Built by AbrazoLevelBuilder.
    /// </summary>
    public class MissionLayout : MonoBehaviour
    {
        public const int PhotoTasks = 3;

        /// <summary>The slot of the gnome photo; the other slots are places.</summary>
        public const int GnomePhotoSlot = 2;

        /// <summary>One item and the poses it can start a match in, already resting on the ground.</summary>
        [Serializable]
        public struct ItemSpots
        {
            public Carryable Item;
            public Vector3[] Spots;
        }

        [Tooltip("Items that move between matches. One spot per item is used, so each stays in its own sector.")]
        [SerializeField] private ItemSpots[] _items;
        [Tooltip("Places north of the trees: one is asked each match, which makes the camper cross.")]
        [SerializeField] private PhotoSubject[] _northSubjects;
        [Tooltip("Places south of the trees: one is asked each match.")]
        [SerializeField] private PhotoSubject[] _southSubjects;
        [Tooltip("Possible butterfly spots, at most 31.")]
        [SerializeField] private ButterflySpot[] _butterflies;
        [Tooltip("Butterflies out per match.")]
        [SerializeField, Min(1)] private int _butterfliesPerMatch = 5;
        [Tooltip("Butterflies the gnomes need between them for their reward.")]
        [SerializeField, Min(1)] private int _butterfliesNeeded = 3;
        [Tooltip("Farthest the camper can be from a gnome in a gnome photo (m).")]
        [SerializeField, Min(1f)] private float _gnomePhotoDistance = 12f;

        private readonly PhotoSubject[] _tasks = new PhotoSubject[GnomePhotoSlot];
        private int _chosenButterflies;
        private bool _gnomePhotoIsStone;

        /// <summary>The layout of the loaded level; null when it has none.</summary>
        public static MissionLayout Current { get; private set; }

        public int ButterfliesNeeded => _butterfliesNeeded;

        public int ButterflyCount => _butterflies != null ? _butterflies.Length : 0;

        public float GnomePhotoDistance => _gnomePhotoDistance;

        /// <summary>True when today's gnome photo asks for a stone gnome; false for a gnome with a water balloon.</summary>
        public bool GnomePhotoIsStone => _gnomePhotoIsStone;

        private void Awake()
        {
            Current = this;
            for (var i = 0; i < ButterflyCount; i++)
                _butterflies[i].Index = i;
        }

        private void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }

        /// <summary>The place asked in a photo slot; null for the gnome slot.</summary>
        public PhotoSubject Subject(int slot) => slot < GnomePhotoSlot ? _tasks[slot] : null;

        /// <summary>The butterfly spot at <paramref name="index"/>.</summary>
        public ButterflySpot Butterfly(int index) => _butterflies[index];

        /// <summary>True when the butterfly spot is used this match.</summary>
        public bool IsChosen(int index) => (_chosenButterflies & (1 << index)) != 0;

        /// <summary>Every peer: derives this match's choice from the seed and moves each item's start pose to its spot.</summary>
        public void ApplySeed(int seed)
        {
            _tasks[0] = Pick(_northSubjects, seed, 1);
            _tasks[1] = Pick(_southSubjects, seed, 2);
            _gnomePhotoIsStone = (Mix(seed, 3) & 1) == 0;
            ChooseButterflies(seed);

            for (var i = 0; i < _items.Length; i++)
            {
                if (TryGetSpot(i, seed, out var item, out var spot))
                    item.SetStartPosition(spot);
            }
        }

        /// <summary>Authority only: puts every item, at rest, on the spot of this match.</summary>
        public void PlaceItems(int seed)
        {
            for (var i = 0; i < _items.Length; i++)
            {
                if (TryGetSpot(i, seed, out var item, out var spot))
                    item.PlaceAt(spot, item.transform.rotation);
            }
        }

        private bool TryGetSpot(int index, int seed, out Carryable item, out Vector3 spot)
        {
            var entry = _items[index];
            item = entry.Item;
            spot = default;
            if (item == null || entry.Spots is not { Length: > 0 })
                return false;

            spot = entry.Spots[Mix(seed, 100 + index) % entry.Spots.Length];
            return true;
        }

        // Walks the spots from a random one with a stride coprime to the count, so the picks are distinct and spread out.
        private void ChooseButterflies(int seed)
        {
            _chosenButterflies = 0;
            var count = ButterflyCount;
            if (count == 0)
                return;

            var stride = 1 + Mix(seed, 4) % Mathf.Max(1, count - 1);
            while (count > 1 && Gcd(stride, count) != 1)
                stride++;

            var at = Mix(seed, 5) % count;
            for (var i = 0; i < Mathf.Min(_butterfliesPerMatch, count); i++)
            {
                _chosenButterflies |= 1 << at;
                at = (at + stride) % count;
            }
        }

        private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

        private static PhotoSubject Pick(PhotoSubject[] pool, int seed, int salt) =>
            pool is { Length: > 0 } ? pool[Mix(seed, salt) % pool.Length] : null;

        // Integer hash: the same on every peer and platform, unlike UnityEngine.Random.
        private static int Mix(int seed, int salt)
        {
            unchecked
            {
                var h = (uint)seed * 2654435761u + (uint)salt * 40503u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return (int)(h & 0x7FFFFFFF);
            }
        }
    }
}
