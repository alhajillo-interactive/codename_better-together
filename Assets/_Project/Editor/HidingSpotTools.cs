// Editor-only tool (C1.3). Menu GnoMore/Crear escondite en la selección: turns each selected bush or trunk into a hiding
// spot by adding a child "HidingSpot" with a NetworkObject (master client object), the HidingSpot component, a box collider
// on the Environment layer sized to the object's renderer bounds, and 3 slots inside it. It never touches other objects.
using BetterTogether.Interaction;
using Fusion;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterTogether.EditorTools
{
    public static class HidingSpotTools
    {
        private const string MenuPath = "GnoMore/Crear escondite en la selección";
        private const string ChildName = "HidingSpot";
        private const string LogPrefix = "[HidingSpotTools]";
        private const int SlotCount = 3;

        // Fraction of the bounds width between neighbouring slots, and how far below the centre the slots sit (fraction of height).
        private const float SlotSpread = 0.25f;
        private const float SlotDrop = 0.15f;

        [MenuItem(MenuPath, true)]
        private static bool CanCreate() => Selection.gameObjects.Length > 0;

        [MenuItem(MenuPath)]
        public static void CreateOnSelection()
        {
            var layer = LayerMask.NameToLayer("Environment");
            if (layer < 0)
                Debug.LogWarning($"{LogPrefix} No layer named Environment; the colliders stay on Default.");

            var created = 0;
            foreach (var target in Selection.gameObjects)
            {
                if (!target.scene.IsValid())
                {
                    Debug.LogWarning($"{LogPrefix} {target.name} is not in a scene; skipped.", target);
                    continue;
                }

                if (target.transform.Find(ChildName) != null)
                {
                    Debug.Log($"{LogPrefix} {target.name} already has a hiding spot; skipped.", target);
                    continue;
                }

                if (!TryGetBounds(target, out var bounds))
                {
                    Debug.LogWarning($"{LogPrefix} {target.name} has no renderers to size the collider; skipped.", target);
                    continue;
                }

                Create(target, bounds, layer);
                created++;
            }

            Debug.Log($"{LogPrefix} Created {created} hiding spot(s).");
        }

        private static bool TryGetBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var renderer in target.GetComponentsInChildren<Renderer>())
            {
                if (!found)
                    bounds = renderer.bounds;
                else
                    bounds.Encapsulate(renderer.bounds);
                found = true;
            }

            return found;
        }

        private static void Create(GameObject target, Bounds bounds, int layer)
        {
            var spot = new GameObject(ChildName);
            Undo.RegisterCreatedObjectUndo(spot, "Crear escondite");
            spot.transform.SetParent(target.transform, false);
            spot.transform.SetPositionAndRotation(bounds.center, Quaternion.identity);
            if (layer >= 0)
                spot.layer = layer;

            // Fusion needs the object to be a master client object so the master arbitrates hiding and searching.
            var networkObject = spot.AddComponent<NetworkObject>();
            var flags = new SerializedObject(networkObject).FindProperty("Flags");
            if (flags != null)
            {
                flags.intValue |= (int)NetworkObjectFlags.MasterClientObject;
                flags.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            var hiding = spot.AddComponent<HidingSpot>();

            // The box is the bounds in world axes; dividing by the lossy scale keeps its world size under a scaled parent.
            var scale = spot.transform.lossyScale;
            var box = spot.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.size = new Vector3(Safe(bounds.size.x, scale.x), Safe(bounds.size.y, scale.y), Safe(bounds.size.z, scale.z));

            var slots = new Transform[SlotCount];
            for (var i = 0; i < SlotCount; i++)
            {
                var slot = new GameObject($"Slot{i + 1}").transform;
                slot.SetParent(spot.transform, false);
                var offset = (i - (SlotCount - 1) * 0.5f) * SlotSpread * bounds.size.x;
                slot.SetPositionAndRotation(bounds.center + new Vector3(offset, -bounds.size.y * SlotDrop, 0f), Quaternion.identity);
                slots[i] = slot;
            }

            var serialized = new SerializedObject(hiding);
            var slotsProperty = serialized.FindProperty("_slots");
            slotsProperty.arraySize = SlotCount;
            for (var i = 0; i < SlotCount; i++)
                slotsProperty.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager_MarkDirty(spot.scene);
        }

        private static float Safe(float size, float scale) => Mathf.Abs(scale) > 1e-4f ? size / Mathf.Abs(scale) : size;

        private static void EditorSceneManager_MarkDirty(Scene scene) => UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
}
