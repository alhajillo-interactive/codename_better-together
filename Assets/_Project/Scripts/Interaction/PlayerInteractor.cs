using BetterTogether.Fireplace;
using BetterTogether.Player;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// Lets the local player focus an <see cref="IInteractable"/> by looking at it and use it with the interact key.
    /// What the player is holding is derived from the <see cref="Carryable"/>s themselves.
    /// </summary>
    public class PlayerInteractor : NetworkBehaviour
    {
        [Tooltip("Where carried objects are held, as a direct child of the player root.")]
        [SerializeField] private Transform _carryAnchor;
        [SerializeField] private float _reach = 2f;
        [SerializeField] private float _aimRadius = 0.1f;
        [SerializeField] private LayerMask _interactionMask = ~0;

        private InputAction _interact;
        private PlayerMovement _movement;
        private PlayerCharacter _character;

        public static PlayerInteractor Local { get; private set; }

        public Transform CarryAnchor => _carryAnchor;
        public float Reach => _reach;
        public PlayerRole Role => _character.Role;

        public Carryable Carried => Carryable.FindHeldBy(this);
        public IInteractable Focused { get; private set; }

        /// <summary>The cooked sausage this player is holding and can eat, if any.</summary>
        public Sausage EdibleCarried
        {
            get
            {
                var carried = Carried;
                return carried != null && carried.TryGetComponent<Sausage>(out var sausage) && sausage.CanBeEatenBy(this)
                    ? sausage
                    : null;
            }
        }

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
        }

        public override void Spawned()
        {
            if (!HasStateAuthority)
                return;

            Local = this;
            _interact = InputSystem.actions.FindAction("Player/Interact");
            _movement = GetComponent<PlayerMovement>();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Local == this)
                Local = null;
        }

        private void Update()
        {
            if (Local != this)
                return;

            // Stunned or frozen (watched by the camper) players can't pick anything up or drop it.
            if (_movement != null && (_movement.IsStunned || _movement.IsFrozen))
            {
                Focused = null;
                return;
            }

            UpdateFocus();

            // The action has a Hold interaction; WasPressedThisFrame ignores it.
            if (_interact == null || !_interact.WasPressedThisFrame())
                return;

            var carried = Carried;
            if (Focused != null && Focused.CanInteract(this))
                Focused.Interact(this);
            else if (carried != null)
            {
                var edible = EdibleCarried;
                if (edible != null)
                    edible.RequestEat(this);
                else
                    carried.RequestRelease(this);
            }
        }

        private void UpdateFocus()
        {
            Focused = null;
            var view = FirstPersonCamera.Instance;
            if (view == null)
                return;

            var origin = view.transform;
            if (Physics.SphereCast(origin.position, _aimRadius, origin.forward, out var hit, _reach, _interactionMask, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(transform))
                Focused = PickInteractable(hit.collider);
        }

        // Nested interactables (e.g. the grill inside the campfire): prefer the first one that accepts this player right now.
        private IInteractable PickInteractable(Collider collider)
        {
            var candidates = collider.GetComponentsInParent<IInteractable>();
            foreach (var candidate in candidates)
            {
                if (candidate.CanInteract(this))
                    return candidate;
            }

            return candidates.Length > 0 ? candidates[0] : null;
        }
    }
}
