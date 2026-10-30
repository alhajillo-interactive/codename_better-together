using BetterTogether.Interaction;
using BetterTogether.Player;
using UnityEngine;

namespace BetterTogether.Gaze
{
    /// <summary>
    /// Local-only visuals for a camper's gaze: a faint cone and its ground footprint, shown to gnomes so they can tell
    /// where the camper is looking, and a diamond floating above the locked gnome, shown to everyone but that gnome.
    /// Everything is generated in code and driven by the replicated state of <see cref="CamperGaze"/>; nothing is networked.
    /// </summary>
    public class GazeConeVisual : MonoBehaviour
    {
        private const int Segments = 24;
        private const int BodyRings = 9;
        private const int RingCount = BodyRings + 2;
        private const int FootprintSegments = 16;
        private const float ApexOffset = 0.1f;
        private const float BodyLengthFraction = 0.95f;

        [SerializeField] private CamperGaze _gaze;
        [Tooltip("Gaze Cone shader (transparent, vertex colours). Referenced here so builds include it.")]
        [SerializeField] private Shader _shader;

        [Header("Colours")]
        [Tooltip("Faint cone shown while the camper is not focusing, so gnomes can tell where it looks.")]
        [SerializeField] private Color _dormantColor = new(0.55f, 0.45f, 1f, 0.03f);
        [Tooltip("Show the dormant cone; off hides the cone until the camper focuses.")]
        [SerializeField] private bool _showDormant = true;
        [SerializeField] private Color _searchColor = new(0.55f, 0.45f, 1f, 0.10f);
        [Tooltip("Alpha swing of the 1 Hz breathing while searching.")]
        [SerializeField] private float _breathingAmplitude = 0.02f;
        [SerializeField] private Color _lockedColor = new(1f, 0.35f, 0.25f, 0.22f);
        [SerializeField] private float _colorTransition = 0.15f;
        [Tooltip("Alpha multiplier while the night ramp is running.")]
        [SerializeField] private float _nightAlphaScale = 1.3f;

        [Header("Ground footprint")]
        [SerializeField] private LayerMask _groundMask = ~0;
        [SerializeField] private float _groundOffset = 0.02f;
        [SerializeField] private float _footprintAlphaScale = 1.5f;

        [Header("Lock marker")]
        [SerializeField] private Color _markerColor = new(1f, 0.2f, 0.15f, 0.95f);
        [SerializeField] private float _markerSize = 0.15f;
        [Tooltip("Metres between the head of the locked gnome and the marker.")]
        [SerializeField] private float _markerHeight = 0.25f;

        [Header("Lens glint (seen by gnomes)")]
        [Tooltip("Show a glint on the camper's lens to gnomes while the camper focuses.")]
        [SerializeField] private bool _showGlint = true;
        [Tooltip("Base glint size in metres.")]
        [SerializeField] private float _glintSize = 0.25f;
        [Tooltip("Size multiplier while a gnome is locked.")]
        [SerializeField] private float _glintLockedScale = 1.6f;
        [Tooltip("Pulse amplitude (fraction of size) at 1 Hz.")]
        [SerializeField, Range(0f, 0.5f)] private float _glintPulse = 0.2f;
        [SerializeField] private Color _glintColor = new(1f, 0.96f, 0.85f, 0.9f);
        [Tooltip("Glint colour while a gnome is locked or on the shot flash (ember #F6C343).")]
        [SerializeField] private Color _glintLockedColor = new(0.965f, 0.765f, 0.263f, 1f);
        [Tooltip("Metres from the eye towards the look direction where the lens sits.")]
        [SerializeField] private float _lensOffset = 0.15f;
        [Tooltip("Seconds the shot flash lasts.")]
        [SerializeField] private float _flashTime = 0.08f;
        [Tooltip("Glint size multiplier during the shot flash.")]
        [SerializeField] private float _flashScale = 3f;
        [Tooltip("Intensity of the shot flash light (cached point light, off by default).")]
        [SerializeField] private float _flashLightIntensity = 6f;
        [Tooltip("Range in metres of the shot flash light.")]
        [SerializeField] private float _flashLightRange = 6f;
        [SerializeField] private Color _flashLightColor = new(1f, 0.93f, 0.8f);

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly RaycastHit[] _hits = new RaycastHit[16];

        private readonly Vector3[] _vertices = new Vector3[RingCount * Segments];
        private readonly Color[] _colors = new Color[RingCount * Segments];

        private Mesh _coneMesh;
        private Mesh _footprintMesh;
        private Mesh _markerMesh;
        private Material _coneMaterial;
        private Material _footprintMaterial;
        private Material _markerMaterial;
        private Renderer _coneRenderer;
        private Renderer _footprintRenderer;
        private Renderer _markerRenderer;
        private float _builtRange = -1f;
        private float _builtAngle = -1f;
        private float _lockBlend;
        private float _focusBlend;
        private Mesh _glintMesh;
        private Material _glintMaterial;
        private Renderer _glintRenderer;
        private Light _flashLight;
        private Camera _viewCamera;
        private byte _prevShotSeq;
        private bool _glintReady;
        private float _flashUntil = -1f;

        private void Awake()
        {
            if (_gaze == null)
                _gaze = GetComponent<CamperGaze>();

            if (_shader == null)
                _shader = Shader.Find("BetterTogether/Gaze Cone");

            if (_shader == null)
            {
                Debug.LogError("GazeConeVisual: the Gaze Cone shader was not found.", this);
                enabled = false;
                return;
            }

            _coneMesh = CreateConeMesh();
            _footprintMesh = CreateFootprintMesh();
            _markerMesh = CreateMarkerMesh();
            _coneMaterial = CreateMaterial(_searchColor);
            _footprintMaterial = CreateMaterial(_searchColor);
            _markerMaterial = CreateMaterial(_markerColor);
            _coneRenderer = CreatePart("Gaze Cone", _coneMesh, _coneMaterial);
            _footprintRenderer = CreatePart("Gaze Footprint", _footprintMesh, _footprintMaterial);
            _markerRenderer = CreatePart("Gaze Lock Marker", _markerMesh, _markerMaterial);

            _glintMesh = CreateGlintMesh();
            _glintMaterial = CreateMaterial(_glintColor);
            _glintRenderer = CreatePart("Gaze Lens Glint", _glintMesh, _glintMaterial);
            var lightObject = new GameObject("Gaze Flash Light") { hideFlags = HideFlags.HideAndDontSave };
            _flashLight = lightObject.AddComponent<Light>();
            _flashLight.type = LightType.Point;
            _flashLight.shadows = LightShadows.None;
            _flashLight.color = _flashLightColor;
            _flashLight.range = _flashLightRange;
            _flashLight.intensity = _flashLightIntensity;
            _flashLight.enabled = false;
        }

        private void OnDestroy()
        {
            DestroyPart(_coneRenderer);
            DestroyPart(_footprintRenderer);
            DestroyPart(_markerRenderer);
            DestroyPart(_glintRenderer);
            if (_flashLight != null)
                Destroy(_flashLight.gameObject);
            DestroyAsset(_glintMesh);
            DestroyAsset(_glintMaterial);
            DestroyAsset(_coneMesh);
            DestroyAsset(_footprintMesh);
            DestroyAsset(_markerMesh);
            DestroyAsset(_coneMaterial);
            DestroyAsset(_footprintMaterial);
            DestroyAsset(_markerMaterial);
        }

        private void LateUpdate()
        {
            var on = _gaze != null && _gaze.IsGazeOn;
            var focusing = on && _gaze.Focusing;
            UpdateMarker(focusing ? _gaze.LockedTarget : null);

            var isGnomeView = on && PlayerCharacter.Local != null && PlayerCharacter.Local.Role == PlayerRole.Gnome;
            UpdateGlint(on, focusing, isGnomeView);
            var color = isGnomeView ? ComputeColor(focusing) : default;
            var showCone = isGnomeView && color.a > 0.001f;
            _coneRenderer.enabled = showCone;
            if (!showCone)
            {
                _footprintRenderer.enabled = false;
                return;
            }

            RebuildIfNeeded();
            _coneMaterial.SetColor(ColorId, color);
            _footprintMaterial.SetColor(ColorId, color);
            _footprintMaterial.SetFloat(AlphaId, _footprintAlphaScale);

            var eye = _gaze.EyePosition;
            var look = _gaze.LookDirection;
            _coneRenderer.transform.SetPositionAndRotation(eye + look * ApexOffset, Quaternion.LookRotation(look));
            UpdateFootprint(eye, look);
        }

        // Lens glint + shot flash, local only. Gnomes see it (never the camper). The first frame after the gaze comes on
        // only takes the shot sequence, so a late joiner gets no old flash.
        private void UpdateGlint(bool on, bool focusing, bool gnomeView)
        {
            if (!on)
            {
                _glintReady = false;
                _glintRenderer.enabled = false;
                _flashLight.enabled = false;
                return;
            }

            var seq = _gaze.ShotSeq;
            if (_glintReady && seq != _prevShotSeq)
                _flashUntil = Time.time + _flashTime;

            _prevShotSeq = seq;
            _glintReady = true;

            var flashing = Time.time < _flashUntil;
            var eye = _gaze.EyePosition;
            var look = _gaze.LookDirection;
            var lens = eye + look * _lensOffset;

            // The flash light lights the surroundings for gnomes; the camper's own screen flash is C8.2's job.
            _flashLight.enabled = flashing && gnomeView;
            if (_flashLight.enabled)
                _flashLight.transform.position = lens;

            var show = _showGlint && gnomeView && (focusing || flashing);
            _glintRenderer.enabled = show;
            if (!show)
                return;

            if (_viewCamera == null)
                _viewCamera = Camera.main;

            var locked = focusing && _gaze.LockedTarget != null;
            var size = _glintSize * (locked ? _glintLockedScale : 1f)
                * (1f + Mathf.Sin(Time.time * Mathf.PI * 2f) * _glintPulse);
            if (flashing)
                size = _glintSize * _flashScale;

            _glintMaterial.SetColor(ColorId, locked || flashing ? _glintLockedColor : _glintColor);
            var glint = _glintRenderer.transform;
            glint.position = lens;
            glint.rotation = _viewCamera != null ? _viewCamera.transform.rotation : Quaternion.LookRotation(-look);
            glint.localScale = Vector3.one * size;
        }

        // Dormant when the camper isn't focusing, search colour while focusing, locked colour on a target.
        private Color ComputeColor(bool focusing)
        {
            _focusBlend = MoveBlend(_focusBlend, focusing ? 1f : 0f);
            _lockBlend = MoveBlend(_lockBlend, focusing && _gaze.LockedTarget != null ? 1f : 0f);

            var dormant = _dormantColor;
            if (!_showDormant)
                dormant.a = 0f;

            var searching = _searchColor;
            searching.a += Mathf.Sin(Time.time * Mathf.PI * 2f) * _breathingAmplitude;
            var active = Color.Lerp(searching, _lockedColor, _lockBlend);
            var color = Color.Lerp(dormant, active, _focusBlend);
            if (_gaze.NightFactor > 0f)
                color.a *= _nightAlphaScale;

            return color;
        }

        private float MoveBlend(float current, float wanted)
            => _colorTransition > 0f ? Mathf.MoveTowards(current, wanted, Time.deltaTime / _colorTransition) : wanted;

        private void UpdateFootprint(Vector3 eye, Vector3 look)
        {
            var range = _gaze.Range;
            var count = Physics.RaycastNonAlloc(eye, look, _hits, range, _groundMask, QueryTriggerInteraction.Ignore);
            var found = false;
            var best = new RaycastHit();
            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (found && hit.distance >= best.distance)
                    continue;

                if (hit.transform.IsChildOf(_gaze.transform) || hit.collider.GetComponentInParent<PlayerCharacter>() != null
                    || hit.collider.GetComponentInParent<Carryable>() != null)
                    continue;

                best = hit;
                found = true;
            }

            _footprintRenderer.enabled = found && best.normal.y > 0.5f;
            if (!_footprintRenderer.enabled)
                return;

            var radius = Mathf.Tan(_gaze.HalfAngle * Mathf.Deg2Rad) * best.distance;
            var footprint = _footprintRenderer.transform;
            footprint.SetPositionAndRotation(best.point + Vector3.up * _groundOffset, Quaternion.identity);
            footprint.localScale = new Vector3(radius, 1f, radius);
        }

        private void UpdateMarker(Petrifiable target)
        {
            var show = target != null && target != Petrifiable.Local;
            _markerRenderer.enabled = show;
            if (!show)
                return;

            var position = target.HeadPosition + Vector3.up * _markerHeight;
            _markerRenderer.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, Time.time * 120f, 0f));
        }

        private void RebuildIfNeeded()
        {
            var range = _gaze.Range;
            var angle = _gaze.HalfAngle;
            if (Mathf.Abs(range - _builtRange) < 0.01f && Mathf.Approximately(angle, _builtAngle))
                return;

            _builtRange = range;
            _builtAngle = angle;
            FillCone(range, angle);
            _coneMesh.SetVertices(_vertices);
            _coneMesh.SetColors(_colors);
            var radius = Mathf.Tan(angle * Mathf.Deg2Rad) * range;
            _coneMesh.bounds = new Bounds(new Vector3(0f, 0f, range * 0.5f), new Vector3(radius * 2f, radius * 2f, range));
        }

        // Rings run from just in front of the eye to the range; the last two form a brighter rim line at the far end.
        private void FillCone(float range, float halfAngle)
        {
            var tan = Mathf.Tan(halfAngle * Mathf.Deg2Rad);
            var bodyEnd = range * BodyLengthFraction;
            var rimAlpha = Mathf.Min(1f, 2f * Fade(0.5f));
            for (var ring = 0; ring < RingCount; ring++)
            {
                float distance;
                float alpha;
                if (ring < BodyRings)
                {
                    distance = Mathf.Lerp(ApexOffset, bodyEnd, ring / (float)(BodyRings - 1));
                    alpha = Fade(distance / range);
                }
                else
                {
                    distance = ring == BodyRings ? range * 0.97f : range;
                    alpha = rimAlpha;
                }

                var radius = tan * distance;
                for (var segment = 0; segment < Segments; segment++)
                {
                    var angle = segment * Mathf.PI * 2f / Segments;
                    var index = ring * Segments + segment;
                    _vertices[index] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, distance - ApexOffset);
                    _colors[index] = new Color(1f, 1f, 1f, alpha);
                }
            }
        }

        private static float Fade(float fraction) => Mathf.Pow(Mathf.Clamp01(1f - fraction), 1.5f);

        private Mesh CreateConeMesh()
        {
            var mesh = new Mesh { name = "Gaze Cone", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            FillCone(1f, 22f);
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);

            var triangles = new int[(RingCount - 1) * Segments * 6];
            var cursor = 0;
            for (var ring = 0; ring < RingCount - 1; ring++)
            {
                for (var segment = 0; segment < Segments; segment++)
                {
                    var next = (segment + 1) % Segments;
                    var a = ring * Segments + segment;
                    var b = ring * Segments + next;
                    var c = (ring + 1) * Segments + segment;
                    var d = (ring + 1) * Segments + next;
                    triangles[cursor++] = a;
                    triangles[cursor++] = c;
                    triangles[cursor++] = b;
                    triangles[cursor++] = b;
                    triangles[cursor++] = c;
                    triangles[cursor++] = d;
                }
            }

            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        // Unit disc on the XZ plane: centre opaque, soft falloff to a transparent edge.
        private static Mesh CreateFootprintMesh()
        {
            var vertices = new Vector3[1 + FootprintSegments * 2];
            var colors = new Color[vertices.Length];
            vertices[0] = Vector3.zero;
            colors[0] = Color.white;
            for (var segment = 0; segment < FootprintSegments; segment++)
            {
                var angle = segment * Mathf.PI * 2f / FootprintSegments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[1 + segment] = direction * 0.5f;
                colors[1 + segment] = new Color(1f, 1f, 1f, 0.6f);
                vertices[1 + FootprintSegments + segment] = direction;
                colors[1 + FootprintSegments + segment] = new Color(1f, 1f, 1f, 0f);
            }

            var triangles = new int[FootprintSegments * 9];
            var cursor = 0;
            for (var segment = 0; segment < FootprintSegments; segment++)
            {
                var next = (segment + 1) % FootprintSegments;
                var inner = 1 + segment;
                var innerNext = 1 + next;
                var outer = 1 + FootprintSegments + segment;
                var outerNext = 1 + FootprintSegments + next;
                triangles[cursor++] = 0;
                triangles[cursor++] = inner;
                triangles[cursor++] = innerNext;
                triangles[cursor++] = inner;
                triangles[cursor++] = outer;
                triangles[cursor++] = outerNext;
                triangles[cursor++] = inner;
                triangles[cursor++] = outerNext;
                triangles[cursor++] = innerNext;
            }

            var mesh = new Mesh { name = "Gaze Footprint", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Unit disc facing the camera (billboard): bright centre, soft falloff to a transparent edge.
        private static Mesh CreateGlintMesh()
        {
            var vertices = new Vector3[1 + FootprintSegments * 2];
            var colors = new Color[vertices.Length];
            vertices[0] = Vector3.zero;
            colors[0] = Color.white;
            for (var segment = 0; segment < FootprintSegments; segment++)
            {
                var angle = segment * Mathf.PI * 2f / FootprintSegments;
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                vertices[1 + segment] = direction * 0.2f;
                colors[1 + segment] = new Color(1f, 1f, 1f, 0.5f);
                vertices[1 + FootprintSegments + segment] = direction * 0.5f;
                colors[1 + FootprintSegments + segment] = new Color(1f, 1f, 1f, 0f);
            }

            var triangles = new int[FootprintSegments * 9];
            var cursor = 0;
            for (var segment = 0; segment < FootprintSegments; segment++)
            {
                var next = (segment + 1) % FootprintSegments;
                var inner = 1 + segment;
                var innerNext = 1 + next;
                var outer = 1 + FootprintSegments + segment;
                var outerNext = 1 + FootprintSegments + next;
                triangles[cursor++] = 0;
                triangles[cursor++] = inner;
                triangles[cursor++] = innerNext;
                triangles[cursor++] = inner;
                triangles[cursor++] = outer;
                triangles[cursor++] = outerNext;
                triangles[cursor++] = inner;
                triangles[cursor++] = outerNext;
                triangles[cursor++] = innerNext;
            }

            var mesh = new Mesh { name = "Gaze Glint", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Octahedron: a small diamond.
        private Mesh CreateMarkerMesh()
        {
            var half = _markerSize * 0.5f;
            var tall = _markerSize * 0.8f;
            var vertices = new[]
            {
                new Vector3(0f, tall, 0f), new Vector3(0f, -tall, 0f),
                new Vector3(half, 0f, 0f), new Vector3(-half, 0f, 0f),
                new Vector3(0f, 0f, half), new Vector3(0f, 0f, -half),
            };
            var colors = new[] { Color.white, Color.white, Color.white, Color.white, Color.white, Color.white };
            var triangles = new[]
            {
                0, 4, 2, 0, 3, 4, 0, 5, 3, 0, 2, 5,
                1, 2, 4, 1, 4, 3, 1, 3, 5, 1, 5, 2,
            };

            var mesh = new Mesh { name = "Gaze Lock Marker", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private Material CreateMaterial(Color color)
        {
            var material = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000 };
            material.SetColor(ColorId, color);
            return material;
        }

        private static Renderer CreatePart(string partName, Mesh mesh, Material material)
        {
            var part = new GameObject(partName) { hideFlags = HideFlags.HideAndDontSave };
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = part.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.enabled = false;
            return meshRenderer;
        }

        private static void DestroyPart(Renderer part)
        {
            if (part != null)
                Destroy(part.gameObject);
        }

        private static void DestroyAsset(Object asset)
        {
            if (asset != null)
                Destroy(asset);
        }
    }
}
