using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Combat;
using BetterTogether.Diagnostics;
using BetterTogether.Gaze;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using UnityEngine;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// A bush or trunk where gnomes hide a carried item and the camper finds it by searching (holding the interact key).
    /// Master-client object: the state authority validates hiding and searching with the same predicates the clients use
    /// for the prompt (<see cref="CanHide"/>, <see cref="CanSearch"/>). What is hidden is not stored here: it is the set of
    /// <see cref="Carryable"/>s pinned to this spot, which already replicates (late joiners and a new master see it).
    /// The search progress is local input and is never networked.
    /// </summary>
    public class HidingSpot : NetworkBehaviour, IPinSpot, IInteractable, IHoldInteractable, IStateAuthorityChanged
    {
        // Seconds a ejected item waits for the Carryable's own rule to make it dynamic before the jump is dropped.
        private const float EjectWaitSeconds = 1f;

        // Extra seconds, after the jump wait, that an ejected item may keep ignoring the spot's colliders before they are restored.
        private const float IgnoreMarginSeconds = 1.5f;

        // Seconds between occupancy checks for the hide sound (the check walks every Carryable).
        private const float OccupancyCheckInterval = 0.2f;

        // After spawning, changes in occupancy are baseline, not events (a late joiner must not hear past sounds).
        private const float QuietSecondsAfterSpawn = 1f;

        [Tooltip("Points inside the mesh where a hidden item rests, out of sight. One item per slot.")]
        [SerializeField] private Transform[] _slots;
        [SerializeField] private string _displayName = "arbusto";
        [Tooltip("Seconds the camper holds the interact key to search.")]
        [SerializeField, Min(0.1f)] private float _searchTime = 1.5f;
        [Tooltip("Speed (m/s) at which searched items fly out of the spot.")]
        [SerializeField, Min(0f)] private float _ejectSpeed = 3f;
        [Tooltip("Degrees above the horizon that searched items are thrown.")]
        [SerializeField, Range(0f, 89f)] private float _ejectElevation = 35f;
        [Tooltip("Distance (m) from the player's eyes to the spot that the authority accepts; a little more than the interaction reach to absorb latency.")]
        [SerializeField, Min(0f)] private float _maxDistance = 2.75f;
        [Tooltip("Heard by everyone from the spot when an item is hidden.")]
        [SerializeField] private SoundCue _hideSfx;
        [Tooltip("Heard by everyone from the spot when the camper searches it, whether or not it was empty.")]
        [SerializeField] private SoundCue _searchSfx;

        private struct PendingEject
        {
            public Carryable Item;
            public Vector3 Velocity;
            public float Until;
            public float RestoreAt;
            public bool Applied;
            public Collider[] Colliders;
        }

        private static readonly List<Carryable> PinnedBuffer = new(8);

        private readonly List<Carryable> _pinned = new(8);
        private readonly List<PendingEject> _ejecting = new(8);
        private Collider[] _colliders;
        private bool _isSpawned;
        private float _spawnTime;
        private int _lastCount;
        private float _nextOccupancyCheck;

        public Transform GetPinPoint(int slot) =>
            _slots != null && _slots.Length > 0 ? _slots[Mathf.Clamp(slot, 0, _slots.Length - 1)] : transform;

        /// <summary>Items hidden here can only come out by searching.</summary>
        public bool AllowsTakeFromPin => false;

        private void Awake()
        {
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        public override void Spawned()
        {
            _isSpawned = true;
            _spawnTime = Time.time;
            _lastCount = -1;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            RestoreAllCollisions();
        }

        /// <summary>The ignored collisions only exist on the old authority: they are restored at once when the authority changes.</summary>
        public void StateAuthorityChanged() => RestoreAllCollisions();

        // ---- Prompt (mirror of the authority's rules) ----

        public string GetPrompt(PlayerInteractor interactor)
        {
            if (interactor.Role == PlayerRole.Camper)
                return $"Registrar {_displayName}";

            var carried = interactor.Carried;
            return carried != null ? $"Esconder {carried.DisplayName} en {_displayName}" : $"Esconder en {_displayName}";
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            // A camper carrying a gnome has its hands full.
            if (interactor.TryGetComponent<GnomeCarrier>(out var gnomeCarrier) && gnomeCarrier.IsCarrying)
                return false;

            return interactor.Role == PlayerRole.Camper ? CanSearch(interactor) : CanHide(interactor);
        }

        public float GetHoldTime(PlayerInteractor interactor) => interactor.Role == PlayerRole.Camper ? _searchTime : 0f;

        public void Interact(PlayerInteractor interactor)
        {
            // Only gnomes tap; the camper searches through OnHoldComplete.
            if (interactor.Role == PlayerRole.Gnome)
                RPC_RequestHide(interactor.Id);
        }

        public void OnHoldComplete(PlayerInteractor interactor)
        {
            if (interactor.Role == PlayerRole.Camper)
                RPC_RequestSearch(interactor.Id);
        }

        /// <summary>A gnome carrying an item alone, able to act, in reach, with a free slot.</summary>
        public bool CanHide(PlayerInteractor who)
        {
            if (!_isSpawned || !MatchManager.AllowsInteraction || who == null || who.Role != PlayerRole.Gnome || !CanAct(who) || !InReach(who))
                return false;

            // Held and moving by exactly one holder means it needs one gnome only (a two-gnome item has two holders to move).
            var carried = who.Carried;
            return carried != null && carried.HolderCount == 1 && carried.IsMoving && !carried.Inert && TryGetFreeSlot(out _);
        }

        /// <summary>
        /// The camper with empty hands (so E keeps placing, pouring and dropping what is carried), able to act and in reach.
        /// An empty spot can be searched too. The local player also counts a predicted grab as a full hand.
        /// </summary>
        public bool CanSearch(PlayerInteractor who) =>
            _isSpawned && MatchManager.AllowsInteraction && who != null && who.Role == PlayerRole.Camper
            && who.Carried == null && (who != PlayerInteractor.Local || who.CarriedOrPredicted == null)
            && CanAct(who) && InReach(who);

        private static bool CanAct(PlayerInteractor who) =>
            !(who.TryGetComponent<Kickable>(out var kickable) && kickable.IsStunned)
            && !(who.TryGetComponent<Petrifiable>(out var petrifiable) && petrifiable.IsStone);

        private bool InReach(PlayerInteractor who)
        {
            var eye = who.EyePosition;
            var limit = _maxDistance * _maxDistance;
            foreach (var col in _colliders)
            {
                if (col != null && col.enabled && (col.ClosestPoint(eye) - eye).sqrMagnitude <= limit)
                    return true;
            }

            return false;
        }

        // Replicated state only: the slots taken are the pinned items' slots.
        private bool TryGetFreeSlot(out int slot)
        {
            slot = -1;
            var count = _slots != null ? _slots.Length : 0;
            if (count == 0)
                return false;

            Carryable.GetPinnedTo(this, PinnedBuffer);
            var taken = 0;
            foreach (var item in PinnedBuffer)
                taken |= 1 << Mathf.Clamp(item.PinnedSlot, 0, 30);

            for (var i = 0; i < count; i++)
            {
                if ((taken & (1 << i)) == 0)
                {
                    slot = i;
                    return true;
                }
            }

            return false;
        }

        // ---- Authority ----

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestHide(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanHide(who) || !TryGetFreeSlot(out var slot))
                return;

            var item = who.Carried;
            item.ForceReleaseAll();
            item.Pin(this, slot);
            if (MatchManager.Instance != null) MatchManager.Instance.Award(who.Object.StateAuthority, MatchManager.ScoreReason.Hide);
            if (Telemetry.Enabled)
                Telemetry.Begin("hide", who).Str("object", item.DisplayName).Str("spot", _displayName).Int("occupancy", PinnedCount()).Send();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestSearch(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanSearch(who))
                return;

            Carryable.GetPinnedTo(this, _pinned);
            var found = _pinned.Count;
            var elevation = _ejectElevation * Mathf.Deg2Rad;
            foreach (var item in _pinned)
            {
                // Out of the spot towards the camper, so the item lands where it can be seen and picked up.
                var outward = Vector3.ProjectOnPlane(who.transform.position - transform.position, Vector3.up);
                if (outward.sqrMagnitude < 0.0001f)
                    outward = transform.forward;

                var velocity = (outward.normalized * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation)) * _ejectSpeed;
                // The item stays kinematic until the Carryable's own rule makes it dynamic (and zeroes its velocity),
                // so the jump is applied in FixedUpdateNetwork afterwards.
                item.Unpin();
                // The slots lie inside the solid collider: without this PhysX would shove the item out violently.
                var colliders = item.GetComponentsInChildren<Collider>();
                SetIgnore(colliders, true);
                _ejecting.Add(new PendingEject
                {
                    Item = item,
                    Velocity = velocity,
                    Until = Time.time + EjectWaitSeconds,
                    RestoreAt = Time.time + EjectWaitSeconds + IgnoreMarginSeconds,
                    Colliders = colliders,
                });
            }

            RPC_PlaySearch();
            if (Telemetry.Enabled)
                Telemetry.Begin("search", who).Str("spot", _displayName).Int("found", found).Send();
        }

        // Authority only. An ejected item gets its jump once the Carryable's rule makes it dynamic; it keeps ignoring the
        // spot's colliders until it leaves the spot's bounds or the time runs out. If the master migrates mid-ejection the
        // new master has no pending list, so the item simply falls without the jump (collisions are restored on the old one).
        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            for (var i = _ejecting.Count - 1; i >= 0; i--)
            {
                var pending = _ejecting[i];
                var item = pending.Item;
                if (item == null || item.Object == null || !item.Object.IsValid || Time.time > pending.RestoreAt || item.IsPinned || item.IsHeld
                    || (pending.Applied && !Overlaps(pending.Colliders)))
                {
                    SetIgnore(pending.Colliders, false);
                    _ejecting.RemoveAt(i);
                    continue;
                }

                if (pending.Applied)
                    continue;

                if (Time.time > pending.Until || !item.TryGetComponent<Rigidbody>(out var body))
                {
                    pending.Applied = true;
                    _ejecting[i] = pending;
                    continue;
                }

                if (body.isKinematic)
                    continue;

                body.linearVelocity = pending.Velocity;
                pending.Applied = true;
                _ejecting[i] = pending;
            }
        }

        // Whether any collider of the item still touches the spot's bounds.
        private bool Overlaps(Collider[] items)
        {
            foreach (var item in items)
            {
                if (item == null)
                    continue;

                foreach (var spot in _colliders)
                {
                    if (spot != null && spot.bounds.Intersects(item.bounds))
                        return true;
                }
            }

            return false;
        }

        private void SetIgnore(Collider[] items, bool ignore)
        {
            if (items == null)
                return;

            foreach (var item in items)
            {
                if (item == null)
                    continue;

                foreach (var spot in _colliders)
                {
                    if (spot != null)
                        Physics.IgnoreCollision(item, spot, ignore);
                }
            }
        }

        private void RestoreAllCollisions()
        {
            foreach (var pending in _ejecting)
                SetIgnore(pending.Colliders, false);
            _ejecting.Clear();
        }

        private int PinnedCount()
        {
            Carryable.GetPinnedTo(this, _pinned);
            return _pinned.Count;
        }

        // Stateless event: the search itself is not replicated state, so everyone hears it, even when nothing was inside.
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlaySearch() => _searchSfx.PlayAt(transform.position);

        // ---- Presentation ----

        // Local feedback derived from replicated state: an item appearing in the spot means someone hid it.
        public override void Render()
        {
            if (!_isSpawned)
                return;

            if (Time.time < _nextOccupancyCheck)
                return;

            _nextOccupancyCheck = Time.time + OccupancyCheckInterval;
            var count = PinnedCount();
            if (_lastCount >= 0 && count > _lastCount && Time.time - _spawnTime > QuietSecondsAfterSpawn)
                _hideSfx.PlayAt(transform.position);
            _lastCount = count;
        }
    }
}
