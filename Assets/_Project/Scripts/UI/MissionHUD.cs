using BetterTogether.Audio;
using BetterTogether.Gaze;
using BetterTogether.Match;
using BetterTogether.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// Shows the local team's side missions, the reward banner and, for the camper, the butterfly swarm over the raised camera.
    /// Everything comes from replicated state in <see cref="MatchManager"/>; the first frame only initialises, so a late joiner
    /// hears nothing old. Built by GnoMore/Construir HUD de juego.
    /// </summary>
    public class MissionHUD : MonoBehaviour
    {
        private const int SwarmTexturePixels = 64;

        [Tooltip("List shown during a match with missions.")]
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text _title;
        [Tooltip("One line per photo; the gnomes use only the first.")]
        [SerializeField] private TMP_Text[] _rows;
        [Tooltip("Message while a reward is running.")]
        [SerializeField] private TMP_Text _banner;
        [Tooltip("Parent of the swarm, shown over the camper's raised camera.")]
        [SerializeField] private GameObject _swarmRoot;
        [SerializeField] private Image[] _swarm;
        [Tooltip("How far the swarm spreads from the centre of the screen, in canvas pixels.")]
        [SerializeField] private Vector2 _swarmSpread = new(330f, 210f);
        [Tooltip("Speed of the swarm's drift.")]
        [SerializeField, Min(0f)] private float _swarmSpeed = 1.6f;
        [Tooltip("Wing beats per second of the swarm.")]
        [SerializeField, Min(0f)] private float _swarmFlap = 6f;
        [SerializeField] private SoundCue _stepSfx;
        [SerializeField] private SoundCue _completeSfx;
        [SerializeField] private SoundCue _swarmSfx;

        private Texture2D _swarmTexture;
        private Sprite _swarmSprite;
        private PlayerRole _shownRole;
        private int _shownState = -1;
        private int _shownSeed;
        private int _lastDone;
        private bool _hasDone;
        private bool _swarmOn;
        private bool _rewardsSeen;

        private void Awake()
        {
            BuildSwarmSprite();
            for (var i = 0; i < _swarm.Length; i++)
                _swarm[i].sprite = _swarmSprite;

            Hide();
        }

        private void OnDestroy()
        {
            if (_swarmSprite != null)
                Destroy(_swarmSprite);
            if (_swarmTexture != null)
                Destroy(_swarmTexture);
        }

        private void LateUpdate()
        {
            var match = MatchManager.Instance;
            var layout = MissionLayout.Current;
            var local = PlayerCharacter.Local;
            if (match != null && local != null && match.TravesurasRunning)
            {
                ShowTravesuras(match, local.Role == PlayerRole.Camper);
                return;
            }

            if (match == null || layout == null || local == null || !match.MissionsRunning)
            {
                Hide();
                return;
            }

            if (!_panel.activeSelf)
                _panel.SetActive(true);

            var camper = local.Role == PlayerRole.Camper;
            var done = camper ? CountPhotos(match) : match.ButterflyTotal;
            var state = (camper ? PhotoMask(match) : match.ButterflyTotal) | (camper ? 1 << 8 : 0);
            if (state != _shownState || local.Role != _shownRole || match.Seed != _shownSeed)
            {
                Refresh(match, layout, camper);
                _shownState = state;
                _shownRole = local.Role;
                _shownSeed = match.Seed;
            }

            UpdateSounds(done, camper ? MissionLayout.PhotoTasks : layout.ButterfliesNeeded);
            UpdateRewards(match, camper);
        }

        // The list of pranks: one line for each team, rewritten only when something changes.
        private void ShowTravesuras(MatchManager match, bool camper)
        {
            if (!_panel.activeSelf)
                _panel.SetActive(true);

            var list = TravesuraLayout.Current;
            var done = match.TravesurasDone;
            var active = match.ActiveTravesura;
            var isBoss = match.TravesuraBoss == PlayerCharacter.Local.Object.StateAuthority;
            var open = match.TravesuraOpenToAll;
            var hidden = PlayerCharacter.Local.TryGetComponent<GardenDisguise>(out var disguise) && disguise.IsHiddenInGrass;
            var panUp = PlayerCharacter.Local.TryGetComponent<GnomePan>(out var pan) && pan.IsUp;
            var state = done | ((active + 1) << 4) | (isBoss ? 1 << 8 : 0) | (camper ? 1 << 9 : 0) | (open ? 1 << 10 : 0) | (hidden ? 1 << 11 : 0) | (panUp ? 1 << 12 : 0) | (match.TravesuraBoss.PlayerId << 13);
            UpdateSounds(done, list.Needed);
            if (state == _shownState)
                return;

            _shownState = state;
            _title.text = camper ? "Los gnomos llevan " + done + " de " + list.Needed : "Lista de Travesuras: " + done + " / " + list.Needed;
            string line;
            if (active < 0)
                line = camper ? "Disfruta de tu cena mientras puedas" : "Esperad la siguiente travesura";
            else if (camper)
                line = "Algo traman " + list.Get(active).CamperHint;
            else if (isBoss)
                line = "Eres el Jefe. Explica: " + list.Get(active).Label;
            else if (open)
                line = list.Get(active).Label;
            else
                line = "Pregúntale al Jefe: " + BossName(match);

            _rows[0].text = line;
            _rows[0].gameObject.SetActive(true);
            _rows[1].text = panUp ? "¡Sartén arriba!" : hidden ? "Oculto en la hierba" : "[Tab] Manual";
            _rows[1].gameObject.SetActive(true);
            for (var i = 2; i < _rows.Length; i++)
                _rows[i].gameObject.SetActive(false);
        }

        private static string BossName(MatchManager match)
        {
            foreach (var character in PlayerCharacter.All)
            {
                if (character != null && character.Object != null && character.Object.StateAuthority == match.TravesuraBoss)
                    return character.DisplayName;
            }

            return "?";
        }

        private void Refresh(MatchManager match, MissionLayout layout, bool camper)
        {
            _title.text = camper ? "Encargos de fotos" : "Misión de los gnomos";
            if (camper)
            {
                for (var slot = 0; slot < _rows.Length; slot++)
                {
                    var label = PhotoLabel(layout, slot);
                    _rows[slot].text = match.IsPhotoDone(slot) ? "<s>" + label + "</s>" : label;
                    _rows[slot].gameObject.SetActive(true);
                }

                return;
            }

            var caught = Mathf.Min(match.ButterflyTotal, layout.ButterfliesNeeded);
            _rows[0].text = "Mariposas: " + caught + " / " + layout.ButterfliesNeeded;
            _rows[0].gameObject.SetActive(true);
            for (var i = 1; i < _rows.Length; i++)
                _rows[i].gameObject.SetActive(false);
        }

        private static string PhotoLabel(MissionLayout layout, int slot)
        {
            var subject = layout.Subject(slot);
            if (subject != null)
                return subject.Label;

            return layout.GnomePhotoIsStone ? "Foto de un gnomo ya en piedra" : "Foto de un gnomo con bomba";
        }

        private void UpdateSounds(int done, int needed)
        {
            if (_hasDone && done > _lastDone)
            {
                if (done == needed)
                    _completeSfx.Play();
                else if (done < needed)
                    _stepSfx.Play();
            }

            _hasDone = true;
            _lastDone = done;
        }

        private void UpdateRewards(MatchManager match, bool camper)
        {
            var reveal = match.RevealRemaining > 0f;
            var swarm = match.SwarmRemaining > 0f;
            if (swarm && !_swarmOn && _rewardsSeen)
                _swarmSfx.Play();

            _rewardsSeen = true;
            _swarmOn = swarm;

            var showBanner = reveal || swarm;
            if (showBanner)
            {
                var text = swarm
                    ? camper ? "¡Enjambre! Las mariposas tapan tu cámara" : "¡Enjambre sobre la cámara del campista!"
                    : camper ? "¡Álbum completo! Los gnomos salen en tu plano" : "¡Cuidado! El campista os ve en su plano";
                if (_banner.text != text)
                    _banner.text = text;
            }

            if (_banner.gameObject.activeSelf != showBanner)
                _banner.gameObject.SetActive(showBanner);

            var focus = CameraFocus.Instance;
            var covered = swarm && camper && focus != null && focus.Amount > 0f;
            if (_swarmRoot.activeSelf != covered)
                _swarmRoot.SetActive(covered);

            if (covered)
                MoveSwarm();
        }

        private void MoveSwarm()
        {
            var t = Time.unscaledTime * _swarmSpeed;
            for (var i = 0; i < _swarm.Length; i++)
            {
                var phase = i * 2.399f;
                var rect = _swarm[i].rectTransform;
                rect.anchoredPosition = new Vector2(
                    Mathf.Sin(t + phase) * Mathf.Sin(phase * 1.3f) * _swarmSpread.x,
                    Mathf.Cos(t * 0.8f + phase * 1.7f) * _swarmSpread.y);
                var flap = Mathf.Abs(Mathf.Sin(Time.unscaledTime * _swarmFlap * Mathf.PI + phase));
                rect.localScale = new Vector3(0.35f + 0.65f * flap, 1f, 1f);
            }
        }

        private void Hide()
        {
            if (_panel.activeSelf)
                _panel.SetActive(false);
            if (_banner.gameObject.activeSelf)
                _banner.gameObject.SetActive(false);
            if (_swarmRoot.activeSelf)
                _swarmRoot.SetActive(false);

            _shownState = -1;
            _hasDone = false;
            _rewardsSeen = false;
            _swarmOn = false;
        }

        private static int PhotoMask(MatchManager match)
        {
            var mask = 0;
            for (var slot = 0; slot < MissionLayout.PhotoTasks; slot++)
            {
                if (match.IsPhotoDone(slot))
                    mask |= 1 << slot;
            }

            return mask;
        }

        private static int CountPhotos(MatchManager match)
        {
            var count = 0;
            for (var slot = 0; slot < MissionLayout.PhotoTasks; slot++)
            {
                if (match.IsPhotoDone(slot))
                    count++;
            }

            return count;
        }

        // Two pairs of round wings, generated once so the HUD needs no art asset.
        private void BuildSwarmSprite()
        {
            var fill = (Color32)UiTheme.Safe.cream;
            var outline = (Color32)UiTheme.Safe.ink;
            var pixels = new Color32[SwarmTexturePixels * SwarmTexturePixels];
            for (var y = 0; y < SwarmTexturePixels; y++)
            {
                for (var x = 0; x < SwarmTexturePixels; x++)
                {
                    var u = Mathf.Abs((x + 0.5f) / SwarmTexturePixels - 0.5f);
                    var v = (y + 0.5f) / SwarmTexturePixels - 0.5f;
                    var wing = Mathf.Min(Wing(u, v, 0.24f, 0.14f, 0.21f), Wing(u, v, 0.2f, -0.16f, 0.16f));
                    var body = u < 0.03f && Mathf.Abs(v) < 0.28f;
                    pixels[y * SwarmTexturePixels + x] = body || (wing <= 1f && wing > 0.72f) ? outline : wing <= 1f ? fill : default;
                }
            }

            _swarmTexture = new Texture2D(SwarmTexturePixels, SwarmTexturePixels, TextureFormat.RGBA32, false) { name = "MissionButterfly", wrapMode = TextureWrapMode.Clamp };
            _swarmTexture.SetPixels32(pixels);
            _swarmTexture.Apply(false);
            _swarmSprite = Sprite.Create(_swarmTexture, new Rect(0, 0, SwarmTexturePixels, SwarmTexturePixels), new Vector2(0.5f, 0.5f));
        }

        private static float Wing(float u, float v, float centreU, float centreV, float radius)
        {
            var du = (u - centreU) / radius;
            var dv = (v - centreV) / radius;
            return du * du + dv * dv;
        }
    }
}
