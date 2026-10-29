using BetterTogether.Player;
using UnityEngine;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// Presentation for an item carried by two gnomes (add it next to <see cref="Carryable"/>): a tension line between the
    /// two carry anchors that goes from white to blinking red as they near the distance that makes them drop it, and a
    /// small wobble of the drawn pose that grows with how differently the two carriers move.
    /// Reads only replicated state and writes nothing to the network, so late joiners and new masters see the same thing.
    /// </summary>
    [RequireComponent(typeof(Carryable), typeof(LineRenderer))]
    public class CarryTension : MonoBehaviour
    {
        [Header("Tension line")]
        [Tooltip("Fraction of the maximum carrier span at which the line starts turning red and blinking.")]
        [SerializeField, Range(0.1f, 1f)] private float _warnRatio = 0.75f;
        [SerializeField] private Color _safeColor = Color.white;
        [SerializeField] private Color _dangerColor = Color.red;
        [Tooltip("Blinks per second at the warning threshold; it speeds up towards the drop distance.")]
        [SerializeField, Min(0.1f)] private float _blinkHz = 3f;
        [Tooltip("Line width in meters.")]
        [SerializeField, Min(0.001f)] private float _lineWidth = 0.03f;

        [Header("Wobble")]
        [Tooltip("Speed difference between the carriers (m/s) that gives the full wobble.")]
        [SerializeField, Min(0.1f)] private float _fullDifferenceSpeed = 2f;
        [Tooltip("Roll around the axis between the carriers at full wobble, in degrees.")]
        [SerializeField, Min(0f)] private float _rollDegrees = 8f;
        [Tooltip("Vertical bob at full wobble, in meters.")]
        [SerializeField, Min(0f)] private float _bobMeters = 0.05f;
        [Tooltip("Wobble cycles per second.")]
        [SerializeField, Min(0.1f)] private float _wobbleHz = 1.6f;
        [Tooltip("How fast the measured speed difference follows the real one (1/s).")]
        [SerializeField, Min(0.1f)] private float _differenceSharpness = 8f;

        private Carryable _carryable;
        private LineRenderer _line;

        // Wobble sampling, presentation only. Reset whenever the previous frame did not draw this pair.
        private PlayerInteractor _sampleA;
        private PlayerInteractor _sampleB;
        private Vector3 _lastPositionA;
        private Vector3 _lastPositionB;
        private int _lastSampleFrame = -10;
        private float _difference;
        private float _phase;

        private void Awake()
        {
            _carryable = GetComponent<Carryable>();
            _line = GetComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.widthMultiplier = _lineWidth;
            _line.enabled = false;
        }

        // Anchors are already up to date here.
        private void LateUpdate()
        {
            if (_line == null)
                return;

            // TryGetPair reads networked state: it returns false before Spawned and after Despawned.
            if (!_carryable.TryGetPair(out var first, out var second, out var ratio))
            {
                _line.enabled = false;
                return;
            }

            var anchorA = first.CarryAnchor;
            var anchorB = second.CarryAnchor;
            if (anchorA == null || anchorB == null)
            {
                _line.enabled = false;
                return;
            }

            _line.enabled = true;
            _line.SetPosition(0, anchorA.position);
            _line.SetPosition(1, anchorB.position);

            var danger = Mathf.Clamp01(Mathf.InverseLerp(_warnRatio, 1f, ratio));
            var color = Color.Lerp(_safeColor, _dangerColor, danger);
            if (danger > 0f)
            {
                var blink = 0.5f + 0.5f * Mathf.Sin(Time.time * _blinkHz * (1f + danger * 2f) * 2f * Mathf.PI);
                color.a *= Mathf.Lerp(1f, blink, danger);
            }

            _line.startColor = color;
            _line.endColor = color;
        }

        /// <summary>Called by <see cref="Carryable"/> while it draws a pair pose: adds the wobble to the drawn pose.</summary>
        public void ApplySway(PlayerInteractor first, PlayerInteractor second, ref Vector3 position, ref Quaternion rotation)
        {
            var dt = Time.deltaTime;
            var a = first.transform.position;
            var b = second.transform.position;

            // Restart the sampling when the pair changed or the previous frame did not draw a pair.
            if (first != _sampleA || second != _sampleB || Time.frameCount - _lastSampleFrame != 1)
            {
                _sampleA = first;
                _sampleB = second;
                _difference = 0f;
            }
            else if (dt > 0f)
            {
                var velocityA = a - _lastPositionA;
                var velocityB = b - _lastPositionB;
                velocityA.y = 0f;
                velocityB.y = 0f;
                var target = (velocityA - velocityB).magnitude / dt;
                _difference = Mathf.Lerp(_difference, target, 1f - Mathf.Exp(-_differenceSharpness * dt));
                _phase = Mathf.Repeat(_phase + _wobbleHz * dt * 2f * Mathf.PI, 2f * Mathf.PI);
            }

            _lastPositionA = a;
            _lastPositionB = b;
            _lastSampleFrame = Time.frameCount;

            var amount = Mathf.Clamp01(_difference / _fullDifferenceSpeed);
            if (amount <= 0f)
                return;

            var across = second.CarryAnchor.position - first.CarryAnchor.position;
            if (across.sqrMagnitude > 1e-4f)
                rotation = Quaternion.AngleAxis(Mathf.Sin(_phase) * amount * _rollDegrees, across.normalized) * rotation;
            position += Vector3.up * (Mathf.Sin(_phase * 2f) * amount * _bobMeters);
        }
    }
}
