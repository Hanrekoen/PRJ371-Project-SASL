using System;
using Convai.Domain.DomainEvents.Vision;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Runtime.Vision.Sources.Internal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting.APIUpdating;

namespace Convai.Runtime.Vision.Sources
{
    /// <summary>
    ///     Vision frame source that captures from a Unity Camera component.
    ///     Implements <see cref="IVisionFrameSource" /> for video streaming and debug preview.
    ///     Publishes domain events via EventHub when capture state changes.
    /// </summary>
    /// <remarks>
    ///     This is the recommended component for capturing in-game visuals (what the player sees).
    ///     Built-in render pipeline uses render hooks plus a camera command buffer.
    ///     SRP/URP auto mode uses an explicit camera render path because render-pipeline hook capture can return empty frames.
    ///     Capture settings are configured directly on this component.
    /// </remarks>
    [MovedFrom(true, "Convai.Runtime.Vision", "Convai.Runtime",
        "CameraVisionFrameSource")]
    [AddComponentMenu("Convai/Vision/Camera Vision Frame Source")]
    public class CameraVisionFrameSource : MonoBehaviour, IVisionFrameSource, IVisionFrameSourceStatusProvider
    {
        private const float RenderHookTimeoutSeconds = 2f;
        private const float FrameHealthStartupGraceSeconds = 1.5f;
        private const float FrameHealthProbeIntervalSeconds = 0.75f;
        private const int FrameHealthWarningThreshold = 5;
        private const int FrameHealthEscalationThreshold = 30;
        private const int HealthProbeTextureSize = 4;
        private const byte FrameHealthColorThreshold = 8;
        private const byte FrameHealthAlphaThreshold = 8;
        private const CameraEvent CaptureCameraEvent = CameraEvent.AfterEverything;
        private static readonly Vector2 FlipScale = new(1f, -1f);
        private static readonly Vector2 FlipOffset = new(0f, 1f);

        public enum CameraCaptureMode
        {
            Auto,
            BuiltInHooks,
            SrpNative,
            ExplicitRenderCompatibility
        }

        public void Inject(IEventHub eventHub) => _eventHub = eventHub;

        private void TryResolveDependencies()
        {
            if (_eventHub != null) return;

            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager != null && manager.TryGetEventHub(out IEventHub eventHub))
                Inject(eventHub);
        }

        private enum CapturePreset
        {
            LowOverhead,
            Balanced,
            HighDetail,
            Custom
        }

        private enum CaptureMode
        {
            None,
            BuiltInHooks,
            SrpNative,
            ExplicitRenderCompatibility
        }

        #region Inspector Fields

#pragma warning disable CS0649
        [Header("Capture Settings")]
        [Tooltip("Quick capture preset. Choose Custom to enter width, height, and fps manually.")]
        [SerializeField]
        private CapturePreset _capturePreset = CapturePreset.Balanced;

        [Tooltip("Capture width in pixels. Used only when Capture Preset is Custom.")] [SerializeField]
        private int _captureWidth;

        [Tooltip("Capture height in pixels. Used only when Capture Preset is Custom.")] [SerializeField]
        private int _captureHeight;

        [Tooltip("Target frames per second. Used only when Capture Preset is Custom.")] [SerializeField]
        private int _targetFps;

        [Tooltip("Capture backend selection. Auto uses built-in hooks on the built-in renderer and explicit render compatibility on SRP/URP. Choose SRP Native only when that backend is available in this SDK build.")]
        [SerializeField]
        private CameraCaptureMode _cameraCaptureMode = CameraCaptureMode.Auto;

        [Header("Camera")] [Tooltip("Camera to capture from. Uses Camera.main if not set.")] [SerializeField]
        private Camera _targetCamera;

        [Header("Debug")] [Tooltip("Optional identifier for multi-camera scenarios.")] [SerializeField]
        private string _sourceId = "camera";

        [Tooltip("Enables a synchronous pixel probe for diagnostics. Leave disabled in runtime builds to avoid GPU stalls.")]
        [SerializeField]
        private bool _enableDiagnosticFrameHealthProbe;
#pragma warning restore CS0649

        #endregion

        #region Private Fields

        private RenderTexture _captureTexture;
        private RenderTexture _outputTexture;
        private Texture2D _frameHealthProbeTexture;
        private CommandBuffer _captureCommandBuffer;
        private CaptureMode _captureCommandBufferMode;
        private bool _commandBufferAttached;
        private bool _frameHealthProbeFailed;
        private bool _isFrameReady;
        private bool _usesExplicitRenderPath;
        private bool _suppressRenderHookCallbacks;
        private bool _warnedForRepeatedInvalidContent;
        private float _nextCaptureTime;
        private float _captureStartTime;
        private float _nextFrameHealthProbeTime;
        private int _effectiveWidth;
        private int _effectiveHeight;
        private int _effectiveFps;
        private bool _isInitialized;
        private CaptureMode _activeCaptureMode;
        private ICameraCaptureBackend _captureBackend;
        private VisionSourceState _state = VisionSourceState.Idle;
        private VisionSourceErrorKind _errorKind = VisionSourceErrorKind.None;
        private string _statusMessage;

        private readonly VisionFrameHealthMonitor _frameHealthMonitor =
            new(
                FrameHealthWarningThreshold,
                FrameHealthEscalationThreshold);

        private IEventHub _eventHub;

        #endregion

        #region Public Properties

        public Camera TargetCamera
        {
            get
            {
                if (_targetCamera == null) ResolveCamera();
                return _targetCamera;
            }
            private set => _targetCamera = value;
        }

        public int EffectiveWidth
        {
            get
            {
                EnsureInitialized();
                return _effectiveWidth;
            }
        }

        public int EffectiveHeight
        {
            get
            {
                EnsureInitialized();
                return _effectiveHeight;
            }
        }

        public int EffectiveFps
        {
            get
            {
                EnsureInitialized();
                return _effectiveFps;
            }
        }

        /// <summary>Gets the current frame count since capture started.</summary>
        public long FrameCount { get; private set; }

        public VisionSourceState State => _state;
        public VisionSourceErrorKind ErrorKind => _errorKind;
        public string StatusMessage => _statusMessage;
        public bool HasUsableFrame => IsFrameReady;
        public event Action StatusChanged;

        public string SourceId => _sourceId;

        /// <summary>Gets the current render texture (Y-flipped for correct orientation).</summary>
        public RenderTexture CurrentRenderTexture
        {
            get
            {
                EnsureInitialized();
                return _outputTexture;
            }
        }

        #endregion

        #region IVisionFrameSource Implementation

        /// <inheritdoc />
        public bool IsCapturing { get; private set; }

        /// <inheritdoc />
        public bool IsFrameReady => _isFrameReady && _outputTexture != null;

        /// <inheritdoc />
        public event Action FrameReady;

        /// <inheritdoc />
        (int Width, int Height) IVisionFrameSource.FrameDimensions
        {
            get
            {
                EnsureInitialized();
                return (_effectiveWidth, _effectiveHeight);
            }
        }

        /// <inheritdoc />
        float IVisionFrameSource.TargetFrameRate
        {
            get
            {
                EnsureInitialized();
                return _effectiveFps;
            }
        }

        /// <inheritdoc />
        public void StartCapture()
        {
            EnsureInitialized();
            if (IsCapturing) return;

            _activeCaptureMode = ResolveCaptureMode();
            UpdateStatus(VisionSourceState.Starting);

            if (!CanStartCapture(
                    _activeCaptureMode,
                    out string startFailureMessage,
                    out VisionSourceErrorKind startFailureKind,
                    out string startFailureCode))
            {
                FailStartCapture(startFailureMessage, startFailureKind, startFailureCode);
                return;
            }

            EnsureRenderTargets();
            if (_captureTexture == null || _outputTexture == null)
            {
                FailStartCapture(
                    $"Failed to create capture RenderTextures ({_effectiveWidth}x{_effectiveHeight}). This may indicate GPU memory exhaustion.",
                    VisionSourceErrorKind.InvalidConfiguration,
                    SessionErrorCodes.VisionRenderTextureFailed);
                return;
            }

            if (_activeCaptureMode == CaptureMode.BuiltInHooks)
                EnsureCaptureCommandBuffer();

            IsCapturing = true;
            FrameCount = 0;
            _isFrameReady = false;
            _usesExplicitRenderPath = _activeCaptureMode == CaptureMode.ExplicitRenderCompatibility;
            _frameHealthMonitor.Reset();
            _captureStartTime = Time.unscaledTime;
            _nextCaptureTime = _captureStartTime;
            _nextFrameHealthProbeTime = _captureStartTime + FrameHealthStartupGraceSeconds;
            _frameHealthProbeFailed = false;
            _warnedForRepeatedInvalidContent = false;

            if (_usesExplicitRenderPath)
            {
                StopBackendCaptureHooks();
                ConvaiLogger.Info(
                    "Using explicit camera render compatibility path. This adds a bounded extra camera render at the configured capture FPS.",
                    LogCategory.Vision);
            }
            else
            {
                InitializeCaptureBackend();
                _captureBackend?.Start();
            }

            _eventHub?.Publish(VisionCaptureStarted.Create(
                _effectiveWidth,
                _effectiveHeight,
                _effectiveFps,
                SourceId));

            ConvaiLogger.Info(
                $"Capture started: {_effectiveWidth}x{_effectiveHeight} @ {_effectiveFps}fps using {_activeCaptureMode}",
                LogCategory.Vision);
        }

        /// <inheritdoc />
        public void StopCapture() => StopCapture(VisionCaptureStopReason.UserRequested);

        #endregion

        #region Unity Lifecycle

        private void Awake() => EnsureInitialized();

        private void OnEnable()
        {
            TryResolveDependencies();
            EnsureInitialized();
        }

        private void Update()
        {
            if (!IsCapturing || _usesExplicitRenderPath)
                return;

            float now = Time.unscaledTime;
            bool hooksHealthy = _captureBackend != null &&
                                _captureBackend.HasRecentHookActivity(now, RenderHookTimeoutSeconds);
            if (!hooksHealthy)
            {
                StopCapture(
                    VisionCaptureStopReason.Error,
                    SessionErrorCodes.GetDescription(SessionErrorCodes.VisionRenderTextureTimeout),
                    SessionErrorCodes.VisionRenderTextureTimeout);
            }
        }

        private void LateUpdate()
        {
            if (!IsCapturing || !_usesExplicitRenderPath)
                return;

            if (Time.unscaledTime < _nextCaptureTime)
                return;

            CaptureExplicitRenderFrame();
        }

        private void OnDisable()
        {
            if (IsCapturing)
                StopCapture(VisionCaptureStopReason.ComponentDisabled);
        }

        private void OnDestroy()
        {
            StopBackendCaptureHooks();
            CleanupCommandBuffer();
            CleanupRenderTargets();
        }

        #endregion

        #region Private Methods

        private void EnsureInitialized()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            ResolveCamera();
            ResolveSettings();
        }

        private void ResolveCamera()
        {
            if (_targetCamera != null) return;
            _targetCamera = GetComponent<Camera>();
            if (_targetCamera == null) _targetCamera = Camera.main;
        }

        private void ResolveSettings()
        {
            (int width, int height, int fps) = ResolveCapturePresetSettings();
            _effectiveWidth = width;
            _effectiveHeight = height;
            _effectiveFps = fps;

            ConvaiLogger.Info(
                $"Resolved settings: {_effectiveWidth}x{_effectiveHeight} @ {_effectiveFps}fps ({_capturePreset})",
                LogCategory.Vision);
        }

        private (int Width, int Height, int Fps) ResolveCapturePresetSettings()
        {
            return _capturePreset switch
            {
                CapturePreset.LowOverhead => (640, 480, 10),
                CapturePreset.Balanced => (1280, 720, 15),
                CapturePreset.HighDetail => (1920, 1080, 30),
                CapturePreset.Custom =>
                (
                    Mathf.Max(320, _captureWidth),
                    Mathf.Max(240, _captureHeight),
                    Mathf.Clamp(_targetFps, 1, 30)
                ),
                _ => (1280, 720, 15)
            };
        }

        private void EnsureRenderTargets()
        {
            if (_captureTexture != null && _outputTexture != null)
                return;

            CleanupRenderTargets();
            _captureTexture = CreateRenderTexture();
            _outputTexture = CreateRenderTexture();
        }

        private RenderTexture CreateRenderTexture()
        {
            var rt = new RenderTexture(_effectiveWidth, _effectiveHeight, 24, RenderTextureFormat.ARGB32);
            if (rt.Create())
                return rt;

            Destroy(rt);
            return null;
        }

        private CommandBuffer EnsureCaptureCommandBuffer()
        {
            if (_captureCommandBuffer != null && _captureCommandBufferMode == _activeCaptureMode)
                return _captureCommandBuffer;

            if (_captureCommandBuffer != null)
                CleanupCommandBuffer();

            EnsureRenderTargets();
            if (_outputTexture == null)
                return null;

            _captureCommandBuffer = new CommandBuffer { name = "Convai Vision Capture" };
            _captureCommandBuffer.Blit(
                BuiltinRenderTextureType.CurrentActive,
                new RenderTargetIdentifier(_outputTexture),
                FlipScale,
                FlipOffset);
            _captureCommandBufferMode = _activeCaptureMode;

            return _captureCommandBuffer;
        }

        private void InitializeCaptureBackend()
        {
            StopBackendCaptureHooks();

            _captureBackend = CreateCaptureBackend(_activeCaptureMode);
            if (_captureBackend == null)
                return;

            _captureBackend.Initialize(new CameraCaptureBackendContext
            {
                TargetCamera = TargetCamera,
                ShouldProcessCamera = ShouldProcessRenderHook,
                GetNextCaptureTime = () => _nextCaptureTime,
                GetCurrentTime = () => Time.unscaledTime,
                EnsureCaptureCommandBuffer = EnsureCaptureCommandBuffer,
                AttachCaptureCommandBuffer = AttachCaptureCommandBuffer,
                DetachCaptureCommandBuffer = DetachCaptureCommandBuffer,
                RegisterHookCapture = RegisterHookCapture,
                StopCaptureWithError = StopWithError
            });
        }

        private static ICameraCaptureBackend CreateCaptureBackend(CaptureMode captureMode)
        {
            return captureMode switch
            {
                CaptureMode.BuiltInHooks => new BuiltInCameraCaptureBackend(),
                _ => null
            };
        }

        private void StopBackendCaptureHooks()
        {
            if (_captureBackend != null)
            {
                try
                {
                    _captureBackend.Stop();
                    _captureBackend.Dispose();
                }
                catch
                {
                    // Best effort teardown.
                }

                _captureBackend = null;
            }

            DetachCaptureCommandBuffer();
        }

        private void RegisterHookCapture()
        {
            if (!IsCapturing)
                return;

            RegisterCapturedFrame();
        }

        private void StopWithError(string errorMessage, string errorCode) =>
            StopCapture(VisionCaptureStopReason.Error, errorMessage, errorCode);

        private bool ShouldProcessRenderHook(Camera renderCamera)
        {
            if (!IsCapturing || _suppressRenderHookCallbacks)
                return false;

            if (TargetCamera == null)
                return false;

            return renderCamera == TargetCamera;
        }

        private void AttachCaptureCommandBuffer()
        {
            if (_activeCaptureMode != CaptureMode.BuiltInHooks ||
                _commandBufferAttached ||
                TargetCamera == null ||
                _captureCommandBuffer == null)
                return;

            TargetCamera.AddCommandBuffer(CaptureCameraEvent, _captureCommandBuffer);
            _commandBufferAttached = true;
        }

        private void DetachCaptureCommandBuffer()
        {
            if (!_commandBufferAttached || TargetCamera == null || _captureCommandBuffer == null)
                return;

            TargetCamera.RemoveCommandBuffer(CaptureCameraEvent, _captureCommandBuffer);
            _commandBufferAttached = false;
        }

        private void EnsureFrameHealthProbeTexture()
        {
            if (_frameHealthProbeTexture != null)
                return;

            _frameHealthProbeTexture = new Texture2D(
                HealthProbeTextureSize,
                HealthProbeTextureSize,
                TextureFormat.RGBA32,
                false,
                true);
        }

        private void CaptureExplicitRenderFrame()
        {
            if (TargetCamera == null)
            {
                ConvaiLogger.Error("Camera lost during capture", LogCategory.Vision);
                StopCapture(
                    VisionCaptureStopReason.CameraLost,
                    SessionErrorCodes.GetDescription(SessionErrorCodes.VisionCameraLost),
                    SessionErrorCodes.VisionCameraLost);
                return;
            }

            EnsureRenderTargets();
            if (_captureTexture == null || _outputTexture == null)
            {
                ConvaiLogger.Error("Failed to create RenderTexture", LogCategory.Vision);
                StopCapture(
                    VisionCaptureStopReason.Error,
                    SessionErrorCodes.GetDescription(SessionErrorCodes.VisionRenderTextureFailed),
                    SessionErrorCodes.VisionRenderTextureFailed);
                return;
            }

            RenderTexture previousTarget = TargetCamera.targetTexture;
            DetachCaptureCommandBuffer();
            _suppressRenderHookCallbacks = true;

            try
            {
                TargetCamera.targetTexture = _captureTexture;
                TargetCamera.Render();
                Graphics.Blit(_captureTexture, _outputTexture, FlipScale, FlipOffset);
                RegisterCapturedFrame();
            }
            finally
            {
                TargetCamera.targetTexture = previousTarget;
                _suppressRenderHookCallbacks = false;
            }
        }

        private void RegisterCapturedFrame()
        {
            float now = Time.unscaledTime;

            FrameCount++;
            _nextCaptureTime = now + (1f / _effectiveFps);

            VisionFrameHealthSample healthSample = EvaluateFrameHealth(now);
            _isFrameReady = healthSample.IsHealthy;

            if (healthSample.ShouldWarn)
            {
                ConvaiLogger.Warning(
                    $"Frame-health probe reported consecutive invalid sampled frames ({healthSample.ConsecutiveInvalidFrames}) while using explicit camera render capture.",
                    LogCategory.Vision);
            }

            if (healthSample.ShouldWarn && _usesExplicitRenderPath && !_warnedForRepeatedInvalidContent)
            {
                _warnedForRepeatedInvalidContent = true;
                ConvaiLogger.Warning(
                    "Explicit camera render capture remains invalid. Holding readiness until a valid sampled frame arrives.",
                    LogCategory.Vision);
            }

            if (healthSample.IsHealthy)
                _warnedForRepeatedInvalidContent = false;

            if (healthSample.ShouldEscalateError && _usesExplicitRenderPath)
            {
                StopCapture(
                    VisionCaptureStopReason.Error,
                    SessionErrorCodes.GetDescription(SessionErrorCodes.VisionRenderTextureTimeout),
                    SessionErrorCodes.VisionRenderTextureTimeout);
                return;
            }

            if (!_isFrameReady)
            {
                if (_usesExplicitRenderPath)
                    UpdateStatus(VisionSourceState.Degraded, message: "Captured frame not yet usable.");
                return;
            }

            UpdateStatus(VisionSourceState.Ready);

            _eventHub?.Publish(VisionFrameCaptured.Create(
                _effectiveWidth,
                _effectiveHeight,
                FrameCount,
                0,
                SourceId));

            FrameReady?.Invoke();
        }

        private void StopCapture(VisionCaptureStopReason reason, string errorMessage = null, string errorCode = null)
        {
            if (!IsCapturing) return;

            IsCapturing = false;
            _isFrameReady = false;
            _usesExplicitRenderPath = false;
            _suppressRenderHookCallbacks = false;
            _warnedForRepeatedInvalidContent = false;
            _frameHealthMonitor.Reset();

            StopBackendCaptureHooks();

            _eventHub?.Publish(VisionCaptureStopped.Create(
                FrameCount,
                reason,
                SourceId,
                errorMessage,
                errorCode));

            if (errorCode != null)
            {
                UpdateStatus(VisionSourceState.Failed, MapErrorKind(errorCode), errorMessage);
                ConvaiLogger.Error(
                    $"Capture stopped with error: {FrameCount} frames, reason: {reason}, errorCode: {errorCode}",
                    LogCategory.Vision);
            }
            else
            {
                UpdateStatus(VisionSourceState.Stopped);
                ConvaiLogger.Info($"Capture stopped: {FrameCount} frames, reason: {reason}",
                    LogCategory.Vision);
            }

            _activeCaptureMode = CaptureMode.None;
        }

        private CaptureMode ResolveCaptureMode()
        {
            bool usesBuiltInPipeline =
                GraphicsSettings.currentRenderPipeline == null &&
                GraphicsSettings.defaultRenderPipeline == null;

            return _cameraCaptureMode switch
            {
                CameraCaptureMode.Auto => usesBuiltInPipeline
                    ? CaptureMode.BuiltInHooks
                    : CaptureMode.ExplicitRenderCompatibility,
                CameraCaptureMode.BuiltInHooks => CaptureMode.BuiltInHooks,
                CameraCaptureMode.SrpNative => CaptureMode.SrpNative,
                CameraCaptureMode.ExplicitRenderCompatibility => CaptureMode.ExplicitRenderCompatibility,
                _ => usesBuiltInPipeline
                    ? CaptureMode.BuiltInHooks
                    : CaptureMode.ExplicitRenderCompatibility
            };
        }

        private bool CanStartCapture(
            CaptureMode captureMode,
            out string failureMessage,
            out VisionSourceErrorKind failureKind,
            out string failureCode)
        {
            if (TargetCamera == null)
            {
                failureMessage = SessionErrorCodes.GetDescription(SessionErrorCodes.VisionCameraLost);
                failureKind = VisionSourceErrorKind.InvalidConfiguration;
                failureCode = SessionErrorCodes.VisionCameraNotFound;
                return false;
            }

            if (captureMode == CaptureMode.SrpNative)
            {
                failureMessage =
                    "SRP-native capture backend not available in this SDK build. Select ExplicitRenderCompatibility to opt into the extra-render path.";
                failureKind = VisionSourceErrorKind.UnsupportedPlatform;
                failureCode = SessionErrorCodes.VisionUnknown;
                return false;
            }

            failureMessage = null;
            failureKind = VisionSourceErrorKind.None;
            failureCode = null;
            return true;
        }

        private void FailStartCapture(string errorMessage, VisionSourceErrorKind errorKind, string errorCode)
        {
            UpdateStatus(VisionSourceState.Failed, errorKind, errorMessage);
            string message = $"[CameraVisionFrameSource] {errorMessage}";
            ConvaiLogger.Error(message, LogCategory.Vision);
            if (ConvaiLogger.SinkCount == 0)
                UnityEngine.Debug.LogError(message);
            _eventHub?.Publish(VisionCaptureStopped.Create(
                0,
                VisionCaptureStopReason.Error,
                SourceId,
                errorMessage,
                errorCode));
            _activeCaptureMode = CaptureMode.None;
        }

        private static VisionSourceErrorKind MapErrorKind(string errorCode)
        {
            return errorCode switch
            {
                SessionErrorCodes.VisionPermissionDenied => VisionSourceErrorKind.PermissionDenied,
                SessionErrorCodes.VisionDeviceNotFound => VisionSourceErrorKind.DeviceUnavailable,
                SessionErrorCodes.VisionCameraLost => VisionSourceErrorKind.DeviceUnavailable,
                SessionErrorCodes.VisionRenderTextureFailed => VisionSourceErrorKind.InvalidConfiguration,
                SessionErrorCodes.VisionRenderTextureTimeout => VisionSourceErrorKind.Timeout,
                _ => VisionSourceErrorKind.Unknown
            };
        }

        private void UpdateStatus(
            VisionSourceState state,
            VisionSourceErrorKind errorKind = VisionSourceErrorKind.None,
            string message = null)
        {
            if (_state == state && _errorKind == errorKind && _statusMessage == message)
                return;

            _state = state;
            _errorKind = errorKind;
            _statusMessage = message;
            StatusChanged?.Invoke();
        }

        private VisionFrameHealthSample EvaluateFrameHealth(float now)
        {
            if (_outputTexture == null || !_outputTexture.IsCreated())
                return _frameHealthMonitor.RegisterSample(false);

            if (now < _captureStartTime + FrameHealthStartupGraceSeconds)
                return _frameHealthMonitor.RegisterSample(true);

            bool hooksHealthy = _captureBackend != null &&
                                _captureBackend.HasRecentHookActivity(now, RenderHookTimeoutSeconds);
            if (hooksHealthy && !_usesExplicitRenderPath)
                return _frameHealthMonitor.RegisterSample(true);

            if (_frameHealthProbeFailed)
                return _frameHealthMonitor.RegisterSample(true);

            if (!_enableDiagnosticFrameHealthProbe)
                return _frameHealthMonitor.RegisterSample(true);

            if (now + Mathf.Epsilon < _nextFrameHealthProbeTime)
                return _frameHealthMonitor.RegisterSample(null);

            _nextFrameHealthProbeTime = now + FrameHealthProbeIntervalSeconds;
            return _frameHealthMonitor.RegisterSample(SampleFrameUsability());
        }

        private bool SampleFrameUsability()
        {
            if (_outputTexture == null || !_outputTexture.IsCreated())
                return false;

            if (_frameHealthProbeFailed)
                return true;

            EnsureFrameHealthProbeTexture();
            if (_frameHealthProbeTexture == null)
                return false;

            RenderTexture previousActive = RenderTexture.active;
            try
            {
                int sampleSize = Mathf.Min(HealthProbeTextureSize, _outputTexture.width, _outputTexture.height);
                int startX = Mathf.Max(0, (_outputTexture.width - sampleSize) / 2);
                int startY = Mathf.Max(0, (_outputTexture.height - sampleSize) / 2);

                RenderTexture.active = _outputTexture;
                _frameHealthProbeTexture.ReadPixels(new Rect(startX, startY, sampleSize, sampleSize), 0, 0, false);
                _frameHealthProbeTexture.Apply(false, false);

                Color32[] pixels = _frameHealthProbeTexture.GetPixels32();
                byte maxRgb = 0;
                byte maxAlpha = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    byte pixelRgbMax = (byte)Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                    if (pixelRgbMax > maxRgb)
                        maxRgb = pixelRgbMax;
                    if (pixel.a > maxAlpha)
                        maxAlpha = pixel.a;
                }

                return maxRgb >= FrameHealthColorThreshold || maxAlpha >= FrameHealthAlphaThreshold;
            }
            catch (Exception ex)
            {
                _frameHealthProbeFailed = true;
                ConvaiLogger.Warning(
                    $"Frame-health pixel probe failed; falling back to texture existence checks. {ex.Message}",
                    LogCategory.Vision);
                return _outputTexture != null;
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }

        private void CleanupCommandBuffer()
        {
            DetachCaptureCommandBuffer();

            if (_captureCommandBuffer == null)
                return;

            _captureCommandBuffer.Release();
            _captureCommandBuffer = null;
            _captureCommandBufferMode = CaptureMode.None;
        }

        private void CleanupRenderTargets()
        {
            if (_captureTexture != null)
            {
                ReleaseAndDestroy(_captureTexture);
                _captureTexture = null;
            }

            if (_outputTexture != null)
            {
                ReleaseAndDestroy(_outputTexture);
                _outputTexture = null;
            }

            if (_frameHealthProbeTexture != null)
            {
                Destroy(_frameHealthProbeTexture);
                _frameHealthProbeTexture = null;
            }
        }

        private static void ReleaseAndDestroy(RenderTexture renderTexture)
        {
            if (renderTexture == null)
                return;

            if (renderTexture.IsCreated())
                renderTexture.Release();

            Destroy(renderTexture);
        }

        #endregion
    }
}
