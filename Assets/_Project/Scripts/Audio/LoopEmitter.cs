using UnityEngine;

namespace BetterTogether.Audio
{
    /// <summary>
    /// Plays a looping sound from this object while it is enabled (fire, waterfall...). It plays through the SFX
    /// mixer group and fades in when it starts. Local to each player like all audio.
    /// Optionally the source follows a polyline (e.g. a river) and always sits on its point closest to the listener,
    /// so a long source is heard from the nearest bank instead of from a single point.
    /// </summary>
    public class LoopEmitter : MonoBehaviour
    {
        [SerializeField] private AudioClip _clip;
        [Range(0f, 1f)] [SerializeField] private float _volume = 1f;
        [Tooltip("0 = same volume everywhere, 1 = fully 3D.")]
        [Range(0f, 1f)] [SerializeField] private float _spatialBlend = 1f;
        [Tooltip("Distance (m) up to which the sound is at full volume.")]
        [SerializeField, Min(0f)] private float _minDistance = 1f;
        [Tooltip("Distance (m) at which the sound is no longer heard.")]
        [SerializeField, Min(0.01f)] private float _maxDistance = 15f;
        [Tooltip("Seconds to fade in when the loop starts.")]
        [SerializeField, Min(0f)] private float _fadeIn = 0.5f;

        [Header("Path (optional)")]
        [Tooltip("World-space polyline. With 2 or more points the source moves to the point of the line closest to the listener.")]
        [SerializeField] private Vector3[] _path;

        private const float ListenerSearchInterval = 1f;

        private AudioSource _source;
        private AudioListener _listener;
        private float _nextListenerSearch;
        private float _fade;

        private bool HasPath => _path != null && _path.Length >= 2;

        private void Awake()
        {
            if (!TryGetComponent(out _source))
                _source = gameObject.AddComponent<AudioSource>();

            _source.playOnAwake = false;
            _source.loop = true;
            _source.clip = _clip;
            _source.outputAudioMixerGroup = AudioManager.SfxGroup;
            _source.spatialBlend = _spatialBlend;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = _minDistance;
            _source.maxDistance = Mathf.Max(_maxDistance, _minDistance + 0.01f);
        }

        private void OnEnable()
        {
            if (_clip == null)
                return;

            _fade = _fadeIn > 0f ? 0f : 1f;
            _source.volume = _volume * _fade;
            if (HasPath)
                FollowListener();
            _source.Play();
        }

        private void OnDisable()
        {
            if (_source != null)
                _source.Stop();
        }

        private void Update()
        {
            if (_fade >= 1f || !_source.isPlaying)
                return;

            _fade = _fadeIn > 0f ? Mathf.MoveTowards(_fade, 1f, Time.unscaledDeltaTime / _fadeIn) : 1f;
            _source.volume = _volume * _fade;
        }

        private void LateUpdate()
        {
            if (HasPath)
                FollowListener();
        }

        private void FollowListener()
        {
            if (_listener == null && Time.unscaledTime >= _nextListenerSearch)
            {
                _nextListenerSearch = Time.unscaledTime + ListenerSearchInterval;
                _listener = FindAnyObjectByType<AudioListener>();
            }

            if (_listener != null)
                _source.transform.position = ClosestPointOnPath(_listener.transform.position);
        }

        private Vector3 ClosestPointOnPath(Vector3 point)
        {
            var best = _path[0];
            var bestSqr = float.MaxValue;
            for (var i = 0; i < _path.Length - 1; i++)
            {
                var a = _path[i];
                var segment = _path[i + 1] - a;
                var length = segment.sqrMagnitude;
                var t = length > 0.0001f ? Mathf.Clamp01(Vector3.Dot(point - a, segment) / length) : 0f;
                var candidate = a + segment * t;
                var sqr = (candidate - point).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            return best;
        }

        private void OnDrawGizmosSelected()
        {
            if (!HasPath)
                return;

            Gizmos.color = Color.cyan;
            for (var i = 0; i < _path.Length - 1; i++)
                Gizmos.DrawLine(_path[i], _path[i + 1]);

            foreach (var p in _path)
                Gizmos.DrawSphere(p, 0.3f);
        }
    }
}
