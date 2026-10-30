using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using UnityEngine;

namespace BetterTogether.Fireplace
{
    /// <summary>
    /// The grill over the campfire: a camper puts the sausage on it and it cooks while the fire is lit.
    /// Master-client object: the state authority validates placing and drives the cooking.
    /// </summary>
    public class FireplaceGrill : NetworkBehaviour, IInteractable, IPinSpot, IMatchResettable
    {
        [SerializeField] private Transform _spot;

        private FireplaceManager _fireplace;
        private bool _isSpawned;

        [Networked] private Sausage Placed { get; set; }

        public Transform GetPinPoint(int slot) => _spot;

        private bool SpotFree => Placed == null || !Placed.Carryable.IsPinnedTo(this);

        private void Awake()
        {
            _fireplace = GetComponentInParent<FireplaceManager>();
        }

        public override void Spawned()
        {
            _isSpawned = true;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
        }

        /// <summary>Authority only: the spot is free again.</summary>
        public void ResetForMatch()
        {
            Placed = null;
        }

        public string GetPrompt(PlayerInteractor interactor) => "Poner salchicha";

        /// <summary>Mirrors the authority's rules using the replicated state.</summary>
        public bool CanInteract(PlayerInteractor interactor)
        {
            if (!_isSpawned || !MatchManager.AllowsInteraction || interactor.Role != PlayerRole.Camper || !SpotFree)
                return false;

            var carried = interactor.Carried;
            // Only a raw sausage goes on the grill: a cooked one must be eaten (E would otherwise put it back to burn),
            // and a burnt one has nothing left to cook.
            return carried != null
                && carried.TryGetComponent<Sausage>(out var sausage)
                && !sausage.Eaten
                && sausage.State == Sausage.CookState.Raw;
        }

        public void Interact(PlayerInteractor interactor)
        {
            var carried = interactor.Carried;
            if (carried != null && carried.HasPrediction)
                return;

            RPC_RequestPlace(interactor.Id);

            // The sausage starts flying to the grill right away; the authority still decides.
            if (carried != null && !HasStateAuthority)
                carried.PredictPin(this);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestPlace(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanInteract(who))
                return;

            var sausage = who.Carried.GetComponent<Sausage>();
            sausage.Carryable.ForceReleaseAll();
            sausage.Carryable.Pin(this);
            Placed = sausage;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            var sausage = Placed;
            if (sausage == null)
                return;

            if (sausage.Object == null || !sausage.Object.IsValid || !sausage.Carryable.IsPinnedTo(this))
            {
                Placed = null;
                return;
            }

            if (_fireplace.IsLit)
                sausage.Cook(Runner.DeltaTime);
        }
    }
}
