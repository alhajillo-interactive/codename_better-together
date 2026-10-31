using BetterTogether.Audio;
using BetterTogether.Player;
using BetterTogether.Voice;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// Local player settings: volume per channel, mouse sensitivity, camera bob (motion sickness) and microphone mode.
    /// Changes apply at once and are saved in PlayerPrefs. Local only: nothing here touches the network.
    /// In the main menu it opens from a button; in a match it opens and closes with Esc (the game keeps running
    /// because it is online) and frees the cursor through <see cref="FirstPersonCamera.CursorFree"/>.
    /// Built by the editor tool GnoMore/Construir ajustes.
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        [Tooltip("Content shown while the panel is open. The component itself stays active so it can read Esc.")]
        [SerializeField] private GameObject _content;
        [Tooltip("Open and close with Esc. On in matches; off in the main menu, where a button opens it.")]
        [SerializeField] private bool _toggleWithEscape = true;

        [Header("Volume")]
        [SerializeField] private Slider _masterSlider;
        [SerializeField] private Slider _musicSlider;
        [SerializeField] private Slider _sfxSlider;
        [SerializeField] private Slider _voiceSlider;

        [Header("Controls")]
        [Tooltip("Slider range is the mouse sensitivity multiplier (1 = default).")]
        [SerializeField] private Slider _sensitivitySlider;
        [Tooltip("0 = no head bob, 1 = full bob.")]
        [SerializeField] private Slider _bobSlider;

        [Header("Microphone")]
        [SerializeField] private Button _micModeButton;
        [SerializeField] private TMP_Text _micModeLabel;
        [SerializeField] private TMP_Text _talkKeyLabel;
        [Tooltip("Same asset the player voice uses; gives the default mode and the push-to-talk key.")]
        [SerializeField] private VoiceChatSettings _voiceSettings;

        [Header("Accessibility")]
        [Tooltip("Toggle (same style as the microphone button) for 'Reducir destellos': caps the camera flashes. Saved in PlayerPrefs 'reduceFlashes'.")]
        [SerializeField] private Button _reduceFlashesButton;
        [SerializeField] private TMP_Text _reduceFlashesLabel;
        [Tooltip("Toggle for 'Mapa: mantener / alternar' (camp plan key held or toggled). Saved in PlayerPrefs 'mapToggle'.")]
        [SerializeField] private Button _mapModeButton;
        [SerializeField] private TMP_Text _mapModeLabel;

        [SerializeField] private Button _closeButton;

        private bool _open;
        private bool _freedCursor;
        private CameraFeel _cameraFeel;

        public bool IsOpen => _open;

        private void Awake()
        {
            Wire(_masterSlider, AudioChannel.Master);
            Wire(_musicSlider, AudioChannel.Music);
            Wire(_sfxSlider, AudioChannel.Sfx);
            Wire(_voiceSlider, AudioChannel.Voice);

            if (_sensitivitySlider != null)
                _sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
            if (_bobSlider != null)
                _bobSlider.onValueChanged.AddListener(OnBobChanged);
            if (_micModeButton != null)
                _micModeButton.onClick.AddListener(OnMicModeClicked);
            if (_reduceFlashesButton != null)
                _reduceFlashesButton.onClick.AddListener(OnReduceFlashesClicked);
            if (_mapModeButton != null)
                _mapModeButton.onClick.AddListener(OnMapModeClicked);
            if (_closeButton != null)
                _closeButton.onClick.AddListener(Close);

            if (_content != null)
                _content.SetActive(false);

            EnsureEventSystem();
        }

        private void OnDestroy()
        {
            if (_sensitivitySlider != null)
                _sensitivitySlider.onValueChanged.RemoveListener(OnSensitivityChanged);
            if (_bobSlider != null)
                _bobSlider.onValueChanged.RemoveListener(OnBobChanged);
            if (_micModeButton != null)
                _micModeButton.onClick.RemoveListener(OnMicModeClicked);
            if (_reduceFlashesButton != null)
                _reduceFlashesButton.onClick.RemoveListener(OnReduceFlashesClicked);
            if (_mapModeButton != null)
                _mapModeButton.onClick.RemoveListener(OnMapModeClicked);
            if (_closeButton != null)
                _closeButton.onClick.RemoveListener(Close);
        }

        private void OnDisable()
        {
            if (_open)
                Close();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
                return;

            if (_open)
                Close();
            else if (_toggleWithEscape && !FirstPersonCamera.IsTyping)
                Open();
        }

        public void Open()
        {
            if (_open)
                return;

            _open = true;
            _cameraFeel = null; // looked up again on the first bob change: the camera may have been recreated
            RefreshControls();
            if (_content != null)
                _content.SetActive(true);

            // In a match the camera relocks the cursor when this goes back to false, so Esc never leaves it free.
            if (_toggleWithEscape && !FirstPersonCamera.CursorFree)
            {
                FirstPersonCamera.CursorFree = true;
                _freedCursor = true;
            }
        }

        public void Close()
        {
            if (!_open)
                return;

            _open = false;
            if (_content != null)
                _content.SetActive(false);

            if (_freedCursor)
            {
                FirstPersonCamera.CursorFree = false;
                _freedCursor = false;
            }

            PlayerPrefs.Save();
        }

        private void Wire(Slider slider, AudioChannel channel)
        {
            if (slider == null)
                return;

            slider.onValueChanged.AddListener(value =>
            {
                AudioManager.SetVolume(channel, value);
                SetPercentLabel(slider, value);
            });
        }

        // Shows the saved values without raising the change events (which would save them again).
        private void RefreshControls()
        {
            SetSlider(_masterSlider, AudioManager.GetVolume(AudioChannel.Master));
            SetSlider(_musicSlider, AudioManager.GetVolume(AudioChannel.Music));
            SetSlider(_sfxSlider, AudioManager.GetVolume(AudioChannel.Sfx));
            SetSlider(_voiceSlider, AudioManager.GetVolume(AudioChannel.Voice));
            SetSlider(_sensitivitySlider, FirstPersonCamera.SensitivityMultiplier);
            SetSlider(_bobSlider, PlayerPrefs.GetFloat(CameraFeel.BobPrefsKey, 1f));
            RefreshMic();
            RefreshFlashes();
            RefreshMapMode();
        }

        private static void SetSlider(Slider slider, float value)
        {
            if (slider == null)
                return;

            slider.SetValueWithoutNotify(value);
            SetPercentLabel(slider, slider.value);
        }

        // Each slider sits in a row that also holds a TMP label named "Value".
        private static void SetPercentLabel(Slider slider, float value)
        {
            var label = slider.transform.parent != null ? slider.transform.parent.Find("Value") : null;
            if (label != null && label.TryGetComponent(out TMP_Text text))
                text.text = Mathf.RoundToInt(value * 100f) + " %";
        }

        private void OnSensitivityChanged(float value)
        {
            FirstPersonCamera.SensitivityMultiplier = value;
            SetPercentLabel(_sensitivitySlider, value);
        }

        private void OnBobChanged(float value)
        {
            PlayerPrefs.SetFloat(CameraFeel.BobPrefsKey, value);
            SetPercentLabel(_bobSlider, value);

            // Apply live when a camera exists (in a match); in the menu it reads the saved value when it starts.
            if (_cameraFeel == null)
                _cameraFeel = FindAnyObjectByType<CameraFeel>();
            if (_cameraFeel != null)
                _cameraFeel.BobScale = value;
        }

        private void OnMicModeClicked()
        {
            if (_voiceSettings == null)
                return;

            _voiceSettings.SetPushToTalk(!_voiceSettings.PushToTalk);
            RefreshMic();
        }

        private void OnReduceFlashesClicked()
        {
            FlashSettings.ReduceFlashes = !FlashSettings.ReduceFlashes;
            PlayerPrefs.Save();
            RefreshFlashes();
        }

        private void OnMapModeClicked()
        {
            MapSettings.Toggle = !MapSettings.Toggle;
            PlayerPrefs.Save();
            RefreshMapMode();
        }

        private void RefreshMapMode()
        {
            if (_mapModeLabel != null)
                _mapModeLabel.text = MapSettings.Toggle ? "Mapa: alternar" : "Mapa: mantener";
        }

        private void RefreshFlashes()
        {
            if (_reduceFlashesLabel != null)
                _reduceFlashesLabel.text = FlashSettings.ReduceFlashes ? "Reducir destellos: sí" : "Reducir destellos: no";
        }

        private void RefreshMic()
        {
            if (_voiceSettings == null)
                return;

            var pushToTalk = _voiceSettings.PushToTalk;
            if (_micModeLabel != null)
                _micModeLabel.text = pushToTalk ? "Micrófono: pulsar para hablar" : "Micrófono: abierto";
            if (_talkKeyLabel != null)
                _talkKeyLabel.text = pushToTalk
                    ? "Mantén [" + _voiceSettings.PushToTalkKey + "] para hablar"
                    : "Silenciar micrófono: [M]";
        }

        // Menus and the match scene may not have one yet; without it the sliders would not react to the mouse.
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
                return;

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
