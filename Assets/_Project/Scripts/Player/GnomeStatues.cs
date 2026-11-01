using BetterTogether.Audio;
using BetterTogether.Environment;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Match;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Player
{
    /// <summary>
    /// Lets a gnome leave ornaments of itself: plain ones to confuse the camper and an explosive one that throws the camper in the
    /// air. The owner writes the slots; every peer shows them with local decoys, so no network object is spawned. The camper's own
    /// client notices walking into a bomb, throws its character and tells the owner to clear it.
    /// Late joiner: Render shows the replicated slots. Master change: irrelevant, the player owns the state.
    /// Player leaves: this object despawns and its statues go with it.
    /// </summary>
    public class GnomeStatues : NetworkBehaviour
    {
        private const int PlainSlots = 2;
        private const int BombSlot = PlainSlots;
        private const int SlotCount = PlainSlots + 1;
        private const byte Empty = 0;
        private const byte Plain = 1;
        private const byte Explosive = 2;

        private struct Slot : INetworkStruct
        {
            public Vector3 Position;
            public float Yaw;
            public byte State;
        }

        [Tooltip("Ornament shown for each statue; the same prefab as the ones in the level, so they can't be told apart.")]
        [SerializeField] private GameObject _statuePrefab;
        [Tooltip("Seconds between two statues of this gnome.")]
        [SerializeField, Min(0f)] private float _placeCooldown = 1f;
        [Tooltip("Seconds between two explosive statues of this gnome.")]
        [SerializeField, Min(0f)] private float _bombCooldown = 12f;
        [Tooltip("Metres at which the camper sets a bomb off.")]
        [SerializeField, Min(0.1f)] private float _triggerRadius = 1.2f;
        [Tooltip("Upward speed given to the camper (m/s).")]
        [SerializeField, Min(0f)] private float _launchUp = 9f;
        [Tooltip("Horizontal speed given to the camper (m/s).")]
        [SerializeField, Min(0f)] private float _launchBack = 5f;
        [Tooltip("Seconds the camper cannot act after the explosion.")]
        [SerializeField, Min(0f)] private float _stunSeconds = 1.5f;
        [SerializeField] private SoundCue _placeSfx;
        [Tooltip("Heard at both ends of a rabbit hole, so the camper can follow the sound.")]
        [SerializeField] private SoundCue _hopSfx;
        [SerializeField] private SoundCue _boomSfx;

        [Networked, Capacity(SlotCount)] private NetworkArray<Slot> Slots => default;
        [Networked] private byte NextPlain { get; set; }
        [Networked] private TickTimer PlaceTimer { get; set; }
        [Networked] private TickTimer BombTimer { get; set; }
        [Networked] private byte BoomSeq { get; set; }
        [Networked] private Vector3 BoomPosition { get; set; }

        private readonly GameObject[] _shown = new GameObject[SlotCount];
        private readonly byte[] _shownState = new byte[SlotCount];
        private readonly Vector3[] _shownPosition = new Vector3[SlotCount];

        private PlayerCharacter _character;
        private PlayerInteractor _interactor;
        private Petrifiable _stone;
        private CharacterController _body;
        private InputAction _placeAction;
        private InputAction _bombAction;
        private bool _isSpawned;
        private bool _placeQueued;
        private bool _bombQueued;
        private bool _clearRequested;
        private bool _feedbackReady;
        private byte _shownBoomSeq;
        private float _reportedAt = float.NegativeInfinity;

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _interactor = GetComponent<PlayerInteractor>();
            _stone = GetComponent<Petrifiable>();
            _body = GetComponent<CharacterController>();
        }

        public override void Spawned()
        {
            _isSpawned = true;
            for (var i = 0; i < SlotCount; i++)
            {
                _shown[i] = Instantiate(_statuePrefab);
                _shown[i].SetActive(false);
            }

            if (!HasStateAuthority)
                return;

            _placeAction = InputSystem.actions.FindAction("Player/PlaceStatue");
            _bombAction = InputSystem.actions.FindAction("Player/PlaceBomb");
            MatchManager.OnGameStarted += HandleGameStarted;
            MatchManager.OnGameOver += HandleGameStarted;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            MatchManager.OnGameStarted -= HandleGameStarted;
            MatchManager.OnGameOver -= HandleGameStarted;
            for (var i = 0; i < SlotCount; i++)
            {
                if (_shown[i] != null)
                    Destroy(_shown[i]);
            }
        }

        private void HandleGameStarted() => _clearRequested = true;

        // Input is per frame: only flags are kept here, FixedUpdateNetwork decides and writes.
        private void Update()
        {
            if (!_isSpawned || !HasStateAuthority || FirstPersonCamera.CursorFree || FirstPersonCamera.IsTyping)
                return;

            if (_placeAction != null && _placeAction.WasPressedThisFrame())
                _placeQueued = true;
            if (_bombAction != null && _bombAction.WasPressedThisFrame())
                _bombQueued = true;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            if (_clearRequested)
            {
                _clearRequested = false;
                for (var i = 0; i < SlotCount; i++)
                    Slots.Set(i, default);
            }

            var place = _placeQueued;
            var bomb = _bombQueued;
            _placeQueued = false;
            _bombQueued = false;
            if (!place && !bomb || !CanPlace)
                return;

            if (bomb && BombTimer.ExpiredOrNotRunning(Runner))
            {
                Write(BombSlot, Explosive);
                BombTimer = TickTimer.CreateFromSeconds(Runner, _bombCooldown);
            }
            else if (place && PlaceTimer.ExpiredOrNotRunning(Runner))
            {
                Write(NextPlain, Plain);
                NextPlain = (byte)((NextPlain + 1) % PlainSlots);
                PlaceTimer = TickTimer.CreateFromSeconds(Runner, _placeCooldown);
            }
        }

        private bool CanPlace =>
            MatchManager.AllowsInteraction && _character.Role == PlayerRole.Gnome && _interactor.Carried == null
            && (_stone == null || (!_stone.IsStone && !_stone.IsLockedInCage)) && (_body == null || _body.isGrounded);

        private void Write(int slot, byte state)
        {
            Slots.Set(slot, new Slot { Position = transform.position, Yaw = transform.eulerAngles.y, State = state });
        }

        /// <summary>Clears a bomb the camper walked into. Anyone may call it: the owner checks that the slot holds a bomb.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Detonate()
        {
            var slot = Slots[BombSlot];
            if (slot.State != Explosive)
                return;

            BoomPosition = slot.Position;
            BoomSeq = (byte)(BoomSeq + 1);
            Slots.Set(BombSlot, default);
        }

        /// <summary>A hop through a rabbit hole: a stateless sound for everyone.</summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_Hop(Vector3 from, Vector3 to)
        {
            _hopSfx.PlayAt(from);
            _hopSfx.PlayAt(to);
        }

        public override void Render()
        {
            if (!_isSpawned)
                return;

            for (var i = 0; i < SlotCount; i++)
                ShowSlot(i);

            if (_feedbackReady && BoomSeq != _shownBoomSeq)
                _boomSfx.PlayAt(BoomPosition);

            _shownBoomSeq = BoomSeq;
            _feedbackReady = true;
            CheckCamper();
        }

        private void ShowSlot(int index)
        {
            var slot = Slots[index];
            if (slot.State == _shownState[index] && slot.Position == _shownPosition[index])
                return;

            _shownState[index] = slot.State;
            _shownPosition[index] = slot.Position;
            var statue = _shown[index];
            statue.SetActive(slot.State != Empty);
            if (slot.State == Empty)
                return;

            statue.transform.SetPositionAndRotation(slot.Position, Quaternion.Euler(0f, slot.Yaw, 0f));
            if (_feedbackReady)
                _placeSfx.PlayAt(slot.Position);
        }

        // Runs on the camper's client, the only one that can move the camper at once.
        private void CheckCamper()
        {
            var bomb = Slots[BombSlot];
            var local = PlayerCharacter.Local;
            if (!MatchManager.AllowsInteraction || bomb.State != Explosive || local == null || local.Role != PlayerRole.Camper || Time.time < _reportedAt + 1f)
                return;

            var offset = local.transform.position - bomb.Position;
            if (offset.sqrMagnitude > _triggerRadius * _triggerRadius)
                return;

            _reportedAt = Time.time;
            if (local.TryGetComponent<PlayerMovement>(out var movement))
            {
                var away = new Vector3(offset.x, 0f, offset.z);
                away = away.sqrMagnitude > 0.001f ? away.normalized : Vector3.back;
                movement.ApplyKnockback(away * _launchBack + Vector3.up * _launchUp, _stunSeconds);
            }

            RPC_Detonate();
            if (MatchManager.Instance != null)
                MatchManager.Instance.RequestBomb(local, bomb.Position);
        }
    }
}
