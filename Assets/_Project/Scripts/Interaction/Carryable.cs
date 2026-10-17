using Fusion;
using UnityEngine;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// An object a player can pick up and drop, one at a time. Whoever picks it up takes state authority
    /// (Shared mode) and every client shows it attached to the holder's carry anchor.
    /// </summary>
    public class Carryable : NetworkBehaviour, IInteractable
    {
        [SerializeField] private string _displayName = "objeto";

        private Collider[] _colliders;
        private PlayerInteractor _pendingHolder;
        private float _halfHeight;
        private bool _collidersEnabled = true;

        [Networked] public PlayerInteractor Holder { get; private set; }

        public bool IsCarried => CurrentHolder != null;
        public string Prompt => $"Tomar {_displayName}";

        // A holder that left the game no longer counts.
        private PlayerInteractor CurrentHolder =>
            Holder != null && Holder.Object != null && Holder.Object.IsValid ? Holder : null;

        private void Awake()
        {
            _colliders = GetComponentsInChildren<Collider>();
        }

        public override void Spawned()
        {
            _halfHeight = _colliders.Length > 0 ? _colliders[0].bounds.extents.y : 0f;
        }

        public bool CanInteract(PlayerInteractor interactor) =>
            !IsCarried && interactor.Carried == null && _pendingHolder == null;

        public void Interact(PlayerInteractor interactor) => PickUp(interactor);

        private void PickUp(PlayerInteractor interactor)
        {
            _pendingHolder = interactor;
            if (HasStateAuthority)
                CompletePickUp();
            else
                Object.RequestStateAuthority();
        }

        private void CompletePickUp()
        {
            if (CurrentHolder == null)
            {
                Holder = _pendingHolder;
                _pendingHolder.SetCarried(this);
            }

            _pendingHolder = null;
        }

        /// <summary>Called by the holder's client, which has state authority.</summary>
        public void Drop()
        {
            var holder = CurrentHolder;
            if (holder == null || !HasStateAuthority)
                return;

            var holderTransform = holder.transform;
            var point = holderTransform.position + holderTransform.forward * Mathf.Max(0.4f, holder.Reach * 0.4f);
            var groundY = Physics.Raycast(point + Vector3.up, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : holderTransform.position.y;

            var yaw = Quaternion.Euler(0f, holderTransform.eulerAngles.y, 0f);
            transform.SetPositionAndRotation(new Vector3(point.x, groundY + _halfHeight, point.z), yaw);

            holder.SetCarried(null);
            Holder = null;
        }

        public override void FixedUpdateNetwork()
        {
            if (HasStateAuthority)
                FollowHolder();
        }

        public override void Render()
        {
            var shouldEnable = !IsCarried;
            if (shouldEnable == _collidersEnabled)
                return;

            _collidersEnabled = shouldEnable;
            foreach (var c in _colliders)
                c.enabled = shouldEnable;
        }

        private void Update()
        {
            // In Shared mode FixedUpdateNetwork only runs on the authority, so the requester waits here.
            if (_pendingHolder != null && Object != null && Object.IsValid && HasStateAuthority)
                CompletePickUp();
        }

        private void LateUpdate() => FollowHolder();

        private void FollowHolder()
        {
            if (Object == null || !Object.IsValid)
                return;

            var holder = CurrentHolder;
            if (holder != null)
                transform.SetPositionAndRotation(holder.CarryAnchor.position, holder.CarryAnchor.rotation);
        }
    }
}
