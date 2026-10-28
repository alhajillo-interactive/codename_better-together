// Editor-only scene audit (C1.9). Run it from the menu GnoMore/Auditar flotantes en escena: it groups the renderers of the
// open scene by prop and lists the props that float above the ground, sink below it (centre under the ground), or are
// tall with no collider at all. It only reports, one console line per finding with the object as context, and never
// moves or edits anything.
using System.Collections.Generic;
using BetterTogether.Helpers;
using BetterTogether.Interaction;
using BetterTogether.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterTogether.EditorTools
{
    public static class FloatingAudit
    {
        private const string LogPrefix = "[FloatingAudit]";

        // Gap (m) above which a prop counts as floating or sinking.
        private const float FloatTolerance = 0.05f;

        // Probes start this far above the bottom (or the top) of the bounds, so they do not begin inside a collider.
        private const float ProbeLift = 0.05f;

        // Ground farther than this below the prop is not considered.
        private const float ProbeDistance = 200f;

        // Props taller than this (m) with no collider at all are listed apart, because players walk through them.
        private const float ColliderlessMinHeight = 0.5f;

        private const float LineDuration = 10f;
        private const int HitCapacity = 32;

        private static readonly RaycastHit[] Hits = new RaycastHit[HitCapacity];

        [MenuItem("GnoMore/Auditar flotantes en escena")]
        public static void AuditOpenScene()
        {
            Physics.SyncTransforms();

            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            // Pass 1: the distinct props that own at least one auditable renderer.
            var rootIds = new HashSet<int>();
            var roots = new List<Transform>();
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!IsAuditable(renderer))
                    continue;

                var root = PropRoot(renderer.gameObject);
                if (rootIds.Add(root.GetInstanceID()))
                    roots.Add(root);
            }

            // Pass 2: one test per prop, on the bounds of all its visible renderers (LOD0 only where there is a LODGroup).
            var floating = 0;
            var sinking = 0;
            var colliderless = 0;
            for (var i = 0; i < roots.Count; i++)
            {
                var root = roots[i];
                if (!TryGetPropBounds(root, out var bounds))
                    continue;

                var owner = root.gameObject;
                var center = bounds.center;

                if (!HasOwnCollider(root) && bounds.size.y > ColliderlessMinHeight && owner.layer == 0)
                {
                    colliderless++;
                    Debug.LogWarning($"{LogPrefix} SIN COLISIÓN: {owner.name} ({bounds.size.y:0.0} m de alto)", owner);
                }

                // Floating: the ground under the centre of the bottom, probed from just above it.
                var bottomOrigin = new Vector3(center.x, bounds.min.y + ProbeLift, center.z);
                if (TryGround(root, bottomOrigin, out var groundY))
                {
                    var gap = bounds.min.y - groundY;
                    if (gap > FloatTolerance)
                    {
                        floating++;
                        Debug.LogWarning($"{LogPrefix} FLOTA {gap * 100f:0} cm: {owner.name}", owner);
                        Debug.DrawLine(new Vector3(center.x, bounds.min.y, center.z), new Vector3(center.x, groundY, center.z), Color.red, LineDuration);
                        continue;
                    }
                }

                // Sinking: the centre lies below the ground under it (terrain first, otherwise the nearest surface below the top).
                if (GroundHeight(root, center, bounds.max.y, out var groundAtCenter) && center.y < groundAtCenter - FloatTolerance)
                {
                    sinking++;
                    Debug.LogWarning($"{LogPrefix} HUNDIDO {(groundAtCenter - center.y) * 100f:0} cm (centro bajo el suelo): {owner.name}", owner);
                    Debug.DrawLine(new Vector3(center.x, center.y, center.z), new Vector3(center.x, groundAtCenter, center.z), Color.magenta, LineDuration);
                }
            }

            var total = floating + sinking + colliderless;
            var summary = total == 0 ? "Sin hallazgos." : "Revisa los avisos anteriores; clic en cada uno selecciona el objeto.";
            Debug.Log($"{LogPrefix} {floating} flotantes, {sinking} hundidos (centro bajo el suelo), {colliderless} sin colisión en '{SceneManager.GetActiveScene().name}'. {summary}");
        }

        /// <summary>
        /// The prop a renderer belongs to: the outermost prefab instance root when the object is a prefab instance, otherwise
        /// the object itself. Climbing to an ancestor is not done for plain objects: a level group with a renderer would
        /// merge unrelated props into one.
        /// </summary>
        private static Transform PropRoot(GameObject go)
        {
            var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            return (outermost != null ? outermost : go).transform;
        }

        /// <summary>Skips objects that are not part of the level audit: inactive, UI, dynamic items, characters, markers and hidden helpers.</summary>
        private static bool IsAuditable(Renderer renderer)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return false;

            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                return false;

            if ((renderer.gameObject.hideFlags & (HideFlags.HideInHierarchy | HideFlags.DontSave)) != 0)
                return false;

            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0 && renderer.gameObject.layer == uiLayer)
                return false;

            if (renderer.GetComponentInParent<PlayerCharacter>(true) != null || renderer.GetComponentInParent<Carryable>(true) != null)
                return false;

            return renderer.GetComponentInParent<IntentionallyFloating>(true) == null;
        }

        /// <summary>
        /// Union of the bounds of the prop's visible renderers. Where a LODGroup exists only its LOD0 renderers count.
        /// Returns false when there is nothing to measure.
        /// </summary>
        private static bool TryGetPropBounds(Transform root, out Bounds bounds)
        {
            bounds = default;

            var hiddenByLod = new HashSet<Renderer>();
            var lodGroups = root.GetComponentsInChildren<LODGroup>(true);
            for (var g = 0; g < lodGroups.Length; g++)
            {
                var lods = lodGroups[g].GetLODs();
                for (var level = 1; level < lods.Length; level++)
                {
                    var levelRenderers = lods[level].renderers;
                    for (var r = 0; r < levelRenderers.Length; r++)
                    {
                        if (levelRenderers[r] != null)
                            hiddenByLod.Add(levelRenderers[r]);
                    }
                }
            }

            var renderers = root.GetComponentsInChildren<Renderer>(false);
            var found = false;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!IsAuditable(renderer) || hiddenByLod.Contains(renderer))
                    continue;

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        /// <summary>True when the prop has a solid collider of its own (not a trigger, not terrain).</summary>
        private static bool HasOwnCollider(Transform root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (!collider.isTrigger && !(collider is TerrainCollider))
                    return true;
            }

            return false;
        }

        /// <summary>Nearest surface below <paramref name="origin"/>, ignoring the prop's own colliders.</summary>
        private static bool TryGround(Transform root, Vector3 origin, out float surfaceY)
        {
            surfaceY = 0f;
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, ProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var nearest = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(root))
                    continue;

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    surfaceY = hit.point.y;
                }
            }

            return nearest < float.MaxValue;
        }

        /// <summary>
        /// Ground height at <paramref name="point"/>: the terrain when one covers it, otherwise the nearest surface below
        /// <paramref name="topY"/> (surfaces above the top of the prop are ignored).
        /// </summary>
        private static bool GroundHeight(Transform root, Vector3 point, float topY, out float groundY)
        {
            if (TryTerrainHeight(point, out groundY))
                return true;

            var origin = new Vector3(point.x, topY + ProbeLift, point.z);
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, ProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var nearest = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(root) || hit.point.y > topY)
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
    }
}
