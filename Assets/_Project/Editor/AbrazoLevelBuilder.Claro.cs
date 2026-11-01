// «Claro de la Caravana»: small camp to play the list of pranks. Builds its own scene from a copy of CampAbrazo, which keeps the match
// systems and the HUD. Every height is a real camp object, measured from the jump of a gnome (0.5 m) and its tower.
using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Bot;
using BetterTogether.Environment;
using BetterTogether.Fireplace;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using BetterTogether.UI;
using Fusion;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterTogether.EditorTools
{
    public static partial class AbrazoLevelBuilder
    {
        private const string ClaroPath = "Assets/_Project/Scenes/CampClaro.unity";
        private const string ClaroRoot = "Claro";
        private const string MatchesTag = "Fosforo";
        private const string CueDir = "Assets/_Project/Audio/SFX/";
        private const string StatuePrefabPath = "Assets/_Project/Prefabs/GardenGnomeDecoy.prefab";
        private const string ClaroGnomePrefab = "Assets/_Project/Prefabs/Gnome.prefab";
        private const float ClaroHalfX = 20f;
        private const float ClaroHalfZ = 18f;
        private const float ClaroWallHeight = 8f;
        private const float ClaroMatchSeconds = 360f;
        private const float ClaroPondRadius = 7f;
        private const float ClaroWaterTop = -0.35f;
        private const float ClaroPondDepth = 1.4f;
        private const float ClaroTerrainSize = 80f;
        private const float ClaroTerrainRange = 6f;
        private const float ClaroTerrainBase = -3f;
        private const int ClaroHeightmap = 129;
        private const int ClaroAlphamap = 128;
        private const float MarkerSize = 0.3f;
        private const float MarkerColliderRadius = 0.5f;
        private const float HoleRadius = 0.55f;

        private static readonly Vector2 ClaroFire = new(0f, -4f);
        private static readonly Vector2 ClaroPond = new(12f, 11f);
        private static readonly Vector2 ClaroCage = new(13f, -13f);
        private static readonly Vector2 ClaroCamper = new(3f, -2f);

        // Wood around the whole camp, not in one pile: the camper has to walk past every prank.
        private static readonly Vector2[] ClaroWood =
        {
            new(11f, -6.5f), new(14f, -6.8f), new(16f, -7.5f), new(-10f, 8.5f), new(2f, 7f), new(-16f, -12f), new(10.5f, -1f),
        };

        // Ornaments near every zone, so any corner can hide a gnome.
        private static readonly Vector2[] ClaroDecoys =
        {
            new(-15f, -8f), new(-12f, -9f), new(-9f, -8f), new(-16f, -11f), new(-13f, -12f), new(-10f, -11f), new(-14f, -14f), new(-9f, -14f),
            new(-8.5f, -4.5f), new(-3f, -0.5f), new(3f, -6f), new(-3.5f, -7f), new(1f, 4.5f), new(4f, -0.6f), new(9.2f, -0.5f),
            new(-8f, 5f), new(-15f, 8.5f), new(-9.5f, 0.5f), new(9f, -4f), new(16.5f, -3.5f), new(10f, -11f), new(16f, -12f), new(1.5f, 9f), new(4f, 13f),
        };

        // Three zones far apart, then three more: gnomes start spread out.
        private static readonly Vector2[] ClaroGnomeSpawns =
        {
            new(-12f, -7f), new(-14f, 3f), new(3f, 14f), new(-7f, -12f), new(-16f, 8f), new(17f, 0f),
        };

        private static readonly Vector2[] ClaroHoles = { new(-17f, -7f), new(-17f, 4f), new(0f, 13f), new(17.5f, -3f) };

        private static readonly (string model, Vector2 at, float height)[] ClaroBushes =
        {
            new("SM_Bush_01", new Vector2(-18f, 0f), 1.4f), new("SM_Bush_Berry", new Vector2(-17.5f, -16f), 1.3f), new("SM_Bush_02", new Vector2(-6f, -15.5f), 1.3f),
            new("SM_Bush_01", new Vector2(5f, -15f), 1.4f), new("SM_Bush_Berry", new Vector2(17.5f, -15f), 1.3f), new("SM_Bush_02", new Vector2(18f, -9f), 1.4f),
            new("SM_Bush_01", new Vector2(-4f, 15.5f), 1.4f), new("SM_Bush_Berry", new Vector2(-10f, 15f), 1.3f), new("SM_Bush_02", new Vector2(-17f, 15f), 1.4f),
            new("SM_Fern", new Vector2(-6f, 7f), 0.8f), new("SM_Fern", new Vector2(-1f, 8f), 0.8f), new("SM_Fern", new Vector2(6f, -10f), 0.8f), new("SM_Fern", new Vector2(-4f, -10f), 0.8f),
        };

        [MenuItem("GnoMore/Construir Claro de la Caravana")]
        public static void BuildClaro()
        {
            if (!OpenClaro())
                return;

            Materials.Clear();
            KeyPoints.Clear();
            DecoyPoints.Clear();
            ResetAmbience();
            _layerEnv = LayerMask.NameToLayer("Environment");
            _layerOccluder = LayerMask.NameToLayer("Occluder");
            _layerWater = LayerMask.NameToLayer("Water");
            _groundMask = 1 << _layerEnv;
            _probeTop = GroundProbeTop;
            EnsureFolder();
            EnsureGnomeStatues();

            RemovePreviousLevel();
            var root = new GameObject(ClaroRoot);
            _root = root.transform;
            SceneManager.MoveGameObjectToScene(root, _scene);
            Physics.SyncTransforms();

            BuildClaroTerrain(Group("Terreno"));
            var props = BuildClaroCamp(Group("Campamento"));
            Physics.SyncTransforms();
            PlaceClaroGameplay(Group("Items"));
            BuildClaroHoles(Group("Huecos"));
            BuildClaroDecoration(Group("Bosque"));
            BuildClaroGrass(Group("Hierba"));
            BuildClaroStatues(Group("Estatuas"));
            ConfigureClaroMatch();
            BuildTravesuras(Group("Travesuras"), props);
            ApplyRenderBudget();
            BuildClaroBot();
            BakeClaroNavMesh(root);

            EditorSceneManager.MarkSceneDirty(_scene);
            EditorSceneManager.SaveScene(_scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} Built {ClaroPath}.");
        }

        [MenuItem("GnoMore/Usar Claro como nivel de juego")]
        public static void UseClaro() => SetGameplayScene(ClaroPath);

        private static bool OpenClaro()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"{LogPrefix} Leave Play Mode first.");
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ClaroPath) == null && !AssetDatabase.CopyAsset(AbrazoPath, ClaroPath))
            {
                Debug.LogError($"{LogPrefix} Could not copy {AbrazoPath} to {ClaroPath}.");
                return false;
            }

            AssetDatabase.Refresh();
            if (HasForeignDirtyScene(ClaroPath))
                return false;

            _scene = EditorSceneManager.OpenScene(ClaroPath, OpenSceneMode.Single);
            SceneManager.SetActiveScene(_scene);
            return _scene.IsValid();
        }

        private static void EnsureGnomeStatues()
        {
            var contents = PrefabUtility.LoadPrefabContents(ClaroGnomePrefab);
            try
            {
                EnsureGnomeDefences(contents);
                var statues = contents.GetComponent<GnomeStatues>();
                if (statues == null)
                    statues = contents.AddComponent<GnomeStatues>();

                var so = new SerializedObject(statues);
                so.FindProperty("_statuePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(StatuePrefabPath);
                so.FindProperty("_placeSfx").objectReferenceValue = LoadCue("SFX_StatuePlace");
                so.FindProperty("_boomSfx").objectReferenceValue = LoadCue("SFX_StatueBoom");
                so.FindProperty("_hopSfx").objectReferenceValue = LoadCue("SFX_RabbitHole");
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, ClaroGnomePrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // The rescue target. Idempotent: an existing part is kept.
        private static void EnsureGnomeDefences(GameObject gnome)
        {
            if (gnome.transform.Find("Rescate") == null)
            {
                var rescue = new GameObject("Rescate") { layer = LayerMask.NameToLayer("NoCollision") };
                rescue.transform.SetParent(gnome.transform, false);
                rescue.transform.localPosition = Vector3.up * 0.3f;
                rescue.AddComponent<SphereCollider>().radius = 0.5f;
                rescue.AddComponent<StoneRescue>();
            }

            // The frying pan was tried and dropped.
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gnome);
            var oldVisual = gnome.transform.Find("Sarten");
            if (oldVisual != null)
                Object.DestroyImmediate(oldVisual.gameObject);
        }

        private static SoundCue LoadCue(string name) => AssetDatabase.LoadAssetAtPath<SoundCue>($"{CueDir}{name}.asset");

        private static void RemovePreviousLevel()
        {
            foreach (var go in _scene.GetRootGameObjects())
            {
                if (go != null && (go.name == RootName || go.name == ClaroRoot))
                    Object.DestroyImmediate(go);
            }

            CleanScene();
            foreach (var area in Object.FindObjectsByType<MinimapArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(area.gameObject);
        }

        // ================================================================== Ground

        private static float ClaroHeight(float x, float z)
        {
            var h = 0f;
            var edge = Mathf.Max(Mathf.Abs(x) - ClaroHalfX, Mathf.Abs(z) - ClaroHalfZ);
            if (edge > 0f)
                h += Mathf.Min(edge * 0.08f, 1f) * (Mathf.PerlinNoise(x * 0.09f + 3f, z * 0.09f + 7f) + 0.3f);

            var d = Vector2.Distance(new Vector2(x, z), ClaroPond);
            h -= ClaroPondDepth * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ClaroPondRadius - 2.5f, ClaroPondRadius + 1f, d)));
            return h;
        }

        private static void BuildClaroTerrain(Transform parent)
        {
            var dataPath = $"{SettingsDir}/ClaroTerrain.asset";
            AssetDatabase.DeleteAsset(dataPath);
            var data = new TerrainData();
            AssetDatabase.CreateAsset(data, dataPath);
            data.heightmapResolution = ClaroHeightmap;
            data.size = new Vector3(ClaroTerrainSize, ClaroTerrainRange, ClaroTerrainSize);
            data.alphamapResolution = ClaroAlphamap;
            var layers = new TerrainLayer[LayerSprites.Length];
            for (var i = 0; i < layers.Length; i++)
                layers[i] = LoadLayer(i);
            data.terrainLayers = layers;

            var half = ClaroTerrainSize * 0.5f;
            var heights = new float[ClaroHeightmap, ClaroHeightmap];
            for (var zi = 0; zi < ClaroHeightmap; zi++)
            {
                for (var xi = 0; xi < ClaroHeightmap; xi++)
                {
                    var x = -half + xi / (float)(ClaroHeightmap - 1) * ClaroTerrainSize;
                    var z = -half + zi / (float)(ClaroHeightmap - 1) * ClaroTerrainSize;
                    heights[zi, xi] = (ClaroHeight(x, z) - ClaroTerrainBase) / ClaroTerrainRange;
                }
            }

            data.SetHeights(0, 0, heights);
            data.SetAlphamaps(0, 0, ClaroAlphas(half, layers.Length));
            EditorUtility.SetDirty(data);

            var terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Terreno";
            terrainObject.layer = _layerEnv;
            _terrain = terrainObject.GetComponent<Terrain>();
            terrainObject.transform.SetParent(parent, false);
            terrainObject.transform.position = new Vector3(-half, ClaroTerrainBase, -half);
            Physics.SyncTransforms();

            var water = Cylinder(parent, "Agua", new Vector3(ClaroPond.x, ClaroWaterTop - WaterThickness * 0.5f, ClaroPond.y), (ClaroPondRadius + 1f) * 2f, WaterThickness,
                Quaternion.identity, Mat("Agua", WaterHex), _layerWater, false);
            MakeWaterTransparent(water);
            BuildWaterPhysics(water, parent, ClaroPond, ClaroPondRadius + 1f, ClaroWaterTop);

            var walls = new[]
            {
                (new Vector3(0f, ClaroWallHeight * 0.5f, ClaroHalfZ + 0.5f), new Vector3(ClaroHalfX * 2f + 2f, ClaroWallHeight, 1f)),
                (new Vector3(0f, ClaroWallHeight * 0.5f, -ClaroHalfZ - 0.5f), new Vector3(ClaroHalfX * 2f + 2f, ClaroWallHeight, 1f)),
                (new Vector3(ClaroHalfX + 0.5f, ClaroWallHeight * 0.5f, 0f), new Vector3(1f, ClaroWallHeight, ClaroHalfZ * 2f + 2f)),
                (new Vector3(-ClaroHalfX - 0.5f, ClaroWallHeight * 0.5f, 0f), new Vector3(1f, ClaroWallHeight, ClaroHalfZ * 2f + 2f)),
            };
            foreach (var (center, size) in walls)
                Box(parent, "Limite", center, size, Mat("ClaroBorde", "#2F4A35"), _layerEnv).GetComponent<Renderer>().enabled = false;
        }

        // Grass, with worn dirt around the camp and paths between zones, sand on the shore, flowers in the garden.
        private static float[,,] ClaroAlphas(float half, int layers)
        {
            var maps = new float[ClaroAlphamap, ClaroAlphamap, layers];
            var paths = new[]
            {
                (new Vector2(0f, -4f), new Vector2(-6f, -2f)), (new Vector2(0f, -4f), new Vector2(6f, 0f)), (new Vector2(0f, -4f), new Vector2(13f, -7f)),
                (new Vector2(13f, -7f), new Vector2(13f, -13f)), (new Vector2(-6f, -2f), new Vector2(-12f, 4f)), (new Vector2(-6f, -2f), new Vector2(-12f, -9f)),
                (new Vector2(3f, 1f), new Vector2(4f, 10f)),
            };

            for (var zi = 0; zi < ClaroAlphamap; zi++)
            {
                for (var xi = 0; xi < ClaroAlphamap; xi++)
                {
                    var p = new Vector2(-half + (xi + 0.5f) / ClaroAlphamap * ClaroTerrainSize, -half + (zi + 0.5f) / ClaroAlphamap * ClaroTerrainSize);
                    var path = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 6.5f, Vector2.Distance(p, ClaroFire)));
                    foreach (var (a, b) in paths)
                        path = Mathf.Max(path, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1.8f, DistanceToSegment(p, a, b))));

                    var pond = Vector2.Distance(p, ClaroPond);
                    var sand = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ClaroPondRadius + 1f, ClaroPondRadius + 3f, pond));
                    var flowers = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 7f, Vector2.Distance(p, new Vector2(-12f, -11f))));
                    sand = Mathf.Clamp01(sand);
                    path *= 1f - sand;
                    flowers *= (1f - sand) * (1f - path);
                    maps[zi, xi, LayerGrass] = Mathf.Max(0f, 1f - sand - path - flowers);
                    maps[zi, xi, LayerDirt] = path;
                    maps[zi, xi, LayerSand] = sand;
                    maps[zi, xi, LayerFlowers] = flowers;
                }
            }

            return maps;
        }

        // ================================================================== Camp

        private readonly struct ClaroProps
        {
            public readonly GameObject Underpants;
            public readonly GameObject FallenAwning;
            public readonly GameObject BootOnCooler;
            public readonly Transform Antenna;

            public ClaroProps(GameObject underpants, GameObject fallenAwning, GameObject bootOnCooler, Transform antenna)
            {
                Underpants = underpants;
                FallenAwning = fallenAwning;
                BootOnCooler = bootOnCooler;
                Antenna = antenna;
            }
        }

        private static ClaroProps BuildClaroCamp(Transform parent)
        {
            var wood = Mat("ClaroMadera", "#A56B3C");
            var dark = Mat("ClaroMaderaOscura", "#6E4526");
            var white = Mat("ClaroCaravana", "#F4F0E2");
            var red = Mat("ClaroToldo", "#D65A3D");
            var grey = Mat("ClaroMetal", "#8E8E8E");
            var tyre = Mat("ClaroRueda", "#2A2A2A");
            var glass = Mat("ClaroVentana", "#7FB7CE");
            var blue = Mat("ClaroHielera", "#3B7EA1");

            BuildClaroTable(parent, wood, dark, blue);
            var (underpants, fallen, antenna) = BuildClaroCaravan(parent, white, red, grey, tyre, glass);
            BuildClaroTents(parent, wood, red, blue);

            var boot = Box(parent, "BotaEnHielera", new Vector3(-4f, 0.62f, -2.6f), new Vector3(0.3f, 0.32f, 0.16f), dark, _layerEnv, false);

            Solid(parent, "Lena", new Vector3(13f, 0.45f, -5f), new Vector3(3f, 0.9f, 1f), dark);
            Prop("SM_FirewoodPile", parent, new Vector2(13f, -5f), 0f, 1f);
            Prop("SM_StumpAxe", parent, new Vector2(15.8f, -5.2f), 40f, 0.9f);

            Solid(parent, "Caja", new Vector3(1.8f, 0.225f, 1.2f), new Vector3(0.7f, 0.45f, 0.7f), wood);
            Solid(parent, "CajaAlta", new Vector3(2.45f, 0.45f, 1.2f), new Vector3(0.6f, 0.9f, 0.7f), dark);
            Prop("box", parent, new Vector2(1.8f, 1.2f), 10f, 0.45f);
            Prop("box-large", parent, new Vector2(2.45f, 1.2f), 0f, 0.9f);

            // Dock on posts, reaching out over the water.
            Box(parent, "Muelle", new Vector3(6.2f, 0.12f, 10.5f), new Vector3(6f, 0.12f, 1.4f), wood, _layerEnv);
            foreach (var x in new[] { 4f, 6f, 8f })
            {
                Box(parent, "PosteMuelle", new Vector3(x, -0.5f, 9.9f), new Vector3(0.14f, 1.2f, 0.14f), dark, _layerEnv);
                Box(parent, "PosteMuelle", new Vector3(x, -0.5f, 11.1f), new Vector3(0.14f, 1.2f, 0.14f), dark, _layerEnv);
            }

            AddClaroLight(parent, new Vector3(-6f, 2f, -2f));
            AddClaroLight(parent, new Vector3(6f, 1.7f, -0.4f));
            AddClaroLight(parent, new Vector3(13f, 2.4f, -11f));
            return new ClaroProps(underpants, fallen, boot, antenna);
        }

        // Real models on top, plain invisible blocks underneath: the heights of the design stay exact whatever the model's shape.
        private static void BuildClaroTable(Transform parent, Material wood, Material dark, Material blue)
        {
            Solid(parent, "Mesa", new Vector3(-6f, 0.72f, -2f), new Vector3(2.2f, 0.06f, 1.1f), wood);
            foreach (var (x, z) in new[] { (-7f, -2.45f), (-5f, -2.45f), (-7f, -1.55f), (-5f, -1.55f) })
                Solid(parent, "PataMesa", new Vector3(x, 0.345f, z), new Vector3(0.1f, 0.69f, 0.1f), dark);

            Prop("SM_PicnicTable", parent, new Vector2(-6f, -2f), 90f, 0.75f);

            foreach (var (x, z, yaw) in new[] { (-6.6f, -3.1f, 0f), (-5.4f, -3.1f, 0f), (-6.6f, -0.9f, 180f), (-5.4f, -0.9f, 180f) })
            {
                Solid(parent, "Silla", new Vector3(x, 0.225f, z), new Vector3(0.5f, 0.45f, 0.5f), wood);
                Prop("SM_CampChair", parent, new Vector2(x, z), yaw, 0.9f);
            }

            Solid(parent, "Hielera", new Vector3(-4f, 0.225f, -2.6f), new Vector3(0.9f, 0.45f, 0.55f), blue);
            Prop("SM_Cooler", parent, new Vector2(-4f, -2.6f), 0f, 0.45f);
            Prop("SM_Lantern", parent, new Vector2(-6.8f, -2f), 0f, 0.35f, 0.75f);
        }

        private static void Solid(Transform parent, string name, Vector3 center, Vector3 size, Material material) =>
            Box(parent, name, center, size, material, _layerEnv).GetComponent<Renderer>().enabled = false;

        // A model without a collider, on the ground or at a given height.
        private static void Prop(string model, Transform parent, Vector2 at, float yaw, float height, float atY = float.NaN)
        {
            if (float.IsNaN(atY))
                Spawn(model, parent, model, at, yaw, height, Body.None, _layerEnv, true, Support.Terrain, false, 0.02f, 0f, 0f, false);
            else
                Spawn(model, parent, model, at, yaw, height, Body.None, _layerEnv, true, Support.Fixed, false, 0f, 0f, atY, false);
        }

        private static (GameObject underpants, GameObject fallen, Transform antenna) BuildClaroCaravan(Transform parent, Material white, Material red, Material grey, Material tyre, Material glass)
        {
            Box(parent, "Caravana", new Vector3(6f, 1.3f, 2.5f), new Vector3(5.6f, 2.2f, 2.4f), white, _layerEnv);
            Box(parent, "FranjaCaravana", new Vector3(6f, 1.1f, 1.28f), new Vector3(5.6f, 0.22f, 0.04f), red, _layerEnv, false);
            Box(parent, "TechoCaravana", new Vector3(6f, 2.42f, 2.5f), new Vector3(5.7f, 0.06f, 2.5f), grey, _layerEnv);
            foreach (var x in new[] { 4.4f, 7.6f })
                Box(parent, "Ventana", new Vector3(x, 1.65f, 1.28f), new Vector3(1.1f, 0.65f, 0.05f), glass, _layerEnv, false);

            Box(parent, "Puerta", new Vector3(6f, 1.05f, 1.28f), new Vector3(0.8f, 1.7f, 0.05f), red, _layerEnv, false);
            foreach (var z in new[] { 1.5f, 3.5f })
            {
                Cylinder(parent, "Rueda", new Vector3(4.6f, 0.3f, z), 0.6f, 0.25f, Quaternion.Euler(90f, 0f, 0f), tyre, _layerEnv, false);
                Cylinder(parent, "Rueda", new Vector3(8.2f, 0.3f, z), 0.6f, 0.25f, Quaternion.Euler(90f, 0f, 0f), tyre, _layerEnv, false);
            }

            // Awning at 1.9 m: tower from the table side, then a jump to the roof.
            Box(parent, "Toldo", new Vector3(6f, 1.88f, 0.55f), new Vector3(5f, 0.05f, 1.5f), red, _layerEnv);
            Box(parent, "PosteToldo", new Vector3(8.4f, 0.93f, -0.1f), new Vector3(0.08f, 1.86f, 0.08f), grey, _layerEnv);
            Box(parent, "PosteToldo", new Vector3(3.6f, 0.93f, -0.1f), new Vector3(0.08f, 1.86f, 0.08f), grey, _layerEnv);
            var fallen = Box(parent, "ToldoCaido", new Vector3(6f, 0.55f, 0.1f), new Vector3(5f, 0.05f, 1.5f), red, _layerEnv, false, Quaternion.Euler(28f, 0f, 0f));

            Box(parent, "Capo", new Vector3(9.5f, 0.5f, 2.5f), new Vector3(1.2f, 1f, 2.2f), grey, _layerEnv);
            for (var i = 0; i < 6; i++)
                Box(parent, "EscaleraTrasera", new Vector3(1.95f + i * 0.2f, 0.2f + i * 0.4f, 3f), new Vector3(0.5f, 0.1f, 0.8f), grey, _layerEnv);

            var antenna = Box(parent, "Antena", new Vector3(7.5f, 2.9f, 2.5f), new Vector3(0.08f, 0.9f, 0.08f), grey, _layerEnv);
            var underpants = Box(parent, "Calzoncillos", new Vector3(7.5f, 3.2f, 2.5f), new Vector3(0.5f, 0.3f, 0.05f), red, _layerEnv, false);
            return (underpants, fallen, antenna.transform);
        }

        private static void BuildClaroTents(Transform parent, Material wood, Material red, Material blue)
        {
            Spawn("SM_Tent", parent, "Tienda", new Vector2(-12f, 6f), 20f, 3.2f, Body.Box, _layerEnv, false);
            Box(parent, "PosteTendedero", new Vector3(-14f, 0.9f, 2f), new Vector3(0.1f, 1.8f, 0.1f), wood, _layerEnv);
            Box(parent, "PosteTendedero", new Vector3(-8f, 0.9f, 2f), new Vector3(0.1f, 1.8f, 0.1f), wood, _layerEnv);
            Box(parent, "Cuerda", new Vector3(-11f, 1.75f, 2f), new Vector3(6f, 0.03f, 0.03f), wood, _layerEnv, false);
            Box(parent, "Camiseta", new Vector3(-12.5f, 1.4f, 2f), new Vector3(0.7f, 0.7f, 0.04f), blue, _layerEnv, false);
            Box(parent, "Camisa", new Vector3(-9.6f, 1.4f, 2f), new Vector3(0.6f, 0.6f, 0.04f), red, _layerEnv, false);
        }

        private static void AddClaroLight(Transform parent, Vector3 position)
        {
            var go = new GameObject("Farol");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = LanternLightRange;
            light.intensity = LanternLightIntensity;
            light.color = new Color(1f, 0.82f, 0.55f);
            light.shadows = LightShadows.None;
        }

        // ================================================================== Gameplay

        private static void PlaceClaroGameplay(Transform items)
        {
            var fire = MoveExisting("Fireplace", ClaroFire, 0f);
            if (fire != null)
                SeatOnVisualBase(fire, ClaroFire);

            RemoveBucket();
            MoveExisting("GnomeCage", ClaroCage, 90f);
            var water = MoveExisting("WaterSource", ClaroPond, 0f);
            if (water != null)
                ConfigureClaroShore(water);

            for (var i = 0; i < ClaroWood.Length; i++)
                PlaceItem(items, "Items/Wood.prefab", $"Lena_{i + 1}", ClaroWood[i]);

            var matches = PlacePrefab("Items/Matches.prefab", items, "Fosforos", new Vector2(1.8f, 1.2f), 0f);
            if (matches != null)
                matches.tag = MatchesTag;

            PlaceItem(items, "Items/Sausage.prefab", "Salchicha", new Vector2(-6f, -2f));

            PlaceSpawn("CamperSpawn", ClaroCamper, Vector3.forward);
            for (var i = 0; i < ClaroGnomeSpawns.Length; i++)
                PlaceSpawn($"GnomeSpawn{i + 1}", ClaroGnomeSpawns[i], new Vector3(-ClaroGnomeSpawns[i].x, 0f, -ClaroGnomeSpawns[i].y));
            for (var i = 0; i < LobbySpawnSpots.Length; i++)
                PlaceSpawn($"SpawnPoint{i + 1}", new Vector2(-4f + i * 2f, -7f), Vector3.forward);
        }

        private static void ConfigureClaroShore(GameObject water)
        {
            var source = water.GetComponent<WaterSource>();
            if (source == null)
                return;

            var serialized = new SerializedObject(source);
            var path = serialized.FindProperty("_path");
            if (path != null)
            {
                path.arraySize = ShorePoints + 1;
                for (var i = 0; i <= ShorePoints; i++)
                {
                    var angle = i * Mathf.PI * 2f / ShorePoints;
                    var shore = ClaroPond + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (ClaroPondRadius + 1f);
                    path.GetArrayElementAtIndex(i).vector3Value = new Vector3(shore.x, 0f, shore.y);
                }
            }

            var volumes = serialized.FindProperty("_volumes");
            if (volumes != null)
                volumes.arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildClaroHoles(Transform parent)
        {
            var holes = new HuecoConejo[ClaroHoles.Length];
            var dirt = Mat("ClaroTierra", "#6B4A2B");
            var black = Mat("ClaroHueco", "#120C08");
            for (var i = 0; i < ClaroHoles.Length; i++)
            {
                var at = GroundPoint(ClaroHoles[i]);
                var go = new GameObject($"Hueco_{i + 1}") { layer = LayerMask.NameToLayer("NoCollision") };
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(new Vector3(-at.x, 0f, -at.z)));
                go.AddComponent<SphereCollider>().radius = HoleRadius;
                Cylinder(go.transform, "Tierra", at + Vector3.up * 0.05f, HoleRadius * 2.6f, 0.1f, Quaternion.identity, dirt, _layerEnv, false);
                Cylinder(go.transform, "Boca", at + Vector3.up * 0.11f, HoleRadius * 1.8f, 0.02f, Quaternion.identity, black, _layerEnv, false);
                holes[i] = go.AddComponent<HuecoConejo>();
            }

            for (var i = 0; i < holes.Length; i++)
            {
                var so = new SerializedObject(holes[i]);
                so.FindProperty("_next").objectReferenceValue = holes[(i + 1) % holes.Length];
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void BuildClaroDecoration(Transform parent)
        {
            // Forest ring just outside the limit, three rows deep.
            for (var row = 0; row < 3; row++)
            {
                var offset = 2.5f + row * 4f;
                var x = ClaroHalfX + offset;
                var z = ClaroHalfZ + offset;
                var index = 0;
                for (var t = -x; t <= x; t += 4f)
                {
                    ClaroTree(parent, row, index++, new Vector2(t, z));
                    ClaroTree(parent, row, index++, new Vector2(t + 1.5f, -z));
                }

                for (var t = -z; t <= z; t += 4f)
                {
                    ClaroTree(parent, row, index++, new Vector2(x, t));
                    ClaroTree(parent, row, index++, new Vector2(-x, t + 1.5f));
                }
            }

            // Camp life: what a camper leaves lying around.
            Prop("SM_LogBench", parent, new Vector2(-2.6f, -5.8f), 60f, 0.45f);
            Prop("SM_LogBench", parent, new Vector2(2.6f, -6f), 120f, 0.45f);
            Prop("SM_Backpack", parent, new Vector2(-9.6f, 7.6f), 200f, 0.7f);
            Prop("SM_SleepingBag_Rolled", parent, new Vector2(-14.6f, 7.8f), 30f, 0.35f);
            Prop("SM_Lantern", parent, new Vector2(-10.5f, 3.6f), 0f, 0.4f);
            Prop("SM_Canoe", parent, new Vector2(3.4f, 13.2f), 20f, 0.7f);
            Prop("SM_Paddle", parent, new Vector2(1.8f, 12.4f), 75f, 0.2f);
            Prop("SM_FishingRod", parent, new Vector2(7.8f, 9.2f), 0f, 1.6f, 0.18f);
            Prop("SM_Signpost", parent, new Vector2(-2f, 4f), 150f, 1.6f);
            Prop("barrel", parent, new Vector2(10.2f, 0.4f), 0f, 0.8f);
            Prop("barrel", parent, new Vector2(10.9f, 0.9f), 40f, 0.8f);
            Prop("SM_Rock_Cluster", parent, new Vector2(-18f, -4f), 80f, 0.9f);
            Prop("SM_Rock_Flat", parent, new Vector2(17.5f, 6f), 20f, 0.5f);
            Prop("SM_Mushrooms", parent, new Vector2(-5f, 10f), 0f, 0.3f);
            Prop("SM_Mushrooms", parent, new Vector2(7f, -14f), 90f, 0.3f);
            foreach (var (x, z) in new[] { (-16f, -15f), (-11f, -15.5f), (-7f, -9.5f), (-17f, -9.5f), (-14.5f, -10.5f), (-11.5f, -13.2f) })
                Prop(x < -12f ? "SM_Flowers_Purple" : "SM_Flowers_Yellow", parent, new Vector2(x, z), x * 31f, 0.35f);

            foreach (var (model, at, height) in ClaroBushes)
                Spawn(model, parent, model, at, Hash01(at.x, at.y) * 360f, height, Body.None, _layerEnv, true, Support.Terrain, false, 0.06f, 0f, 0f, false);

            Spawn("SM_Log_Fallen", parent, "TroncoCaido", new Vector2(-1f, -14f), 70f, 3f, Body.Box, _layerEnv, false, Support.Terrain, false, 0.06f);
            Spawn("SM_Log_Fallen", parent, "TroncoCaido", new Vector2(-18f, 11f), 10f, 3f, Body.Box, _layerEnv, false, Support.Terrain, false, 0.06f);
        }

        private static void ClaroTree(Transform parent, int row, int index, Vector2 at)
        {
            var kind = TreeKinds[TreePick[(index * 7 + row * 3) % TreePick.Length]];
            var height = Mathf.Lerp(kind.MinHeight, kind.MaxHeight, Hash01(at.x, at.y));
            Spawn(kind.Model, parent, $"Arbol_{row}_{index}", at, Hash01(at.y, at.x) * 360f, height, Body.None, _layerEnv, true, Support.Terrain, false, 0.12f, 0f, 0f, false);
        }

        private static void BuildClaroStatues(Transform parent)
        {
            for (var i = 0; i < ClaroDecoys.Length; i++)
                PlacePrefab("GardenGnomeDecoy.prefab", parent, $"Estatua_{i + 1:00}", ClaroDecoys[i], i * 47f);
        }

        private static void ConfigureClaroMatch()
        {
            var bounds = Object.FindFirstObjectByType<LevelBounds>(FindObjectsInactive.Include);
            if (bounds != null)
            {
                var serialized = new SerializedObject(bounds);
                serialized.FindProperty("_bounds").boundsValue = new Bounds(new Vector3(0f, 8f, 0f), new Vector3(ClaroHalfX * 2f + 4f, 30f, ClaroHalfZ * 2f + 4f));
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var match = Object.FindFirstObjectByType<MatchManager>(FindObjectsInactive.Include);
            if (match != null)
            {
                var serialized = new SerializedObject(match);
                serialized.FindProperty("_matchDuration").floatValue = ClaroMatchSeconds;
                serialized.FindProperty("_botCamper").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ================================================================== Tall grass

        private const int GrassGridX = 48;
        private const int GrassGridZ = 44;

        // Meadows between the zones (x, z, radius): every crossing has a covered way.
        private static readonly Vector3[] GrassMeadows =
        {
            new(-15f, -1.5f, 6f), new(-6f, 6.5f, 6.5f), new(-15f, 13f, 7f), new(-3f, 13f, 6f), new(8f, -11f, 6.5f), new(-3f, -12f, 7f),
            new(17f, 0f, 5.5f), new(17f, -14f, 4.5f), new(17f, 15f, 3.5f), new(3f, -9f, 4f), new(-9f, 13f, 5f), new(14f, 3f, 3.5f),
        };

        private static readonly (Vector2 a, Vector2 b)[] GrassPaths =
        {
            (new Vector2(0f, -4f), new Vector2(-6f, -2f)), (new Vector2(0f, -4f), new Vector2(6f, 0f)), (new Vector2(0f, -4f), new Vector2(13f, -7f)),
            (new Vector2(13f, -7f), new Vector2(13f, -13f)), (new Vector2(-6f, -2f), new Vector2(-12f, 4f)), (new Vector2(-6f, -2f), new Vector2(-12f, -9f)),
            (new Vector2(3f, 1f), new Vector2(4f, 10f)),
        };

        private static bool GrassGrows(Vector2 p)
        {
            var inMeadow = false;
            foreach (var meadow in GrassMeadows)
                inMeadow |= Vector2.Distance(p, new Vector2(meadow.x, meadow.y)) <= meadow.z;

            if (!inMeadow || Vector2.Distance(p, ClaroFire) < 5f || Vector2.Distance(p, ClaroPond) < ClaroPondRadius + 1.5f || Vector2.Distance(p, ClaroCage) < 3f)
                return false;

            foreach (var (a, b) in GrassPaths)
            {
                if (DistanceToSegment(p, a, b) < 0.9f)
                    return false;
            }

            var inCaravan = p.x > 1.5f && p.x < 11f && p.y > -1f && p.y < 4.5f;
            var inTable = p.x > -8.5f && p.x < -3f && p.y > -4f && p.y < 0f;
            var inTent = p.x > -14.5f && p.x < -9.5f && p.y > 3.5f && p.y < 8.5f;
            var inWood = p.x > 10.5f && p.x < 16.5f && p.y > -9f && p.y < -3.5f;
            return !inCaravan && !inTable && !inTent && !inWood;
        }

        private static void BuildClaroGrass(Transform parent)
        {
            var origin = new Vector2(-GrassGridX * 0.5f, -GrassGridZ * 0.5f);
            var cells = new bool[GrassGridX * GrassGridZ];
            for (var z = 0; z < GrassGridZ; z++)
            {
                for (var x = 0; x < GrassGridX; x++)
                    cells[z * GrassGridX + x] = GrassGrows(origin + new Vector2(x + 0.5f, z + 0.5f));
            }

            var fieldObject = new GameObject("CampoHierba");
            fieldObject.transform.SetParent(parent, false);
            fieldObject.AddComponent<TallGrassField>().Configure(origin, GrassGridX, GrassGridZ, cells);
            ConfigureGrassRenderer(fieldObject);
        }

        // A copy of the material with instancing on: the model's own lives inside the FBX and can't be changed.
        private static void ConfigureGrassRenderer(GameObject field)
        {
            var first = LoadModel("SM_Grass_01");
            var second = LoadModel("SM_Grass_02");
            var source = first.GetComponentInChildren<MeshFilter>();
            var materialPath = $"{SettingsDir}/Mat_ClaroHierba.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(source.GetComponent<MeshRenderer>().sharedMaterial);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);

            // Measured from the model as imported: which axis is up depends on the FBX.
            var rotation = source.transform.rotation;
            var bounds = GeometryUtility.CalculateBounds(WorldCorners(source.sharedMesh.bounds, rotation), Matrix4x4.identity);
            field.AddComponent<TallGrassRenderer>().Configure(
                new[] { source.sharedMesh, second.GetComponentInChildren<MeshFilter>().sharedMesh }, material, rotation, bounds.size.y, bounds.min.y);
        }

        private static Vector3[] WorldCorners(Bounds bounds, Quaternion rotation)
        {
            var corners = new Vector3[8];
            for (var i = 0; i < 8; i++)
            {
                var sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                corners[i] = rotation * (bounds.center + Vector3.Scale(bounds.extents, sign));
            }

            return corners;
        }

        // ================================================================== Bot camper

        private const string BotPrefabPath = "Assets/_Project/Prefabs/CampistaBot.prefab";
        private const string CamperPrefabPath = "Assets/_Project/Prefabs/Camper.prefab";
        private const string WoodPrefabPath = "Assets/_Project/Prefabs/Items/Wood.prefab";

        private static void BuildClaroBot()
        {
            var old = FindInScene("CampistaBot");
            if (old != null)
                Object.DestroyImmediate(old);

            var root = new GameObject("CampistaBot") { layer = LayerMask.NameToLayer("Player") };
            try
            {
                FillBotPrefab(root);
                PrefabUtility.SaveAsPrefabAsset(root, BotPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BotPrefabPath), _root);
            instance.name = "CampistaBot";
            instance.transform.SetPositionAndRotation(GroundPoint(ClaroCamper), Quaternion.LookRotation(Vector3.forward));
        }

        private static void FillBotPrefab(GameObject root)
        {
            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.5f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.75f, 0f);
            controller.stepOffset = 0.3f;

            var network = root.AddComponent<NetworkObject>();
            var woodNetwork = AssetDatabase.LoadAssetAtPath<GameObject>(WoodPrefabPath).GetComponent<NetworkObject>();
            var flags = new SerializedObject(network);
            flags.FindProperty("Flags").intValue = new SerializedObject(woodNetwork).FindProperty("Flags").intValue;
            flags.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<NetworkTransform>();

            var eye = NewChild(root.transform, "Ojos", new Vector3(0f, 1.4f, 0.1f));
            var hand = NewChild(root.transform, "Mano", new Vector3(0.3f, 0.9f, 0.4f));
            var anchor = NewChild(root.transform, "CarryAnchor", new Vector3(0f, 1.1f, 0.5f));

            var camperAnimator = AssetDatabase.LoadAssetAtPath<GameObject>(CamperPrefabPath).GetComponentInChildren<Animator>(true);
            var visual = Object.Instantiate(camperAnimator.gameObject, root.transform);
            visual.name = "Modelo";
            visual.transform.localPosition = camperAnimator.transform.localPosition;
            visual.transform.localRotation = camperAnimator.transform.localRotation;
            foreach (var behaviour in visual.GetComponentsInChildren<NetworkBehaviour>(true))
                Object.DestroyImmediate(behaviour);
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            var barkObject = NewChild(root.transform, "Globo", new Vector3(0f, 2.1f, 0f));
            var bark = barkObject.gameObject.AddComponent<TextMeshPro>();
            bark.fontSize = 3.5f;
            bark.alignment = TextAlignmentOptions.Center;
            bark.rectTransform.sizeDelta = new Vector2(6f, 1.2f);
            bark.text = "";

            var flash = NewChild(root.transform, "Flash", new Vector3(0f, 1.4f, 0.4f)).gameObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.range = 9f;
            flash.intensity = 0f;

            var carrier = root.AddComponent<GnomeCarrier>();
            var carrierSo = new SerializedObject(carrier);
            carrierSo.FindProperty("_botControlled").boolValue = true;
            carrierSo.FindProperty("_carryAnchor").objectReferenceValue = anchor;
            carrierSo.ApplyModifiedPropertiesWithoutUndo();

            var bot = root.AddComponent<CampistaBot>();
            var so = new SerializedObject(bot);
            so.FindProperty("_controller").objectReferenceValue = controller;
            so.FindProperty("_eye").objectReferenceValue = eye;
            so.FindProperty("_hand").objectReferenceValue = hand;
            so.FindProperty("_carrier").objectReferenceValue = carrier;
            so.FindProperty("_animator").objectReferenceValue = visual.GetComponent<Animator>();
            so.FindProperty("_bark").objectReferenceValue = bark;
            so.FindProperty("_flash").objectReferenceValue = flash;
            so.FindProperty("_sightMask").intValue = _groundMask | (1 << _layerOccluder);
            so.FindProperty("_shutterSfx").objectReferenceValue = LoadCue("SFX_PhotoShutter");
            so.FindProperty("_noticeSfx").objectReferenceValue = LoadCue("SFX_Watched");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform NewChild(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        private static void BakeClaroNavMesh(GameObject root)
        {
            Physics.SyncTransforms();
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = _groundMask;
            surface.BuildNavMesh();

            var path = $"{SettingsDir}/ClaroNavMesh.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
        }

        // ================================================================== Pranks

        private static void BuildTravesuras(Transform parent, ClaroProps props)
        {
            var layoutObject = new GameObject("Lista");
            layoutObject.transform.SetParent(parent, false);
            var layout = layoutObject.AddComponent<TravesuraLayout>();
            var marker = Mat("ClaroMarca", "#FFD25E");

            var entries = new (string label, string hint, TravesuraLayout.Kind kind, int points, float window, GameObject result, string cue)[]
            {
                ("Llevad los fósforos al muelle y empujadlos entre dos", "en el muelle", TravesuraLayout.Kind.Points, 2, 0f, null, null),
                ("Coged sus calzoncillos del tendedero y colgadlos en la antena", "en el tendedero", TravesuraLayout.Kind.Points, 2, 0f, props.Underpants, null),
                ("¡Bocinazo! Uno en la cabina y otro en el capó, a la vez", "en la caravana", TravesuraLayout.Kind.Points, 2, 5f, null, "SFX_Horn"),
                ("Apagad la fogata con dos bombas de agua", "en la fogata", TravesuraLayout.Kind.FireOut, 0, 0f, null, null),
                ("Haced que pise una estatua explosiva (H)", "algo que explota", TravesuraLayout.Kind.Bomb, 0, 0f, null, null),
                ("Tiradle el toldo: soltad los dos postes a la vez", "en el toldo", TravesuraLayout.Kind.Points, 2, 5f, props.FallenAwning, null),
                ("Robad su bota de la tienda y metedla en la hielera", "en la tienda", TravesuraLayout.Kind.Points, 2, 0f, props.BootOnCooler, null),
            };

            var serialized = new SerializedObject(layout);
            serialized.FindProperty("_needed").intValue = 5;
            var array = serialized.FindProperty("_entries");
            array.arraySize = entries.Length;
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = array.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Label").stringValue = entries[i].label;
                entry.FindPropertyRelative("CamperHint").stringValue = entries[i].hint;
                entry.FindPropertyRelative("Kind").enumValueIndex = (int)entries[i].kind;
                entry.FindPropertyRelative("PointCount").intValue = entries[i].points;
                entry.FindPropertyRelative("WindowSeconds").floatValue = entries[i].window;
                entry.FindPropertyRelative("Result").objectReferenceValue = entries[i].result;
                entry.FindPropertyRelative("DoneSfx").objectReferenceValue = entries[i].cue != null ? LoadCue(entries[i].cue) : LoadCue("SFX_MissionStep");
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            var points = layoutObject.transform;
            AddPoint(points, 0, 0, new Vector3(8.4f, 0.75f, 10.5f), "Mantén para dejar los fósforos", MatchesTag, marker);
            AddPoint(points, 0, 1, new Vector3(6f, 0.75f, 10.5f), "Mantén para empujar los fósforos", null, marker);
            AddPoint(points, 1, 0, new Vector3(-11f, 0.9f, 2f), "Mantén para coger los calzoncillos", null, marker);
            AddPoint(points, 1, 1, props.Antenna.position + new Vector3(0.4f, -0.3f, 0f), "Mantén para colgar los calzoncillos", null, marker);
            AddPoint(points, 2, 0, new Vector3(9.4f, 0.9f, 0.9f), "Mantén para tocar la bocina", null, marker);
            AddPoint(points, 2, 1, new Vector3(9.5f, 1.5f, 2.5f), "Mantén para pisar el capó", null, marker);
            AddPoint(points, 5, 0, new Vector3(3.6f, 0.7f, -0.5f), "Mantén para soltar el poste", null, marker);
            AddPoint(points, 5, 1, new Vector3(8.4f, 0.7f, -0.5f), "Mantén para soltar el poste", null, marker);
            AddPoint(points, 6, 0, new Vector3(-10.4f, 0.7f, 4f), "Mantén para robar la bota", null, marker);
            AddPoint(points, 6, 1, new Vector3(-4f, 0.9f, -3.2f), "Mantén para meter la bota", null, marker);
        }

        private static void AddPoint(Transform parent, int travesura, int index, Vector3 position, string prompt, string itemTag, Material material)
        {
            var go = new GameObject($"Punto_{travesura}_{index}") { layer = LayerMask.NameToLayer("NoCollision") };
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.AddComponent<SphereCollider>().radius = MarkerColliderRadius;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.name = "Marca";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = Vector3.one * MarkerSize;
            visual.GetComponent<Renderer>().sharedMaterial = material;

            var point = go.AddComponent<TravesuraPoint>();
            var so = new SerializedObject(point);
            so.FindProperty("_travesura").intValue = travesura;
            so.FindProperty("_point").intValue = index;
            so.FindProperty("_prompt").stringValue = prompt;
            so.FindProperty("_requiredItemTag").stringValue = itemTag ?? "";
            so.FindProperty("_marker").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
