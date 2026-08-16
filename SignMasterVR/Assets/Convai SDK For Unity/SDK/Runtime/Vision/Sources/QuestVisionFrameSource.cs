using System;
using System.Reflection;
using Convai.Runtime.Actions;
using Convai.Runtime.Vision.Sources;
using UnityEngine;
namespace Convai.Runtime.Vision.Sources
{
    /// <summary>
    ///     Vision frame source that captures the Meta Quest passthrough camera feed via
    ///     the <c>PassthroughCameraAccess</c> component (Meta XR Passthrough Camera API Samples).
    ///     Implements <see cref="IVisionFrameSource" /> to integrate with the SDK's video publishing system.
    /// </summary>
    /// <remarks>
    ///     Use this component to stream the Quest's real-world passthrough camera texture to Convai,
    ///     enabling vision features (e.g. scene understanding) on Meta Quest headsets.
    ///     The component reflects into <c>PassthroughCameraAccess</c> at runtime, so it does not require
    ///     a hard reference to the Meta SDK assemblies.
    ///     <para>
    ///         <b>Setup Instructions:</b>
    ///         <list type="number">
    ///             <item>Import the Meta XR SDK and add a <c>PassthroughCameraAccess</c> component to your scene</item>
    ///             <item>Add this component to a GameObject (optionally drag the <c>PassthroughCameraAccess</c> reference, otherwise it is auto-discovered)</item>
    ///             <item>Add ConvaiVisionPublisher to the same GameObject (or assign in inspector)</item>
    ///             <item>ConvaiVisionPublisher will auto-discover this component and stream the passthrough feed</item>
    ///             <item>Optionally add VisionDebugPreview to display the captured frames on screen</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Platform Notes:</b>
    ///         <list type="bullet">
    ///             <item>Meta Quest only: requires Horizon OS with Passthrough Camera API access (Quest 3 / Quest 3S)</item>
    ///             <item>The <c>horizonos.permission.HEADSET_CAMERA</c> and <c>android.permission.CAMERA</c> permissions must be granted to the app</item>
    ///             <item>Editor / non-Quest platforms: capture will not produce frames (no PassthroughCameraAccess available)</item>
    ///         </list>
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Vision/Quest Vision Frame Source")]
    public class QuestVisionFrameSource : MonoBehaviour, IVisionFrameSource, IVisionFrameSourceStatusProvider
    {
        private const int MaxBindSearchAttemptsPerCapture = 3;
        private const float BindRetryIntervalSeconds = 1f;

        [Tooltip("Optional explicit PassthroughCameraAccess component. Leave empty to auto-find one in the scene.")]
        [SerializeField]
        [ConvaiInspectorSection("Quest Camera Access")]
        private MonoBehaviour _passthroughCameraAccess;

        [Tooltip("Unique identifier for this source.")]
        [SerializeField]
        [ConvaiInspectorSection("Output Settings")]
        private string _sourceId = "quest-passthrough";

        [Tooltip("Maximum output width. 0 keeps the source texture width.")]
        [SerializeField]
        [ConvaiInspectorSection("Output Settings")]
        private int _maxOutputWidth = 1280;

        [Tooltip("Maximum output height. 0 keeps the source texture height.")]
        [SerializeField]
        [ConvaiInspectorSection("Output Settings")]
        private int _maxOutputHeight = 720;

        [Tooltip("Target capture rate used by publishers.")]
        [SerializeField]
        [ConvaiInspectorSection("Output Settings")]
        private int _targetFrameRate = 15;

        [Tooltip("Flips the Quest camera texture vertically so the published RenderTexture is top-down.")]
        [SerializeField]
        [ConvaiInspectorSection("Output Settings")]
        private bool _flipY = true;

        private MethodInfo _getTextureMethod;
        private PropertyInfo _isPlayingProperty;
        private Func<Texture> _getTexture;
        private Func<bool> _getIsPlaying;
        private bool _isCapturing;
        private bool _isFrameReady;
        private float _nextCaptureTime;
        private float _nextBindAttemptTime;
        private int _bindSearchAttemptsRemaining;
        private VisionSourceState _state = VisionSourceState.Idle;
        private VisionSourceErrorKind _errorKind = VisionSourceErrorKind.None;
        private string _statusMessage;

        public bool IsCapturing =>
            _isCapturing &&
            _passthroughCameraAccess != null &&
            (_getTexture != null || _getTextureMethod != null);
        public long FrameCount { get; private set; }
        public (int Width, int Height) FrameDimensions =>
            CurrentRenderTexture != null
                ? (CurrentRenderTexture.width, CurrentRenderTexture.height)
                : (0, 0);
        public float TargetFrameRate => Mathf.Max(1, _targetFrameRate);
        public string SourceId => _sourceId;
        public RenderTexture CurrentRenderTexture { get; private set; }
        public bool IsFrameReady => _isFrameReady && CurrentRenderTexture != null;
        public VisionSourceState State => _state;
        public VisionSourceErrorKind ErrorKind => _errorKind;
        public string StatusMessage => _statusMessage;
        public bool HasUsableFrame => IsFrameReady;
        public event Action FrameReady;
        public event Action StatusChanged;

        private void Awake()
        {
            BindPassthroughAccess(forceSearch: _passthroughCameraAccess == null);
        }

        private void OnDestroy()
        {
            StopCapture();
        }

        public void StartCapture()
        {
            if (_isCapturing)
            {
                return;
            }

            UpdateStatus(VisionSourceState.Starting);
            BindPassthroughAccess(forceSearch: _passthroughCameraAccess == null);
            if (_passthroughCameraAccess == null || (_getTexture == null && _getTextureMethod == null))
            {
                _isCapturing = false;
                UpdateStatus(
                    VisionSourceState.Failed,
                    VisionSourceErrorKind.InvalidConfiguration,
                    "PassthroughCameraAccess not found or not readable. Assign a valid component before starting capture.");
                return;
            }

            _isCapturing = true;
            _isFrameReady = false;
            _nextCaptureTime = 0f;
            _nextBindAttemptTime = 0f;
            _bindSearchAttemptsRemaining = MaxBindSearchAttemptsPerCapture;
            FrameCount = 0;
        }

        public void StopCapture()
        {
            _isCapturing = false;
            _isFrameReady = false;
            _nextCaptureTime = 0f;
            _nextBindAttemptTime = 0f;
            _bindSearchAttemptsRemaining = 0;

            ReleaseRenderTexture();
            UpdateStatus(VisionSourceState.Stopped);
        }

        private void Update()
        {
            if (!_isCapturing)
            {
                return;
            }

            if (!HasReadableFrameSource())
            {
                if (_bindSearchAttemptsRemaining > 0 && Time.unscaledTime >= _nextBindAttemptTime)
                {
                    _bindSearchAttemptsRemaining--;
                    _nextBindAttemptTime = Time.unscaledTime + BindRetryIntervalSeconds;
                    BindPassthroughAccess(forceSearch: true);
                }
                else if (_bindSearchAttemptsRemaining <= 0)
                {
                    UpdateStatus(
                        VisionSourceState.Failed,
                        VisionSourceErrorKind.DeviceUnavailable,
                        "Quest passthrough source stopped producing readable frames.");
                }

                return;
            }

            if (Time.unscaledTime < _nextCaptureTime)
            {
                return;
            }

            Texture sourceTexture = GetSourceTexture();
            if (sourceTexture == null || sourceTexture.width <= 0 || sourceTexture.height <= 0)
            {
                return;
            }

            EnsureRenderTexture(sourceTexture.width, sourceTexture.height);

            if (CurrentRenderTexture == null)
            {
                return;
            }

            if (_flipY)
            {
                Graphics.Blit(sourceTexture, CurrentRenderTexture, new Vector2(1f, -1f), new Vector2(0f, 1f));
            }
            else
            {
                Graphics.Blit(sourceTexture, CurrentRenderTexture);
            }

            FrameCount++;
            _isFrameReady = true;
            UpdateStatus(VisionSourceState.Ready);
            FrameReady?.Invoke();

            _nextCaptureTime = Time.unscaledTime + (1f / TargetFrameRate);
        }

        private bool HasReadableFrameSource()
        {
            if (_passthroughCameraAccess == null || (_getTexture == null && _getTextureMethod == null))
            {
                return false;
            }

            if (_getIsPlaying != null)
            {
                try
                {
                    return _getIsPlaying();
                }
                catch
                {
                    return false;
                }
            }

            if (_isPlayingProperty == null)
            {
                return true;
            }

            try
            {
                object isPlayingValue = _isPlayingProperty.GetValue(_passthroughCameraAccess);
                return isPlayingValue is bool isPlaying && isPlaying;
            }
            catch
            {
                return false;
            }
        }

        private Texture GetSourceTexture()
        {
            if (_passthroughCameraAccess == null || (_getTexture == null && _getTextureMethod == null))
            {
                return null;
            }

            try
            {
                if (_getTexture != null)
                {
                    return _getTexture();
                }

                return _getTextureMethod.Invoke(_passthroughCameraAccess, null) as Texture;
            }
            catch
            {
                return null;
            }
        }

        private void BindPassthroughAccess(bool forceSearch)
        {
            if (_passthroughCameraAccess != null && !forceSearch)
            {
                CacheReflectionMembers(_passthroughCameraAccess.GetType());
                if (_getTextureMethod != null)
                {
                    return;
                }
            }

            MonoBehaviour resolved = FindPassthroughCameraAccess();
            if (resolved == null)
            {
                return;
            }

            _passthroughCameraAccess = resolved;
            CacheReflectionMembers(resolved.GetType());
        }

        private static MonoBehaviour FindPassthroughCameraAccess()
        {
            MonoBehaviour[] behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null ||
                    !behaviour.gameObject.scene.IsValid() ||
                    !behaviour.isActiveAndEnabled ||
                    !behaviour.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Type type = behaviour.GetType();
                if (type.Name == "PassthroughCameraAccess" && IsReadableCandidate(behaviour, type))
                {
                    return behaviour;
                }
            }

            return null;
        }

        private void CacheReflectionMembers(Type type)
        {
            _getTextureMethod = type.GetMethod("GetTexture", BindingFlags.Instance | BindingFlags.Public);
            _isPlayingProperty = type.GetProperty("IsPlaying", BindingFlags.Instance | BindingFlags.Public);
            _getTexture = null;
            _getIsPlaying = null;

            if (_passthroughCameraAccess == null)
            {
                return;
            }

            try
            {
                if (_getTextureMethod != null)
                {
                    _getTexture = (Func<Texture>)Delegate.CreateDelegate(
                        typeof(Func<Texture>),
                        _passthroughCameraAccess,
                        _getTextureMethod);
                }
            }
            catch
            {
                _getTexture = null;
            }

            try
            {
                MethodInfo getter = _isPlayingProperty?.GetGetMethod();
                if (getter != null)
                {
                    _getIsPlaying = (Func<bool>)Delegate.CreateDelegate(
                        typeof(Func<bool>),
                        _passthroughCameraAccess,
                        getter);
                }
            }
            catch
            {
                _getIsPlaying = null;
            }
        }

        private static bool IsReadableCandidate(MonoBehaviour behaviour, Type type)
        {
            PropertyInfo isPlayingProperty = type.GetProperty("IsPlaying", BindingFlags.Instance | BindingFlags.Public);
            if (isPlayingProperty == null)
            {
                return true;
            }

            try
            {
                object value = isPlayingProperty.GetValue(behaviour);
                return value is bool isPlaying && isPlaying;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureRenderTexture(int sourceWidth, int sourceHeight)
        {
            int outputWidth = sourceWidth;
            int outputHeight = sourceHeight;

            if (_maxOutputWidth > 0 || _maxOutputHeight > 0)
            {
                float widthScale = _maxOutputWidth > 0 ? (float)_maxOutputWidth / sourceWidth : float.PositiveInfinity;
                float heightScale = _maxOutputHeight > 0 ? (float)_maxOutputHeight / sourceHeight : float.PositiveInfinity;
                float scale = Mathf.Min(widthScale, heightScale, 1f);

                outputWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale));
                outputHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale));
            }

            if (CurrentRenderTexture != null &&
                CurrentRenderTexture.width == outputWidth &&
                CurrentRenderTexture.height == outputHeight)
            {
                return;
            }

            ReleaseRenderTexture();

            CurrentRenderTexture = new RenderTexture(outputWidth, outputHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "QuestVisionFrameSource",
                filterMode = FilterMode.Bilinear
            };
            CurrentRenderTexture.Create();
        }

        private void OnValidate()
        {
            _maxOutputWidth = Mathf.Max(0, _maxOutputWidth);
            _maxOutputHeight = Mathf.Max(0, _maxOutputHeight);
            _targetFrameRate = Mathf.Max(1, _targetFrameRate);
        }

        private void ReleaseRenderTexture()
        {
            if (CurrentRenderTexture == null)
            {
                return;
            }

            CurrentRenderTexture.Release();
            Destroy(CurrentRenderTexture);
            CurrentRenderTexture = null;
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
    }
}
