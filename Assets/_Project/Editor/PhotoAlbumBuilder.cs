// Editor-only tool (C8.3). Menu GnoMore/Añadir álbum de fotos al campista: idempotently adds PhotoAlbum to the Camper prefab.
using BetterTogether.Gaze;
using UnityEditor;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static class PhotoAlbumBuilder
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Camper.prefab";
        private const string LogPrefix = "[PhotoAlbumBuilder]";

        [MenuItem("GnoMore/Añadir álbum de fotos al campista")]
        public static void Build()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var gaze = root.GetComponentInChildren<CamperGaze>(true);
                if (gaze == null)
                {
                    Debug.LogError($"{LogPrefix} No CamperGaze on {PrefabPath}.");
                    return;
                }

                var album = gaze.GetComponent<PhotoAlbum>();
                if (album == null)
                    album = gaze.gameObject.AddComponent<PhotoAlbum>();

                var serialized = new SerializedObject(album);
                serialized.FindProperty("_gaze").objectReferenceValue = gaze;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"{LogPrefix} PhotoAlbum ready on {PrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
