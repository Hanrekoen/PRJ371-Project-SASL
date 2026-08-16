using System;
using System.Threading.Tasks;
using Convai.Domain.Errors;
using Convai.Domain.Logging;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Logging;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Convai.Runtime.Vision.Sources
{
    /// <summary>
    ///     Vision frame source that captures from a physical webcam device.
    ///     Implements <see cref="IVisionFrameSource" /> to integrate with the SDK's video publishing system.
    /// </summary>
    /// <remarks>
    ///     Use this component to stream frames from a physical webcam device.
    ///     <para>
    ///         <b>Setup Instructions:</b>
    ///         <list type="number">
    ///             <item>Add this component to a GameObject</item>
    ///             <item>Add ConvaiVisionPublisher to the same GameObject (or assign in inspector)</item>
    ///             <item>ConvaiVisionPublisher will auto-discover this component and stream the webcam</item>
    ///             <item>Optionally add VisionDebugPreview to show the webcam feed on screen</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Platform Notes:</b>
    ///         <list type="bullet">
    ///             <item>Android/iOS: Camera permission must be granted</item>
    ///             <item>WebGL: Not supported (AsyncGPUReadback not available)</item>
    ///             <item>macOS: May require Camera access in System Preferences</item>
    ///         </list>
    ///     </para>
    /// </remarks>
    [MovedFrom(true, "Convai.Runtime.Vision", "Convai.Runtime",
        "WebcamVisionFrameSource")]
    [AddComponentMenu("Convai/Vision/Webcam Vision Frame Source")]
    public class WebcamVisionFrameSource : MonoBehaviour, IVisionFrameSource, IVisionFrameSourceStatusProvider
    {
        #region Public Properties

        public static WebCamDevice[] AvailableDevices => WebCamTexture.devices;

        #endregion

        #region Permission Handling

        private async Task<bool> CheckWebcamPermissionAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                UpdateStatus(VisionSourceState.AwaitingPermission, message: "Awaiting camera permission.");
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                await Task.Delay(500);
                bool granted = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera);
                if (granted)
                    UpdateStatus(VisionSourceState.Starting);
                return granted;
            }
            return true;
#elif UNITY_IOS && !UNITY_EDITOR
            if (!UnityEngine.Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                UpdateStatus(VisionSourceState.AwaitingPermission, message: "Awaiting camera permission.");
                await UnityEngine.Application.RequestUserAuthorization(UserAuthorization.WebCam);
                bool granted = UnityEngine.Application.HasUserAuthorization(UserAuthorization.WebCam);
                if (granted)
                    UpdateStatus(VisionSourceState.Starting);
                return granted;
            }
            return true;
#else
            await Task.CompletedTask;
            return true;
#endif
        }

        #endregion

        #region Inspector Fields

        [Header("Webcam Settings")]
        [Tooltip("Name of the webcam device. Leave empty to use the default camera.")]
        [SerializeField]
        private string _webcamDeviceName = "";

        [Tooltip("Requested capture width. Actual may differ based on device capabilities.")] [SerializeField]
        private int _requestedWidth = 640;

        [Tooltip("Requested capture height. Actual may differ based on device capabilities.")] [SerializeField]
        private int _requestedHeight = 480;

        [Tooltip("Requested frame rate. Actual may differ based on device capabilities.")] [SerializeField]
        private int _requestedFps = 15;

        [Tooltip("Maximum output width. 0 keeps the webcam texture width.")]
        [SerializeField]
        private int _maxOutputWidth = 1280;

        [Tooltip("Maximum output height. 0 keeps the webcam texture height.")]
        [SerializeField]
        private int _maxOutputHeight = 720;

        [Header("Source Identification")] [Tooltip("Unique identifier for this vision source")] [SerializeField]
        private string _sourceId = "webcam";

        #endregion

        #region Private Fields

        private WebCamTexture _webCamTexture;
        private bool _isCapturing;
        private bool _isFrameReady;
        private bool _isInitializing;
        private float _nextCaptureTime;
        private VisionSourceState _state = VisionSourceState.Idle;
        private VisionSourceErrorKind _errorKind = VisionSourceErrorKind.None;
        private string _statusMessage;

        #endregion

        #region Error Properties

        /// <summary>
        ///     Gets the last error code if capture failed.
        /// </summary>
        public string LastErrorCode { get; private set; }

        /// <summary>
        ///     Gets the last error message if capture failed.
        /// </summary>
        public string LastErrorMessage { get; private set; }

        #endregion

        #region IVisionFrameSource Implementation

        /// <inheritdoc />
        public bool IsCapturing => _isCapturing && _webCamTexture != null && _webCamTexture.isPlaying;

        /// <inheritdoc />
        public bool IsFrameReady => _isFrameReady && CurrentRenderTexture != null;

        /// <inheritdoc />
        public long FrameCount { get; private set; }

        /// <inheritdoc />
        public (int Width, int Height) FrameDimensions =>
            CurrentRenderTexture != null
                ? (CurrentRenderTexture.width, CurrentRenderTexture.height)
                : (_requestedWidth, _requestedHeight);

        /// <inheritdoc />
        public float TargetFrameRate => Mathf.Max(1, _requestedFps);

        /// <inheritdoc />
        public string SourceId => _sourceId;

        /// <inheritdoc />
        public RenderTexture CurrentRenderTexture { get; private set; }

        public VisionSourceState State => _state;
        public VisionSourceErrorKind ErrorKind => _errorKind;
        public string StatusMessage => _statusMessage;
        public bool HasUsableFrame => IsFrameReady;

        /// <inheritdoc />
        public event Action FrameReady;
        public event Action StatusChanged;

        /// <inheritdoc />
        public void StartCapture()
        {
            if (_isCapturing || _isInitializing) return;
            _ = StartCaptureAsync();
        }

        /// <inheritdoc />
        public void StopCapture() => StopWebcam();

        #endregion

        #region Unity Lifecycle

        private void OnDestroy() => StopWebcam();

        private void Update()
        {
            if (!IsCapturing || CurrentRenderTexture == null) return;
            if (!ShouldCaptureFrameAt(Time.unscaledTime)) return;

            if (BlitWebcamToRenderTexture())
                _nextCaptureTime = Time.unscaledTime + (1f / TargetFrameRate);
        }

        #endregion

        #region Webcam Management

        /// <summary>
        ///     Starts the webcam capture asynchronously.
        /// </summary>
        private async Task StartCaptureAsync()
        {
            if (_isInitializing) return;
            _isInitializing = true;
            UpdateStatus(VisionSourceState.Starting);

            try
            {
                ConvaiLogger.Info("Starting webcam capture...", LogCategory.Vision);

                if (!await CheckWebcamPermissionAsync())
                {
                    LastErrorCode = SessionErrorCodes.VisionPermissionDenied;
                    LastErrorMessage = SessionErrorCodes.GetDescription(SessionErrorCodes.VisionPermissionDenied);
                    UpdateStatus(VisionSourceState.Failed, VisionSourceErrorKind.PermissionDenied, LastErrorMessage);
                    ConvaiLogger.Error($"{LastErrorMessage}", LogCategory.Vision);
                    return;
                }

                if (!InitializeWebcam()) return;

                await WaitForWebcamToStartAsync();

                EnsureRenderTexture(
                    _webCamTexture.width,
                    _webCamTexture.height,
                    _webCamTexture.videoRotationAngle);

                FrameCount = 0;
                _isFrameReady = false;
                _isCapturing = true;
                _nextCaptureTime = 0f;
                LastErrorCode = null;
                LastErrorMessage = null;
                UpdateStatus(VisionSourceState.Starting);
                ConvaiLogger.Info(
                    $"Webcam capture started: {FrameDimensions.Width}x{FrameDimensions.Height}",
                    LogCategory.Vision);
            }
            catch (Exception ex)
            {
                LastErrorCode = SessionErrorCodes.VisionUnknown;
                LastErrorMessage = ex.Message;
                UpdateStatus(VisionSourceState.Failed, VisionSourceErrorKind.Unknown, ex.Message);
                ConvaiLogger.Error($"Error starting webcam: {ex.Message}",
                    LogCategory.Vision);
                _isCapturing = false;
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private bool InitializeWebcam()
        {
            string deviceName = string.IsNullOrEmpty(_webcamDeviceName) ? null : _webcamDeviceName;

            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                LastErrorCode = SessionErrorCodes.VisionDeviceNotFound;
                LastErrorMessage = SessionErrorCodes.GetDescription(SessionErrorCodes.VisionDeviceNotFound);
                UpdateStatus(VisionSourceState.Failed, VisionSourceErrorKind.DeviceUnavailable, LastErrorMessage);
                ConvaiLogger.Error($"{LastErrorMessage}", LogCategory.Vision);
                return false;
            }

            ConvaiLogger.Info($"Found {devices.Length} webcam device(s):",
                LogCategory.Vision);
            foreach (WebCamDevice device in devices)
                ConvaiLogger.Info($"  - {device.name} (Front: {device.isFrontFacing})", LogCategory.Vision);

            _webCamTexture = deviceName != null
                ? new WebCamTexture(deviceName, _requestedWidth, _requestedHeight, Mathf.RoundToInt(TargetFrameRate))
                : new WebCamTexture(_requestedWidth, _requestedHeight, Mathf.RoundToInt(TargetFrameRate));

            _webCamTexture.Play();

            ConvaiLogger.Info($"Starting webcam: {_webCamTexture.deviceName}",
                LogCategory.Vision);
            return true;
        }

        private async Task WaitForWebcamToStartAsync()
        {
            int waitMs = 0;
            while (!_webCamTexture.didUpdateThisFrame && waitMs < 5000)
            {
                await Task.Delay(100);
                waitMs += 100;
            }

            if (!_webCamTexture.didUpdateThisFrame)
            {
                LastErrorCode = SessionErrorCodes.VisionDeviceInitTimeout;
                LastErrorMessage = SessionErrorCodes.GetDescription(SessionErrorCodes.VisionDeviceInitTimeout);
                UpdateStatus(VisionSourceState.Degraded, VisionSourceErrorKind.Timeout, LastErrorMessage);
                ConvaiLogger.Warning($"{LastErrorMessage}", LogCategory.Vision);
            }

            ConvaiLogger.Info(
                $"Webcam active: {_webCamTexture.width}x{_webCamTexture.height} @ rotation {_webCamTexture.videoRotationAngle}°",
                LogCategory.Vision);
        }

        private bool ShouldCaptureFrameAt(float now) => now + Mathf.Epsilon >= _nextCaptureTime;

        private void EnsureRenderTexture(int sourceWidth, int sourceHeight, int rotationAngle)
        {
            (int outputWidth, int outputHeight) = ResolveOutputDimensions(sourceWidth, sourceHeight, rotationAngle);

            if (CurrentRenderTexture != null &&
                CurrentRenderTexture.width == outputWidth &&
                CurrentRenderTexture.height == outputHeight)
            {
                return;
            }

            ReleaseRenderTexture();

            CurrentRenderTexture = new RenderTexture(outputWidth, outputHeight, 24, RenderTextureFormat.ARGB32);
            CurrentRenderTexture.Create();

            ConvaiLogger.Info($"RenderTexture created: {outputWidth}x{outputHeight}",
                LogCategory.Vision);
        }

        private (int Width, int Height) ResolveOutputDimensions(int sourceWidth, int sourceHeight, int rotationAngle)
        {
            int outputWidth = Mathf.Max(1, sourceWidth);
            int outputHeight = Mathf.Max(1, sourceHeight);

            if (rotationAngle == 90 || rotationAngle == 270)
                (outputWidth, outputHeight) = (outputHeight, outputWidth);

            if (_maxOutputWidth > 0 || _maxOutputHeight > 0)
            {
                float widthScale = _maxOutputWidth > 0 ? (float)_maxOutputWidth / outputWidth : float.PositiveInfinity;
                float heightScale = _maxOutputHeight > 0 ? (float)_maxOutputHeight / outputHeight : float.PositiveInfinity;
                float scale = Mathf.Min(widthScale, heightScale, 1f);

                outputWidth = Mathf.Max(1, Mathf.RoundToInt(outputWidth * scale));
                outputHeight = Mathf.Max(1, Mathf.RoundToInt(outputHeight * scale));
            }

            return (outputWidth, outputHeight);
        }

        private bool BlitWebcamToRenderTexture()
        {
            if (_webCamTexture == null || !_webCamTexture.didUpdateThisFrame) return false;

            FrameCount++;

            float rotation = _webCamTexture.videoRotationAngle;
            bool verticallyMirrored = _webCamTexture.videoVerticallyMirrored;

            bool needsYFlip = !verticallyMirrored;
            var scale = new Vector2(1, needsYFlip ? -1 : 1);
            var offset = new Vector2(0, needsYFlip ? 1 : 0);

            if (Mathf.Approximately(rotation, 0f))
                Graphics.Blit(_webCamTexture, CurrentRenderTexture, scale, offset);
            else
                BlitWithRotation(_webCamTexture, CurrentRenderTexture, rotation, needsYFlip);

            _isFrameReady = true;
            UpdateStatus(VisionSourceState.Ready);
            FrameReady?.Invoke();
            return true;
        }

        private void BlitWithRotation(Texture source, RenderTexture dest, float rotationDegrees, bool flipVertical)
        {
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = dest;

            GL.Clear(true, true, Color.black);

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, dest.width, dest.height, 0);

            float halfWidth = dest.width * 0.5f;
            float halfHeight = dest.height * 0.5f;

            Matrix4x4 translation1 = Matrix4x4.Translate(new Vector3(-halfWidth, -halfHeight, 0));
            Matrix4x4 rotation = Matrix4x4.Rotate(Quaternion.Euler(0, 0, -rotationDegrees));
            Matrix4x4 translation2 = Matrix4x4.Translate(new Vector3(halfWidth, halfHeight, 0));
            Matrix4x4 flip = flipVertical ? Matrix4x4.Scale(new Vector3(1, -1, 1)) : Matrix4x4.identity;

            Matrix4x4 matrix4X4 = translation2 * rotation * translation1 * flip;
            GL.MultMatrix(matrix4X4);

            Graphics.DrawTexture(new Rect(0, 0, dest.width, dest.height), source);

            GL.PopMatrix();
            RenderTexture.active = prevActive;
        }

        /// <summary>
        ///     Stops the webcam capture and releases resources.
        /// </summary>
        private void StopWebcam()
        {
            _isCapturing = false;
            _isFrameReady = false;
            _nextCaptureTime = 0f;

            if (_webCamTexture != null)
            {
                _webCamTexture.Stop();
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }

            ReleaseRenderTexture();
            UpdateStatus(VisionSourceState.Stopped);

            ConvaiLogger.Info("Webcam stopped", LogCategory.Vision);
        }

        private void ReleaseRenderTexture()
        {
            if (CurrentRenderTexture == null)
                return;

            CurrentRenderTexture.Release();
            Destroy(CurrentRenderTexture);
            CurrentRenderTexture = null;
        }

        private void OnValidate()
        {
            _requestedWidth = Mathf.Max(1, _requestedWidth);
            _requestedHeight = Mathf.Max(1, _requestedHeight);
            _requestedFps = Mathf.Max(1, _requestedFps);
            _maxOutputWidth = Mathf.Max(0, _maxOutputWidth);
            _maxOutputHeight = Mathf.Max(0, _maxOutputHeight);
        }

        #endregion

        #region Public API

        /// <summary>
        ///     Switches to a different webcam device.
        /// </summary>
        /// <param name="deviceName">Name of the webcam device to switch to.</param>
        public IConvaiOperation<Unit> SwitchWebcamAsync(string deviceName) =>
            ConvaiOperation<Unit>.FromTask(SwitchWebcamAsyncCore(deviceName));

        private async Task<Unit> SwitchWebcamAsyncCore(string deviceName)
        {
            bool wasCapturing = _isCapturing;

            StopWebcam();

            _webcamDeviceName = deviceName;

            if (wasCapturing) await StartCaptureAsync();
            return Unit.Value;
        }

        /// <summary>
        ///     Gets available webcam device names for UI dropdown population.
        /// </summary>
        public static string[] GetAvailableDeviceNames()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            string[] names = new string[devices.Length];
            for (int i = 0; i < devices.Length; i++) names[i] = devices[i].name;
            return names;
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

        #endregion
    }
}
