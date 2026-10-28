// Editor-only scene audit (C1.9). Run it from the menu GnoMore/Auditar flotantes en escena: it lists the renderers of the
// open scene that float above the ground, sink into it, or are tall props with no collider at all. It only reports,
// one console line per finding with the object as context, and never moves or edits anything.
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

        // Gap (m) above which an object counts as floating or sinking.
        private const float FloatTolerance = 0.05f;

        // Probes start this far above the bottom (or the top) of the bounds, so they do not begin inside a collider.
        private const float ProbeLift = 0.05f;

        // Ground farther than this below the object is not considered.
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
            var reported = new System.Collections.Generic.HashSet<int>();
            var floating = 0;
            var sinking = 0;
            var colliderless = 0;

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!IsAuditable(renderer))
                    continue;

                // One line per GameObject, even when it has several renderers.
                var owner = renderer.gameObject;
                if (!reported.Add(owner.GetInstanceID()))
                    continue;

                if (!HasCollider(renderer))
                {
                    if (renderer.bounds.size.y > ColliderlessMinHeight && owner.layer == 0)
                    {
                        colliderless++;
                        Debug.LogWarning($"{LogPrefix} SIN COLISIÓN: {owner.name} ({renderer.bounds.size.y:0.0} m de alto)", owner);
                    }
                }

                var bounds = renderer.bounds;
                var center = bounds.center;
                var self = OwnerRoot(renderer.transform);

                // Floating: the ground under the bottom of the bounds, probed from just above it.
                var bottomOrigin = new Vector3(center.x, bounds.min.y + ProbeLift, center.z);
                if (TryGround(self, bottomOrigin, out var groundY))
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

                // Sinking: the surface under the top of the bounds lies above the bottom, so the base is below ground.
                var topOrigin = new Vector3(center.x, bounds.max.y + ProbeLift, center.z);
                if (TryGround(self, topOrigin, out var surfaceY) && surfaceY - bounds.min.y > FloatTolerance)
                {
                    sinking++;
                    Debug.LogWarning($"{LogPrefix} HUNDIDO {(surfaceY - bounds.min.y) * 100f:0} cm: {owner.name}", owner);
                    Debug.DrawLine(new Vector3(center.x, bounds.min.y, center.z), new Vector3(center.x, surfaceY, center.z), Color.magenta, LineDuration);
                }
            }

            var total = floating + sinking + colliderless;
            var summary = total == 0 ? "Sin hallazgos." : "Revisa los avisos anteriores; clic en cada uno selecciona el objeto.";
            Debug.Log($"{LogPrefix} {floating} flotantes, {sinking} hundidos (base bajo el suelo), {colliderless} sin colisión en '{SceneManager.GetActiveScene().name}'. {summary}");
        }

        /// <summary>Skips objects that are not part of the level audit: inactive, UI, dynamic items and characters, and markers.</summary>
        private static bool IsAuditable(Renderer renderer)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return false;

            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                return false;

            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0 && renderer.gameObject.layer == uiLayer)
                return false;

            if (renderer.GetComponentInParent<PlayerCharacter>(true) != null || renderer.GetComponentInParent<Carryable>(true) != null)
                return false;

            return renderer.GetComponentInParent<IntentionallyFloating>(true) == null;
        }

        private static bool HasCollider(Renderer renderer) =>
            renderer.GetComponentInParent<Collider>(true) != null || renderer.GetComponentInChildren<Collider>(true) != null;

        /// <summary>The nearest ancestor (or the object itself) that has a collider or a rigidbody. Its colliders are not probed against.</summary>
        private static Transform OwnerRoot(Transform start)
        {
            for (var t = start; t != null; t = t.parent)
            {
                if (t.TryGetComponent(out Collider _) || t.TryGetComponent(out Rigidbody _))
                    return t;
            }

            return start;
        }

        /// <summary>Height of the nearest collider below <paramref name="origin"/> that is not part of <paramref name="self"/>.</summary>
        private static bool TryGround(Transform self, Vector3 origin, out float surfaceY)
        {
            surfaceY = 0f;
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, ProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var nearest = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var hit = Hits[i];
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
    }
}
