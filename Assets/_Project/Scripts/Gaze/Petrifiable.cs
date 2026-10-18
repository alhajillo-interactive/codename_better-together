using System.Collections.Generic;
using BetterTogether.Player;
using Fusion;
using UnityEngine;

namespace BetterTogether.Gaze
{
    /// <summary>
    /// A player that freezes while any camper is looking at it (see <see cref="CamperGaze"/>).
    /// The owner decides (after a short grace window) and publishes <see cref="IsPetrified"/>;
    /// every client tints the player like stone.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class Petrifiable : NetworkBehaviour
    {
        [Tooltip("Heights above the feet that are checked; seeing any of them counts (e.g. feet and head).")]
        [SerializeField] private float[] _checkHeights = { 0.1f, 0.4f };
        [Tooltip("Seconds a camper must keep looking before the player turns to stone.")]
        [SerializeField] private float _graceTime = 0.25f;

        [Header("Stone look")]
        [Tooltip("Root of the visual; every renderer below it is tinted.")]
        [SerializeField] private Transform _visual;
        [SerializeField] private Color _stoneColor = new(0.55f, 0.55f, 0.58f);
        [SerializeField] private float _stoneBlendTime = 0.2f;
        [SerializeField] private AudioClip _petrifySound;
        [SerializeField] private AudioClip _unpetrifySound;

        private static readonly List<Petrifiable> _all = new();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private PlayerMovement _movement;
        private Renderer[] _renderers;
        private Color[] _originalColors;
        private MaterialPropertyBlock _block;
        private AudioSource _audio;
        private float _seenTime;
        private float _stoneAmount;
        private bool _wasPetrified;
        private bool _isSpawned;

        public static Petrifiable Local { get; private set; }

        /// <summary>Every spawned petrifiable player, local and remote.</summary>
        public static IReadOnlyList<Petrifiable> All => _all;

        [Networked] public NetworkBool IsPetrified { get; private set; }

        /// <summary>Safe to read at any time, unlike <see cref="IsPetrified"/> which needs a spawned object.</summary>
        public bool IsStone => _isSpawned && IsPetrified;

        /// <summary>Only meaningful on the owner.</summary>
        public bool IsWatched { get; private set; }

        /// <summary>0..1 progress toward turning to stone while being watched; 0 when petrified or unseen. Owner only.</summary>
        public float WarningProgress { get; private set; }

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            _block = new MaterialPropertyBlock();
            _renderers = _visual != null ? _visual.GetComponentsInChildren<Renderer>() : new Renderer[0];
            _originalColors = new Color[_renderers.Length];
            for (var i = 0; i < _renderers.Length; i++)
            {
                var material = _renderers[i].sharedMaterial;
                _originalColors[i] = material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white;
            }
        }

        public override void Spawned()
        {
            _isSpawned = true;
            _all.Add(this);
            if (HasStateAuthority)
                Local = this;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            _all.Remove(this);
            if (Local == this)
                Local = null;
        }

        private void Update()
        {
            if (Local != this)
                return;

            IsWatched = IsSeenByAnyCamper();
            _seenTime = IsWatched ? _seenTime + Time.deltaTime : 0f;

            var petrified = IsWatched && _seenTime >= _graceTime;
            if (IsPetrified != petrified)
                IsPetrified = petrified;

            _movement.IsFrozen = petrified;
            WarningProgress = petrified || _graceTime <= 0f ? 0f : Mathf.Clamp01(_seenTime / _graceTime);
        }

        public override void Render()
        {
            var petrified = IsPetrified;
            if (petrified != _wasPetrified)
            {
                _wasPetrified = petrified;
                PlaySound(petrified ? _petrifySound : _unpetrifySound);
            }

            var target = petrified ? 1f : 0f;
            if (Mathf.Approximately(_stoneAmount, target))
                return;

            var step = _stoneBlendTime > 0f ? Time.deltaTime / _stoneBlendTime : 1f;
            _stoneAmount = Mathf.MoveTowards(_stoneAmount, target, step);
            ApplyStone();
        }

        private void ApplyStone()
        {
            for (var i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                    continue;

                _block.SetColor(BaseColorId, Color.Lerp(_originalColors[i], _stoneColor, _stoneAmount));
                _renderers[i].SetPropertyBlock(_block);
            }
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip == null)
                return;

            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 1f;
            }

            _audio.PlayOneShot(clip);
        }

        private bool IsSeenByAnyCamper()
        {
            var feet = transform.position;
            foreach (var gaze in CamperGaze.All)
            {
                if (gaze == null || gaze.Object == null || !gaze.Object.IsValid)
                    continue;

                foreach (var height in _checkHeights)
                {
                    if (gaze.CanSee(feet + Vector3.up * height, transform))
                        return true;
                }
            }

            return false;
        }
    }
}
