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
    /// Master-client object: the state authority counts the logs and validates requests.
    /// </summary>
    public class FireplaceManager : NetworkBehaviour, IInteractable, IMatchResettable
    {
        private const int MaxColliders = 32;

        [FormerlySerializedAs("amountLabel")]
        [SerializeField] private TextMeshProUGUI _amountLabel;
        [FormerlySerializedAs("requiredAmount")]
        [SerializeField] private int _requiredWood = 5;
        [SerializeField] private GameObject _fire;

        [Header("Logs detection")]
        [FormerlySerializedAs("logsLayer")]
        [SerializeField] private LayerMask _logsLayer = ~0;
        [FormerlySerializedAs("detectorRadius")]
        [SerializeField] private float _detectorRadius = 1f;

        private readonly Collider[] _results = new Collider[MaxColliders];
        private readonly HashSet<FireplaceWood> _counted = new();
        private bool _isSpawned;

        [Networked, OnChangedRender(nameof(OnLogsChanged))]
        private int LogsInside { get; set; }

        [Networked, OnChangedRender(nameof(OnLitChanged))]
        public NetworkBool IsLit { get; private set; }

        public static FireplaceManager Instance { get; private set; }

        public int RequiredWood => _requiredWood;
        public int WoodCount => _isSpawned ? LogsInside : 0;
        public bool HasEnoughWood => _isSpawned && LogsInside >= _requiredWood;

        public override void Spawned()
        {
            _isSpawned = true;
            Instance = this;
            UpdateLabel();
            UpdateFire();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Authority only: the fire is out; logs are counted again from the zone.</summary>
        public void ResetForMatch() => IsLit = false;

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

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            LogsInside = CountLogs();

            if (IsLit && !HasEnoughWood)
                IsLit = false;
        }

        /// <summary>Logs inside the zone; one being carried never counts.</summary>
        private int CountLogs()
        {
            _counted.Clear();
            var found = Physics.OverlapSphereNonAlloc(transform.position, _detectorRadius, _results, _logsLayer);
            for (var i = 0; i < found; i++)
            {
                if (!_results[i].TryGetComponent<FireplaceWood>(out var wood))
                    continue;

                if (wood.TryGetComponent<Carryable>(out var carryable) && carryable.IsMoving)
                    continue;

                _counted.Add(wood);
            }

            return _counted.Count;
        }

        private void OnLogsChanged() => UpdateLabel();

        private void OnLitChanged()
        {
            UpdateLabel();
            UpdateFire();
        }

        private void UpdateLabel()
        {
            if (_amountLabel == null)
                return;

            _amountLabel.text = IsLit ? "¡Encendida!" : $"{LogsInside}/{_requiredWood}";
        }

        private void UpdateFire()
        {
            if (_fire != null && _fire.activeSelf != IsLit)
                _fire.SetActive(IsLit);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, _detectorRadius);
        }
    }
}
