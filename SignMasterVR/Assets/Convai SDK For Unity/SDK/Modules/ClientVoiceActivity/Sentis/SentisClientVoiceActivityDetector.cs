using System;
using System.Threading;
using Convai.Domain.Logging;
using Convai.Runtime.Networking.Media;
using Unity.InferenceEngine;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;
using Object = UnityEngine.Object;

namespace Convai.Modules.ClientVoiceActivity.Sentis
{
    internal sealed class SentisClientVoiceActivityDetectorFactory : IClientVoiceActivityDetectorFactory
    {
        private const string RunnerName = "[Convai] Client Voice Activity";

        public IClientVoiceActivityDetector Create(
            IMicrophonePcmSource source,
            GameObject host,
            Func<bool> shouldProcess,
            Action<ClientVoiceActivityStateChanged> stateChanged,
            ILogger logger)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (shouldProcess == null) throw new ArgumentNullException(nameof(shouldProcess));
            if (stateChanged == null) throw new ArgumentNullException(nameof(stateChanged));

            var runnerObject = new GameObject(RunnerName)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            if (host != null)
                runnerObject.transform.SetParent(host.transform, false);
            else
                Object.DontDestroyOnLoad(runnerObject);

            var runner = runnerObject.AddComponent<SentisClientVoiceActivityDetector>();
            try
            {
                runner.Initialize(source, shouldProcess, stateChanged, logger);
                return runner;
            }
            catch
            {
                if (UnityEngine.Application.isPlaying)
                    Object.Destroy(runnerObject);
                else
                    Object.DestroyImmediate(runnerObject);
                throw;
            }
        }
    }

    internal static class SentisClientVoiceActivityRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() =>
            ClientVoiceActivityDetectorFactoryRegistry.Factory =
                new SentisClientVoiceActivityDetectorFactory();
    }

    internal sealed class SentisClientVoiceActivityDetector : MonoBehaviour, IClientVoiceActivityDetector
    {
        private const string ModelResourcePath = "Convai/SileroVad16k";
        private const int RingCapacity = 131072;
        private const int ContextSamples = 64;
        private const int WindowSamples = 512;
        private const int ModelInputSamples = ContextSamples + WindowSamples;
        private const int StateSamples = 2 * 1 * 128;
        private const int MaxWindowsPerUpdate = 4;

        private readonly float[] _context = new float[ContextSamples];
        private readonly float[] _modelInput = new float[ModelInputSamples];
        private readonly float[] _resampledWindow = new float[WindowSamples];
        private readonly float[] _zeroState = new float[StateSamples];
        private readonly SpscFloatRingBuffer _sampleBuffer = new(RingCapacity);
        private readonly StreamingLinearResampler _resampler = new();

        private ClientVoiceActivityDebouncer _debouncer;
        private Func<bool> _shouldProcess;
        private IMicrophonePcmSource _source;
        private Tensor<float> _inputTensor;
        private Tensor<float> _probabilityOutput;
        private Tensor<float> _stateInput;
        private Tensor<float> _stateOutput;
        private Worker _worker;
        private int _activeSampleRate;
        private int _latestSampleRate;
        private int _captureEnabled;
        private bool _processingEnabled;
        private bool _disposed;

        internal void Initialize(
            IMicrophonePcmSource source,
            Func<bool> shouldProcess,
            Action<ClientVoiceActivityStateChanged> stateChanged,
            ILogger logger)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _shouldProcess = shouldProcess ?? throw new ArgumentNullException(nameof(shouldProcess));
            _debouncer = new ClientVoiceActivityDebouncer(state =>
                stateChanged(new ClientVoiceActivityStateChanged(
                    state.Stage,
                    state.Probability,
                    _source?.IsAcousticEchoCancellationActive == true)));

            ModelAsset modelAsset = Resources.Load<ModelAsset>(ModelResourcePath);
            if (modelAsset == null)
                throw new InvalidOperationException(
                    $"Silero VAD model was not found at Resources/{ModelResourcePath}.");

            Model model = ModelLoader.Load(modelAsset);
            _worker = new Worker(model, BackendType.CPU);
            _inputTensor = new Tensor<float>(new TensorShape(1, ModelInputSamples));
            _stateInput = new Tensor<float>(new TensorShape(2, 1, 128));
            _stateOutput = new Tensor<float>(new TensorShape(2, 1, 128));
            _probabilityOutput = new Tensor<float>(new TensorShape(1, 1));

            _source.PcmFrame += HandlePcmFrame;
            logger?.Info("Client-side Silero VAD started on the native microphone stream.");
        }

        private void Update()
        {
            if (_disposed || _worker == null)
                return;

            bool shouldProcess = _shouldProcess?.Invoke() ?? false;
            Volatile.Write(ref _captureEnabled, shouldProcess ? 1 : 0);
            if (!shouldProcess)
            {
                if (_processingEnabled)
                    ResetInferenceState();

                _processingEnabled = false;
                _sampleBuffer.Clear();
                return;
            }

            if (!_processingEnabled)
            {
                _processingEnabled = true;
                _sampleBuffer.Clear();
                _resampler.Reset();
                ResetInferenceState();
            }

            int sampleRate = Volatile.Read(ref _latestSampleRate);
            if (sampleRate <= 0)
                return;

            if (sampleRate != _activeSampleRate)
            {
                _activeSampleRate = sampleRate;
                _sampleBuffer.Clear();
                _resampler.Configure(sampleRate);
                ResetInferenceState();
                return;
            }

            int processed = 0;
            while (processed < MaxWindowsPerUpdate &&
                   _resampler.TryReadFrame(_sampleBuffer, _resampledWindow))
            {
                RunInference();
                processed++;
            }
        }

        private void HandlePcmFrame(float[] samples, int channels, int sampleRate)
        {
            if (_disposed || Volatile.Read(ref _captureEnabled) == 0 ||
                samples == null || channels <= 0 || sampleRate <= 0)
                return;

            Volatile.Write(ref _latestSampleRate, sampleRate);
            int completeSampleCount = samples.Length - samples.Length % channels;
            for (int i = 0; i < completeSampleCount; i += channels)
            {
                float mono = 0f;
                for (int channel = 0; channel < channels; channel++)
                    mono += samples[i + channel];

                _sampleBuffer.TryWrite(mono / channels);
            }
        }

        private void RunInference()
        {
            Array.Copy(_context, 0, _modelInput, 0, ContextSamples);
            Array.Copy(_resampledWindow, 0, _modelInput, ContextSamples, WindowSamples);
            Array.Copy(
                _resampledWindow,
                WindowSamples - ContextSamples,
                _context,
                0,
                ContextSamples);

            _inputTensor.Upload(_modelInput);
            _worker.SetInput("input", _inputTensor);
            _worker.SetInput("state", _stateInput);
            _worker.Schedule();

            Tensor probabilityOutput = _probabilityOutput;
            Tensor stateOutput = _stateOutput;
            _worker.CopyOutput("output", ref probabilityOutput);
            _worker.CopyOutput("stateN", ref stateOutput);
            _probabilityOutput = (Tensor<float>)probabilityOutput;
            _stateOutput = (Tensor<float>)stateOutput;

            _probabilityOutput.CompleteAllPendingOperations();
            float probability = _probabilityOutput[0];
            _debouncer.Observe(probability);

            Tensor<float> previousState = _stateInput;
            _stateInput = _stateOutput;
            _stateOutput = previousState;
        }

        private void ResetInferenceState()
        {
            Array.Clear(_context, 0, _context.Length);
            Array.Clear(_modelInput, 0, _modelInput.Length);
            Array.Clear(_resampledWindow, 0, _resampledWindow.Length);
            _stateInput?.Upload(_zeroState);
            _stateOutput?.Upload(_zeroState);
            _debouncer?.Stop();
        }

        public void Dispose() => DisposeCore(destroyObject: true);

        private void DisposeCore(bool destroyObject)
        {
            if (_disposed)
                return;

            _disposed = true;
            Volatile.Write(ref _captureEnabled, 0);
            if (_source != null)
                _source.PcmFrame -= HandlePcmFrame;

            _debouncer?.Stop();
            _worker?.Dispose();
            _inputTensor?.Dispose();
            _stateInput?.Dispose();
            _stateOutput?.Dispose();
            _probabilityOutput?.Dispose();

            _source = null;
            _shouldProcess = null;
            _debouncer = null;
            _worker = null;
            _inputTensor = null;
            _stateInput = null;
            _stateOutput = null;
            _probabilityOutput = null;

            if (!destroyObject || gameObject == null)
                return;

            if (UnityEngine.Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }

        private void OnDestroy() => DisposeCore(destroyObject: false);
    }
}
