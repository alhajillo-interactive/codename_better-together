using BetterTogether.Audio;
using BetterTogether.Player;
using PrimeTween;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterTogether.Gaze
{
    /// <summary>
    /// Presentation of turning to stone, derived from <see cref="Petrifiable.IsHeldStone"/> (replicated state; nothing
    /// is networked here). Everyone sees stone dust when the sweep starts and a burst of fragments when it ends.
    /// Only the owner gets the desaturated, vignetted screen (a local Volume) and the FOV squeeze.
    /// A player that is already stone when it is first seen (late joiner) gets none of it.
    /// </summary>
    [RequireComponent(typeof(Petrifiable))]
    public class PetrifyFeel : MonoBehaviour
    {
        [Header("Owner screen")]
        [Tooltip("Local Volume with the petrified look (saturation down, grey vignette). Only the owner drives its weight.")]
        [SerializeField] private Volume _volume;
        [Tooltip("Seconds the Volume weight takes to go 0 to 1 (and back when freed).")]
        [SerializeField] private float _screenBlendTime = 0.35f;
        [Tooltip("Degrees the field of view narrows on freezing (eases back by itself).")]
        [SerializeField] private float _fovSqueeze = -3f;
        [SerializeField] private float _fovSqueezeTime = 0.8f;

        [Header("Everyone")]
        [Tooltip("Stone dust burst when the sweep starts.")]
        [SerializeField] private ParticleSystem _dust;
        [Tooltip("Fragment burst when the stone breaks.")]
        [SerializeField] private ParticleSystem _fragments;
        [Tooltip("Crack heard when a rider is frozen by the tower below (a petrified gnome already has its own sound).")]
        [SerializeField] private SoundCue _crackSfx;

        private Petrifiable _petrifiable;
        private bool _seen;
        private bool _lastHeld;
        private Tween _weightTween;

        private void Awake()
        {
            _petrifiable = GetComponent<Petrifiable>();
            if (_volume != null)
                _volume.weight = 0f;
        }

        private void OnDisable()
        {
            _weightTween.Stop();
            if (_volume != null)
                _volume.weight = 0f;
        }

        private void Update()
        {
            if (!_petrifiable.IsAvailable)
            {
                _seen = false;
                return;
            }

            var held = _petrifiable.IsHeldStone;
            var isOwner = Petrifiable.Local == _petrifiable;
            if (!_seen)
            {
                // First look after spawning: take the state as it is, with no effects.
                _seen = true;
                _lastHeld = held;
                if (isOwner && _volume != null)
                    _volume.weight = held ? 1f : 0f;
                return;
            }

            if (held == _lastHeld)
                return;

            _lastHeld = held;
            if (held)
                OnFrozen(isOwner);
            else
                OnReleased(isOwner);
        }

        private void OnFrozen(bool isOwner)
        {
            if (_dust != null)
                _dust.Play();
            if (!_petrifiable.IsStone)
                _crackSfx.PlayAt(transform.position);

            if (!isOwner)
                return;

            BlendScreen(1f);
            if (CameraPunch.Instance != null)
                CameraPunch.Instance.PunchFov(_fovSqueeze, _fovSqueezeTime);
        }

        private void OnReleased(bool isOwner)
        {
            if (_fragments != null)
                _fragments.Play();

            if (isOwner)
                BlendScreen(0f);
        }

        private void BlendScreen(float target)
        {
            if (_volume == null)
                return;

            _weightTween.Stop();
            _weightTween = Tween.Custom(this, _volume.weight, target, _screenBlendTime,
                static (feel, value) => feel._volume.weight = value);
        }
    }
}
