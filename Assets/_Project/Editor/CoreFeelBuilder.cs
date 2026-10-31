// Editor-only tool (C9.9). Menu GnoMore/Construir feel núcleo: idempotently builds the game-feel core package on the prefabs.
//  - Fireplace: "CoreFlare" particle system (flare burst when the fire is lit), wired to FireplaceManager._flare.
//  - Camper and Gnome: "SprintDust" and "SprintLeaves" particle systems and a SprintFeel component wired to them.
//  - MatchHUD: "SpeedLines" (10 wind strokes) + SpeedLinesHUD, and "PhotoHitFrame" (4 white bars) + PhotoHitFeel.
// A child that already exists (by name) is left as the designer tuned it; references are wired again every run.
// No dialogs, no scene edits: safe to call from the editor menu or from a CLI-driven editor (CoreFeelBuilder.Build()).
using System;
using BetterTogether.Fireplace;
using BetterTogether.Player;
using BetterTogether.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.EditorTools
{
    public static class CoreFeelBuilder
    {
        private const string LogPrefix = "[CoreFeelBuilder]";
        private const string FireplacePrefab = "Assets/_Project/Prefabs/Fireplace.prefab";
        private const string CamperPrefab = "Assets/_Project/Prefabs/Camper.prefab";
        private const string GnomePrefab = "Assets/_Project/Prefabs/Gnome.prefab";
        private const string HudPrefab = "Assets/_Project/Prefabs/UI/MatchHUD.prefab";
        private const string ThemePath = "Assets/_Project/Resources/UiTheme.asset";
        private const string MaterialFolder = "Assets/_Project/Materials/Feel";
        private const string SoftMaterialPath = MaterialFolder + "/FeelSoft.mat";
        private const string AddMaterialPath = MaterialFolder + "/FeelAdd.mat";
        private const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";
        private const int UiLayer = 5;
        private const int StrokeCount = 10;
        private const float FrameThickness = 8f;

        private static readonly Vector3 Up = new(-90f, 0f, 0f); // Particle emitters point along +Z; -90 around X is up.
        private static readonly Color Flame = new(1f, 0.62f, 0.2f);
        private static readonly Color FlameDark = new(0.6f, 0.12f, 0.03f);
        private static readonly Color Dust = new(0.78f, 0.7f, 0.58f);
        private static readonly Color Leaf = new(0.55f, 0.66f, 0.2f);
        private static readonly Color LeafDark = new(0.72f, 0.45f, 0.15f);

        [MenuItem("GnoMore/Construir feel núcleo")]
        public static void Build()
        {
            var soft = GetMaterial(SoftMaterialPath, false);
            var add = GetMaterial(AddMaterialPath, true);
            if (soft == null || add == null)
                return;

            BuildFireplace(add);
            BuildPlayer(CamperPrefab, soft);
            BuildPlayer(GnomePrefab, soft);
            BuildHud();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"{LogPrefix} Core feel built (safe to run again).");
        }

        // ---------------------------------------------------------------- Fireplace

        private static void BuildFireplace(Material add)
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

                var flare = GetSystem(root.transform, "CoreFlare", new Vector3(0f, 0.2f, 0f), Up, ps =>
                {
                    Setup(ps, add, life: new Vector2(0.25f, 0.55f), speed: new Vector2(1.2f, 2.8f), size: new Vector2(0.18f, 0.42f),
                        gravity: -0.2f, maxParticles: 64);
                    Shape(ps, ParticleSystemShapeType.Cone, 0.18f, 22f);
                    ColorOverLife(ps, Flame, FlameDark, 0.9f);
                    SizeOverLife(ps, 1f, 0.2f);
                });

                var so = new SerializedObject(manager);
                Wire(so, "_flare", flare);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, FireplacePrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---------------------------------------------------------------- Players

        private static void BuildPlayer(string path, Material soft)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var movement = root.GetComponentInChildren<PlayerMovement>(true);
                if (movement == null)
                {
                    Debug.LogError($"{LogPrefix} No PlayerMovement in {path}.");
                    return;
                }

                var dust = GetSystem(root.transform, "SprintDust", new Vector3(0f, 0.03f, 0f), Up, ps =>
                {
                    Setup(ps, soft, life: new Vector2(0.35f, 0.65f), speed: new Vector2(0.15f, 0.5f), size: new Vector2(0.1f, 0.14f),
                        gravity: -0.05f, maxParticles: 60);
                    Shape(ps, ParticleSystemShapeType.Cone, 0.05f, 30f);
                    ColorOverLife(ps, Dust, Dust, 0.45f);
                    SizeOverLife(ps, 0.6f, 1.8f);
                });
                var leaves = GetSystem(root.transform, "SprintLeaves", new Vector3(0f, 0.05f, 0f), Up, ps =>
                {
                    Setup(ps, soft, life: new Vector2(0.5f, 0.9f), speed: new Vector2(0.8f, 1.6f), size: new Vector2(0.05f, 0.08f),
                        gravity: 0.5f, maxParticles: 40);
                    Shape(ps, ParticleSystemShapeType.Cone, 0.08f, 35f);
                    var main = ps.main;
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    main.startColor = new ParticleSystem.MinMaxGradient(Leaf, LeafDark);
                    var spin = ps.rotationOverLifetime;
                    spin.enabled = true;
                    spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
                    ColorOverLife(ps, Color.white, Color.white, 0.9f);
                });

                var feel = movement.gameObject.GetComponent<SprintFeel>();
                if (feel == null)
                    feel = movement.gameObject.AddComponent<SprintFeel>();

                var so = new SerializedObject(feel);
                Wire(so, "_dust", dust);
                Wire(so, "_leaves", leaves);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---------------------------------------------------------------- HUD

        private static void BuildHud()
        {
            var root = PrefabUtility.LoadPrefabContents(HudPrefab);
            try
            {
                var rootRect = (RectTransform)root.transform;
                var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);
                var cream = theme != null ? theme.cream : new Color(1f, 0.96f, 0.87f);
                var flash = theme != null ? theme.flash : Color.white;

                var strokes = BuildSpeedLines(rootRect, cream, out var linesRoot);
                var speed = GetOrAdd<SpeedLinesHUD>(root);
                var speedSo = new SerializedObject(speed);
                SetReference(speedSo, "_root", linesRoot);
                var array = speedSo.FindProperty("_strokes");
                array.arraySize = strokes.Length;
                for (var i = 0; i < strokes.Length; i++)
                    array.GetArrayElementAtIndex(i).objectReferenceValue = strokes[i];
                speedSo.ApplyModifiedPropertiesWithoutUndo();

                var frame = BuildPhotoFrame(rootRect, flash, out var group);
                var photo = GetOrAdd<PhotoHitFeel>(root);
                var photoSo = new SerializedObject(photo);
                SetReference(photoSo, "_frame", frame);
                SetReference(photoSo, "_group", group);
                photoSo.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, HudPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Stretched container (first sibling: under every other HUD piece) with thin cream strokes; the component places them.
        private static Image[] BuildSpeedLines(RectTransform parent, Color cream, out RectTransform linesRoot)
        {
            linesRoot = Child(parent, "SpeedLines");
            Stretch(linesRoot);
            linesRoot.SetAsFirstSibling();

            var strokes = new Image[StrokeCount];
            for (var i = 0; i < StrokeCount; i++)
            {
                var rect = Child(linesRoot, "Stroke_" + i);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(100f, 3f);
                var image = GetOrAdd<Image>(rect.gameObject);
                image.color = new Color(cream.r, cream.g, cream.b, 0f);
                image.raycastTarget = false;
                strokes[i] = image;
            }

            return strokes;
        }

        // Centered square frame made of 4 edge bars; PhotoHitFeel sizes it and fades its CanvasGroup.
        private static RectTransform BuildPhotoFrame(RectTransform parent, Color color, out CanvasGroup group)
        {
            var frame = Child(parent, "PhotoHitFrame");
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = new Vector2(600f, 600f);
            group = GetOrAdd<CanvasGroup>(frame.gameObject);
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            Bar(frame, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, FrameThickness), color);
            Bar(frame, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, FrameThickness), color);
            Bar(frame, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(FrameThickness, 0f), color);
            Bar(frame, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(FrameThickness, 0f), color);
            frame.gameObject.SetActive(false); // PhotoHitFeel shows it when a photo lands.
            return frame;
        }

        private static void Bar(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Color color)
        {
            var rect = Child(parent, name);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            var image = GetOrAdd<Image>(rect.gameObject);
            image.color = color;
            image.raycastTarget = false;
        }

        // ---------------------------------------------------------------- Helpers

        private static RectTransform Child(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null)
                return (RectTransform)existing;

            var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component =>
            go.TryGetComponent<T>(out var component) ? component : go.AddComponent<T>();

        // Always points the field at the given object (the builder owns these references).
        private static void SetReference(SerializedObject so, string field, UnityEngine.Object value)
        {
            var property = so.FindProperty(field);
            if (property == null)
                Debug.LogError($"{LogPrefix} Field {field} not found on {so.targetObject.GetType().Name}.");
            else
                property.objectReferenceValue = value;
        }

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

        // Manual-Emit system: no loop, no play on awake, no rate, no bursts; world space so a moving owner leaves the puffs behind.
        private static void Setup(ParticleSystem ps, Material material, Vector2 life, Vector2 speed, Vector2 size, float gravity, int maxParticles)
        {
            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
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

        // Same materials FeelBuilder makes (reused when they exist, created the same way when they do not).
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
