// Editor-only tool (C2.2). Menu GnoMore/Construir feel de cámara: creates the two head-bob NoiseSettings assets
// (camper 2.5 cm @ 1.8 Hz, gnome 1 cm @ 3.5 Hz) and, in every open scene, makes sure each CinemachineCamera has a
// Basic Multi Channel Perlin and a CameraFeel wired to those assets. Idempotent: running it again only refreshes values.
using BetterTogether.Player;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterTogether.EditorTools
{
    public static class CameraFeelBuilder
    {
        private const string MenuPath = "GnoMore/Construir feel de cámara";
        private const string LogPrefix = "[CameraFeelBuilder]";
        private const string SettingsFolder = "Assets/_Project/Settings";
        private const string CameraFolder = SettingsFolder + "/Camera";
        private const string CamperPath = CameraFolder + "/CamperHeadBob.asset";
        private const string GnomePath = CameraFolder + "/GnomeHeadBob.asset";

        // Horizontal sway runs at half the step frequency and amplitude, which reads as a weight shift.
        private const float SwayRatio = 0.5f;

        [MenuItem(MenuPath)]
        public static void Build()
        {
            var camper = BuildProfile(CamperPath, 0.025f, 1.8f);
            var gnome = BuildProfile(GnomePath, 0.01f, 3.5f);
            AssetDatabase.SaveAssets();

            var cameras = 0;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var camera in root.GetComponentsInChildren<CinemachineCamera>(true))
                    {
                        // Only gameplay cameras: menu cameras have no FirstPersonCamera.
                        if (camera.GetComponent<BetterTogether.Player.FirstPersonCamera>() == null)
                            continue;
                        Setup(camera, camper, gnome);
                        EditorSceneManager.MarkSceneDirty(scene);
                        cameras++;
                    }
                }
            }

            Debug.Log($"{LogPrefix} Perfiles de bob listos en {CameraFolder}; {cameras} CinemachineCamera configuradas en las escenas abiertas.");
        }

        private static NoiseSettings BuildProfile(string path, float amplitude, float frequency)
        {
            if (!AssetDatabase.IsValidFolder(SettingsFolder))
                AssetDatabase.CreateFolder("Assets/_Project", "Settings");
            if (!AssetDatabase.IsValidFolder(CameraFolder))
                AssetDatabase.CreateFolder(SettingsFolder, "Camera");

            var profile = AssetDatabase.LoadAssetAtPath<NoiseSettings>(path);
            var isNew = profile == null;
            if (isNew)
                profile = ScriptableObject.CreateInstance<NoiseSettings>();

            var channel = new NoiseSettings.TransformNoiseParams();
            channel.Y.Frequency = frequency;
            channel.Y.Amplitude = amplitude;
            channel.X.Frequency = frequency * SwayRatio;
            channel.X.Amplitude = amplitude * SwayRatio;
            profile.PositionNoise = new[] { channel };
            profile.OrientationNoise = new NoiseSettings.TransformNoiseParams[0];

            if (isNew)
                AssetDatabase.CreateAsset(profile, path);
            else
                EditorUtility.SetDirty(profile);

            return profile;
        }

        private static void Setup(CinemachineCamera camera, NoiseSettings camper, NoiseSettings gnome)
        {
            var go = camera.gameObject;
            var noise = go.GetComponent<CinemachineBasicMultiChannelPerlin>();
            if (noise == null)
                noise = Undo.AddComponent<CinemachineBasicMultiChannelPerlin>(go);
            noise.NoiseProfile = camper;
            noise.AmplitudeGain = 0f;
            noise.FrequencyGain = 1f;
            EditorUtility.SetDirty(noise);

            var feel = go.GetComponent<CameraFeel>();
            if (feel == null)
                feel = Undo.AddComponent<CameraFeel>(go);

            var serialized = new SerializedObject(feel);
            serialized.FindProperty("_camperNoise").objectReferenceValue = camper;
            serialized.FindProperty("_gnomeNoise").objectReferenceValue = gnome;
            serialized.ApplyModifiedProperties();
        }
    }
}
