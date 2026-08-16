using System;
using System.Collections.Generic;
using Convai.Domain.Abstractions;
using Convai.Domain.EventSystem;
using Convai.Domain.Identity;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Configuration;
using Convai.Shared.Types;

namespace Convai.Runtime.Adapters.Networking
{
    internal enum RoomStartupFailureReason
    {
        None = 0,
        MissingOwnershipProvider,
        MissingPlayer,
        MissingCharacters,
        MissingConversationTarget,
        MissingRuntimeCredentials,
        MissingControllerFactory,
        ControllerCreationFailed
    }

    internal sealed class ActiveConversationTarget
    {
        public ActiveConversationTarget(IConvaiCharacterAgent character)
        {
            Character = character ?? throw new ArgumentNullException(nameof(character));
        }

        public IConvaiCharacterAgent Character { get; }
    }

    internal sealed class RoomOwnershipSnapshot
    {
        public RoomOwnershipSnapshot(
            IConvaiPlayerAgent player,
            IReadOnlyList<IConvaiCharacterAgent> characters,
            ActiveConversationTarget conversationTarget = null)
        {
            Player = player;
            Characters = characters ?? Array.Empty<IConvaiCharacterAgent>();
            ConversationTarget = conversationTarget;
        }

        public IConvaiPlayerAgent Player { get; }
        public IReadOnlyList<IConvaiCharacterAgent> Characters { get; }
        public ActiveConversationTarget ConversationTarget { get; }
    }

    internal interface IRoomOwnershipProvider
    {
        public RoomOwnershipSnapshot CaptureOwnership();
    }

    internal sealed class RoomStartupCompositionRequest
    {
        public IRoomOwnershipProvider OwnershipProvider { get; set; }

        /// <summary>
        ///     The agent registry that provides characters for the room connection.
        ///     Characters should already be registered in the registry before composing.
        /// </summary>
        public IAgentRegistry AgentRegistry { get; set; }

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

        /// <summary>When true, the connect request sends debug=true so the backend enables RTVI metrics.</summary>
        public bool Debug { get; set; }

        public Func<IReadOnlyList<IConvaiCharacterAgent>, LipSyncTransportOptions> ResolveLipSyncTransportOptions
        {
            get;
            set;
        }

        public Func<Action, bool> PostToMainThread { get; set; }
    }

    internal sealed class RoomStartupCompositionResult
    {
        private RoomStartupCompositionResult(RoomStartupFailureReason failureReason)
        {
            FailureReason = failureReason;
        }

        public RoomStartupFailureReason FailureReason { get; }
        public bool IsSuccess => FailureReason == RoomStartupFailureReason.None;
        public IConvaiPlayerAgent Player { get; private set; }
        public List<IConvaiCharacterAgent> Characters { get; private set; }
        public IConvaiCharacterAgent ActiveCharacter { get; private set; }
        public IAgentRegistry AgentRegistry { get; private set; }
        public PlayerSessionAdapter PlayerSession { get; private set; }
        public IConvaiRoomController RoomController { get; private set; }

        public static RoomStartupCompositionResult Failure(RoomStartupFailureReason failureReason) =>
            new(failureReason);

        public static RoomStartupCompositionResult Success(
            IConvaiPlayerAgent player,
            List<IConvaiCharacterAgent> characters,
            IConvaiCharacterAgent activeCharacter,
            IAgentRegistry agentRegistry,
            PlayerSessionAdapter playerSession,
            IConvaiRoomController roomController)
        {
            return new RoomStartupCompositionResult(RoomStartupFailureReason.None)
            {
                Player = player,
                Characters = characters,
                ActiveCharacter = activeCharacter,
                AgentRegistry = agentRegistry,
                PlayerSession = playerSession,
                RoomController = roomController
            };
        }
    }

    internal sealed class RoomStartupComposer
    {
        public RoomStartupCompositionResult Compose(RoomStartupCompositionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.AgentRegistry == null) throw new ArgumentNullException(nameof(request.AgentRegistry));
            if (request.PostToMainThread == null) throw new ArgumentNullException(nameof(request.PostToMainThread));
            if (request.OwnershipProvider == null)
                return RoomStartupCompositionResult.Failure(RoomStartupFailureReason.MissingOwnershipProvider);
            if (request.CredentialProvider?.HasValidCredentials != true &&
                request.CredentialProvider is not IExplicitAuthTokenCredentialProvider)
                return RoomStartupCompositionResult.Failure(RoomStartupFailureReason.MissingRuntimeCredentials);

            RoomOwnershipSnapshot ownership = request.OwnershipProvider.CaptureOwnership();

            IConvaiPlayerAgent player = ownership?.Player;
            if (player == null)
                return RoomStartupCompositionResult.Failure(RoomStartupFailureReason.MissingPlayer);

            IAgentRegistry registry = request.AgentRegistry;

            // Characters should already be registered in the registry
            IReadOnlyList<IConvaiCharacterAgent> ownedCharacters =
                ownership?.Characters ?? Array.Empty<IConvaiCharacterAgent>();
            var characters = new List<IConvaiCharacterAgent>(ownedCharacters);
            if (characters.Count == 0)
                return RoomStartupCompositionResult.Failure(RoomStartupFailureReason.MissingCharacters);

            IConvaiCharacterAgent activeCharacter = ownership?.ConversationTarget?.Character;
            if (activeCharacter == null || !ContainsReference(characters, activeCharacter))
                return RoomStartupCompositionResult.Failure(RoomStartupFailureReason.MissingConversationTarget);

            if (request.ControllerFactory == null)
                return RoomStartupCompositionResult.Failure(RoomStartupFailureReason.MissingControllerFactory);

            var playerSession = new PlayerSessionAdapter(player, request.EventHub);
            ITransportConfiguration configProvider = new TransportConfigurationBuilder()
                .WithCredentialProvider(request.CredentialProvider)
                .WithEndUserIdentityProvider(request.EndUserIdentityProvider)
                .WithEndUserMetadataProvider(request.EndUserMetadataProvider)
                .WithConnectionType(request.ConnectionType)
                .WithVideoTrackName(request.VideoTrackName)
                .WithServerEndpoint(request.ServerEndpoint)
                .WithLipSyncTransportOptions(
                    request.ResolveLipSyncTransportOptions?.Invoke(characters) ?? LipSyncTransportOptions.Disabled)
                .WithDebug(request.Debug)
                .Build();
            var dispatcher = new MainThreadDispatcherAdapter(request.PostToMainThread);

            IConvaiRoomController roomController = request.ControllerFactory.Create(
                registry,
                playerSession,
                configProvider,
                request.SessionPersistence,
                dispatcher,
                request.Logger,
                request.EventHub,
                request.SectionNameResolver);

            if (roomController == null)
            {
                playerSession.Dispose();
                return RoomStartupCompositionResult.Failure(RoomStartupFailureReason.ControllerCreationFailed);
            }

            return RoomStartupCompositionResult.Success(
                player,
                characters,
                activeCharacter,
                registry,
                playerSession,
                roomController);
        }

        private static bool ContainsReference(IReadOnlyList<IConvaiCharacterAgent> characters,
            IConvaiCharacterAgent candidate)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                if (ReferenceEquals(characters[i], candidate))
                    return true;
            }

            return false;
        }
    }
}
