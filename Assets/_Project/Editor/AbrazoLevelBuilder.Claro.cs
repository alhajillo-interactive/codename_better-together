// Grey-box of the small camp, «Claro de la Caravana», to test the list of pranks. Plain blocks at their real heights, no art. Builds
// its own scene from a copy of CampAbrazo, which keeps the match systems and the HUD. Heights come from the jump of a gnome (0.5 m).
using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Fireplace;
using BetterTogether.Match;
using BetterTogether.UI;
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
        private const float BombFuseSeconds = 45f;
        private const string BoomCue = "Assets/_Project/Audio/SFX/SFX_BalloonPop.asset";
        private const float ClaroHalfX = 20f;
        private const float ClaroHalfZ = 18f;
        private const float ClaroWallHeight = 6f;
        private const float ClaroMatchSeconds = 360f;
        private const float ClaroPondRadius = 7f;
        private const float MarkerSize = 0.35f;
        private const float MarkerColliderRadius = 0.5f;

        private static readonly Vector2 ClaroFire = new(0f, -4f);
        private static readonly Vector2 ClaroPond = new(12f, 11f);
        private static readonly Vector2 ClaroCage = new(13f, -13f);
        private static readonly Vector2 ClaroCamper = new(3f, -2f);

        private static readonly Vector2[] ClaroWood = { new(11.5f, -7f), new(13f, -7f), new(14.5f, -7f), new(12f, -8.5f), new(14f, -8.5f), new(16f, -7.5f), new(10f, -8f) };
        private static readonly Vector2[] ClaroDecoys =
        {
            new(-15f, -8f), new(-12f, -9f), new(-9f, -8f), new(-16f, -11f), new(-13f, -12f), new(-10f, -11f), new(-14f, -14f), new(-9f, -14f),
        };

        private static readonly Vector2[] ClaroGnomeSpawns = { new(-12f, -6f), new(-14f, -6f), new(-10f, -6f), new(-16f, -6f), new(-8f, -6f), new(-12f, -5f) };

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

            RemovePreviousLevel();
            var root = new GameObject(ClaroRoot);
            _root = root.transform;
            SceneManager.MoveGameObjectToScene(root, _scene);
            Physics.SyncTransforms();

            BuildClaroGround(Group("Suelo"));
            var props = BuildClaroCamp(Group("Campamento"));
            Physics.SyncTransforms();
            PlaceClaroGameplay(Group("Items"));
            BuildClaroGarden(Group("Jardin"));
            ConfigureClaroMatch();
            BuildTravesuras(Group("Travesuras"), props);

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

        private static void BuildClaroGround(Transform parent)
        {
            var grass = Mat("ClaroCesped", "#8FAE62");
            var edge = Mat("ClaroBorde", "#2F4A35");
            Box(parent, "Suelo", new Vector3(0f, -0.5f, 0f), new Vector3(ClaroHalfX * 2f + 8f, 1f, ClaroHalfZ * 2f + 8f), grass, _layerEnv);
            Box(parent, "BordeNorte", new Vector3(0f, ClaroWallHeight * 0.5f, ClaroHalfZ + 0.5f), new Vector3(ClaroHalfX * 2f + 2f, ClaroWallHeight, 1f), edge, _layerEnv);
            Box(parent, "BordeSur", new Vector3(0f, ClaroWallHeight * 0.5f, -ClaroHalfZ - 0.5f), new Vector3(ClaroHalfX * 2f + 2f, ClaroWallHeight, 1f), edge, _layerEnv);
            Box(parent, "BordeEste", new Vector3(ClaroHalfX + 0.5f, ClaroWallHeight * 0.5f, 0f), new Vector3(1f, ClaroWallHeight, ClaroHalfZ * 2f + 2f), edge, _layerEnv);
            Box(parent, "BordeOeste", new Vector3(-ClaroHalfX - 0.5f, ClaroWallHeight * 0.5f, 0f), new Vector3(1f, ClaroWallHeight, ClaroHalfZ * 2f + 2f), edge, _layerEnv);

            var pond = Cylinder(parent, "Agua", new Vector3(ClaroPond.x, 0.02f, ClaroPond.y), ClaroPondRadius * 2f, 0.04f, Quaternion.identity, Mat("Agua", WaterHex), _layerWater, false);
            MakeWaterTransparent(pond);
        }

        private readonly struct ClaroProps
        {
            public readonly GameObject Underpants;
            public readonly Transform AntennaTop;

            public ClaroProps(GameObject underpants, Transform antennaTop)
            {
                Underpants = underpants;
                AntennaTop = antennaTop;
            }
        }

        private static ClaroProps BuildClaroCamp(Transform parent)
        {
            var wood = Mat("ClaroMadera", "#A56B3C");
            var white = Mat("ClaroCaravana", "#F4F0E2");
            var red = Mat("ClaroToldo", "#D65A3D");
            var grey = Mat("ClaroMetal", "#8E8E8E");
            var blue = Mat("ClaroHielera", "#3B7EA1");
            var cloth = Mat("ClaroTienda", "#D98D32");

            // Table: top at 0.75, legs a gnome walks between.
            Box(parent, "Mesa", new Vector3(-6f, 0.72f, -2f), new Vector3(2.2f, 0.06f, 1.1f), wood, _layerEnv);
            foreach (var (x, z) in new[] { (-7f, -2.45f), (-5f, -2.45f), (-7f, -1.55f), (-5f, -1.55f) })
                Box(parent, "PataMesa", new Vector3(x, 0.345f, z), new Vector3(0.1f, 0.69f, 0.1f), wood, _layerEnv);

            foreach (var (x, z) in new[] { (-6.6f, -3.1f), (-5.4f, -3.1f), (-6.6f, -0.9f), (-5.4f, -0.9f) })
                Box(parent, "Silla", new Vector3(x, 0.225f, z), new Vector3(0.5f, 0.45f, 0.5f), wood, _layerEnv);

            Box(parent, "Hielera", new Vector3(-4f, 0.225f, -2.6f), new Vector3(0.8f, 0.45f, 0.5f), blue, _layerEnv);
            Box(parent, "Caja", new Vector3(1.8f, 0.225f, 1.2f), new Vector3(0.7f, 0.45f, 0.7f), wood, _layerEnv);
            Box(parent, "CajaAlta", new Vector3(2.45f, 0.45f, 1.2f), new Vector3(0.6f, 0.9f, 0.7f), wood, _layerEnv);

            // Caravan: body 2.4 m, awning 1.9 m on the south side, ladder at the back for the camper, low bonnet at the front.
            Box(parent, "Caravana", new Vector3(6f, 1.2f, 2.5f), new Vector3(5.6f, 2.4f, 2.4f), white, _layerEnv);
            Box(parent, "Toldo", new Vector3(6f, 1.88f, 0.55f), new Vector3(5f, 0.05f, 1.5f), red, _layerEnv);
            Box(parent, "PosteToldo", new Vector3(8.4f, 0.93f, -0.1f), new Vector3(0.08f, 1.86f, 0.08f), grey, _layerEnv);
            Box(parent, "PosteToldo", new Vector3(3.6f, 0.93f, -0.1f), new Vector3(0.08f, 1.86f, 0.08f), grey, _layerEnv);
            Box(parent, "Capo", new Vector3(9.5f, 0.5f, 2.5f), new Vector3(1.2f, 1f, 2.2f), grey, _layerEnv);
            for (var i = 0; i < 6; i++)
                Box(parent, "EscaleraTrasera", new Vector3(2.8f - i * 0.0f - 0.2f - (5 - i) * 0.25f, 0.2f + i * 0.4f, 3f), new Vector3(0.45f, 0.1f, 0.8f), grey, _layerEnv);

            var antenna = Box(parent, "Antena", new Vector3(7.5f, 2.85f, 2.5f), new Vector3(0.08f, 0.9f, 0.08f), grey, _layerEnv);
            var underpants = Box(parent, "Calzoncillos", new Vector3(7.5f, 3.2f, 2.5f), new Vector3(0.5f, 0.3f, 0.05f), red, _layerEnv, false);

            Box(parent, "Tienda", new Vector3(-12f, 0.75f, 6f), new Vector3(3f, 1.5f, 3f), cloth, _layerEnv);
            Box(parent, "PosteTendedero", new Vector3(-14f, 0.9f, 2f), new Vector3(0.1f, 1.8f, 0.1f), wood, _layerEnv);
            Box(parent, "PosteTendedero", new Vector3(-8f, 0.9f, 2f), new Vector3(0.1f, 1.8f, 0.1f), wood, _layerEnv);
            Box(parent, "Lena", new Vector3(13f, 0.45f, -5f), new Vector3(3f, 0.9f, 1f), wood, _layerEnv);
            Box(parent, "Muelle", new Vector3(6f, 0.15f, 8f), new Vector3(4f, 0.3f, 1.2f), wood, _layerEnv);
            return new ClaroProps(underpants, antenna.transform);
        }

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

            var matches = PlacePrefab("Items/Matches.prefab", items, "Fosforos", new Vector2(-6.5f, -2f), 0f);
            if (matches != null)
                matches.tag = MatchesTag;

            PlaceItem(items, "Items/Sausage.prefab", "Salchicha", new Vector2(-5.5f, -2f));

            PlaceSpawn("CamperSpawn", ClaroCamper, Vector3.forward);
            for (var i = 0; i < ClaroGnomeSpawns.Length; i++)
                PlaceSpawn($"GnomeSpawn{i + 1}", ClaroGnomeSpawns[i], Vector3.forward);
            for (var i = 0; i < LobbySpawnSpots.Length; i++)
                PlaceSpawn($"SpawnPoint{i + 1}", new Vector2(-14f + i * 2f, -3f), Vector3.forward);
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
                    var shore = ClaroPond + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ClaroPondRadius;
                    path.GetArrayElementAtIndex(i).vector3Value = new Vector3(shore.x, 0f, shore.y);
                }
            }

            var volumes = serialized.FindProperty("_volumes");
            if (volumes != null)
                volumes.arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildClaroGarden(Transform parent)
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
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void BuildTravesuras(Transform parent, ClaroProps props)
        {
            var layoutObject = new GameObject("Lista");
            layoutObject.transform.SetParent(parent, false);
            var layout = layoutObject.AddComponent<TravesuraLayout>();
            var marker = Mat("ClaroMarca", "#FFD25E");

            // Index, text for the gnomes, hint for the camper, kind, points, seconds between points, result.
            var entries = new (string label, string hint, TravesuraLayout.Kind kind, int points, float window, GameObject result)[]
            {
                ("Tirad los fósforos al muelle y empujadlos entre dos", "en el muelle", TravesuraLayout.Kind.Points, 2, 0f, null),
                ("Coged los calzoncillos del tendedero y colgadlos en la antena", "en el tendedero", TravesuraLayout.Kind.Points, 2, 0f, props.Underpants),
                ("¡Bocinazo! Uno en la cabina y otro en el capó, a la vez", "en la caravana", TravesuraLayout.Kind.Points, 2, 5f, null),
                ("Apagad la fogata con dos bombas de agua", "en la fogata", TravesuraLayout.Kind.FireOut, 0, 0f, null),
                ("Armad la estatua explosiva junto a la mesa", "en el comedor", TravesuraLayout.Kind.Bomb, 1, BombFuseSeconds, null),
            };

            var serialized = new SerializedObject(layout);
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
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            var parentOfPoints = layoutObject.transform;
            AddPoint(parentOfPoints, 0, 0, new Vector3(5f, 0.9f, 8f), "Mantén para dejar los fósforos", "Fosforo", marker);
            AddPoint(parentOfPoints, 0, 1, new Vector3(7f, 0.9f, 8f), "Mantén para empujar los fósforos", null, marker);
            AddPoint(parentOfPoints, 1, 0, new Vector3(-11f, 1.1f, 2f), "Mantén para coger los calzoncillos", null, marker);
            AddPoint(parentOfPoints, 1, 1, props.AntennaTop.position + Vector3.down * 0.3f + Vector3.right * 0.4f, "Mantén para colgar los calzoncillos", null, marker);
            AddPoint(parentOfPoints, 2, 0, new Vector3(8.9f, 1.1f, 1.1f), "Mantén para tocar la bocina", null, marker);
            AddPoint(parentOfPoints, 2, 1, new Vector3(9.5f, 1.5f, 2.5f), "Mantén para pisar el capó", null, marker);
            AddPoint(parentOfPoints, 4, 0, new Vector3(-3f, 0.9f, -6f), "Mantén para armar la estatua", null, marker);

            var bomb = PlacePrefab("GardenGnomeDecoy.prefab", parent, "EstatuaExplosiva", new Vector2(-3f, -6f), 0f);
            if (bomb != null)
            {
                var component = bomb.AddComponent<TravesuraBomb>();
                var so = new SerializedObject(component);
                so.FindProperty("_boomSfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SoundCue>(BoomCue);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void AddPoint(Transform parent, int travesura, int index, Vector3 position, string prompt, string itemTag, Material material)
        {
            var go = new GameObject($"Punto_{travesura}_{index}") { layer = LayerMask.NameToLayer("NoCollision") };
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var collider = go.AddComponent<SphereCollider>();
            collider.radius = MarkerColliderRadius;

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
