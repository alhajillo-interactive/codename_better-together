using BetterTogether.Player;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// Lets the local player focus an <see cref="IInteractable"/> by looking at it and use it with the interact key.
    /// Also tracks the one <see cref="Carryable"/> the player is holding.
    /// </summary>
    public class PlayerInteractor : NetworkBehaviour
    {
        [Tooltip("Where carried objects are held, as a direct child of the player root.")]
        [SerializeField] private Transform _carryAnchor;
        [SerializeField] private float _reach = 2f;
        [SerializeField] private LayerMask _interactionMask = ~0;

        private InputAction _interact;

        public static PlayerInteractor Local { get; private set; }

        public Transform CarryAnchor => _carryAnchor;
        public float Reach => _reach;

        /// <summary>Only meaningful on the local player.</summary>
        public Carryable Carried { get; private set; }
        public IInteractable Focused { get; private set; }

        public override void Spawned()
        {
            if (!HasStateAuthority)
                return;

            Local = this;
            _interact = InputSystem.actions.FindAction("Player/Interact");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Local == this)
                Local = null;
        }

        internal void SetCarried(Carryable carryable) => Carried = carryable;

        private void Update()
        {
            if (Local != this)
                return;

            UpdateFocus();

            // The action has a Hold interaction; WasPressedThisFrame ignores it.
            if (_interact == null || !_interact.WasPressedThisFrame())
                return;

            if (Focused != null && Focused.CanInteract(this))
                Focused.Interact(this);
            else if (Carried != null)
                Carried.Drop();
        }

        private void UpdateFocus()
        {
            Focused = null;
            var view = FirstPersonCamera.Instance;
            if (view == null)
                return;

            var origin = view.transform;
            if (Physics.Raycast(origin.position, origin.forward, out var hit, _reach, _interactionMask, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(transform))
                Focused = hit.collider.GetComponentInParent<IInteractable>();
        }
    }
}
