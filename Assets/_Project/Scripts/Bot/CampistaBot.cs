using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Environment;
using BetterTogether.Fireplace;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

namespace BetterTogether.Bot
{
    /// <summary>
    /// The camper played by the game. Master-client object: <see cref="BotPerception"/> senses, <see cref="BotBrain"/> picks a goal and
    /// this class carries it out in FixedUpdateNetwork, then replicates only goal, suspicion and a few events for presentation.
    /// Master change: the new master rebuilds its senses from nothing and takes the goal again; held items are released by themselves.
    /// Late joiner: sees the replicated pose, goal and icon. A player who leaves is just a forgotten contact.
    /// </summary>
    public class CampistaBot : NetworkBehaviour, IMatchResettable, IStateAuthorityChanged
    {
        private const float DecideSeconds = 0.2f;
        private const float RepathSeconds = 0.4f;
        private const float Gravity = -20f;

        public static CampistaBot Instance { get; private set; }

        [Header("Parts")]
        [SerializeField] private CharacterController _controller;
        [Tooltip("Where the bot looks from.")]
        [SerializeField] private Transform _eye;
        [Tooltip("Where a held item rides.")]
        [SerializeField] private Transform _hand;
        [SerializeField] private GnomeCarrier _carrier;
        [SerializeField] private Animator _animator;
        [Tooltip("Text floating over the head: ?, ! and short comments.")]
        [SerializeField] private TMP_Text _bark;
        [Tooltip("Flash of the camera.")]
        [SerializeField] private Light _flash;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float _walkSpeed = 3f;
        [SerializeField, Min(0f)] private float _runSpeed = 4.5f;
        [SerializeField, Min(0f)] private float _carrySpeed = 2.8f;
        [SerializeField, Min(0f)] private float _turnSpeed = 420f;
        [Tooltip("Metres at which the bot can use what it walks to.")]
        [SerializeField, Min(0.1f)] private float _reach = 1.6f;
        [Tooltip("Seconds a step of a goal may take before the bot gives up on it.")]
        [SerializeField, Min(1f)] private float _stepTimeout = 12f;

        [Tooltip("Seconds it takes to pick up or use something.")]
        [SerializeField, Min(0f)] private float _useSeconds = 1.5f;

        [Header("Camera")]
        [Tooltip("Metres of the bot's camera.")]
        [SerializeField, Min(0f)] private float _flashRange = 5.5f;
        [Tooltip("Seconds the camera is raised before the flash: the warning for the gnomes.")]
        [SerializeField, Min(0f)] private float _flashWindup = 0.8f;
        [Tooltip("Seconds to recharge a shot.")]
        [SerializeField, Min(0f)] private float _flashCooldown = 10f;

        [Tooltip("Metres to look for walkable ground around the bot and its destination.")]
        [SerializeField, Min(0.1f)] private float _meshSearchRadius = 3f;

        [Header("Pacing")]
        [Tooltip("Seconds a single chase can last before the bot gives up.")]
        [SerializeField, Min(1f)] private float _chaseGiveUpSeconds = 18f;
        [Tooltip("Seconds of calm after caging a gnome, when it prefers to cook.")]
        [SerializeField, Min(0f)] private float _recoverySeconds = 8f;
        [Tooltip("Seconds it will not chase after losing someone.")]
        [SerializeField, Min(0f)] private float _chaseCooldown = 4f;
        [Tooltip("Seconds a noise stays interesting.")]
        [SerializeField, Min(0f)] private float _noiseMemory = 6f;

        [Header("Brain")]
        [SerializeField] private BotPerception.Settings _perception = new();
        [SerializeField] private BotBrain.Settings _brainSettings = new();
        [Tooltip("What blocks the bot's sight.")]
        [SerializeField] private LayerMask _sightMask;

        [Header("Sounds")]
        [SerializeField] private SoundCue _shutterSfx;
        [SerializeField] private SoundCue _noticeSfx;

        [Networked] private BotGoal NetGoal { get; set; }
        [Networked] private SuspicionLevel NetSuspicion { get; set; }
        [Networked] private byte NetFlashSeq { get; set; }
        [Networked] private float NetMovement { get; set; }
        [Networked] private NetworkBool NetAirborne { get; set; }

        private readonly List<Vector3> _ornaments = new(64);
        private readonly List<Carryable> _wood = new(8);
        private readonly Vector3[] _corners = new Vector3[32];

        private NavMeshPath _path;
        private NetworkTransform _network;
        private BotPerception _senses;
        private BotBrain _brain;
        private FireplaceManager _fire;
        private FireplaceGrill _grill;
        private GnomeCage _cage;
        private Carryable _held;
        private GnomeCapture _ignoredGnome;
        private BotPerception.Contact _target;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Vector3 _destination;
        private Vector3 _investigatePoint;
        private Vector3 _knockback;
        private bool _hasInvestigatePoint;
        private bool _isSpawned;
        private bool _movingNow;
        private int _cornerCount;
        private int _cornerIndex;
        private float _verticalSpeed;
        private float _nextDecide;
        private float _nextPath;
        private float _stepStarted;
        private float _flashReady;
        private float _raiseStarted = -1f;
        private float _stunUntil;
        private float _recoveryUntil;
        private float _chaseBlockedUntil;
        private BotGoal _lastGoal;
        private byte _shownFlash;
        private BotGoal _shownGoal;
        private SuspicionLevel _shownSuspicion;
        private float _flashFade;
        private bool _feedbackReady;

        /// <summary>Master only: set by bombs, so the bot's stumble stays with its owner.</summary>
        public bool IsMaster => _isSpawned && HasStateAuthority;

        public override void Spawned()
        {
            _isSpawned = true;
            Instance = this;
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _path = new NavMeshPath();
            _network = GetComponent<NetworkTransform>();
            _placeWood = TryPlaceWood;
            _light = TryLight;
            _grillSausage = TryGrill;
            _fire = FindAnyObjectByType<FireplaceManager>();
            _grill = _fire != null ? _fire.GetComponentInChildren<FireplaceGrill>() : null;
            _cage = FindAnyObjectByType<GnomeCage>();
            foreach (var wood in FindObjectsByType<FireplaceWood>(FindObjectsSortMode.None))
                _wood.Add(wood.GetComponent<Carryable>());

            RebuildBrain();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            if (Instance == this)
                Instance = null;
        }

        public void StateAuthorityChanged()
        {
            if (HasStateAuthority)
                RebuildBrain();
        }

        private void RebuildBrain()
        {
            _senses = new BotPerception(_perception, transform, _eye, _sightMask);
            _brain = new BotBrain(_brainSettings);
            _ornaments.Clear();
            foreach (var decoy in FindObjectsByType<GardenGnomeDecoy>(FindObjectsSortMode.None))
                _ornaments.Add(decoy.transform.position);

            _senses.Remember(_ornaments);
            _held = null;
            _target = null;
        }

        /// <summary>Authority only: back to the start with empty hands.</summary>
        public void ResetForMatch()
        {
            _held = null;
            _brain.Reset();
            _senses.Remember(_ornaments);
            _target = null;
            _hasInvestigatePoint = false;
            _stunUntil = 0f;
            _recoveryUntil = 0f;
            _chaseBlockedUntil = 0f;
            _knockback = Vector3.zero;
            _controller.enabled = false;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            if (_network != null)
                _network.Teleport(_spawnPosition, _spawnRotation);
            _controller.enabled = true;
        }

        /// <summary>Master only: an explosive statue throws the bot in the air, and everything it held goes flying.</summary>
        public void Blast(Vector3 velocity, float stunSeconds)
        {
            if (!IsMaster)
                return;

            DropHeld();
            _knockback = new Vector3(velocity.x, 0f, velocity.z);
            _verticalSpeed = velocity.y;
            _stunUntil = Time.time + stunSeconds;
            _raiseStarted = -1f;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            var match = MatchManager.Instance;
            UpdateCarriedCollision();
            if (match == null || match.Phase != MatchPhase.Playing)
            {
                Stop();
                Move(Runner.DeltaTime);
                NetSuspicion = SuspicionLevel.Unaware;
                return;
            }

            var statues = GnomeStatues.All;
            for (var i = 0; i < statues.Count; i++)
                statues[i].CheckBot(this);

            var dt = Runner.DeltaTime;
            if (Time.time >= _nextDecide)
            {
                _nextDecide = Time.time + DecideSeconds;
                _senses.Tick(DecideSeconds);
                _brain.Decide(Situation(match), Time.time, DecideSeconds);
                if (_brain.Goal != _lastGoal)
                {
                    _lastGoal = _brain.Goal;
                    _stepStarted = Time.time;
                    _raiseStarted = -1f;
                    _performUntil = -1f;
                }
            }

            Act();
            Move(dt);
            Publish();
        }

        private BotSituation Situation(MatchManager match)
        {
            var sausage = Sausage.All.Count > 0 ? Sausage.All[0] : null;
            var cooked = sausage != null && sausage.State == Sausage.CookState.Cooked && !sausage.Eaten;
            var confirmed = _senses.Best(SuspicionLevel.Confirmed);
            var needed = TravesuraLayout.Current != null ? TravesuraLayout.Current.Needed : 1;
            return new BotSituation
            {
                SausageReady = cooked,
                CarryingGnome = _carrier.IsCarrying,
                Stunned = Time.time < _stunUntil,
                ChaseCertainty = confirmed != null ? 1f : _target != null && _target.Level == SuspicionLevel.Searching ? 0.4f : 0f,
                CageFree = _cage != null && _cage.Occupant == null,
                InvestigateInterest = InvestigateInterest(),
                ListProgress = (float)match.TravesurasDone / needed,
                CookProgress = CookProgress(sausage),
                InRecovery = Time.time < _recoveryUntil,
                ChaseOnCooldown = Time.time < _chaseBlockedUntil,
            };
        }

        private float CookProgress(Sausage sausage)
        {
            if (_fire == null)
                return 0f;

            var wood = _fire.RequiredWood > 0 ? (float)_fire.WoodCount / _fire.RequiredWood : 1f;
            var progress = wood * 0.5f + (_fire.IsLit ? 0.2f : 0f);
            return sausage != null && sausage.Carryable.IsPinned ? progress + 0.3f : progress;
        }

        // Curiosity: a gnome half seen, a loud noise nearby or an ornament that was not there this morning.
        private float InvestigateInterest()
        {
            var suspect = _senses.Best(SuspicionLevel.Curious);
            if (suspect != null)
            {
                SetInvestigatePoint(suspect.LastSeen);
                return suspect.Level == SuspicionLevel.Suspicious ? 0.9f : 0.75f;
            }

            if (_senses.TryFindNewStatue(out var statue))
            {
                SetInvestigatePoint(statue);
                return 0.7f;
            }

            var heard = Time.time - BotEars.Time < _noiseMemory
                        && (BotEars.Position - transform.position).sqrMagnitude <= BotEars.Radius * BotEars.Radius;
            if (heard)
            {
                SetInvestigatePoint(BotEars.Position);
                return 0.8f;
            }

            if (_hasInvestigatePoint && !_movingNow)
                _hasInvestigatePoint = false;

            return _hasInvestigatePoint ? 0.5f : 0f;
        }

        private void SetInvestigatePoint(Vector3 point)
        {
            if (!_hasInvestigatePoint || (point - _investigatePoint).sqrMagnitude > 4f)
                _stepStarted = Time.time;

            _investigatePoint = point;
            _hasInvestigatePoint = true;
        }

        private void Act()
        {
            switch (_brain.Goal)
            {
                case BotGoal.Cook: DoCook(); break;
                case BotGoal.Eat: DoEat(); break;
                case BotGoal.Investigate: DoInvestigate(); break;
                case BotGoal.Chase: DoChase(); break;
                case BotGoal.Carry: DoCarry(); break;
                default: Stop(); break;
            }
        }

        // ---- Cooking: an ordered recipe, looked up fresh so a prank just sends the bot back a step ----

        private void DoCook()
        {
            if (_fire == null)
                return;

            if (ClearBurntSausage())
                return;

            // A prank can change the step while something is in hand: what does not fit is put down.
            if (!_fire.HasEnoughWood)
            {
                KeepOnly<FireplaceWood>();
                Fetch(NearestWood(), _placeWood);
            }
            else if (!_fire.IsLit)
            {
                KeepOnly<Matches>();
                Fetch(FindMatches(), _light);
            }
            else
            {
                KeepOnly<Sausage>();
                Fetch(NextSausage(), _grillSausage);
            }
        }

        private void KeepOnly<T>()
        {
            if (_held != null && !_held.TryGetComponent<T>(out _))
                DropHeld();
        }

        // A burnt sausage is useless: the bot throws it off the grill and the game gives a new one.
        private bool ClearBurntSausage()
        {
            if (Sausage.All.Count == 0 || _grill == null)
                return false;

            var sausage = Sausage.All[0];
            if (sausage.State != Sausage.CookState.Burnt || !sausage.Carryable.IsPinnedTo(_grill))
                return false;

            DropHeld();
            GoTo(sausage.transform.position, _walkSpeed);
            if (Near(sausage.transform.position, _reach) && Perform())
                sausage.Carryable.BotRelease(transform.position + transform.forward * 0.8f, Quaternion.identity);

            return true;
        }

        private delegate bool UseHeld(Carryable item);

        private UseHeld _placeWood;
        private UseHeld _light;
        private UseHeld _grillSausage;
        private Carryable _fetchTarget;
        private Carryable _skipped;
        private float _fetchSince;
        private float _skipUntil;

        // Every use takes a moment, standing still: the time the gnomes have to act.
        private bool Perform()
        {
            Stop();
            if (_performUntil < 0f)
                _performUntil = Time.time + _useSeconds;

            if (Time.time < _performUntil)
                return false;

            _performUntil = -1f;
            return true;
        }

        private void Fetch(Carryable wanted, UseHeld use)
        {
            if (_held != null)
            {
                GoTo(_fire.transform.position, _walkSpeed);
                if (Near(_fire.transform.position, _reach * 1.6f) && Perform() && use(_held))
                    ReleaseHeld();

                return;
            }

            if (wanted == null)
            {
                Stop();
                return;
            }

            if (wanted != _fetchTarget)
            {
                _fetchTarget = wanted;
                _fetchSince = Time.time;
            }

            // Out of reach, perhaps on a roof: it tries something else for a while.
            if (Time.time - _fetchSince > _stepTimeout)
            {
                _skipped = wanted;
                _skipUntil = Time.time + _stepTimeout;
                _fetchTarget = null;
                Stop();
                return;
            }

            GoTo(wanted.transform.position, _walkSpeed);
            if (Near(wanted.transform.position, _reach) && Perform())
            {
                wanted.BotHold(_hand);
                _held = wanted;
            }
        }

        private bool TryPlaceWood(Carryable item) => _fire.BotPlaceLog(item);

        private bool TryLight(Carryable item)
        {
            _fire.BotLight();
            item.BotRelease(transform.position + transform.forward * 0.6f, Quaternion.identity);
            _held = null;
            return false;
        }

        private bool TryGrill(Carryable item) => _grill != null && _grill.BotPlace(item);

        private void ReleaseHeld() => _held = null;

        private void DropHeld()
        {
            if (_held != null)
                _held.BotRelease(transform.position + transform.forward * 0.5f, Quaternion.identity);

            _held = null;
        }

        private Carryable NearestWood()
        {
            Carryable best = null;
            var bestDistance = float.MaxValue;
            foreach (var item in _wood)
            {
                if (item == null || item.IsPinned || item.IsHeld || item.Inert || (item == _skipped && Time.time < _skipUntil))
                    continue;

                var distance = (item.transform.position - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = item;
                }
            }

            return best;
        }

        private static Carryable FindMatches() => Matches.All.Count > 0 ? Matches.All[0].GetComponent<Carryable>() : null;

        private static Carryable NextSausage()
        {
            if (Sausage.All.Count == 0)
                return null;

            var sausage = Sausage.All[0];
            return sausage.Eaten || sausage.State != Sausage.CookState.Raw || sausage.Carryable.IsPinned ? null : sausage.Carryable;
        }

        private void DoEat()
        {
            if (Sausage.All.Count == 0)
                return;

            var sausage = Sausage.All[0];
            GoTo(sausage.transform.position, _runSpeed);
            if (Near(sausage.transform.position, _reach))
                sausage.BotEat();
        }

        // ---- Curiosity ----

        private void DoInvestigate()
        {
            if (!_hasInvestigatePoint)
            {
                Stop();
                return;
            }

            GoTo(_investigatePoint, _walkSpeed);
            if (Near(_investigatePoint, _reach * 1.3f) || Time.time - _stepStarted > _stepTimeout)
            {
                _senses.MarkChecked(_investigatePoint);
                _hasInvestigatePoint = false;
                GardenGnomeDecoy.NotifyKick(transform.position, transform.forward, _reach * 1.5f, 60f);
            }
        }

        // ---- Hunting ----

        private void DoChase()
        {
            var seen = _senses.Best(SuspicionLevel.Confirmed);
            if (seen != null)
                _target = seen;

            if (_target == null || _target.Gnome == null || !_target.Gnome.IsAvailable || _target.Gnome.IsLockedInCage)
            {
                LoseTarget();
                return;
            }

            DropHeld();
            var gnome = _target.Gnome;
            var position = _target.Visible ? gnome.transform.position : _target.LastSeen;
            GoTo(position, _runSpeed);

            // A chase that goes nowhere ends: the dinner matters more.
            if (Time.time - _stepStarted > _chaseGiveUpSeconds)
            {
                LoseTarget();
                return;
            }

            if (gnome.IsStone)
            {
                _raiseStarted = -1f;
                if (Near(gnome.transform.position, _reach) && gnome.TryGetComponent<GnomeCapture>(out var capture))
                    _carrier.BotPickUp(capture);

                return;
            }

            if (!_target.Visible)
            {
                _raiseStarted = -1f;
                if (Near(_target.LastSeen, _reach) || Time.time - _stepStarted > _stepTimeout)
                    LoseTarget();

                return;
            }

            AimCamera(gnome);
        }

        private void LoseTarget()
        {
            _target = null;
            _raiseStarted = -1f;
            _chaseBlockedUntil = Time.time + _chaseCooldown;
        }

        // The camera is raised first: the gnome hears and sees it coming and can break the line of sight.
        private void AimCamera(Petrifiable gnome)
        {
            var inRange = Near(gnome.transform.position, _flashRange) && Time.time >= _flashReady && !gnome.IsGazeImmune;
            if (!inRange)
            {
                _raiseStarted = -1f;
                return;
            }

            if (_raiseStarted < 0f)
                _raiseStarted = Time.time;

            if (Time.time - _raiseStarted < _flashWindup)
                return;

            _raiseStarted = -1f;
            _flashReady = Time.time + _flashCooldown;

            // Decided at the shot, not at the aim: stepping behind cover in time saves the gnome, and the plate is spent all the same.
            if (!_senses.CanSee(gnome.transform.position + Vector3.up * 0.3f))
            {
                NetFlashSeq = (byte)(NetFlashSeq + 1);
                return;
            }

            NetFlashSeq = (byte)(NetFlashSeq + 1);
            gnome.RPC_BotFreeze();
        }

        private void DoCarry()
        {
            if (_cage == null)
                return;

            GoTo(_cage.transform.position, _carrySpeed);
            if (Near(_cage.transform.position, _reach * 2f) && _cage.BotCage(_carrier))
            {
                _recoveryUntil = Time.time + _recoverySeconds;
                _target = null;
            }
        }

        // The gnome rides in front of the bot: its body must not push the bot back.
        private void UpdateCarriedCollision()
        {
            var carried = _carrier.Carried;
            if (carried == _ignoredGnome)
                return;

            SetIgnored(_ignoredGnome, false);
            SetIgnored(carried, true);
            _ignoredGnome = carried;
        }

        private void SetIgnored(GnomeCapture gnome, bool ignore)
        {
            if (gnome != null && gnome.TryGetComponent<CharacterController>(out var body))
                Physics.IgnoreCollision(_controller, body, ignore);
        }

        // ---- Movement: NavMesh only gives the route; the body walks like any player ----

        private void GoTo(Vector3 destination, float speed)
        {
            _destination = destination;
            _movingNow = !Near(destination, 0.5f);
            _currentSpeed = speed;
            if (Time.time < _nextPath)
                return;

            _nextPath = Time.time + RepathSeconds;
            // Next to furniture the body stands outside the walkable mesh: the route starts at the nearest walkable point.
            var routed = NavMesh.SamplePosition(transform.position, out var start, _meshSearchRadius, NavMesh.AllAreas)
                         && NavMesh.SamplePosition(destination, out var end, _meshSearchRadius, NavMesh.AllAreas)
                         && NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, _path);
            if (routed)
            {
                _cornerCount = _path.GetCornersNonAlloc(_corners);
                _cornerIndex = 0;
                if (_cornerCount > 0)
                    return;
            }

            // No route at all: it walks straight at the target rather than freeze.
            _corners[0] = destination;
            _cornerCount = 1;
            _cornerIndex = 0;
        }

        private float _currentSpeed;
        private float _performUntil = -1f;

        private void Stop()
        {
            _movingNow = false;
            _cornerCount = 0;
        }

        private bool Near(Vector3 point, float distance)
        {
            var offset = point - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= distance * distance;
        }

        private void Move(float dt)
        {
            var velocity = Vector3.zero;
            if (Time.time >= _stunUntil && _movingNow && _cornerCount > 0)
            {
                while (_cornerIndex < _cornerCount - 1 && Near(_corners[_cornerIndex], 0.3f))
                    _cornerIndex++;

                var toCorner = _corners[_cornerIndex] - transform.position;
                toCorner.y = 0f;
                if (toCorner.sqrMagnitude > 0.01f)
                {
                    var direction = toCorner.normalized;
                    velocity = direction * _currentSpeed;
                    var look = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeed * dt);
                }
            }

            _verticalSpeed = _controller.isGrounded && _verticalSpeed < 0f ? -1f : _verticalSpeed + Gravity * dt;
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, 6f * dt);
            _controller.Move((velocity + _knockback + Vector3.up * _verticalSpeed) * dt);
            // The camper's tree has walk at 0.5 and sprint at 1. Quantized, so a constant speed sends nothing.
            var speed = velocity.magnitude;
            var movement = speed <= _walkSpeed ? 0.5f * speed / _walkSpeed : Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(_walkSpeed, _runSpeed, speed));
            NetMovement = Mathf.Round(movement * 20f) / 20f;
            NetAirborne = !_controller.isGrounded && _verticalSpeed > 0f || Time.time < _stunUntil;
        }

        // ---- Presentation: replicated state, shown by everyone ----

        private void Publish()
        {
            NetGoal = _brain.Goal;
            var strongest = _senses.Best(SuspicionLevel.Curious);
            NetSuspicion = strongest != null ? strongest.Level : SuspicionLevel.Unaware;
        }

        public override void Render()
        {
            if (!_isSpawned)
                return;

            if (_animator != null)
            {
                _animator.SetFloat(MovementParameter, NetMovement, 0.1f, Time.deltaTime);
                _animator.SetBool(JumpParameter, NetAirborne);
            }

            if (_feedbackReady)
            {
                if (NetFlashSeq != _shownFlash)
                {
                    _shutterSfx.PlayAt(transform.position);
                    _flashFade = 1f;
                }

                if (NetSuspicion != _shownSuspicion && NetSuspicion >= SuspicionLevel.Suspicious)
                    _noticeSfx.PlayAt(transform.position);

                if (NetSuspicion != _shownSuspicion || NetGoal != _shownGoal)
                    ShowBark();
            }

            _feedbackReady = true;
            _shownFlash = NetFlashSeq;
            _shownGoal = NetGoal;
            _shownSuspicion = NetSuspicion;
            if (_flash != null)
            {
                _flashFade = Mathf.MoveTowards(_flashFade, 0f, Time.deltaTime * 6f);
                _flash.intensity = _flashFade * 8f;
            }

            if (_bark != null)
            {
                var camera = Camera.main;
                if (camera != null)
                    _bark.transform.rotation = camera.transform.rotation;
            }
        }

        private void ShowBark()
        {
            if (_bark == null)
                return;

            _bark.text = NetSuspicion switch
            {
                SuspicionLevel.Curious => "?",
                SuspicionLevel.Suspicious => "¿Quién anda ahí?",
                SuspicionLevel.Confirmed => "¡Te vi, gnomo!",
                SuspicionLevel.Searching => "¿Dónde te metiste?",
                _ => NetGoal switch
                {
                    BotGoal.Carry => "¡A la jaula!",
                    BotGoal.Eat => "¡Por fin, mi cena!",
                    BotGoal.Recover => "¡Ay!",
                    _ => "",
                },
            };
        }

        private static readonly int MovementParameter = Animator.StringToHash("Movement");
        private static readonly int JumpParameter = Animator.StringToHash("IsJump");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool _showDebug;

        private void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.f6Key.wasPressedThisFrame)
                _showDebug = !_showDebug;
        }

        private void OnGUI()
        {
            if (!_showDebug || !_isSpawned || !HasStateAuthority || _brain == null)
                return;

            var text = $"Bot: {_brain.Goal}  presión {_brain.Pressure:0.00}\n";
            foreach (BotGoal goal in System.Enum.GetValues(typeof(BotGoal)))
                text += $"{goal}: {_brain.ScoreOf(goal):0.00}\n";
            foreach (var contact in _senses.Contacts)
                text += $"{contact.Gnome.name}: {contact.Level} {contact.Suspicion:0.00}\n";

            GUI.Label(new Rect(10f, 200f, 320f, 260f), text);
        }
#endif
    }
}
