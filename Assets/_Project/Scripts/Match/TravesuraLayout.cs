using System;
using BetterTogether.Audio;
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
            /// <summary>Done when the camper walks into an explosive statue while it is active.</summary>
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
            [Tooltip("Points to use, for Points.")]
            public int PointCount;
            [Tooltip("Seconds between the first and the last point; 0 means no limit.")]
            public float WindowSeconds;
            [Tooltip("Shown once the prank is done; may be empty.")]
            public GameObject Result;
            [Tooltip("Heard by everyone when the prank is done; may be empty.")]
            public SoundCue DoneSfx;
        }

        [SerializeField] private Entry[] _entries;
        [Tooltip("Pranks the gnomes need to win; the list may hold more, so matches differ.")]
        [SerializeField, Min(1)] private int _needed = 5;

        private byte _shownDone;
        private byte _previousDone;
        private bool _initialised;

        /// <summary>The layout of the loaded level; null when it has none.</summary>
        public static TravesuraLayout Current { get; private set; }

        public int Count => _entries != null ? _entries.Length : 0;

        public int Needed => _needed;

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
            if (match == null || !match.IsReady)
                return;

            var done = match.TravesurasDoneMask;
            if (done == _shownDone && _initialised)
                return;

            // A late joiner sees what is done, but hears nothing old.
            _previousDone = _initialised ? _shownDone : done;
            _initialised = true;
            _shownDone = done;
            for (var i = 0; i < Count; i++)
            {
                var finished = (done & (1 << i)) != 0;
                if (finished && (_previousDone & (1 << i)) == 0)
                    _entries[i].DoneSfx.Play();

                if (_entries[i].Result != null)
                    _entries[i].Result.SetActive(finished);
            }
        }
    }
}
