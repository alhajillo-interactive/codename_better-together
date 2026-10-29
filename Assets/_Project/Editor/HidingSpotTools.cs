// Editor-only tool (C1.3). Menu GnoMore/Crear escondite en la selección: turns each selected bush or trunk into a hiding
// spot by adding a child "HidingSpot" with a NetworkObject (master client object), the HidingSpot component, a box collider
// on the Environment layer sized to the object's renderer bounds, and 3 slots inside it. It never touches other objects.
using BetterTogether.Interaction;
using Fusion;
using UnityEditor;
using UnityEngine;

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

        // Lowest a slot may sit above the bottom of the bounds, in world metres.
        private const float MinSlotHeight = 0.25f;

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

                if (!HasRenderers(target))
                {
                    Debug.LogWarning($"{LogPrefix} {target.name} has no renderers to size the collider; skipped.", target);
                    continue;
                }

                Create(target, layer);
                created++;
            }

            Debug.Log($"{LogPrefix} Created {created} hiding spot(s).");
        }

        private static bool HasRenderers(GameObject target) => target.GetComponentInChildren<Renderer>() != null;

        // Bounds of every renderer in the local space of <space>, from the eight corners of each renderer's local bounds,
        // so a rotated or scaled parent still gives a tight box.
        private static Bounds LocalBounds(GameObject target, Transform space)
        {
            var bounds = new Bounds();
            var first = true;
            foreach (var renderer in target.GetComponentsInChildren<Renderer>())
            {
                var local = renderer.localBounds;
                for (var i = 0; i < 8; i++)
                {
                    var sign = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                    var corner = local.center + Vector3.Scale(local.extents, sign);
                    var point = space.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first)
                        bounds = new Bounds(point, Vector3.zero);
                    else
                        bounds.Encapsulate(point);
                    first = false;
                }
            }

            return bounds;
        }

        private static void Create(GameObject target, int layer)
        {
            var spot = new GameObject(ChildName);
            Undo.RegisterCreatedObjectUndo(spot, "Crear escondite");
            spot.transform.SetParent(target.transform, false);
            if (layer >= 0)
                spot.layer = layer;

            // Fusion needs the object to be a master client object so the master arbitrates hiding and searching.
            // The inspector clears these two flags for master client objects, so do the same.
            var networkObject = spot.AddComponent<NetworkObject>();
            var flags = new SerializedObject(networkObject).FindProperty("Flags");
            if (flags != null)
            {
                flags.intValue |= (int)NetworkObjectFlags.MasterClientObject;
                flags.intValue &= ~(int)NetworkObjectFlags.AllowStateAuthorityOverride;
                flags.intValue &= ~(int)NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
                flags.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            var hiding = spot.AddComponent<HidingSpot>();

            // The spot shares the target's axes (local identity), so the box is measured in that space under any parent.
            var bounds = LocalBounds(target, spot.transform);
            var box = spot.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.center = bounds.center;
            box.size = bounds.size;

            // Slots sit below the centre but never lower than 25 cm above the bottom (measured in world metres).
            var scaleY = Mathf.Max(1e-4f, Mathf.Abs(spot.transform.lossyScale.y));
            var minY = bounds.min.y + MinSlotHeight / scaleY;
            var slotY = Mathf.Min(Mathf.Max(bounds.center.y - bounds.size.y * SlotDrop, minY), bounds.max.y);
            var slots = new Transform[SlotCount];
            for (var i = 0; i < SlotCount; i++)
            {
                var slot = new GameObject($"Slot{i + 1}").transform;
                slot.SetParent(spot.transform, false);
                slot.localPosition = new Vector3(bounds.center.x + (i - (SlotCount - 1) * 0.5f) * SlotSpread * bounds.size.x, slotY, bounds.center.z);
                slots[i] = slot;
            }

            var serialized = new SerializedObject(hiding);
            var slotsProperty = serialized.FindProperty("_slots");
            slotsProperty.arraySize = SlotCount;
            for (var i = 0; i < SlotCount; i++)
                slotsProperty.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(spot.scene);
        }
    }
}
