using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.EventSystem;
using Convai.Domain.Models.LipSync;
using Convai.Modules.LipSync.Profiles;
using Convai.Runtime.Components;
using Convai.Runtime.Core;
using Convai.Runtime.Core.Modules;
using Convai.Runtime.Room;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Thin MonoBehaviour shell that delegates runtime lip sync behavior to dedicated services.
    /// </summary>
    [AddComponentMenu("Convai/Lip Sync/Convai Lip Sync")]
    public sealed class ConvaiLipSyncComponent : MonoBehaviour, ILipSyncCapabilitySource,
        IConvaiModule
    {
        // Section grouping lives in ConvaiLipSyncComponentEditor's Convai sections; [Header]
        // attributes here would render the same titles a second time inside those sections.
        [SerializeField]
        private string _lockedProfileId = LipSyncProfileId.ARKitValue;

        [SerializeField] private ConvaiLipSyncMapAsset _mapping;
        [SerializeField] private List<SkinnedMeshRenderer> _targetMeshes = new();

        [SerializeField] [Range(0f, 0.9f)]
        private float _smoothingFactor = 0.5f;

        [SerializeField] [Range(0.05f, 2f)] private float _fadeOutDuration = 0.2f;

        [Tooltip("Ramp from the current pose into the first played frames at playback start. 0 disables it.")]
        [SerializeField] [Range(0f, 0.5f)] private float _fadeInDuration = 0.1f;

        [SerializeField] [Range(-0.5f, 0.5f)] private float _timeOffset;

        [SerializeField]
        private LipSyncLatencyMode _latencyMode = LipSyncLatencyMode.Balanced;

        [SerializeField] [Range(1f, 10f)] private float _maxBufferedSeconds = 3f;
        [SerializeField] [Range(0.05f, 0.3f)] private float _minResumeHeadroomSeconds = 0.12f;
        [SerializeField] private bool _deliverChunksAhead;
        private LipSyncLifecycleOrchestrator _lifecycleOrchestrator;
        private IConvaiRoomAudioService _roomAudioService;
        private bool _isRuntimePaused;

        private LipSyncRuntimeController _runtimeController;
        /// <summary>Currently active profile id used by this component.</summary>
        public LipSyncProfileId ActiveProfile
        {
            get
            {
                if (_lifecycleOrchestrator != null && _lifecycleOrchestrator.ActiveProfile.IsValid)
                    return _lifecycleOrchestrator.ActiveProfile;

                return new LipSyncProfileId(LipSyncProfileId.Normalize(_lockedProfileId));
            }
        }

        /// <summary>Inspector-configured profile id normalized into a value object.</summary>
        public LipSyncProfileId LockedProfile => new(_lockedProfileId);

        /// <summary>Whether the playback engine is currently playing or starving.</summary>
        public bool IsPlaying => _lifecycleOrchestrator?.IsPlaying ?? false;

        /// <summary>Whether smooth fade-out is currently in progress.</summary>
        public bool IsFadingOut => _lifecycleOrchestrator?.IsFadingOut ?? false;

        /// <summary>Whether this component currently has remaining talking time.</summary>
        public bool IsTalking => GetTalkingTimeRemaining() > 0f;

        /// <summary>Current playback engine state.</summary>
        public PlaybackState EngineState => _lifecycleOrchestrator?.EngineState ?? PlaybackState.Idle;

        /// <summary>Whether runtime lifecycle has paused LipSync presentation ticks.</summary>
        public bool IsPresentationPaused => _isRuntimePaused;

        /// <summary>Configured target meshes receiving blendshape output.</summary>
        public IReadOnlyList<SkinnedMeshRenderer> TargetMeshes => _targetMeshes;

        /// <summary>Inspector-assigned mapping asset, if any.</summary>
        public ConvaiLipSyncMapAsset Mapping => _mapping;

        /// <summary>Effective runtime mapping after profile-default fallback resolution.</summary>
        public ConvaiLipSyncMapAsset EffectiveMapping
        {
            get
            {
                LipSyncRuntimeConfig config = BuildRuntimeConfig();
                return LipSyncCapabilityResolver.ResolveEffectiveMapping(config);
            }
        }

        private void Awake()
        {
            EnsureServices();
            _lifecycleOrchestrator.HandleAwake(this, BuildRuntimeConfig());
            ConvaiManager.ActiveManager?.RegisterModule(this);
        }

        private void LateUpdate()
        {
            if (!_isRuntimePaused)
                _lifecycleOrchestrator?.Tick(Time.deltaTime);
        }

        private void OnEnable()
        {
            EnsureServices();
            _lifecycleOrchestrator.HandleEnable(this, UnityEngine.Application.isPlaying, BuildRuntimeConfig());
        }

        private void OnDisable()
        {
            _lifecycleOrchestrator?.HandleDisable();
        }

        private void OnDestroy()
        {
            ConvaiManager.ActiveManager?.UnregisterModule(this);
            _lifecycleOrchestrator?.HandleDestroy();
        }

        private void OnValidate()
        {
            ApplyLatencyPreset();
            EnsureServices();
            _lifecycleOrchestrator.HandleValidate(this, UnityEngine.Application.isPlaying, BuildRuntimeConfig());
        }

        /// <summary>
        ///     Builds transport options for room negotiation from the active profile and effective source schema.
        /// </summary>
        public bool TryGetLipSyncTransportOptions(out LipSyncTransportOptions options)
        {
            EnsureServices();
            return _lifecycleOrchestrator.TryGetTransportOptions(BuildRuntimeConfig(), out options);
        }

        /// <summary>
        ///     Injects required runtime services for event-driven lip sync playback.
        /// </summary>
        public void Inject(IEventHub eventHub, ILogger logger = null)
        {
            EnsureServices();
            _lifecycleOrchestrator.HandleInject(
                this,
                BuildRuntimeConfig(),
                eventHub,
                logger,
                enabled && isActiveAndEnabled);
        }

        /// <summary>Returns estimated remaining talking time in seconds.</summary>
        public float GetTalkingTimeRemaining() => _lifecycleOrchestrator?.GetTalkingTimeRemaining() ?? 0f;

        /// <summary>Returns elapsed talking time in seconds from stream playback start.</summary>
        public float GetTalkingTimeElapsed() => _lifecycleOrchestrator?.GetTalkingTimeElapsed() ?? 0f;

        /// <summary>Total duration currently buffered in the runtime ring buffer.</summary>
        public float GetTotalBufferedDuration() => _lifecycleOrchestrator?.GetTotalBufferedDuration() ?? 0f;

        /// <summary>Total duration of all frames received since stream start.</summary>
        public float GetTotalStreamDuration() => _lifecycleOrchestrator?.GetTotalStreamDuration() ?? 0f;

        /// <summary>Current headroom between buffer end and playback position. Negative means starving.</summary>
        public float GetHeadroom() => _lifecycleOrchestrator?.GetHeadroom() ?? 0f;

        /// <summary>
        ///     Returns a zero-allocation snapshot of the current blendshape output values.
        /// </summary>
        public BlendshapeSnapshot GetBlendshapeSnapshot() => _lifecycleOrchestrator?.GetBlendshapeSnapshot() ?? default;

        private void EnsureServices()
        {
            if (_lifecycleOrchestrator != null) return;

            _runtimeController = new LipSyncRuntimeController();
            _lifecycleOrchestrator = new LipSyncLifecycleOrchestrator(_runtimeController);
        }

        private void ApplyLatencyPreset()
        {
            switch (_latencyMode)
            {
                case LipSyncLatencyMode.UltraLowLatency:
                    _maxBufferedSeconds = 1f;
                    _minResumeHeadroomSeconds = 0.05f;
                    break;
                case LipSyncLatencyMode.Balanced:
                    _maxBufferedSeconds = 3f;
                    _minResumeHeadroomSeconds = 0.12f;
                    break;
                case LipSyncLatencyMode.NetworkSafe:
                    _maxBufferedSeconds = 6f;
                    _minResumeHeadroomSeconds = 0.25f;
                    break;
            }
        }

        private LipSyncRuntimeConfig BuildRuntimeConfig() => LipSyncRuntimeConfig.CreateNormalized(
            _lockedProfileId,
            _mapping,
            _targetMeshes,
            _fadeOutDuration,
            _smoothingFactor,
            _timeOffset,
            _maxBufferedSeconds,
            _minResumeHeadroomSeconds,
            _deliverChunksAhead,
            _fadeInDuration);

        #region IConvaiModule

        /// <inheritdoc />
        public string ModuleId => "convai.lipsync";

        /// <inheritdoc />
        public string DisplayName => "Lip Sync";

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
            // LipSync module does not register any services.
            return default;
        }

        /// <inheritdoc />
        public ValueTask StartAsync(IModuleContext context, CancellationToken ct = default)
        {
            if (context == null) return default;

            IEventHub eventHub = context.Events;
            ILogger logger = context.Logger;
            _roomAudioService = context.RoomAudio;

            _runtimeController.SetRoomAudioService(_roomAudioService);

            if (eventHub != null)
            {
                Inject(eventHub, logger);
            }

            return default;
        }

        /// <inheritdoc />
        public ValueTask PauseAsync(RuntimePauseReason reason, CancellationToken ct = default)
        {
            _isRuntimePaused = true;
            return default;
        }

        /// <inheritdoc />
        public ValueTask ResumeAsync(CancellationToken ct = default)
        {
            _isRuntimePaused = false;
            return default;
        }

        /// <inheritdoc />
        public ValueTask StopAsync(CancellationToken ct = default)
        {
            _isRuntimePaused = false;
            _lifecycleOrchestrator?.HandleDisable();
            return default;
        }

        #endregion
    }
}
