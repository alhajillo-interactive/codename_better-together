// In-game grounding audit (C1.9). Press F4 to list loose items that float or sink, pinned items off their spot,
// visuals that do not match their colliders and characters whose feet or root float. It only reports: it never moves,
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
    /// logged once per F4 session, the first time it appears, with the object as context. Loose items are only checked for
    /// floating on the state authority (<see cref="Carryable.FreeTime"/> is only known there) and only while they are still.
    /// </summary>
    public class GroundingAudit : MonoBehaviour
    {
        private const float AuditInterval = 0.5f;

        // A loose item must have been free this long before it counts as resting.
        private const float RestingSeconds = 1f;

        // Speed below which a loose item counts as still (m/s, squared).
        private const float StillSpeedSquared = 0.0025f;

        // Probes start this far above the bottom (or the top) of the bounds, so they do not begin inside a collider.
        private const float ProbeLift = 0.05f;

        // Ground farther than this below the object is not considered.
        private const float ProbeDistance = 100f;

        // Footprint fraction used for the floating rays: centre plus four points at this share of the half extents.
        private const float FootprintShare = 0.7f;

        private const int MaxLines = 12;
        private const int HitCapacity = 16;
        private const int NetworkObjectCapacity = 256;
        private const int ComponentCapacity = 32;
        private const int BakeCapacity = 512;

        private const string WatercolorMaskName = "Watercolor Mask";
        private const string WatercolorOutlineName = "Watercolor Outline";

        // Carryable keeps its pin owner private. Read it once here, so no public API has to change for this tool.
        // TODO: replace this reflection with a public Carryable.TryGetPinPoint(out Transform) when the orchestrator
        // integrates that change; then delete PinOwnerProperty and the warning below.
        private static readonly PropertyInfo PinOwnerProperty =
            typeof(Carryable).GetProperty("PinOwnerId", BindingFlags.Instance | BindingFlags.NonPublic);

        // Static because a shape does not change between runs: computed once per item instance.
        private static readonly Dictionary<int, float> VisualOffsets = new Dictionary<int, float>();

        private static bool _pinWarned;

        [Tooltip("Loose items with their base or centre below ground by more than this (m) are reported.")]
        [SerializeField] private float _floatTolerance = 0.02f;

        [Tooltip("Lowest point of the visuals vs the lowest point of the colliders, in the item's own space: more than this apart (m) is reported.")]
        [SerializeField] private float _visualTolerance = 0.02f;

        [Tooltip("Pinned items farther than this (m) from their pin point in two runs in a row are reported.")]
        [SerializeField] private float _pinTolerance = 0.01f;

        [Tooltip("Characters on the ground whose lowest skinned mesh is more than this (m) away from their root are reported.")]
        [SerializeField] private float _feetTolerance = 0.03f;

        [Tooltip("Layers that count as ground for the probes. Triggers are always ignored.")]
        [SerializeField] private LayerMask _groundMask = Physics.DefaultRaycastLayers;

        private enum Finding : byte { Floats, Sinks, Visual, Pin, Feet, Root }

        /// <summary>The single audit alive in this session.</summary>
        public static GroundingAudit Instance { get; private set; }

        // Preallocated so auditing does not allocate lists or arrays on every run.
        private readonly List<NetworkObject> _networkObjects = new List<NetworkObject>(NetworkObjectCapacity);
        private readonly List<Collider> _colliders = new List<Collider>(ComponentCapacity);
        private readonly List<Renderer> _renderers = new List<Renderer>(ComponentCapacity);
        private readonly List<SkinnedMeshRenderer> _skinned = new List<SkinnedMeshRenderer>(ComponentCapacity);
        private readonly List<Vector3> _bakeVertices = new List<Vector3>(BakeCapacity);
        private readonly RaycastHit[] _hits = new RaycastHit[HitCapacity];
        private readonly List<string> _lines = new List<string>(MaxLines);
        private readonly StringBuilder _overlay = new StringBuilder(512);
        private readonly GUIContent _overlayText = new GUIContent();

        // Rigidbodies of the items, looked up once per item.
        private readonly Dictionary<int, Rigidbody> _bodies = new Dictionary<int, Rigidbody>();

        // Keys already logged in this F4 session. Cleared when F4 turns on.
        private readonly HashSet<long> _logged = new HashSet<long>();

        // Pin findings must repeat in two runs in a row (the pin blend takes about 0.28 s). Candidates of the last run
        // and of this run are kept in two sets that swap at the end of each run.
        private HashSet<long> _pinCandidates = new HashSet<long>();
        private HashSet<long> _nextPinCandidates = new HashSet<long>();

        private Mesh _bakeMesh;
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
            if (_bakeMesh != null)
                Destroy(_bakeMesh);

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // Only the key check runs while the audit is off: no allocations and no scanning.
            if (Keyboard.current != null && !FirstPersonCamera.IsTyping && Keyboard.current.f4Key.wasPressedThisFrame)
            {
                _active = !_active;

                // Each F4 session logs its findings again, and starts with no pin candidates.
                _logged.Clear();
                _pinCandidates.Clear();
                _nextPinCandidates.Clear();

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
            _lines.Clear();
            _findingCount = 0;
            _nextPinCandidates.Clear();

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

            // Pin candidates seen now must repeat in the next run to be reported.
            (_pinCandidates, _nextPinCandidates) = (_nextPinCandidates, _pinCandidates);
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

            CheckVisual(item);

            // Only the state authority knows how long the item has been loose (FreeTime is 0 on proxies).
            if (!item.HasStateAuthority || item.FreeTime < RestingSeconds)
                return;

            // Floating and sinking are only judged while the item is still.
            var body = CachedBody(item);
            if (body == null || !(body.IsSleeping() || body.linearVelocity.sqrMagnitude < StillSpeedSquared))
                return;

            var center = bounds.center;

            // Sinking: the centre is below the ground under it. The ground is searched from above the top of the bounds.
            if (GroundHeight(item.transform, center, bounds.max.y, out var groundAtCenter)
                && center.y < groundAtCenter - _floatTolerance)
            {
                Report(item, Finding.Sinks, $"HUNDIDO {Centimeters(groundAtCenter - center.y)} cm (centro bajo el suelo)",
                    new Vector3(center.x, center.y, center.z), new Vector3(center.x, groundAtCenter, center.z), true);
                return;
            }

            // Floating: five rays (centre and four points inside the footprint). The smallest gap must exceed the tolerance,
            // so an item resting on one corner is not reported.
            var extentX = bounds.extents.x * FootprintShare;
            var extentZ = bounds.extents.z * FootprintShare;
            var smallestGap = float.MaxValue;
            var gapFrom = Vector3.zero;
            var gapTo = Vector3.zero;
            var anyHit = false;
            for (var r = 0; r < 5; r++)
            {
                var offsetX = r == 0 ? 0f : (r == 1 || r == 2 ? extentX : -extentX);
                var offsetZ = r == 0 ? 0f : (r == 1 || r == 3 ? extentZ : -extentZ);
                var origin = new Vector3(center.x + offsetX, bounds.min.y + ProbeLift, center.z + offsetZ);
                if (!TryGround(item.transform, origin, out var groundY))
                    continue;

                var gap = bounds.min.y - groundY;
                anyHit = true;
                if (gap < smallestGap)
                {
                    smallestGap = gap;
                    gapFrom = new Vector3(origin.x, bounds.min.y, origin.z);
                    gapTo = new Vector3(origin.x, groundY, origin.z);
                }
            }

            if (anyHit && smallestGap > _floatTolerance)
                Report(item, Finding.Floats, $"FLOTA {Centimeters(smallestGap)} cm", gapFrom, gapTo, true);
        }

        /// <summary>
        /// Compares the lowest visual with the lowest collider in the item's own space. The offset does not depend on the
        /// item's position or rotation, so it is computed once per instance and cached.
        /// </summary>
        private void CheckVisual(Carryable item)
        {
            var id = item.GetInstanceID();
            if (!VisualOffsets.TryGetValue(id, out var offset))
            {
                offset = ComputeVisualOffset(item.transform);
                VisualOffsets[id] = offset;
            }

            // Offsets are in the item's local units; the lossy scale turns them into metres.
            var diffMeters = offset * item.transform.lossyScale.y;
            if (Mathf.Abs(diffMeters) > _visualTolerance)
            {
                var side = diffMeters > 0f ? "por encima" : "por debajo";
                var from = item.transform.position;
                Report(item, Finding.Visual, $"VISUAL DESPLAZADO {Centimeters(diffMeters)} cm {side} del colisionador",
                    from, from + Vector3.up * diffMeters, false);
            }
        }

        private float ComputeVisualOffset(Transform root)
        {
            GatherRenderers(root);
            GatherColliders(root);
            if (_renderers.Count == 0 || _colliders.Count == 0)
                return 0f;

            var visualMin = float.MaxValue;
            for (var i = 0; i < _renderers.Count; i++)
            {
                var renderer = _renderers[i];
                var local = renderer.localBounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z);
                    var inRoot = root.InverseTransformPoint(renderer.transform.TransformPoint(point));
                    if (inRoot.y < visualMin)
                        visualMin = inRoot.y;
                }
            }

            var colliderMin = float.MaxValue;
            for (var i = 0; i < _colliders.Count; i++)
            {
                var lowest = LocalShapeMinY(_colliders[i], root);
                if (lowest < colliderMin)
                    colliderMin = lowest;
            }

            return visualMin - colliderMin;
        }

        /// <summary>Lowest Y, in <paramref name="root"/> space, of the collider's own shape (box, sphere or capsule corners).</summary>
        private static float LocalShapeMinY(Collider collider, Transform root)
        {
            Vector3 center;
            Vector3 extents;
            switch (collider)
            {
                case BoxCollider box:
                    center = box.center;
                    extents = box.size * 0.5f;
                    break;
                case SphereCollider sphere:
                    center = sphere.center;
                    extents = Vector3.one * sphere.radius;
                    break;
                case CapsuleCollider capsule:
                    center = capsule.center;
                    extents = new Vector3(capsule.radius, capsule.radius, capsule.radius);
                    var half = Mathf.Max(capsule.height * 0.5f, capsule.radius);
                    if (capsule.direction == 0) extents.x = half;
                    else if (capsule.direction == 1) extents.y = half;
                    else extents.z = half;
                    break;
                default:
                    // Meshes and other shapes: the world bounds are the best cheap approximation.
                    return LowestWorldBoundsInRoot(collider, root);
            }

            var lowest = float.MaxValue;
            for (var corner = 0; corner < 8; corner++)
            {
                var local = center + new Vector3(
                    (corner & 1) == 0 ? -extents.x : extents.x,
                    (corner & 2) == 0 ? -extents.y : extents.y,
                    (corner & 4) == 0 ? -extents.z : extents.z);
                var inRoot = root.InverseTransformPoint(collider.transform.TransformPoint(local));
                if (inRoot.y < lowest)
                    lowest = inRoot.y;
            }

            return lowest;
        }

        private static float LowestWorldBoundsInRoot(Collider collider, Transform root)
        {
            var b = collider.bounds;
            var lowest = float.MaxValue;
            for (var corner = 0; corner < 8; corner++)
            {
                var world = new Vector3(
                    (corner & 1) == 0 ? b.min.x : b.max.x,
                    (corner & 2) == 0 ? b.min.y : b.max.y,
                    (corner & 4) == 0 ? b.min.z : b.max.z);
                var inRoot = root.InverseTransformPoint(world);
                if (inRoot.y < lowest)
                    lowest = inRoot.y;
            }

            return lowest;
        }

        private void AuditPin(Carryable item)
        {
            if (PinOwnerProperty == null)
            {
                if (!_pinWarned)
                {
                    _pinWarned = true;
                    Debug.LogWarning("[GroundingAudit] No se encuentra Carryable.PinOwnerId: la auditoría de pines está desactivada.", item);
                }

                return;
            }

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
            if (offset <= _pinTolerance)
                return;

            // Reported only when the same pin is off in two runs in a row.
            var key = KeyOf(item, Finding.Pin);
            if (_pinCandidates.Contains(key))
            {
                Report(item, Finding.Pin, $"PIN DESALINEADO {Centimeters(offset)} cm",
                    item.transform.position, point.position, true);
            }
            else
            {
                _nextPinCandidates.Add(key);
            }
        }

        private void AuditCharacter(PlayerCharacter character)
        {
            // Airborne, riding and stunned characters are not expected to stand on the ground.
            var movement = character.GetComponent<PlayerMovement>();
            if (movement != null && (movement.IsAirborne || movement.IsStunned))
                return;

            var stack = character.GetComponent<GnomeStack>();
            if (stack != null && stack.IsRiding)
                return;

            var position = character.transform.position;
            var view = character.GetComponentInChildren<LocalPlayerView>(true);
            var body = view != null && view.Body != null ? view.Body : character.transform;

            // Feet: lowest skinned vertex of the visible model, compared with the root.
            if (TryLowestSkinnedY(body, out var lowest))
            {
                var diff = lowest - position.y;
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

            // Root: the ray from the root to the ground should not be longer than the controller's skin plus tolerance.
            var controller = character.GetComponent<CharacterController>();
            if (controller == null)
                return;

            var rootOrigin = position + Vector3.up * ProbeLift;
            if (!TryGround(character.transform, rootOrigin, out var groundY))
                return;

            var rootGap = position.y - groundY;
            if (rootGap > controller.skinWidth + _feetTolerance)
            {
                Report(character, Finding.Root, $"RAÍZ FLOTA {Centimeters(rootGap)} cm",
                    position, new Vector3(position.x, groundY, position.z), false);
            }
        }

        /// <summary>
        /// Lowest world Y of the visible skinned meshes under <paramref name="body"/>, from the baked (posed) mesh.
        /// Watercolor copies are skipped. Only called while F4 is on, every half second.
        /// </summary>
        private bool TryLowestSkinnedY(Transform body, out float lowest)
        {
            lowest = float.MaxValue;
            _skinned.Clear();
            body.GetComponentsInChildren(false, _skinned);

            if (_bakeMesh == null)
                _bakeMesh = new Mesh { name = "GroundingAudit bake" };

            var any = false;
            for (var i = 0; i < _skinned.Count; i++)
            {
                var smr = _skinned[i];
                if (!smr.enabled || smr.sharedMesh == null)
                    continue;

                if (smr.name == WatercolorMaskName || smr.name == WatercolorOutlineName)
                    continue;

                smr.BakeMesh(_bakeMesh, true);
                _bakeVertices.Clear();
                _bakeMesh.GetVertices(_bakeVertices);

                var transform = smr.transform;
                for (var v = 0; v < _bakeVertices.Count; v++)
                {
                    var y = transform.TransformPoint(_bakeVertices[v]).y;
                    if (y < lowest)
                        lowest = y;
                }

                any = true;
            }

            return any;
        }

        /// <summary>
        /// Ground height at <paramref name="point"/>: the terrain when one covers it, otherwise the nearest surface below
        /// <paramref name="topY"/> (surfaces above the top of the object are ignored). Own colliders are excluded.
        /// </summary>
        private bool GroundHeight(Transform self, Vector3 point, float topY, out float groundY)
        {
            if (TryTerrainHeight(point, out groundY))
                return true;

            var origin = new Vector3(point.x, topY + ProbeLift, point.z);
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, ProbeDistance, _groundMask, QueryTriggerInteraction.Ignore);
            var nearest = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(self) || hit.point.y > topY)
                    continue;

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    groundY = hit.point.y;
                }
            }

            return nearest < float.MaxValue;
        }

        private static bool TryTerrainHeight(Vector3 point, out float height)
        {
            height = 0f;
            var terrains = Terrain.activeTerrains;
            for (var i = 0; i < terrains.Length; i++)
            {
                var terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null)
                    continue;

                var origin = terrain.GetPosition();
                var size = terrain.terrainData.size;
                if (point.x < origin.x || point.x > origin.x + size.x || point.z < origin.z || point.z > origin.z + size.z)
                    continue;

                height = terrain.SampleHeight(point) + origin.y;
                return true;
            }

            return false;
        }

        /// <summary>Surface height of the nearest collider below <paramref name="origin"/>, ignoring colliders of <paramref name="self"/>.</summary>
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

        private Rigidbody CachedBody(Carryable item)
        {
            var id = item.GetInstanceID();
            if (_bodies.TryGetValue(id, out var body) && body != null)
                return body;

            body = item.GetComponent<Rigidbody>();
            if (body != null)
                _bodies[id] = body;

            return body;
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

        /// <summary>Records a finding for the overlay and logs it the first time it shows up in this F4 session.</summary>
        private void Report(UnityEngine.Object owner, Finding kind, string text, Vector3 from, Vector3 to, bool drawLine)
        {
            _findingCount++;

            if (_lines.Count < MaxLines)
                _lines.Add($"{text}  {owner.name}");

            if (_logged.Add(KeyOf(owner, kind)))
                Debug.LogWarning($"[GroundingAudit] {text}", owner);

            if (drawLine)
                Debug.DrawLine(from, to, Color.red, AuditInterval);
        }

        private static long KeyOf(UnityEngine.Object owner, Finding kind) => (long)owner.GetInstanceID() * 16 + (int)kind;

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
