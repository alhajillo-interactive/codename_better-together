using BetterTogether.Audio;
using BetterTogether.Player;
using UnityEngine;

namespace BetterTogether.Match
{
    /// <summary>
    /// The exploding gnome. Once armed, the camper's own client notices walking into it, throws its character and tells the master.
    /// The master then decides with <see cref="MatchManager.CanBomb"/>. Presentation and the push stay with the camper's owner.
    /// </summary>
    public class TravesuraBomb : MonoBehaviour
    {
        [Tooltip("Metres at which the camper sets it off.")]
        [SerializeField, Min(0.1f)] private float _triggerRadius = 1.2f;
        [Tooltip("Upward speed given to the camper (m/s).")]
        [SerializeField, Min(0f)] private float _launchUp = 9f;
        [Tooltip("Horizontal speed given to the camper (m/s).")]
        [SerializeField, Min(0f)] private float _launchBack = 5f;
        [Tooltip("Seconds the camper cannot act.")]
        [SerializeField, Min(0f)] private float _stunSeconds = 1.5f;
        [SerializeField] private SoundCue _boomSfx;

        private bool _wasArmed;
        private bool _fired;

        private void Update()
        {
            var match = MatchManager.Instance;
            var armed = match != null && match.BombIsArmed;
            if (_wasArmed && !armed && match != null && match.TravesurasRunning)
                _boomSfx.PlayAt(transform.position);

            _wasArmed = armed;
            if (!armed)
            {
                _fired = false;
                return;
            }

            var local = PlayerCharacter.Local;
            if (local == null || local.Role != PlayerRole.Camper)
                return;

            var offset = local.transform.position - transform.position;
            if (offset.sqrMagnitude > _triggerRadius * _triggerRadius)
            {
                _fired = false; // A report the master refused can be sent again.
                return;
            }

            if (_fired)
                return;

            _fired = true;
            if (local.TryGetComponent<PlayerMovement>(out var movement))
            {
                var away = new Vector3(offset.x, 0f, offset.z);
                away = away.sqrMagnitude > 0.001f ? away.normalized : Vector3.back;
                movement.ApplyKnockback(away * _launchBack + Vector3.up * _launchUp, _stunSeconds);
            }

            match.RequestBomb(local, transform.position);
        }
    }
}
