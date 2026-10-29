using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Keeps a dim backdrop (a sibling object) visible exactly while this modal window is active, so scripts that toggle
    /// the window with SetActive need no change. Added by the UI style tool.
    /// </summary>
    [DisallowMultipleComponent]
    public class UiBackdropLink : MonoBehaviour
    {
        [Tooltip("Backdrop object shown while this window is active.")]
        [SerializeField] private GameObject _backdrop;

        /// <summary>Backdrop object driven by this window.</summary>
        public GameObject Backdrop
        {
            get => _backdrop;
            set => _backdrop = value;
        }

        private void OnEnable()
        {
            if (_backdrop != null)
                _backdrop.SetActive(true);
        }

        private void OnDisable()
        {
            if (_backdrop != null)
                _backdrop.SetActive(false);
        }
    }
}
