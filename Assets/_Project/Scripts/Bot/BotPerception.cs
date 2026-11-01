using System.Collections.Generic;
using BetterTogether.Gaze;
using BetterTogether.Player;
using UnityEngine;

namespace BetterTogether.Bot
{
    /// <summary>How sure the bot is about one gnome.</summary>
    public enum SuspicionLevel : byte
    {
        Unaware,
        Curious,
        Suspicious,
        Confirmed,
        Searching,
    }

    /// <summary>Noises any system can report on the master, so the bot hears the camp instead of reading the game state.</summary>
    public static class BotEars
    {
        /// <summary>Last noise: position, how far it carries and when it happened.</summary>
        public static Vector3 Position { get; private set; }
        public static float Radius { get; private set; }
        public static float Time { get; private set; } = float.NegativeInfinity;

        public static void Report(Vector3 position, float radius)
        {
            Position = position;
            Radius = radius;
            Time = UnityEngine.Time.time;
        }
    }

    /// <summary>
    /// The bot's senses and memory, on the master only. Each gnome has a suspicion that grows while the bot really sees it, so the
    /// gnomes get a fair moment to react. A gnome posing as an ornament looks like one: the bot only doubts it when it is standing
    /// where no ornament stood at the start of the match. Nothing here reads whether a statue is fake or explosive.
    /// </summary>
    public class BotPerception
    {
        /// <summary>What the bot remembers about a gnome.</summary>
        public sealed class Contact
        {
            public Petrifiable Gnome;
            public float Suspicion;
            public SuspicionLevel Level;
            public Vector3 LastSeen;
            public float LastSeenTime = float.NegativeInfinity;
            public float CuriousSince;
            public bool Visible;
        }

        private const int MaxOrnaments = 64;
        private const float OrnamentMatchDistance = 0.8f;

        private readonly Settings _settings;
        private readonly Transform _body;
        private readonly Transform _eye;
        private readonly LayerMask _sightMask;
        private readonly List<Contact> _contacts = new(4);
        private readonly List<Vector3> _knownOrnaments = new(MaxOrnaments);
        private readonly List<Vector3> _checkedStatues = new(8);

        [System.Serializable]
        public class Settings
        {
            [Tooltip("Metres of the long, narrow view.")]
            public float FarRange = 12f;
            [Tooltip("Half angle in degrees of the long view.")]
            public float FarAngle = 50f;
            [Tooltip("Metres of the close, wide view.")]
            public float NearRange = 5f;
            [Tooltip("Half angle in degrees of the close view.")]
            public float NearAngle = 110f;
            [Tooltip("Seconds of plain sight, up close, from nothing to certain.")]
            public float SecondsToConfirm = 1.2f;
            [Tooltip("Suspicion lost per second out of sight.")]
            public float DecayPerSecond = 0.12f;
            [Tooltip("Shortest time in Curious before it can be Confirmed: the gnomes' chance to react.")]
            public float MinCuriousSeconds = 0.6f;
            [Tooltip("Random change per check, so the bot is not a machine.")]
            public float Noise = 0.05f;
            [Tooltip("Metres at which a gnome crouched in tall grass can be seen.")]
            public float GrassSightDistance = 2f;
            [Tooltip("Metres at which the bot looks closely at an ornament that was not there before.")]
            public float StatueCheckDistance = 7f;
        }

        public IReadOnlyList<Contact> Contacts => _contacts;

        public BotPerception(Settings settings, Transform body, Transform eye, LayerMask sightMask)
        {
            _settings = settings;
            _body = body;
            _eye = eye;
            _sightMask = sightMask;
        }

        /// <summary>Learns where the level's ornaments stand: what a person remembers after a look around.</summary>
        public void Remember(IReadOnlyList<Vector3> ornaments)
        {
            _knownOrnaments.Clear();
            for (var i = 0; i < ornaments.Count && i < MaxOrnaments; i++)
                _knownOrnaments.Add(ornaments[i]);

            _checkedStatues.Clear();
            _contacts.Clear();
        }

        public void Tick(float deltaTime)
        {
            SyncContacts();
            for (var i = 0; i < _contacts.Count; i++)
                TickContact(_contacts[i], deltaTime);
        }

        /// <summary>The most certain contact at or above <paramref name="level"/>, or null.</summary>
        public Contact Best(SuspicionLevel level)
        {
            Contact best = null;
            for (var i = 0; i < _contacts.Count; i++)
            {
                var contact = _contacts[i];
                if (contact.Level >= level && contact.Level != SuspicionLevel.Searching && (best == null || contact.Suspicion > best.Suspicion))
                    best = contact;
            }

            return best;
        }

        /// <summary>True when the point is in view and nothing solid is in the way.</summary>
        public bool CanSee(Vector3 point)
        {
            var eye = _eye.position;
            var toPoint = point - eye;
            var distance = toPoint.magnitude;
            var angle = Vector3.Angle(_body.forward, new Vector3(toPoint.x, 0f, toPoint.z));
            var inView = (distance <= _settings.FarRange && angle <= _settings.FarAngle) || (distance <= _settings.NearRange && angle <= _settings.NearAngle);
            return inView && !Physics.Linecast(eye, point, _sightMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>A statue the bot has not seen before, in view and close; null when there is none.</summary>
        public bool TryFindNewStatue(out Vector3 position)
        {
            var statues = GnomeStatues.All;
            for (var s = 0; s < statues.Count; s++)
            {
                for (var slot = 0; slot < GnomeStatues.SlotTotal; slot++)
                {
                    if (!statues[s].TryGetPosition(slot, out var at) || IsChecked(at))
                        continue;

                    if ((at - _body.position).sqrMagnitude <= _settings.StatueCheckDistance * _settings.StatueCheckDistance && CanSee(at + Vector3.up * 0.3f))
                    {
                        position = at;
                        return true;
                    }
                }
            }

            position = default;
            return false;
        }

        /// <summary>The bot has looked at this spot and will leave it alone.</summary>
        public void MarkChecked(Vector3 position)
        {
            if (_checkedStatues.Count >= _checkedStatues.Capacity)
                _checkedStatues.RemoveAt(0);

            _checkedStatues.Add(position);
        }

        private bool IsChecked(Vector3 position)
        {
            for (var i = 0; i < _checkedStatues.Count; i++)
            {
                if ((_checkedStatues[i] - position).sqrMagnitude < 0.25f)
                    return true;
            }

            return false;
        }

        private void SyncContacts()
        {
            var gnomes = Petrifiable.All;
            for (var i = _contacts.Count - 1; i >= 0; i--)
            {
                var gnome = _contacts[i].Gnome;
                if (gnome == null || !gnome.IsAvailable)
                    _contacts.RemoveAt(i);
            }

            for (var i = 0; i < gnomes.Count; i++)
            {
                var gnome = gnomes[i];
                if (gnome != null && gnome.IsAvailable && !HasContact(gnome))
                    _contacts.Add(new Contact { Gnome = gnome });
            }
        }

        private bool HasContact(Petrifiable gnome)
        {
            for (var i = 0; i < _contacts.Count; i++)
            {
                if (_contacts[i].Gnome == gnome)
                    return true;
            }

            return false;
        }

        private void TickContact(Contact contact, float deltaTime)
        {
            var gnome = contact.Gnome;
            var position = gnome.transform.position;
            contact.Visible = !gnome.IsLockedInCage && !IsHidden(gnome, position) && CanSee(position + Vector3.up * 0.3f);

            if (contact.Visible)
            {
                var gain = deltaTime / _settings.SecondsToConfirm * Conspicuousness(gnome, position);
                contact.Suspicion = Mathf.Clamp01(contact.Suspicion + gain + Random.Range(-_settings.Noise, _settings.Noise) * deltaTime);
                if (contact.Suspicion > 0.05f)
                {
                    contact.LastSeen = position;
                    contact.LastSeenTime = Time.time;
                }
            }
            else
            {
                contact.Suspicion = Mathf.Max(0f, contact.Suspicion - _settings.DecayPerSecond * deltaTime);
            }

            contact.Level = NextLevel(contact);
        }

        // Crouched in tall grass nobody is seen, until the bot almost steps on them.
        private bool IsHidden(Petrifiable gnome, Vector3 position) =>
            gnome.TryGetComponent<GardenDisguise>(out var disguise) && disguise.IsHiddenInGrass
            && (position - _body.position).sqrMagnitude > _settings.GrassSightDistance * _settings.GrassSightDistance;

        // How much a sighting tells the bot. Near and moving says everything; a still ornament where one always stood says nothing.
        private float Conspicuousness(Petrifiable gnome, Vector3 position)
        {
            var distance = Vector3.Distance(_body.position, position);
            var nearness = Mathf.Lerp(1.4f, 0.5f, Mathf.InverseLerp(_settings.NearRange, _settings.FarRange, distance));
            if (!gnome.TryGetComponent<GardenDisguise>(out var disguise) || !disguise.IsDisguised)
                return nearness;

            return IsKnownOrnament(position) ? 0f : nearness * 0.25f;
        }

        private bool IsKnownOrnament(Vector3 position)
        {
            var limit = OrnamentMatchDistance * OrnamentMatchDistance;
            for (var i = 0; i < _knownOrnaments.Count; i++)
            {
                if ((_knownOrnaments[i] - position).sqrMagnitude <= limit)
                    return true;
            }

            return false;
        }

        private SuspicionLevel NextLevel(Contact contact)
        {
            var value = contact.Suspicion;
            if (contact.Level == SuspicionLevel.Confirmed && !contact.Visible)
                return SuspicionLevel.Searching;
            if (contact.Level == SuspicionLevel.Searching)
                return contact.Visible ? SuspicionLevel.Confirmed : value > 0.2f ? SuspicionLevel.Searching : SuspicionLevel.Unaware;

            if (value < 0.25f)
                return SuspicionLevel.Unaware;

            if (contact.Level == SuspicionLevel.Unaware)
                contact.CuriousSince = Time.time;

            if (value < 0.6f)
                return SuspicionLevel.Curious;

            if (value < 0.95f || Time.time - contact.CuriousSince < _settings.MinCuriousSeconds)
                return SuspicionLevel.Suspicious;

            return SuspicionLevel.Confirmed;
        }
    }
}
