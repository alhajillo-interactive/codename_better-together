using System.Collections.Generic;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace BetterTogether.Fireplace
{
    /// <summary>
    /// The campfire. Counts the logs inside its zone and, once there are enough, a camper holding the matches can light it.
    /// If gnomes pull logs out and the count drops below the requirement, the fire goes out.
    /// Master-client object: the state authority tracks the logs and validates requests.
    /// </summary>
    public class FireplaceManager : NetworkBehaviour, IInteractable, IMatchResettable
    {
        private const int MaxTracked = 32;

        [FormerlySerializedAs("amountLabel")]
        [SerializeField] private TextMeshProUGUI _amountLabel;
        [FormerlySerializedAs("requiredAmount")]
        [SerializeField] private int _requiredWood = 5;
        [SerializeField] private GameObject _fire;

        private readonly List<NetworkObject> _scratch = new();
        private ChangeDetector _detector;
        private bool _isSpawned;

        // The replicated list: all clients see the same set of object ids.
        [Networked, Capacity(MaxTracked)]
        private NetworkLinkedList<NetworkObject> Tracked { get; }

        [Networked] public NetworkBool IsLit { get; private set; }

        public static FireplaceManager Instance { get; private set; }

        public int RequiredWood => _requiredWood;
        public int WoodCount => _isSpawned ? Tracked.Count : 0;
        public bool HasEnoughWood => _isSpawned && Tracked.Count >= _requiredWood;

        public override void Spawned()
        {
            _isSpawned = true;
            Instance = this;
            _detector = GetChangeDetector(ChangeDetector.Source.SimulationState);
            UpdateLabel();
            UpdateFire();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Authority only: the fire is out and no log counts until it is back in the zone.</summary>
        public void ResetForMatch()
        {
            IsLit = false;
            Tracked.Clear();
        }

        public string GetPrompt(PlayerInteractor interactor) => "Prender fogata";

        /// <summary>Mirrors the authority's rules using the replicated state.</summary>
        public bool CanInteract(PlayerInteractor interactor)
        {
            if (!_isSpawned || !MatchManager.AllowsInteraction || IsLit || interactor.Role != PlayerRole.Camper || !HasEnoughWood)
                return false;

            var carried = interactor.Carried;
            return carried != null && carried.TryGetComponent<Matches>(out _);
        }

        public void Interact(PlayerInteractor interactor) => RPC_RequestLight(interactor.Id);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestLight(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanInteract(who))
                return;

            IsLit = true;
        }

        private void OnTriggerEnter(Collider other) => Track(other);

        private void OnTriggerStay(Collider other) => Track(other);

        private void OnTriggerExit(Collider other)
        {
            if (!_isSpawned || !HasStateAuthority)
                return;

            if (!other.TryGetComponent<FireplaceWood>(out var wood))
                return;

            Tracked.Remove(wood.NObject);
        }

        private void Track(Collider other)
        {
            if (!_isSpawned || !HasStateAuthority)
                return;

            if (!other.TryGetComponent<FireplaceWood>(out var wood))
                return;

            // A log in somebody's hands is not in the zone.
            if (wood.TryGetComponent<Carryable>(out var carryable) && carryable.IsMoving)
                return;

            if (!Tracked.Contains(wood.NObject))
                Tracked.Add(wood.NObject);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            PruneTracked();

            if (IsLit && !HasEnoughWood)
                IsLit = false;
        }

        /// <summary>Drops logs that despawned or are being carried off (colliders of carried items don't report exits).</summary>
        private void PruneTracked()
        {
            _scratch.Clear();
            var changed = false;
            foreach (var obj in Tracked)
            {
                var valid = obj != null
                    && obj.IsValid
                    && !(obj.TryGetComponent<Carryable>(out var carryable) && carryable.IsMoving);
                if (valid)
                    _scratch.Add(obj);
                else
                    changed = true;
            }

            if (!changed)
                return;

            Tracked.Clear();
            foreach (var obj in _scratch)
                Tracked.Add(obj);
        }

        public override void Render()
        {
            foreach (var change in _detector.DetectChanges(this))
            {
                if (change == nameof(Tracked) || change == nameof(IsLit))
                    UpdateLabel();
            }

            UpdateFire();
        }

        private void UpdateLabel()
        {
            if (_amountLabel == null)
                return;

            _amountLabel.text = IsLit ? "¡Encendida!" : $"{Tracked.Count}/{_requiredWood}";
        }

        private void UpdateFire()
        {
            if (_fire != null && _fire.activeSelf != IsLit)
                _fire.SetActive(IsLit);
        }
    }
}
