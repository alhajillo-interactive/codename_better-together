using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Diagnostics;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using PrimeTween;
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

        [Header("Feel (C2.3) - all derived from replicated state in Render")]
        [Tooltip("Looping sizzle while on a lit grill. Volume and pitch rise with cook progress.")]
        [SerializeField] private AudioClip _sizzleLoop;
        [Tooltip("Sizzle volume at the start and at the end of the cook.")]
        [SerializeField] private Vector2 _sizzleVolume = new(0.15f, 0.8f);
        [Tooltip("Sizzle pitch at the start and at the end of the cook.")]
        [SerializeField] private Vector2 _sizzlePitch = new(0.8f, 1.4f);
        [Tooltip("Distance (m) at which the sizzle is no longer heard.")]
        [SerializeField] private float _sizzleMaxDistance = 12f;
        [Tooltip("Played when the sausage becomes ready.")]
        [SerializeField] private SoundCue _readySfx;
        [Tooltip("Seconds of warning before it burns (beeping and smoke).")]
        [SerializeField] private float _alarmLead = 3f;
        [Tooltip("Beep interval (s) at the start and at the end of the alarm; it speeds up.")]
        [SerializeField] private Vector2 _beepInterval = new(0.55f, 0.12f);
        [Tooltip("Beep played during the burn alarm.")]
        [SerializeField] private SoundCue _alarmSfx;
        [Tooltip("Played when the sausage burns.")]
        [SerializeField] private SoundCue _burntSfx;
        [Tooltip("Scale multiplier reached at the top of the 'ready' hop.")]
        [SerializeField] private float _readyHopScale = 1.15f;
        [Tooltip("Total duration (s) of the 'ready' hop.")]
        [SerializeField] private float _readyHopTime = 0.2f;
        [Tooltip("Seconds per bite; the sausage shrinks in 3 steps and a bite sound plays on each.")]
        [SerializeField] private float _biteTime = 0.15f;
        [Tooltip("Sparks while cooking.")]
        [SerializeField] private ParticleSystem _sparks;
        [Tooltip("Light smoke that rises during the burn alarm.")]
        [SerializeField] private ParticleSystem _warnSmoke;
        [Tooltip("Black puff when it burns.")]
        [SerializeField] private ParticleSystem _burntPuff;
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
        private bool _burnWatched;
        private bool _wasBurnt;
        private bool _lastEaten;
        private AudioSource _sizzle;
        private FireplaceGrill _grill;
        private float _nextGrillSearch;
        private CookState _feelState = (CookState)(-1);
        private float _nextBeep;
        private Vector3 _visualScale = Vector3.one;
        private bool _biting;
        private Sequence _feelSequence;
        private System.Action _playBite;
        private System.Action _endBites;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Telemetry: last state and grill flag seen, so only changes are logged.
        private CookState _loggedState = (CookState)(-1);
        private bool _loggedOnGrill;
#endif

        [Networked] private float CookTime { get; set; }
        [Networked] private NetworkBool IsEaten { get; set; }
        [Networked] private TickTimer ResetTimer { get; set; }

        // The level can hold several sausages (Interactuables has three): every rule asks about all of them.
        private static readonly List<Sausage> _all = new();

        /// <summary>Every spawned sausage.</summary>
        public static IReadOnlyList<Sausage> All => _all;

        /// <summary>True once any sausage has been eaten (the camper's win condition).</summary>
        public static bool AnyEaten
        {
            get
            {
                foreach (var sausage in _all)
                {
                    if (sausage.Eaten)
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// The sausage the HUD talks about: an eaten one, else a cooked one, else one resting on a spot (the grill),
        /// else the first. Null when there is none. Replicated state only, no allocation.
        /// </summary>
        public static Sausage Featured
        {
            get
            {
                Sausage cooked = null, placed = null;
                foreach (var sausage in _all)
                {
                    if (sausage.Eaten)
                        return sausage;
                    if (cooked == null && sausage.State == CookState.Cooked)
                        cooked = sausage;
                    if (placed == null && sausage._carryable.IsPinned)
                        placed = sausage;
                }

                return cooked != null ? cooked : placed != null ? placed : _all.Count > 0 ? _all[0] : null;
            }
        }

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
            if (_visual != null)
                _visualScale = _visual.localScale;

            _playBite = PlayBite;
            _endBites = EndBites;
            _sizzle = gameObject.AddComponent<AudioSource>();
            _sizzle.playOnAwake = false;
            _sizzle.loop = true;
            _sizzle.clip = _sizzleLoop;
            _sizzle.spatialBlend = 1f;
            _sizzle.rolloffMode = AudioRolloffMode.Linear;
            _sizzle.minDistance = 1f;
            _sizzle.maxDistance = _sizzleMaxDistance;
        }

        public override void Spawned()
        {
            _isSpawned = true;
            if (!_all.Contains(this))
                _all.Add(this);
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _lastEaten = IsEaten; // Late joiners don't hear a bite that already happened.
            _feelState = State; // Nor a ding or a burn that already happened.
            _sizzle.outputAudioMixerGroup = AudioManager.SfxGroup;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            SyncLogged();
#endif
            ApplyEaten();
            ApplyColor();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            StopFeel();
            _all.Remove(this);
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

        public void RequestEat(PlayerInteractor interactor) => RPC_RequestEat(interactor.Id);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestEat(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanBeEatenBy(who))
                return;

            _carryable.ForceReleaseAll();
            IsEaten = true;
            ApplyEaten();
            if (Telemetry.Enabled) Telemetry.Begin("sausage_eaten", who).Float("cook_s", CookTime).Send();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
            {
                // A new master must not pay for a burn that already happened under the previous one.
                _burnWatched = false;
                return;
            }

            ApplyEaten();
            WatchBurn();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Telemetry.Enabled) TrackCooking();
#endif

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

        // Authority only: pays every gnome when the sausage turns burnt. The first tick as authority only records the state.
        private void WatchBurn()
        {
            var burnt = State == CookState.Burnt;
            if (_burnWatched && burnt && !_wasBurnt && MatchManager.Instance != null)
                MatchManager.Instance.AwardGnomes(MatchManager.ScoreReason.BurntSausage);

            _wasBurnt = burnt;
            _burnWatched = true;
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Telemetry only (authority): logs each change of cooking state or of being on the grill. cook_s is the seconds
        // cooked on a lit grill (it survives a master change); the script derives the time since it was put on the grill.
        private void TrackCooking()
        {
            var state = State;
            var onGrill = _carryable.IsPinned;
            if (state == _loggedState && onGrill == _loggedOnGrill)
                return;

            var stateChanged = state != _loggedState;
            _loggedState = state;
            _loggedOnGrill = onGrill;
            Telemetry.Begin("sausage_state").Str("state", state == CookState.Burnt ? "burnt" : state == CookState.Cooked ? "cooked" : "raw")
                .Bool("on_grill", onGrill).Float("cook_s", CookTime).Send();
            if (stateChanged && state == CookState.Burnt)
                Telemetry.Begin("sausage_burnt").Float("cook_s", CookTime).Send();
        }

        private void SyncLogged()
        {
            _loggedState = State;
            _loggedOnGrill = _carryable.IsPinned;
        }
#endif

        public override void Render()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Non-authority peers follow the state silently, so a new master does not log a change that already happened.
            if (!HasStateAuthority)
                SyncLogged();
#endif

            var eaten = IsEaten;
            if (eaten && !_lastEaten)
                StartBites();
            else if (!eaten && _lastEaten)
                StopFeel();
            _lastEaten = eaten;

            ApplyEaten();
            ApplyColor();
            RenderFeel(eaten);
        }

        // Presentation only: everything below is derived from the replicated cook state, so late joiners and a new master
        // simply follow it without replaying past events.
        private void RenderFeel(bool eaten)
        {
            var state = State;
            var cooking = !eaten && IsCooking();
            var progress = _burntAt > 0f ? Mathf.Clamp01(CookTime / _burntAt) : 0f;

            if (state != _feelState)
            {
                if (_feelState == CookState.Raw && state == CookState.Cooked)
                    PlayReady();
                else if (state == CookState.Burnt && _feelState != CookState.Burnt)
                    PlayBurnt();

                _feelState = state;
            }

            UpdateSizzle(cooking && state != CookState.Burnt, progress);
            SetEmitting(_sparks, cooking && state != CookState.Burnt);

            var alarm = cooking && state == CookState.Cooked && CookTime >= _burntAt - _alarmLead;
            SetEmitting(_warnSmoke, alarm);
            if (!alarm || Time.time < _nextBeep)
                return;

            var t = Mathf.Clamp01((CookTime - (_burntAt - _alarmLead)) / Mathf.Max(0.01f, _alarmLead));
            _nextBeep = Time.time + Mathf.Lerp(_beepInterval.x, _beepInterval.y, t);
            _alarmSfx.PlayAt(transform.position);
        }

        // True while it rests on the grill over a lit fire. The grill is looked up once (retried every second until found).
        private bool IsCooking()
        {
            if (_grill == null)
            {
                if (Time.time < _nextGrillSearch)
                    return false;

                _nextGrillSearch = Time.time + 1f;
                _grill = FindAnyObjectByType<FireplaceGrill>();
                if (_grill == null)
                    return false;
            }

            var fire = FireplaceManager.Instance;
            return fire != null && fire.IsLit && _carryable.IsPinnedTo(_grill);
        }

        private void UpdateSizzle(bool cooking, float progress)
        {
            if (_sizzle.clip == null)
                return;

            if (!cooking)
            {
                if (_sizzle.isPlaying)
                    _sizzle.Stop();
                return;
            }

            _sizzle.volume = Mathf.Lerp(_sizzleVolume.x, _sizzleVolume.y, progress);
            _sizzle.pitch = Mathf.Lerp(_sizzlePitch.x, _sizzlePitch.y, progress);
            if (!_sizzle.isPlaying)
                _sizzle.Play();
        }

        private static void SetEmitting(ParticleSystem particles, bool on)
        {
            if (particles == null || particles.isEmitting == on)
                return;

            if (on)
                particles.Play();
            else
                particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        private void PlayReady()
        {
            _readySfx.PlayAt(transform.position);
            if (_visual == null || _biting)
                return;

            _feelSequence.Stop();
            _visual.localScale = _visualScale;
            _feelSequence = Sequence.Create()
                .Chain(Tween.Scale(_visual, _visualScale * _readyHopScale, _readyHopTime * 0.5f, Ease.OutQuad))
                .Chain(Tween.Scale(_visual, _visualScale, _readyHopTime * 0.5f, Ease.InQuad));
        }

        private void PlayBurnt()
        {
            _burntSfx.PlayAt(transform.position);
            if (_burntPuff != null)
                _burntPuff.Play();
        }

        // Three bites: the sausage shrinks in 3 steps with a bite sound on each, then the visual hides.
        private void StartBites()
        {
            if (_visual == null)
            {
                _eatSfx.PlayAt(transform.position);
                return;
            }

            _feelSequence.Stop();
            _biting = true;
            _visual.localScale = _visualScale;
            _feelSequence = Sequence.Create();
            for (var i = 1; i <= 3; i++)
            {
                _feelSequence = _feelSequence.ChainCallback(_playBite);
                _feelSequence = _feelSequence.Chain(Tween.Scale(_visual, _visualScale * (1f - i / 3f), _biteTime, Ease.InQuad));
            }

            _feelSequence = _feelSequence.ChainCallback(_endBites);
        }

        private void PlayBite() => _eatSfx.PlayAt(transform.position);

        private void EndBites() => _biting = false;

        // Stops any running tween and restores the visual (new match, despawn).
        private void StopFeel()
        {
            _feelSequence.Stop();
            _biting = false;
            if (_visual != null)
                _visual.localScale = _visualScale;
            if (_sizzle != null && _sizzle.isPlaying)
                _sizzle.Stop();
            SetEmitting(_sparks, false);
            SetEmitting(_warnSmoke, false);
        }

        private void ApplyEaten()
        {
            var eaten = IsEaten;
            _carryable.Inert = eaten;
            var show = !eaten || _biting; // Stays visible while it is being bitten.
            if (_visual != null && _visual.gameObject.activeSelf != show)
                _visual.gameObject.SetActive(show);
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
