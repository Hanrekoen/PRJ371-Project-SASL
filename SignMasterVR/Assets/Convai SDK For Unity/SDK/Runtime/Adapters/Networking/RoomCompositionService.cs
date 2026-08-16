using System;
using System.Collections.Generic;
using System.IO;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;
using Convai.Domain.Identity;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Audio;
using Convai.Infrastructure.Networking.Models;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Networking.Media;
using Convai.Shared.Types;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Adapters.Networking
{
    internal sealed class RoomCompositionContext
    {
        public IAgentRegistry AgentRegistry { get; set; }
        public IRoomOwnershipProvider OwnershipProvider { get; set; }
        public IEventHub EventHub { get; set; }
        public IConvaiRoomControllerFactory ControllerFactory { get; set; }
        public ICredentialProvider CredentialProvider { get; set; }
        public IEndUserIdentityProvider EndUserIdentityProvider { get; set; }
        public IEndUserMetadataProvider EndUserMetadataProvider { get; set; }
        public ISessionPersistence SessionPersistence { get; set; }
        public ILogger Logger { get; set; }
        public INarrativeSectionNameResolver SectionNameResolver { get; set; }
        public ConvaiConnectionType ConnectionType { get; set; }
        public string VideoTrackName { get; set; }
        public ConvaiServerEndpoint ServerEndpoint { get; set; }
        public bool Debug { get; set; }
        public bool ConnectOnStart { get; set; }
        public ReconnectPolicy ReconnectPolicy { get; set; }
        public ITransportProvider TransportProvider { get; set; }
        public RemoteAudioPreferenceManager RemoteAudioPreferences { get; set; }
        public RoomControllerEventBinder RoomControllerEventBinder { get; set; }
        public RoomSessionDiagnostics SessionDiagnostics { get; set; }
        public Action<bool> HandleMicMuteChanged { get; set; }
        public Func<IRoomFacade> CurrentRoomProvider { get; set; }
        public Func<(bool hasPublisher, bool hasFrameSource)> GetVisionComponentFlags { get; set; }
        public string PersistentDataPath { get; set; }

        public Func<IReadOnlyList<IConvaiCharacterAgent>, LipSyncTransportOptions> ResolveLipSyncTransportOptions
        {
            get;
            set;
        }

        public Func<Action, bool> PostToMainThread { get; set; }
        public Action EnsureRuntimeSettingsDependencies { get; set; }
    }

    internal sealed class RoomCompositionState
    {
        public bool IsInjected { get; set; }
        public bool HasStarted { get; set; }
        public SessionState CurrentState { get; set; }
        public bool HasPendingOwnershipReconnect { get; set; }
        public string PendingOwnershipRequestedCharacterId { get; set; }
        public ConnectionContext ConnectionContext { get; set; }
        public IConvaiPlayerAgent Player { get; set; }
        public IConvaiCharacterAgent ActiveCharacter { get; set; }
        public IReadOnlyList<IConvaiCharacterAgent> CharacterList { get; set; }
        public IConvaiRoomController RoomController { get; set; }
    }

    internal sealed class RoomCompositionArtifacts
    {
        public IConvaiPlayerAgent Player { get; set; }
        public List<IConvaiCharacterAgent> Characters { get; set; }
        public IConvaiCharacterAgent ActiveCharacter { get; set; }
        public PlayerSessionAdapter PlayerSession { get; set; }
        public IConvaiRoomController RoomController { get; set; }
        public AudioTrackManager AudioTrackManager { get; set; }
        public IDebugMetricsFileWriter DebugMetricsFileWriter { get; set; }
        public ClientLatencyMetricsCollector ClientLatencyMetricsCollector { get; set; }
    }

    internal sealed class RoomCompositionStartupResult
    {
        public bool IsValid { get; set; }
        public bool DisableManager { get; set; }
        public bool ShouldAutoConnect { get; set; }
        public string ErrorMessage { get; set; }
        public RoomStartupFailureReason FailureReason { get; set; }
        public string FailureErrorCode { get; set; }
        public string FailureRecordMessage { get; set; }
        public RoomCompositionArtifacts Artifacts { get; set; }
    }

    internal sealed class RoomOwnershipChangeResult
    {
        public RoomOwnershipRebindOutcome Outcome { get; set; }
        public RoomStartupFailureReason FailureReason { get; set; }
        public string RequestedCharacterId { get; set; }
        public RoomCompositionArtifacts Artifacts { get; set; }
        public bool ClearPendingReconnect { get; set; }
        public bool SetPendingReconnect { get; set; }
        public string PendingReconnectCharacterId { get; set; }
        public bool ResetConnectionContext { get; set; }
        public bool TransitionErrorToDisconnected { get; set; }
    }

    internal sealed class RoomCompositionPreparationResult
    {
        public bool Success { get; set; }
        public RoomStartupFailureReason FailureReason { get; set; }
        public RoomCompositionArtifacts Artifacts { get; set; }
        public RoomOwnershipRebindOutcome? PublishedOutcome { get; set; }
        public string PublishedCharacterId { get; set; }
        public bool ClearPendingReconnect { get; set; }
        public bool ResetConnectionContext { get; set; }
    }

    internal sealed class RoomCompositionService
    {
        public RoomCompositionStartupResult ValidateAndComposeStartup(
            RoomCompositionContext context,
            RoomCompositionState state)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (state == null) throw new ArgumentNullException(nameof(state));

            if (!state.IsInjected)
            {
                return new RoomCompositionStartupResult
                {
                    DisableManager = true,
                    ErrorMessage =
                        "[Convai SDK Setup Error] ConvaiRoomManager dependencies were not injected before Start().\n\n" +
                        "ConvaiRoomManager requires ConvaiManager setup to inject its dependencies.\n\n" +
                        "To fix this:\n" +
                        "1. Add ConvaiManager to your scene:\n" +
                        "   → Use menu: GameObject > Convai > Setup Required Components\n\n" +
                        "2. Verify ConvaiManager is active/enabled in the scene.\n\n" +
                        "📖 See: https://docs.convai.com/api-docs/plugins-and-integrations/convai-unity-sdk/getting-started/installation"
                };
            }

            if (!HasRuntimeCredentials(context))
            {
                return CreateMissingRuntimeCredentialsResult(context.CredentialProvider);
            }

            context.EnsureRuntimeSettingsDependencies?.Invoke();

            RoomStartupCompositionResult composition = ComposeStartupState(context);
            if (!composition.IsSuccess)
            {
                // A scene may intentionally create its characters from prefabs at runtime.
                // Complete room-manager startup without connecting so a later ownership
                // refresh can compose the room once the first character is registered.
                if (composition.FailureReason == RoomStartupFailureReason.MissingCharacters)
                {
                    return new RoomCompositionStartupResult
                    {
                        IsValid = true,
                        ShouldAutoConnect = false,
                        FailureReason = RoomStartupFailureReason.MissingCharacters
                    };
                }

                return CreateFailureResult(composition.FailureReason, context.CredentialProvider);
            }

            return new RoomCompositionStartupResult
            {
                IsValid = true,
                // A project may intentionally supply its auth token through ConnectWithAuthTokenAsync.
                // In that case composition must finish, but there is no credential available for auto-connect.
                ShouldAutoConnect = context.ConnectOnStart &&
                                    context.CredentialProvider?.HasValidCredentials == true,
                Artifacts = CreateArtifacts(context, composition)
            };
        }

        public RoomOwnershipChangeResult HandleOwnedAgentStateChanged(
            RoomCompositionContext context,
            RoomCompositionState state)
        {
            string requestedCharacterId = CaptureRequestedOwnershipCharacterId(context);

            if (!state.IsInjected || !state.HasStarted)
            {
                return new RoomOwnershipChangeResult
                {
                    Outcome = RoomOwnershipRebindOutcome.DeferredUntilStartup,
                    RequestedCharacterId = requestedCharacterId
                };
            }

            switch (state.CurrentState)
            {
                case SessionState.Disconnected:
                case SessionState.Error:
                    {
                        RoomStartupCompositionResult composition = ComposeLatestState(context);
                        if (!composition.IsSuccess)
                        {
                            return new RoomOwnershipChangeResult
                            {
                                Outcome = RoomOwnershipRebindOutcome.RejectedInvalidOwnership,
                                RequestedCharacterId = requestedCharacterId,
                                FailureReason = composition.FailureReason,
                                ClearPendingReconnect = true
                            };
                        }

                        return new RoomOwnershipChangeResult
                        {
                            Outcome = RoomOwnershipRebindOutcome.AppliedImmediately,
                            RequestedCharacterId = requestedCharacterId,
                            Artifacts = CreateArtifacts(context, composition),
                            ClearPendingReconnect = true,
                            ResetConnectionContext = true,
                            TransitionErrorToDisconnected = state.CurrentState == SessionState.Error
                        };
                    }

                case SessionState.Connected:
                    return new RoomOwnershipChangeResult
                    {
                        Outcome = RoomOwnershipRebindOutcome.PendingReconnect,
                        RequestedCharacterId = requestedCharacterId,
                        SetPendingReconnect = true,
                        PendingReconnectCharacterId = requestedCharacterId ?? string.Empty
                    };

                case SessionState.Connecting:
                case SessionState.Reconnecting:
                case SessionState.Disconnecting:
                default:
                    return new RoomOwnershipChangeResult
                    {
                        Outcome = RoomOwnershipRebindOutcome.RejectedTransitionState,
                        RequestedCharacterId = requestedCharacterId
                    };
            }
        }

        public RoomCompositionPreparationResult PrepareOwnershipCompositionForNextConnect(
            RoomCompositionContext context,
            RoomCompositionState state)
        {
            if (state.HasPendingOwnershipReconnect)
                return ConsumePendingOwnershipReconnect(context, state);

            if (state.RoomController != null && OwnershipMatchesCurrentComposition(context, state))
            {
                return new RoomCompositionPreparationResult { Success = true };
            }

            RoomStartupCompositionResult composition = ComposeLatestState(context);
            if (!composition.IsSuccess)
            {
                return new RoomCompositionPreparationResult { FailureReason = composition.FailureReason };
            }

            return new RoomCompositionPreparationResult
            {
                Success = true, Artifacts = CreateArtifacts(context, composition)
            };
        }

        public void LogOwnershipRebindFailure(ILogger logger, RoomStartupFailureReason failureReason)
        {
            switch (failureReason)
            {
                case RoomStartupFailureReason.MissingOwnershipProvider:
                    logger?.Warning(
                        "Ownership rebind rejected because no IRoomOwnershipProvider is available.");
                    break;
                case RoomStartupFailureReason.MissingPlayer:
                    logger?.Warning("Ownership rebind rejected because no owned player resolved.");
                    break;
                case RoomStartupFailureReason.MissingCharacters:
                    logger?.Warning(
                        "Ownership rebind rejected because no owned characters resolved.");
                    break;
                case RoomStartupFailureReason.MissingRuntimeCredentials:
                    logger?.Warning(
                        "Ownership rebind rejected because runtime credentials are unavailable.");
                    break;
                case RoomStartupFailureReason.MissingConversationTarget:
                    logger?.Warning(
                        "Ownership rebind rejected because no valid active conversation target resolved.");
                    break;
                case RoomStartupFailureReason.MissingControllerFactory:
                    logger?.Warning(
                        "Ownership rebind rejected because no room controller factory is registered.");
                    break;
                case RoomStartupFailureReason.ControllerCreationFailed:
                    logger?.Warning(
                        "Ownership rebind rejected because room controller creation failed.");
                    break;
            }
        }

        private RoomCompositionPreparationResult ConsumePendingOwnershipReconnect(
            RoomCompositionContext context,
            RoomCompositionState state)
        {
            string requestedCharacterId = state.PendingOwnershipRequestedCharacterId;
            string previousCharacterId = state.ActiveCharacter?.CharacterId ?? state.ConnectionContext?.CharacterId;

            RoomStartupCompositionResult composition = ComposeLatestState(context);
            if (!composition.IsSuccess)
            {
                return new RoomCompositionPreparationResult
                {
                    FailureReason = composition.FailureReason,
                    PublishedOutcome = RoomOwnershipRebindOutcome.RejectedInvalidOwnership,
                    PublishedCharacterId = requestedCharacterId
                };
            }

            string activeCharacterId = composition.ActiveCharacter?.CharacterId;
            return new RoomCompositionPreparationResult
            {
                Success = true,
                Artifacts = CreateArtifacts(context, composition),
                ClearPendingReconnect = true,
                ResetConnectionContext =
                    !string.Equals(previousCharacterId, activeCharacterId, StringComparison.Ordinal),
                PublishedOutcome = RoomOwnershipRebindOutcome.AppliedImmediately,
                PublishedCharacterId = requestedCharacterId ?? activeCharacterId
            };
        }

        private RoomStartupCompositionResult ComposeLatestState(RoomCompositionContext context)
        {
            if (!HasRuntimeCredentials(context))
                return RoomStartupCompositionResult.Failure(RoomStartupFailureReason.MissingRuntimeCredentials);

            context.EnsureRuntimeSettingsDependencies?.Invoke();
            return ComposeStartupState(context);
        }

        private RoomStartupCompositionResult ComposeStartupState(RoomCompositionContext context) =>
            new RoomStartupComposer().Compose(new RoomStartupCompositionRequest
            {
                AgentRegistry = context.AgentRegistry,
                OwnershipProvider = context.OwnershipProvider,
                EventHub = context.EventHub,
                ControllerFactory = context.ControllerFactory,
                CredentialProvider = context.CredentialProvider,
                EndUserIdentityProvider = context.EndUserIdentityProvider,
                EndUserMetadataProvider = context.EndUserMetadataProvider,
                SessionPersistence = context.SessionPersistence,
                Logger = context.Logger,
                SectionNameResolver = context.SectionNameResolver,
                ConnectionType = context.ConnectionType,
                VideoTrackName = context.VideoTrackName,
                ServerEndpoint = context.ServerEndpoint,
                Debug = context.Debug,
                ResolveLipSyncTransportOptions = context.ResolveLipSyncTransportOptions,
                PostToMainThread = context.PostToMainThread
            });

        private RoomCompositionArtifacts CreateArtifacts(
            RoomCompositionContext context,
            RoomStartupCompositionResult composition)
        {
            WarnIfVisionComponentsMissing(context);

            Func<string, bool> shouldSubscribe = _ => true;
            if (context.RemoteAudioPreferences != null)
                shouldSubscribe = context.RemoteAudioPreferences.ShouldSubscribe;

            composition.RoomController.SetAudioSubscriptionPolicy(shouldSubscribe);

            AudioSource AudioSourceResolver(string characterId) =>
                context.AgentRegistry.TryGetAudioSource(characterId, out AudioSource source) ? source : null;

            IAudioStreamFactory audioStreamFactory = context.TransportProvider?.CreateAudioStreamFactory();
            var audioTrackManager = new AudioTrackManager(
                context.CurrentRoomProvider,
                context.AgentRegistry,
                context.Logger,
                AudioSourceResolver,
                audioStreamFactory: audioStreamFactory,
                eventHub: context.EventHub);
            if (context.HandleMicMuteChanged != null)
                audioTrackManager.OnMicMuteChanged += context.HandleMicMuteChanged;

            IDebugMetricsFileWriter debugMetricsFileWriter = null;
            if (context.Debug)
            {
                string metricsPath = Path.Combine(
                    context.PersistentDataPath,
                    "ConvaiDebugMetrics_" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".txt");
                debugMetricsFileWriter = new DebugMetricsFileWriter(metricsPath);
                context.Logger?.Info($"Debug metrics logging to: {metricsPath}");
            }

            var latencyCollector = new ClientLatencyMetricsCollector(
                context.EventHub,
                () => context.Debug,
                context.Logger,
                debugMetricsFileWriter);
            latencyCollector.Bind();

            return new RoomCompositionArtifacts
            {
                Player = composition.Player,
                Characters = composition.Characters,
                ActiveCharacter = composition.ActiveCharacter,
                PlayerSession = composition.PlayerSession,
                RoomController = composition.RoomController,
                AudioTrackManager = audioTrackManager,
                DebugMetricsFileWriter = debugMetricsFileWriter,
                ClientLatencyMetricsCollector = latencyCollector
            };
        }

        private RoomCompositionStartupResult CreateFailureResult(
            RoomStartupFailureReason failureReason,
            ICredentialProvider credentialProvider)
        {
            return failureReason switch
            {
                RoomStartupFailureReason.MissingOwnershipProvider => new RoomCompositionStartupResult
                {
                    FailureReason = failureReason,
                    ErrorMessage =
                        "[ConvaiRoomManager] IRoomOwnershipProvider was not injected before room startup. Ensure ConvaiManager initialized the runtime context and composed room services before Start()."
                },
                RoomStartupFailureReason.MissingPlayer => new RoomCompositionStartupResult
                {
                    FailureReason = failureReason,
                    ErrorMessage = "[ConvaiRoomManager] Failed to resolve an owned player before room startup.",
                    FailureErrorCode = SessionErrorCodes.ConnectionFailed,
                    FailureRecordMessage = "Failed to connect to Convai room"
                },
                RoomStartupFailureReason.MissingCharacters => new RoomCompositionStartupResult
                {
                    FailureReason = failureReason,
                    ErrorMessage =
                        "[ConvaiRoomManager] Failed to resolve any owned characters before room startup.",
                    FailureErrorCode = SessionErrorCodes.ConnectionFailed,
                    FailureRecordMessage = "Failed to connect to Convai room"
                },
                RoomStartupFailureReason.MissingRuntimeCredentials =>
                    CreateMissingRuntimeCredentialsResult(credentialProvider),
                RoomStartupFailureReason.MissingConversationTarget => new RoomCompositionStartupResult
                {
                    FailureReason = failureReason,
                    ErrorMessage =
                        "[ConvaiRoomManager] Room ownership did not resolve an active conversation target. Assign an explicit conversation target in ConvaiManager, or ensure ownership resolves to a single character before startup.",
                    FailureErrorCode = SessionErrorCodes.ConnectionFailed,
                    FailureRecordMessage = "Failed to connect to Convai room"
                },
                RoomStartupFailureReason.MissingControllerFactory => new RoomCompositionStartupResult
                {
                    FailureReason = failureReason,
                    ErrorMessage =
                        "[ConvaiRoomManager] IConvaiRoomControllerFactory not registered. Platform networking services were not composed before room startup. Ensure ConvaiManager completed bootstrap successfully and that the current platform networking assembly was preserved in the player build."
                },
                RoomStartupFailureReason.ControllerCreationFailed => new RoomCompositionStartupResult
                {
                    FailureReason = failureReason,
                    ErrorMessage = "[ConvaiRoomManager] Failed to create room controller."
                },
                _ => new RoomCompositionStartupResult { FailureReason = failureReason }
            };
        }

        private void WarnIfVisionComponentsMissing(RoomCompositionContext context)
        {
            if (context.ConnectionType != ConvaiConnectionType.Video || context.GetVisionComponentFlags == null)
                return;

            (bool hasPublisher, bool hasFrameSource) = context.GetVisionComponentFlags();
            if (hasPublisher && hasFrameSource)
                return;

            context.Logger?.Warning(
                "ConnectionType is Video but vision components are missing. Add ConvaiVisionPublisher and CameraVisionFrameSource (or another IVisionFrameSource) under this RoomManager to enable vision streaming.");
        }

        private bool OwnershipMatchesCurrentComposition(RoomCompositionContext context, RoomCompositionState state)
        {
            if (context.OwnershipProvider == null)
                return false;

            RoomOwnershipSnapshot ownership = context.OwnershipProvider.CaptureOwnership();
            IReadOnlyList<IConvaiCharacterAgent> ownedCharacters =
                ownership?.Characters ?? Array.Empty<IConvaiCharacterAgent>();

            return ReferenceEquals(state.Player, ownership?.Player)
                   && ReferenceEquals(state.ActiveCharacter, ownership?.ConversationTarget?.Character)
                   && HaveSameCharacterReferences(state.CharacterList, ownedCharacters);
        }

        private static bool HasRuntimeCredentials(RoomCompositionContext context)
        {
            return context.CredentialProvider?.HasValidCredentials == true ||
                   context.CredentialProvider is IExplicitAuthTokenCredentialProvider;
        }

        private static RoomCompositionStartupResult CreateMissingRuntimeCredentialsResult(
            ICredentialProvider credentialProvider)
        {
            var configurationStatus = credentialProvider as ICredentialConfigurationStatus;
            string errorCode = string.IsNullOrWhiteSpace(configurationStatus?.ConfigurationErrorCode)
                ? SessionErrorCodes.ConfigApiKeyMissing
                : configurationStatus.ConfigurationErrorCode;
            string detail = string.IsNullOrWhiteSpace(configurationStatus?.ConfigurationErrorMessage)
                ? "Runtime credentials are not configured"
                : configurationStatus.ConfigurationErrorMessage;
            string sentence = detail.TrimEnd().TrimEnd('.') + ".";

            return new RoomCompositionStartupResult
            {
                FailureReason = RoomStartupFailureReason.MissingRuntimeCredentials,
                ErrorMessage =
                    $"[ConvaiRoomManager] {sentence} Configure credentials via Edit > Project Settings > Convai SDK, or register a custom credential provider before room startup.",
                FailureErrorCode = errorCode,
                FailureRecordMessage = detail
            };
        }

        private static string CaptureRequestedOwnershipCharacterId(RoomCompositionContext context) =>
            context.OwnershipProvider?.CaptureOwnership()?.ConversationTarget?.Character?.CharacterId;

        private static bool HaveSameCharacterReferences(
            IReadOnlyList<IConvaiCharacterAgent> currentCharacters,
            IReadOnlyList<IConvaiCharacterAgent> candidateCharacters)
        {
            int currentCount = currentCharacters?.Count ?? 0;
            int candidateCount = candidateCharacters?.Count ?? 0;
            if (currentCount != candidateCount)
                return false;

            for (int i = 0; i < currentCount; i++)
            {
                if (!ContainsReference(candidateCharacters, currentCharacters[i]))
                    return false;
            }

            return true;
        }

        private static bool ContainsReference(
            IReadOnlyList<IConvaiCharacterAgent> characters,
            IConvaiCharacterAgent candidate)
        {
            if (characters == null)
                return false;

            for (int i = 0; i < characters.Count; i++)
            {
                if (ReferenceEquals(characters[i], candidate))
                    return true;
            }

            return false;
        }
    }
}
