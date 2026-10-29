// Editor-only tool (C4.5). Menu GnoMore/Construir jaula: idempotently (1) builds Prefabs/GnomeCage.prefab, a greybox cage
// with a master-client NetworkObject, the GnomeCage component, a solid collider on Environment and the Inside / Exit points,
// (2) adds GnomeCarrier (+ a GnomeCarryAnchor child) to the Camper prefab and (3) adds GnomeCapture to the Gnome prefab.
// It never touches scenes: placing the cage next to the tent is done by hand or by CLI.
using BetterTogether.Interaction;
using Fusion;
using UnityEditor;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static class CageBuilder
    {
        private const string CagePath = "Assets/_Project/Prefabs/GnomeCage.prefab";
        private const string MaterialPath = "Assets/_Project/Materials/GnomeCageGreybox.mat";
        private const string CamperPath = "Assets/_Project/Prefabs/Camper.prefab";
        private const string GnomePath = "Assets/_Project/Prefabs/Gnome.prefab";
        private const string AnchorName = "GnomeCarryAnchor";
        private const string LogPrefix = "[CageBuilder]";

        // Greybox dimensions in metres: a cage the camper can see into, big enough for a gnome to stand inside.
        private const float Width = 1.2f;
        private const float Height = 1f;
        private const float PostThickness = 0.06f;
        private const float BarThickness = 0.03f;
        private const int BarsPerSide = 7;

        // Where the carried gnome's feet are held, relative to the camper root (in front of the chest).
        private static readonly Vector3 CarryAnchorOffset = new(0f, 0.95f, 0.6f);

        [MenuItem("GnoMore/Construir jaula")]
        public static void Build()
        {
            BuildCage();
            AddToPrefab<GnomeCarrier>(CamperPath, true);
            AddToPrefab<GnomeCapture>(GnomePath, false);
            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} Done. Place {CagePath} next to the tent and add the Gnome layer to the camper's interaction mask.");
        }

        private static void BuildCage()
        {
            var material = EnsureMaterial();
            var root = new GameObject("GnomeCage");
            try
            {
                var environment = LayerMask.NameToLayer("Environment");
                if (environment < 0)
                    Debug.LogWarning($"{LogPrefix} No layer named Environment; the cage stays on Default.");
                else
                    root.layer = environment;

                var networkObject = root.AddComponent<NetworkObject>();
                var flags = new SerializedObject(networkObject).FindProperty("Flags");
                if (flags != null)
                {
                    // Master client object, like the hiding spots; the inspector clears these two flags for it too.
                    flags.intValue |= (int)NetworkObjectFlags.MasterClientObject;
                    flags.intValue &= ~(int)NetworkObjectFlags.AllowStateAuthorityOverride;
                    flags.intValue &= ~(int)NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
                    flags.serializedObject.ApplyModifiedPropertiesWithoutUndo();
                }

                var cage = root.AddComponent<GnomeCage>();

                var half = Width * 0.5f;
                AddPart(root.transform, "Floor", new Vector3(0f, 0.025f, 0f), new Vector3(Width, 0.05f, Width), material);
                AddPart(root.transform, "Roof", new Vector3(0f, Height - 0.025f, 0f), new Vector3(Width, 0.05f, Width), material);
                foreach (var sx in new[] { -1f, 1f })
                {
                    foreach (var sz in new[] { -1f, 1f })
                        AddPart(root.transform, "Post", new Vector3(sx * (half - PostThickness * 0.5f), Height * 0.5f, sz * (half - PostThickness * 0.5f)),
                            new Vector3(PostThickness, Height, PostThickness), material);
                }

                var span = Width - PostThickness * 2f;
                for (var i = 0; i < BarsPerSide; i++)
                {
                    var along = -span * 0.5f + span * (i + 0.5f) / BarsPerSide;
                    AddPart(root.transform, "BarFront", new Vector3(along, Height * 0.5f, half - PostThickness * 0.5f), new Vector3(BarThickness, Height, BarThickness), material);
                    AddPart(root.transform, "BarBack", new Vector3(along, Height * 0.5f, -half + PostThickness * 0.5f), new Vector3(BarThickness, Height, BarThickness), material);
                    AddPart(root.transform, "BarLeft", new Vector3(-half + PostThickness * 0.5f, Height * 0.5f, along), new Vector3(BarThickness, Height, BarThickness), material);
                    AddPart(root.transform, "BarRight", new Vector3(half - PostThickness * 0.5f, Height * 0.5f, along), new Vector3(BarThickness, Height, BarThickness), material);
                }

                // One solid collider for the whole cage: players and the camper's cursor see a box, the gnome inside is placed by teleport.
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, Height * 0.5f, 0f);
                box.size = new Vector3(Width, Height, Width);

                var inside = new GameObject("Inside").transform;
                inside.SetParent(root.transform, false);
                inside.localPosition = new Vector3(0f, 0.05f, 0f);
                var exit = new GameObject("Exit").transform;
                exit.SetParent(root.transform, false);
                exit.localPosition = new Vector3(0f, 0f, half + 0.5f);
                exit.localRotation = Quaternion.identity;

                var serialized = new SerializedObject(cage);
                serialized.FindProperty("_inside").objectReferenceValue = inside;
                serialized.FindProperty("_exit").objectReferenceValue = exit;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, CagePath);
                Debug.Log($"{LogPrefix} Saved {CagePath}.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AddPart(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.layer = parent.gameObject.layer;
            Object.DestroyImmediate(part.GetComponent<Collider>()); // The root's box is the only collider.
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Material EnsureMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
                return material;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning($"{LogPrefix} URP Lit shader not found; the cage material may render pink.");
                shader = Shader.Find("Standard");
            }

            material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", new Color(0.35f, 0.3f, 0.28f));
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        // Adds the component to the prefab root if missing; for the camper also creates and assigns the carry anchor.
        private static void AddToPrefab<T>(string path, bool withAnchor) where T : Component
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning($"{LogPrefix} {path} not found; skipped.");
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var component = contents.GetComponent<T>();
                if (component == null)
                    component = contents.AddComponent<T>();

                if (withAnchor)
                {
                    var anchor = contents.transform.Find(AnchorName);
                    if (anchor == null)
                    {
                        anchor = new GameObject(AnchorName).transform;
                        anchor.SetParent(contents.transform, false);
                        anchor.localPosition = CarryAnchorOffset;
                    }

                    var serialized = new SerializedObject(component);
                    var property = serialized.FindProperty("_carryAnchor");
                    if (property != null)
                    {
                        property.objectReferenceValue = anchor;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                Debug.Log($"{LogPrefix} {typeof(T).Name} ready on {path}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}
