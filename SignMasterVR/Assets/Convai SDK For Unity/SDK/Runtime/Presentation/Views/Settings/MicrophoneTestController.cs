using System;
using System.Collections;
using Convai.Domain.Logging;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Shared.Abstractions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Convai.Runtime.Presentation.Views.Settings
{
    public class MicrophoneTestController : MonoBehaviour
    {
        private const string WAITING_FOR_RECORDING = "Waiting for recording...";
        private const string RECORDING = "Recording...";
        private const string PLAYING = "Playing...";
        private const string NO_MICROPHONE_DETECTED = "No Microphone Detected";
        private const string MICROPHONE_PERMISSION_DENIED = "Microphone Permission Denied";
        private const string MICROPHONE_TEST_NOT_SUPPORTED = "Microphone test not supported on WebGL";
        private const string BUTTON_TEXT_RECORD = "Record";
        private const string BUTTON_TEXT_STOP = "Stop";
        private const int RECORDING_LENGTH = 10;
        private const int FREQUENCY = 44100;

        private static readonly Color COLOR_BUTTON_RECORDING = Color.red;
        private static readonly Color COLOR_BUTTON_IDLE = Color.green;
        private static readonly Color COLOR_TEXT_RECORDING = Color.white;
        private static readonly Color COLOR_TEXT_IDLE = new(0.14f, 0.14f, 0.14f);

        [SerializeField] private TMP_Dropdown microphoneDropdown;
        [SerializeField] private TextMeshProUGUI recordStatusText;
        [SerializeField] private Button recordButton;
        [SerializeField] private RectTransform waveVisualizerUI;
        [SerializeField] private RectTransform waveVisualizerBackground;

        private readonly float[] _clipSampleData = new float[1024];
        private readonly float _waveMultiplier = 500;
        private AudioSource _audioSource;
        private Image _buttonImage;
        private TextMeshProUGUI _buttonText;
        private bool _isRecording;
        private IConvaiPermissionService _permissionService;
        private Coroutine _playAudioCoroutine;

        private AudioClip _recording;

        private void OnEnable()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Microphone recording/playback is not supported on WebGL.
            recordStatusText.text = MICROPHONE_TEST_NOT_SUPPORTED;
            recordButton.interactable = false;
            return;
#else
            TryResolveDependencies();
            EnsurePermissionService();
            CacheButtonComponents();
            recordButton.onClick.AddListener(OnRecordButtonClicked);
            InitializeRecordStatusText();
            InitializeAudioSource();
            UpdateButtonAppearance(false);
#endif
        }

        public void Inject(IConvaiPermissionService permissionService = null)
        {
            _permissionService = permissionService;

#if !UNITY_WEBGL || UNITY_EDITOR
            if (isActiveAndEnabled && recordStatusText != null)
                InitializeRecordStatusText();
#endif
        }

        private void TryResolveDependencies()
        {
            if (_permissionService != null) return;

            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager != null && manager.TryGetPermissionService(out IConvaiPermissionService permissionService))
                Inject(permissionService);
        }

        private void CacheButtonComponents()
        {
            _buttonImage = recordButton.GetComponent<Image>();
            _buttonText = recordButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        private void UpdateButtonAppearance(bool isRecording)
        {
            if (_buttonImage != null) _buttonImage.color = isRecording ? COLOR_BUTTON_RECORDING : COLOR_BUTTON_IDLE;

            if (_buttonText != null)
            {
                _buttonText.text = isRecording ? BUTTON_TEXT_STOP : BUTTON_TEXT_RECORD;
                _buttonText.color = isRecording ? COLOR_TEXT_RECORDING : COLOR_TEXT_IDLE;
            }
        }

        private void InitializeAudioSource()
        {
            if (!TryGetComponent(out _audioSource)) _audioSource = gameObject.AddComponent<AudioSource>();
        }

#if !UNITY_WEBGL || UNITY_EDITOR

        private void InitializeRecordStatusText()
        {
            if (Microphone.devices.Length == 0)
            {
                recordStatusText.text = NO_MICROPHONE_DETECTED;
                return;
            }

            EnsurePermissionService();

            if (_permissionService != null && _permissionService.HasMicrophonePermission())
                recordStatusText.text = WAITING_FOR_RECORDING;
            else
            {
                if (_permissionService == null)
                    recordStatusText.text = MICROPHONE_PERMISSION_DENIED;
                else
                {
                    _permissionService.RequestMicrophonePermission(hasPermission =>
                    {
                        recordStatusText.text = hasPermission ? WAITING_FOR_RECORDING : MICROPHONE_PERMISSION_DENIED;
                    });
                }
            }
        }

        private void OnRecordButtonClicked()
        {
            if (_isRecording)
                StopRecording();
            else
                StartRecording();
        }

        private void StartRecording()
        {
            EnsurePermissionService();
            if (_permissionService == null || !_permissionService.HasMicrophonePermission()) return;

            if (_playAudioCoroutine != null) StopCoroutine(_playAudioCoroutine);

            waveVisualizerUI.sizeDelta = new Vector2(2, waveVisualizerUI.sizeDelta.y);
            _audioSource.Stop();
            _audioSource.clip = null;
            _isRecording = true;
            recordStatusText.text = RECORDING;
            UpdateButtonAppearance(true);
            _recording = Microphone.Start(Microphone.devices[microphoneDropdown.value], false, RECORDING_LENGTH,
                FREQUENCY);
        }

        private void StopRecording()
        {
            _isRecording = false;
            int position = Microphone.GetPosition(Microphone.devices[microphoneDropdown.value]);
            _audioSource.clip = _recording;
            Microphone.End(Microphone.devices[microphoneDropdown.value]);
            TrimAudio(position);
            recordStatusText.text = PLAYING;
            UpdateButtonAppearance(false);
            _playAudioCoroutine = StartCoroutine(PlayAudio());
        }

        private IEnumerator PlayAudio()
        {
            _audioSource.Play();
            while (_audioSource.isPlaying)
            {
                ShowAudioSourceAudioWaves();
                yield return null;
            }

            recordStatusText.text = WAITING_FOR_RECORDING;
            waveVisualizerUI.sizeDelta = new Vector2(2, waveVisualizerUI.sizeDelta.y);
        }

        private void TrimAudio(int micRecordLastPosition)
        {
            if (_audioSource.clip == null || micRecordLastPosition <= 0) return;

            AudioClip tempAudioClip = _audioSource.clip;
            int channels = tempAudioClip.channels;
            int position = micRecordLastPosition;
            float[] samplesArray = new float[position * channels];
            tempAudioClip.GetData(samplesArray, 0);

            int samplesToRemove = Mathf.RoundToInt(0.5f * FREQUENCY) * channels;

            if (samplesToRemove >= samplesArray.Length)
            {
                ConvaiLogger.Warning(
                    "Recording is too short to remove 0.5 seconds. Keeping original clip.",
                    LogCategory.Audio);
                return;
            }

            float[] trimmedSamplesArray = new float[samplesArray.Length - samplesToRemove];
            Array.Copy(samplesArray, samplesToRemove, trimmedSamplesArray, 0, trimmedSamplesArray.Length);

            var newClip = AudioClip.Create("RecordedSound", trimmedSamplesArray.Length / channels, channels, FREQUENCY,
                false);
            newClip.SetData(trimmedSamplesArray, 0);
            _audioSource.clip = newClip;
        }

        private void ShowAudioSourceAudioWaves()
        {
            _audioSource.GetSpectrumData(_clipSampleData, 0, FFTWindow.Rectangular);
            float sum = 0;
            Array.ForEach(_clipSampleData, sample => sum += sample);
            float currentAverageVolume = sum * _waveMultiplier;
            Vector2 size = waveVisualizerUI.sizeDelta;
            size.x = currentAverageVolume;
            size.x = Mathf.Clamp(size.x, 2, waveVisualizerBackground.sizeDelta.x);
            waveVisualizerUI.sizeDelta = size;
        }

        private void EnsurePermissionService()
        {
        }

#endif
    }
}
