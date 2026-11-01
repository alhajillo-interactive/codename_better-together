using BetterTogether.Interaction;
using UnityEngine;

namespace BetterTogether.Match
{
    /// <summary>
    /// One place where a gnome acts for the active prank, holding the interact key. Scene object without networking: it shows while
    /// the prank needs it, and the master validates with <see cref="MatchManager.CanUsePoint"/>, the same check as the prompt.
    /// </summary>
    public class TravesuraPoint : MonoBehaviour, IInteractable, IHoldInteractable
    {
        [Tooltip("Index of the prank in the TravesuraLayout.")]
        [SerializeField, Min(0)] private int _travesura;
        [Tooltip("Index of this point inside the prank.")]
        [SerializeField, Min(0)] private int _point;
        [Tooltip("Text of the prompt, in Spanish.")]
        [SerializeField] private string _prompt = "Mantén para actuar";
        [Tooltip("Optional tag of the item the gnome must carry here; the item stays at this point.")]
        [SerializeField] private string _requiredItemTag;
        [Tooltip("Seconds the interact key is held.")]
        [SerializeField, Min(0f)] private float _holdSeconds = 0.8f;
        [Tooltip("Marker and aim collider, hidden while this point is not needed.")]
        [SerializeField] private GameObject _marker;

        private bool _shown = true;

        public int Travesura => _travesura;

        public int Point => _point;

        public string RequiredItemTag => _requiredItemTag;

        public Vector3 Position => transform.position;

        private void Awake() => Show(false);

        public string GetPrompt(PlayerInteractor interactor) => _prompt;

        public bool CanInteract(PlayerInteractor interactor)
        {
            var match = MatchManager.Instance;
            return match != null && match.CanUsePoint(interactor, this);
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
                match.RequestPoint(interactor, this);
        }

        private void Update()
        {
            var match = MatchManager.Instance;
            var show = match != null && match.IsPointNeeded(_travesura, _point);
            if (show != _shown)
                Show(show);
        }

        private void Show(bool show)
        {
            _shown = show;
            if (_marker != null)
                _marker.SetActive(show);
        }
    }
}
