// Editor-only (C9.7). Part of AbrazoLevelBuilder: El Abrazo has no bucket (water balloons replace it), and the menu item
// GnoMore/Añadir bombas de agua al Gnome idempotently adds BalloonThrower to Prefabs/Gnome.prefab and wires its shader.
// It never touches scenes other than the one being built.
using BetterTogether.Fireplace;
using UnityEditor;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static partial class AbrazoLevelBuilder
    {
        private const string GnomePrefabPath = "Assets/_Project/Prefabs/Gnome.prefab";

        // Removes the "Bucket" of the scene being built; quiet when it is already gone (the build can run again).
        private static void RemoveBucket()
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t != null && t.name == "Bucket" && t.gameObject.scene == _scene)
                {
                    Object.DestroyImmediate(t.gameObject);
                    return;
                }
            }
        }

        [MenuItem("GnoMore/Añadir bombas de agua al Gnome")]
        public static void AddBalloonThrowerToGnome()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GnomePrefabPath) == null)
            {
                Debug.LogWarning($"{LogPrefix} {GnomePrefabPath} not found; skipped.");
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(GnomePrefabPath);
            try
            {
                var thrower = contents.GetComponent<BalloonThrower>();
                if (thrower == null)
                    thrower = contents.AddComponent<BalloonThrower>();

                // A serialized reference keeps the unlit shader in builds (Shader.Find alone could be stripped).
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                var serialized = new SerializedObject(thrower);
                var property = serialized.FindProperty("_shader");
                if (property != null && shader != null)
                {
                    property.objectReferenceValue = shader;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(contents, GnomePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} BalloonThrower is on {GnomePrefabPath} (safe to run again).");
        }
    }
}
