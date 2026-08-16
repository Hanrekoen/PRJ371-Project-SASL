using System;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models.LipSync;
using Convai.Modules.LipSync.Profiles;
using Convai.Runtime.Logging;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Owns component lifecycle, validation, capability resolution, and runtime binding.
    /// </summary>
    internal sealed class LipSyncLifecycleOrchestrator
    {
        private readonly LipSyncRuntimeController _runtimeController;
        private IEventHub _eventHub;
        private ICharacterIdentitySource _identitySource;
        private bool _isInjected;
        private ILogger _logger;
        private string _resolvedCharacterId = string.Empty;

        public LipSyncLifecycleOrchestrator(LipSyncRuntimeController runtimeController)
        {
            _runtimeController = runtimeController ?? throw new ArgumentNullException(nameof(runtimeController));
        }

        public LipSyncProfileId ActiveProfile { get; private set; }
        public bool IsPlaying => _runtimeController.IsPlaying;
        public bool IsFadingOut => _runtimeController.IsFadingOut;
        public PlaybackState EngineState => _runtimeController.EngineState;

        public void HandleAwake(Component context, in LipSyncRuntimeConfig config)
        {
            EnsureRuntimeInitialized(context, config);
            if (!EnsureRuntimePrerequisites(context)) return;
            TryBindRuntimeIfInjected(context);
        }

        public void HandleEnable(Component context, bool isPlaying, in LipSyncRuntimeConfig config)
        {
            if (!isPlaying) return;
            EnsureRuntimeInitialized(context, config);
            if (!EnsureRuntimePrerequisites(context)) return;
            TryBindRuntimeIfInjected(context);
        }

        public void HandleDisable() => _runtimeController.UnbindAndReset();

        public void HandleDestroy() => _runtimeController.Dispose();

        public void HandleValidate(Component context, bool isPlaying, in LipSyncRuntimeConfig config)
        {
            ActiveProfile = config.ProfileId;
            if (isPlaying && _runtimeController.IsInitialized) ConfigureRuntime(context, config, false);
        }

        public void HandleInject(
            Component context,
            in LipSyncRuntimeConfig config,
            IEventHub eventHub,
            ILogger logger,
            bool shouldBindNow)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _logger = logger;
            _isInjected = true;

            EnsureRuntimeInitialized(context, config);
            if (!EnsureRuntimePrerequisites(context)) return;
            if (shouldBindNow) TryBindRuntimeIfInjected(context);
        }

        public ConvaiLipSyncMapAsset ResolveEffectiveMapping(in LipSyncRuntimeConfig config) =>
            LipSyncCapabilityResolver.ResolveEffectiveMapping(config);

        public bool TryGetTransportOptions(in LipSyncRuntimeConfig config, out LipSyncTransportOptions options) =>
            LipSyncCapabilityResolver.TryGetTransportOptions(config, out options);

        public void Tick(float deltaTime) => _runtimeController.Tick(deltaTime);
        public float GetTalkingTimeRemaining() => _runtimeController.GetTalkingTimeRemaining();
        public float GetTalkingTimeElapsed() => _runtimeController.GetTalkingTimeElapsed();
        public float GetTotalBufferedDuration() => _runtimeController.GetTotalBufferedDuration();
        public float GetTotalStreamDuration() => _runtimeController.GetTotalStreamDuration();
        public float GetHeadroom() => _runtimeController.GetHeadroom();
        public BlendshapeSnapshot GetBlendshapeSnapshot() => _runtimeController.GetBlendshapeSnapshot();

        private void EnsureRuntimeInitialized(Component context, in LipSyncRuntimeConfig config) =>
            ConfigureRuntime(context, config, true);

        private void ConfigureRuntime(Component context, in LipSyncRuntimeConfig config, bool ensureInitialized)
        {
            ActiveProfile = config.ProfileId;
            ConvaiLipSyncMapAsset effectiveMapping = LipSyncCapabilityResolver.ResolveEffectiveMapping(config);
            if (ensureInitialized)
            {
                _runtimeController.EnsureInitialized(context, config, effectiveMapping);
                return;
            }

            if (_runtimeController.IsInitialized) _runtimeController.Reconfigure(config, effectiveMapping);
        }

        private bool EnsureRuntimePrerequisites(Component context)
        {
            if (!LipSyncProfileCatalog.TryGetProfile(ActiveProfile, out _))
                return Disable(context,
                    $"[Convai LipSync] Profile '{ActiveProfile}' not found. Component disabled.");

            _identitySource ??= context != null ? context.GetComponent<ICharacterIdentitySource>() : null;
            if (_identitySource == null)
                return Disable(context,
                    "[Convai LipSync] No ICharacterIdentitySource found. Component disabled.");

            _resolvedCharacterId = _identitySource.CharacterId?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(_resolvedCharacterId) ||
                   Disable(context,
                       "[Convai LipSync] ICharacterIdentitySource found but CharacterId is empty. Component disabled.");
        }

        private void TryBindRuntimeIfInjected(Component context)
        {
            if (!_isInjected || !EnsureRuntimePrerequisites(context)) return;
            _runtimeController.Bind(_eventHub, _resolvedCharacterId, _logger);
        }

        private static bool Disable(Component context, string message)
        {
            ConvaiLogger.Error(message, LogCategory.LipSync);
            if (context is Behaviour behaviour) behaviour.enabled = false;
            return false;
        }
    }
}
