using BetterTogether.Audio;
using BetterTogether.Diagnostics;
using BetterTogether.Interaction;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Player
{
    /// <summary>
    /// Silent coordination for gnomes: pressing Ping marks the world point the gnome looks at, and only gnomes see
    /// the marker for a few seconds. The ping is a stateless presentation event (no networked state), so a late
    /// joiner simply misses past pings, a master change does not matter and a gnome that leaves just stops pinging.
    /// </summary>
    public class GnomePing : NetworkBehaviour
    {
        [Header("Sending")]
        [Tooltip("Seconds before the same gnome can ping again.")]
        [SerializeField, Min(0f)] private float _cooldown = 1f;
        [Tooltip("Maximum distance (m) of the look raycast.")]
        [SerializeField, Min(1f)] private float _range = 30f;
        [Tooltip("Layers the ping can land on: Player(8), Item(10), Environment(11), Occluder(12).")]
        [SerializeField] private LayerMask _mask = (1 << 8) | (1 << 10) | (1 << 11) | (1 << 12);

        [Header("Marker")]
        [Tooltip("Seconds a marker stays visible.")]
        [SerializeField, Min(0.1f)] private float _markerDuration = 3f;
        [Tooltip("Markers kept ready; the oldest is reused when all are busy.")]
        [SerializeField, Min(1)] private int _poolSize = 4;
        [Tooltip("TextMeshPro font size of the marker.")]
        [SerializeField] private float _fontSize = 4f;
        [Tooltip("World size of the marker per meter of distance, so it keeps a constant size on screen.")]
        [SerializeField] private float _screenScale = 0.05f;
        [SerializeField] private Color _placeColor = new(1f, 0.85f, 0.2f);
        [SerializeField] private Color _objectColor = new(0.4f, 0.9f, 1f);
        [SerializeField] private Color _camperColor = new(1f, 0.3f, 0.3f);
        [Tooltip("Played where the ping lands, for gnomes only. May stay empty.")]
        [SerializeField] private SoundCue _pingSfx;

        private const byte KindPlace = 0;
        private const byte KindObject = 1;
        private const byte KindCamper = 2;
        private const string OutlineMaterialPath = "Fonts & Materials/LiberationSans SDF - Outline";

        private TextMeshPro[] _markers;
        private float[] _expireTime;
        private byte[] _kinds;
        private int[] _shownMeters;
        private int _next;
        private InputAction _ping;
        private float _readyTime;
        private bool _isSpawned;

        public override void Spawned()
        {
            _isSpawned = true;
            if (HasStateAuthority)
                _ping = InputSystem.actions.FindAction("Player/Ping");

            BuildPool();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            _ping = null;
            if (_markers == null)
                return;

            for (var i = 0; i < _markers.Length; i++)
            {
                if (_markers[i] != null)
                    Destroy(_markers[i].gameObject);
            }

            _markers = null;
        }

        private void BuildPool()
        {
            _markers = new TextMeshPro[_poolSize];
            _expireTime = new float[_poolSize];
            _kinds = new byte[_poolSize];
            _shownMeters = new int[_poolSize];

            var outline = Resources.Load<Material>(OutlineMaterialPath);
            for (var i = 0; i < _poolSize; i++)
            {
                // Parentless on purpose: the marker stays where the ping landed, whatever this gnome does.
                var marker = new GameObject("PingMarker");
                var text = marker.AddComponent<TextMeshPro>();
                text.font = TMP_Settings.defaultFontAsset;
                if (outline != null)
                    text.fontSharedMaterial = outline;
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.fontSize = _fontSize;
                text.raycastTarget = false;
                text.enabled = false;
                _markers[i] = text;
                _expireTime[i] = float.NegativeInfinity;
            }
        }

        private void Update()
        {
            if (!_isSpawned || !HasStateAuthority || _ping == null || !_ping.WasPressedThisFrame())
                return;

            // Ignore it while the cursor is free (debug panels, typing) and during the cooldown.
            if (Cursor.lockState != CursorLockMode.Locked || FirstPersonCamera.IsTyping || Time.time < _readyTime)
                return;

            TrySendPing();
        }

        private void TrySendPing()
        {
            var cam = FirstPersonCamera.Instance;
            if (cam == null)
                return;

            var eye = cam.transform;
            if (!Physics.Raycast(eye.position, eye.forward, out var hit, _range, _mask, QueryTriggerInteraction.Ignore))
                return;

            _readyTime = Time.time + _cooldown;

            var kind = KindPlace;
            if (hit.collider.GetComponentInParent<Carryable>() != null)
            {
                kind = KindObject;
            }
            else
            {
                var player = hit.collider.GetComponentInParent<PlayerCharacter>();
                if (player != null && player.Role == PlayerRole.Camper)
                    kind = KindCamper;
            }

            if (Telemetry.Enabled) Telemetry.Begin("ping").Int("kind", kind).Float("distance", hit.distance).Send();
            RPC_Ping(hit.point, kind);
        }

        /// <summary>Stateless presentation event: every peer runs it, but only gnomes show anything.</summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Ping(Vector3 point, byte kind)
        {
            var local = PlayerCharacter.Local;
            if (!_isSpawned || _markers == null || local == null || local.Role != PlayerRole.Gnome)
                return;

            var index = _next;
            _next = (_next + 1) % _markers.Length;

            var marker = _markers[index];
            marker.transform.position = point;
            marker.color = kind == KindCamper ? _camperColor : kind == KindObject ? _objectColor : _placeColor;
            marker.enabled = true;
            _kinds[index] = kind;
            _shownMeters[index] = -1; // Forces the text to be written on the next LateUpdate.
            _expireTime[index] = Time.time + _markerDuration;

            if (_pingSfx != null)
                _pingSfx.PlayAt(point);
        }

        private void LateUpdate()
        {
            if (_markers == null)
                return;

            var cam = Camera.main;
            var camTransform = cam != null ? cam.transform : null;
            for (var i = 0; i < _markers.Length; i++)
            {
                var marker = _markers[i];
                if (!marker.enabled)
                    continue;

                if (camTransform == null || Time.time >= _expireTime[i])
                {
                    marker.enabled = false;
                    continue;
                }

                var markerTransform = marker.transform;
                var offset = markerTransform.position - camTransform.position;
                var distance = offset.magnitude;
                if (offset.sqrMagnitude > 0.0001f)
                    markerTransform.rotation = Quaternion.LookRotation(offset, Vector3.up);

                markerTransform.localScale = Vector3.one * Mathf.Max(distance * _screenScale, 0.01f);

                // The text only changes when the whole-meter distance does; SetText formats without allocating.
                var meters = Mathf.RoundToInt(distance);
                if (meters == _shownMeters[i])
                    continue;

                _shownMeters[i] = meters;
                marker.SetText(_kinds[i] == KindObject ? "⬤ {0} m" : "! {0} m", meters);
            }
        }
    }
}
