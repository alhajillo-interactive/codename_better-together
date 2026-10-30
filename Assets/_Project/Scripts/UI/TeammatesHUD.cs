using System.Collections.Generic;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// Top-right row (gnome only) with the two other gnomes: an icon per teammate that tells at a glance whether it is free,
    /// frozen, carried or caged, plus its name. Purely presentation: everything is derived from replicated state
    /// (<see cref="Petrifiable"/>, <see cref="GnomeCapture"/>), so a late joiner sees the current state and no old pulses.
    /// Slots keep their teammate for the whole match unless someone joins or leaves.
    /// </summary>
    public class TeammatesHUD : MonoBehaviour
    {
        private const float NameRefreshInterval = 0.5f;
        private const float NameRefreshWindow = 5f;

        private enum State : byte
        {
            Free,
            Stone,
            Carried,
            Caged,
        }

        private sealed class Mate
        {
            public Petrifiable Gnome;
            public GnomeCapture Capture;
            public PlayerCharacter Character;
            public string Name;
            public float NameUntil;
            public float NextNameRead;
        }

        private sealed class Slot
        {
            public RectTransform Root;
            public Image Icon;
            public RectTransform IconRect;
            public TMP_Text Name;
            public Mate Mate;
            public bool HasShown;
            public State ShownState;
            public float ShownAngle;
            public Tween Pulse;
        }

        [Header("Slots")]
        [Tooltip("The fixed slots (root of each: icon + name).")]
        [SerializeField] private RectTransform[] _slotRoots;
        [Tooltip("Icon image of each slot, same order as the slot roots.")]
        [SerializeField] private Image[] _icons;
        [Tooltip("Name label of each slot, same order as the slot roots.")]
        [SerializeField] private TMP_Text[] _names;

        [Header("Feel")]
        [Tooltip("Opacity of the icon of a free teammate.")]
        [Range(0f, 1f)] [SerializeField] private float _freeAlpha = 0.8f;
        [Tooltip("Sway of the icon of a carried teammate, in degrees either side.")]
        [SerializeField] private float _swayDegrees = 6f;
        [Tooltip("Sway speed of a carried teammate, in radians per second.")]
        [SerializeField] private float _swaySpeed = 6f;
        [Tooltip("Scale the icon starts at when a teammate gets frozen, carried or caged (it settles at 1).")]
        [SerializeField] private float _pulseScale = 1.3f;
        [Tooltip("Seconds the pulse takes.")]
        [SerializeField] private float _pulseSeconds = 0.2f;

        private readonly Dictionary<Petrifiable, Mate> _mates = new();
        private Slot[] _slots;

        private static UiTheme Theme => UiTheme.Safe;

        private void Awake()
        {
            var count = _slotRoots != null ? _slotRoots.Length : 0;
            if (count == 0 || _icons == null || _icons.Length < count || _names == null || _names.Length < count)
            {
                Debug.LogError("[TeammatesHUD] Slots are not wired; run GnoMore/Construir HUD de juego.", this);
                enabled = false;
                return;
            }

            _slots = new Slot[count];
            for (var i = 0; i < count; i++)
            {
                var icon = _icons[i];
                _slots[i] = new Slot
                {
                    Root = _slotRoots[i],
                    Icon = icon,
                    IconRect = icon.rectTransform,
                    Name = _names[i],
                };

                icon.raycastTarget = false;
                SetSlotActive(_slots[i], false);
            }
        }

        private void OnDisable()
        {
            if (_slots == null)
                return;

            for (var i = 0; i < _slots.Length; i++)
                _slots[i].Pulse.Stop();
        }

        private void LateUpdate()
        {
            if (!ShouldShow())
            {
                ReleaseAll();
                return;
            }

            DropGoneMates();
            AssignNewMates();

            for (var i = 0; i < _slots.Length; i++)
                Refresh(_slots[i]);
        }

        // Only a gnome in a running match sees the row.
        private static bool ShouldShow()
        {
            var local = Petrifiable.Local;
            if (local == null || !local.IsAvailable)
                return false;

            var match = MatchManager.Instance;
            return match != null && match.Phase == MatchPhase.Playing;
        }

        private void ReleaseAll()
        {
            for (var i = 0; i < _slots.Length; i++)
                ClearSlot(_slots[i]);

            if (_mates.Count > 0)
                _mates.Clear();
        }

        // A slot whose gnome left (or is no longer available) is freed; the others keep their place.
        private void DropGoneMates()
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                var mate = slot.Mate;
                if (mate == null)
                    continue;

                if (mate.Gnome != null && mate.Gnome.IsAvailable && IsListed(mate.Gnome))
                    continue;

                // A destroyed gnome reads as null but its key is still the same reference, so it comes out too.
                _mates.Remove(mate.Gnome);

                ClearSlot(slot);
            }
        }

        private static void ClearSlot(Slot slot)
        {
            slot.Pulse.Stop();
            slot.Mate = null;
            slot.HasShown = false;
            SetSlotActive(slot, false);
        }

        private static bool IsListed(Petrifiable gnome)
        {
            var all = Petrifiable.All;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] == gnome)
                    return true;
            }

            return false;
        }

        // A gnome that has no slot takes the first free one (the set only changes when someone joins or leaves).
        private void AssignNewMates()
        {
            var local = Petrifiable.Local;
            var all = Petrifiable.All;
            for (var i = 0; i < all.Count; i++)
            {
                var gnome = all[i];
                if (gnome == null || gnome == local || !gnome.IsAvailable || _mates.ContainsKey(gnome))
                    continue;

                var slot = FreeSlot();
                if (slot == null)
                    return;

                gnome.TryGetComponent<GnomeCapture>(out var capture);
                gnome.TryGetComponent<PlayerCharacter>(out var character);
                var mate = new Mate
                {
                    Gnome = gnome,
                    Capture = capture,
                    Character = character,
                    NameUntil = Time.unscaledTime + NameRefreshWindow,
                    NextNameRead = Time.unscaledTime + NameRefreshInterval,
                };

                mate.Name = NameOf(mate);
                _mates.Add(gnome, mate);
                slot.Mate = mate;
                slot.HasShown = false; // first sight: sets the state without a pulse (a late joiner sees no old pulses)
                if (slot.Name != null)
                    slot.Name.SetText(mate.Name);

                SetSlotActive(slot, true);
            }
        }

        private Slot FreeSlot()
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Mate == null)
                    return _slots[i];
            }

            return null;
        }

        private static State StateOf(Mate mate)
        {
            var capture = mate.Capture;
            if (capture != null)
            {
                if (capture.IsCaged)
                    return State.Caged;

                if (capture.IsCarried)
                    return State.Carried;
            }

            return mate.Gnome.IsStone ? State.Stone : State.Free;
        }

        private static string NameOf(Mate mate) => mate.Character != null ? mate.Character.DisplayName : "Jugador";

        private void Refresh(Slot slot)
        {
            var mate = slot.Mate;
            if (mate == null)
                return;

            // The name can arrive after the gnome spawns: it is read again for a few seconds, and written only if it changed.
            var now = Time.unscaledTime;
            if (slot.Name != null && now < mate.NameUntil && now >= mate.NextNameRead)
            {
                mate.NextNameRead = now + NameRefreshInterval;
                var text = NameOf(mate);
                if (text != mate.Name)
                {
                    mate.Name = text;
                    slot.Name.SetText(text);
                }
            }

            var state = StateOf(mate);
            if (!slot.HasShown || state != slot.ShownState)
            {
                var pulse = slot.HasShown && state != State.Free;
                slot.HasShown = true;
                slot.ShownState = state;
                ApplyLook(slot, state);
                if (pulse)
                    PlayPulse(slot);
            }

            // Carried teammates sway a little; the others sit straight.
            var angle = state == State.Carried ? Mathf.Sin(now * _swaySpeed) * _swayDegrees : 0f;
            if (!Mathf.Approximately(angle, slot.ShownAngle))
            {
                slot.ShownAngle = angle;
                slot.IconRect.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private void ApplyLook(Slot slot, State state)
        {
            var theme = Theme;
            var sprite = theme.Icon(state == State.Caged ? UiIcon.Cage : UiIcon.Gnome);
            slot.Icon.sprite = sprite;
            slot.Icon.enabled = sprite != null;

            Color tint;
            switch (state)
            {
                case State.Caged:
                    tint = Color.white; // ui-color-ok: white is the untinted multiplier of a sprite, not a themed colour
                    break;
                case State.Carried:
                case State.Stone:
                    tint = theme.plaster;
                    break;
                default:
                    tint = UiTheme.WithAlpha(Color.white, _freeAlpha); // ui-color-ok: white is the untinted multiplier of a sprite, not a themed colour
                    break;
            }

            slot.Icon.color = tint;
        }

        private void PlayPulse(Slot slot)
        {
            slot.Pulse.Stop();
            slot.Pulse = Tween.Scale(slot.IconRect, _pulseScale, 1f, _pulseSeconds, Ease.OutQuad, useUnscaledTime: true);
        }

        private static void SetSlotActive(Slot slot, bool active)
        {
            if (slot.Root != null && slot.Root.gameObject.activeSelf != active)
                slot.Root.gameObject.SetActive(active);

            if (!active)
            {
                slot.IconRect.localScale = Vector3.one;
                slot.IconRect.localRotation = Quaternion.identity;
                slot.ShownAngle = 0f;
            }
        }
    }
}
