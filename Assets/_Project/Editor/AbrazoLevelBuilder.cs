// Editor-only tool (C9.2). Menu GnoMore/Construir nivel El Abrazo: idempotently builds the scene CampAbrazo.unity (a copy of
// CampBryan, made once) with the level of Docs/plan/fase-9/02-diseno-v2.md: terrain, the two giant trees with the slit between
// them, roots, the ramp around the trunks, six sectors and the gameplay objects, all with simple coloured shapes. Everything
// generated hangs from a root "Abrazo" that is deleted and rebuilt on every run, and every Y comes from a raycast against
// this scene's colliders. It never opens or saves CampBryan, CampIsma or any other scene (except MainMenu in UseAsGameplay).
// All public entry points are dialog-free so they run in batchmode.
using System.Collections.Generic;
using System.IO;
using BetterTogether.Environment;
using BetterTogether.Helpers;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterTogether.EditorTools
{
    public static class AbrazoLevelBuilder
    {
        private const string LogPrefix = "[AbrazoLevelBuilder]";

        // ---- Assets and scenes ----
        private const string BryanPath = "Assets/_Project/Scenes/CampBryan.unity";
        private const string AbrazoPath = "Assets/_Project/Scenes/CampAbrazo.unity";
        private const string MainMenuPath = "Assets/_Project/Scenes/MainMenu.unity";
        private const string SettingsDir = "Assets/_Project/Settings/Abrazo";
        private const string PrefabDir = "Assets/_Project/Prefabs/";
        private const string SpritesDir = "Assets/_Project/Sprites/";
        private const string SmokeMaterialPath = "Assets/_Project/Prefabs/Items/Mat_SausageSmoke.mat";
        private const string RootName = "Abrazo";

        // ---- Terrain (110 x 110 m centred at the origin; Y range -2..2 so the basins can go below zero) ----
        private const float TerrainSize = 110f;
        private const float TerrainBaseY = -2f;
        private const float TerrainHeightRange = 4f;
        private const int HeightmapResolution = 257;
        private const int AlphamapResolution = 128;
        private const float TerrainTileSize = 6f;
        private static readonly Vector2 HondonadaCenter = new(0f, 26f);
        private const float HondonadaFlatRadius = 9f;
        private const float HondonadaFalloff = 8f;
        private const float HondonadaDepth = -1f;
        private static readonly Vector2 PozaCenter = new(28f, 26f);
        private const float PozaFlatRadius = 7f;
        private const float PozaFalloff = 10f;
        private const float PozaDepth = -1.5f;
        private static readonly Vector2 StreamStart = new(22f, 26f);
        private static readonly Vector2 StreamEnd = new(10f, 26f);
        private const float StreamDepth = -0.6f;
        private const float StreamInnerHalfWidth = 0.8f;
        private const float StreamOuterHalfWidth = 2.8f;
        private const float WaterRadius = 10.4f;
        private const float WaterTopY = -1.05f;
        private const float WaterThickness = 0.6f;

        // ---- Limits of the clearing ----
        private const float ClearingHalfX = 38f;
        private const float ClearingHalfZ = 35f;
        private const float InvisibleWallX = 39f;
        private const float InvisibleWallZ = 36f;
        private const float InvisibleWallThickness = 2f;
        private const float InvisibleWallHeight = 30f;
        private const float TreeRingX = 41f;
        private const float TreeRingZ = 38f;
        private const float TreeSpacing = 6.5f;
        private const float TreeRowGap = 4f;
        private static readonly Vector3 BoundsCenter = new(0f, 5f, 0f);
        private static readonly Vector3 BoundsSize = new(80f, 30f, 80f);

        // ---- The twin trees ----
        private const float TrunkRadius = 7f;
        private const float TrunkTopY = 14f;
        private const float TrunkSink = 0.5f;
        private const int TrunkArcSegments = 8;
        private static readonly Vector2 SlitStart = new(-4.6f, 7f);
        private static readonly Vector2 SlitEnd = new(6.5f, -10f);
        private const float SlitHalfWidth = 0.3f;
        private const float SlitClearHeight = 0.8f;
        private const float SlitRoofThickness = 0.4f;
        private const float SlitWallThickness = 0.4f;
        private const float SlitWallHeight = 1.2f;
        private const int RibbonCount = 3;
        private const int RibbonSegments = 44;
        private const float RibbonTurns = 2f;
        private const float RibbonStartY = 1.6f;
        private const float RibbonEndY = 13.4f;
        private const float RibbonSize = 0.45f;

        // ---- Roots ----
        private const float RootHeight = 2.8f;
        private const float RootHalfThickness = 1f;
        private const float RootEastStartX = 7f;
        private const float RootWestStartX = 7f;
        private const float RootTipX = 24f;
        private const float TunnelX = 16f;
        private const float TunnelWidth = 0.6f;
        private const float TunnelHeight = 0.7f;
        private const float FireU_InnerX = 3.5f;
        private const float FireU_OuterX = 4f;
        private const float FireU_FrontZ = -12f;
        private const float FireU_BackZ = -8f;
        private const float FireU_BackOuterZ = -7.5f;
        private const float PeepholeCenterZ = -9.85f;
        private const float PeepholeSize = 0.4f;
        private const float PeepholeSillY = 0.15f;

        // ---- Ramp around the trunks ----
        private const float RampRadius = 10f;
        private const float RampEntryX = 12.5f;
        private const float RampEntryStartZ = -24f;
        private const float RampEntryEndY = 3.2f;
        private const int RampEntrySegments = 8;
        private const int RampArcSegments = 22;
        private const float RampArcStartRadius = 12.5f;
        private const float RampArcBlendDeg = 40f;
        private const float RampEndDeg = 401f;
        private const float RampShoulderY = 6f;
        private const float RampTopY = 10f;
        private const float RampWidth = 2f;
        private const float RampThickness = 0.2f;
        private const float RampOverlap = 0.45f;
        private const float RampRailHeight = 0.5f;
        private const float RampPostOffset = 0.85f;
        private const float RampPostSize = 0.25f;
        private const float SecondLapDeg = 360f;
        private const float ShoulderDeg = 229.4f;
        private const float ShoulderRadius = 3.3f;
        private const float CanopyPlatformRadius = 2.5f;
        private const float PlatformThickness = 0.3f;
        private const float TowerStairRadius = 12.2f;
        private const float TowerStairStartDeg = 207f;
        private const float TowerStairEndDeg = 229.4f;
        private const float TowerStairWidth = 1.2f;

        // ---- Gnome-only crack (Grieta) and the tower ledge ----
        private const float CrackRadius = 7.6f;
        private const float CrackLedgeWidth = 1.5f;
        private const float CrackLedgeStepDeg = 6f;
        private const float CrackStartDeg = 137f;
        private const int CrackLedges = 8;
        private const float CrackLedgeHeight = 0.4f;
        private const float CrackInclineWidth = 1.4f;
        private const int CrackInclineParts = 3;
        private static readonly Vector2 TowerLedge = new(-10f, -4f);
        private const float TowerLedgeTopY = 1.6f;
        private const float TowerLedgeSize = 1.4f;

        // ---- Stairs ----
        private const float CamperStepHeight = 0.3f;
        private const float GnomeStepHeight = 0.4f;
        private const float StepDepth = 0.7f;
        private const float StairSink = 0.15f;

        // ---- Sector: camp ----
        private static readonly Vector2[] TentSpots = { new(-9f, -27f), new(9f, -32f) };
        private static readonly Vector2 CageSpot = new(17f, -24f);
        private static readonly Vector2 CampWoodSpot = new(-6f, -26f);
        private static readonly Vector2 CamperSpawnSpot = new(0f, -30f);

        // ---- Sector: sawmill ----
        private static readonly Vector2 SawmillCenter = new(28f, -14f);
        private const float SawmillLowY = 1.5f;
        private const float SawmillHighY = 3f;
        private const float SawmillDeckThickness = 0.3f;
        private static readonly Vector2 SawmillWoodSpot = new(30f, -14f);

        // ---- Sector: orchard ----
        private static readonly Vector2 OrchardWoodSpot = new(-30f, -22f);
        private const float OrchardBedHeight = 0.4f;

        // ---- Sector: crag ----
        private const float CragTerraceLowY = 1.5f;
        private const float CragTerraceMidY = 3f;
        private const float CragTerraceHighY = 4.5f;
        private static readonly Vector2 CragWoodSpot = new(-34f, 22f);
        private static readonly Vector2 CragSausageSpot = new(-30f, 28f);

        // ---- Sector: hollow ----
        private static readonly Vector2 HollowWoodSpot = new(6f, 30f);

        // ---- Sector: pool ----
        private static readonly Vector2 WaterSourceSpot = new(26f, 22f);
        private static readonly Vector2 PoolWoodSpot = new(26f, 30f);
        private static readonly Vector2 DockSausageSpot = new(34f, 28f);
        private static readonly Vector2 BucketSpot = new(32f, 6f);
        private const float DockTopY = -0.8f;
        private const float DockThickness = 0.15f;

        // ---- Gameplay placement ----
        private static readonly Vector2 FireplaceSpot = new(0f, -9.5f);
        private static readonly Vector2 ShoulderWoodSpot = new(-8f, -5f);
        private static readonly Vector2 ShoulderMatchesSpot = new(-6f, -7f);
        private static readonly Vector2[] GnomeSpawnSpots = { new(-34f, 8f), new(0f, 32f), new(34f, 14f) };
        private static readonly Vector2[] LobbySpawnSpots = { new(-3f, -32f), new(3f, -32f), new(-3f, -34f), new(3f, -34f) };
        private static readonly Vector2[] HidingSpots =
        {
            new(-24f, -14f), new(-34f, -14f), // orchard
            new(34f, -18f),                   // sawmill
            new(-8f, 28f), new(10f, 24f), new(0f, 33f), // hollow
            new(-36f, 12f),                   // crag
            new(20f, 31f),                    // pool
        };

        private static readonly Vector2[] OrchardDecoyAnchors =
        {
            new(-32f, -24f), new(-28f, -24f), new(-24f, -23f), new(-33f, -19f), new(-25f, -19f),
            new(-31f, -14f), new(-27f, -13f), new(-22f, -17f), new(-34f, -27f), new(-26f, -28f),
            new(-30f, -16f), new(-23f, -26f), new(-35f, -22f), new(-29f, -27f),
        };

        private static readonly Vector2[] HollowDecoyAnchors = { new(-4f, 24f), new(5f, 28f), new(0f, 20f), new(-10f, 31f) };
        private static readonly Vector2[] ShoulderDecoyAnchors = { new(-4.2f, -8.3f), new(-7.6f, -8.4f), new(-3.8f, -6.3f), new(-8f, -6.5f) };
        private const int OrchardDecoys = 10;
        private const int HollowDecoys = 3;
        private const int ShoulderDecoys = 2;

        // ---- Cover (tall bushes) ----
        private const float CoverSpacing = 6f;
        private const float CoverJitter = 1.6f;
        private const float HollowCoverSpacing = 3.6f;
        private const float CoverLimitX = 37f;
        private const float CoverLimitZ = 34f;
        private const float CoverClearRadius = 1.1f;
        private const float CoverKeepAway = 3f;
        private const float CoverMinGroundY = -0.9f;
        private const float CoverMinNormalY = 0.85f;
        private const float CoverTrunkExclusion = 13f;
        private const float CoverSlitExclusion = 3f;

        // ---- Hiding bush and decoys ----
        private static readonly Vector3 HidingBushScale = new(2.4f, 1.6f, 2.4f);
        private const float DecoyRadius = 0.2f;
        private const float DecoyHeight = 0.7f;
        private const float DecoySpacing = 1f;
        private const float DecoyKeepAway = 1.5f;
        private const float DecoyMinNormalY = 0.85f;
        private const float DecoyRingStep = 0.6f;
        private const int DecoyRings = 3;
        private const int DecoySteps = 12;

        // ---- Probes ----
        private const float GroundProbeTop = 40f;
        private const float GroundProbeDistance = 80f;
        private const float CamperRadius = 0.3f;
        private const float CamperCrouchHeight = 1f;
        private const float GnomeRadius = 0.12f;
        private const float GnomeHeight = 0.45f;
        private const float GnomeProbeLift = 0.02f;
        private const float SpawnLift = 0.05f;

        // ---- Capture ----
        private const int CaptureSize = 1200;
        private const float CaptureTopHeight = 70f;
        private const float CaptureTopOrthoSize = 42f;
        private const float CaptureSectorDistance = 20f;
        private const float CaptureSectorHeight = 13f;
        private const float CaptureLookHeight = 1.5f;
        private static readonly Vector3[] CaptureSectors =
        {
            new(0f, 0f, -24f), new(28f, 0f, -14f), new(-28f, 0f, -20f),
            new(-30f, 2f, 18f), new(0f, -1f, 26f), new(28f, -1f, 26f),
        };

        private static readonly string[] CaptureNames = { "campamento", "serreria", "huerto", "risco", "hondonada", "poza" };

        // Colours of the prototype (hex).
        private const string BarkDarkHex = "#5A3A26";
        private const string BarkPaleHex = "#D9CDB6";
        private const string RootDarkHex = "#4A3022";
        private const string RootPaleHex = "#C2B49A";
        private const string WoodHex = "#A97C50";
        private const string WoodOldHex = "#7C5B3A";
        private const string RockHex = "#8A8478";
        private const string LeafHex = "#4F7A3A";
        private const string BushHex = "#3E6B34";
        private const string BranchHex = "#E6DCC8";
        private const string BedHex = "#6B4A2F";
        private const string WaterHex = "#3D7FA8";
        private const string RoofHex = "#6F5A44";
        private const string SignPostHex = "#5B4636";

        // Terrain layers: index in the array below. Sector floor colours come from these watercolours.
        private const int LayerGrass = 0;
        private const int LayerDirt = 1;
        private const int LayerPebbles = 2;
        private const int LayerFlowers = 3;
        private const int LayerSand = 4;
        private const int LayerWetSand = 5;
        private static readonly string[] LayerSprites =
        {
            "Acuarela_Pasto.png", "Acuarela_Tierra.png", "Acuarela_TierraConPiedritas.png",
            "Acuarela_CespedConFlores.png", "Acuarela_Arena.png", "Acuarela_ArenaMojada.png",
        };

        private struct Patch
        {
            public Vector2 Center;
            public float Radius;
            public int Layer;

            public Patch(float x, float z, float radius, int layer)
            {
                Center = new Vector2(x, z);
                Radius = radius;
                Layer = layer;
            }
        }

        // Painted in order: later patches cover earlier ones.
        private static readonly Patch[] Patches =
        {
            new(0f, -9.5f, 10f, LayerDirt),
            new(0f, -24f, 11f, LayerDirt),
            new(28f, -14f, 12f, LayerPebbles),
            new(-28f, -20f, 12f, LayerFlowers),
            new(-30f, 18f, 12f, LayerPebbles),
            new(28f, 26f, 12f, LayerWetSand),
            new(28f, 26f, 8f, LayerSand),
            new(12f, 26f, 2.6f, LayerWetSand),
            new(15f, 26f, 2.6f, LayerWetSand),
            new(18f, 26f, 2.6f, LayerWetSand),
            new(21f, 26f, 2.6f, LayerWetSand),
        };

        // ---- State of the current run ----
        private static Scene _scene;
        private static Transform _root;
        private static int _layerEnv;
        private static int _layerGnomeOnly;
        private static int _layerOccluder;
        private static int _layerWater;
        private static int _groundMask;
        private static readonly Dictionary<string, Material> Materials = new();
        private static readonly List<Vector2> KeyPoints = new();
        private static readonly List<Vector2> DecoyPoints = new();
        private static readonly RaycastHit[] Hits = new RaycastHit[64];
        private static int _bushCount;
        private static Vector3 _canopyCenter;

        // ================================================================== Entry points

        [MenuItem("GnoMore/Construir nivel El Abrazo")]
        public static void Build()
        {
            if (!OpenAbrazo())
                return;

            Materials.Clear();
            KeyPoints.Clear();
            DecoyPoints.Clear();
            _bushCount = 0;
            _layerEnv = LayerMask.NameToLayer("Environment");
            _layerGnomeOnly = LayerMask.NameToLayer("GnomeOnly");
            _layerOccluder = LayerMask.NameToLayer("Occluder");
            _layerWater = LayerMask.NameToLayer("Water");
            _groundMask = 1 << _layerEnv;
            EnsureFolder();

            CleanScene();
            var root = new GameObject(RootName);
            _root = root.transform;
            SceneManager.MoveGameObjectToScene(root, _scene);

            BuildTerrain(Group("Terreno"));
            BuildBorder(Group("Borde"));
            BuildTrees(Group("Arbol"));
            BuildRoots(Group("Raices"));
            BuildRamp(Group("Rampa"));
            BuildCrack(Group("Grieta"));
            BuildSectors(Group("Sectores"));
            Physics.SyncTransforms();
            PlaceGameplay(Group("Items"));
            ScatterCover(Group("Cobertura"));
            BuildHidingSpots(Group("Escondites"));
            BuildDecoys(Group("Senuelos"));
            ConfigureLevelBounds();
            Validate();

            Lightmapping.lightingDataAsset = null;
            FloatingAudit.AuditOpenScene();
            EditorSceneManager.MarkSceneDirty(_scene);
            EditorSceneManager.SaveScene(_scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} Built {AbrazoPath}: {_bushCount} cover bushes, {DecoyPoints.Count} decoys.");
        }

        [MenuItem("GnoMore/Usar El Abrazo como nivel de juego")]
        public static void UseAsGameplay() => SetGameplayScene(AbrazoPath);

        [MenuItem("GnoMore/Volver a CampBryan como nivel de juego")]
        public static void UseBryan() => SetGameplayScene(BryanPath);

        [MenuItem("GnoMore/Capturas de El Abrazo")]
        public static void Capture()
        {
            if (!OpenAbrazo())
                return;

            var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "abrazo");
            Directory.CreateDirectory(dir);
            var cameraObject = new GameObject("TmpCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.7f, 0.85f);
            camera.farClipPlane = 250f;
            var target = new RenderTexture(CaptureSize, CaptureSize, 24);
            camera.targetTexture = target;

            camera.orthographic = true;
            camera.orthographicSize = CaptureTopOrthoSize;
            camera.transform.SetPositionAndRotation(new Vector3(0f, CaptureTopHeight, 0f), Quaternion.Euler(90f, 0f, 0f));
            Save(camera, target, Path.Combine(dir, "cenital.png"));

            camera.orthographic = false;
            camera.fieldOfView = 55f;
            for (var i = 0; i < CaptureSectors.Length; i++)
            {
                var center = CaptureSectors[i];
                var toMap = new Vector3(-center.x, 0f, -center.z);
                toMap = toMap.sqrMagnitude < 1f ? Vector3.back : toMap.normalized;
                var look = center + Vector3.up * CaptureLookHeight;
                camera.transform.position = look - toMap * CaptureSectorDistance + Vector3.up * CaptureSectorHeight;
                camera.transform.LookAt(look);
                Save(camera, target, Path.Combine(dir, $"{CaptureNames[i]}.png"));
            }

            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);
            Debug.Log($"{LogPrefix} Captures written to {dir}.");
        }

        private static void Save(Camera camera, RenderTexture target, string path)
        {
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            RenderTexture.active = previous;
            Object.DestroyImmediate(texture);
        }

        // ================================================================== Scenes

        private static bool HasForeignDirtyScene(string allowedPath)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty && scene.path != allowedPath)
                {
                    Debug.LogWarning($"{LogPrefix} {scene.path} has unsaved changes; save it and run again.");
                    return true;
                }
            }

            return false;
        }

        // Copies CampBryan the first time, then opens CampAbrazo alone. Only CampAbrazo is ever edited.
        private static bool OpenAbrazo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"{LogPrefix} Leave Play Mode first.");
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(AbrazoPath) == null)
            {
                if (!AssetDatabase.CopyAsset(BryanPath, AbrazoPath))
                {
                    Debug.LogError($"{LogPrefix} Could not copy {BryanPath} to {AbrazoPath}.");
                    return false;
                }

                AssetDatabase.Refresh();
            }

            var active = SceneManager.GetActiveScene();
            if (active.path == AbrazoPath && SceneManager.sceneCount == 1)
            {
                _scene = active;
                return true;
            }

            if (HasForeignDirtyScene(AbrazoPath))
                return false;

            _scene = EditorSceneManager.OpenScene(AbrazoPath, OpenSceneMode.Single);
            SceneManager.SetActiveScene(_scene);
            return _scene.IsValid();
        }

        private static void SetGameplayScene(string scenePath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"{LogPrefix} Leave Play Mode first.");
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            if (asset == null)
            {
                Debug.LogWarning($"{LogPrefix} {scenePath} does not exist; run GnoMore/Construir nivel El Abrazo first.");
                return;
            }

            AddToBuildSettings(scenePath);
            if (HasForeignDirtyScene(MainMenuPath))
                return;

            var menu = EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Single);
            var manager = Object.FindFirstObjectByType<ConnectionManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                Debug.LogWarning($"{LogPrefix} No ConnectionManager in {MainMenuPath}.");
                return;
            }

            var serialized = new SerializedObject(manager);
            var property = serialized.FindProperty("_gameplayScene");
            var assetField = property?.FindPropertyRelative("asset");
            var guidField = property?.FindPropertyRelative("guid");
            if (assetField == null || guidField == null)
            {
                Debug.LogError($"{LogPrefix} ConnectionManager._gameplayScene has an unexpected layout.");
                return;
            }

            assetField.objectReferenceValue = asset;
            guidField.stringValue = AssetDatabase.AssetPathToGUID(scenePath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(menu);
            EditorSceneManager.SaveScene(menu);
            Debug.Log($"{LogPrefix} MainMenu now loads {scenePath} as the gameplay scene.");
        }

        private static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var scene in scenes)
            {
                if (scene.path != path)
                    continue;

                if (!scene.enabled)
                {
                    scene.enabled = true;
                    EditorBuildSettings.scenes = scenes.ToArray();
                }

                return;
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(SettingsDir))
                AssetDatabase.CreateFolder("Assets/_Project/Settings", "Abrazo");
        }

        // Removes the old map (and the previous run's output). Systems (GameManager, HUD, lights, day/night, volumes, spawner) stay.
        private static readonly string[] OldRoots =
        {
            RootName, "Greybox", "GardenGnomeDecoys", "TmpShotCam", "bottle-large (1)", "bottle-large", "bottle", "fish",
            "SM_StumpAxe", "Interactuables", "CampSite",
        };

        private static void CleanScene()
        {
            var removed = 0;
            foreach (var go in _scene.GetRootGameObjects())
            {
                if (go == null || !IsOldMap(go))
                    continue;

                Object.DestroyImmediate(go);
                removed++;
            }

            // Hiding spots of the old map that survived inside systems.
            foreach (var spot in Object.FindObjectsByType<HidingSpot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (spot != null && spot.gameObject.scene == _scene)
                {
                    Object.DestroyImmediate(spot.gameObject);
                    removed++;
                }
            }

            Debug.Log($"{LogPrefix} Removed {removed} old-map objects.");
        }

        private static bool IsOldMap(GameObject go)
        {
            if (System.Array.IndexOf(OldRoots, go.name) >= 0 || go.GetComponentInChildren<Terrain>(true) != null)
                return true;

            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            return source != null && AssetDatabase.GetAssetPath(source).Contains("/CampSite");
        }

        // ================================================================== Helpers

        private static Transform Group(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            return go.transform;
        }

        private static float Hash01(float a, float b) => Mathf.Repeat(Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f, 1f);

        private static Material Mat(string name, string hex)
        {
            if (Materials.TryGetValue(name, out var cached) && cached != null)
                return cached;

            ColorUtility.TryParseHtmlString(hex, out var color);
            var path = $"{SettingsDir}/Mat_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(material);
            Materials[name] = material;
            return material;
        }

        private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material, int layer, bool collide = true, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, rotation ?? Quaternion.identity);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.layer = layer;
            if (!collide)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        // A box that spans x0..x1, z0..z1 from the ground (a little below) up to topY.
        private static GameObject Solid(Transform parent, string name, float x0, float x1, float z0, float z1, float topY, Material material, int layer, float bottomY = -StairSink)
        {
            var center = new Vector3((x0 + x1) * 0.5f, (topY + bottomY) * 0.5f, (z0 + z1) * 0.5f);
            return Box(parent, name, center, new Vector3(x1 - x0, topY - bottomY, z1 - z0), material, layer);
        }

        private static GameObject Cylinder(Transform parent, string name, Vector3 center, float diameter, float thickness, Quaternion rotation, Material material, int layer, bool collide)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, rotation);
            go.transform.localScale = new Vector3(diameter, thickness * 0.5f, diameter);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.layer = layer;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (collide)
                go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            return go;
        }

        private static GameObject Sphere(Transform parent, string name, Vector3 center, Vector3 scale, Material material, int layer, bool collide)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.layer = layer;
            if (!collide)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static void MarkFloating(GameObject go) => go.AddComponent<IntentionallyFloating>();

        // Highest Environment surface of this scene under (x, z), ignoring one transform's own colliders.
        private static bool TryGround(float x, float z, Transform ignore, out RaycastHit best)
        {
            best = default;
            var found = false;
            var count = Physics.RaycastNonAlloc(new Vector3(x, GroundProbeTop, z), Vector3.down, Hits, GroundProbeDistance, _groundMask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.collider.gameObject.scene != _scene || (ignore != null && hit.collider.transform.IsChildOf(ignore)))
                    continue;

                if (!found || hit.point.y > best.point.y)
                {
                    best = hit;
                    found = true;
                }
            }

            return found;
        }

        private static bool TryBottom(Transform t, out float minY)
        {
            minY = float.MaxValue;
            var found = false;
            foreach (var collider in t.GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger || !collider.enabled)
                    continue;

                minY = Mathf.Min(minY, collider.bounds.min.y);
                found = true;
            }

            if (found)
                return true;

            foreach (var renderer in t.GetComponentsInChildren<Renderer>())
            {
                minY = Mathf.Min(minY, renderer.bounds.min.y);
                found = true;
            }

            return found;
        }

        // Puts the object's lowest collider (or renderer) point on the ground below (x, z).
        private static bool PlaceOnGround(Transform t, float x, float z, float yaw, bool keepRotation, float lift = 0f)
        {
            // A fresh prefab instance is turned by yaw around its own base pose; an object that already exists keeps its rotation.
            if (!keepRotation)
                t.rotation = Quaternion.Euler(0f, yaw, 0f) * t.rotation;
            t.position = new Vector3(x, 0f, z);
            Physics.SyncTransforms();
            if (!TryGround(x, z, t, out var hit))
            {
                Debug.LogWarning($"{LogPrefix} No ground below {t.name} at ({x}, {z}).", t);
                return false;
            }

            t.position = new Vector3(x, hit.point.y, z);
            Physics.SyncTransforms();
            if (TryBottom(t, out var minY))
                t.position += Vector3.up * (hit.point.y - minY + lift);
            Physics.SyncTransforms();
            return true;
        }

        private static GameObject PlacePrefab(string relativePath, Transform parent, string name, Vector2 spot, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + relativePath);
            if (prefab == null)
            {
                Debug.LogWarning($"{LogPrefix} Missing prefab {PrefabDir}{relativePath}.");
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            PlaceOnGround(go.transform, spot.x, spot.y, yaw, false);
            KeyPoints.Add(spot);
            return go;
        }

        private static GameObject FindInScene(string name)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name == name && t.gameObject.scene == _scene)
                    return t.gameObject;
            }

            Debug.LogWarning($"{LogPrefix} No '{name}' in {_scene.path}.");
            return null;
        }

        // Moves an object that already exists in the copied scene (a system object of the game) onto the ground at a spot.
        private static GameObject MoveExisting(string name, Vector2 spot, float yaw)
        {
            var go = FindInScene(name);
            if (go == null)
                return null;

            PlaceOnGround(go.transform, spot.x, spot.y, yaw, true);
            KeyPoints.Add(spot);
            return go;
        }

        private static Vector3 GroundPoint(Vector2 spot)
        {
            return TryGround(spot.x, spot.y, null, out var hit) ? hit.point : new Vector3(spot.x, 0f, spot.y);
        }

        // ================================================================== Terrain

        private static float Basin(float x, float z, Vector2 center, float flat, float falloff, float depth)
        {
            var d = Vector2.Distance(new Vector2(x, z), center);
            return depth * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flat, flat + falloff, d)));
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        private static float HeightAt(float x, float z)
        {
            var h = Mathf.Min(0f, Basin(x, z, HondonadaCenter, HondonadaFlatRadius, HondonadaFalloff, HondonadaDepth));
            h = Mathf.Min(h, Basin(x, z, PozaCenter, PozaFlatRadius, PozaFalloff, PozaDepth));
            var stream = DistanceToSegment(new Vector2(x, z), StreamStart, StreamEnd);
            var channel = StreamDepth * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StreamInnerHalfWidth, StreamOuterHalfWidth, stream)));
            return Mathf.Min(h, channel);
        }

        private static TerrainLayer LoadLayer(int index)
        {
            var path = $"{SettingsDir}/Layer_{Path.GetFileNameWithoutExtension(LayerSprites[index])}.terrainlayer";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SpritesDir + LayerSprites[index]);
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }

            layer.diffuseTexture = texture;
            layer.tileSize = new Vector2(TerrainTileSize, TerrainTileSize);
            layer.smoothness = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        private static void BuildTerrain(Transform parent)
        {
            var dataPath = $"{SettingsDir}/AbrazoTerrain.asset";
            AssetDatabase.DeleteAsset(dataPath);
            var data = new TerrainData();
            AssetDatabase.CreateAsset(data, dataPath);
            data.heightmapResolution = HeightmapResolution;
            data.size = new Vector3(TerrainSize, TerrainHeightRange, TerrainSize);
            data.alphamapResolution = AlphamapResolution;

            var layers = new TerrainLayer[LayerSprites.Length];
            for (var i = 0; i < layers.Length; i++)
                layers[i] = LoadLayer(i);
            data.terrainLayers = layers;

            var half = TerrainSize * 0.5f;
            var heights = new float[HeightmapResolution, HeightmapResolution];
            for (var zi = 0; zi < HeightmapResolution; zi++)
            {
                for (var xi = 0; xi < HeightmapResolution; xi++)
                {
                    var x = -half + xi / (float)(HeightmapResolution - 1) * TerrainSize;
                    var z = -half + zi / (float)(HeightmapResolution - 1) * TerrainSize;
                    heights[zi, xi] = (HeightAt(x, z) - TerrainBaseY) / TerrainHeightRange;
                }
            }

            data.SetHeights(0, 0, heights);

            var maps = new float[AlphamapResolution, AlphamapResolution, layers.Length];
            var weights = new float[layers.Length];
            for (var zi = 0; zi < AlphamapResolution; zi++)
            {
                for (var xi = 0; xi < AlphamapResolution; xi++)
                {
                    var x = -half + (xi + 0.5f) / AlphamapResolution * TerrainSize;
                    var z = -half + (zi + 0.5f) / AlphamapResolution * TerrainSize;
                    for (var i = 0; i < weights.Length; i++)
                        weights[i] = 0f;
                    weights[LayerGrass] = 1f;
                    foreach (var patch in Patches)
                    {
                        var d = Vector2.Distance(new Vector2(x, z), patch.Center);
                        var a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(patch.Radius * 0.55f, patch.Radius, d));
                        for (var i = 0; i < weights.Length; i++)
                            weights[i] *= 1f - a;
                        weights[patch.Layer] += a;
                    }

                    for (var i = 0; i < weights.Length; i++)
                        maps[zi, xi, i] = weights[i];
                }
            }

            data.SetAlphamaps(0, 0, maps);
            EditorUtility.SetDirty(data);

            var terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Terreno";
            terrainObject.layer = _layerEnv;
            terrainObject.transform.SetParent(parent, false);
            terrainObject.transform.position = new Vector3(-half, TerrainBaseY, -half);
            Physics.SyncTransforms();

            // Visual water of the pool: no collider, so the camper wades through it.
            var water = Cylinder(parent, "Agua", new Vector3(PozaCenter.x, WaterTopY - WaterThickness * 0.5f, PozaCenter.y),
                WaterRadius * 2f, WaterThickness, Quaternion.identity, Mat("Agua", WaterHex), _layerWater, false);
            water.name = "Agua";
        }

        // ================================================================== Border

        private static void BuildBorder(Transform parent)
        {
            var invisible = Group("MurosInvisibles");
            invisible.SetParent(parent, false);
            var clear = Mat("Invisible", "#FFFFFF");
            var halfHeight = InvisibleWallHeight * 0.5f;
            var spanZ = InvisibleWallZ * 2f + InvisibleWallThickness * 2f;
            var spanX = InvisibleWallX * 2f + InvisibleWallThickness * 2f;
            Box(invisible, "MuroEste", new Vector3(InvisibleWallX + InvisibleWallThickness * 0.5f, halfHeight - 3f, 0f), new Vector3(InvisibleWallThickness, InvisibleWallHeight, spanZ), clear, _layerEnv, true).GetComponent<MeshRenderer>().enabled = false;
            Box(invisible, "MuroOeste", new Vector3(-InvisibleWallX - InvisibleWallThickness * 0.5f, halfHeight - 3f, 0f), new Vector3(InvisibleWallThickness, InvisibleWallHeight, spanZ), clear, _layerEnv, true).GetComponent<MeshRenderer>().enabled = false;
            Box(invisible, "MuroNorte", new Vector3(0f, halfHeight - 3f, InvisibleWallZ + InvisibleWallThickness * 0.5f), new Vector3(spanX, InvisibleWallHeight, InvisibleWallThickness), clear, _layerEnv, true).GetComponent<MeshRenderer>().enabled = false;
            Box(invisible, "MuroSur", new Vector3(0f, halfHeight - 3f, -InvisibleWallZ - InvisibleWallThickness * 0.5f), new Vector3(spanX, InvisibleWallHeight, InvisibleWallThickness), clear, _layerEnv, true).GetComponent<MeshRenderer>().enabled = false;

            // Ring of trees just outside the walls, in two staggered rows.
            var trees = Group("Arboles");
            trees.SetParent(parent, false);
            var index = 0;
            for (var row = 0; row < 2; row++)
            {
                var offset = row * TreeRowGap;
                var halfX = TreeRingX + offset;
                var halfZ = TreeRingZ + offset;
                for (var x = -halfX; x <= halfX; x += TreeSpacing)
                {
                    PlaceRingTree(trees, x, halfZ, index++);
                    PlaceRingTree(trees, x + TreeSpacing * 0.5f, -halfZ, index++);
                }

                for (var z = -halfZ + TreeSpacing; z < halfZ; z += TreeSpacing)
                {
                    PlaceRingTree(trees, halfX, z, index++);
                    PlaceRingTree(trees, -halfX, z + TreeSpacing * 0.5f, index++);
                }
            }
        }

        private static void PlaceRingTree(Transform parent, float x, float z, int index)
        {
            var jitterX = (Hash01(x, z) - 0.5f) * 2.5f;
            var jitterZ = (Hash01(z, x) - 0.5f) * 2.5f;
            var kind = 1 + Mathf.FloorToInt(Hash01(x + 3f, z + 7f) * 4f) % 4;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}Arbol_0{kind}.prefab");
            if (prefab == null)
                return;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = $"Arbol_{index:000}";
            var scale = 0.9f + Hash01(z + 1f, x + 2f) * 0.35f;
            go.transform.localScale *= scale;
            var height = HeightAt(x + jitterX, z + jitterZ);
            go.transform.SetPositionAndRotation(new Vector3(x + jitterX, height, z + jitterZ), Quaternion.Euler(0f, Hash01(x, z + 5f) * 360f, 0f));
        }

        // ================================================================== Twin trees

        private static Vector2 Dir2 => (SlitEnd - SlitStart).normalized;

        // Mesh of a convex polygon (x, z) extruded between two heights. Flat shading; winding is made clockwise seen from above.
        private static Mesh PrismMesh(string name, List<Vector2> polygon, float y0, float y1)
        {
            var area = 0f;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }

            var points = new List<Vector2>(polygon);
            if (area > 0f)
                points.Reverse();

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            const float uvScale = 0.2f;

            var top = vertices.Count;
            foreach (var p in points)
            {
                vertices.Add(new Vector3(p.x, y1, p.y));
                uvs.Add(p * uvScale);
            }

            for (var i = 1; i < points.Count - 1; i++)
            {
                triangles.Add(top);
                triangles.Add(top + i);
                triangles.Add(top + i + 1);
            }

            var bottom = vertices.Count;
            foreach (var p in points)
            {
                vertices.Add(new Vector3(p.x, y0, p.y));
                uvs.Add(p * uvScale);
            }

            for (var i = 1; i < points.Count - 1; i++)
            {
                triangles.Add(bottom);
                triangles.Add(bottom + i + 1);
                triangles.Add(bottom + i);
            }

            var along = 0f;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                var length = Vector2.Distance(a, b);
                var start = vertices.Count;
                vertices.Add(new Vector3(a.x, y0, a.y));
                vertices.Add(new Vector3(b.x, y0, b.y));
                vertices.Add(new Vector3(b.x, y1, b.y));
                vertices.Add(new Vector3(a.x, y1, a.y));
                uvs.Add(new Vector2(along * uvScale, y0 * uvScale));
                uvs.Add(new Vector2((along + length) * uvScale, y0 * uvScale));
                uvs.Add(new Vector2((along + length) * uvScale, y1 * uvScale));
                uvs.Add(new Vector2(along * uvScale, y1 * uvScale));
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
                along += length;
            }

            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh SaveMesh(Mesh mesh)
        {
            var path = $"{SettingsDir}/Mesh_{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }

            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // Half of the round trunk on one side of the slit band: side +1 is the pale one (north-east), -1 the dark one.
        private static List<Vector2> TrunkHalf(float side)
        {
            var dir = Dir2;
            var normal = new Vector2(-dir.y, dir.x);
            var along = Mathf.Sqrt(TrunkRadius * TrunkRadius - SlitHalfWidth * SlitHalfWidth);
            var e1 = normal * (side * SlitHalfWidth) + dir * along;
            var e2 = normal * (side * SlitHalfWidth) - dir * along;
            var a1 = Mathf.Atan2(e1.y, e1.x);
            var a2 = Mathf.Atan2(e2.y, e2.x);
            if (side > 0f)
            {
                while (a2 < a1)
                    a2 += Mathf.PI * 2f;
            }
            else
            {
                while (a2 > a1)
                    a2 -= Mathf.PI * 2f;
            }

            var polygon = new List<Vector2>();
            for (var i = 0; i <= TrunkArcSegments; i++)
            {
                var angle = Mathf.Lerp(a1, a2, i / (float)TrunkArcSegments);
                polygon.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * TrunkRadius);
            }

            return polygon;
        }

        private static void BuildTrunk(Transform parent, string name, float side, Material material)
        {
            var mesh = SaveMesh(PrismMesh(name, TrunkHalf(side), -TrunkSink, TrunkTopY));
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = _layerEnv;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static void BuildTrees(Transform parent)
        {
            BuildTrunk(parent, "TroncoOscuro", -1f, Mat("CortezaOscura", BarkDarkHex));
            BuildTrunk(parent, "TroncoPalido", 1f, Mat("CortezaPalida", BarkPaleHex));
            BuildSlit(parent);
            BuildRibbons(parent);
            BuildCanopy(parent);
        }

        // The slit: roof at 0.8 m over its whole length, plus low side walls where the trunks do not form them.
        private static void BuildSlit(Transform parent)
        {
            var slit = Group("Rendija");
            slit.SetParent(parent, false);
            var dir = Dir2;
            var direction = new Vector3(dir.x, 0f, dir.y);
            var rotation = Quaternion.LookRotation(direction);
            var length = Vector2.Distance(SlitStart, SlitEnd);
            var middle = (SlitStart + SlitEnd) * 0.5f;
            var roofWidth = SlitHalfWidth * 2f + SlitWallThickness * 2f;
            var roof = Box(slit, "Techo", new Vector3(middle.x, SlitClearHeight + SlitRoofThickness * 0.5f, middle.y),
                new Vector3(roofWidth, SlitRoofThickness, length), Mat("TechoRendija", RoofHex), _layerEnv, true, rotation);
            MarkFloating(roof); // Held by the trunks and by the side walls.

            // Part of the slit inside the trunk circle: the trunks themselves are its walls.
            var closest = Vector2.Dot(-SlitStart, dir);
            var onLine = SlitStart + dir * closest;
            var halfChord = Mathf.Sqrt(Mathf.Max(0f, TrunkRadius * TrunkRadius - onLine.sqrMagnitude));
            BuildSlitWalls(slit, rotation, 0f, closest - halfChord);
            BuildSlitWalls(slit, rotation, closest + halfChord, length);
        }

        private static void BuildSlitWalls(Transform parent, Quaternion rotation, float from, float to)
        {
            if (to - from < 0.05f)
                return;

            var dir = Dir2;
            var normal = new Vector2(-dir.y, dir.x);
            var middle = SlitStart + dir * ((from + to) * 0.5f);
            var offset = SlitHalfWidth + SlitWallThickness * 0.5f;
            var pale = middle + normal * offset;
            var dark = middle - normal * offset;
            var size = new Vector3(SlitWallThickness, SlitWallHeight, to - from);
            Box(parent, "ParedPalida", new Vector3(pale.x, SlitWallHeight * 0.5f - StairSink * 0.5f, pale.y), size, Mat("RaizPalida", RootPaleHex), _layerEnv, true, rotation);
            Box(parent, "ParedOscura", new Vector3(dark.x, SlitWallHeight * 0.5f - StairSink * 0.5f, dark.y), size, Mat("RaizOscura", RootDarkHex), _layerEnv, true, rotation);
        }

        // Visual braid: helical ribbons around both trunks, no colliders.
        private static void BuildRibbons(Transform parent)
        {
            var ribbons = Group("Cintas");
            ribbons.SetParent(parent, false);
            MarkFloating(ribbons.gameObject);
            var dir = Dir2;
            var normal = new Vector2(-dir.y, dir.x);
            var radius = TrunkRadius + RibbonSize * 0.3f;
            for (var r = 0; r < RibbonCount; r++)
            {
                var phase = r * Mathf.PI * 2f / RibbonCount;
                Vector3? previous = null;
                for (var i = 0; i <= RibbonSegments; i++)
                {
                    var u = i / (float)RibbonSegments;
                    var angle = phase + u * RibbonTurns * Mathf.PI * 2f;
                    var point = new Vector3(Mathf.Cos(angle) * radius, Mathf.Lerp(RibbonStartY, RibbonEndY, u), Mathf.Sin(angle) * radius);
                    if (previous.HasValue)
                    {
                        var delta = point - previous.Value;
                        var pale = Vector2.Dot(new Vector2(point.x, point.z), normal) > 0f;
                        Box(ribbons, $"Cinta{r}_{i}", (point + previous.Value) * 0.5f, new Vector3(RibbonSize, RibbonSize, delta.magnitude + 0.05f),
                            pale ? Mat("CortezaPalida", BarkPaleHex) : Mat("CortezaOscura", BarkDarkHex), _layerEnv, false, Quaternion.LookRotation(delta));
                    }

                    previous = point;
                }
            }
        }

        // Visual crown: flattened green spheres to the south, pale branches to the north. No colliders.
        private static void BuildCanopy(Transform parent)
        {
            var canopy = Group("Copa");
            canopy.SetParent(parent, false);
            MarkFloating(canopy.gameObject);
            var leaf = Mat("Hojas", LeafHex);
            Vector3[] blobs = { new(-3f, 15.5f, -4f), new(2.5f, 16f, -6f), new(-6.5f, 15f, 0f), new(0f, 17f, -1.5f), new(5f, 15.5f, -2f) };
            for (var i = 0; i < blobs.Length; i++)
                Sphere(canopy, $"Hojas{i}", blobs[i], new Vector3(7f, 3.2f, 7f), leaf, _layerEnv, false);

            var branch = Mat("RamaPalida", BranchHex);
            Vector3[] branches = { new(0f, 15f, 5f), new(-3f, 15.5f, 4f), new(3.5f, 14.8f, 3f) };
            var angles = new[] { 15f, -25f, 40f };
            for (var i = 0; i < branches.Length; i++)
                Box(canopy, $"Rama{i}", branches[i], new Vector3(0.8f, 0.8f, 7f), branch, _layerEnv, false, Quaternion.Euler(-10f, angles[i], 0f));
        }

        // ================================================================== Roots

        private static void BuildRoots(Transform parent)
        {
            var darkRoot = Mat("RaizOscura", RootDarkHex);
            var paleRoot = Mat("RaizPalida", RootPaleHex);
            RootWall(parent, "RaizOeste", -RootTipX, -RootWestStartX, -TunnelX, darkRoot);
            RootWall(parent, "RaizEste", RootEastStartX, RootTipX, TunnelX, paleRoot);

            // The fire's U: open to the south, with a 0.4 m peephole in the east wall that faces the slit's south exit.
            var u = Group("UFogata");
            u.SetParent(parent, false);
            Solid(u, "UOeste", -FireU_OuterX, -FireU_InnerX, FireU_FrontZ, FireU_BackOuterZ, RootHeight, darkRoot, _layerEnv);
            Solid(u, "UNorte", -FireU_OuterX, FireU_OuterX, FireU_BackZ, FireU_BackOuterZ, RootHeight, darkRoot, _layerEnv);
            var half = PeepholeSize * 0.5f;
            var holeFrom = PeepholeCenterZ - half;
            var holeTo = PeepholeCenterZ + half;
            Solid(u, "UEsteSur", FireU_InnerX, FireU_OuterX, FireU_FrontZ, holeFrom, RootHeight, paleRoot, _layerEnv);
            Solid(u, "UEsteNorte", FireU_InnerX, FireU_OuterX, holeTo, FireU_BackOuterZ, RootHeight, paleRoot, _layerEnv);
            Solid(u, "UEsteMirillaBase", FireU_InnerX, FireU_OuterX, holeFrom, holeTo, PeepholeSillY, paleRoot, _layerEnv);
            MarkFloating(Solid(u, "UEsteMirillaDintel", FireU_InnerX, FireU_OuterX, holeFrom, holeTo, RootHeight, paleRoot, _layerEnv, PeepholeSillY + PeepholeSize));
        }

        // A root wall along x between x0 and x1 (thickness 2 m in z) with a gnome tunnel at tunnelX.
        private static void RootWall(Transform parent, string name, float x0, float x1, float tunnelX, Material material)
        {
            var wall = Group(name);
            wall.SetParent(parent, false);
            var half = TunnelWidth * 0.5f;
            var left = Mathf.Min(tunnelX - half, x1);
            Solid(wall, "TramoA", x0, Mathf.Min(x1, tunnelX - half), -RootHalfThickness, RootHalfThickness, RootHeight, material, _layerEnv);
            Solid(wall, "TramoB", Mathf.Max(x0, tunnelX + half), x1, -RootHalfThickness, RootHalfThickness, RootHeight, material, _layerEnv);
            MarkFloating(Solid(wall, "Dintel", tunnelX - half, tunnelX + half, -RootHalfThickness, RootHalfThickness, RootHeight, material, _layerEnv, TunnelHeight));
            if (left < x0)
                Debug.LogWarning($"{LogPrefix} Tunnel of {name} lies outside the wall.");
        }

        // ================================================================== Ramp

        private static float RampRadiusAt(float degrees)
        {
            if (degrees <= 0f)
                return RampArcStartRadius;

            return degrees >= RampArcBlendDeg ? RampRadius : Mathf.Lerp(RampArcStartRadius, RampRadius, Mathf.SmoothStep(0f, 1f, degrees / RampArcBlendDeg));
        }

        private static float RampY(float degrees)
        {
            return degrees <= ShoulderDeg
                ? Mathf.Lerp(RampEntryEndY, RampShoulderY, degrees / ShoulderDeg)
                : Mathf.Lerp(RampShoulderY, RampTopY, (degrees - ShoulderDeg) / (RampEndDeg - ShoulderDeg));
        }

        // A point of the arc part of the ramp (degrees from +x towards +z; 0 is where the entry bridge ends).
        private static Vector3 RampPos(float degrees)
        {
            var angle = degrees * Mathf.Deg2Rad;
            var radius = RampRadiusAt(degrees);
            return new Vector3(Mathf.Cos(angle) * radius, RampY(degrees), Mathf.Sin(angle) * radius);
        }

        private static Quaternion RampRotation(float degrees)
        {
            var direction = RampPos(degrees + 1f) - RampPos(degrees - 1f);
            return Quaternion.LookRotation(direction.normalized);
        }

        // Points that must stay free of ramp posts: inside the fire's U and along the slit.
        private static bool PostBlocked(float x, float z)
        {
            if (Mathf.Abs(x) < FireU_OuterX + 2f && z > FireU_FrontZ - 2f && z < FireU_BackOuterZ + 2f)
                return true;

            return DistanceToSegment(new Vector2(x, z), SlitStart, SlitEnd) < 2f;
        }

        // Entry straight (12.5, -24, Y0) to (12.5, 0, Y3.2): a bridge over the east root; then the arc round the trunks.
        private static void BuildRamp(Transform parent)
        {
            var ramp = Group("Tramos");
            ramp.SetParent(parent, false);
            MarkFloating(ramp.gameObject); // The decks are held by the posts at their outer edge and by the platforms; the second lap passes over the first.
            var deckA = Mat("Tablas", WoodHex);
            var deckB = Mat("TablasViejas", WoodOldHex);
            var posts = Group("Postes");
            posts.SetParent(parent, false);

            var path = new List<Vector3>();
            var degrees = new List<float>();
            for (var k = 0; k <= RampEntrySegments; k++)
            {
                path.Add(new Vector3(RampEntryX, RampEntryEndY * k / RampEntrySegments, RampEntryStartZ * (1f - k / (float)RampEntrySegments)));
                degrees.Add(-1f);
            }

            for (var j = 1; j <= RampArcSegments; j++)
            {
                var d = RampEndDeg * j / RampArcSegments;
                path.Add(RampPos(d));
                degrees.Add(d);
            }

            for (var k = 0; k + 1 < path.Count; k++)
            {
                var p0 = path[k];
                var p1 = path[k + 1];
                var rotation = Quaternion.LookRotation((p1 - p0).normalized);
                var normal = rotation * Vector3.up;
                var length = (p1 - p0).magnitude;
                var center = (p0 + p1) * 0.5f - normal * (RampThickness * 0.5f);
                Box(ramp, $"Tramo_{k:00}", center, new Vector3(RampWidth, RampThickness, length + RampOverlap), k % 2 == 0 ? deckA : deckB, _layerEnv, true, rotation);

                // Low visual rail on the outer edge (no collider).
                var outward = rotation * Vector3.right;
                var railCenter = (p0 + p1) * 0.5f + outward * (RampWidth * 0.5f - 0.04f) + normal * (RampRailHeight * 0.5f);
                Box(ramp, $"Baranda_{k:00}", railCenter, new Vector3(0.08f, RampRailHeight, length), deckB, _layerEnv, false, rotation);

                // Post at the end of the tramo, under the outer edge of the deck, down to the ground.
                if (degrees[k + 1] < SecondLapDeg)
                {
                    outward.y = 0f;
                    outward.Normalize();
                    BuildPost(posts, k + 1, p1 + outward * RampPostOffset, p1.y);
                }
            }

            BuildShoulder(ramp);
            BuildCanopyPlatform(ramp, parent);
        }

        private static void BuildPost(Transform parent, int index, Vector3 at, float deckY)
        {
            // Not in the root wall (the bridge passes over it), the fire's U or the slit.
            var inRoot = Mathf.Abs(at.z) < RootHalfThickness + 0.6f && Mathf.Abs(at.x) > RootWestStartX;
            if (deckY < 0.6f || inRoot || PostBlocked(at.x, at.z))
                return;

            var ground = HeightAt(at.x, at.z);
            var top = deckY - RampThickness;
            Box(parent, $"Poste_{index:00}", new Vector3(at.x, (top + ground - StairSink) * 0.5f, at.z), new Vector3(RampPostSize, top - ground + StairSink, RampPostSize),
                Mat("TablasViejas", WoodOldHex), _layerEnv);
        }

        // A round platform lying in the plane of the ramp: no step to climb onto it.
        private static void BuildShoulder(Transform parent)
        {
            var point = RampPos(ShoulderDeg);
            var rotation = RampRotation(ShoulderDeg);
            var center = point - rotation * Vector3.up * (PlatformThickness * 0.5f);
            Cylinder(parent, "Hombro", center, ShoulderRadius * 2f, PlatformThickness, rotation, Mat("Tablas", WoodHex), _layerEnv, true);
        }

        // Flat round platform where the ramp ends, at the top (Y 10), with the smoke column on it.
        private static void BuildCanopyPlatform(Transform ramp, Transform parent)
        {
            var end = RampPos(RampEndDeg);
            var tangent = RampPos(RampEndDeg + 1f) - RampPos(RampEndDeg - 1f);
            tangent.y = 0f;
            tangent.Normalize();
            _canopyCenter = new Vector3(end.x + tangent.x * CanopyPlatformRadius, RampTopY, end.z + tangent.z * CanopyPlatformRadius);
            Cylinder(ramp, "CopaPlataforma", _canopyCenter + Vector3.down * (PlatformThickness * 0.5f), CanopyPlatformRadius * 2f, PlatformThickness,
                Quaternion.identity, Mat("Tablas", WoodHex), _layerEnv, true);
            BuildSmoke(Group("Humo"), parent);
        }

        private static void BuildSmoke(Transform group, Transform parent)
        {
            group.SetParent(parent, false);
            var go = new GameObject("HumoCopa");
            go.transform.SetParent(group, false);
            go.transform.SetPositionAndRotation(_canopyCenter + Vector3.up * 0.4f, Quaternion.Euler(-90f, 0f, 0f));
            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = 9f;
            main.startSpeed = 2.5f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.02f;
            main.maxParticles = 400;
            var emission = system.emission;
            emission.rateOverTime = 14f;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = 0.6f;
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2f));
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 0f), new GradientColorKey(new Color(0.6f, 0.6f, 0.6f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SmokeMaterialPath);
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var beacon = go.AddComponent<FireSmokeBeacon>();
            var serialized = new SerializedObject(beacon);
            serialized.FindProperty("_smoke").objectReferenceValue = system;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ================================================================== Gnome-only crack and the tower ledge

        private static Vector3 OnCircle(float radius, float degrees, float y)
        {
            var angle = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }

        private static void BuildCrack(Transform parent)
        {
            var crack = Group("GrietaGnomos");
            crack.SetParent(parent, false);
            var rock = Mat("Piedra", RockHex);
            for (var k = 1; k <= CrackLedges; k++)
            {
                var degrees = CrackStartDeg + CrackLedgeStepDeg * (k - 0.5f);
                var top = CrackLedgeHeight * k;
                var center = OnCircle(CrackRadius, degrees, (top - StairSink) * 0.5f);
                var tangent = new Vector3(-Mathf.Sin(degrees * Mathf.Deg2Rad), 0f, Mathf.Cos(degrees * Mathf.Deg2Rad));
                var depth = CrackLedgeStepDeg * Mathf.Deg2Rad * CrackRadius;
                Box(crack, $"Repisa_{k}", center, new Vector3(CrackLedgeWidth, top + StairSink, depth), rock, _layerGnomeOnly, true, Quaternion.LookRotation(tangent));
            }

            var inclineStart = CrackStartDeg + CrackLedgeStepDeg * CrackLedges;
            var startY = CrackLedgeHeight * CrackLedges;
            var endY = RampPos(ShoulderDeg).y;
            for (var j = 0; j < CrackInclineParts; j++)
            {
                var d0 = Mathf.Lerp(inclineStart, ShoulderDeg, j / (float)CrackInclineParts);
                var d1 = Mathf.Lerp(inclineStart, ShoulderDeg, (j + 1) / (float)CrackInclineParts);
                var p0 = OnCircle(CrackRadius, d0, Mathf.Lerp(startY, endY, j / (float)CrackInclineParts));
                var p1 = OnCircle(CrackRadius, d1, Mathf.Lerp(startY, endY, (j + 1) / (float)CrackInclineParts));
                var rotation = Quaternion.LookRotation((p1 - p0).normalized);
                var length = (p1 - p0).magnitude;
                var center = (p0 + p1) * 0.5f - rotation * Vector3.up * (RampThickness * 0.5f);
                var part = Box(crack, $"Tramo_{j}", center, new Vector3(CrackInclineWidth, RampThickness, length + 0.3f), rock, _layerGnomeOnly, true, rotation);
                MarkFloating(part);
            }

            // Ledge at 1.6 m: only a tower of gnomes reaches it (Environment layer, so everyone collides with it).
            var half = TowerLedgeSize * 0.5f;
            Solid(parent, "RepisaTorre", TowerLedge.x - half, TowerLedge.x + half, TowerLedge.y - half, TowerLedge.y + half, TowerLedgeTopY, rock, _layerEnv);
            BuildTowerStairs(crack, rock);
        }

        // Gnome-only stairs of 0.4 m steps from the tower ledge to the shoulder. They run outside the ramp's band (r 9..11)
        // at r 12.2, so they never cross the ramp; a landing at ledge height links the ledge to the first step.
        private static void BuildTowerStairs(Transform parent, Material material)
        {
            var rise = RampPos(ShoulderDeg).y - TowerLedgeTopY;
            var steps = Mathf.CeilToInt(rise / GnomeStepHeight - 0.001f);
            var each = rise / steps;
            var span = TowerStairEndDeg - TowerStairStartDeg;
            for (var k = 1; k <= steps; k++)
            {
                var degrees = TowerStairStartDeg + span * (k - 0.5f) / steps;
                var top = TowerLedgeTopY + each * k;
                var depth = span * Mathf.Deg2Rad * TowerStairRadius / steps;
                var center = OnCircle(TowerStairRadius, degrees, (top - StairSink) * 0.5f);
                var tangent = new Vector3(-Mathf.Sin(degrees * Mathf.Deg2Rad), 0f, Mathf.Cos(degrees * Mathf.Deg2Rad));
                Box(parent, $"EscaleraTorre_{k}", center, new Vector3(TowerStairWidth, top + StairSink, depth + 0.02f), material, _layerGnomeOnly, true, Quaternion.LookRotation(tangent));
            }

            var firstStep = OnCircle(TowerStairRadius, TowerStairStartDeg, 0f);
            var ledge = new Vector3(TowerLedge.x, 0f, TowerLedge.y);
            var along = firstStep - ledge;
            var landingCenter = (firstStep + ledge) * 0.5f;
            landingCenter.y = (TowerLedgeTopY - StairSink) * 0.5f;
            Box(parent, "DescansoTorre", landingCenter, new Vector3(TowerStairWidth, TowerLedgeTopY + StairSink, along.magnitude), material, _layerGnomeOnly, true, Quaternion.LookRotation(along.normalized));
        }

        // ================================================================== Stairs

        // Solid steps (down to the ground) climbing along dir from start; start.y is the lower surface.
        private static void Stairs(Transform parent, string name, Vector3 start, Vector3 dir, float width, float rise, float stepHeight, int layer, Material material)
        {
            dir.y = 0f;
            dir.Normalize();
            var steps = Mathf.Max(1, Mathf.CeilToInt(rise / stepHeight - 0.001f));
            var rotation = Quaternion.LookRotation(dir);
            var each = rise / steps;
            var total = steps * StepDepth;
            for (var k = 1; k <= steps; k++)
            {
                var top = start.y + each * k;
                var length = total - (k - 1) * StepDepth;
                var along = (k - 1) * StepDepth + length * 0.5f;
                var center = start + dir * along;
                center.y = (top - StairSink) * 0.5f;
                Box(parent, $"{name}_{k}", center, new Vector3(width, top + StairSink, length), material, layer, true, rotation);
            }
        }

        // ================================================================== Sectors

        private static void BuildSectors(Transform parent)
        {
            BuildCamp(Sector(parent, "Campamento"));
            BuildSawmill(Sector(parent, "Serreria"));
            BuildOrchard(Sector(parent, "Huerto"));
            BuildCrag(Sector(parent, "Risco"));
            BuildHollow(Sector(parent, "Hondonada"));
            BuildPool(Sector(parent, "Poza"));

            var signs = Group("Rotulos");
            signs.SetParent(parent, false);
            Sign(signs, "Campamento", new Vector2(0f, -19f), "#E0903A");
            Sign(signs, "Serreria", new Vector2(24f, -10f), "#D9C24A");
            Sign(signs, "Huerto", new Vector2(-24f, -16f), "#D874A8");
            Sign(signs, "Risco", new Vector2(-26f, 12f), "#7C8FA8");
            Sign(signs, "Hondonada", new Vector2(0f, 18f), "#5E9E52");
            Sign(signs, "Poza", new Vector2(22f, 16f), "#D8C48A");
        }

        private static Transform Sector(Transform parent, string name)
        {
            var group = Group(name);
            group.SetParent(parent, false);
            return group;
        }

        private static void Sign(Transform parent, string sector, Vector2 spot, string hex)
        {
            var ground = GroundPoint(spot);
            var post = Box(parent, $"Rotulo_{sector}", new Vector3(spot.x, ground.y + 1.1f - StairSink * 0.5f, spot.y), new Vector3(0.15f, 2.2f + StairSink, 0.15f), Mat("PosteRotulo", SignPostHex), _layerEnv);
            var board = Box(post.transform, "Tablero", new Vector3(spot.x, ground.y + 1.9f, spot.y), new Vector3(1.4f, 0.5f, 0.1f), Mat($"Rotulo{sector}", hex), _layerEnv, false);
            board.transform.localScale = new Vector3(1.4f / 0.15f, 0.5f / 2.2f, 0.1f / 0.15f);
            MarkFloating(board);
            KeyPoints.Add(spot);
        }

        private static void BuildCamp(Transform group)
        {
            for (var i = 0; i < TentSpots.Length; i++)
                PlacePrefab("Tent.prefab", group, $"Tienda_{i + 1}", TentSpots[i], i == 0 ? 20f : -35f);
        }

        private static void BuildSawmill(Transform group)
        {
            var wood = Mat("Tablas", WoodHex);
            var old = Mat("TablasViejas", WoodOldHex);
            // Low walkway (Y 1.5): x 24..32, z -16..-12, on posts so gnomes can hide beneath the boards.
            var lowTop = SawmillLowY;
            Box(group, "PasarelaBaja", new Vector3(SawmillCenter.x, lowTop - SawmillDeckThickness * 0.5f, SawmillCenter.y), new Vector3(8f, SawmillDeckThickness, 4f), wood, _layerEnv);
            foreach (var p in new[] { new Vector2(24.3f, -15.7f), new Vector2(31.7f, -15.7f), new Vector2(24.3f, -12.3f), new Vector2(31.7f, -12.3f), new Vector2(28f, -14f) })
                Post(group, "PostePasarelaBaja", p, lowTop - SawmillDeckThickness, old);

            // High walkway (Y 3): x 35.5..38.5, z -16..-12.
            Box(group, "PasarelaAlta", new Vector3(37f, SawmillHighY - SawmillDeckThickness * 0.5f, -14f), new Vector3(3f, SawmillDeckThickness, 4f), wood, _layerEnv);
            foreach (var p in new[] { new Vector2(36f, -15.5f), new Vector2(38f, -12.5f), new Vector2(37f, -14f) })
                Post(group, "PostePasarelaAlta", p, SawmillHighY - SawmillDeckThickness, old);

            // Camper stairs (0.3 m steps): ground -> low walkway from the south, low -> high walkway from the west.
            Stairs(group, "EscaleraSur", new Vector3(26f, 0f, -19.5f), Vector3.forward, 2f, SawmillLowY, CamperStepHeight, _layerEnv, wood);
            Stairs(group, "EscaleraAlta", new Vector3(32f, SawmillLowY, -14f), Vector3.right, 2f, SawmillHighY - SawmillLowY, CamperStepHeight, _layerEnv, wood);

            // Gentle ramp from the north that gnomes (step 0.1) can climb to the low walkway.
            var p0 = new Vector3(30.5f, 0f, -8f);
            var p1 = new Vector3(30.5f, SawmillLowY, -12f);
            var rotation = Quaternion.LookRotation((p1 - p0).normalized);
            var ramp = Box(group, "RampaGnomos", (p0 + p1) * 0.5f - rotation * Vector3.up * (RampThickness * 0.5f),
                new Vector3(1.4f, RampThickness, (p1 - p0).magnitude + 0.2f), wood, _layerEnv, true, rotation);
            MarkFloating(ramp);
        }

        private static void Post(Transform parent, string name, Vector2 spot, float topY, Material material)
        {
            var ground = HeightAt(spot.x, spot.y);
            Box(parent, name, new Vector3(spot.x, (topY + ground - StairSink) * 0.5f, spot.y), new Vector3(0.3f, topY - ground + StairSink, 0.3f), material, _layerEnv);
        }

        private static void BuildOrchard(Transform group)
        {
            var bed = Mat("Cama", BedHex);
            var plant = Mat("Hojas", LeafHex);
            foreach (var z in new[] { -17.5f, -20f, -26f, -28.5f })
            {
                Solid(group, "Bancal", -32.5f, -25.5f, z - 0.6f, z + 0.6f, OrchardBedHeight, bed, _layerEnv);
                for (var i = 0; i < 5; i++)
                {
                    var x = -31.5f + i * 1.5f;
                    Sphere(group, "Planta", new Vector3(x, OrchardBedHeight + 0.2f, z), new Vector3(0.7f, 0.5f, 0.7f), plant, _layerEnv, false);
                }
            }

            PlacePrefab("Tocon.prefab", group, "Tocon_1", new Vector2(-23f, -22f), 10f);
            PlacePrefab("Tocon.prefab", group, "Tocon_2", new Vector2(-34f, -24f), 140f);
        }

        private static void BuildCrag(Transform group)
        {
            var rock = Mat("Piedra", RockHex);
            Solid(group, "TerrazaBaja", -34f, -22f, 10f, 18f, CragTerraceLowY, rock, _layerEnv);
            Solid(group, "TerrazaMedia", -37f, -27f, 17f, 23f, CragTerraceMidY, rock, _layerEnv);
            Solid(group, "TerrazaAlta", -34f, -26f, 24f, 30f, CragTerraceHighY, rock, _layerEnv);

            // Camper stairs (0.3 m steps) and gnome stairs (0.4 m steps) between levels.
            Stairs(group, "EscaleraA", new Vector3(-26f, 0f, 6.5f), Vector3.forward, 2f, CragTerraceLowY, CamperStepHeight, _layerEnv, rock);
            Stairs(group, "EscaleraGnomoA", new Vector3(-23f, 0f, 7.2f), Vector3.forward, 1.2f, CragTerraceLowY, GnomeStepHeight, _layerGnomeOnly, rock);
            Stairs(group, "EscaleraB", new Vector3(-32f, CragTerraceLowY, 13.5f), Vector3.forward, 2f, CragTerraceMidY - CragTerraceLowY, CamperStepHeight, _layerEnv, rock);
            Stairs(group, "EscaleraGnomoB", new Vector3(-29f, CragTerraceLowY, 14.2f), Vector3.forward, 1.2f, CragTerraceMidY - CragTerraceLowY, GnomeStepHeight, _layerGnomeOnly, rock);
            Stairs(group, "EscaleraC", new Vector3(-31f, CragTerraceMidY, 20.5f), Vector3.forward, 2f, CragTerraceHighY - CragTerraceMidY, CamperStepHeight, _layerEnv, rock);
            Stairs(group, "EscaleraGnomoC", new Vector3(-28f, CragTerraceMidY, 21.2f), Vector3.forward, 1.2f, CragTerraceHighY - CragTerraceMidY, GnomeStepHeight, _layerGnomeOnly, rock);

            PlacePrefab("Rock_01.prefab", group, "Roca_1", new Vector2(-24f, 25f), 30f);
            PlacePrefab("Rock_02.prefab", group, "Roca_2", new Vector2(-37f, 5f), 100f);
            PlacePrefab("Rock_03.prefab", group, "Roca_3", new Vector2(-21f, 12f), 200f);
        }

        private static void BuildHollow(Transform group)
        {
            PlacePrefab("Rock_02.prefab", group, "Roca_Hondonada", new Vector2(14f, 33f), 60f);
        }

        private static void BuildPool(Transform group)
        {
            // Dock into the pool: deck at -0.8 on posts that reach the basin floor.
            var wood = Mat("Tablas", WoodHex);
            var old = Mat("TablasViejas", WoodOldHex);
            Box(group, "Muelle", new Vector3(34.5f, DockTopY - DockThickness * 0.5f, 28f), new Vector3(8f, DockThickness, 2f), wood, _layerEnv);
            foreach (var x in new[] { 31f, 33f, 34.5f, 36f, 38f })
                Post(group, "PosteMuelle", new Vector2(x, 28f), DockTopY - DockThickness, old);
            PlacePrefab("Rock_03.prefab", group, "Roca_Poza", new Vector2(20f, 18f), 300f);
        }

        // ================================================================== Gameplay objects

        private static void PlaceGameplay(Transform items)
        {
            // The fire, the bucket, the water, the cage: existing objects of the scene, moved onto the new ground.
            MoveExisting("Fireplace", FireplaceSpot, 0f);
            MoveExisting("Bucket", BucketSpot, 0f);
            MoveExisting("GnomeCage", CageSpot, 90f);
            var water = MoveExisting("WaterSource", WaterSourceSpot, 0f);
            if (water != null)
                ConfigureWaterSource(water);
            var river = FindInScene("RiverAudio");
            if (river != null)
                river.transform.position = GroundPoint(WaterSourceSpot);

            // Logs: 3 south of the trees, 3 north, 1 on the shoulder.
            PlaceItem(items, "Items/Wood.prefab", "Lena_Campamento", CampWoodSpot);
            PlaceItem(items, "Items/Wood.prefab", "Lena_Huerto", OrchardWoodSpot);
            PlaceItem(items, "Items/Wood.prefab", "Lena_Serreria", SawmillWoodSpot);
            PlaceItem(items, "Items/Wood.prefab", "Lena_Risco", CragWoodSpot);
            PlaceItem(items, "Items/Wood.prefab", "Lena_Hondonada", HollowWoodSpot);
            PlaceItem(items, "Items/Wood.prefab", "Lena_Poza", PoolWoodSpot);
            PlaceItem(items, "Items/Wood.prefab", "Lena_Hombro", ShoulderWoodSpot);
            PlaceItem(items, "Items/Matches.prefab", "Fosforos_Hombro", ShoulderMatchesSpot);
            PlaceItem(items, "Items/Sausage.prefab", "Salchicha_Copa", new Vector2(_canopyCenter.x, _canopyCenter.z));
            PlaceItem(items, "Items/Sausage.prefab", "Salchicha_Risco", CragSausageSpot);
            PlaceItem(items, "Items/Sausage.prefab", "Salchicha_Muelle", DockSausageSpot);

            // Spawns (children of PlayerSpawner, referenced by it).
            PlaceSpawn("CamperSpawn", CamperSpawnSpot, Vector3.forward);
            for (var i = 0; i < GnomeSpawnSpots.Length; i++)
                PlaceSpawn($"GnomeSpawn{i + 1}", GnomeSpawnSpots[i], new Vector3(-GnomeSpawnSpots[i].x, 0f, -GnomeSpawnSpots[i].y));
            for (var i = 0; i < LobbySpawnSpots.Length; i++)
                PlaceSpawn($"SpawnPoint{i + 1}", LobbySpawnSpots[i], Vector3.forward);
        }

        private static void PlaceItem(Transform parent, string path, string name, Vector2 spot)
        {
            PlacePrefab(path, parent, name, spot, 0f);
        }

        private static void PlaceSpawn(string name, Vector2 spot, Vector3 facing)
        {
            var go = FindInScene(name);
            if (go == null)
                return;

            var ground = GroundPoint(spot);
            go.transform.SetPositionAndRotation(ground + Vector3.up * SpawnLift, Quaternion.LookRotation(facing.sqrMagnitude < 0.01f ? Vector3.forward : new Vector3(facing.x, 0f, facing.z)));
            KeyPoints.Add(spot);
        }

        // WaterSource keeps its shore line in world space: point it at the pool and drop the old river volumes.
        private static void ConfigureWaterSource(GameObject water)
        {
            var source = water.GetComponent<Fireplace.WaterSource>();
            if (source == null)
                return;

            var serialized = new SerializedObject(source);
            var path = serialized.FindProperty("_path");
            if (path != null)
            {
                var offsets = new[] { new Vector2(-1.5f, 0f), new Vector2(0f, 1.5f), new Vector2(1.5f, 0f), new Vector2(0f, -1.5f) };
                path.arraySize = offsets.Length;
                for (var i = 0; i < offsets.Length; i++)
                    path.GetArrayElementAtIndex(i).vector3Value = GroundPoint(WaterSourceSpot + offsets[i]);
            }

            var volumes = serialized.FindProperty("_volumes");
            if (volumes != null)
                volumes.arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureLevelBounds()
        {
            var bounds = Object.FindFirstObjectByType<LevelBounds>(FindObjectsInactive.Include);
            if (bounds == null)
            {
                Debug.LogWarning($"{LogPrefix} No LevelBounds in the scene.");
                return;
            }

            var serialized = new SerializedObject(bounds);
            var property = serialized.FindProperty("_bounds");
            if (property != null)
                property.boundsValue = new Bounds(BoundsCenter, BoundsSize);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ================================================================== Cover, hiding spots and decoys

        private static bool CoverExcluded(float x, float z)
        {
            if (Mathf.Sqrt(x * x + z * z) < CoverTrunkExclusion)
                return true;

            var slitFrom = SlitStart - Dir2 * CoverSlitExclusion;
            var slitTo = SlitEnd + Dir2 * CoverSlitExclusion;
            if (DistanceToSegment(new Vector2(x, z), slitFrom, slitTo) < CoverSlitExclusion)
                return true;

            if (Mathf.Abs(x) < FireU_OuterX + 5f && z > FireU_FrontZ - 3f && z < FireU_BackOuterZ + 3f)
                return true;

            if (Mathf.Abs(z) < RootHalfThickness + 2.5f && Mathf.Abs(x) > RootWestStartX - 1f && Mathf.Abs(x) < RootTipX + 3f)
                return true;

            if (Mathf.Abs(x - RampEntryX) < 3f && z > RampEntryStartZ - 3f && z < 2f)
                return true;

            if (Vector2.Distance(new Vector2(x, z), PozaCenter) < WaterRadius + 1.2f)
                return true;

            return z > StreamEnd.y - 3.5f && z < StreamEnd.y + 3.5f && x > StreamEnd.x - 2f && x < StreamStart.x + 2f;
        }

        private static bool NearKeyPoint(float x, float z, float distance)
        {
            var p = new Vector2(x, z);
            foreach (var key in KeyPoints)
            {
                if ((key - p).sqrMagnitude < distance * distance)
                    return true;
            }

            return false;
        }

        private static void ScatterCover(Transform parent)
        {
            var min = new Vector2(-CoverLimitX, -CoverLimitZ);
            var max = new Vector2(CoverLimitX, CoverLimitZ);
            Lattice(parent, min, max, CoverSpacing, CoverJitter, 0);
            // Dense cover in the hollow.
            Lattice(parent, new Vector2(-14f, 12f), new Vector2(14f, CoverLimitZ), HollowCoverSpacing, CoverJitter * 0.5f, 11);
        }

        private static void Lattice(Transform parent, Vector2 min, Vector2 max, float spacing, float jitter, int seed)
        {
            for (var x = min.x; x <= max.x; x += spacing)
            {
                for (var z = min.y; z <= max.y; z += spacing)
                {
                    var px = x + (Hash01(x + seed, z) - 0.5f) * 2f * jitter;
                    var pz = z + (Hash01(z, x + seed) - 0.5f) * 2f * jitter;
                    TryBush(parent, px, pz);
                }
            }
        }

        private static void TryBush(Transform parent, float x, float z)
        {
            if (Mathf.Abs(x) > CoverLimitX || Mathf.Abs(z) > CoverLimitZ || CoverExcluded(x, z) || NearKeyPoint(x, z, CoverKeepAway))
                return;

            // Only bare terrain: not on top of a structure, not in the pool, not on a steep slope.
            if (!TryGround(x, z, null, out var hit) || !(hit.collider is TerrainCollider) || hit.normal.y < CoverMinNormalY || hit.point.y < CoverMinGroundY)
                return;

            var scale = 1.8f + Hash01(x + 9f, z + 4f) * 0.8f;
            var height = 2f + Hash01(x + 2f, z + 8f) * 0.6f;
            var center = new Vector3(x, hit.point.y + height * 0.5f - 0.2f, z);
            if (Physics.CheckSphere(center, CoverClearRadius, _groundMask | (1 << _layerOccluder), QueryTriggerInteraction.Ignore))
                return;

            var bush = Sphere(parent, $"Arbusto_{_bushCount:000}", center, new Vector3(scale, height, scale), Mat("Arbusto", BushHex), _layerOccluder, true);
            bush.transform.rotation = Quaternion.Euler(0f, Hash01(x, z) * 360f, 0f);
            Physics.SyncTransforms();
            _bushCount++;
        }

        // Eight bushes that become hiding spots (child with NetworkObject + HidingSpot, built by HidingSpotTools).
        private static void BuildHidingSpots(Transform parent)
        {
            var bushes = new List<Object>();
            for (var i = 0; i < HidingSpots.Length; i++)
            {
                var spot = HidingSpots[i];
                var ground = GroundPoint(spot);
                var bush = Sphere(parent, $"ArbustoEscondite_{i + 1}", new Vector3(spot.x, ground.y + HidingBushScale.y * 0.5f - 0.2f, spot.y), HidingBushScale,
                    Mat("ArbustoEscondite", "#2F5A2B"), _layerOccluder, false);
                bushes.Add(bush);
                KeyPoints.Add(spot);
            }

            Physics.SyncTransforms();
            Selection.objects = bushes.ToArray();
            HidingSpotTools.CreateOnSelection();
            Selection.objects = new Object[0];
        }

        private static void BuildDecoys(Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "GardenGnomeDecoy.prefab");
            if (prefab == null)
            {
                Debug.LogWarning($"{LogPrefix} Missing GardenGnomeDecoy prefab.");
                return;
            }

            PlaceDecoys(parent, prefab, OrchardDecoyAnchors, OrchardDecoys);
            PlaceDecoys(parent, prefab, HollowDecoyAnchors, HollowDecoys);
            PlaceDecoys(parent, prefab, ShoulderDecoyAnchors, ShoulderDecoys);
        }

        private static void PlaceDecoys(Transform parent, GameObject prefab, Vector2[] anchors, int count)
        {
            var blockers = LayerMask.GetMask("Environment", "Player", "Gnome", "Item", "Occluder");
            var placed = 0;
            foreach (var anchor in anchors)
            {
                if (placed >= count)
                    break;

                if (!TryDecoySpot(anchor, blockers, out var spot))
                {
                    Debug.LogWarning($"{LogPrefix} No free decoy spot near ({anchor.x}, {anchor.y}).");
                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                instance.name = $"Senuelo_{DecoyPoints.Count + 1:00}";
                var yaw = Mathf.Repeat(spot.x * 53f + spot.z * 97f, 360f);
                instance.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));
                DecoyPoints.Add(new Vector2(spot.x, spot.z));
                placed++;
            }
        }

        // First flat bare spot around the anchor where a gnome-sized capsule touches nothing and no other decoy or key point is near.
        private static bool TryDecoySpot(Vector2 anchor, int blockers, out Vector3 spot)
        {
            for (var ring = 0; ring <= DecoyRings; ring++)
            {
                var steps = ring == 0 ? 1 : DecoySteps;
                for (var i = 0; i < steps; i++)
                {
                    var angle = i * Mathf.PI * 2f / steps;
                    var x = anchor.x + Mathf.Cos(angle) * ring * DecoyRingStep;
                    var z = anchor.y + Mathf.Sin(angle) * ring * DecoyRingStep;
                    if (!TryGround(x, z, null, out var hit) || hit.normal.y < DecoyMinNormalY || NearKeyPoint(x, z, DecoyKeepAway))
                        continue;

                    var feet = hit.point;
                    var bottom = feet + Vector3.up * (DecoyRadius + 0.03f);
                    var top = feet + Vector3.up * Mathf.Max(DecoyRadius + 0.03f, DecoyHeight - DecoyRadius);
                    if (Physics.CheckCapsule(bottom, top, DecoyRadius, blockers, QueryTriggerInteraction.Ignore) || DecoyTooClose(feet))
                        continue;

                    spot = feet;
                    return true;
                }
            }

            spot = default;
            return false;
        }

        private static bool DecoyTooClose(Vector3 feet)
        {
            var p = new Vector2(feet.x, feet.z);
            foreach (var other in DecoyPoints)
            {
                if ((other - p).sqrMagnitude < DecoySpacing * DecoySpacing)
                    return true;
            }

            return false;
        }

        // ================================================================== Validation

        // The slit and the tunnels must admit the gnome and refuse the camper (crouched, 1 m tall).
        private static void Validate()
        {
            Physics.SyncTransforms();
            var mask = _groundMask | (1 << _layerOccluder);
            var failures = 0;

            var length = Vector2.Distance(SlitStart, SlitEnd);
            for (var i = 1; i <= 9; i++)
            {
                var p = SlitStart + Dir2 * (length * i / 10f);
                if (GnomeBlocked(new Vector3(p.x, 0f, p.y), mask, out var where))
                {
                    failures++;
                    Debug.LogWarning($"{LogPrefix} The gnome does not fit in the slit at {where}.");
                }
            }

            var slitMiddle = (SlitStart + SlitEnd) * 0.5f;
            failures += RequireCamperBlocked("slit", new Vector3(slitMiddle.x, 0f, slitMiddle.y), mask);
            foreach (var sign in new[] { -1f, 1f })
            {
                var tunnel = new Vector3(sign * TunnelX, 0f, 0f);
                if (GnomeBlocked(tunnel, mask, out var where))
                {
                    failures++;
                    Debug.LogWarning($"{LogPrefix} The gnome does not fit in the tunnel at {where}.");
                }

                failures += RequireCamperBlocked($"tunnel x={tunnel.x}", tunnel, mask);
            }

            // Peephole: a ray from the slit's south exit towards the fire must cross the U's east wall.
            var exitPoint = SlitEnd + Dir2 * 0.4f;
            var exit = new Vector3(exitPoint.x, GnomeHeight * 0.5f, exitPoint.y);
            var target = new Vector3(0f, GnomeHeight * 0.5f, FireplaceSpot.y);
            var direction = target - exit;
            var distance = exit.x - (FireU_InnerX - 0.5f);
            if (Physics.Raycast(exit, direction.normalized, direction.magnitude * (distance / Mathf.Abs(direction.x)), mask, QueryTriggerInteraction.Ignore))
            {
                failures++;
                Debug.LogWarning($"{LogPrefix} The peephole does not give a view of the fire from the slit exit.");
            }

            Debug.Log($"{LogPrefix} Passage check: {(failures == 0 ? "slit and tunnels admit the gnome and refuse the camper" : failures + " failure(s)")}.");
        }

        private static bool GnomeBlocked(Vector3 ground, int mask, out Vector3 where)
        {
            where = ground;
            var bottom = ground + Vector3.up * (GnomeRadius + GnomeProbeLift);
            var top = ground + Vector3.up * (GnomeHeight - GnomeRadius);
            return Physics.CheckCapsule(bottom, top, GnomeRadius, mask, QueryTriggerInteraction.Ignore);
        }

        private static int RequireCamperBlocked(string label, Vector3 ground, int mask)
        {
            var bottom = ground + Vector3.up * CamperRadius;
            var top = ground + Vector3.up * (CamperCrouchHeight - CamperRadius);
            if (Physics.CheckCapsule(bottom, top, CamperRadius, mask, QueryTriggerInteraction.Ignore))
                return 0;

            Debug.LogWarning($"{LogPrefix} The crouched camper fits in the {label}.");
            return 1;
        }
    }
}
