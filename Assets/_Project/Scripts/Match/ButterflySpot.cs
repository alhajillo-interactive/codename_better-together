using BetterTogether.Audio;
using BetterTogether.Interaction;
using UnityEngine;

namespace BetterTogether.Match
{
    /// <summary>
    /// A spot where a butterfly can appear. Scene object without networking: whether it shows comes from the replicated seed and
    /// the caught mask of <see cref="MatchManager"/>, and catching it is an RPC to the master, which validates with
    /// <see cref="MatchManager.CanCatch"/>, the same predicate as the prompt.
    /// </summary>
    public class ButterflySpot : MonoBehaviour, IInteractable, IHoldInteractable
    {
        [Tooltip("Visual root; hidden, with the collider, while no butterfly is here.")]
        [SerializeField] private GameObject _visual;
        [Tooltip("Left and right wing, flapped around their local Z.")]
        [SerializeField] private Transform[] _wings;
        [Tooltip("Collider the player aims at.")]
        [SerializeField] private Collider _collider;
        [Tooltip("Seconds the interact key is held to catch it.")]
        [SerializeField, Min(0f)] private float _holdSeconds = 0.8f;
        [Tooltip("Metres the butterfly drifts around its spot.")]
        [SerializeField, Min(0f)] private float _driftRadius = 0.35f;
        [Tooltip("Wing beats per second.")]
        [SerializeField, Min(0f)] private float _flapRate = 7f;
        [Tooltip("Largest wing angle in degrees.")]
        [SerializeField, Min(0f)] private float _flapAngle = 55f;
        [SerializeField] private SoundCue _catchSfx;

        private bool _shown;
        private bool _seenMatch;

        /// <summary>Position in the layout; also its bit in the caught mask. Set by <see cref="MissionLayout"/>.</summary>
        public int Index { get; set; }

        public Vector3 Position => transform.position;

        private void Awake() => Show(false);

        public string GetPrompt(PlayerInteractor interactor) => "Mantén para atrapar la mariposa";

        public bool CanInteract(PlayerInteractor interactor)
        {
            var match = MatchManager.Instance;
            return match != null && match.CanCatch(interactor, Index);
        }

        // The hold path does the work; a tap only starts the hold.
        public void Interact(PlayerInteractor interactor)
        {
        }

        public float GetHoldTime(PlayerInteractor interactor) => _holdSeconds;

        public void OnHoldComplete(PlayerInteractor interactor)
        {
            var match = MatchManager.Instance;
            if (match != null)
                match.RequestCatch(interactor, Index);
        }

        private void Update()
        {
            var match = MatchManager.Instance;
            var layout = MissionLayout.Current;
            var active = match != null && layout != null && match.MissionsRunning;
            var show = active && match.IsButterflyAvailable(Index);

            // A butterfly that goes away during a running match was caught; a late joiner never hears old ones.
            if (_shown && !show && active && _seenMatch)
                _catchSfx.PlayAt(transform.position);

            _seenMatch = active;
            if (show != _shown)
                Show(show);

            if (show)
                Flutter();
        }

        private void Show(bool show)
        {
            _shown = show;
            if (_visual != null)
                _visual.SetActive(show);
            if (_collider != null)
                _collider.enabled = show;
        }

        private void Flutter()
        {
            var t = Time.time + Index * 1.7f;
            _visual.transform.localPosition = new Vector3(Mathf.Sin(t * 0.9f), Mathf.Sin(t * 1.7f) * 0.5f, Mathf.Sin(t * 1.3f)) * _driftRadius;
            var angle = Mathf.Sin(t * _flapRate * Mathf.PI * 2f) * _flapAngle;
            for (var i = 0; i < _wings.Length; i++)
                _wings[i].localRotation = Quaternion.Euler(0f, 0f, i == 0 ? angle : -angle);
        }
    }
}
