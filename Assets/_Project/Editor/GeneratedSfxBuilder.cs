// Editor-only builder for the synthesised sounds (C10.2). Run Tools/generate-sfx.py first, then GnoMore/Construir sonidos generados:
// sets the import options of Audio/SFX/Clips/Generated and creates or refreshes one SoundCue per entry of the table below.
// Idempotent. Prefabs and scenes reference the cues directly; AssignScene wires the objects AbrazoLevelBuilder creates itself.
using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Environment;
using BetterTogether.Interaction;
using UnityEditor;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static class GeneratedSfxBuilder
    {
        private const string LogPrefix = "[GeneratedSfxBuilder]";
        private const string ClipFolder = "Assets/_Project/Audio/SFX/Clips/Generated";
        private const string CueFolder = "Assets/_Project/Audio/SFX";

        private readonly struct Entry
        {
            public readonly string Cue;
            public readonly string[] Clips;
            public readonly float Volume;
            public readonly bool Spatial;
            public readonly float MaxDistance;
            public readonly float MinInterval;
            public readonly float PitchSpread;

            public Entry(string cue, string clips, float volume, bool spatial, float maxDistance, float minInterval, float pitchSpread)
            {
                Cue = cue;
                Clips = clips.Split(' ');
                Volume = volume;
                Spatial = spatial;
                MaxDistance = maxDistance;
                MinInterval = minInterval;
                PitchSpread = pitchSpread;
            }
        }

        // The shutter and the cage carry far on purpose: both are information for the other team.
        private static readonly Entry[] Table =
        {
            new("SFX_PhotoShutter", "photo_shutter", 0.9f, true, 26f, 0.05f, 0.03f),
            new("SFX_PhotoRaise", "photo_raise", 0.5f, true, 10f, 0.1f, 0.04f),
            new("SFX_PhotoDry", "photo_dry", 0.7f, false, 20f, 0.15f, 0.03f),
            new("SFX_PhotoReady", "photo_ready", 0.6f, false, 20f, 0.1f, 0.02f),
            new("SFX_PhotoHit", "photo_hit", 0.75f, false, 20f, 0.1f, 0.02f),
            new("SFX_LockedTick", "locked_tick", 0.35f, false, 20f, 0.04f, 0.03f),
            new("SFX_BalloonFill", "balloon_fill", 0.65f, true, 14f, 0.1f, 0.05f),
            new("SFX_BalloonThrow", "balloon_throw", 0.6f, true, 16f, 0.1f, 0.08f),
            new("SFX_BalloonPop", "balloon_pop", 0.8f, true, 18f, 0.05f, 0.08f),
            new("SFX_BalloonSplash", "balloon_splash_a balloon_splash_b", 1f, true, 28f, 0.05f, 0.06f),
            new("SFX_WaterSplash", "water_splash_a water_splash_b", 0.75f, true, 20f, 0.1f, 0.08f),
            new("SFX_WaterWade", "water_wade_a water_wade_b water_wade_c", 0.35f, true, 12f, 0.14f, 0.1f),
            new("SFX_CageLock", "cage_lock", 1f, true, 34f, 0.2f, 0.03f),
            new("SFX_CageOpen", "cage_open", 0.9f, true, 30f, 0.2f, 0.03f),
            new("SFX_StoneShatter", "stone_shatter_a stone_shatter_b", 0.9f, true, 24f, 0.1f, 0.06f),
            new("SFX_CeramicClink", "ceramic_clink_a ceramic_clink_b", 0.6f, true, 14f, 0.08f, 0.06f),
            new("SFX_LandCamper", "land_camper_a land_camper_b", 0.55f, true, 14f, 0.1f, 0.06f),
            new("SFX_LandGnome", "land_gnome_a land_gnome_b", 0.45f, true, 10f, 0.1f, 0.08f),
            new("SFX_StrainCamper", "strain_camper", 0.4f, true, 10f, 0.25f, 0.06f),
            new("SFX_StrainGnome", "strain_gnome", 0.4f, true, 10f, 0.25f, 0.08f),
            new("SFX_GnomeMount", "gnome_mount", 0.55f, true, 12f, 0.1f, 0.08f),
            new("SFX_GnomeDismount", "gnome_dismount", 0.55f, true, 12f, 0.1f, 0.08f),
            new("SFX_GnomePing", "gnome_ping", 0.7f, true, 50f, 0.2f, 0.01f),
            new("SFX_ItemRespawn", "item_respawn", 0.55f, true, 16f, 0.1f, 0.05f),
            new("SFX_ImpactBucket", "impact_bucket", 0.6f, true, 16f, 0.1f, 0.08f),
            new("SFX_ImpactMatches", "impact_matches", 0.55f, true, 12f, 0.1f, 0.08f),
            new("SFX_ImpactSausage", "impact_sausage", 0.6f, true, 12f, 0.1f, 0.1f),
            new("SFX_BushHide", "bush_hide_a bush_hide_b", 0.55f, true, 14f, 0.15f, 0.08f),
            new("SFX_BushSearch", "bush_search_a bush_search_b", 0.8f, true, 18f, 0.2f, 0.08f),
            new("SFX_UiClick", "ui_click", 0.5f, false, 20f, 0.03f, 0.03f),
            new("SFX_UiHover", "ui_hover", 0.25f, false, 20f, 0.05f, 0.03f),
            new("SFX_LogHandOff", "log_handoff_a log_handoff_b", 0.6f, true, 14f, 0.08f, 0.07f),
            new("SFX_LogSnap", "log_snap", 0.8f, true, 20f, 0.05f, 0.05f),
            new("SFX_LogUnsnap", "log_unsnap", 0.65f, true, 16f, 0.05f, 0.05f),
            new("SFX_FireReady", "fire_ready", 0.8f, true, 28f, 0.2f, 0.02f),
            new("SFX_Watched", "watched", 0.55f, false, 20f, 0.3f, 0.02f),
            new("SFX_BreakPress", "break_press", 0.45f, false, 20f, 0.03f, 0.08f),
            new("SFX_BreakFree", "break_free", 0.7f, true, 18f, 0.1f, 0.03f),
        };

        [MenuItem("GnoMore/Construir sonidos generados")]
        public static void Build()
        {
            foreach (var entry in Table)
                BuildCue(entry);

            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} {Table.Length} cues ready.");
        }

        /// <summary>Wires the sounds of the objects the level builder creates in the open scene. Empty fields only; does not save.</summary>
        public static void AssignScene()
        {
            var hide = Load("SFX_BushHide");
            var search = Load("SFX_BushSearch");
            foreach (var spot in Object.FindObjectsByType<HidingSpot>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(spot);
                SetIfEmpty(so, "_hideSfx", hide);
                SetIfEmpty(so, "_searchSfx", search);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var splash = Load("SFX_WaterSplash");
            var wade = Load("SFX_WaterWade");
            foreach (var ripples in Object.FindObjectsByType<WaterRipples>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(ripples);
                SetIfEmpty(so, "_splashSfx", splash);
                SetIfEmpty(so, "_wadeSfx", wade);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void BuildCue(Entry entry)
        {
            var clips = new List<AudioClip>(entry.Clips.Length);
            foreach (var name in entry.Clips)
            {
                var path = $"{ClipFolder}/{name}.wav";
                ConfigureImport(path);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                    Debug.LogWarning($"{LogPrefix} Missing clip {path}: run Tools/generate-sfx.py.");
                else
                    clips.Add(clip);
            }

            var cuePath = $"{CueFolder}/{entry.Cue}.asset";
            var cue = AssetDatabase.LoadAssetAtPath<SoundCue>(cuePath);
            if (cue == null)
            {
                cue = ScriptableObject.CreateInstance<SoundCue>();
                AssetDatabase.CreateAsset(cue, cuePath);
            }

            cue.Sound = clips.Count == 1 ? clips[0] : null;
            cue.Variations = clips.Count > 1 ? clips.ToArray() : null;
            cue.Volume = entry.Volume;
            cue.PitchRange = new Vector2(1f - entry.PitchSpread, 1f + entry.PitchSpread);
            cue.Spatial = entry.Spatial;
            cue.MaxDistance = entry.MaxDistance;
            cue.MinInterval = entry.MinInterval;
            EditorUtility.SetDirty(cue);
        }

        private static void ConfigureImport(string path)
        {
            if (AssetImporter.GetAtPath(path) is not AudioImporter importer)
                return;

            var settings = importer.defaultSampleSettings;
            if (settings.loadType == AudioClipLoadType.DecompressOnLoad && settings.compressionFormat == AudioCompressionFormat.Vorbis)
                return;

            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        private static SoundCue Load(string cue) => AssetDatabase.LoadAssetAtPath<SoundCue>($"{CueFolder}/{cue}.asset");

        private static void SetIfEmpty(SerializedObject so, string field, Object value)
        {
            var property = so.FindProperty(field);
            if (property != null && property.objectReferenceValue == null)
                property.objectReferenceValue = value;
        }
    }
}
