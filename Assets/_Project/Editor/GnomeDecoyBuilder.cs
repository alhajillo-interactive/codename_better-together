// Editor-only tool. Menu GnoMore/Colocar gnomos señuelo: idempotently
//  1. builds Prefabs/GardenGnomeDecoy.prefab from the Gnome prefab's visual (Mesh01): same model and Animator, no network
//     or gameplay components, a solid capsule the size of the gnome's CharacterController on the Environment layer, and
//     GardenGnomeDecoy with the ceramic colour copied from the Gnome's GardenDisguise (so decoy and disguise match);
//  2. places one decoy next to each anchor in CampBryan (beside mushrooms, bushes, the tent...), under a "GardenGnomeDecoys"
//     root that is rebuilt on every run. Each spot is grounded with a raycast and must be free of geometry and players.
using System.Collections.Generic;
using BetterTogether.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static class GnomeDecoyBuilder
    {
        private const string LogPrefix = "[GnomeDecoyBuilder]";
        private const string GnomePath = "Assets/_Project/Prefabs/Gnome.prefab";
        private const string DecoyPath = "Assets/_Project/Prefabs/GardenGnomeDecoy.prefab";
        private const string ScenePath = "Assets/_Project/Scenes/CampBryan.unity";
        private const string RootName = "GardenGnomeDecoys";
        private const string VisualName = "Mesh01";

        // Scenery a gnome would hide next to (x, z in CampBryan). The decoy goes on free ground around each one.
        private static readonly Vector2[] Anchors =
        {
            new(-6.0f, -3.2f),  // Mushrooms near the fire
            new(-1.1f, -3.0f),  // Mushrooms by the fire
            new(-8.6f, -9.5f),  // Berry bush
            new(-3.7f, 11.1f),  // Mushrooms on the way to the tent
            new(-6.2f, 15.8f),  // Beside the tent
            new(-3.0f, 21.2f),  // Next to the cage
        };

        private const float SearchRadius = 0.7f;
        private const int SearchSteps = 12;

        [MenuItem("GnoMore/Colocar gnomos señuelo")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"{LogPrefix} Leave Play Mode first.");
                return;
            }

            var decoy = BuildPrefab(out var radius, out var height);
            if (decoy == null)
                return;

            Place(decoy, radius, height);
        }

        private static GameObject BuildPrefab(out float radius, out float height)
        {
            radius = 0.12f;
            height = 0.3f;
            var gnome = PrefabUtility.LoadPrefabContents(GnomePath);
            try
            {
                var visual = gnome.transform.Find(VisualName);
                if (visual == null)
                {
                    Debug.LogError($"{LogPrefix} {GnomePath} has no '{VisualName}' child.");
                    return null;
                }

                var controller = gnome.GetComponent<CharacterController>();
                var scale = Mathf.Abs(gnome.transform.lossyScale.y);
                if (controller != null)
                {
                    radius = controller.radius * scale;
                    height = controller.height * scale;
                }

                var ceramic = new Color(0.85f, 0.45f, 0.35f);
                var disguise = gnome.GetComponent("GardenDisguise");
                if (disguise != null)
                    ceramic = new SerializedObject(disguise).FindProperty("_ceramicColor").colorValue;

                var root = new GameObject("GardenGnomeDecoy") { layer = LayerMask.NameToLayer("Environment") };
                root.transform.localScale = gnome.transform.localScale;
                var model = Object.Instantiate(visual.gameObject, root.transform);
                model.name = VisualName;
                model.transform.localPosition = visual.localPosition;
                model.transform.localRotation = visual.localRotation;
                model.transform.localScale = visual.localScale;
                model.SetActive(true);
                StripBehaviours(model);

                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.radius = controller != null ? controller.radius : radius;
                capsule.height = controller != null ? controller.height : height;
                capsule.center = controller != null ? controller.center : new Vector3(0f, height * 0.5f, 0f);

                var component = root.AddComponent<GardenGnomeDecoy>();
                var serialized = new SerializedObject(component);
                serialized.FindProperty("_visual").objectReferenceValue = model.transform;
                serialized.FindProperty("_ceramicColor").colorValue = ceramic;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var saved = PrefabUtility.SaveAsPrefabAsset(root, DecoyPath);
                Object.DestroyImmediate(root);
                Debug.Log($"{LogPrefix} Built {DecoyPath} (capsule r {radius:0.00} h {height:0.00}).");
                return saved;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(gnome);
            }
        }

        // The decoy is pure scenery: only Transform, Animator and renderers survive.
        private static void StripBehaviours(GameObject model)
        {
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
                Object.DestroyImmediate(behaviour);
            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
        }

        private static void Place(GameObject decoy, float radius, float height)
        {
            var scene = EditorSceneManager.GetSceneByPath(ScenePath);
            var openedHere = !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            else if (scene.isDirty)
            {
                Debug.LogWarning($"{LogPrefix} {ScenePath} has unsaved changes; save it and run again.");
                return;
            }

            GameObject root = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == RootName)
                    root = go;
            }

            if (root != null)
                Object.DestroyImmediate(root);
            root = new GameObject(RootName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);

            var ground = LayerMask.GetMask("Environment");
            var blockers = LayerMask.GetMask("Environment", "Player", "Gnome", "Item", "Occluder");
            var placed = new List<Vector3>();
            foreach (var anchor in Anchors)
            {
                if (!TryFindSpot(anchor, radius, height, ground, blockers, placed, out var spot))
                {
                    Debug.LogWarning($"{LogPrefix} No free spot near ({anchor.x}, {anchor.y}).");
                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(decoy, scene);
                instance.transform.SetParent(root.transform, true);
                var yaw = Mathf.Repeat(spot.x * 53f + spot.z * 97f, 360f);
                instance.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));
                placed.Add(spot);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"{LogPrefix} Placed {placed.Count} decoys in {ScenePath}.");
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }

        // Rings around the anchor: the first grounded spot where a gnome-sized capsule touches nothing.
        private static bool TryFindSpot(Vector2 anchor, float radius, float height, int ground, int blockers, List<Vector3> placed, out Vector3 spot)
        {
            for (var ring = 1; ring <= 3; ring++)
            {
                var distance = SearchRadius * ring * 0.6f;
                for (var i = 0; i < SearchSteps; i++)
                {
                    var angle = i * Mathf.PI * 2f / SearchSteps;
                    var x = anchor.x + Mathf.Cos(angle) * distance;
                    var z = anchor.y + Mathf.Sin(angle) * distance;
                    if (!Physics.Raycast(new Vector3(x, 20f, z), Vector3.down, out var hit, 40f, ground, QueryTriggerInteraction.Ignore))
                        continue;
                    // Only bare ground: not the top of a mushroom, rock or log (the terrain, when the level has one).
                    if (hit.normal.y < 0.85f || (Terrain.activeTerrains.Length > 0 && !(hit.collider is TerrainCollider)))
                        continue;

                    var feet = hit.point;
                    var bottom = feet + Vector3.up * (radius + 0.03f);
                    var top = feet + Vector3.up * Mathf.Max(radius + 0.03f, height - radius);
                    if (Physics.CheckCapsule(bottom, top, radius, blockers, QueryTriggerInteraction.Ignore))
                        continue;

                    var tooClose = false;
                    foreach (var other in placed)
                        tooClose |= (other - feet).sqrMagnitude < 1f;
                    if (tooClose)
                        continue;

                    spot = feet;
                    return true;
                }
            }

            spot = default;
            return false;
        }
    }
}
