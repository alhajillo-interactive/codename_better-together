using System.Collections.Generic;
using UnityEngine;

namespace BetterTogether.Environment
{
    /// <summary>
    /// Draws the tall grass of a <see cref="TallGrassField"/> directly on the GPU: tufts are built once, grouped in chunks, and each
    /// frame only the chunks near and in front of the camera are drawn. No GameObjects, no shadows and nothing allocated per frame.
    /// Local presentation; every peer builds the same grass from the same grid.
    /// </summary>
    [RequireComponent(typeof(TallGrassField))]
    public class TallGrassRenderer : MonoBehaviour
    {
        private const int ChunkSize = 8;
        private const int MaxPerChunk = 1023;

        private sealed class Chunk
        {
            public Bounds Bounds;
            public Matrix4x4[][] Matrices;
            public int[] Counts;
        }

        [Tooltip("Tuft meshes.")]
        [SerializeField] private Mesh[] _meshes;
        [Tooltip("Instancing material for the tufts.")]
        [SerializeField] private Material _material;
        [Tooltip("Mesh-to-world pose of each tuft at scale 1, as the model is imported.")]
        [SerializeField] private Quaternion _modelRotation = Quaternion.identity;
        [Tooltip("Height of a tuft at scale 1 (m).")]
        [SerializeField, Min(0.01f)] private float _modelHeight = 0.58f;
        [Tooltip("Metres from the mesh origin to its bottom at scale 1.")]
        [SerializeField] private float _modelBottom;
        [Tooltip("Tufts per square metre of grass.")]
        [SerializeField, Min(1)] private int _tuftsPerCell = 11;
        [Tooltip("Width of a tuft compared with its height, so blades stay thin and the gnome can still see past them.")]
        [SerializeField, Min(0.05f)] private float _widthFactor = 0.45f;
        [Tooltip("Shortest and tallest tuft (m). A crouched gnome must fit under it.")]
        [SerializeField] private Vector2 _heightRange = new(0.65f, 0.9f);
        [Tooltip("Metres at which the grass stops being drawn.")]
        [SerializeField, Min(1f)] private float _drawDistance = 40f;

        private readonly List<Chunk> _chunks = new();
        private readonly Plane[] _planes = new Plane[6];
        private Camera _camera;

        private void Start()
        {
            var field = GetComponent<TallGrassField>();
            var terrain = Terrain.activeTerrain;
            var cells = new Dictionary<Vector2Int, List<Matrix4x4>[]>();
            field.ForEachCell(cell => AddCell(cell, terrain, cells));
            foreach (var pair in cells)
                _chunks.Add(MakeChunk(pair.Key, pair.Value));
        }

        private void AddCell(Vector2 cell, Terrain terrain, Dictionary<Vector2Int, List<Matrix4x4>[]> cells)
        {
            var key = new Vector2Int(Mathf.FloorToInt(cell.x / ChunkSize), Mathf.FloorToInt(cell.y / ChunkSize));
            if (!cells.TryGetValue(key, out var lists))
            {
                lists = new List<Matrix4x4>[_meshes.Length];
                for (var i = 0; i < lists.Length; i++)
                    lists[i] = new List<Matrix4x4>();
                cells.Add(key, lists);
            }

            // Hash of the cell, not Random: the same grass on every peer.
            var state = Random.state;
            Random.InitState(Mathf.FloorToInt(cell.x) * 7919 + Mathf.FloorToInt(cell.y) * 104729);
            for (var i = 0; i < _tuftsPerCell; i++)
            {
                var x = cell.x + Random.value;
                var z = cell.y + Random.value;
                var ground = terrain != null ? terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y : 0f;
                var scale = Random.Range(_heightRange.x, _heightRange.y) / _modelHeight;
                var position = new Vector3(x, ground - _modelBottom * scale, z);
                var yaw = Quaternion.Euler(0f, Random.value * 360f, 0f);
                var size = new Vector3(scale * _widthFactor, scale, scale * _widthFactor);
                lists[Random.Range(0, lists.Length)].Add(Matrix4x4.TRS(position, yaw, size) * Matrix4x4.Rotate(_modelRotation));
            }

            Random.state = state;
        }

        private Chunk MakeChunk(Vector2Int key, List<Matrix4x4>[] lists)
        {
            var center = new Vector3((key.x + 0.5f) * ChunkSize, 0.6f, (key.y + 0.5f) * ChunkSize);
            var chunk = new Chunk
            {
                Bounds = new Bounds(center, new Vector3(ChunkSize + 1f, 6f, ChunkSize + 1f)),
                Matrices = new Matrix4x4[lists.Length][],
                Counts = new int[lists.Length],
            };

            for (var i = 0; i < lists.Length; i++)
            {
                chunk.Counts[i] = Mathf.Min(lists[i].Count, MaxPerChunk);
                chunk.Matrices[i] = lists[i].ToArray();
            }

            return chunk;
        }

        private void LateUpdate()
        {
            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null || _material == null)
                return;

            GeometryUtility.CalculateFrustumPlanes(_camera, _planes);
            var origin = _camera.transform.position;
            var limit = _drawDistance * _drawDistance;
            var parameters = new RenderParams(_material) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = true };
            for (var c = 0; c < _chunks.Count; c++)
            {
                var chunk = _chunks[c];
                if (chunk.Bounds.SqrDistance(origin) > limit || !GeometryUtility.TestPlanesAABB(_planes, chunk.Bounds))
                    continue;

                parameters.worldBounds = chunk.Bounds;
                for (var m = 0; m < _meshes.Length; m++)
                {
                    if (chunk.Counts[m] > 0)
                        Graphics.RenderMeshInstanced(parameters, _meshes[m], 0, chunk.Matrices[m], chunk.Counts[m]);
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>Editor only: stores what the level builder measured from the model.</summary>
        public void Configure(Mesh[] meshes, Material material, Quaternion modelRotation, float modelHeight, float modelBottom)
        {
            _meshes = meshes;
            _material = material;
            _modelRotation = modelRotation;
            _modelHeight = modelHeight;
            _modelBottom = modelBottom;
        }
#endif
    }
}
