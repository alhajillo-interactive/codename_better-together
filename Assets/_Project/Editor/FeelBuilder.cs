// Editor-only tool (C2.1). Menu GnoMore/Construir feel de fogata y cubeta: adds the particle systems, the point light and the
// hiss loop to the Fireplace prefab and the particle systems to the Items/Bucket prefab, creates the URP particle materials
// and the provisional sound cues they use, and wires the serialized references of FireplaceManager and WaterBucket.
// Idempotent: a child that already exists (by name) is left as the designer tuned it, a material or cue that exists is reused,
// and references that already point somewhere are not overwritten.
using System;
using BetterTogether.Audio;
using BetterTogether.Fireplace;
using UnityEditor;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static class FeelBuilder
    {
        private const string MenuPath = "GnoMore/Construir feel de fogata y cubeta";
        private const string LogPrefix = "[FeelBuilder]";
        private const string FireplacePrefab = "Assets/_Project/Prefabs/Fireplace.prefab";
        private const string BucketPrefab = "Assets/_Project/Prefabs/Items/Bucket.prefab";
        private const string MaterialFolder = "Assets/_Project/Materials/Feel";
        private const string SoftMaterialPath = MaterialFolder + "/FeelSoft.mat";
        private const string AddMaterialPath = MaterialFolder + "/FeelAdd.mat";
        private const string CueFolder = "Assets/_Project/Audio/SFX";
        private const string WaterClipPath = "Assets/_Project/Audio/SFX/Clips/cascada.wav";
        private const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";

        private static readonly Color Ember = new(1f, 0.55f, 0.15f);
        private static readonly Color EmberDark = new(0.55f, 0.08f, 0.02f);
        private static readonly Color Water = new(0.65f, 0.85f, 1f);
        private static readonly Color Smoke = new(0.28f, 0.28f, 0.3f);
        private static readonly Color Steam = new(0.92f, 0.95f, 1f);

        [MenuItem(MenuPath)]
        public static void Build()
        {
            var soft = GetMaterial(SoftMaterialPath, false);
            var add = GetMaterial(AddMaterialPath, true);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(WaterClipPath);
            // Provisional sounds: the only water clip in the project, pitched and trimmed per use. The audio team replaces them.
            var fillCue = GetCue("SFX_BucketFill", clip, 0.7f, new Vector2(0.95f, 1.15f));
            var emptyCue = GetCue("SFX_BucketEmpty", clip, 0.6f, new Vector2(0.75f, 0.9f));
            var pourCue = GetCue("SFX_FirePour", clip, 0.5f, new Vector2(1.3f, 1.5f));
            var extinguishCue = GetCue("SFX_FireExtinguish", clip, 0.8f, new Vector2(1.5f, 1.7f));

            BuildFireplace(soft, add, clip, pourCue, extinguishCue);
            BuildBucket(soft, fillCue, emptyCue);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"{LogPrefix} Fireplace and Bucket feel built (safe to run again).");
        }

        // ---------------------------------------------------------------- Fireplace

        private static void BuildFireplace(Material soft, Material add, AudioClip hissClip, SoundCue pourCue, SoundCue extinguishCue)
        {
            var root = PrefabUtility.LoadPrefabContents(FireplacePrefab);
            try
            {
                var manager = root.GetComponentInChildren<FireplaceManager>(true);
                if (manager == null)
                {
                    Debug.LogError($"{LogPrefix} No FireplaceManager in {FireplacePrefab}.");
                    return;
                }

                var t = root.transform;
                var embers = GetSystem(t, "FeelEmbers", new Vector3(0f, 0.12f, 0f), Up, ps =>
                {
                    Setup(ps, add, loop: true, playOnAwake: true, rate: 3f, life: new Vector2(1.2f, 2.4f), speed: new Vector2(0.2f, 0.6f),
                        size: new Vector2(0.03f, 0.07f), gravity: -0.05f, world: true);
                    Shape(ps, ParticleSystemShapeType.Circle, 0.25f, 10f);
                    ColorOverLife(ps, Ember, EmberDark, 0.9f);
                });
                var spark = GetSystem(t, "FeelIgnitionSpark", new Vector3(0f, 0.15f, 0f), Up, ps =>
                {
                    Setup(ps, add, loop: false, playOnAwake: false, rate: 0f, life: new Vector2(0.4f, 0.9f), speed: new Vector2(1.5f, 3.2f),
                        size: new Vector2(0.03f, 0.08f), gravity: 0.4f, world: true, duration: 0.2f);
                    Shape(ps, ParticleSystemShapeType.Cone, 0.15f, 28f);
                    Burst(ps, 18);
                    ColorOverLife(ps, new Color(1f, 0.85f, 0.4f), Ember, 1f);
                });
                var smoke = GetSystem(t, "FeelSmokePuff", new Vector3(0f, 0.3f, 0f), Up, ps =>
                {
                    Setup(ps, soft, loop: false, playOnAwake: false, rate: 0f, life: new Vector2(1.4f, 2.4f), speed: new Vector2(0.5f, 1.1f),
                        size: new Vector2(0.35f, 0.6f), gravity: -0.08f, world: true, duration: 0.3f);
                    Shape(ps, ParticleSystemShapeType.Cone, 0.2f, 18f);
                    Burst(ps, 14);
                    ColorOverLife(ps, Smoke, Smoke, 0.6f);
                    SizeOverLife(ps, 0.5f, 1.8f);
                });
                var steam = GetSystem(t, "FeelSteam", new Vector3(0f, 0.4f, 0f), Up, ps =>
                {
                    Setup(ps, soft, loop: true, playOnAwake: true, rate: 0f, life: new Vector2(0.9f, 1.6f), speed: new Vector2(0.8f, 1.4f),
                        size: new Vector2(0.3f, 0.55f), gravity: -0.1f, world: true);
                    Shape(ps, ParticleSystemShapeType.Cone, 0.25f, 14f);
                    ColorOverLife(ps, Steam, Steam, 0.4f);
                    SizeOverLife(ps, 0.6f, 1.6f);
                });

                var light = GetLight(t);
                var hiss = GetHiss(t, hissClip);

                var so = new SerializedObject(manager);
                Wire(so, "_embers", embers);
                Wire(so, "_ignitionSpark", spark);
                Wire(so, "_smokePuff", smoke);
                Wire(so, "_steam", steam);
                Wire(so, "_fireLight", light);
                Wire(so, "_hissLoop", hiss);
                Wire(so, "_pourSfx", pourCue);
                Wire(so, "_extinguishSfx", extinguishCue);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, FireplacePrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---------------------------------------------------------------- Bucket

        private static void BuildBucket(Material soft, SoundCue fillCue, SoundCue emptyCue)
        {
            var root = PrefabUtility.LoadPrefabContents(BucketPrefab);
            try
            {
                var bucket = root.GetComponentInChildren<WaterBucket>(true);
                if (bucket == null)
                {
                    Debug.LogError($"{LogPrefix} No WaterBucket in {BucketPrefab}.");
                    return;
                }

                var t = root.transform;
                var splash = GetSystem(t, "FeelSplash", new Vector3(0f, 0.28f, 0f), Up, ps =>
                {
                    Setup(ps, soft, loop: true, playOnAwake: true, rate: 0f, life: new Vector2(0.35f, 0.7f), speed: new Vector2(1f, 2.2f),
                        size: new Vector2(0.02f, 0.05f), gravity: 1.2f, world: true);
                    Shape(ps, ParticleSystemShapeType.Cone, 0.08f, 40f);
                    ColorOverLife(ps, Water, Water, 0.9f);
                });
                var drips = GetSystem(t, "FeelDrips", new Vector3(0.11f, 0.27f, 0f), Up, ps =>
                {
                    Setup(ps, soft, loop: true, playOnAwake: true, rate: 0f, life: new Vector2(0.7f, 1f), speed: new Vector2(0f, 0.1f),
                        size: new Vector2(0.02f, 0.035f), gravity: 1f, world: true);
                    Shape(ps, ParticleSystemShapeType.Sphere, 0.01f, 0f);
                    ColorOverLife(ps, Water, Water, 0.9f);
                });
                // Points along the transform's +Z; the bucket aims it at the fire every frame while it pours.
                var stream = GetSystem(t, "FeelPourStream", new Vector3(0f, 0.28f, 0.12f), Vector3.zero, ps =>
                {
                    Setup(ps, soft, loop: true, playOnAwake: true, rate: 0f, life: new Vector2(0.6f, 0.9f), speed: new Vector2(3f, 4.5f),
                        size: new Vector2(0.04f, 0.07f), gravity: 1f, world: true);
                    Shape(ps, ParticleSystemShapeType.Cone, 0.02f, 4f);
                    ColorOverLife(ps, Water, Water, 0.85f);
                });

                var so = new SerializedObject(bucket);
                Wire(so, "_model", t.Find("Model"));
                Wire(so, "_splash", splash);
                Wire(so, "_drips", drips);
                Wire(so, "_pourStream", stream);
                Wire(so, "_fillSfx", fillCue);
                Wire(so, "_emptySfx", emptyCue);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, BucketPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---------------------------------------------------------------- Helpers

        // Particle emitters point along their +Z; rotating -90 degrees around X makes them emit upwards.
        private static readonly Vector3 Up = new(-90f, 0f, 0f);

        // Only fills a reference that is still empty, so a designer's own assignment is never replaced.
        private static void Wire(SerializedObject so, string field, UnityEngine.Object value)
        {
            var property = so.FindProperty(field);
            if (property == null)
                Debug.LogError($"{LogPrefix} Field {field} not found on {so.targetObject.GetType().Name}.");
            else if (property.objectReferenceValue == null && value != null)
                property.objectReferenceValue = value;
        }

        private static ParticleSystem GetSystem(Transform parent, string name, Vector3 localPosition, Vector3 euler, Action<ParticleSystem> setup)
        {
            var existing = parent.Find(name);
            if (existing != null && existing.TryGetComponent<ParticleSystem>(out var found))
                return found;

            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.SetLocalPositionAndRotation(localPosition, Quaternion.Euler(euler));
            var ps = child.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            setup(ps);
            return ps;
        }

        private static Light GetLight(Transform parent)
        {
            var existing = parent.Find("FeelLight");
            if (existing != null && existing.TryGetComponent<Light>(out var found))
                return found;

            var child = new GameObject("FeelLight");
            child.transform.SetParent(parent, false);
            child.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            var light = child.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.62f, 0.28f);
            light.shadows = LightShadows.None;
            light.intensity = 0f;
            light.range = 1f;
            light.enabled = false; // FireplaceManager drives it from the fire state.
            return light;
        }

        private static AudioSource GetHiss(Transform parent, AudioClip clip)
        {
            var existing = parent.Find("FeelHiss");
            if (existing != null && existing.TryGetComponent<AudioSource>(out var found))
                return found;

            var child = new GameObject("FeelHiss");
            child.transform.SetParent(parent, false);
            child.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            var source = child.AddComponent<AudioSource>();
            source.clip = clip;
            source.playOnAwake = false;
            source.loop = true;
            source.volume = 0f;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 14f;
            return source;
        }

        private static void Setup(ParticleSystem ps, Material material, bool loop, bool playOnAwake, float rate, Vector2 life, Vector2 speed,
            Vector2 size, float gravity, bool world, float duration = 5f)
        {
            var main = ps.main;
            main.duration = duration;
            main.loop = loop;
            main.playOnAwake = playOnAwake;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.gravityModifier = gravity;
            main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.maxParticles = 300;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void Shape(ParticleSystem ps, ParticleSystemShapeType type, float radius, float angle)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = type;
            shape.radius = radius;
            shape.angle = angle;
        }

        private static void Burst(ParticleSystem ps, short count)
        {
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        }

        private static void ColorOverLife(ParticleSystem ps, Color from, Color to, float peakAlpha)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, 0.12f), new GradientAlphaKey(0f, 1f) });
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private static void SizeOverLife(ParticleSystem ps, float start, float end)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, start, 1f, end));
        }

        private static Material GetMaterial(string path, bool additive)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            var shader = Shader.Find(ParticleShader);
            if (shader == null)
            {
                Debug.LogError($"{LogPrefix} Shader {ParticleShader} not found (is URP installed?).");
                return null;
            }

            EnsureFolder(MaterialFolder);
            var material = new Material(shader) { name = additive ? "FeelAdd" : "FeelSoft" };
            material.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static SoundCue GetCue(string name, AudioClip clip, float volume, Vector2 pitch)
        {
            var path = $"{CueFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
            if (existing != null)
                return existing;

            var cue = ScriptableObject.CreateInstance<SoundCue>();
            cue.Sound = clip;
            cue.Volume = volume;
            cue.PitchRange = pitch;
            cue.MaxDistance = 15f;
            AssetDatabase.CreateAsset(cue, path);
            return cue;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
