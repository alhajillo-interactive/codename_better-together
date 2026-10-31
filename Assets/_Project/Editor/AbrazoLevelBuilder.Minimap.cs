// Editor-only (C9.6, replaces the camera bake of C9.5): draws the camp plan of El Abrazo as a hand-drawn paper sheet and creates the
// MinimapArea of the scene. Menu GnoMore/Hornear minimapa de El Abrazo. Build runs the same two steps at its end.
// The picture is NOT rendered from the scene: it is generated pixel by pixel from the constants of this builder (distances to
// polygons and polylines), wobbled with Perlin noise so the strokes look hand-drawn. Paper background, ink strokes, photoSepia fills.
// It never draws logs, matches, sausages, hiding spots, decoys or the bucket. All public entry points are dialog-free.
using System.Collections.Generic;
using System.IO;
using BetterTogether.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static partial class AbrazoLevelBuilder
    {
        private const string MinimapPath = SpritesDir + "UI/Minimap_Abrazo.png";
        private const string MinimapThemePath = "Assets/_Project/Resources/UiTheme.asset";
        private const string MinimapAreaName = "Minimapa";
        private const int MinimapWidth = 1024;
        private const int MinimapHeight = 945;
        // World rectangle the picture covers (XZ plane), centred on the world origin. Must match the MinimapArea written below.
        private const float MinimapWorldWidth = 78f;
        private const float MinimapWorldHeight = 72f;
        // Hand-drawn look: low-frequency wobble of the whole drawing, and per-pixel variation of the stroke width.
        private const float PlanWobbleScale = 0.22f;
        private const float PlanWobbleAmount = 1.4f;
        private const float PlanWidthScale = 1.7f;
        private const float PlanEdgeSoftness = 0.08f;
        private const float PlanOutlineWidth = 0.34f;
        private const float PlanThinWidth = 0.16f;
        private const float PlanPathWidth = 0.24f;
        private const float PlanDotPeriod = 0.95f;
        private const float PlanDotOn = 0.5f;
        private const float PlanHatchSpacing = 1f;
        private const float PlanHatchWidth = 0.12f;
        private const int PlanCircleSegments = 64;
        // Orchard fence (same rectangle and gaps as BuildOrchardFence in the Ambience part of this builder).
        private const float PlanFenceX0 = -38f;
        private const float PlanFenceX1 = -18f;
        private const float PlanFenceZ0 = -30f;
        private const float PlanFenceZ1 = -12f;
        private static readonly Vector2 PlanFenceGapEast = new(PlanFenceX1, -21f);
        private static readonly Vector2 PlanFenceGapNorth = new(-28f, PlanFenceZ1);

        // Sector names written on the plan (text, world position of the centre of the text).
        private static readonly (string text, Vector2 position)[] PlanLabels =
        {
            ("Campamento", new Vector2(-4f, -26f)),
            ("Serrería", new Vector2(31f, -20.5f)),
            ("Huerto", new Vector2(-28f, -8.5f)),
            ("Risco", new Vector2(-30f, 32.5f)),
            ("Hondonada", new Vector2(0f, 31f)),
            ("La Poza", new Vector2(28f, 13f)),
        };

        [MenuItem("GnoMore/Hornear minimapa de El Abrazo")]
        public static void BakeMinimap()
        {
            if (!OpenAbrazo())
                return;

            _root = FindAbrazoRoot();
            if (_root == null)
            {
                Debug.LogWarning($"{LogPrefix} The scene has no '{RootName}' root; run GnoMore/Construir nivel El Abrazo first.");
                return;
            }

            if (!BakeMinimapPicture())
                return;

            EnsureMinimapArea();
            EditorSceneManager.MarkSceneDirty(_scene);
            EditorSceneManager.SaveScene(_scene);
            AssetDatabase.SaveAssets();
        }

        private static Transform FindAbrazoRoot()
        {
            var roots = _scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == RootName)
                    return roots[i].transform;
            }

            return null;
        }

        // Draws the plan and writes Sprites/UI/Minimap_Abrazo.png. Needs no camera and no open scene.
        private static bool BakeMinimapPicture()
        {
            var paper = new Color32(0xF9, 0xEA, 0xC8, 0xFF);
            var ink = new Color32(0x5E, 0x2C, 0x12, 0xFF);
            var sepia = new Color32(0xE8, 0xD5, 0xAE, 0xFF);
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(MinimapThemePath);
            if (theme != null)
            {
                paper = theme.paper;
                ink = theme.ink;
                sepia = theme.photoSepia;
            }

            var canvas = new PlanCanvas(MinimapWidth, MinimapHeight, MinimapWorldWidth, MinimapWorldHeight);
            DrawPlan(canvas);

            var texture = new Texture2D(MinimapWidth, MinimapHeight, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(canvas.Compose(paper, sepia, ink));
                texture.Apply(false);
                Directory.CreateDirectory(Path.GetDirectoryName(MinimapPath) ?? "Assets");
                File.WriteAllBytes(MinimapPath, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(MinimapPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureMinimapTexture();
            Debug.Log($"{LogPrefix} Camp plan drawn to {MinimapPath}: {MinimapWidth}x{MinimapHeight} px, centre (0, 0), size {MinimapWorldWidth} x {MinimapWorldHeight} m.");
            return true;
        }

        // ================================================================== The drawing

        // Order matters: dotted paths first (every filled shape erases the ink under it), then the ground features from the
        // back to the front. Everything comes from the constants of this builder, so the plan follows the level.
        private static void DrawPlan(PlanCanvas c)
        {
            // Forest edge: the sepia ring outside the clearing, with its ink line.
            c.FillOutsideRect(ClearingHalfX, ClearingHalfZ);
            c.Stroke(Rectangle(-ClearingHalfX, ClearingHalfX, -ClearingHalfZ, ClearingHalfZ), true, PlanOutlineWidth * 1.4f);

            // Dotted paths between the sectors.
            var paths = new[]
            {
                new[] { new Vector2(0f, -26f), new Vector2(12f, -22.5f), new Vector2(26f, -21f) },
                new[] { new Vector2(0f, -26f), new Vector2(-10f, -22f), new Vector2(-18f, -21f), new Vector2(-26f, -21f) },
                new[] { new Vector2(-4f, -24f), new Vector2(-14f, -10f), new Vector2(-24f, -3f), new Vector2(-26f, 6f) },
                new[] { new Vector2(33f, -12f), new Vector2(32f, 6f), new Vector2(30f, 16f), new Vector2(26f, 20f) },
                new[] { new Vector2(-26f, 31f), new Vector2(-14f, 29f), new Vector2(-9f, 27f) },
            };
            foreach (var path in paths)
                c.Stroke(path, false, PlanPathWidth, PlanDotPeriod, PlanDotOn);

            // Hondonada: a dotted ring around its flat floor.
            c.Stroke(Circle(HondonadaCenter, HondonadaFlatRadius), true, PlanThinWidth, PlanDotPeriod, PlanDotOn);

            // La Poza: striped water with its shore, and the stream that feeds the hondonada.
            var pool = Circle(PozaCenter, WaterRadius);
            c.Hatch(pool, PlanHatchSpacing, PlanHatchWidth);
            c.Stroke(pool, true, PlanOutlineWidth);
            var streamHalf = (StreamInnerHalfWidth + StreamOuterHalfWidth) * 0.5f;
            var stream = Rectangle(StreamEnd.x, StreamStart.x, StreamStart.y - streamHalf, StreamStart.y + streamHalf);
            c.Hatch(stream, PlanHatchSpacing * 0.9f, PlanHatchWidth);
            c.Stroke(stream, true, PlanThinWidth);

            // Muelle: deck with its planks.
            DrawBoards(c, 30.5f, 38.5f, 27f, 29f, 1f);

            // Terrazas del Risco.
            DrawSlab(c, -34f, -22f, 10f, 18f);
            DrawSlab(c, -37f, -27f, 17f, 23f);
            DrawSlab(c, -34f, -26f, 24f, 30f);

            // Pasarelas de la Serrería.
            DrawBoards(c, 24f, 32f, -16f, -12f, 1f);
            DrawBoards(c, 35.5f, 38.5f, -16f, -12f, 1f);

            // Huerto: the beds and the fence (dashed, with its two gates).
            foreach (var z in new[] { -17.5f, -20f, -26f, -28.5f })
                DrawSlab(c, -32.5f, -25.5f, z - 0.6f, z + 0.6f, PlanThinWidth);

            DrawFence(c);

            // The two trunks with the slit between them: the slit is a paper band cut through the sepia disc.
            var trunk = Circle(Vector2.zero, TrunkRadius);
            c.Fill(trunk, 1f);
            c.Stroke(trunk, true, PlanOutlineWidth * 1.3f);
            var dir = Dir2;
            var normal = new Vector2(-dir.y, dir.x);
            var slitHalf = SlitHalfWidth + 0.05f;
            var band = new[]
            {
                SlitStart + normal * slitHalf, SlitEnd + normal * slitHalf, SlitEnd - normal * slitHalf, SlitStart - normal * slitHalf,
            };
            c.Fill(band, 0f);
            c.Stroke(new[] { SlitStart + normal * slitHalf, SlitEnd + normal * slitHalf }, false, PlanThinWidth);
            c.Stroke(new[] { SlitStart - normal * slitHalf, SlitEnd - normal * slitHalf }, false, PlanThinWidth);

            // The roots on both sides, each with its gnome tunnel, and the U of the fire.
            DrawRoot(c, 1f);
            DrawRoot(c, -1f);
            var fireHalf = (FireU_InnerX + FireU_OuterX) * 0.5f;
            c.Stroke(new[]
            {
                new Vector2(-fireHalf, FireU_FrontZ), new Vector2(-fireHalf, (FireU_BackZ + FireU_BackOuterZ) * 0.5f),
                new Vector2(fireHalf, (FireU_BackZ + FireU_BackOuterZ) * 0.5f), new Vector2(fireHalf, FireU_FrontZ),
            }, false, PlanOutlineWidth);

            // The ramp: entry bridge from the camp, then the spiral around the trunks. A sepia band with ink edges.
            var ramp = new List<Vector2> { new(RampEntryX, RampEntryStartZ), new(RampArcStartRadius, 0f) };
            for (var degrees = 4f; degrees < RampEndDeg; degrees += 4f)
            {
                var p = RampPos(degrees);
                ramp.Add(new Vector2(p.x, p.z));
            }

            var last = RampPos(RampEndDeg);
            ramp.Add(new Vector2(last.x, last.z));
            var rampPoints = ramp.ToArray();
            c.StrokeFilled(rampPoints, RampWidth);
            c.Stroke(OffsetLine(rampPoints, RampWidth * 0.5f), false, PlanThinWidth);
            c.Stroke(OffsetLine(rampPoints, -RampWidth * 0.5f), false, PlanThinWidth);
        }

        private static Vector2[] Rectangle(float x0, float x1, float z0, float z1) =>
            new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) };

        private static Vector2[] Circle(Vector2 center, float radius)
        {
            var points = new Vector2[PlanCircleSegments];
            for (var i = 0; i < PlanCircleSegments; i++)
            {
                var angle = i * Mathf.PI * 2f / PlanCircleSegments;
                points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            return points;
        }

        // A sepia slab with its ink outline.
        private static void DrawSlab(PlanCanvas c, float x0, float x1, float z0, float z1, float width = PlanOutlineWidth)
        {
            var rect = Rectangle(x0, x1, z0, z1);
            c.Fill(rect, 1f);
            c.Stroke(rect, true, width);
        }

        // A deck: a slab with a thin line per board every 'step' metres along x.
        private static void DrawBoards(PlanCanvas c, float x0, float x1, float z0, float z1, float step)
        {
            DrawSlab(c, x0, x1, z0, z1);
            for (var x = x0 + step; x < x1 - 0.01f; x += step)
                c.Stroke(new[] { new Vector2(x, z0), new Vector2(x, z1) }, false, PlanHatchWidth);
        }

        // The orchard fence: dashed runs, open at the east and north gates.
        private static void DrawFence(PlanCanvas c)
        {
            const float gap = 2f;
            var runs = new[]
            {
                new[] { new Vector2(PlanFenceX0, PlanFenceZ0), new Vector2(PlanFenceX1, PlanFenceZ0) },
                new[] { new Vector2(PlanFenceX0, PlanFenceZ0), new Vector2(PlanFenceX0, PlanFenceZ1) },
                new[] { new Vector2(PlanFenceX0, PlanFenceZ1), new Vector2(PlanFenceGapNorth.x - gap, PlanFenceZ1) },
                new[] { new Vector2(PlanFenceGapNorth.x + gap, PlanFenceZ1), new Vector2(PlanFenceX1, PlanFenceZ1) },
                new[] { new Vector2(PlanFenceX1, PlanFenceZ0), new Vector2(PlanFenceX1, PlanFenceGapEast.y - gap) },
                new[] { new Vector2(PlanFenceX1, PlanFenceGapEast.y + gap), new Vector2(PlanFenceX1, PlanFenceZ1) },
            };
            foreach (var run in runs)
                c.Stroke(run, false, PlanThinWidth * 1.4f, 0.9f, 0.7f);
        }

        // One root wall: the stepped outline of its three prisms, with the gnome tunnel left open as a paper gap between two ink ends.
        private static void DrawRoot(PlanCanvas c, float side)
        {
            var points = new List<Vector2>();
            var lower = new List<Vector2>();
            for (var i = 0; i < RootStepHalfThickness.Length; i++)
            {
                var t = RootStepHalfThickness[i];
                points.Add(new Vector2(side * RootStepEdges[i], t));
                points.Add(new Vector2(side * RootStepEdges[i + 1], t));
                lower.Add(new Vector2(side * RootStepEdges[i], -t));
                lower.Add(new Vector2(side * RootStepEdges[i + 1], -t));
            }

            lower.Reverse();
            points.AddRange(lower);
            var outline = points.ToArray();
            c.Fill(outline, 1f);
            c.Stroke(outline, true, PlanOutlineWidth);

            var half = TunnelWidth * 0.5f + 0.05f;
            var tunnelX = side * TunnelX;
            var maxT = RootStepHalfThickness[0] + 0.2f;
            c.Fill(Rectangle(tunnelX - half, tunnelX + half, -maxT, maxT), 0f);
            c.Stroke(new[] { new Vector2(tunnelX - half, -RootStepHalfThickness[1]), new Vector2(tunnelX - half, RootStepHalfThickness[1]) }, false, PlanThinWidth);
            c.Stroke(new[] { new Vector2(tunnelX + half, -RootStepHalfThickness[1]), new Vector2(tunnelX + half, RootStepHalfThickness[1]) }, false, PlanThinWidth);
        }

        // The polyline moved sideways by 'offset' metres (positive = left of the walking direction), with averaged corner normals.
        private static Vector2[] OffsetLine(Vector2[] points, float offset)
        {
            var result = new Vector2[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                var before = points[Mathf.Max(0, i - 1)];
                var after = points[Mathf.Min(points.Length - 1, i + 1)];
                var direction = (after - before).normalized;
                result[i] = points[i] + new Vector2(-direction.y, direction.x) * offset;
            }

            return result;
        }

        // Plain 1024x945 texture: no mipmaps, clamped, bilinear, uncompressed, no NPOT scaling (a picture that must stay crisp).
        private static void ConfigureMinimapTexture()
        {
            var importer = AssetImporter.GetAtPath(MinimapPath) as TextureImporter;
            if (importer == null)
                return;

            if (importer.textureType == TextureImporterType.Default && !importer.mipmapEnabled && importer.wrapMode == TextureWrapMode.Clamp
                && importer.filterMode == FilterMode.Bilinear && importer.npotScale == TextureImporterNPOTScale.None
                && importer.textureCompression == TextureImporterCompression.Uncompressed && importer.maxTextureSize >= MinimapWidth
                && !importer.alphaIsTransparency && !importer.isReadable)
                return;

            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.alphaIsTransparency = false;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        // Creates (or reuses) the MinimapArea under the Abrazo root with the exact centre and size of the drawing, the two fixed
        // points (fire and cage) and the six sector names, all taken from the constants of this builder.
        private static void EnsureMinimapArea()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(MinimapPath);
            var existing = _root.Find(MinimapAreaName);
            var go = existing != null ? existing.gameObject : new GameObject(MinimapAreaName);
            if (existing == null)
                go.transform.SetParent(_root, false);

            if (!go.TryGetComponent<MinimapArea>(out var area))
                area = go.AddComponent<MinimapArea>();

            var serialized = new SerializedObject(area);
            serialized.FindProperty("_map").objectReferenceValue = texture;
            serialized.FindProperty("_worldCenter").vector2Value = Vector2.zero;
            serialized.FindProperty("_worldSize").vector2Value = new Vector2(MinimapWorldWidth, MinimapWorldHeight);

            Vector2[] positions = { FireplaceSpot, CageSpot };
            UiIcon[] icons = { UiIcon.Fire, UiIcon.Cage };
            var points = serialized.FindProperty("_points");
            points.arraySize = positions.Length;
            for (var i = 0; i < positions.Length; i++)
            {
                var element = points.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Position").vector2Value = positions[i];
                element.FindPropertyRelative("Icon").enumValueIndex = (int)icons[i];
            }

            var labels = serialized.FindProperty("_labels");
            labels.arraySize = PlanLabels.Length;
            for (var i = 0; i < PlanLabels.Length; i++)
            {
                var element = labels.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Text").stringValue = PlanLabels[i].text;
                element.FindPropertyRelative("Position").vector2Value = PlanLabels[i].position;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ================================================================== Canvas

        // Two coverage layers (sepia fill and ink) over a paper background, drawn in world metres on a wobbled copy of each pixel's
        // position, so every shape wobbles the same way. Row 0 is the south edge (z up), as the texture and the HUD expect.
        private sealed class PlanCanvas
        {
            private readonly int _width;
            private readonly int _height;
            private readonly float _worldWidth;
            private readonly float _worldHeight;
            private readonly float[] _fill;
            private readonly float[] _ink;
            private readonly float[] _qx;
            private readonly float[] _qz;
            private readonly float[] _widthScale;

            public PlanCanvas(int width, int height, float worldWidth, float worldHeight)
            {
                _width = width;
                _height = height;
                _worldWidth = worldWidth;
                _worldHeight = worldHeight;
                var count = width * height;
                _fill = new float[count];
                _ink = new float[count];
                _qx = new float[count];
                _qz = new float[count];
                _widthScale = new float[count];
                for (var y = 0; y < height; y++)
                {
                    var wz = (y + 0.5f) / height * worldHeight - worldHeight * 0.5f;
                    for (var x = 0; x < width; x++)
                    {
                        var wx = (x + 0.5f) / width * worldWidth - worldWidth * 0.5f;
                        var i = y * width + x;
                        _qx[i] = wx + (Mathf.PerlinNoise(wx * PlanWobbleScale + 11.3f, wz * PlanWobbleScale + 4.7f) - 0.5f) * PlanWobbleAmount;
                        _qz[i] = wz + (Mathf.PerlinNoise(wx * PlanWobbleScale + 37.9f, wz * PlanWobbleScale + 21.1f) - 0.5f) * PlanWobbleAmount;
                        _widthScale[i] = 0.75f + 0.5f * Mathf.PerlinNoise(wx * PlanWidthScale + 5.2f, wz * PlanWidthScale + 9.4f);
                    }
                }
            }

            // Paper everywhere, sepia where filled, ink on top.
            public Color32[] Compose(Color paper, Color sepia, Color ink)
            {
                var pixels = new Color32[_fill.Length];
                for (var i = 0; i < pixels.Length; i++)
                {
                    var c = Color.Lerp(paper, sepia, _fill[i]);
                    c = Color.Lerp(c, ink, _ink[i]);
                    c.a = 1f;
                    pixels[i] = c;
                }

                return pixels;
            }

            public void FillOutsideRect(float halfX, float halfZ)
            {
                for (var i = 0; i < _fill.Length; i++)
                {
                    if (Mathf.Abs(_qx[i]) > halfX || Mathf.Abs(_qz[i]) > halfZ)
                        _fill[i] = 1f;
                }
            }

            // Sets the fill of a polygon (1 = sepia, 0 = paper) and erases the ink under it.
            public void Fill(Vector2[] polygon, float value)
            {
                Region(polygon, 1.5f, i =>
                {
                    _fill[i] = value;
                    _ink[i] = 0f;
                });
            }

            // Diagonal fine lines inside a polygon (water).
            public void Hatch(Vector2[] polygon, float spacing, float lineWidth)
            {
                Region(polygon, 1.5f, i =>
                {
                    var d = Mathf.Repeat((_qx[i] + _qz[i]) * 0.70710678f, spacing);
                    var distance = Mathf.Min(d, spacing - d);
                    var half = lineWidth * 0.5f * _widthScale[i];
                    Ink(i, Mathf.Clamp01((half - distance) / PlanEdgeSoftness + 0.5f));
                });
            }

            // Ink line along a polyline (closed adds the last-to-first segment). A dash period makes it dotted.
            public void Stroke(Vector2[] points, bool closed, float width, float dashPeriod = 0f, float dashOn = 0.5f) =>
                Sweep(points, closed, width, dashPeriod, dashOn, false);

            // Sepia band of the given width along a polyline, erasing the ink under it.
            public void StrokeFilled(Vector2[] points, float width) => Sweep(points, false, width, 0f, 0f, true);

            private void Ink(int i, float alpha)
            {
                if (alpha > _ink[i])
                    _ink[i] = alpha;
            }

            private void Region(Vector2[] polygon, float margin, System.Action<int> apply)
            {
                Bounds(polygon, margin, out var x0, out var x1, out var y0, out var y1);
                for (var y = y0; y <= y1; y++)
                {
                    for (var x = x0; x <= x1; x++)
                    {
                        var i = y * _width + x;
                        if (Contains(polygon, _qx[i], _qz[i]))
                            apply(i);
                    }
                }
            }

            private void Sweep(Vector2[] points, bool closed, float width, float dashPeriod, float dashOn, bool toFill)
            {
                var segments = closed ? points.Length : points.Length - 1;
                var travelled = 0f;
                for (var s = 0; s < segments; s++)
                {
                    var a = points[s];
                    var b = points[(s + 1) % points.Length];
                    var ab = b - a;
                    var length = ab.magnitude;
                    if (length > 0.0001f)
                        SweepSegment(a, b, ab, length, travelled, width, dashPeriod, dashOn, toFill);

                    travelled += length;
                }
            }

            private void SweepSegment(Vector2 a, Vector2 b, Vector2 ab, float length, float travelled, float width, float dashPeriod,
                float dashOn, bool toFill)
            {
                var margin = width + 1.5f;
                Bounds(new[] { a, b }, margin, out var x0, out var x1, out var y0, out var y1);
                var squared = length * length;
                for (var y = y0; y <= y1; y++)
                {
                    for (var x = x0; x <= x1; x++)
                    {
                        var i = y * _width + x;
                        var q = new Vector2(_qx[i], _qz[i]);
                        var t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / squared);
                        var distance = Vector2.Distance(q, a + ab * t);
                        var half = width * 0.5f * (toFill ? 1f : _widthScale[i]);
                        if (distance > half + PlanEdgeSoftness)
                            continue;

                        if (toFill)
                        {
                            _fill[i] = 1f;
                            _ink[i] = 0f;
                            continue;
                        }

                        if (dashPeriod > 0f && Mathf.Repeat(travelled + t * length, dashPeriod) > dashPeriod * dashOn)
                            continue;

                        Ink(i, Mathf.Clamp01((half - distance) / PlanEdgeSoftness + 0.5f));
                    }
                }
            }

            // Pixel box of the world box of the points, grown by the margin (covers the wobble and the stroke width).
            private void Bounds(Vector2[] points, float margin, out int x0, out int x1, out int y0, out int y1)
            {
                var minX = float.MaxValue;
                var maxX = float.MinValue;
                var minZ = float.MaxValue;
                var maxZ = float.MinValue;
                for (var i = 0; i < points.Length; i++)
                {
                    minX = Mathf.Min(minX, points[i].x);
                    maxX = Mathf.Max(maxX, points[i].x);
                    minZ = Mathf.Min(minZ, points[i].y);
                    maxZ = Mathf.Max(maxZ, points[i].y);
                }

                x0 = Mathf.Max(0, Mathf.FloorToInt((minX - margin + _worldWidth * 0.5f) / _worldWidth * _width - 0.5f));
                x1 = Mathf.Min(_width - 1, Mathf.CeilToInt((maxX + margin + _worldWidth * 0.5f) / _worldWidth * _width - 0.5f));
                y0 = Mathf.Max(0, Mathf.FloorToInt((minZ - margin + _worldHeight * 0.5f) / _worldHeight * _height - 0.5f));
                y1 = Mathf.Min(_height - 1, Mathf.CeilToInt((maxZ + margin + _worldHeight * 0.5f) / _worldHeight * _height - 0.5f));
            }

            // Even-odd point in polygon test.
            private static bool Contains(Vector2[] polygon, float x, float z)
            {
                var inside = false;
                var j = polygon.Length - 1;
                for (var i = 0; i < polygon.Length; i++)
                {
                    var pi = polygon[i];
                    var pj = polygon[j];
                    if ((pi.y > z) != (pj.y > z) && x < (pj.x - pi.x) * (z - pi.y) / (pj.y - pi.y) + pi.x)
                        inside = !inside;

                    j = i;
                }

                return inside;
            }
        }
    }
}
