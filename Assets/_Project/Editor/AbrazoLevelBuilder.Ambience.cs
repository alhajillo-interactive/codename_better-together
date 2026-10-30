// Ambience of El Abrazo (C9.3), second half of AbrazoLevelBuilder: forest ring, sector dividers with marked gates, groves,
// cover, small vegetation, props of every sector, tree details, ramp lanterns, signs, fog and the sight-line check. Everything
// is built from the project's models (NewAssets and KenneySurvivalKit). The size of each model is MEASURED from its renderer
// bounds at placement time (the FBX may come in any unit) and every piece is seated on the terrain (or on a structure) by
// its lowest point, so FloatingAudit has nothing to report. All of it hangs from the root "Abrazo" and is rebuilt on every run.
using System.Collections.Generic;
using BetterTogether.Helpers;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static partial class AbrazoLevelBuilder
    {
        // ---- Models ----
        private const string NewModelDir = "Assets/_Project/Models/NewAssets/";
        private const string KenneyModelDir = "Assets/_Project/Models/KenneySurvivalKit/";
        private const string TitleFontPath = "Assets/_Project/Fonts/LilitaOne/LilitaOne-Regular SDF.asset";
        private const string ColdRockHex = "#7A8696";

        private enum Body { None, Box, Capsule, Sphere, Mesh }

        private enum Support { Terrain, Structure, Fixed }

        private struct TreeKind
        {
            public string Model;
            public float MinHeight;
            public float MaxHeight;
            public float TrunkRadius;
        }

        private struct GateInfo
        {
            public string Name;
            public Vector2 Center;
            public Vector2 WallDirection;
        }

        private static readonly TreeKind[] TreeKinds =
        {
            new TreeKind { Model = "SM_Pine_01", MinHeight = 11f, MaxHeight = 15f, TrunkRadius = 0.5f },
            new TreeKind { Model = "SM_Pine_02", MinHeight = 11f, MaxHeight = 15f, TrunkRadius = 0.5f },
            new TreeKind { Model = "SM_Tree_Round_01", MinHeight = 9f, MaxHeight = 12f, TrunkRadius = 0.55f },
            new TreeKind { Model = "SM_Tree_Birch", MinHeight = 9f, MaxHeight = 11.5f, TrunkRadius = 0.4f },
            new TreeKind { Model = "SM_Tree_Round_Autumn", MinHeight = 9f, MaxHeight = 11f, TrunkRadius = 0.55f },
        };

        // Weighted pick of TreeKinds (indices).
        private static readonly int[] TreePick = { 0, 1, 0, 1, 2, 3, 2, 4 };
        private static readonly int[] PinePick = { 0, 1 };
        private static readonly string[] BushModels = { "SM_Bush_01", "SM_Bush_02", "SM_Bush_Berry" };

        // ---- Forest ring (outside the invisible walls) ----
        private const int ForestRows = 3;
        private const float ForestBaseX = 42f;
        private const float ForestBaseZ = 39f;
        private const float ForestRowGap = 4.6f;
        private const float ForestSpacingNear = 4.2f;
        private const float ForestSpacingFar = 5f;
        private const float ForestJitter = 1.2f;
        private const float ForestRowExtraHeight = 1.4f;
        private const float ForestBushHeight = 1.5f;

        // ---- Dividers, gates, groves ----
        private const float GatePostHeight = 2.2f;
        private const float GateOpeningMin = 3f;
        private const float GateHalfOpening = 2f;
        private const float FenceHeight = 1.5f;
        private const float DividerKeyAway = 1.6f;
        private const float PoolRingRadius = 12.5f;
        private const float PoolRingStep = 2.8f;
        private const float PoolGapHalfAngle = 0.3f;
        private const float GroveLimitX = 36f;
        private const float GroveLimitZ = 34f;
        private const float GroveNearPath = 2.8f;
        private const float GroveKeyAway = 2.5f;
        private const float GroveTrunkExclusion = 14.5f;

        private static readonly Vector2[] GroveAnchors =
        {
            new(-18f, -6f), new(-30f, -5f), new(-35f, -9f), new(-23f, -2f), new(-14f, -33f), new(-20f, -33.5f), new(20f, -33f),
            new(27f, -28f), new(35f, -27f), new(17f, 5f), new(23f, 9f), new(36f, 3f), new(36f, 10f), new(24f, 2f), new(16f, 14f),
            new(-16f, 12f), new(-15f, 17f), new(-30f, -33f), new(-17f, 30f), new(-17f, 24f), new(34f, -3f), new(21f, -3f),
        };

        // ---- Paths (dirt, painted into the terrain; also the routes the cover follows) ----
        private const float PathHalfWidth = 1.7f;
        private const float PatchInner = 0.3f;

        private static readonly Vector2[][] PathLines =
        {
            new Vector2[] { new(6f, -27f), new(13f, -27.5f), new(18f, -22f), new(19.5f, -17f), new(24f, -16.5f) },
            new Vector2[] { new(-4f, -25f), new(-12f, -23f), new(-18f, -21f), new(-22f, -21f) },
            new Vector2[] { new(-28f, -12f), new(-27.5f, -5f), new(-25.5f, 2f), new(-24.5f, 6.5f) },
            new Vector2[] { new(0f, -29f), new(0f, -12.5f) },
            new Vector2[] { new(-21f, 20f), new(-17f, 23f), new(-11f, 25f), new(-5f, 26f) },
            new Vector2[] { new(5f, 24f), new(11f, 22f), new(15.5f, 24f), new(16f, 26f) },
            new Vector2[] { new(-4.6f, 8.5f), new(-2f, 14f), new(0f, 19f), new(0f, 24f) },
            new Vector2[] { new(-9f, 19f), new(-8f, 14f), new(-4.6f, 8.5f) },
            new Vector2[] { new(9f, 19f), new(8f, 14f), new(4f, 9.5f) },
            new Vector2[] { new(28f, 13.5f), new(30f, 6f), new(30.5f, 0f), new(30.5f, -7.5f) },
        };

        // Crossings and gates: a patch of path stones lies on each.
        private static readonly Vector2[] Junctions =
        {
            new(13f, -27.5f), new(19.5f, -17f), new(-18f, -21f), new(-28f, -12f), new(-24.5f, 6.5f), new(-4.6f, 8.5f),
            new(0f, 19f), new(-9f, 19f), new(9f, 19f), new(30f, 6f), new(16f, 26f),
        };

        // ---- Fire mound ----
        private const float FireMoundHeight = 0.4f;
        private const float FireMoundFlat = 1.3f;
        private const float FireMoundFalloff = 2f;

        // ---- Cover ----
        private const int CoverMaxBushes = 90;
        private const float CoverRouteStep = 5f;
        private const float CoverRouteOffset = 2.6f;
        private const float CoverNearPath = 2f;
        private const float CoverBushMinHeight = 2f;
        private const float CoverBushExtraHeight = 0.7f;

        // ---- Small vegetation ----
        private const int SmallVegetationCap = 250;

        // ---- Sight lines ----
        private const float SightEye = 1.6f;
        private const float SightMax = 30f;
        private const float SightLimit = 15f;
        private const float SightGrid = 6f;
        private const int SightDirections = 12;
        private const int SightPasses = 3;
        private const int SightMaxClumps = 22;

        // ---- Ramp lanterns and lights ----
        private const float LanternHeight = 0.6f;
        private const float LanternLightRange = 7f;
        private const float LanternLightIntensity = 1.4f;
        private const float RampLanternOffset = 0.8f;
        private const float RampLanternStepDeg = 47f;

        // ---- State of the run ----
        private static Terrain _terrain;
        private static int _smallVegetation;
        private static int _groveCount;
        private static readonly Dictionary<string, GameObject> Models = new();
        private static readonly HashSet<string> MissingModels = new();
        private static readonly List<Vector3> ClearZones = new();
        private static readonly List<GateInfo> Gates = new();
        private static readonly List<Vector2> DecoyRejects = new();
        private static readonly Collider[] Overlap = new Collider[64];

        private static void ResetAmbience()
        {
            _terrain = null;
            _smallVegetation = 0;
            _groveCount = 0;
            Models.Clear();
            MissingModels.Clear();
            ClearZones.Clear();
            Gates.Clear();
            DecoyRejects.Clear();
        }

        // ================================================================== Terrain helpers

        private static float FireMound(float x, float z)
        {
            var d = Vector2.Distance(new Vector2(x, z), FireplaceSpot);
            return FireMoundHeight * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FireMoundFlat, FireMoundFlat + FireMoundFalloff, d)));
        }

        private static float PatchStrength(int layer)
        {
            // Dirt is mixed with the grass below it: less saturated, more natural.
            return layer == LayerDirt ? 0.65f : layer == LayerPebbles ? 0.8f : 1f;
        }

        private static float PathWeight(float x, float z)
        {
            var p = new Vector2(x, z);
            var best = float.MaxValue;
            foreach (var line in PathLines)
            {
                for (var i = 0; i + 1 < line.Length; i++)
                    best = Mathf.Min(best, DistanceToSegment(p, line[i], line[i + 1]));
            }

            return (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PathHalfWidth * 0.5f, PathHalfWidth * 1.4f, best))) * 0.7f;
        }

        private static float TerrainY(float x, float z)
        {
            return _terrain != null ? _terrain.SampleHeight(new Vector3(x, 0f, z)) + _terrain.transform.position.y : HeightAt(x, z);
        }

        private static float TerrainNormalY(float x, float z)
        {
            if (_terrain == null)
                return 1f;

            var data = _terrain.terrainData;
            var origin = _terrain.transform.position;
            return data.GetInterpolatedNormal((x - origin.x) / data.size.x, (z - origin.z) / data.size.z).y;
        }

        // ================================================================== Model helpers

        private static GameObject LoadModel(string model)
        {
            if (Models.TryGetValue(model, out var cached) && cached != null)
                return cached;

            var dir = model.StartsWith("SM_") ? NewModelDir : KenneyModelDir;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(dir + model + ".fbx");
            if (asset == null)
            {
                if (MissingModels.Add(model))
                    Debug.LogWarning($"{LogPrefix} Missing model {dir}{model}.fbx.");
                return null;
            }

            Models[model] = asset;
            return asset;
        }

        // Union of the visible renderers' bounds (world space), ignoring particles, trails, lines and 3D text.
        private static bool RenderBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            var has = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r.GetComponent<TMP_Text>() != null)
                    continue;

                if (has)
                    bounds.Encapsulate(r.bounds);
                else
                {
                    bounds = r.bounds;
                    has = true;
                }
            }

            return has;
        }

        // Bounds of the meshes in the local space of root (exact under rotation and scale).
        private static bool LocalBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            var has = false;
            var toRoot = root.worldToLocalMatrix;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || filter.GetComponent<Renderer>() == null)
                    continue;

                var matrix = toRoot * filter.transform.localToWorldMatrix;
                var mb = mesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var p = matrix.MultiplyPoint3x4(corner);
                    if (has)
                        bounds.Encapsulate(p);
                    else
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        has = true;
                    }
                }
            }

            return has;
        }

        private static bool MeasureModel(string model, float size, bool byHeight, out Vector3 scaled)
        {
            scaled = Vector3.zero;
            var prefab = LoadModel(model);
            if (prefab == null)
                return false;

            var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var ok = RenderBounds(temp, out var b);
            Object.DestroyImmediate(temp);
            if (!ok)
                return false;

            var measure = byHeight ? b.size.y : Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (measure < 0.0001f)
                return false;

            scaled = b.size * (size / measure);
            return true;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }

        private static void ShadowsOff(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void AddBody(GameObject go, Body body, float worldRadius)
        {
            if (body == Body.None)
                return;

            if (body == Body.Mesh)
            {
                foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh != null && filter.GetComponent<Collider>() == null)
                        filter.gameObject.AddComponent<MeshCollider>();
                }

                return;
            }

            if (!LocalBounds(go.transform, out var lb))
                return;

            var scale = Mathf.Max(0.0001f, Mathf.Abs(go.transform.lossyScale.x));
            switch (body)
            {
                case Body.Box:
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = lb.center;
                    box.size = lb.size;
                    break;
                }

                case Body.Capsule:
                {
                    var capsule = go.AddComponent<CapsuleCollider>();
                    var radius = worldRadius > 0f ? worldRadius / scale : Mathf.Min(lb.size.x, lb.size.z) * 0.15f;
                    capsule.direction = 1;
                    capsule.radius = Mathf.Min(radius, lb.size.y * 0.5f);
                    capsule.height = lb.size.y;
                    capsule.center = new Vector3(lb.center.x, lb.min.y + lb.size.y * 0.5f, lb.center.z);
                    break;
                }

                case Body.Sphere:
                {
                    var sphere = go.AddComponent<SphereCollider>();
                    sphere.center = lb.center;
                    sphere.radius = Mathf.Max(lb.extents.x, lb.extents.z) * 0.85f;
                    break;
                }
            }
        }

        // Seats go on the ground (terrain by its lowest footprint point, a structure by the highest surface under its centre,
        // or at a fixed height) and sinks into it by 'sink'.
        private static void Put(GameObject go, Vector2 at, Support support, float sink, float fixedY)
        {
            if (!RenderBounds(go, out var b))
                return;

            go.transform.position += new Vector3(at.x - b.center.x, 0f, at.y - b.center.z);
            RenderBounds(go, out b);

            float ground;
            if (support == Support.Fixed)
                ground = fixedY;
            else if (support == Support.Structure && TryGround(b.center.x, b.center.z, go.transform, out var hit))
                ground = hit.point.y;
            else
            {
                var e = b.extents * 0.8f;
                ground = TerrainY(b.center.x, b.center.z);
                ground = Mathf.Min(ground, TerrainY(b.center.x - e.x, b.center.z - e.z));
                ground = Mathf.Min(ground, TerrainY(b.center.x + e.x, b.center.z - e.z));
                ground = Mathf.Min(ground, TerrainY(b.center.x - e.x, b.center.z + e.z));
                ground = Mathf.Min(ground, TerrainY(b.center.x + e.x, b.center.z + e.z));
            }

            go.transform.position += Vector3.up * (ground - sink - b.min.y);
        }

        // True when one of go's own colliders overlaps another Environment/Occluder collider (the terrain does not count).
        private static bool BodyOverlaps(GameObject go)
        {
            var mask = _groundMask | (1 << _layerOccluder);
            foreach (var c in go.GetComponentsInChildren<Collider>())
            {
                int n;
                var t = c.transform;
                var ls = t.lossyScale;
                if (c is CapsuleCollider capsule)
                {
                    var s = Mathf.Abs(ls.x);
                    var half = Mathf.Max(0f, capsule.height * s * 0.5f - capsule.radius * s);
                    var center = t.TransformPoint(capsule.center);
                    n = Physics.OverlapCapsuleNonAlloc(center + Vector3.up * half, center - Vector3.up * half, capsule.radius * s + 0.15f, Overlap, mask, QueryTriggerInteraction.Ignore);
                }
                else if (c is BoxCollider box)
                {
                    var half = Vector3.Scale(box.size, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z))) * 0.45f;
                    n = Physics.OverlapBoxNonAlloc(t.TransformPoint(box.center), half, Overlap, t.rotation, mask, QueryTriggerInteraction.Ignore);
                }
                else if (c is SphereCollider sphere)
                {
                    n = Physics.OverlapSphereNonAlloc(t.TransformPoint(sphere.center), sphere.radius * Mathf.Abs(ls.x) * 0.9f, Overlap, mask, QueryTriggerInteraction.Ignore);
                }
                else
                {
                    var bounds = c.bounds;
                    n = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents * 0.7f, Overlap, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
                }

                for (var i = 0; i < n; i++)
                {
                    var other = Overlap[i];
                    if (other is TerrainCollider || other.transform.IsChildOf(go.transform))
                        continue;

                    return true;
                }
            }

            return false;
        }

        private static bool HitsSolid(Vector3 center, float radius)
        {
            var n = Physics.OverlapSphereNonAlloc(center, radius, Overlap, _groundMask | (1 << _layerOccluder), QueryTriggerInteraction.Ignore);
            for (var i = 0; i < n; i++)
            {
                if (!(Overlap[i] is TerrainCollider))
                    return true;
            }

            return false;
        }

        // The one way a model gets into the scene. 'size' is the target height (byHeight) or the target longest side, in metres.
        // Returns null when the model is missing or, with 'check', when its collider would overlap something that already exists.
        private static GameObject Spawn(string model, Transform parent, string name, Vector2 at, float yaw, float size, Body body, int layer,
            bool byHeight = true, Support support = Support.Terrain, bool lieFlat = false, float sink = 0.05f, float bodyRadius = 0f,
            float fixedY = 0f, bool check = true)
        {
            var prefab = LoadModel(model);
            if (prefab == null)
                return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            if (!RenderBounds(go, out var b))
            {
                Object.DestroyImmediate(go);
                return null;
            }

            if (lieFlat && b.size.y > Mathf.Max(b.size.x, b.size.z))
            {
                go.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                RenderBounds(go, out b);
            }

            var measure = byHeight ? b.size.y : Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (measure < 0.0001f)
            {
                Object.DestroyImmediate(go);
                return null;
            }

            go.transform.localScale = Vector3.one * (size / measure);
            SetLayerRecursive(go, layer);
            AddBody(go, body, bodyRadius);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
            Put(go, at, support, sink, fixedY);
            Physics.SyncTransforms();

            if (check && body != Body.None && BodyOverlaps(go))
            {
                Object.DestroyImmediate(go);
                Physics.SyncTransforms();
                return null;
            }

            return go;
        }

        // ================================================================== Clear zones, paths, blocking tests

        private static void Zone(float x, float z, float radius) => ClearZones.Add(new Vector3(x, z, radius));

        private static bool InClearZone(float x, float z, float extra)
        {
            foreach (var zone in ClearZones)
            {
                var dx = x - zone.x;
                var dz = z - zone.y;
                var r = zone.z + extra;
                if (dx * dx + dz * dz < r * r)
                    return true;
            }

            return false;
        }

        private static bool NearPath(float x, float z, float distance)
        {
            var p = new Vector2(x, z);
            foreach (var line in PathLines)
            {
                for (var i = 0; i + 1 < line.Length; i++)
                {
                    if (DistanceToSegment(p, line[i], line[i + 1]) < distance)
                        return true;
                }
            }

            return false;
        }

        private static bool InSectorInterior(float x, float z)
        {
            if (x > 20f && z > -24f && z < -4.5f)
                return true; // sawmill

            if (x < -17f && z > -31.5f && z < -10.5f)
                return true; // orchard

            if (x < -18.5f && z > 1.5f && z < 33.5f)
                return true; // crag

            if (z > 18f && x > -17f && x < 17f)
                return true; // hollow

            return Vector2.Distance(new Vector2(x, z), PozaCenter) < 15f; // pool
        }

        private static bool InCampView(float x, float z)
        {
            return x > -14f && x < 14f && z > -37f && z < -11f;
        }

        private static bool GroveAllowed(float x, float z)
        {
            if (Mathf.Abs(x) > GroveLimitX || Mathf.Abs(z) > GroveLimitZ || Mathf.Sqrt(x * x + z * z) < GroveTrunkExclusion)
                return false;

            if (CoverExcluded(x, z) || InClearZone(x, z, 0.5f) || InSectorInterior(x, z) || NearPath(x, z, GroveNearPath) || NearKeyPoint(x, z, GroveKeyAway))
                return false;

            return TerrainNormalY(x, z) >= 0.9f && TerrainY(x, z) > CoverMinGroundY;
        }

        // Spots that must never be built on: accesses, fire mouth, spawns, slit mouths, tunnels, stairs.
        private static void RegisterZones()
        {
            Zone(-24.5f, 7f, 5f);   // crag: stairs from the south
            Zone(-21f, 20f, 4.5f);  // crag: east flank
            Zone(0f, -20f, 10f);    // mouth of the fire and the camp: intentional long view
            Zone(0f, -30f, 7f);     // camper spawn
            Zone(6.5f, -10f, 4.5f); // slit south exit
            Zone(-4.6f, 7f, 5f);    // slit north mouth
            Zone(-TunnelX, 0f, 4f);
            Zone(TunnelX, 0f, 4f);
            Zone(-RootTipX, 0f, 4.5f);
            Zone(RootTipX, 0f, 4.5f);
            Zone(26f, -18f, 4f);    // sawmill stairs
            Zone(30.5f, -9f, 3f);   // sawmill gnome ramp
            Zone(32f, 6f, 3f);      // bucket
        }

        // ================================================================== Entry point

        private static void BuildAmbience(Transform parent)
        {
            KeyPoints.AddRange(HidingSpots);
            RegisterZones();
            BuildPaths(Sector(parent, "Senderos"));
            BuildDividers(Sector(parent, "Divisiones"));
            var identity = Sector(parent, "Identidad");
            BuildCampProps(Sector(identity, "Campamento"));
            BuildSawmillProps(Sector(identity, "Serreria"));
            BuildOrchardProps(Sector(identity, "Huerto"));
            BuildHollowProps(Sector(identity, "Hondonada"));
            BuildPoolProps(Sector(identity, "Poza"));
            BuildGroves(Sector(parent, "Bosquetes"));
            BuildVegetation(Sector(parent, "Vegetacion"));
            BuildRampLanterns(Sector(parent, "FarolesRampa"));
            BuildFog(Sector(parent, "Niebla"));
        }

        // ================================================================== Forest ring

        private static GameObject PlaceTree(Transform parent, string name, float x, float z, float extraHeight, bool check, bool pines)
        {
            var pick = pines ? PinePick : TreePick;
            var kind = TreeKinds[pick[Mathf.FloorToInt(Hash01(x + 3f, z + 7f) * pick.Length) % pick.Length]];
            var height = Mathf.Lerp(kind.MinHeight, kind.MaxHeight, Hash01(z + 1f, x + 2f)) + extraHeight;
            return Spawn(kind.Model, parent, name, new Vector2(x, z), Hash01(x, z + 5f) * 360f, height, Body.Capsule, _layerEnv,
                true, Support.Terrain, false, 0.12f, kind.TrunkRadius, 0f, check);
        }

        private static GameObject PlaceBush(Transform parent, string name, float x, float z, float height, int layer, Body body, bool check)
        {
            var model = BushModels[Mathf.FloorToInt(Hash01(x + 1f, z + 9f) * BushModels.Length) % BushModels.Length];
            var bush = Spawn(model, parent, name, new Vector2(x, z), Hash01(x, z) * 360f, height, body, layer,
                true, Support.Terrain, false, 0.08f, 0f, 0f, check);
            if (bush != null && body == Body.None)
                ShadowsOff(bush);
            return bush;
        }

        // Three staggered rows of dense trees that close the horizon, with bushes at the foot of the first row.
        private static void BuildForest(Transform parent)
        {
            var trees = Sector(parent, "Arboles");
            var index = 0;
            for (var row = 0; row < ForestRows; row++)
            {
                var spacing = row == 0 ? ForestSpacingNear : ForestSpacingFar;
                var halfX = ForestBaseX + row * ForestRowGap;
                var halfZ = ForestBaseZ + row * ForestRowGap;
                for (var x = -halfX; x <= halfX; x += spacing)
                {
                    RingTree(trees, x, halfZ, row, 0f, -1f, ref index);
                    RingTree(trees, x + spacing * 0.5f, -halfZ, row, 0f, 1f, ref index);
                }

                for (var z = -halfZ + spacing; z < halfZ; z += spacing)
                {
                    RingTree(trees, halfX, z, row, -1f, 0f, ref index);
                    RingTree(trees, -halfX, z + spacing * 0.5f, row, 1f, 0f, ref index);
                }
            }
        }

        private static void RingTree(Transform parent, float x, float z, int row, float inwardX, float inwardZ, ref int index)
        {
            var px = x + (Hash01(x, z) - 0.5f) * 2f * ForestJitter;
            var pz = z + (Hash01(z, x) - 0.5f) * 2f * ForestJitter;
            PlaceTree(parent, $"Arbol_{index:000}", px, pz, row * ForestRowExtraHeight, false, false);
            if (row == 0 && index % 2 == 0)
                PlaceBush(parent, $"ArbustoBorde_{index:000}", px + inwardX * 1.6f, pz + inwardZ * 1.6f, ForestBushHeight, _layerEnv, Body.None, false);
            index++;
        }

        // ================================================================== Gates and dividers

        private static void Gate(Transform parent, string name, Vector2 center, Vector2 wallDirection, float halfOpening, string sectorKey, string hex)
        {
            var dir = wallDirection.normalized;
            for (var s = -1; s <= 1; s += 2)
            {
                var p = center + dir * (s * (halfOpening + 0.05f));
                var ground = TerrainY(p.x, p.y);
                var bottom = ground - 0.1f;
                var top = ground + GatePostHeight;
                Box(parent, $"{name}_Poste{(s < 0 ? "A" : "B")}", new Vector3(p.x, (bottom + top) * 0.5f, p.y), new Vector3(0.3f, top - bottom, 0.3f), Mat("PosteRotulo", SignPostHex), _layerEnv);
                var cap = Box(parent, $"{name}_Tapa{(s < 0 ? "A" : "B")}", new Vector3(p.x, top + 0.11f, p.y), new Vector3(0.55f, 0.22f, 0.55f), Mat($"Tapa{sectorKey}", hex), _layerEnv, false);
                MarkFloating(cap);
            }

            Gates.Add(new GateInfo { Name = name, Center = center, WallDirection = dir });
            Zone(center.x, center.y, halfOpening + 1.8f);
        }

        // Points along a line, spaced at most 'step' apart, skipping the gaps (the opening is at least 2 * gapHalf wide) and key points.
        private static IEnumerable<Vector2> Along(Vector2 from, Vector2 to, float step, Vector2[] gaps, float gapHalf)
        {
            var length = Vector2.Distance(from, to);
            var dir = (to - from) / length;
            var count = Mathf.Max(1, Mathf.CeilToInt(length / step));
            var real = length / count;
            for (var i = 0; i < count; i++)
            {
                var p = from + dir * (real * (i + 0.5f));
                var blocked = false;
                if (gaps != null)
                {
                    foreach (var gap in gaps)
                    {
                        if (Vector2.Distance(p, gap) < gapHalf + real * 0.5f)
                        {
                            blocked = true;
                            break;
                        }
                    }
                }

                if (blocked || NearKeyPoint(p.x, p.y, DividerKeyAway))
                    continue;

                yield return p;
            }
        }

        private static void BuildDividers(Transform parent)
        {
            BuildOrchardFence(Sector(parent, "ValaHuerto"));
            BuildCampDivider(Sector(parent, "DivisionCampamento"));
            BuildHollowBorder(Sector(parent, "BordeHondonada"));
            BuildPoolRing(Sector(parent, "AnilloPoza"));
            BuildCragFoot(Sector(parent, "PieRisco"));
        }

        private static void BuildOrchardFence(Transform group)
        {
            if (!MeasureModel("fence-fortified", FenceHeight, true, out var size))
                return;

            var alongX = size.x >= size.z;
            var length = Mathf.Max(size.x, size.z);
            const float x0 = -38f;
            const float x1 = -18f;
            const float z0 = -30f;
            const float z1 = -12f;
            var gapEast = new[] { new Vector2(x1, -21f) };
            var gapNorth = new[] { new Vector2(-28f, z1) };
            FenceRun(group, "ValaSur", new Vector2(x0, z0), new Vector2(x1, z0), length, alongX, null);
            FenceRun(group, "ValaOeste", new Vector2(x0, z0), new Vector2(x0, z1), length, alongX, null);
            FenceRun(group, "ValaNorte", new Vector2(x0, z1), new Vector2(x1, z1), length, alongX, gapNorth);
            FenceRun(group, "ValaEste", new Vector2(x1, z0), new Vector2(x1, z1), length, alongX, gapEast);
            Gate(group, "HuertoEste", gapEast[0], Vector2.up, GateHalfOpening, "Huerto", "#D874A8");
            Gate(group, "HuertoNorte", gapNorth[0], Vector2.right, GateHalfOpening, "Huerto", "#D874A8");
        }

        private static void FenceRun(Transform group, string name, Vector2 from, Vector2 to, float pieceLength, bool pieceAlongX, Vector2[] gaps)
        {
            var dir = to - from;
            var runAlongX = Mathf.Abs(dir.x) > Mathf.Abs(dir.y);
            var yaw = runAlongX == pieceAlongX ? 0f : 90f;
            var i = 0;
            foreach (var p in Along(from, to, pieceLength * 0.98f, gaps, GateHalfOpening))
            {
                // No overlap check: neighbouring pieces and the corners touch each other by design.
                Spawn("fence-fortified", group, $"{name}_{i:00}", p, yaw, FenceHeight, Body.Box, _layerEnv, true, Support.Terrain, false, 0.08f, 0f, 0f, false);
                i++;
            }
        }

        // Line of woodpiles, crates and barrels at x = 19.5 (z -26..-8) with a gap at z -17. The ends of the line are open too.
        private static void BuildCampDivider(Transform group)
        {
            var gap = new[] { new Vector2(19.5f, -17f) };
            var i = 0;
            foreach (var p in Along(new Vector2(19.5f, -26f), new Vector2(19.5f, -8f), 2f, gap, GateHalfOpening))
            {
                var yaw = Hash01(p.x, p.y) * 360f;
                var name = $"Barrera_{i:00}";
                switch (i % 4)
                {
                    case 0:
                    case 3:
                        Spawn("SM_FirewoodPile", group, name, p, yaw, 1.9f, Body.Box, _layerEnv, false);
                        break;
                    case 1:
                        Spawn("box-large", group, name, p, yaw, 1.1f, Body.Box, _layerEnv);
                        break;
                    default:
                        Spawn("barrel", group, name, p, yaw, 1f, Body.Box, _layerEnv);
                        break;
                }

                i++;
            }

            Gate(group, "CampamentoSerreria", gap[0], Vector2.up, GateHalfOpening, "Campamento", "#E0903A");
        }

        // Border of rock clusters and fallen logs along z = 19 (x -14..14) with three gaps; short wings of logs reach x = +-18.
        private static void BuildHollowBorder(Transform group)
        {
            var gaps = new[] { new Vector2(-9f, 19f), new Vector2(0f, 19f), new Vector2(9f, 19f) };
            var i = 0;
            foreach (var p in Along(new Vector2(-14f, 19f), new Vector2(14f, 19f), 3.2f, gaps, 2.2f))
            {
                var rock = i % 2 == 0;
                Spawn(rock ? "SM_Rock_Cluster" : "SM_Log_Fallen", group, $"Borde_{i:00}", p, Hash01(p.x, p.y) * 360f, rock ? 3.2f : 3f,
                    rock ? Body.Mesh : Body.Box, _layerEnv, false, Support.Terrain, false, rock ? 0.12f : 0.06f);
                i++;
            }

            var wing = 0;
            foreach (var p in Along(new Vector2(-18f, 19.5f), new Vector2(-14f, 19f), 3.4f, null, 0f))
                Spawn("SM_Log_Fallen", group, $"AlaOeste_{wing++:00}", p, Hash01(p.x, p.y) * 360f, 3f, Body.Box, _layerEnv, false, Support.Terrain, false, 0.06f);
            foreach (var p in Along(new Vector2(14f, 19f), new Vector2(18f, 19.5f), 3.4f, null, 0f))
                Spawn("SM_Log_Fallen", group, $"AlaEste_{wing++:00}", p, Hash01(p.x, p.y) * 360f, 3f, Body.Box, _layerEnv, false, Support.Terrain, false, 0.06f);

            for (var k = 0; k < gaps.Length; k++)
                Gate(group, $"Hondonada{k + 1}", gaps[k], Vector2.right, 2.2f, "Hondonada", "#5E9E52");
        }

        private static bool AngleNear(float angle, float[] centers, float half)
        {
            foreach (var c in centers)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, c * Mathf.Rad2Deg)) < half * Mathf.Rad2Deg)
                    return true;
            }

            return false;
        }

        // Ring of rocks and ferns round the pool, open to the west (where the stream comes in) and to the south.
        private static void BuildPoolRing(Transform group)
        {
            var count = Mathf.RoundToInt(2f * Mathf.PI * PoolRingRadius / PoolRingStep);
            var gapAngles = new[] { Mathf.PI, Mathf.PI * 1.5f };
            var rocks = new[] { "SM_Rock_01", "SM_Rock_02", "SM_Rock_03", "SM_Rock_Cluster" };
            var sizes = new[] { 2.2f, 2f, 2.4f, 2.9f };
            var placed = 0;
            for (var i = 0; i < count; i++)
            {
                var angle = i * 2f * Mathf.PI / count;
                if (AngleNear(angle, gapAngles, PoolGapHalfAngle))
                    continue;

                var radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var p = PozaCenter + radial * (PoolRingRadius + (Hash01(i, 3.7f) - 0.5f));
                if (Mathf.Abs(p.x) > 36.5f || Mathf.Abs(p.y) > 33.5f || NearKeyPoint(p.x, p.y, DividerKeyAway))
                    continue;

                var kind = i % rocks.Length;
                var rock = Spawn(rocks[kind], group, $"RocaPoza_{i:00}", p, Hash01(i, 9.1f) * 360f, sizes[kind], Body.Mesh, _layerEnv, false, Support.Terrain, false, 0.12f);
                if (rock == null)
                    continue;

                placed++;
                if (placed % 2 == 0)
                {
                    SmallPlant(group, "SM_Fern", $"HelechoPoza_{i:00}a", p - radial * 1.7f, 0.8f, Support.Terrain, false);
                    SmallPlant(group, "SM_Fern", $"HelechoPoza_{i:00}b", p + radial * 1.9f, 0.8f, Support.Terrain, false);
                }
            }

            for (var g = 0; g < gapAngles.Length; g++)
            {
                var radial = new Vector2(Mathf.Cos(gapAngles[g]), Mathf.Sin(gapAngles[g]));
                var center = PozaCenter + radial * PoolRingRadius;
                Gate(group, g == 0 ? "PozaOeste" : "PozaSur", center, new Vector2(-radial.y, radial.x), 2.4f, "Poza", "#D8C48A");
            }
        }

        // Pines and cold rocks at the foot of the crag; the two accesses (see RegisterZones) stay clear and are marked.
        private static void BuildCragFoot(Transform group)
        {
            Vector2[] pines =
            {
                new(-37f, 8f), new(-35f, 3.5f), new(-31f, 32.5f), new(-36f, 32.5f), new(-24.5f, 28f), new(-23f, 24f),
                new(-21.5f, 31f), new(-27f, 34f), new(-20f, 12f), new(-37.5f, 26f),
            };
            for (var i = 0; i < pines.Length; i++)
            {
                if (InClearZone(pines[i].x, pines[i].y, 0.5f) || NearKeyPoint(pines[i].x, pines[i].y, GroveKeyAway))
                    continue;

                PlaceTree(group, $"PinoRisco_{i:00}", pines[i].x, pines[i].y, 0f, true, true);
            }

            Vector2[] rocks = { new(-20.5f, 11.5f), new(-31f, 5.5f), new(-24f, 33f), new(-33f, 33f), new(-35f, 6f), new(-23.5f, 26.5f), new(-38f, 21f) };
            var kinds = new[] { "SM_Rock_Cluster", "SM_Rock_01", "SM_Rock_Cluster", "SM_Rock_02", "SM_Rock_03", "SM_Rock_Flat", "SM_Rock_02" };
            for (var i = 0; i < rocks.Length; i++)
            {
                if (InClearZone(rocks[i].x, rocks[i].y, 0.5f) || NearKeyPoint(rocks[i].x, rocks[i].y, DividerKeyAway))
                    continue;

                Spawn(kinds[i], group, $"RocaFria_{i:00}", rocks[i], Hash01(i, 4.4f) * 360f, 2.6f, Body.Mesh, _layerEnv, false, Support.Terrain, false, 0.12f);
            }

            Gate(group, "RiscoSur", new Vector2(-24.5f, 7f), Vector2.right, 3.2f, "Risco", "#7C8FA8");
            Gate(group, "RiscoEste", new Vector2(-21f, 20f), Vector2.up, 3f, "Risco", "#7C8FA8");
        }

        // ================================================================== Paths

        private static void BuildPaths(Transform group)
        {
            for (var i = 0; i < Junctions.Length; i++)
            {
                var j = Junctions[i];
                Spawn("SM_PathStones", group, $"Piedras_{i:00}", j, Hash01(j.x, j.y) * 360f, 2.4f, Body.None, _layerEnv, false, Support.Terrain, false, 0.02f, 0f, 0f, false);
            }
        }

        // ================================================================== Groves

        private static bool TryClump(Transform parent, float x, float z, string name)
        {
            if (!GroveAllowed(x, z))
                return false;

            var made = new List<GameObject>();
            var count = 3 + Mathf.FloorToInt(Hash01(x, z) * 3f);
            for (var i = 0; i < count; i++)
            {
                var angle = i / (float)count * Mathf.PI * 2f + Hash01(x + i, z) * 1.2f;
                var radius = i == 0 ? 0f : 1.6f + Hash01(z, x + i) * 1.4f;
                var px = x + Mathf.Cos(angle) * radius;
                var pz = z + Mathf.Sin(angle) * radius;
                if (!GroveAllowed(px, pz))
                    continue;

                var tree = PlaceTree(parent, $"{name}_{i}", px, pz, 0f, true, false);
                if (tree != null)
                    made.Add(tree);
            }

            if (made.Count < 2)
            {
                foreach (var tree in made)
                    Object.DestroyImmediate(tree);
                Physics.SyncTransforms();
                return false;
            }

            for (var i = 0; i < 2; i++)
            {
                var angle = Hash01(x, z + i) * Mathf.PI * 2f;
                var px = x + Mathf.Cos(angle) * 2.4f;
                var pz = z + Mathf.Sin(angle) * 2.4f;
                if (GroveAllowed(px, pz))
                    PlaceBush(parent, $"{name}_Arbusto{i}", px, pz, 1.3f, _layerEnv, Body.None, false);
            }

            _groveCount++;
            return true;
        }

        private static void BuildGroves(Transform parent)
        {
            for (var i = 0; i < GroveAnchors.Length; i++)
                TryClump(parent, GroveAnchors[i].x, GroveAnchors[i].y, $"Bosquete_{i:00}");
        }

        // ================================================================== Small vegetation

        private static bool VegetationSpotOk(float x, float z, bool exclusions)
        {
            if (Mathf.Abs(x) > CoverLimitX || Mathf.Abs(z) > CoverLimitZ)
                return false;

            if (exclusions && CoverExcluded(x, z))
                return false;

            if (Vector2.Distance(new Vector2(x, z), PozaCenter) < WaterRadius + 0.4f || NearKeyPoint(x, z, 0.8f) || NearPath(x, z, 1.3f))
                return false;

            var y = TerrainY(x, z);
            if (y < CoverMinGroundY || TerrainNormalY(x, z) < 0.9f)
                return false;

            return !HitsSolid(new Vector3(x, y + 0.4f, z), 0.45f);
        }

        // Grass, ferns, flowers and mushrooms: no collider, no shadow, and at most SmallVegetationCap in the whole level.
        private static bool SmallPlant(Transform parent, string model, string name, Vector2 at, float height, Support support, bool exclusions)
        {
            if (_smallVegetation >= SmallVegetationCap)
                return false;

            if (support == Support.Terrain && !VegetationSpotOk(at.x, at.y, exclusions))
                return false;

            var go = Spawn(model, parent, name, at, Hash01(at.x, at.y) * 360f, height * (0.85f + 0.3f * Hash01(at.y, at.x)), Body.None, _layerEnv,
                true, support, false, 0.03f, 0f, 0f, false);
            if (go == null)
                return false;

            ShadowsOff(go);
            _smallVegetation++;
            return true;
        }

        private static void Scatter(Transform parent, string model, string prefix, Vector2 center, Vector2 half, int count, float height, int seed)
        {
            var placed = 0;
            for (var attempt = 0; attempt < count * 8 && placed < count; attempt++)
            {
                if (_smallVegetation >= SmallVegetationCap)
                    return;

                var x = center.x + (Hash01(seed + attempt * 1.7f, 3.1f) - 0.5f) * 2f * half.x;
                var z = center.y + (Hash01(7.3f, seed + attempt * 2.3f) - 0.5f) * 2f * half.y;
                if (SmallPlant(parent, model, $"{prefix}_{placed:00}", new Vector2(x, z), height, Support.Terrain, true))
                    placed++;
            }
        }

        private static void BuildVegetation(Transform parent)
        {
            // Camp
            Scatter(parent, "SM_Grass_01", "HierbaCampamento", new Vector2(0f, -24f), new Vector2(11f, 9f), 10, 0.6f, 1);
            Scatter(parent, "SM_Flowers_Yellow", "FloresCampamentoA", new Vector2(-8f, -23f), new Vector2(5f, 4f), 3, 0.5f, 2);
            Scatter(parent, "SM_Flowers_Purple", "FloresCampamentoB", new Vector2(8f, -26f), new Vector2(5f, 4f), 3, 0.5f, 3);
            // Sawmill
            Scatter(parent, "SM_Grass_02", "HierbaSerreria", new Vector2(28f, -14f), new Vector2(10f, 8f), 8, 0.6f, 4);
            Scatter(parent, "SM_Mushrooms", "SetasSerreriaA", new Vector2(22f, -11f), new Vector2(3f, 3f), 2, 0.35f, 5);
            Scatter(parent, "SM_Mushrooms", "SetasSerreriaB", new Vector2(35f, -20f), new Vector2(3f, 2f), 2, 0.35f, 6);
            // Orchard
            Scatter(parent, "SM_Grass_01", "HierbaHuerto", new Vector2(-28f, -20f), new Vector2(9f, 8f), 8, 0.55f, 7);
            // Crag
            Scatter(parent, "SM_Fern", "HelechoRisco", new Vector2(-30f, 18f), new Vector2(8f, 12f), 8, 0.8f, 8);
            Scatter(parent, "SM_Mushrooms", "SetasRisco", new Vector2(-24f, 24f), new Vector2(3f, 4f), 3, 0.35f, 9);
            Scatter(parent, "SM_Grass_02", "HierbaRisco", new Vector2(-29f, 6f), new Vector2(9f, 4f), 6, 0.6f, 10);
            // Hollow
            Scatter(parent, "SM_Fern", "HelechoHondonada", new Vector2(0f, 26f), new Vector2(14f, 7f), 24, 0.85f, 11);
            Scatter(parent, "SM_Mushrooms", "SetasHondonada", new Vector2(0f, 27f), new Vector2(14f, 7f), 14, 0.35f, 12);
            Scatter(parent, "SM_Grass_02", "HierbaHondonada", new Vector2(0f, 26f), new Vector2(14f, 7f), 12, 0.6f, 13);
            // Pool
            Scatter(parent, "SM_Grass_01", "HierbaPoza", new Vector2(28f, 26f), new Vector2(14f, 12f), 10, 0.6f, 14);
            Scatter(parent, "SM_Flowers_Yellow", "FloresPoza", new Vector2(28f, 26f), new Vector2(14f, 12f), 6, 0.5f, 15);
            // Corridors
            Scatter(parent, "SM_Grass_01", "HierbaPasillo1", new Vector2(-30f, -5f), new Vector2(8f, 5f), 5, 0.6f, 16);
            Scatter(parent, "SM_Grass_02", "HierbaPasillo2", new Vector2(14f, 8f), new Vector2(8f, 5f), 5, 0.6f, 17);
            Scatter(parent, "SM_Grass_01", "HierbaPasillo3", new Vector2(34f, 0f), new Vector2(4f, 6f), 4, 0.6f, 18);
            Scatter(parent, "SM_Grass_02", "HierbaPasillo4", new Vector2(-12f, 10f), new Vector2(5f, 5f), 3, 0.6f, 19);
            Scatter(parent, "SM_Grass_01", "HierbaPasillo5", new Vector2(20f, -32f), new Vector2(10f, 2f), 3, 0.6f, 20);
        }

        // ================================================================== Props of each sector

        private static GameObject Prop(Transform group, string model, string name, float x, float z, float yaw, float size, Body body,
            bool byHeight = true, Support support = Support.Terrain, bool lieFlat = false, float sink = 0.05f)
        {
            if (NearKeyPoint(x, z, 1.2f))
                return null;

            return Spawn(model, group, name, new Vector2(x, z), yaw, size, body, _layerEnv, byHeight, support, lieFlat, sink);
        }

        private static void AddLanternLight(GameObject lantern)
        {
            var go = new GameObject("Luz");
            go.transform.SetParent(lantern.transform, false);
            if (RenderBounds(lantern, out var b))
                go.transform.position = b.center + Vector3.up * (b.extents.y * 0.2f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = LanternLightRange;
            light.intensity = LanternLightIntensity;
            light.color = new Color(1f, 0.72f, 0.4f);
            light.shadows = LightShadows.None;
        }

        private static GameObject Lantern(Transform group, string name, float x, float z, Support support, float fixedY)
        {
            var lantern = Spawn("SM_Lantern", group, name, new Vector2(x, z), 0f, LanternHeight, Body.None, _layerEnv, true, support, false, 0.02f, 0f, fixedY, false);
            if (lantern != null)
                AddLanternLight(lantern);
            return lantern;
        }

        private static void BuildCampProps(Transform group)
        {
            Prop(group, "SM_PicnicTable", "MesaCampamento", 7f, -22f, 90f, 2.4f, Body.Box, false);
            Prop(group, "SM_CampChair", "SillaCampamento_1", 4.6f, -22.3f, 90f, 0.9f, Body.Box);
            Prop(group, "SM_CampChair", "SillaCampamento_2", 9.6f, -22f, -90f, 0.9f, Body.Box);
            Prop(group, "SM_CampChair", "SillaCampamento_3", -12.5f, -25f, 40f, 0.9f, Body.Box);
            Prop(group, "SM_LogBench", "BancoCampamento_1", 7f, -19.5f, 90f, 2.2f, Body.Box, false);
            Prop(group, "SM_LogBench", "BancoCampamento_2", -4f, -29f, 10f, 2.2f, Body.Box, false);
            Prop(group, "SM_Cooler", "NeveraCampamento", 11f, -28f, 25f, 0.6f, Body.Box);
            Prop(group, "SM_Backpack", "MochilaCampamento_1", -6.5f, -29.5f, 15f, 0.6f, Body.Box);
            Prop(group, "SM_Backpack", "MochilaCampamento_2", 12f, -30f, 110f, 0.6f, Body.Box);
            Prop(group, "SM_SleepingBag_Rolled", "SacoCampamento", -11f, -29f, 70f, 0.8f, Body.None, false);
            Lantern(group, "FarolMesa", 7f, -22f, Support.Structure, 0f);
            Lantern(group, "FarolTienda_1", -10.8f, -24.6f, Support.Terrain, 0f);
            Lantern(group, "FarolTienda_2", 7.4f, -30.4f, Support.Terrain, 0f);
        }

        private static void BuildSawmillProps(Transform group)
        {
            Prop(group, "resource-planks", "TablasApiladas_1", 22.5f, -19f, 15f, 1.8f, Body.Box, false);
            Prop(group, "resource-planks", "TablasApiladas_2", 36f, -20.5f, -20f, 1.8f, Body.Box, false);
            Prop(group, "box-large", "CajaSerreria_1", 23.2f, -8.5f, 10f, 1.1f, Body.Box);
            Prop(group, "box-large", "CajaSerreria_2", 35.5f, -9.8f, 40f, 1.1f, Body.Box);
            Prop(group, "box-large", "CajaSerreria_3", 24.4f, -9.4f, 75f, 0.8f, Body.Box);
            Prop(group, "SM_StumpAxe", "TroncoHacha", 26.5f, -9.2f, 30f, 1f, Body.Box);
            Prop(group, "SM_Log_Fallen", "TroncoCaidoSerreria", 34f, -7.2f, 15f, 3.2f, Body.Box, false);
            Prop(group, "SM_FirewoodPile", "LenaSerreria", 22.2f, -13.5f, 90f, 1.9f, Body.Box, false);
            Prop(group, "barrel", "BarrilSerreria", 38f, -17.5f, 0f, 1f, Body.Box);
            Lantern(group, "FarolSerreria", 27f, -9.2f, Support.Terrain, 0f);
        }

        // Flower beds: yellow and purple flowers between the plants of every bed (on top of the beds, no collider).
        private static void BuildOrchardProps(Transform group)
        {
            var beds = new[] { -17.5f, -20f, -26f, -28.5f };
            var flower = 0;
            foreach (var z in beds)
            {
                for (var i = 0; i < 4; i++)
                {
                    var model = (flower & 1) == 0 ? "SM_Flowers_Purple" : "SM_Flowers_Yellow";
                    SmallPlant(group, model, $"FlorBancal_{flower:00}", new Vector2(-30.75f + i * 1.5f, z), 0.55f, Support.Structure, false);
                    flower++;
                }
            }
        }

        private static void BuildHollowProps(Transform group)
        {
            Vector2[] dead = { new(-13f, 22.5f), new(13f, 22.5f), new(-3f, 34f) };
            for (var i = 0; i < dead.Length; i++)
            {
                if (NearKeyPoint(dead[i].x, dead[i].y, 1.2f))
                    continue;

                Spawn("SM_Tree_Dead", group, $"ArbolSeco_{i}", dead[i], Hash01(i, 6.3f) * 360f, 6f, Body.Capsule, _layerEnv, true, Support.Terrain, false, 0.1f, 0.35f);
            }
        }

        private static void BuildPoolProps(Transform group)
        {
            Prop(group, "SM_Canoe", "Canoa", 24.6f, 16.6f, 30f, 3.2f, Body.Box, false, Support.Terrain, false, 0.12f);
            Prop(group, "SM_Paddle", "Remo", 36.8f, 27.6f, 70f, 1.5f, Body.None, false, Support.Structure, true, 0f);
            Prop(group, "SM_FishingRod", "CanaPescar", 37.4f, 28.5f, 20f, 1.8f, Body.None, false, Support.Structure, true, 0f);
            Lantern(group, "FarolMuelle", 31.4f, 27.5f, Support.Structure, 0f);
        }

        // ================================================================== Ramp lanterns and low fog

        private static void BuildRampLanterns(Transform group)
        {
            var n = 0;
            foreach (var z in new[] { -19f, -11f, -4f })
            {
                var y = RampEntryEndY * (z - RampEntryStartZ) / -RampEntryStartZ;
                Lantern(group, $"FarolRampa_{n++:00}", RampEntryX + RampLanternOffset, z, Support.Fixed, y);
            }

            // First lap only (the second lap overlaps the first from 0 to 41 degrees).
            for (var d = ArcLanternFirst; d < SecondLapDeg; d += RampLanternStepDeg)
            {
                var angle = d * Mathf.Deg2Rad;
                var p = RampPos(d);
                Lantern(group, $"FarolRampa_{n++:00}", p.x + Mathf.Cos(angle) * RampLanternOffset, p.z + Mathf.Sin(angle) * RampLanternOffset, Support.Fixed, p.y);
            }
        }

        private const float ArcLanternFirst = 52f;

        private static void BuildFog(Transform group)
        {
            var go = new GameObject("NieblaHondonada");
            go.transform.SetParent(group, false);
            go.transform.position = new Vector3(HondonadaCenter.x, TerrainY(HondonadaCenter.x, HondonadaCenter.y) + 0.5f, HondonadaCenter.y);
            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true;
            main.startLifetime = 14f;
            main.startSpeed = 0.15f;
            main.startSize = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.92f, 0.95f, 0.14f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 48;
            var emission = system.emission;
            emission.rateOverTime = 3.5f;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(28f, 0.4f, 16f);
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SmokeMaterialPath);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ================================================================== Cover (tall occluding bushes)

        private static void ScatterCover(Transform parent)
        {
            foreach (var line in PathLines)
                CoverAlong(parent, line);

            // Dense cover in the hollow and the orchard.
            CoverLattice(parent, new Vector2(-15f, 20.5f), new Vector2(15f, 34f), 3.3f, 0.9f, 11);
            CoverLattice(parent, new Vector2(-36.5f, -28.5f), new Vector2(-19.5f, -13.5f), 3.6f, 0.9f, 23);
        }

        // A bush every ~5 m on alternating sides of each route.
        private static void CoverAlong(Transform parent, Vector2[] line)
        {
            var distance = CoverRouteStep * 0.5f;
            var side = 1f;
            for (var i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i];
                var b = line[i + 1];
                var length = Vector2.Distance(a, b);
                var dir = (b - a) / length;
                var normal = new Vector2(-dir.y, dir.x);
                while (distance < length)
                {
                    var offset = CoverRouteOffset + Hash01(a.x + distance, a.y) * 0.9f;
                    var p = a + dir * distance + normal * (side * offset);
                    TryCover(parent, p.x, p.y);
                    side = -side;
                    distance += CoverRouteStep + (Hash01(distance, a.x) - 0.5f) * 1.2f;
                }

                distance -= length;
            }
        }

        private static void CoverLattice(Transform parent, Vector2 min, Vector2 max, float spacing, float jitter, int seed)
        {
            for (var x = min.x; x <= max.x; x += spacing)
            {
                for (var z = min.y; z <= max.y; z += spacing)
                {
                    var px = x + (Hash01(x + seed, z) - 0.5f) * 2f * jitter;
                    var pz = z + (Hash01(z, x + seed) - 0.5f) * 2f * jitter;
                    TryCover(parent, px, pz);
                }
            }
        }

        private static void TryCover(Transform parent, float x, float z)
        {
            if (_bushCount >= CoverMaxBushes || Mathf.Abs(x) > CoverLimitX || Mathf.Abs(z) > CoverLimitZ)
                return;

            if (CoverExcluded(x, z) || InClearZone(x, z, 0.3f) || NearKeyPoint(x, z, CoverKeepAway) || NearPath(x, z, CoverNearPath))
                return;

            // Only bare, gently sloping terrain, not in the pool.
            if (TerrainNormalY(x, z) < CoverMinNormalY || TerrainY(x, z) < CoverMinGroundY)
                return;

            var height = CoverBushMinHeight + Hash01(x + 2f, z + 8f) * CoverBushExtraHeight;
            var bush = PlaceBush(parent, $"Arbusto_{_bushCount:000}", x, z, height, _layerOccluder, Body.Sphere, true);
            if (bush != null)
                _bushCount++;
        }

        // ================================================================== Decoys on the shoulder, support check

        private static Vector2[] ShoulderAnchors()
        {
            var p = RampPos(ShoulderDeg);
            return new[] { new Vector2(p.x - 1.3f, p.z - 1.2f), new Vector2(p.x + 1.5f, p.z - 1.6f), new Vector2(p.x + 1.6f, p.z + 1.5f) };
        }

        private static bool RejectedDecoy(float x, float z)
        {
            foreach (var r in DecoyRejects)
            {
                if ((r - new Vector2(x, z)).sqrMagnitude < 0.35f * 0.35f)
                    return true;
            }

            return false;
        }

        // The decoy's footprint must lie on one flat surface: the ground under its centre and under four points of its outline agree.
        private static bool FootprintSupported(GameObject go)
        {
            if (!RenderBounds(go, out var b))
                return true;

            if (!TryGround(b.center.x, b.center.z, go.transform, out var center))
                return false;

            var e = b.extents * 0.8f;
            for (var i = 0; i < 4; i++)
            {
                var x = b.center.x + ((i & 1) == 0 ? -e.x : e.x);
                var z = b.center.z + ((i & 2) == 0 ? -e.z : e.z);
                if (!TryGround(x, z, go.transform, out var hit) || Mathf.Abs(hit.point.y - center.point.y) > 0.06f)
                    return false;
            }

            return true;
        }

        // ================================================================== Fireplace seat

        // The fireplace sits on its lowest visible mesh (not on its lowest collider), on the mound of the terrain.
        private static void SeatOnVisualBase(GameObject go, Vector2 spot)
        {
            var uiLayer = LayerMask.NameToLayer("UI");
            var lowest = float.MaxValue;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r.GetComponent<TMP_Text>() != null || r.gameObject.layer == uiLayer)
                    continue;

                lowest = Mathf.Min(lowest, r.bounds.min.y);
            }

            if (lowest >= float.MaxValue)
                return;

            go.transform.position += Vector3.up * (TerrainY(spot.x, spot.y) - lowest);
            Physics.SyncTransforms();
        }

        // ================================================================== Tree details

        private static void TrunkArc(float side, out float a1, out float a2)
        {
            var dir = Dir2;
            var normal = new Vector2(-dir.y, dir.x);
            var along = Mathf.Sqrt(TrunkRadius * TrunkRadius - SlitHalfWidth * SlitHalfWidth);
            var e1 = normal * (side * SlitHalfWidth) + dir * along;
            var e2 = normal * (side * SlitHalfWidth) - dir * along;
            a1 = Mathf.Atan2(e1.y, e1.x);
            a2 = Mathf.Atan2(e2.y, e2.x);
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
        }

        private static bool NearSlit(Vector3 point, float distance)
        {
            return DistanceToSegment(new Vector2(point.x, point.z), SlitStart, SlitEnd) < distance;
        }

        // Bark ribs, moss on the north face and shelf mushrooms. All of it is visual (no colliders) and marked as intentionally
        // floating, like the braid of ribbons: it clings to the trunks at heights the audit would call "floating".
        private static void BuildTrunkDetails(Transform parent)
        {
            var group = Group("DetallesCorteza");
            group.SetParent(parent, false);
            MarkFloating(group.gameObject);
            BuildBarkRibs(group, -1f, Mat("CostillaOscura", "#3F271A"));
            BuildBarkRibs(group, 1f, Mat("CostillaPalida", "#C4B598"));
            BuildMoss(group);
            BuildShelfFungi(group);
        }

        private const int RibCount = 26;

        private static void BuildBarkRibs(Transform group, float side, Material material)
        {
            TrunkArc(side, out var a1, out var a2);
            for (var i = 0; i < RibCount; i++)
            {
                var t = (i + 0.2f + 0.6f * Hash01(i * 3.1f, side * 7.7f)) / RibCount;
                var angle = Mathf.Lerp(a1, a2, t);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var length = 5f + Hash01(i, side + 11f) * 7.5f;
                var bottom = 0.3f + Hash01(i + 5f, side) * 3f;
                var width = 0.22f + Hash01(i, side + 3f) * 0.3f;
                var center = radial * (TrunkRadius + 0.04f) + Vector3.up * (bottom + length * 0.5f);
                if (NearSlit(center, 1.4f))
                    continue;

                var rotation = Quaternion.AngleAxis((Hash01(side, i) - 0.5f) * 10f, radial) * Quaternion.LookRotation(radial, Vector3.up);
                Box(group, $"Costilla_{(side < 0f ? "O" : "P")}{i:00}", center, new Vector3(width, length, 0.3f), material, _layerEnv, false, rotation);
            }
        }

        private static void BuildMoss(Transform group)
        {
            var light = Mat("MusgoClaro", "#76A04A");
            var dark = Mat("MusgoOscuro", "#5E8A3C");
            for (var i = 0; i < 20; i++)
            {
                var angle = Mathf.Lerp(35f, 145f, (i + Hash01(i, 2.2f)) / 20f) * Mathf.Deg2Rad;
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var center = radial * (TrunkRadius + 0.0f) + Vector3.up * (0.8f + Hash01(i, 5.5f) * 5.7f);
                if (NearSlit(center, 1.6f))
                    continue;

                var scale = new Vector3(1.2f + Hash01(i, 8.8f) * 1.2f, 1f + Hash01(9.9f, i) * 1.8f, 0.32f);
                var patch = Sphere(group, $"Musgo_{i:00}", center, scale, (i & 1) == 0 ? light : dark, _layerEnv, false);
                patch.transform.rotation = Quaternion.LookRotation(radial, Vector3.up);
            }
        }

        private static void BuildShelfFungi(Transform group)
        {
            var cream = Mat("SetaCrema", "#E3C58B");
            var red = Mat("SetaRoja", "#B9573A");
            for (var i = 0; i < 10; i++)
            {
                var angle = (i + Hash01(i, 1.3f)) / 10f * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var center = radial * (TrunkRadius + 0.35f) + Vector3.up * (1.5f + Hash01(i, 4.1f) * 5.5f);
                if (NearSlit(center, 1.8f))
                    continue;

                var shelf = Sphere(group, $"SetaRepisa_{i:00}", center, new Vector3(1f, 0.3f, 0.75f), (i & 1) == 0 ? cream : red, _layerEnv, false);
                shelf.transform.rotation = Quaternion.LookRotation(radial, Vector3.up);
            }
        }

        // ================================================================== Root bumps

        private static readonly float[] RootStepEdges = { 7f, 13f, 19f, RootTipX };
        private static readonly float[] RootStepHalfThickness = { 1f, 0.85f, 0.7f };
        private static readonly float[] RootStepHeight = { 2.8f, 2.7f, 2.55f };
        private static readonly float[] RootBumpX = { 9f, 21.5f };

        // Knuckles on the sides of the root wall (solid, so what you see is what you bump into).
        private static void RootBumps(Transform wall, float side, Material material)
        {
            var n = 0;
            foreach (var ax in RootBumpX)
            {
                for (var s = -1; s <= 1; s += 2)
                {
                    var x = side * ax + (Hash01(ax, s) - 0.5f) * 1.2f;
                    var thickness = RootStepHalfThickness[ax < RootStepEdges[1] ? 0 : 2];
                    var z = s * (thickness + 0.3f);
                    var height = 1.2f + Hash01(x, s) * 0.8f;
                    var yaw = (Hash01(s, x) - 0.5f) * 24f;
                    Box(wall, $"Nudo_{n++}", new Vector3(x, (height - StairSink) * 0.5f, z), new Vector3(2.6f + Hash01(x, z) * 0.8f, height + StairSink, 0.7f),
                        material, _layerEnv, true, Quaternion.Euler(0f, yaw, 0f));
                }
            }
        }

        // ================================================================== Signs

        private static void BuildSign(Transform parent, string sector, string title, Vector2 spot, string hex)
        {
            var ground = GroundPoint(spot);
            Box(parent, $"Rotulo_{sector}", new Vector3(spot.x, ground.y + 1.1f - StairSink * 0.5f, spot.y), new Vector3(0.15f, 2.2f + StairSink, 0.15f), Mat("PosteRotulo", SignPostHex), _layerEnv);

            var toCenter = new Vector3(-spot.x, 0f, -spot.y);
            var board = new GameObject("Tablero");
            board.transform.SetParent(parent, false);
            board.transform.SetPositionAndRotation(new Vector3(spot.x, ground.y + 1.85f, spot.y), Quaternion.LookRotation(toCenter.sqrMagnitude < 0.01f ? Vector3.forward : toCenter.normalized));
            MarkFloating(board);

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Placa";
            plate.transform.SetParent(board.transform, false);
            plate.transform.localScale = new Vector3(2.4f, 0.75f, 0.1f);
            plate.GetComponent<MeshRenderer>().sharedMaterial = Mat($"Rotulo{sector}", hex);
            plate.layer = _layerEnv;
            Object.DestroyImmediate(plate.GetComponent<Collider>());

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
            SignText(board.transform, title, font, -0.07f, 0f);
            SignText(board.transform, title, font, 0.07f, 180f);
            KeyPoints.Add(spot);
        }

        private static void SignText(Transform board, string text, TMP_FontAsset font, float z, float yaw)
        {
            var go = new GameObject("Texto");
            var tmp = go.AddComponent<TextMeshPro>();
            go.transform.SetParent(board, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.layer = _layerEnv;
            if (font != null)
                tmp.font = font;
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.17f, 0.11f, 0.07f);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 1f;
            tmp.fontSizeMax = 4f;
            tmp.rectTransform.sizeDelta = new Vector2(2.3f, 0.65f);
        }

        // ================================================================== Validation

        // The camper (standing, 1.8 m) must be able to walk the entry straight, the bridge over the east root and the first lap.
        private static int ValidateCamperRamp(int mask)
        {
            var failures = 0;
            for (var z = RampEntryStartZ + 0.5f; z < 0.5f; z += 0.5f)
            {
                var y = RampEntryEndY * (z - RampEntryStartZ) / -RampEntryStartZ;
                failures += CamperAt(new Vector3(RampEntryX, y, z), mask, $"entry z={z:0.0}", failures);
            }

            for (var d = 0f; d <= RampEndDeg; d += 4f)
                failures += CamperAt(RampPos(d), mask, $"arc {d:0}", failures);

            return failures;
        }

        private static int CamperAt(Vector3 deckPoint, int mask, string label, int alreadyFailed)
        {
            const float lift = 0.12f;
            var bottom = deckPoint + Vector3.up * (CamperRadius + lift);
            var top = deckPoint + Vector3.up * (1.8f - CamperRadius + lift);
            if (!Physics.CheckCapsule(bottom, top, CamperRadius, mask, QueryTriggerInteraction.Ignore))
                return 0;

            if (alreadyFailed < 5)
                Debug.LogWarning($"{LogPrefix} The standing camper bumps into something on the ramp at {label}.");
            return 1;
        }

        // Every gate must leave an opening of at least GateOpeningMin along the wall (a box 1.2 m high, 0.6 m deep, above the terrain).
        private static int ValidateGates(int mask)
        {
            var failures = 0;
            foreach (var gate in Gates)
            {
                var center = new Vector3(gate.Center.x, TerrainY(gate.Center.x, gate.Center.y) + 1.4f, gate.Center.y);
                var forward = new Vector3(-gate.WallDirection.y, 0f, gate.WallDirection.x);
                if (Physics.CheckBox(center, new Vector3(GateOpeningMin * 0.5f, 0.6f, 0.3f), Quaternion.LookRotation(forward), mask, QueryTriggerInteraction.Ignore))
                {
                    failures++;
                    Debug.LogWarning($"{LogPrefix} Gate {gate.Name} is narrower than {GateOpeningMin} m.");
                }
            }

            return failures;
        }

        // ================================================================== Sight lines

        private static bool SightOrigin(float x, float z, out Vector3 eye)
        {
            eye = default;
            if (Mathf.Abs(x) > 35f || Mathf.Abs(z) > 32f || Mathf.Sqrt(x * x + z * z) < 15f || InSectorInterior(x, z) || InCampView(x, z))
                return false;

            eye = new Vector3(x, TerrainY(x, z) + SightEye, z);
            return !HitsSolid(eye, 0.3f);
        }

        private static float SightLength(Vector3 eye, Vector3 dir)
        {
            return Physics.Raycast(eye, dir, out var hit, SightMax, _groundMask | (1 << _layerOccluder), QueryTriggerInteraction.Ignore) ? hit.distance : SightMax;
        }

        // Adds a few clumps of trees where an eye-level line stays open for more than 15 m (the intended long views excepted),
        // then reports how many such lines are left.
        private static void FillSightLines(Transform parent)
        {
            var added = 0;
            for (var pass = 0; pass < SightPasses && added < SightMaxClumps; pass++)
            {
                var addedThisPass = 0;
                for (var gx = -34f; gx <= 34f; gx += SightGrid)
                {
                    for (var gz = -31f; gz <= 31f; gz += SightGrid)
                    {
                        if (!SightOrigin(gx, gz, out var eye))
                            continue;

                        for (var k = 0; k < SightDirections && added < SightMaxClumps; k++)
                        {
                            var angle = (k + pass * 0.5f) * Mathf.PI * 2f / SightDirections;
                            var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                            if (SightLength(eye, dir) <= SightLimit)
                                continue;

                            for (var step = 0; step < 3; step++)
                            {
                                var p = eye + dir * (9f - step * 2f);
                                if (TryClump(parent, p.x, p.z, $"Relleno_{added:00}"))
                                {
                                    added++;
                                    addedThisPass++;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (addedThisPass == 0)
                    break;
            }

            var open = 0;
            var total = 0;
            for (var gx = -34f; gx <= 34f; gx += SightGrid)
            {
                for (var gz = -31f; gz <= 31f; gz += SightGrid)
                {
                    if (!SightOrigin(gx, gz, out var eye))
                        continue;

                    for (var k = 0; k < SightDirections; k++)
                    {
                        var angle = k * Mathf.PI * 2f / SightDirections;
                        total++;
                        if (SightLength(eye, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))) > SightLimit)
                            open++;
                    }
                }
            }

            Debug.Log($"{LogPrefix} Sight lines: {_groveCount} groves ({added} added to close lines); {open} of {total} sampled eye-level lines are still open beyond {SightLimit} m (camp, trunk, sector interiors excluded).");
        }

        // ================================================================== Performance

        // Marks as static everything that never moves (not the networked groups, lights, particles, text or rigidbodies).
        private static void MarkStaticScenery()
        {
            const StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;
            var count = 0;
            foreach (Transform group in _root)
            {
                if (group.name == "Items" || group.name == "Escondites" || group.name == "Senuelos")
                    continue;

                foreach (var t in group.GetComponentsInChildren<Transform>(true))
                {
                    var go = t.gameObject;
                    if (go.GetComponent<Light>() != null || go.GetComponent<ParticleSystem>() != null || go.GetComponent<Rigidbody>() != null || go.GetComponent<TMP_Text>() != null)
                        continue;

                    GameObjectUtility.SetStaticEditorFlags(go, flags);
                    count++;
                }
            }

            Debug.Log($"{LogPrefix} Marked {count} objects as static.");
        }

        private static void LogSceneWeight()
        {
            var renderers = _root.GetComponentsInChildren<Renderer>(true).Length;
            var colliders = _root.GetComponentsInChildren<Collider>(true).Length;
            Debug.Log($"{LogPrefix} Weight: {renderers} renderers, {colliders} colliders under {RootName}; {_smallVegetation} small plants (cap {SmallVegetationCap}), {_bushCount} cover bushes, {_groveCount} groves.");
        }
    }
}
