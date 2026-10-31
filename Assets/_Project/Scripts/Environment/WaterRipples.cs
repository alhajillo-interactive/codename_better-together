using BetterTogether.Audio;
using BetterTogether.Interaction;
using System.Collections.Generic;
using BetterTogether.Player;
using UnityEngine;

namespace BetterTogether.Environment
{
    /// <summary>
    /// Local presentation of a <see cref="WaterBody"/>: rings where players and items enter or move in the water, a bigger
    /// splash when something drops in, and a slow swell of the visible disc (+-2 cm, physics unaffected). Runs on every
    /// peer from replicated positions only, never networked. A late joiner just sees the current state; a master change or a
    /// player leaving only removes an entry from the tracking table.
    /// </summary>
    [RequireComponent(typeof(WaterBody))]
    public class WaterRipples : MonoBehaviour
    {
        [Tooltip("Ring emitter lying on the water plane (manual Emit only, world simulation space). Empty = the first ParticleSystem below this object.")]
        [SerializeField] private ParticleSystem _rings;
        [Tooltip("Seconds between two scans of players and items.")]
        [SerializeField, Min(0.05f)] private float _scanInterval = 0.15f;
        [Tooltip("Metres something must travel in the water between scans to leave a ring.")]
        [SerializeField, Min(0f)] private float _moveThreshold = 0.05f;
        [Tooltip("Downward speed (m/s) at which an entry makes the big splash.")]
        [SerializeField, Min(0f)] private float _bigSplashSpeed = 2.5f;
        [Tooltip("Ring size (m) for a small ripple.")]
        [SerializeField, Min(0.05f)] private float _ringSize = 0.6f;
        [Tooltip("Ring size (m) for the big splash.")]
        [SerializeField, Min(0.05f)] private float _splashSize = 1.6f;
        [Tooltip("Amplitude (m) of the surface swell, drawing only.")]
        [SerializeField, Min(0f)] private float _swellAmplitude = 0.02f;
        [Tooltip("Frequency (Hz) of the surface swell.")]
        [SerializeField, Min(0f)] private float _swellHz = 0.35f;
        [Tooltip("Minimum seconds between two wading sounds.")]
        [SerializeField, Min(0f)] private float _wadeSfxCooldown = 0.5f;
        [Tooltip("Height (m) of the rings above the water surface, so they do not z-fight with the disc.")]
        [SerializeField, Min(0f)] private float _ringLift = 0.04f;
        [Tooltip("Heard where something falls into the water. May be empty (silence).")]
        [SerializeField] private SoundCue _splashSfx;
        [Tooltip("Heard where something wades through the water, at most once per scan. May be empty (silence).")]
        [SerializeField] private SoundCue _wadeSfx;

        private struct Tracked
        {
            public int Id;
            public bool InWater;
            public Vector3 LastPosition;
            public int Sweep;
        }

        private static readonly List<WaterRipples> Instances = new();

        private const int MaxTracked = 64;
        private readonly Tracked[] _tracked = new Tracked[MaxTracked];
        private int _trackedCount;
        private int _sweep;
        private float _nextScan;
        private float _lastScanTime;
        private Vector3 _basePosition;
        private ParticleSystem.EmitParams _emit;
        private bool _wadeThisScan;
        private float _nextWadeSfx;
        private WaterBody _body;

        private void Awake()
        {
            _body = GetComponent<WaterBody>();
            _basePosition = transform.position;
            if (_rings == null)
                _rings = GetComponentInChildren<ParticleSystem>();
        }

        private void OnEnable() => Instances.Add(this);

        private void OnDisable() => Instances.Remove(this);

        private void LateUpdate()
        {
            // Swell: only the drawing moves, the physics keeps using WaterBody.TopY.
            transform.position = _basePosition + Vector3.up * (Mathf.Sin(Time.time * _swellHz * 2f * Mathf.PI) * _swellAmplitude);

            if (Time.time < _nextScan)
                return;

            var elapsed = Mathf.Max(0.0001f, Time.time - _lastScanTime);
            _lastScanTime = Time.time;
            _nextScan = Time.time + _scanInterval;
            _sweep++;
            _wadeThisScan = false;

            var players = PlayerCharacter.All;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player != null)
                    Visit(player.GetInstanceID(), player.transform.position, elapsed);
            }

            var items = Carryable.AllItems;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item != null)
                    Visit(item.GetInstanceID(), item.transform.position, elapsed);
            }

            // Forget what vanished (a player who left, a despawned item).
            for (var i = _trackedCount - 1; i >= 0; i--)
            {
                if (_tracked[i].Sweep != _sweep)
                    _tracked[i] = _tracked[--_trackedCount];
            }
        }

        private void Visit(int id, Vector3 position, float elapsed)
        {
            var index = -1;
            for (var i = 0; i < _trackedCount; i++)
            {
                if (_tracked[i].Id == id)
                {
                    index = i;
                    break;
                }
            }

            var inWater = WaterBody.TryGetSurface(position, out var topY) && position.y < topY + 0.05f && position.y > topY - 3f;
            if (index < 0)
            {
                if (_trackedCount >= MaxTracked)
                    return;

                // First sight (late joiner, new spawn): remember it without a splash.
                index = _trackedCount++;
                _tracked[index] = new Tracked { Id = id, InWater = inWater, LastPosition = position, Sweep = _sweep };
                return;
            }

            var previous = _tracked[index].LastPosition;
            var wasInWater = _tracked[index].InWater;
            _tracked[index].Sweep = _sweep;
            _tracked[index].LastPosition = position;
            _tracked[index].InWater = inWater;
            if (!inWater)
                return;

            var surface = new Vector3(position.x, topY, position.z);
            if (!wasInWater)
            {
                var fallSpeed = (previous.y - position.y) / elapsed;
                Emit(surface, fallSpeed >= _bigSplashSpeed ? _splashSize : _ringSize * 1.3f);
                _splashSfx.PlayAt(surface);
                return;
            }

            var dx = position.x - previous.x;
            var dz = position.z - previous.z;
            if (dx * dx + dz * dz < _moveThreshold * _moveThreshold)
                return;

            Emit(surface, _ringSize);
            if (_wadeThisScan || Time.time < _nextWadeSfx)
                return;

            _wadeThisScan = true;
            _nextWadeSfx = Time.time + _wadeSfxCooldown;
            _wadeSfx.PlayAt(surface);
        }

        /// <summary>
        /// Draws one ring on the surface of whichever pool contains the horizontal position of <paramref name="position"/>.
        /// Returns false (and draws nothing) when the point is not over water. Allocation-free.
        /// </summary>
        public static bool EmitAt(Vector3 position, float size)
        {
            for (var i = 0; i < Instances.Count; i++)
            {
                var ripples = Instances[i];
                var body = ripples._body;
                if (body == null)
                    continue;

                var dx = position.x - body.CenterXZ.x;
                var dz = position.z - body.CenterXZ.y;
                if (dx * dx + dz * dz > body.Radius * body.Radius)
                    continue;

                ripples.Emit(new Vector3(position.x, body.TopY, position.z), size);
                return true;
            }

            return false;
        }

        /// <summary>Draws one ring at a point on the water; also usable by other presentation (e.g. a falling bomb).</summary>
        public void Emit(Vector3 surfacePosition, float size)
        {
            if (_rings == null)
                return;

            _emit.position = surfacePosition + Vector3.up * _ringLift;
            _emit.startSize = size;
            _rings.Emit(_emit, 1);
        }
    }
}
