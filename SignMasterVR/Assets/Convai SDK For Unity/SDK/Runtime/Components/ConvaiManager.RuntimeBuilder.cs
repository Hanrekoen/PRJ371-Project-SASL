using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Identity;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Core.Modules;
using Convai.Runtime.Core.Providers;
using Convai.Runtime.Core.Registry;
using Convai.Runtime.Logging;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Deferred room runtime that delegates to the actual room runtime from <see cref="ConvaiRoomManager" />.
    /// </summary>
    internal sealed class DeferredRoomRuntime : IRoomRuntime
    {
        private readonly ConvaiManager _manager;

        public DeferredRoomRuntime(ConvaiManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        private IRoomRuntime Underlying
        {
            get
            {
                if (!_manager.TryGetRoomManager(out ConvaiRoomManager roomManager))
                    throw new InvalidOperationException("Room runtime accessed before ConvaiRoomManager is available.");

                IRoomRuntime runtime = roomManager.GetRoomRuntime();
                if (runtime == null)
                    throw new InvalidOperationException("Room runtime not initialized.");

                return runtime;
            }
        }

        public bool IsActive => Underlying.IsActive;
        public RoomSession Session => Underlying.Session;
        public IRoomConnectionCoordinator Connection => Underlying.Connection;
        public IRoomAudioCoordinator Audio => Underlying.Audio;
        public IRoomOwnershipCoordinator Ownership => Underlying.Ownership;
        public IRoomDiagnostics Diagnostics => Underlying.Diagnostics;

        public IConvaiOperation<RoomSession> ConnectAsync(CancellationToken ct = default) =>
            Underlying.ConnectAsync(ct);

        public IConvaiOperation<Unit> DisconnectAsync(CancellationToken ct = default) =>
            Underlying.DisconnectAsync(ct);

        public void Initialize(RoomSession session) => Underlying.Initialize(session);
        public void Shutdown() => Underlying.Shutdown();
    }

    /// <summary>
    ///     ConvaiManager partial: ConvaiRuntimeBuilder integration.
    /// </summary>
    public partial class ConvaiManager
    {
        private IEndUserIdentityProvider _endUserIdentityProvider;
        private IEndUserMetadataProvider _endUserMetadataProvider;
        private readonly Dictionary<string, bool> _backgroundCharacterMuteStates = new();
        private readonly Dictionary<string, PausedCharacterAudioState> _pausedCharacterAudioStates = new();
        private readonly HashSet<RuntimePauseReason> _timelinePauseReasons = new();
        private readonly SemaphoreSlim _sessionLifecycleGate = new(1, 1);
        private RuntimeBackgroundPolicy _backgroundPolicy = RuntimeBackgroundPolicy.PauseTimeline;
        private RuntimeBackgroundPolicy _effectiveBackgroundPolicy = RuntimeBackgroundPolicy.PauseTimeline;
        private RuntimePauseReason _activeBackgroundReason = RuntimePauseReason.ApplicationBackground;
        private bool _hasRunInBackgroundSnapshot;
        private bool _hasTranscriptPresentationSnapshot;
        private bool _isApplicationBackgrounded;
        private bool _previousRunInBackground;
        private bool _previousTranscriptPresentationEnabled;
        private UnityConvaiAdapter _unityAdapter;

        private sealed class PausedCharacterAudioState
        {
            public AudioSource Source;
            public bool ShouldResume;
        }

        public ConvaiRuntime ConvaiRuntime { get; private set; }

        public bool IsUsingRuntimeBuilder => ConvaiRuntime != null;

        /// <summary>The configured application background policy.</summary>
        public RuntimeBackgroundPolicy BackgroundPolicy => _backgroundPolicy;

        /// <summary>The policy currently applied after platform fallbacks.</summary>
        public RuntimeBackgroundPolicy EffectiveBackgroundPolicy => _effectiveBackgroundPolicy;

        /// <summary>Whether Unity has reported that the application is currently backgrounded.</summary>
        public bool IsApplicationBackgrounded => _isApplicationBackgrounded;

        /// <summary>Whether Convai runtime modules are currently paused.</summary>
        public bool IsRuntimePaused => ConvaiRuntime?.State == RuntimeState.Paused;

        protected virtual ConvaiRuntimeBuilder CreateRuntimeBuilder()
        {
            var builder = new ConvaiRuntimeBuilder();

            ConvaiLogger.Initialize();
            ILogger logger = new ConvaiLogger();
            builder.UseLogger(logger);

            IEventHub eventHub = new EventHub(UnityScheduler.Instance, logger);
            builder.UseEventHub(eventHub);

            IAgentRegistry agentRegistry = new AgentRegistry();
            builder.UseAgentRegistry(agentRegistry);

            builder.UseRoomRuntime(() => new DeferredRoomRuntime(this));

            ITransportProvider transportProvider = GetPlatformTransportProvider();
            if (transportProvider != null)
                builder.UseTransport(transportProvider);

            IConversationProvider conversationProvider = GetConversationProvider();
            if (conversationProvider != null)
                builder.UseConversation(conversationProvider);

            if (_endUserIdentityProvider != null)
                builder.WithEndUserIdentityProvider(_endUserIdentityProvider);

            if (_endUserMetadataProvider != null)
                builder.WithEndUserMetadataProvider(_endUserMetadataProvider);

            ConvaiSettings settings = ConvaiSettings.Instance;
            if (settings != null)
            {
                builder.WithRuntimePreferences(new RuntimePreferences
                {
                    TranscriptEnabled = settings.TranscriptSystemEnabled,
                    NotificationsEnabled = settings.NotificationSystemEnabled,
                    CharacterAudioVolume = settings.CharacterAudioVolume,
                    AudioFeedbackEnabled = settings.AudioFeedbackEnabled,
                    PreferredMicrophoneDeviceId = settings.DefaultMicrophoneDeviceId
                });
            }

            return builder;
        }

        protected virtual ConvaiRuntime BuildRuntime()
        {
            if (ConvaiRuntime != null)
            {
                if (_debugLogging)
                    ConvaiLogger.Warning("Runtime already built.", LogCategory.Bootstrap);
                return ConvaiRuntime;
            }

            ConvaiRuntimeBuilder builder = CreateRuntimeBuilder();
            ConvaiRuntime = builder.Build();

            if (_debugLogging)
                ConvaiLogger.Debug("ConvaiRuntime built via builder.", LogCategory.Bootstrap);

            return ConvaiRuntime;
        }

        protected virtual void InitializeUnityAdapter()
        {
            if (ConvaiRuntime == null)
            {
                ConvaiLogger.Warning("Cannot initialize adapter: runtime not built.",
                    LogCategory.Bootstrap);
                return;
            }

            _unityAdapter = GetComponent<UnityConvaiAdapter>() ?? gameObject.AddComponent<UnityConvaiAdapter>();
            _unityAdapter.Initialize(ConvaiRuntime);
            _backgroundPolicy = ConvaiSettings.Instance?.BackgroundPolicy ?? RuntimeBackgroundPolicy.PauseTimeline;
            _effectiveBackgroundPolicy = _backgroundPolicy;
            _unityAdapter.SetApplicationLifecycleHandler(HandleApplicationLifecycleStateChanged);

            if (_debugLogging)
                ConvaiLogger.Debug("UnityConvaiAdapter initialized.", LogCategory.Bootstrap);
        }

        public async void StartRuntimeAsync()
        {
            if (_unityAdapter == null)
            {
                ConvaiLogger.Warning("Cannot start: adapter not initialized.", LogCategory.Bootstrap);
                return;
            }

            await _unityAdapter.StartRuntimeAsync();

            // Subscribe to facade events after runtime starts (events now flowing)
            _host?.EnsureFacades(_roomManager);
            SubscribeToFacadeEvents();

            if (_debugLogging)
                ConvaiLogger.Debug("ConvaiRuntime started.", LogCategory.Bootstrap);
        }

        private void DiscoverAndAddModules()
        {
            if (ConvaiRuntime == null || _host == null) return;

            IReadOnlyList<IConvaiModule> modules = _host.RegisteredModules;
            if (modules.Count == 0) return;

            ConvaiRuntime.AddModules(modules);

            if (_debugLogging)
                ConvaiLogger.Debug($"Added {modules.Count} module(s) to runtime.",
                    LogCategory.Bootstrap);
        }

        private void DisposeBuilderRuntime()
        {
            if (ConvaiRuntime != null)
            {
                try
                {
                    ConvaiRuntime.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error($"Error disposing runtime: {ex.Message}", LogCategory.Bootstrap);
                }

                ConvaiRuntime = null;
            }

            _unityAdapter = null;
        }

        /// <summary>
        ///     Changes the policy used for subsequent application focus/background transitions.
        ///     If the application is already backgrounded, the new policy is applied immediately.
        /// </summary>
        public IConvaiOperation<Unit> SetBackgroundPolicyAsync(
            RuntimeBackgroundPolicy policy,
            CancellationToken cancellationToken = default) =>
            ConvaiOperation<Unit>.FromTask(SetBackgroundPolicyAsyncCore(policy, cancellationToken));

        /// <summary>
        ///     Pauses local audio, shipped transcript presentation, and runtime modules while keeping the room connected.
        /// </summary>
        public IConvaiOperation<Unit> PauseAsync(CancellationToken cancellationToken = default) =>
            ConvaiOperation<Unit>.FromTask(SetTimelinePauseAsync(
                RuntimePauseReason.UserRequested,
                true,
                cancellationToken));

        /// <summary>Resumes a pause requested through <see cref="PauseAsync" />.</summary>
        public IConvaiOperation<Unit> ResumeAsync(CancellationToken cancellationToken = default) =>
            ConvaiOperation<Unit>.FromTask(SetTimelinePauseAsync(
                RuntimePauseReason.UserRequested,
                false,
                cancellationToken));

        /// <summary>
        ///     Performs an explicit disconnect/connect cycle using the configured reconnect and session-resume policy.
        /// </summary>
        public IConvaiOperation<RoomSession> ReconnectAsync(CancellationToken cancellationToken = default) =>
            ConvaiOperation<RoomSession>.FromTask(ReconnectAsyncCore(cancellationToken));

        /// <summary>Sends reset-idle-timer for the current connected room.</summary>
        public bool ResetIdleTimer()
        {
            bool sent = _roomManager?.ResetIdleTimer() ?? false;
            if (sent) ClearIdleDeadline();
            return sent;
        }

        /// <summary>Alias for <see cref="ResetIdleTimer" /> for UI wording such as "Continue session".</summary>
        public bool ExtendIdleTimeout() => ResetIdleTimer();

        private async Task<Unit> SetBackgroundPolicyAsyncCore(
            RuntimeBackgroundPolicy policy,
            CancellationToken cancellationToken)
        {
            if (!Enum.IsDefined(typeof(RuntimeBackgroundPolicy), policy))
                throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown background policy.");

            await _sessionLifecycleGate.WaitAsync(cancellationToken);
            try
            {
                if (_backgroundPolicy == policy)
                    return Unit.Value;

                if (_isApplicationBackgrounded)
                    await ExitBackgroundPolicyAsync(cancellationToken);

                _backgroundPolicy = policy;
                _effectiveBackgroundPolicy = policy;

                if (_isApplicationBackgrounded)
                    await EnterBackgroundPolicyAsync(_activeBackgroundReason, cancellationToken);

                PublishBackgroundStateChanged(_activeBackgroundReason);
                return Unit.Value;
            }
            finally
            {
                _sessionLifecycleGate.Release();
            }
        }

        private async Task<Unit> SetTimelinePauseAsync(
            RuntimePauseReason reason,
            bool paused,
            CancellationToken cancellationToken)
        {
            await _sessionLifecycleGate.WaitAsync(cancellationToken);
            try
            {
                if (paused)
                    await AddTimelinePauseReasonAsync(reason, cancellationToken);
                else
                    await RemoveTimelinePauseReasonAsync(reason, cancellationToken);

                return Unit.Value;
            }
            finally
            {
                _sessionLifecycleGate.Release();
            }
        }

        private async Task<RoomSession> ReconnectAsyncCore(CancellationToken cancellationToken)
        {
            if (_roomManager == null)
                throw new InvalidOperationException("ConvaiRoomManager is not available.");

            SessionState state = _roomManager.CurrentState;
            if (state is SessionState.Connecting or SessionState.Disconnecting)
                throw new InvalidOperationException($"Cannot reconnect while the session is {state}.");

            if (state != SessionState.Disconnected)
                await _roomManager.DisconnectAsync(cancellationToken: cancellationToken);

            return await _roomManager.ConnectAsync(cancellationToken);
        }

        private async void HandleApplicationLifecycleStateChanged(bool isBackgrounded, RuntimePauseReason reason)
        {
            try
            {
                await ApplyApplicationBackgroundStateAsync(isBackgrounded, reason, CancellationToken.None);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"Failed to apply application background policy: {ex.Message}", LogCategory.SDK);
            }
        }

        private async Task ApplyApplicationBackgroundStateAsync(
            bool isBackgrounded,
            RuntimePauseReason reason,
            CancellationToken cancellationToken)
        {
            await _sessionLifecycleGate.WaitAsync(cancellationToken);
            try
            {
                if (_isApplicationBackgrounded == isBackgrounded)
                    return;

                if (isBackgrounded)
                {
                    _isApplicationBackgrounded = true;
                    _activeBackgroundReason = reason;
                    await EnterBackgroundPolicyAsync(reason, cancellationToken);
                }
                else
                {
                    await ExitBackgroundPolicyAsync(cancellationToken);
                    _isApplicationBackgrounded = false;
                }

                PublishBackgroundStateChanged(reason);
            }
            finally
            {
                _sessionLifecycleGate.Release();
            }
        }

        private async Task EnterBackgroundPolicyAsync(
            RuntimePauseReason reason,
            CancellationToken cancellationToken)
        {
            _effectiveBackgroundPolicy = ResolveEffectiveBackgroundPolicy(
                _backgroundPolicy,
                ShouldUseWebGLTransportProvider());

            switch (_effectiveBackgroundPolicy)
            {
                case RuntimeBackgroundPolicy.PauseTimeline:
                    await AddTimelinePauseReasonAsync(reason, cancellationToken);
                    break;

                case RuntimeBackgroundPolicy.MuteButCatchUp:
                    RequestBackgroundExecution();
                    ApplyBackgroundCharacterMute();
                    break;

                default:
                    RequestBackgroundExecution();
                    break;
            }
        }

        private async Task ExitBackgroundPolicyAsync(CancellationToken cancellationToken)
        {
            switch (_effectiveBackgroundPolicy)
            {
                case RuntimeBackgroundPolicy.PauseTimeline:
                    await RemoveTimelinePauseReasonAsync(_activeBackgroundReason, cancellationToken);
                    break;

                case RuntimeBackgroundPolicy.MuteButCatchUp:
                    RestoreBackgroundCharacterMute();
                    RestoreBackgroundExecutionRequest();
                    break;

                default:
                    RestoreBackgroundExecutionRequest();
                    break;
            }

            _effectiveBackgroundPolicy = _backgroundPolicy;
        }

        private async Task AddTimelinePauseReasonAsync(
            RuntimePauseReason reason,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_timelinePauseReasons.Add(reason) || _timelinePauseReasons.Count > 1)
                return;

            PauseCharacterAudioSources();

            _host?.EnsureFacades(_roomManager);
            if (_host?.Transcripts != null)
            {
                _previousTranscriptPresentationEnabled = _host.Transcripts.IsPresentationEnabled;
                _hasTranscriptPresentationSnapshot = true;
                _host.Transcripts.SetPresentationEnabled(false);
            }

            if (_unityAdapter != null)
                await _unityAdapter.PauseRuntimeAsync(reason);
        }

        private async Task RemoveTimelinePauseReasonAsync(
            RuntimePauseReason reason,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_timelinePauseReasons.Remove(reason) || _timelinePauseReasons.Count > 0)
                return;

            if (_unityAdapter != null)
                await _unityAdapter.ResumeRuntimeAsync();

            ResumeCharacterAudioSources();

            if (_hasTranscriptPresentationSnapshot)
            {
                _host?.Transcripts?.SetPresentationEnabled(_previousTranscriptPresentationEnabled);
                _hasTranscriptPresentationSnapshot = false;
            }
        }

        private void RequestBackgroundExecution()
        {
            if (!_hasRunInBackgroundSnapshot)
            {
                _previousRunInBackground = UnityEngine.Application.runInBackground;
                _hasRunInBackgroundSnapshot = true;
            }

            UnityEngine.Application.runInBackground = true;
        }

        private void RestoreBackgroundExecutionRequest()
        {
            if (!_hasRunInBackgroundSnapshot)
                return;

            UnityEngine.Application.runInBackground = _previousRunInBackground;
            _hasRunInBackgroundSnapshot = false;
        }

        private void ApplyBackgroundCharacterMute()
        {
            if (_roomManager == null || !TryGetAgentRegistry(out IAgentRegistry registry))
                return;

            foreach (IConvaiCharacterAgent character in registry.Characters)
            {
                string characterId = character?.CharacterId;
                if (string.IsNullOrWhiteSpace(characterId) || _backgroundCharacterMuteStates.ContainsKey(characterId))
                    continue;

                _backgroundCharacterMuteStates[characterId] = _roomManager.IsCharacterMuted(characterId);
                _roomManager.SetCharacterMuted(characterId, true);
            }
        }

        private void PauseCharacterAudioSources()
        {
            if (!TryGetAgentRegistry(out IAgentRegistry registry))
                return;

            foreach (IConvaiCharacterAgent character in registry.Characters)
            {
                string characterId = character?.CharacterId;
                if (string.IsNullOrWhiteSpace(characterId) ||
                    !registry.TryGetAudioSource(characterId, out AudioSource source) ||
                    source == null)
                    continue;

                if (!_pausedCharacterAudioStates.TryGetValue(characterId, out PausedCharacterAudioState state) ||
                    state.Source != source)
                {
                    if (state?.Source != null && state.ShouldResume)
                        state.Source.UnPause();

                    state = new PausedCharacterAudioState { Source = source };
                    _pausedCharacterAudioStates[characterId] = state;
                }

                if (!source.isPlaying)
                    continue;

                state.ShouldResume = true;
                source.Pause();
            }
        }

        private void ResumeCharacterAudioSources()
        {
            foreach (PausedCharacterAudioState state in _pausedCharacterAudioStates.Values)
                if (state?.Source != null && state.ShouldResume)
                    state.Source.UnPause();

            _pausedCharacterAudioStates.Clear();
        }

        private void RestoreBackgroundCharacterMute()
        {
            if (_roomManager != null)
            {
                foreach (KeyValuePair<string, bool> entry in _backgroundCharacterMuteStates)
                    _roomManager.SetCharacterMuted(entry.Key, entry.Value);
            }

            _backgroundCharacterMuteStates.Clear();
        }

        internal static RuntimeBackgroundPolicy ResolveEffectiveBackgroundPolicy(
            RuntimeBackgroundPolicy requestedPolicy,
            bool isWebGl)
        {
            return isWebGl && requestedPolicy == RuntimeBackgroundPolicy.PauseTimeline
                ? RuntimeBackgroundPolicy.MuteButCatchUp
                : requestedPolicy;
        }

        private void PublishBackgroundStateChanged(RuntimePauseReason reason)
        {
            _host?.Events?.Raw.Publish(RuntimeBackgroundStateChanged.Create(
                _isApplicationBackgrounded,
                _backgroundPolicy,
                _effectiveBackgroundPolicy,
                reason));
        }

        private void RestoreSessionLifecycleState()
        {
            RestoreBackgroundCharacterMute();
            RestoreBackgroundExecutionRequest();
            ResumeCharacterAudioSources();

            if (_hasTranscriptPresentationSnapshot)
                _host?.Transcripts?.SetPresentationEnabled(_previousTranscriptPresentationEnabled);

            _hasTranscriptPresentationSnapshot = false;
            _timelinePauseReasons.Clear();
            _isApplicationBackgrounded = false;
        }

        protected virtual ITransportProvider GetPlatformTransportProvider()
        {
            string typeName = ShouldUseWebGLTransportProvider()
                ? "Convai.Infrastructure.Networking.WebGL.WebGLTransportProvider, Convai.Transport.WebGL"
                : "Convai.Infrastructure.Networking.Native.NativeTransportProvider, Convai.Transport.Native";
            var providerType = Type.GetType(typeName);
            if (providerType == null)
            {
                if (_debugLogging)
                    ConvaiLogger.Debug($"Transport provider type not found: {typeName}",
                        LogCategory.Bootstrap);
                return null;
            }

            PropertyInfo instanceProp = providerType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            if (instanceProp != null)
                return instanceProp.GetValue(null) as ITransportProvider;

            return Activator.CreateInstance(providerType) as ITransportProvider;
        }

        private static bool ShouldUseWebGLTransportProvider() =>
            !UnityEngine.Application.isEditor && UnityEngine.Application.platform == RuntimePlatform.WebGLPlayer;

        protected virtual IConversationProvider GetConversationProvider() =>
            ConvaiConversationProvider.Instance;

        /// <summary>
        ///     Overrides the runtime end-user identity provider used for room connections.
        /// </summary>
        public void SetEndUserIdentityProvider(IEndUserIdentityProvider provider)
        {
            _endUserIdentityProvider = provider;
            _host?.SetEndUserIdentityProvider(provider);
        }

        /// <summary>
        ///     Overrides the runtime end-user metadata provider used for room connections.
        /// </summary>
        public void SetEndUserMetadataProvider(IEndUserMetadataProvider provider)
        {
            _endUserMetadataProvider = provider;
            _host?.SetEndUserMetadataProvider(provider);
        }
    }
}
