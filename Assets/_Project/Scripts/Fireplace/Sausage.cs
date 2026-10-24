using BetterTogether.Audio;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using UnityEngine;

namespace BetterTogether.Fireplace
{
    /// <summary>
    /// The camper's sausage. It cooks on the grill while the fire is lit (raw, cooked, burnt) and a camper holding
    /// it cooked can eat it, which completes the objective. A burnt sausage that nobody holds returns to its
    /// starting spot after a while so the match can't be soft-locked.
    /// Master-client object: the state authority validates eating and runs the cooking and reset timers.
    /// </summary>
    [RequireComponent(typeof(Carryable))]
    public class Sausage : NetworkBehaviour, IMatchResettable
    {
        public enum CookState
        {
            Raw,
            Cooked,
            Burnt,
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Seconds on a lit grill until it is ready to eat.")]
        [SerializeField] private float _cookedAt = 15f;
        [Tooltip("Seconds on a lit grill until it burns.")]
        [SerializeField] private float _burntAt = 25f;
        [Tooltip("Seconds a burnt, unattended sausage waits before going back to its spawn point.")]
        [SerializeField] private float _resetDelay = 4f;
        [Tooltip("Heard by everyone, from the sausage, when the camper eats it.")]
        [SerializeField] private SoundCue _eatSfx;
        [SerializeField] private Transform _visual;
        [SerializeField] private Color _rawColor = new(0.9f, 0.45f, 0.45f);
        [SerializeField] private Color _cookedColor = new(0.65f, 0.3f, 0.1f);
        [SerializeField] private Color _burntColor = new(0.08f, 0.06f, 0.05f);

        private Carryable _carryable;
        private Rigidbody _body;
        private NetworkTransform _networkTransform;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private bool _isSpawned;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private CookState _shownState = (CookState)(-1);
        private bool _lastEaten;

        [Networked] private float CookTime { get; set; }
        [Networked] private NetworkBool IsEaten { get; set; }
        [Networked] private TickTimer ResetTimer { get; set; }

        public static Sausage Instance { get; private set; }

        public Carryable Carryable => _carryable;

        public bool Eaten => _isSpawned && IsEaten;

        public CookState State
        {
            get
            {
                if (!_isSpawned)
                    return CookState.Raw;

                if (CookTime >= _burntAt)
                    return CookState.Burnt;

                return CookTime >= _cookedAt ? CookState.Cooked : CookState.Raw;
            }
        }

        /// <summary>Spanish name of the current state, for the HUD.</summary>
        public string StateLabel => State switch
        {
            CookState.Cooked => "Lista",
            CookState.Burnt => "Quemada",
            _ => "Cruda",
        };

        private void Awake()
        {
            _carryable = GetComponent<Carryable>();
            _body = GetComponent<Rigidbody>();
            _networkTransform = GetComponent<NetworkTransform>();
            _renderers = _visual != null ? _visual.GetComponentsInChildren<Renderer>(true) : System.Array.Empty<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        public override void Spawned()
        {
            _isSpawned = true;
            Instance = this;
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _lastEaten = IsEaten; // Late joiners don't hear a bite that already happened.
            ApplyEaten();
            ApplyColor();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Authority only: called by the grill every tick the sausage cooks.</summary>
        public void Cook(float deltaTime)
        {
            CookTime += deltaTime;
        }

        public bool CanBeEatenBy(PlayerInteractor interactor)
        {
            return _isSpawned
                && MatchManager.AllowsInteraction
                && !IsEaten
                && State == CookState.Cooked
                && interactor.Role == PlayerRole.Camper
                && Carryable.FindHeldBy(interactor) == _carryable;
        }

        /// <summary>Authority only: raw and uneaten again. Its <see cref="Carryable"/> puts the pose back.</summary>
        public void ResetForMatch()
        {
            CookTime = 0f;
            IsEaten = false;
            ResetTimer = default;
            ApplyEaten();
        }

        /// <summary>Permite al bot comerse la salchicha cocinada para ganar la partida.</summary>
        public void ComerPorBot()
        {
            if (State == CookState.Cooked && !IsEaten)
            {
                _carryable.ForceReleaseAll();
                IsEaten = true;
                ApplyEaten();
            }
        }


        public void RequestEat(PlayerInteractor interactor) => RPC_RequestEat(interactor.Id);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestEat(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanBeEatenBy(who))
                return;

            _carryable.ForceReleaseAll();
            IsEaten = true;
            ApplyEaten();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            ApplyEaten();

            var stranded = !IsEaten
                && State == CookState.Burnt
                && !_carryable.IsHeld
                && !_carryable.IsPinned;
            if (!stranded)
            {
                if (ResetTimer.IsRunning)
                    ResetTimer = default;
                return;
            }

            if (!ResetTimer.IsRunning)
                ResetTimer = TickTimer.CreateFromSeconds(Runner, _resetDelay);
            else if (ResetTimer.Expired(Runner))
                ResetToSpawn();
        }

        private void ResetToSpawn()
        {
            ResetTimer = default;
            CookTime = 0f;
            if (!_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            if (_networkTransform != null)
                _networkTransform.Teleport(_spawnPosition, _spawnRotation);
        }

        public override void Render()
        {
            var eaten = IsEaten;
            if (eaten && !_lastEaten)
                _eatSfx.PlayAt(transform.position);
            _lastEaten = eaten;

            ApplyEaten();
            ApplyColor();
        }

        private void ApplyEaten()
        {
            var eaten = IsEaten;
            _carryable.Inert = eaten;
            if (_visual != null && _visual.gameObject.activeSelf == eaten)
                _visual.gameObject.SetActive(!eaten);
        }

        private void ApplyColor()
        {
            var state = State;
            if (state == _shownState)
                return;

            _shownState = state;
            var color = state switch
            {
                CookState.Cooked => _cookedColor,
                CookState.Burnt => _burntColor,
                _ => _rawColor,
            };

            _block.SetColor(BaseColorId, color);
            foreach (var r in _renderers)
                r.SetPropertyBlock(_block);
        }
    }
}
