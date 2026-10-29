using System.Collections.Generic;
using BetterTogether.Audio;
using UnityEngine;

namespace BetterTogether.Environment
{
    /// <summary>
    /// A real garden-gnome ornament placed in the level so the camper can't tell a disguised gnome (C4.1) from scenery.
    /// It copies the gnome model frozen in the idle pose with the same painted-ceramic tint that GardenDisguise applies.
    /// Scene prop, not networked: every client builds the same pose and tint locally. A camper's kick (seen by everyone
    /// through the kick animation RPC) only makes it wobble and clink, the same on every client, and never reveals anything.
    /// </summary>
    public class GardenGnomeDecoy : MonoBehaviour
    {
        private static readonly List<GardenGnomeDecoy> All = new();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int WatercolorColorId = Shader.PropertyToID("_WatercolorBaseColor");

        [Header("Look")]
        [Tooltip("Visual child with the gnome model and its Animator. Empty = the first Animator below this object.")]
        [SerializeField] private Transform _visual;
        [Tooltip("Must match GardenDisguise's ceramic colour on the Gnome prefab, or the decoy gives itself away.")]
        [SerializeField] private Color _ceramicColor = new(0.85f, 0.45f, 0.35f);
        [Tooltip("Normalized time of the idle clip where the pose is frozen. Negative = picked from the position, so decoys differ but every client agrees.")]
        [SerializeField] private float _poseTime = -1f;

        [Header("Kick")]
        [Tooltip("Peak tilt in degrees when the camper kicks it.")]
        [SerializeField] private float _wobbleDegrees = 14f;
        [Tooltip("Seconds the wobble lasts.")]
        [SerializeField, Min(0.05f)] private float _wobbleTime = 0.5f;
        [Tooltip("Wobble oscillations per second.")]
        [SerializeField] private float _wobbleHz = 6f;
        [Tooltip("Ceramic clink heard by everyone when it is kicked.")]
        [SerializeField] private SoundCue _clinkSfx;

        private MaterialPropertyBlock _block;
        private Quaternion _visualRest;
        private Vector3 _wobbleAxis;
        private float _wobbleLeft;

        private void Awake()
        {
            if (_visual == null)
            {
                var animator = GetComponentInChildren<Animator>();
                _visual = animator != null ? animator.transform : transform;
            }

            _visualRest = _visual.localRotation;
            _block = new MaterialPropertyBlock();
        }

        private void OnEnable() => All.Add(this);

        private void OnDisable()
        {
            All.Remove(this);
            if (_visual != null)
                _visual.localRotation = _visualRest;
            _wobbleLeft = 0f;
        }

        private void Start()
        {
            ApplyCeramicTint();
            FreezePose();
        }

        // Same result as Petrifiable with the disguise overlay at full amount: every tintable material shows the ceramic colour.
        private void ApplyCeramicTint()
        {
            foreach (var renderer in _visual.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (var m = 0; m < materials.Length; m++)
                {
                    var material = materials[m];
                    if (material == null)
                        continue;

                    var id = material.HasProperty(BaseColorId) ? BaseColorId : material.HasProperty(WatercolorColorId) ? WatercolorColorId : 0;
                    if (id == 0)
                        continue;

                    _block.Clear();
                    _block.SetColor(id, _ceramicColor);
                    renderer.SetPropertyBlock(_block, m);
                }
            }
        }

        // A disguised gnome freezes its Animator in the idle pose; do the same once and stop animating.
        private void FreezePose()
        {
            var animator = _visual.GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
                return;

            var p = transform.position;
            var time = _poseTime >= 0f ? _poseTime : Mathf.Repeat(p.x * 0.37f + p.z * 0.61f, 1f);
            animator.applyRootMotion = false;
            animator.Play(0, 0, time);
            animator.Update(0f);
            animator.speed = 0f;
        }

        /// <summary>
        /// Called on every client when a camper's kick animation plays: decoys inside that kick's reach wobble and clink.
        /// Uses the same cone as the kick itself, so it matches what the camper aimed at.
        /// </summary>
        public static void NotifyKick(Vector3 origin, Vector3 forward, float range, float halfAngle)
        {
            forward.y = 0f;
            foreach (var decoy in All)
            {
                var toDecoy = decoy.transform.position - origin;
                toDecoy.y = 0f;
                if (toDecoy.magnitude <= range && Vector3.Angle(forward, toDecoy) <= halfAngle)
                    decoy.Wobble(toDecoy);
            }
        }

        private void Wobble(Vector3 push)
        {
            var axis = Vector3.Cross(Vector3.up, push.sqrMagnitude > 0.0001f ? push.normalized : transform.forward);
            _wobbleAxis = transform.InverseTransformDirection(axis);
            _wobbleLeft = _wobbleTime;
            _clinkSfx.PlayAt(transform.position);
        }

        private void Update()
        {
            if (_wobbleLeft <= 0f)
                return;

            _wobbleLeft -= Time.deltaTime;
            if (_wobbleLeft <= 0f)
            {
                _visual.localRotation = _visualRest;
                return;
            }

            var t = 1f - _wobbleLeft / _wobbleTime;
            var angle = _wobbleDegrees * (1f - t) * Mathf.Sin(t * _wobbleTime * _wobbleHz * 2f * Mathf.PI);
            _visual.localRotation = Quaternion.AngleAxis(angle, _wobbleAxis) * _visualRest;
        }
    }
}
