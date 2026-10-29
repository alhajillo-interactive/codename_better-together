// Editor-only tool (C2.6). Menu GnoMore/Construir feel de día y noche: builds (idempotently) the night Volume profile
// Assets/_Project/Settings/Volumes/Night.asset (cold white balance, lower exposure, desaturation), a global "NightVolume"
// in the open scene, and a DayNightFeel on the DayNightCycle object wired to it. Re-running refills the same assets.
using BetterTogether.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BetterTogether.EditorTools
{
    public static class DayNightFeelBuilder
    {
        private const string MenuPath = "GnoMore/Construir feel de día y noche";
        private const string FolderPath = "Assets/_Project/Settings/Volumes";
        private const string ProfilePath = FolderPath + "/Night.asset";
        private const string VolumeName = "NightVolume";
        private const string LogPrefix = "[DayNightFeelBuilder]";

        [MenuItem(MenuPath)]
        public static void Build()
        {
            var profile = BuildProfile();

            var cycle = Object.FindAnyObjectByType<DayNightCycle>();
            if (cycle == null)
            {
                Debug.LogError($"{LogPrefix} No DayNightCycle in the open scene; open CampBryan first. The profile was saved anyway.");
                return;
            }

            var volume = FindOrCreateVolume(profile);

            var feel = cycle.GetComponent<DayNightFeel>();
            if (feel == null)
                feel = Undo.AddComponent<DayNightFeel>(cycle.gameObject);

            var so = new SerializedObject(feel);
            so.FindProperty("_volume").objectReferenceValue = volume;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(feel);
            EditorUtility.SetDirty(volume);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(cycle.gameObject.scene);
            Debug.Log($"{LogPrefix} Night volume and DayNightFeel built. Save the scene.");
        }

        private static VolumeProfile BuildProfile()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Settings/Volumes"))
                AssetDatabase.CreateFolder("Assets/_Project/Settings", "Volumes");

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            if (!profile.TryGet(out WhiteBalance balance))
                balance = profile.Add<WhiteBalance>(true);
            balance.active = true;
            balance.temperature.Override(-28f);
            balance.tint.Override(8f);

            if (!profile.TryGet(out ColorAdjustments adjustments))
                adjustments = profile.Add<ColorAdjustments>(true);
            adjustments.active = true;
            adjustments.postExposure.Override(-0.9f);
            adjustments.saturation.Override(-18f);
            adjustments.contrast.Override(8f);
            adjustments.colorFilter.Override(new Color(0.78f, 0.86f, 1f));

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static Volume FindOrCreateVolume(VolumeProfile profile)
        {
            Volume volume = null;
            foreach (var candidate in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.gameObject.name == VolumeName)
                {
                    volume = candidate;
                    break;
                }
            }

            if (volume == null)
            {
                var go = new GameObject(VolumeName);
                Undo.RegisterCreatedObjectUndo(go, "Create night volume");
                volume = go.AddComponent<Volume>();
            }

            volume.isGlobal = true;
            volume.priority = 10f;
            volume.weight = 0f;
            volume.sharedProfile = profile;
            return volume;
        }
    }
}
