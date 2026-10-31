using BetterTogether.Fireplace;
using UnityEngine;

namespace BetterTogether.Environment
{
    /// <summary>
    /// A smoke column that rises while the campfire is lit, so everyone can see from afar that the camper succeeded.
    /// Presentation only, no network state: it reads the replicated <see cref="FireplaceManager.IsLit"/> every frame.
    /// <see cref="FireplaceManager.Instance"/> only exists between Spawned and Despawned, so the networked value is
    /// never read too early. A late joiner or a new master simply sees the current state; a player leaving changes nothing.
    /// </summary>
    public class FireSmokeBeacon : MonoBehaviour
    {
        [Tooltip("Smoke column that plays while the campfire is lit. Empty = the first ParticleSystem below this object.")]
        [SerializeField] private ParticleSystem _smoke;

        [Tooltip("C9.9: extra smoke particles emitted in one go when the fire is lit (a puff before the column builds up). 0 = none.")]
        [SerializeField, Min(0)] private int _puffCount = 14;

        private bool _playing;
        private bool _seen;

        private void Awake()
        {
            if (_smoke == null)
                _smoke = GetComponentInChildren<ParticleSystem>();
        }

        private void OnEnable()
        {
            _seen = false;
            Apply(false, true);
        }

        private void Update()
        {
            var fire = FireplaceManager.Instance;
            var ready = fire != null && fire.Object != null && fire.Object.IsValid;
            var lit = ready && fire.IsLit;
            if (!_seen)
            {
                // First look (late joiner, scene start): take the state as it is, without the puff.
                // The fire may not be spawned yet: wait for it, or a late joiner would take a burning fire for one just lit.
                _seen = ready;
                Apply(lit, true);
                return;
            }

            var starting = lit && !_playing;
            Apply(lit, false);
            if (starting && _smoke != null && _puffCount > 0)
                _smoke.Emit(_puffCount);
        }

        private void Apply(bool lit, bool force)
        {
            if (_smoke == null || (!force && lit == _playing))
                return;

            _playing = lit;
            if (lit)
                _smoke.Play();
            else
                _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
