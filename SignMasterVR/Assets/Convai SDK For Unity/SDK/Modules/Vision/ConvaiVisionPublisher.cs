using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Components;
using Convai.Runtime.Core;
using Convai.Runtime.Core.Modules;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using Convai.Runtime.Vision.Publishing;
using Convai.Runtime.Vision.Sources;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Convai.Modules.Vision
{
    /// <summary>
    ///     Publishes visual context to the LiveKit room.
    /// </summary>
    [MovedFrom(true, "Convai.Modules.Vision", "Convai.Modules.Vision",
        "ConvaiVideoPublisher")]
    public class ConvaiVisionPublisher : MonoBehaviour, IVisionPublisher, IConvaiModule
    {
        [Header("Frame Source")]
        [Tooltip(
            "Vision frame source to publish on native platforms. WebGL publishes the visible Unity canvas instead of a Unity RenderTexture source.")]
        [SerializeField]
        private MonoBehaviour _frameSourceComponent;

        [Header("Publish Policy")]
        [Tooltip("Controls client-side vision transport behavior without assuming backend model capabilities.")]
        [SerializeField]
        private VisionPublishPolicy _publishPolicy = VisionPublishPolicy.AutoCompatible;

        [Header("Video Settings")] [Tooltip("Name of the video track as it appears in the LiveKit room.")]
        public string videoTrackName = "unity-scene";

        [Tooltip("Optional publish FPS cap. 0 uses the selected publish policy default.")] [Range(0, 30)]
        public int publishFrameRateOverride;

        [Tooltip("Optional publish bitrate cap in bits per second. 0 uses the selected publish policy default.")]
        public int publishBitrateOverride;

        private IEventHub _eventHub;
        private VisionPublishCoordinator _publishController;
        private IConvaiRoomConnectionService _roomConnectionService;
        private IVideoSourceFactory _videoSourceFactory;

        public bool IsPublishing => _publishController?.IsPublishing ?? false;

        public IVisionFrameSource FrameSource { get; private set; }

        public VisionPublishPolicy PublishPolicy => _publishPolicy;

        private void Awake()
        {
            _publishController = new VisionPublishCoordinator();
            ApplyPublishConfiguration();
            ConvaiManager.ActiveManager?.RegisterModule(this);
        }

        private void OnDisable()
        {
            if (_publishController != null)
                _ = _publishController.StopAsync();
        }

        private void OnDestroy()
        {
            ConvaiManager.ActiveManager?.UnregisterModule(this);
            _publishController?.Dispose();
            _publishController = null;
        }

        private void OnValidate()
        {
            publishFrameRateOverride = Mathf.Max(0, publishFrameRateOverride);
            publishBitrateOverride = Mathf.Max(0, publishBitrateOverride);

            if (_publishController != null)
                ApplyPublishConfiguration();
        }

        /// <summary>
        ///     Gets the configured video track name used for publishing.
        /// </summary>
        public string VideoTrackName => string.IsNullOrWhiteSpace(videoTrackName)
            ? VideoPublishOptions.Default.TrackName
            : videoTrackName.Trim();

        /// <summary>
        ///     Compatibility injection path. Production bootstrap uses IConvaiModule instead.
        /// </summary>
        public void Inject(
            IEventHub eventHub,
            IVideoSourceFactory videoSourceFactory,
            IConvaiRoomConnectionService connectionService) =>
            ConfigureRuntimeDependencies(eventHub, videoSourceFactory, connectionService);

        /// <summary>
        ///     Compatibility injection path. Production bootstrap uses IConvaiModule instead.
        /// </summary>
        public void Inject(IConvaiRoomConnectionService connectionService) =>
            ConfigureRuntimeDependencies(_eventHub, _videoSourceFactory, connectionService);

        /// <summary>
        ///     Updates the client-side publish policy.
        /// </summary>
        public void SetPublishPolicy(VisionPublishPolicy policy)
        {
            _publishPolicy = policy;
            if (_publishController != null)
                _publishController.SetPublishPolicy(policy);
        }

        /// <summary>
        ///     Enables or disables video publishing without changing the selected policy.
        /// </summary>
        public void EnablePublishing(bool enabled) => _publishController?.SetPublishingEnabled(enabled);

        private void ConfigureRuntimeDependencies(
            IEventHub eventHub,
            IVideoSourceFactory videoSourceFactory,
            IConvaiRoomConnectionService connectionService)
        {
            _eventHub = eventHub;
            _videoSourceFactory = videoSourceFactory;
            _roomConnectionService = connectionService;

            _publishController?.ConfigureDependencies(_eventHub, _videoSourceFactory, _roomConnectionService);

            if (FrameSource != null)
                _publishController?.SetFrameSource(FrameSource);

            ApplyPublishConfiguration();
        }

        private void ApplyPublishConfiguration()
        {
            _publishController?.ApplyConfiguration(
                _publishPolicy,
                Mathf.Max(0, publishFrameRateOverride),
                Mathf.Max(0, publishBitrateOverride),
                VideoTrackName);
        }

        private void ResolveFrameSource()
        {
            if (_frameSourceComponent != null)
            {
                FrameSource = _frameSourceComponent as IVisionFrameSource;
                if (FrameSource == null)
                {
                    ConvaiLogger.Warning(
                        $"Assigned component '{_frameSourceComponent.GetType().Name}' does not implement IVisionFrameSource",
                        LogCategory.Vision);
                }
            }

            if (FrameSource == null)
            {
                var cameraSource = GetComponent<CameraVisionFrameSource>();
                if (cameraSource != null)
                {
                    FrameSource = cameraSource;
                    _frameSourceComponent = cameraSource;
                }
            }

            if (FrameSource == null)
            {
                MonoBehaviour[] localBehaviours = GetComponentsInChildren<MonoBehaviour>(true);
                int localSourceCount = 0;
                foreach (MonoBehaviour behaviour in localBehaviours)
                {
                    if (behaviour is not IVisionFrameSource source)
                        continue;

                    localSourceCount++;
                    if (FrameSource == null)
                    {
                        FrameSource = source;
                        _frameSourceComponent = behaviour;
                    }
                }

                if (localSourceCount > 1)
                {
                    ConvaiLogger.Warning(
                        $"Found {localSourceCount} local frame sources under '{name}'. Auto-selected {DescribeFrameSource(FrameSource)}. Assign a frame source explicitly to avoid ambiguity.",
                        LogCategory.Vision);
                }
            }

            if (FrameSource != null && _frameSourceComponent != null)
            {
                ConvaiLogger.Info($"Using frame source: {DescribeFrameSource(FrameSource)}",
                    LogCategory.Vision);
            }
        }

        internal static string DescribeFrameSource(IVisionFrameSource frameSource)
        {
            if (frameSource is MonoBehaviour behaviour)
            {
                string sourceId = string.IsNullOrWhiteSpace(frameSource.SourceId) ? "unlabeled" : frameSource.SourceId;
                return $"{GetHierarchyPath(behaviour.transform)} [{sourceId}] ({behaviour.GetType().Name})";
            }

            return frameSource?.GetType().Name ?? "(none)";
        }

        internal static string GetHierarchyPath(Transform transform)
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

        private static bool UsesWebGLCanvasPublishPath() =>
            UsesWebGLCanvasPublishPath(UnityEngine.Application.platform);

        private static bool UsesWebGLCanvasPublishPath(RuntimePlatform platform) =>
            platform == RuntimePlatform.WebGLPlayer;

        #region IConvaiModule

        /// <inheritdoc />
        public string ModuleId => "convai.vision";

        /// <inheritdoc />
        public string DisplayName => "Vision";

        /// <inheritdoc />
        public IReadOnlyList<string> RequiredModules => Array.Empty<string>();

        /// <inheritdoc />
        public IReadOnlyList<Type> RequiredServices => Array.Empty<Type>();

        /// <inheritdoc />
        public IReadOnlyList<Type> ProvidedServices => Array.Empty<Type>();

        /// <inheritdoc />
        public bool IsActive => enabled && isActiveAndEnabled;

        /// <inheritdoc />
        public ValueTask RegisterAsync(IModuleContext context, CancellationToken ct = default)
        {
            if (UsesWebGLCanvasPublishPath())
            {
                if (_frameSourceComponent != null)
                {
                    ConvaiLogger.Info(
                        "WebGL publish path ignores assigned IVisionFrameSource and uses the visible Unity canvas instead.",
                        LogCategory.Vision);
                }

                return default;
            }

            ResolveFrameSource();

            if (FrameSource == null)
            {
                ConvaiLogger.Error(
                    "No IVisionFrameSource found. Add CameraVisionFrameSource or assign a frame source.",
                    LogCategory.Vision);
            }
            else
                _publishController?.SetFrameSource(FrameSource);

            return default;
        }

        /// <inheritdoc />
        public ValueTask StartAsync(IModuleContext context, CancellationToken ct = default)
        {
            if (context == null)
                return default;

            context.TryGetModuleService(out IConvaiRoomConnectionService roomConnectionService);
            ConfigureRuntimeDependencies(
                context.Events,
                context.Transport?.CreateVideoSourceFactory(),
                roomConnectionService);

            if (_roomConnectionService == null)
            {
                ConvaiLogger.Error(
                    "Room connection service not available via module context.",
                    LogCategory.Vision);
                return default;
            }

            if (_roomConnectionService.ConnectionType != ConvaiConnectionType.Video)
            {
                ConvaiLogger.Info(
                    "Dynamic Vision Context is disabled for this room — vision publishing is off. " +
                    "Set Dynamic Vision Context mode to Enabled (or Auto with Connection Type set to Video) on the Convai Room Manager to enable it.",
                    LogCategory.Vision);
                return default;
            }

            return _publishController != null ? _publishController.StartAsync(ct) : default;
        }

        /// <inheritdoc />
        public ValueTask PauseAsync(RuntimePauseReason reason, CancellationToken ct = default) => default;

        /// <inheritdoc />
        public ValueTask ResumeAsync(CancellationToken ct = default) => default;

        /// <inheritdoc />
        public ValueTask StopAsync(CancellationToken ct = default) =>
            _publishController != null ? _publishController.StopAsync(ct) : default;

        #endregion
    }
}
