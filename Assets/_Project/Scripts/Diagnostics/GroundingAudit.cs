// In-game grounding audit (C1.9). Press F4 to list loose items that float or sink, pinned items off their spot,
// visuals that do not match their colliders and characters whose feet float or sink. It only reports: it never moves,
// networks or changes anything. Compiles to nothing in release builds.
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && FUSION_WEAVER
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BetterTogether.Interaction;
using BetterTogether.Networking;
using BetterTogether.Player;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Diagnostics
{
    /// <summary>
    /// Read-only scan for grounding problems, run every half second while F4 is on. Created automatically in the Editor
    /// and development builds, like <see cref="PerfProbe"/>. Findings are listed on screen (bottom left) and each one is
    /// logged once when it first appears, with the object as context. Loose-item floating is only checked on the state
    /// authority, because <see cref="Carryable.FreeTime"/> is only known there.
    /// </summary>
    public class GroundingAudit : MonoBehaviour
    {
        private const float AuditInterval = 0.5f;

        // A loose item must have been free this long before it counts as resting.
        private const float RestingSeconds = 1f;

        // Probes start this far above the bottom (or the top) of the bounds, so they do not begin inside a collider.
        private const float ProbeLift = 0.05f;

        // Ground farther than this below the item is not considered.
        private const float ProbeDistance = 100f;

        private const int MaxLines = 12;
        private const int HitCapacity = 16;
        private const int NetworkObjectCapacity = 256;
        private const int ComponentCapacity = 32;

        // Carryable keeps its pin owner private. Read it once here, so no public API has to change for this tool.
        private static readonly PropertyInfo PinOwnerProperty =
            typeof(Carryable).GetProperty("PinOwnerId", BindingFlags.Instance | BindingFlags.NonPublic);

        [Tooltip("Loose items floating or sinking by more than this (m) are reported.")]
        [SerializeField] private float _floatTolerance = 0.02f;

        [Tooltip("Lowest point of the visuals vs the lowest point of the colliders: more than this apart (m) is reported.")]
        [SerializeField] private float _visualTolerance = 0.02f;

        [Tooltip("Pinned items farther than this (m) from their pin point are reported.")]
        [SerializeField] private float _pinTolerance = 0.01f;

        [Tooltip("Characters on the ground whose lowest visual is more than this (m) away from their root are reported.")]
        [SerializeField] private float _feetTolerance = 0.03f;

        [Tooltip("Layers that count as ground for the item probes. Triggers are always ignored.")]
        [SerializeField] private LayerMask _groundMask = Physics.DefaultRaycastLayers;

        private enum Finding : byte { Floats, Sinks, Visual, Pin, Feet }

        /// <summary>The single audit alive in this session.</summary>
        public static GroundingAudit Instance { get; private set; }

        // Preallocated so auditing does not allocate lists or arrays on every run.
        private readonly List<NetworkObject> _networkObjects = new List<NetworkObject>(NetworkObjectCapacity);
        private readonly List<Collider> _colliders = new List<Collider>(ComponentCapacity);
        private readonly List<Renderer> _renderers = new List<Renderer>(ComponentCapacity);
        private readonly RaycastHit[] _hits = new RaycastHit[HitCapacity];
        private readonly List<string> _lines = new List<string>(MaxLines);
        private readonly StringBuilder _overlay = new StringBuilder(512);
        private readonly GUIContent _overlayText = new GUIContent();

        // Keys of the findings of the last run and of this run. Only keys that were not in the last run are logged.
        private HashSet<long> _previous = new HashSet<long>();
        private HashSet<long> _current = new HashSet<long>();

        private GUIStyle _overlayStyle;
        private bool _active;
        private float _timer;
        private int _findingCount;

        // Created at startup so every scene gets one; nothing has to be added by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateAtStartup()
        {
            if (Instance == null)
                new GameObject(nameof(GroundingAudit)).AddComponent<GroundingAudit>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // The overlay uses absolute Rects, so skip the GUILayout pass.
            useGUILayout = false;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // Only the key check runs while the audit is off: no allocations and no scanning.
            if (Keyboard.current != null && !FirstPersonCamera.IsTyping && Keyboard.current.f4Key.wasPressedThisFrame)
            {
                _active = !_active;

                // Audit right away when turned on, instead of after the first interval.
                _timer = AuditInterval;
                if (!_active)
                    ClearOverlay();
            }

            if (!_active)
                return;

            _timer += Time.unscaledDeltaTime;
            if (_timer < AuditInterval)
                return;

            _timer = 0f;
            Audit();
        }

        private void OnGUI()
        {
            if (!_active)
                return;

            _overlayStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12,
                padding = new RectOffset(8, 8, 6, 6),
            };

            // Bottom left, so it does not cover the bottom right overlay of the performance probe (F3).
            const float width = 460f;
            const float height = 260f;
            GUI.Label(new Rect(10f, Screen.height - height - 10f, width, height), _overlayText, _overlayStyle);
        }

        /// <summary>One full pass over the loose items, the pinned items and the characters.</summary>
        private void Audit()
        {
            _current.Clear();
            _lines.Clear();
            _findingCount = 0;

            // Items come from the network runner. The list only holds spawned objects, and the checks ask the Carryable
            // for its state, so nothing networked is read before it is spawned.
            var manager = ConnectionManager.Instance;
            var runner = manager != null ? manager.Runner : null;
            if (runner != null && runner.IsRunning)
            {
                _networkObjects.Clear();
                runner.GetAllNetworkObjects(_networkObjects);
                for (var i = 0; i < _networkObjects.Count; i++)
                {
                    var networkObject = _networkObjects[i];
                    if (networkObject == null || !networkObject.IsValid)
                        continue;

                    var item = networkObject.GetComponentInChildren<Carryable>();
                    if (item != null)
                        AuditItem(item);
                }
            }

            var characters = PlayerCharacter.All;
            for (var i = 0; i < characters.Count; i++)
            {
                var character = characters[i];
                if (character != null && character.Object != null && character.Object.IsValid)
                    AuditCharacter(character);
            }

            // Keys seen now become the reference for the next run; the old set is cleared at the start of that run.
            (_previous, _current) = (_current, _previous);
            UpdateOverlay();
        }

        private void AuditItem(Carryable item)
        {
            if (item.Inert || item.IsHeld)
                return;

            if (item.IsPinned)
            {
                AuditPin(item);
                return;
            }

            GatherColliders(item.transform);
            if (_colliders.Count == 0)
                return;

            var bounds = _colliders[0].bounds;
            for (var i = 1; i < _colliders.Count; i++)
                bounds.Encapsulate(_colliders[i].bounds);

            GatherRenderers(item.transform);
            if (_renderers.Count > 0)
            {
                var visualDiff = LowestRendererY() - bounds.min.y;
                if (Mathf.Abs(visualDiff) > _visualTolerance)
                {
                    var side = visualDiff > 0f ? "por encima" : "por debajo";
                    Report(item, Finding.Visual, $"VISUAL DESPLAZADO {Centimeters(visualDiff)} cm {side} del colisionador",
                        bounds.min, bounds.min + Vector3.up * visualDiff, true);
                }
            }

            // Only the state authority knows how long the item has been loose (FreeTime is 0 on proxies).
            if (!item.HasStateAuthority || item.FreeTime < RestingSeconds)
                return;

            var center = bounds.center;

            // Probe from above the top: the first surface found is the ground under the item (its own colliders excluded).
            // If that surface is above the bottom, the item is sunk into it.
            var topOrigin = new Vector3(center.x, bounds.max.y + ProbeLift, center.z);
            if (!TryGround(item.transform, topOrigin, out var surfaceY))
                return;

            var sinkDepth = surfaceY - bounds.min.y;
            if (sinkDepth > _floatTolerance)
            {
                Report(item, Finding.Sinks, $"HUNDIDO {Centimeters(sinkDepth)} cm",
                    new Vector3(center.x, bounds.min.y, center.z), new Vector3(center.x, surfaceY, center.z), true);
                return;
            }

            // Probe from just above the bottom: the gap to the first surface below is how far the item floats.
            var bottomOrigin = new Vector3(center.x, bounds.min.y + ProbeLift, center.z);
            if (!TryGround(item.transform, bottomOrigin, out var groundY))
                return;

            var gap = bounds.min.y - groundY;
            if (gap > _floatTolerance)
            {
                Report(item, Finding.Floats, $"FLOTA {Centimeters(gap)} cm",
                    new Vector3(center.x, bounds.min.y, center.z), new Vector3(center.x, groundY, center.z), true);
            }
        }

        private void AuditPin(Carryable item)
        {
            if (PinOwnerProperty == null)
                return;

            var ownerId = (NetworkBehaviourId)PinOwnerProperty.GetValue(item);
            if (!item.Runner.TryFindBehaviour(ownerId, out NetworkBehaviour owner))
                return;

            var spot = owner as IPinSpot;
            if (spot == null)
                return;

            var point = spot.GetPinPoint(item.PinnedSlot);
            if (point == null)
                return;

            var offset = Vector3.Distance(item.transform.position, point.position);
            if (offset > _pinTolerance)
            {
                Report(item, Finding.Pin, $"PIN DESALINEADO {Centimeters(offset)} cm",
                    item.transform.position, point.position, true);
            }
        }

        private void AuditCharacter(PlayerCharacter character)
        {
            // Airborne and riding characters are not on the ground, so their feet are not expected to touch it.
            var movement = character.GetComponent<PlayerMovement>();
            if (movement != null && movement.IsAirborne)
                return;

            var stack = character.GetComponent<GnomeStack>();
            if (stack != null && stack.IsRiding)
                return;

            GatherRenderers(character.transform);
            if (_renderers.Count == 0)
                return;

            // The root sits at the feet, so the lowest visual should be level with it.
            var position = character.transform.position;
            var diff = LowestRendererY() - position.y;
            if (diff > _feetTolerance)
            {
                Report(character, Finding.Feet, $"PIES FLOTAN {Centimeters(diff)} cm",
                    position, position + Vector3.up * diff, false);
            }
            else if (diff < -_feetTolerance)
            {
                Report(character, Finding.Feet, $"PIES SE HUNDEN {Centimeters(-diff)} cm",
                    position, position + Vector3.up * diff, false);
            }
        }

        /// <summary>Records a finding for the overlay and logs it the first time it shows up.</summary>
        private void Report(UnityEngine.Object owner, Finding kind, string text, Vector3 from, Vector3 to, bool drawLine)
        {
            var key = (long)owner.GetInstanceID() * 16 + (int)kind;
            _current.Add(key);
            _findingCount++;

            if (_lines.Count < MaxLines)
                _lines.Add($"{text}  {owner.name}");

            if (!_previous.Contains(key))
                Debug.LogWarning($"[GroundingAudit] {text}", owner);

            if (drawLine)
                Debug.DrawLine(from, to, Color.red, AuditInterval);
        }

        /// <summary>
        /// Surface height of the nearest collider below <paramref name="origin"/>, ignoring colliders of <paramref name="self"/>.
        /// Triggers are ignored. Returns false when nothing is found within <see cref="ProbeDistance"/>.
        /// </summary>
        private bool TryGround(Transform self, Vector3 origin, out float surfaceY)
        {
            surfaceY = 0f;
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, ProbeDistance, _groundMask, QueryTriggerInteraction.Ignore);
            var nearest = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(self))
                    continue;

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    surfaceY = hit.point.y;
                }
            }

            return nearest < float.MaxValue;
        }

        private void GatherColliders(Transform root)
        {
            _colliders.Clear();
            root.GetComponentsInChildren(false, _colliders);
            for (var i = _colliders.Count - 1; i >= 0; i--)
            {
                if (_colliders[i].isTrigger || !_colliders[i].enabled)
                    _colliders.RemoveAt(i);
            }
        }

        private void GatherRenderers(Transform root)
        {
            _renderers.Clear();
            root.GetComponentsInChildren(false, _renderers);
            for (var i = _renderers.Count - 1; i >= 0; i--)
            {
                var renderer = _renderers[i];
                if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                    _renderers.RemoveAt(i);
            }
        }

        /// <summary>Lowest world Y of the gathered renderers. Only call with at least one renderer.</summary>
        private float LowestRendererY()
        {
            var lowest = float.MaxValue;
            for (var i = 0; i < _renderers.Count; i++)
            {
                var minY = _renderers[i].bounds.min.y;
                if (minY < lowest)
                    lowest = minY;
            }

            return lowest;
        }

        private void UpdateOverlay()
        {
            _overlay.Clear();
            if (_findingCount == 0)
            {
                _overlay.Append("Auditoría de flotantes: sin hallazgos");
            }
            else
            {
                _overlay.Append("Auditoría de flotantes: ").Append(_findingCount).Append(" hallazgo(s)");
                for (var i = 0; i < _lines.Count; i++)
                    _overlay.Append('\n').Append(_lines[i]);

                if (_findingCount > _lines.Count)
                    _overlay.Append("\n... y ").Append(_findingCount - _lines.Count).Append(" más");
            }

            _overlayText.text = _overlay.ToString();
        }

        private void ClearOverlay()
        {
            _lines.Clear();
            _findingCount = 0;
            _overlayText.text = string.Empty;
        }

        private static string Centimeters(float meters) => (meters * 100f).ToString("0");
    }
}
#endif
