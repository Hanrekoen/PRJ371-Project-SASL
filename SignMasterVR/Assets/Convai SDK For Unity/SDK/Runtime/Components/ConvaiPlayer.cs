using System;
using Convai.Domain.Logging;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.DependencyInjection;
using Convai.Runtime.Logging;
using Convai.Runtime.Presentation.Services;
using Convai.Shared.Abstractions;
using UnityEngine;
using UnityEngine.Serialization;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Main player component that implements IConvaiPlayerAgent.
    ///     Provides player identity for the Convai conversation system.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This component is the player-side equivalent of <see cref="ConvaiCharacter" />.
    ///         It provides player identity (name, playerId, color) and text messaging capability.
    ///         Microphone input is managed separately by <see cref="Convai.Runtime.Adapters.Networking.ConvaiRoomManager" />.
    ///         Player behavior can be extended using <see cref="ConvaiPlayerBehaviorBase" />.
    ///     </para>
    ///     <para>
    ///         <b>Important - PlayerId vs Server Speaker ID:</b>
    ///     </para>
    ///     <para>
    ///         The <c>PlayerId</c> property on this component is a <b>local display identifier</b> used for
    ///         transcript UI attribution. It is NOT the same as the server-generated <c>speaker_id</c> used
    ///         for Long-Term Memory (LTM) and interaction tracking.
    ///     </para>
    ///     <para>
    ///         When the backend includes it for diagnostics, the server-generated speaker ID may be visible via
    ///         <see cref="Convai.Infrastructure.Networking.IConvaiRoomController.ResolvedSpeakerId" />
    ///         after connection is established.
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Convai Player")]
    public class ConvaiPlayer : MonoBehaviour, IConvaiPlayerAgent,
        IInjectable<IConvaiPlayerDependencies>
    {
        #region Events

        /// <summary>Raised when a text message is sent.</summary>
        public event Action<string> OnTextMessageSent;

        #endregion

        #region Dependency Injection

        /// <inheritdoc cref="IInjectable{TDependencies}.InjectDependencies" />
        public void InjectDependencies(IConvaiPlayerDependencies dependencies)
        {
            _deps = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

            // Register with player input service if available
            if (PlayerInputService != null)
                PlayerInputService.SetPlayer(this);

            Logger?.Debug($"[{_playerName}] Dependencies injected (typed bundle)");
        }

        #endregion

        #region Dependencies

        /// <summary>
        ///     Typed dependency bundle (new pattern). When set, provides all dependencies.
        /// </summary>
        private IConvaiPlayerDependencies _deps;

        // Accessor properties for dependencies
        private IPlayerInputService PlayerInputService => _deps?.PlayerInputService;
        private ILogger Logger => _deps?.Logger;

        #endregion

        #region Serialized Fields

        [Header("Player Configuration")]
        [SerializeField]
        [Tooltip("Display name for the player, used in transcripts and debug logs.")]
        private string _playerName = "Player";

        [SerializeField]
        [FormerlySerializedAs("_speakerId")]
        [FormerlySerializedAs("_actorId")]
        [Tooltip("Optional local player identifier for transcript UI attribution. " +
                 "If empty, PlayerName is used. This is NOT the server-generated speaker ID used for Long-Term Memory.")]
        private string _playerId = "";

        [SerializeField] private Color _nameTagColor = Color.green;

        private string _runtimeDisplayName = string.Empty;
        private bool _hasRuntimeDisplayName;

        #endregion

        #region IConvaiPlayerAgent Implementation

        /// <summary>Display name for the player.</summary>
        public string PlayerName => _hasRuntimeDisplayName ? _runtimeDisplayName : _playerName;

        /// <summary>
        ///     Local player identifier for transcript UI attribution.
        /// </summary>
        /// <remarks>
        ///     This is a local display identifier, NOT the server-generated speaker_id used for LTM.
        ///     If the backend exposes a resolved speaker ID for diagnostics, it can be read from
        ///     <see cref="Convai.Infrastructure.Networking.ConvaiRoomController.ResolvedSpeakerId" />.
        /// </remarks>
        public string PlayerId => string.IsNullOrWhiteSpace(_playerId) ? PlayerName : _playerId.Trim();

        /// <summary>Name tag color for transcript display.</summary>
        public Color NameTagColor => _nameTagColor;

        /// <summary>
        ///     Sends a text message to the active Character.
        /// </summary>
        /// <param name="message">The text message to send.</param>
        public void SendTextMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                ConvaiLogger.Warning("SendTextMessage called with null or empty message; ignoring.",
                    LogCategory.SDK);
                return;
            }

            if (OnTextMessageSent == null)
            {
                ConvaiLogger.Warning(
                    "SendTextMessage has no subscribers (is ConvaiRoomManager active?); message dropped.",
                    LogCategory.SDK);
                return;
            }

            OnTextMessageSent.Invoke(message);
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Configures the player identity.
        /// </summary>
        /// <param name="playerName">Display name for the player.</param>
        /// <param name="playerId">Optional local player ID (defaults to playerName).</param>
        public void Configure(string playerName, string playerId = null)
        {
            _playerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();
            _playerId = string.IsNullOrWhiteSpace(playerId) ? _playerName : playerId.Trim();
            _runtimeDisplayName = string.Empty;
            _hasRuntimeDisplayName = false;
        }

        /// <summary>
        ///     Updates the effective runtime display name used by transcripts and player identity.
        /// </summary>
        /// <param name="displayName">Display name override. Null/empty clears the runtime override.</param>
        public void SetRuntimeDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                _runtimeDisplayName = string.Empty;
                _hasRuntimeDisplayName = false;
                return;
            }

            _runtimeDisplayName = displayName.Trim();
            _hasRuntimeDisplayName = true;
        }

        #endregion

        #region Unity Lifecycle

        private void Awake() { }

        private void OnDestroy() { }

        #endregion
    }
}
