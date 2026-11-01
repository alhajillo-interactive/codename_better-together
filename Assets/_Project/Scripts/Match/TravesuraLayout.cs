using System;
using UnityEngine;

namespace BetterTogether.Match
{
    /// <summary>
    /// The list of pranks of one level. Scene component without networking; progress lives in <see cref="MatchManager"/>.
    /// A level without it plays as before. It also shows the lasting result of each finished prank. Built by AbrazoLevelBuilder.
    /// </summary>
    public class TravesuraLayout : MonoBehaviour
    {
        public enum Kind
        {
            /// <summary>Done when every point of the prank is used by different gnomes.</summary>
            Points,
            /// <summary>Done when the fire goes out while it is active.</summary>
            FireOut,
            /// <summary>Armed at its point; done when the camper walks into the bomb.</summary>
            Bomb,
        }

        [Serializable]
        public struct Entry
        {
            [Tooltip("What the gnomes read, in Spanish.")]
            public string Label;
            [Tooltip("What the camper reads: where something is going on, not what.")]
            public string CamperHint;
            public Kind Kind;
            [Tooltip("Points to use, for Points and Bomb.")]
            public int PointCount;
            [Tooltip("Seconds between the first and the last point; 0 means no limit.")]
            public float WindowSeconds;
            [Tooltip("Shown once the prank is done; may be empty.")]
            public GameObject Result;
        }

        [SerializeField] private Entry[] _entries;

        private byte _shownDone;

        /// <summary>The layout of the loaded level; null when it has none.</summary>
        public static TravesuraLayout Current { get; private set; }

        public int Count => _entries != null ? _entries.Length : 0;

        public ref readonly Entry Get(int index) => ref _entries[index];

        private void Awake()
        {
            Current = this;
            for (var i = 0; i < Count; i++)
            {
                if (_entries[i].Result != null)
                    _entries[i].Result.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }

        private void Update()
        {
            var match = MatchManager.Instance;
            var done = match != null ? match.TravesurasDoneMask : (byte)0;
            if (done == _shownDone)
                return;

            _shownDone = done;
            for (var i = 0; i < Count; i++)
            {
                if (_entries[i].Result != null)
                    _entries[i].Result.SetActive((done & (1 << i)) != 0);
            }
        }
    }
}
