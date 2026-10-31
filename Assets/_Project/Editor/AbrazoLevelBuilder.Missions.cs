// Side missions and per-match variation of El Abrazo (C11): the spots each item can use, the places to photograph, the butterflies
// and the extra gnome spawns. Every spot is measured against the finished level: a spot rests on the terrain or on a walkable
// structure, away from vegetation and props. One that fails moves to the nearest place that passes, or is logged and left out.
using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Networking;
using UnityEditor;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static partial class AbrazoLevelBuilder
    {
        private const float ButterflyHeight = 1.1f;
        private const float ButterflyColliderRadius = 0.45f;
        private const float WingSize = 0.3f;
        private const string ButterflyHex = "#F2A31B";
        private const string ButterflyCue = "Assets/_Project/Audio/SFX/SFX_ButterflyCatch.asset";

        private static readonly Vector3 SpotProbeOffset = Vector3.up * 0.5f;
        private const float SpotProbeRadius = 0.3f;
        private const float SubjectProbeRadius = 0.15f;
        private const float SpotSearchRadius = 5f;
        private const float SpotSearchStep = 0.75f;
        private const int SpotSearchAngles = 12;
        private const float SpotSeparation = 1.5f;
        private const float ClutterMargin = 0.4f;
        private const float ClutterHeight = 1.4f;
        private const float ClutterMaxWidth = 10f;
        private const float SpotMinNormalY = 0.9f;

        // Colliders meant to be walked on; a spot on anything else (a bush, a rock, a tent) is not valid.
        private static readonly string[] WalkableSurfaces = { "Terreno", "Pasarela", "Terraza", "Hombro", "Tramo", "Muelle" };

        // Parts of the walkable structures themselves: an item may sit next to a railing.
        private static readonly string[] StructureParts = { "Baranda", "Poste", "Escalera", "Tronco" };

        private static readonly List<Bounds> Clutter = new();
        private static readonly List<Vector2> TakenSpots = new();

        // The first spot of each item is the one of the design: the sector split of 02-diseno-v2.md stays true in every match.
        private static readonly (string item, Vector2[] extra)[] ItemVariants =
        {
            ("Lena_Campamento", new[] { new Vector2(-2f, -34f), new Vector2(-14f, -27f) }),
            ("Lena_Huerto", new[] { new Vector2(-24f, -28f), new Vector2(-35f, -17f) }),
            ("Lena_Serreria", new[] { new Vector2(24f, -21f), new Vector2(34f, -8f) }),
            ("Lena_Risco", new[] { new Vector2(-27f, 13f), new Vector2(-36f, 16f) }),
            ("Lena_Hondonada", new[] { new Vector2(-9f, 27f), new Vector2(12f, 25f) }),
            ("Lena_Poza", new[] { new Vector2(14f, 20f), new Vector2(34f, 20f) }),
            ("Lena_Hombro", new[] { new Vector2(-8.5f, -7f) }),
            ("Fosforos_Hombro", new[] { new Vector2(-8.5f, -8.5f) }),
            ("Salchicha_Risco", new[] { new Vector2(-34f, 26f) }),
            ("Salchicha_Muelle", new[] { new Vector2(32f, 28f) }),
        };

        private static readonly Vector2[] ButterflySpots =
        {
            new(10f, -20f), new(-18f, -30f), new(-36f, -27f), new(-22f, -16f), new(36f, -20f), new(22f, -8f),
            new(-26f, 8f), new(-36f, 31f), new(-10f, 25f), new(14f, 18f), new(12f, 34f), new(36f, 18f),
        };

        // Placed between the three spawns of the design: a match uses every other point, so gnomes stay far from each other.
        private static readonly Vector2[] ExtraGnomeSpawns = { new(-24f, 28f), new(18f, 33f), new(37f, -2f) };

        private static void ReserveMissionSpots()
        {
            foreach (var (_, extra) in ItemVariants)
                KeyPoints.AddRange(extra);

            KeyPoints.AddRange(ButterflySpots);
            KeyPoints.AddRange(ExtraGnomeSpawns);
        }

        private static void BuildMissions(Transform parent)
        {
            var layoutObject = new GameObject("Layout");
            layoutObject.transform.SetParent(parent, false);
            var layout = layoutObject.AddComponent<MissionLayout>();
            var serialized = new SerializedObject(layout);

            CollectClutter();
            TakenSpots.Clear();
            FillItems(serialized.FindProperty("_items"));
            FillSubjects(serialized.FindProperty("_northSubjects"), parent,
                ("Foto de la Poza", PozaCenter, 1f, 22f),
                ("Foto del Risco", new Vector2(-30f, 18f), 1.8f, 18f),
                ("Foto de la Hondonada", new Vector2(0f, 26f), 1.6f, 18f));
            FillSubjects(serialized.FindProperty("_southSubjects"), parent,
                ("Foto de la fogata", FireplaceSpot, 1.8f, 16f),
                ("Foto de la jaula", CageSpot, 2.8f, 16f),
                ("Foto de la Serrería", new Vector2(28f, -14f), 2.8f, 18f),
                ("Foto del Huerto", new Vector2(-28f, -20f), 1.6f, 18f));
            FillButterflies(serialized.FindProperty("_butterflies"), parent);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            ExtendGnomeSpawns();
        }

        private static void FillItems(SerializedProperty items)
        {
            items.arraySize = ItemVariants.Length;
            for (var i = 0; i < ItemVariants.Length; i++)
            {
                var (name, extra) = ItemVariants[i];
                var entry = items.GetArrayElementAtIndex(i);
                var go = FindInScene(name);
                var item = go != null ? go.GetComponent<Carryable>() : null;
                entry.FindPropertyRelative("Item").objectReferenceValue = item;
                var spots = entry.FindPropertyRelative("Spots");
                if (item == null)
                {
                    Debug.LogWarning($"{LogPrefix} Missing item {name}.");
                    spots.arraySize = 0;
                    continue;
                }

                var home = go.transform.position;
                var valid = new List<Vector3>();
                if (FindClearSpot(new Vector2(home.x, home.z), out var clearHome))
                {
                    PlaceOnGround(go.transform, clearHome.x, clearHome.y, 0f, false);
                    home = go.transform.position;
                }
                else
                {
                    Debug.LogWarning($"{LogPrefix} No clear spot near the home of {name}; it stays at {home}.");
                }

                valid.Add(home);
                foreach (var spot in extra)
                {
                    if (!FindClearSpot(spot, out var clear))
                    {
                        Debug.LogWarning($"{LogPrefix} No clear spot near {spot} for {name}; left out.");
                        continue;
                    }

                    PlaceOnGround(go.transform, clear.x, clear.y, 0f, false);
                    valid.Add(go.transform.position);
                }

                go.transform.position = home;
                spots.arraySize = valid.Count;
                for (var s = 0; s < valid.Count; s++)
                    spots.GetArrayElementAtIndex(s).vector3Value = valid[s];
            }
        }

        // Every renderer counts, not only what blocks: many bushes have no solid collider.
        private static void CollectClutter()
        {
            Clutter.Clear();
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (renderer.gameObject.scene != _scene || renderer.GetComponentInParent<Carryable>() != null)
                    continue;

                var bounds = renderer.bounds;
                if (SurfaceKind(renderer.name) < 0 && !IsStructurePart(renderer.name) && Mathf.Max(bounds.size.x, bounds.size.z) <= ClutterMaxWidth)
                    Clutter.Add(bounds);
            }
        }

        private static bool IsStructurePart(string objectName)
        {
            for (var i = 0; i < StructureParts.Length; i++)
            {
                if (objectName.StartsWith(StructureParts[i]))
                    return true;
            }

            return false;
        }

        private static int SurfaceKind(string objectName)
        {
            for (var i = 0; i < WalkableSurfaces.Length; i++)
            {
                if (objectName.StartsWith(WalkableSurfaces[i]))
                    return i;
            }

            return -1;
        }

        // The spot itself when it passes, otherwise the nearest that does, on the same surface when the spot is on a walkable one:
        // a log of the design on a footbridge must not end up on the ground below.
        private static bool FindClearSpot(Vector2 spot, out Vector2 clear)
        {
            var kind = TryGround(spot.x, spot.y, null, out var origin) ? SurfaceKind(origin.collider.name) : -1;
            for (var radius = 0f; radius <= SpotSearchRadius; radius += SpotSearchStep)
            {
                for (var i = 0; i < (radius > 0f ? SpotSearchAngles : 1); i++)
                {
                    var angle = i * Mathf.PI * 2f / SpotSearchAngles;
                    clear = spot + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    if (!SpotIsFree(clear, kind))
                        continue;

                    TakenSpots.Add(clear);
                    return true;
                }
            }

            clear = spot;
            return false;
        }

        private static bool SpotIsFree(Vector2 spot, int requiredKind)
        {
            if (!TryGround(spot.x, spot.y, null, out var hit))
                return false;

            var kind = SurfaceKind(hit.collider.name);
            if (kind < 0 || (requiredKind >= 0 && kind != requiredKind) || hit.normal.y < SpotMinNormalY || hit.point.y <= WaterTopY + 0.1f)
                return false;

            if (Physics.CheckSphere(hit.point + SpotProbeOffset, SpotProbeRadius, _groundMask | (1 << _layerOccluder), QueryTriggerInteraction.Ignore))
                return false;

            for (var i = 0; i < TakenSpots.Count; i++)
            {
                if ((TakenSpots[i] - spot).sqrMagnitude < SpotSeparation * SpotSeparation)
                    return false;
            }

            for (var i = 0; i < Clutter.Count; i++)
            {
                var bounds = Clutter[i];
                if (spot.x > bounds.min.x - ClutterMargin && spot.x < bounds.max.x + ClutterMargin
                    && spot.y > bounds.min.z - ClutterMargin && spot.y < bounds.max.z + ClutterMargin
                    && bounds.max.y > hit.point.y && bounds.min.y < hit.point.y + ClutterHeight)
                    return false;
            }

            return true;
        }

        private static void FillSubjects(SerializedProperty array, Transform parent, params (string label, Vector2 spot, float height, float distance)[] places)
        {
            array.arraySize = places.Length;
            for (var i = 0; i < places.Length; i++)
            {
                var (label, spot, height, distance) = places[i];
                var go = new GameObject(label);
                go.transform.SetParent(parent, false);
                go.transform.position = GroundPoint(spot) + Vector3.up * height;
                if (Physics.CheckSphere(go.transform.position, SubjectProbeRadius, _groundMask | (1 << _layerOccluder), QueryTriggerInteraction.Ignore))
                    Debug.LogWarning($"{LogPrefix} {label}: the point is inside scenery, so it can never be in view.");

                var subject = go.AddComponent<PhotoSubject>();
                var so = new SerializedObject(subject);
                so.FindProperty("_label").stringValue = label;
                so.FindProperty("_maxDistance").floatValue = distance;
                so.ApplyModifiedPropertiesWithoutUndo();
                array.GetArrayElementAtIndex(i).objectReferenceValue = subject;
            }
        }

        private static void FillButterflies(SerializedProperty array, Transform parent)
        {
            var material = Mat("Mariposa", ButterflyHex);
            var cue = AssetDatabase.LoadAssetAtPath<SoundCue>(ButterflyCue);
            var built = new List<ButterflySpot>();
            for (var i = 0; i < ButterflySpots.Length; i++)
            {
                if (!FindClearSpot(ButterflySpots[i], out var clear))
                {
                    Debug.LogWarning($"{LogPrefix} No clear spot near butterfly {ButterflySpots[i]}; left out.");
                    continue;
                }

                built.Add(BuildButterfly(parent, i, GroundPoint(clear) + Vector3.up * ButterflyHeight, material, cue));
            }

            array.arraySize = built.Count;
            for (var i = 0; i < built.Count; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = built[i];
        }

        private static ButterflySpot BuildButterfly(Transform parent, int index, Vector3 position, Material material, SoundCue cue)
        {
            var root = new GameObject($"Mariposa_{index + 1:00}") { layer = LayerMask.NameToLayer("NoCollision") };
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            var collider = root.AddComponent<SphereCollider>();
            collider.radius = ButterflyColliderRadius;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var left = Wing(visual.transform, "AlaIzquierda", -1f, material);
            var right = Wing(visual.transform, "AlaDerecha", 1f, material);

            var spot = root.AddComponent<ButterflySpot>();
            var so = new SerializedObject(spot);
            so.FindProperty("_visual").objectReferenceValue = visual;
            so.FindProperty("_collider").objectReferenceValue = collider;
            so.FindProperty("_catchSfx").objectReferenceValue = cue;
            var wings = so.FindProperty("_wings");
            wings.arraySize = 2;
            wings.GetArrayElementAtIndex(0).objectReferenceValue = left;
            wings.GetArrayElementAtIndex(1).objectReferenceValue = right;
            so.ApplyModifiedPropertiesWithoutUndo();
            return spot;
        }

        // Hinge at the body; a thin box instead of a quad so it shows from above and below.
        private static Transform Wing(Transform parent, string name, float side, Material material)
        {
            var hinge = new GameObject(name).transform;
            hinge.SetParent(parent, false);
            var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(wing.GetComponent<Collider>());
            wing.name = "Ala";
            wing.transform.SetParent(hinge, false);
            wing.transform.localPosition = new Vector3(side * WingSize * 0.5f, 0f, 0f);
            wing.transform.localScale = new Vector3(WingSize, 0.01f, WingSize * 0.8f);
            wing.GetComponent<Renderer>().sharedMaterial = material;
            return hinge;
        }

        private static void ExtendGnomeSpawns()
        {
            var first = FindInScene("GnomeSpawn1");
            var spawner = Object.FindFirstObjectByType<PlayerSpawner>(FindObjectsInactive.Include);
            if (first == null || spawner == null)
                return;

            // Odd positions are the extra points, so every-other picks give the three of the design or the three new ones.
            var points = new Object[GnomeSpawnSpots.Length + ExtraGnomeSpawns.Length];
            for (var i = 0; i < GnomeSpawnSpots.Length; i++)
                points[i * 2] = FindInScene($"GnomeSpawn{i + 1}").transform;

            for (var i = 0; i < ExtraGnomeSpawns.Length; i++)
            {
                var name = $"GnomeSpawn{GnomeSpawnSpots.Length + i + 1}";
                var go = FindInScene(name);
                if (go == null)
                {
                    go = new GameObject(name);
                    go.transform.SetParent(first.transform.parent, false);
                }

                if (!FindClearSpot(ExtraGnomeSpawns[i], out var spot))
                    Debug.LogWarning($"{LogPrefix} {name} at {spot} is blocked.");

                PlaceSpawn(name, spot, new Vector3(-spot.x, 0f, -spot.y));
                points[i * 2 + 1] = go.transform;
            }

            var so = new SerializedObject(spawner);
            var array = so.FindProperty("_gnomeSpawns");
            array.arraySize = points.Length;
            for (var i = 0; i < points.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
