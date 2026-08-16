using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Runtime.Vision.Sources;
using Convai.Shared.Compatibility;
using Convai.Shared;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UI;

namespace Convai.Runtime.Vision.Debug
{
    /// <summary>
    ///     Debug preview component for vision capture.
    ///     In the Unity Editor it displays captured frames and statistics for debugging.
    ///     In player builds it disables itself (so it is safe to leave on prefabs/scenes).
    /// </summary>
    /// <remarks>
    ///     Use this to debug "What does the AI actually see?" during development.
    ///     Features:
    ///     - Displays captured RenderTexture as an overlay
    ///     - Shows capture statistics (FPS, resolution, frame count)
    ///     - Configurable corner position and offset
    ///     - Works with any <see cref="IVisionFrameSource" /> (CameraVisionFrameSource, WebcamVisionFrameSource, etc.)
    ///     - Maintains correct aspect ratio (16:9 by default, or matches frame source)
    /// </remarks>
    [MovedFrom(true, "Convai.Runtime.Vision", "Convai.Runtime",
        "VisionDebugPreview")]
    [AddComponentMenu("Convai/Vision/Vision Debug Preview (Editor Only)")]
    public class VisionDebugPreview : MonoBehaviour
    {
        #region Constants

        /// <summary>Default aspect ratio (16:9) for preview overlay.</summary>
        private const float DefaultAspectRatio = 16f / 9f;

        #endregion

        #region Inspector Fields

#pragma warning disable CS0649
        [HideInInspector]
        [Tooltip("RawImage to display the captured texture. If not set, creates an overlay.")]
        [SerializeField]
        private RawImage _previewImage;
#pragma warning restore CS0649

        [Tooltip("Vision frame source to preview. If not set, finds any IVisionFrameSource in scene.")] [SerializeField]
        private MonoBehaviour _frameSourceComponent;

        [Tooltip("If the assigned source is idle, preview the active scene source that is actually capturing frames.")]
        [SerializeField]
        private bool _fallbackToActiveFrameSource = true;

        [Header("Overlay Layout")] [Tooltip("Corner position for the overlay preview.")] [SerializeField]
        private PreviewPosition _overlayPosition = PreviewPosition.BottomRight;

        [Tooltip("Width of the overlay preview in pixels. Height is calculated based on aspect ratio.")]
        [SerializeField]
        [Range(160, 640)]
        private int _overlayWidth = 320;

        [Tooltip("Horizontal offset from the selected corner in pixels.")] [SerializeField] [Range(0, 200)]
        private int _offsetX = 10;

        [Tooltip("Vertical offset from the selected corner in pixels.")] [SerializeField] [Range(0, 200)]
        private int _offsetY = 10;

        [Header("Aspect Ratio")]
        [Tooltip(
            "When enabled, uses the frame source's aspect ratio. When disabled, uses the custom aspect ratio below.")]
        [SerializeField]
        private bool _useSourceAspectRatio = true;

        [Tooltip("Custom aspect ratio (width / height). Only used when 'Use Source Aspect Ratio' is disabled.")]
        [SerializeField]
        [Range(1f, 3f)]
        private float _customAspectRatio = DefaultAspectRatio;

        #endregion

        #region Private Fields

        private IVisionFrameSource _frameSource;

        private long _lastKnownFrameCount;

        private const float FpsUpdateInterval = 0.5f;
        private const float ActiveSourceFallbackDelaySeconds = 1f;
        private const float ActiveSourceSearchIntervalSeconds = 0.5f;
        private long _fpsFrameCountStart;
        private float _fpsTimeStart;
        private float _nextActiveSourceSearchTime;
        private bool _loggedMissingFallbackSource;

        private GUIStyle _statsStyle;
        private bool _styleInitialized;
        private RectTransform _ownedOverlayRoot;
        private bool _ownsPreviewImage;
        private bool _hasExplicitFrameSourceAssignment;

        #endregion

        #region Public Properties

        /// <summary>Gets or sets whether the preview is visible.</summary>
        [field: Header("Preview Settings")]
        [field: Tooltip("Enable or disable the debug preview.")]
        [field: SerializeField]
        public bool ShowPreview { get; set; } = true;

        /// <summary>Gets or sets whether statistics are shown.</summary>
        [field: Header("Statistics")]
        [field: Tooltip("Show capture statistics overlay.")]
        [field: SerializeField]
        public bool ShowStats { get; set; } = true;

        /// <summary>Gets the current capture FPS.</summary>
        public float CurrentFps { get; private set; }

        /// <summary>Gets the total frame count from the frame source.</summary>
        public long FrameCount => _frameSource?.FrameCount ?? 0;

        /// <summary>Gets whether capture is currently active.</summary>
        public bool IsCapturing => _frameSource?.IsCapturing ?? false;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
#if !UNITY_EDITOR
            enabled = false;
            return;
#else
            _hasExplicitFrameSourceAssignment = _frameSourceComponent != null;
            ResolveFrameSource();
#endif
        }

        private void OnEnable()
        {
#if !UNITY_EDITOR
            return;
#else
            EnsureFrameSourceResolved();
            EnsureOverlayPreview();
            ResetActiveSourceFallbackTimer();
            SubscribeToFrameSource();
            RefreshPreviewTexture();
#endif
        }

        private void Update()
        {
#if !UNITY_EDITOR
            return;
#else
            EnsureFrameSourceResolved();
            EnsureOverlayPreview();
            TryFallbackToActiveFrameSource();
            RefreshPreviewTexture();
            if (_frameSource == null) return;

            UpdateFpsCalculation();
#endif
        }

        private void OnDisable()
        {
#if !UNITY_EDITOR
            return;
#else
            UnsubscribeFromFrameSource();
            CleanupOwnedOverlay();
#endif
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            CleanupOwnedOverlay();
#endif
        }

        private void OnGUI()
        {
#if !UNITY_EDITOR
            return;
#else
            if (!ShowPreview && !ShowStats) return;

            InitializeStyles();
            EnsureFrameSourceResolved();
            TryFallbackToActiveFrameSource();
            RefreshPreviewTexture();

            if (ShowPreview && _previewImage == null && _frameSource != null) DrawOverlayPreview();

            if (ShowStats) DrawStatistics();
#endif
        }

        #endregion

        #region Private Methods

        private void ResolveFrameSource()
        {
            if (_frameSourceComponent != null)
            {
                _frameSource = _frameSourceComponent as IVisionFrameSource;
                if (_frameSource == null)
                {
                    ConvaiLogger.Warning(
                        $"Assigned component '{_frameSourceComponent.GetType().Name}' does not implement IVisionFrameSource",
                        LogCategory.Vision);
                }
            }

            if (_frameSource == null && TryFindFirstFrameSource(GetComponents<MonoBehaviour>(), out IVisionFrameSource localSource))
            {
                _frameSource = localSource;
                _frameSourceComponent = localSource as MonoBehaviour;
            }

            if (_frameSource == null &&
                TryFindFirstFrameSource(GetComponentsInChildren<MonoBehaviour>(true), out IVisionFrameSource childSource))
            {
                _frameSource = childSource;
                _frameSourceComponent = childSource as MonoBehaviour;
            }

            if (_frameSource == null &&
                TryFindFirstFrameSource(GetComponentsInParent<MonoBehaviour>(true), out IVisionFrameSource parentSource))
            {
                _frameSource = parentSource;
                _frameSourceComponent = parentSource as MonoBehaviour;
            }

            if (_frameSource == null)
            {
                if (TryFindFirstSceneFrameSource(out IVisionFrameSource discoveredSource))
                {
                    _frameSource = discoveredSource;
                    _frameSourceComponent = discoveredSource as MonoBehaviour;
                }
            }

            if (_frameSource != null)
            {
                ConvaiLogger.Info($"Using frame source: {GetFrameSourceName(_frameSource)}",
                    LogCategory.Vision);
            }
        }

        private void EnsureFrameSourceResolved()
        {
            if (_frameSource != null)
                return;

            ResolveFrameSource();
            SubscribeToFrameSource();
        }

        private void SubscribeToFrameSource()
        {
            if (_frameSource == null)
                return;

            _frameSource.FrameReady -= HandleFrameReady;
            _frameSource.FrameReady += HandleFrameReady;
        }

        private void UnsubscribeFromFrameSource()
        {
            if (_frameSource == null)
                return;

            _frameSource.FrameReady -= HandleFrameReady;
        }

        private void HandleFrameReady() => RefreshPreviewTexture();

        private void ResetActiveSourceFallbackTimer() =>
            _nextActiveSourceSearchTime = Time.realtimeSinceStartup + ActiveSourceFallbackDelaySeconds;

        private void TryFallbackToActiveFrameSource()
        {
            if (!_fallbackToActiveFrameSource)
                return;

            if (TryRestoreAssignedFrameSource())
                return;

            if (_frameSource != null && _frameSource.CurrentRenderTexture != null)
                return;

            if (Time.realtimeSinceStartup < _nextActiveSourceSearchTime)
                return;

            _nextActiveSourceSearchTime = Time.realtimeSinceStartup + ActiveSourceSearchIntervalSeconds;

            if (!TryFindActiveSceneFrameSource(_frameSource, out IVisionFrameSource activeSource, out int candidateCount))
            {
                if (!_loggedMissingFallbackSource)
                {
                    _loggedMissingFallbackSource = true;
                    ConvaiLogger.Info("Waiting for another scene frame source with a texture",
                        LogCategory.Vision);
                }

                return;
            }

            SwitchFrameSource(activeSource, candidateCount);
        }

        private bool TryRestoreAssignedFrameSource()
        {
            if (!_hasExplicitFrameSourceAssignment || _frameSourceComponent is not IVisionFrameSource assignedSource)
                return false;

            if (ReferenceEquals(_frameSource, assignedSource))
                return false;

            if (assignedSource.CurrentRenderTexture == null)
                return false;

            SwitchFrameSource(assignedSource, candidateCount: 0);
            return true;
        }

        private void SwitchFrameSource(IVisionFrameSource activeSource, int candidateCount)
        {
            UnsubscribeFromFrameSource();

            _frameSource = activeSource;
            if (!_hasExplicitFrameSourceAssignment)
                _frameSourceComponent = activeSource as MonoBehaviour;
            _loggedMissingFallbackSource = false;

            SubscribeToFrameSource();
            RefreshPreviewTexture();

            if (candidateCount > 1)
            {
                ConvaiLogger.Warning(
                    $"Found {candidateCount} active scene frame sources; selecting {GetFrameSourceName(activeSource)}. Assign a source explicitly to avoid ambiguity.",
                    LogCategory.Vision);
            }

            ConvaiLogger.Info(
                $"Switched to active frame source: {GetFrameSourceName(activeSource)}",
                LogCategory.Vision);
        }

        private void RefreshPreviewTexture()
        {
            if (_ownsPreviewImage && _ownedOverlayRoot != null)
                _ownedOverlayRoot.gameObject.SetActive(ShowPreview);

            if (!ShowPreview || _previewImage == null)
                return;

            if (_ownsPreviewImage)
                UpdatePreviewRectTransform();

            RenderTexture currentRt = _frameSource?.CurrentRenderTexture;
            if (currentRt != null)
            {
                if (_previewImage.texture != currentRt)
                    _previewImage.texture = currentRt;

                if (_ownsPreviewImage)
                    _previewImage.color = Color.white;
            }
            else if (_ownsPreviewImage)
            {
                _previewImage.texture = null;
                _previewImage.color = new Color(0f, 0f, 0f, 0.72f);
            }
        }

        private void EnsureOverlayPreview()
        {
            if (!ShowPreview || _previewImage != null)
                return;

            var canvasObject = new GameObject("Convai Vision Debug Preview", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            _ownedOverlayRoot = canvasObject.GetComponent<RectTransform>();

            var imageObject = new GameObject("Preview", typeof(RawImage));
            imageObject.transform.SetParent(canvasObject.transform, false);

            _previewImage = imageObject.GetComponent<RawImage>();
            _previewImage.raycastTarget = false;
            _previewImage.uvRect = new Rect(0f, 1f, 1f, -1f);
            _previewImage.color = new Color(0f, 0f, 0f, 0.72f);
            _ownsPreviewImage = true;

            UpdatePreviewRectTransform();
        }

        private void UpdatePreviewRectTransform()
        {
            if (_previewImage == null)
                return;

            RectTransform rectTransform = _previewImage.rectTransform;
            Vector2 overlaySize = CalculateOverlaySize();

            Vector2 anchor;
            Vector2 pivot;
            Vector2 anchoredPosition;

            switch (_overlayPosition)
            {
                case PreviewPosition.TopLeft:
                    anchor = new Vector2(0f, 1f);
                    pivot = new Vector2(0f, 1f);
                    anchoredPosition = new Vector2(_offsetX, -_offsetY);
                    break;
                case PreviewPosition.TopRight:
                    anchor = new Vector2(1f, 1f);
                    pivot = new Vector2(1f, 1f);
                    anchoredPosition = new Vector2(-_offsetX, -_offsetY);
                    break;
                case PreviewPosition.BottomLeft:
                    anchor = new Vector2(0f, 0f);
                    pivot = new Vector2(0f, 0f);
                    anchoredPosition = new Vector2(_offsetX, _offsetY);
                    break;
                case PreviewPosition.BottomRight:
                default:
                    anchor = new Vector2(1f, 0f);
                    pivot = new Vector2(1f, 0f);
                    anchoredPosition = new Vector2(-_offsetX, _offsetY);
                    break;
            }

            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.pivot = pivot;
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, overlaySize.x);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, overlaySize.y);
        }

        private void CleanupOwnedOverlay()
        {
            if (!_ownsPreviewImage)
                return;

            GameObject overlayObject = _ownedOverlayRoot != null ? _ownedOverlayRoot.gameObject : null;
            _previewImage = null;
            _ownedOverlayRoot = null;
            _ownsPreviewImage = false;

            if (overlayObject == null)
                return;

            if (UnityEngine.Application.isPlaying)
                Destroy(overlayObject);
            else
                DestroyImmediate(overlayObject);
        }

        private static bool TryFindFirstSceneFrameSource(out IVisionFrameSource frameSource)
        {
            MonoBehaviour[] behaviours = ConvaiObjectFind.All<MonoBehaviour>(
                FindObjectsInactive.Exclude);
            return TryFindFirstFrameSource(behaviours, out frameSource);
        }

        private static bool TryFindFirstFrameSource(MonoBehaviour[] behaviours, out IVisionFrameSource frameSource)
        {
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IVisionFrameSource discoveredSource)
                {
                    frameSource = discoveredSource;
                    return true;
                }
            }

            frameSource = null;
            return false;
        }

        private static bool TryFindActiveSceneFrameSource(
            IVisionFrameSource currentSource,
            out IVisionFrameSource frameSource,
            out int candidateCount)
        {
            MonoBehaviour[] behaviours = ConvaiObjectFind.All<MonoBehaviour>(
                FindObjectsInactive.Exclude);
            IVisionFrameSource bestSource = null;
            int bestScore = int.MinValue;
            candidateCount = 0;

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is not IVisionFrameSource discoveredSource)
                    continue;

                if (ReferenceEquals(discoveredSource, currentSource))
                    continue;

                if (discoveredSource.CurrentRenderTexture == null)
                    continue;

                candidateCount++;

                int score = ScoreFrameSource(discoveredSource);
                if (bestSource == null || score > bestScore)
                {
                    bestSource = discoveredSource;
                    bestScore = score;
                }
            }

            frameSource = bestSource;
            return bestSource != null;
        }

        private static int ScoreFrameSource(IVisionFrameSource frameSource)
        {
            int score = 0;
            if (frameSource.CurrentRenderTexture != null)
                score += 100;
            if (frameSource.IsCapturing)
                score += 10;
            if (frameSource.IsFrameReady)
                score += 5;
            if (frameSource.FrameCount > 0)
                score += 1;
            return score;
        }

        private static string GetFrameSourceName(IVisionFrameSource frameSource)
        {
            if (frameSource == null)
                return "(none)";

            if (frameSource is MonoBehaviour behaviour)
            {
                string path = GetHierarchyPath(behaviour.transform);
                string sourceId = string.IsNullOrWhiteSpace(frameSource.SourceId) ? "unlabeled" : frameSource.SourceId;
                return $"{path} [{sourceId}] ({behaviour.GetType().Name})";
            }

            return frameSource.GetType().Name;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "(unknown)";

            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return path;
        }

        private void UpdateFpsCalculation()
        {
            float now = Time.realtimeSinceStartup;
            float elapsed = now - _fpsTimeStart;

            if (elapsed >= FpsUpdateInterval)
            {
                long currentFrameCount = _frameSource.FrameCount;
                long framesDelta = currentFrameCount - _fpsFrameCountStart;

                CurrentFps = framesDelta / elapsed;

                _fpsFrameCountStart = currentFrameCount;
                _fpsTimeStart = now;
            }
        }

        private void InitializeStyles()
        {
            if (_styleInitialized) return;

            _statsStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12, alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 8, 8)
            };
            _statsStyle.normal.textColor = Color.white;
            _styleInitialized = true;
        }

        private void DrawOverlayPreview()
        {
            Rect previewRect = GetOverlayRect();
            RenderTexture rt = _frameSource.CurrentRenderTexture;
            if (rt == null)
            {
                GUI.Box(previewRect, "Waiting for vision frame", _statsStyle);
                return;
            }

            GUI.DrawTextureWithTexCoords(previewRect, rt, new Rect(0, 1, 1, -1));
        }

        private void DrawStatistics()
        {
            Rect statsRect = GetStatsRect();

            (int w, int h) = _frameSource?.FrameDimensions ?? (0, 0);
            float targetFps = _frameSource?.TargetFrameRate ?? 0;
            long frameCount = _frameSource?.FrameCount ?? 0;

            string statsText = "Vision Capture Debug\n" +
                               $"Status: {(IsCapturing ? "Capturing" : "Stopped")}\n" +
                               $"Source: {GetFrameSourceName(_frameSource)}\n" +
                               $"Resolution: {w}x{h}\n" +
                               $"FPS: {CurrentFps:F1} (target: {targetFps:F0})\n" +
                               $"Frames: {frameCount}";

            GUI.Box(statsRect, statsText, _statsStyle);
        }

        private Rect GetOverlayRect()
        {
            Vector2 overlaySize = CalculateOverlaySize();

            float x = 0, y = 0;

            switch (_overlayPosition)
            {
                case PreviewPosition.TopLeft:
                    x = _offsetX;
                    y = _offsetY;
                    break;
                case PreviewPosition.TopRight:
                    x = Screen.width - overlaySize.x - _offsetX;
                    y = _offsetY;
                    break;
                case PreviewPosition.BottomLeft:
                    x = _offsetX;
                    y = Screen.height - overlaySize.y - _offsetY;
                    break;
                case PreviewPosition.BottomRight:
                    x = Screen.width - overlaySize.x - _offsetX;
                    y = Screen.height - overlaySize.y - _offsetY;
                    break;
            }

            return new Rect(x, y, overlaySize.x, overlaySize.y);
        }

        /// <summary>
        ///     Calculates overlay size maintaining the correct aspect ratio.
        /// </summary>
        /// <returns>Overlay size with width from inspector and height calculated from aspect ratio.</returns>
        private Vector2 CalculateOverlaySize()
        {
            float aspectRatio = GetEffectiveAspectRatio();
            float height = _overlayWidth / aspectRatio;
            return new Vector2(_overlayWidth, height);
        }

        /// <summary>
        ///     Gets the effective aspect ratio based on settings.
        ///     Uses frame source dimensions if available and enabled, otherwise uses custom or default ratio.
        /// </summary>
        /// <returns>The aspect ratio (width / height) to use for overlay sizing.</returns>
        private float GetEffectiveAspectRatio()
        {
            if (_useSourceAspectRatio && _frameSource != null)
            {
                (int width, int height) = _frameSource.FrameDimensions;
                if (width > 0 && height > 0) return (float)width / height;
            }

            return _customAspectRatio;
        }

        private Rect GetStatsRect()
        {
            float statsHeight = 122f;
            float statsWidth = 180f;
            float statsGap = 5f;

            if (ShowPreview && _previewImage == null)
            {
                Rect overlayRect = GetOverlayRect();

                float statsY = overlayRect.yMax - statsHeight;
                float statsX = _overlayPosition switch
                {
                    PreviewPosition.TopRight or PreviewPosition.BottomRight => overlayRect.x - statsWidth - statsGap,
                    _ => overlayRect.xMax + statsGap
                };
                return new Rect(statsX, statsY, statsWidth, statsHeight);
            }

            float x, y;
            switch (_overlayPosition)
            {
                case PreviewPosition.TopLeft:
                    x = _offsetX;
                    y = _offsetY;
                    break;
                case PreviewPosition.TopRight:
                    x = Screen.width - statsWidth - _offsetX;
                    y = _offsetY;
                    break;
                case PreviewPosition.BottomLeft:
                    x = _offsetX;
                    y = Screen.height - statsHeight - _offsetY;
                    break;
                case PreviewPosition.BottomRight:
                default:
                    x = Screen.width - statsWidth - _offsetX;
                    y = Screen.height - statsHeight - _offsetY;
                    break;
            }

            return new Rect(x, y, statsWidth, statsHeight);
        }

        #endregion
    }

    /// <summary>
    ///     Position options for the debug preview overlay.
    /// </summary>
    public enum PreviewPosition
    {
        /// <summary>Top-left corner.</summary>
        TopLeft,

        /// <summary>Top-right corner.</summary>
        TopRight,

        /// <summary>Bottom-left corner.</summary>
        BottomLeft,

        /// <summary>Bottom-right corner.</summary>
        BottomRight
    }
}
