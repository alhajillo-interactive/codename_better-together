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

        private bool _playing;

        private void Awake()
        {
            if (_smoke == null)
                _smoke = GetComponentInChildren<ParticleSystem>();
        }

        private void OnEnable() => Apply(false, true);

        private void Update()
        {
            var fire = FireplaceManager.Instance;
            var lit = fire != null && fire.Object != null && fire.Object.IsValid && fire.IsLit;
            Apply(lit, false);
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
