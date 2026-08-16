using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Domain.Logging;
using Convai.Runtime.Behaviors;

namespace Convai.Infrastructure.Networking.Audio
{
    /// <summary>
    ///     Thread-safe implementation of IRemoteAudioPreferenceManager.
    /// </summary>
    /// <remarks>
    ///     Extracted from ConvaiRoomManager to adhere to Single Responsibility Principle.
    ///     Manages per-character remote audio preferences with thread-safe access.
    ///     Default behavior for known characters: Remote audio is OFF (disabled) unless
    ///     explicitly enabled via SetRemoteAudioEnabled or InitializePreference.
    ///     Default behavior for unknown identities (e.g., "ConvAI-Bot" from LiveKit):
    ///     Audio is enabled if ANY character has audio enabled, or if no preferences are set.
    ///     This allows audio to work before participant-to-character mapping completes.
    /// </remarks>
    public sealed class RemoteAudioPreferenceManager
    {
        /// <summary>
        ///     When a participant identity cannot be resolved to a known character,
        ///     this controls whether to default to enabled (true) or disabled (false).
        /// </summary>
        private const bool DefaultForUnknownIdentity = true;

        private readonly IAgentRegistry _agentRegistry;
        private readonly object _lock = new();

        private readonly ILogger _logger;
        private readonly Dictionary<string, bool> _preferences = new();

        /// <summary>
        ///     Creates a new RemoteAudioPreferenceManager.
        /// </summary>
        /// <param name="agentRegistry">Optional agent registry for participant-to-character resolution.</param>
        /// <param name="logger">Optional logger for diagnostic messages.</param>
        public RemoteAudioPreferenceManager(IAgentRegistry agentRegistry = null, ILogger logger = null)
        {
            _agentRegistry = agentRegistry;
            _logger = logger.WithTag(nameof(RemoteAudioPreferenceManager));
        }

        /// <inheritdoc />
        public event Action<string, bool> RemoteAudioEnabledChanged;

        /// <inheritdoc />
        public void InitializePreference(string characterId, bool enabled)
        {
            if (string.IsNullOrEmpty(characterId)) return;

            lock (_lock) _preferences[characterId] = enabled;

            _logger?.Debug(
                $"Initialized preference for {characterId}: {(enabled ? "ON" : "OFF")}",
                LogCategory.Audio);
        }

        /// <inheritdoc />
        public bool SetRemoteAudioEnabled(string characterId, bool enabled)
        {
            if (string.IsNullOrEmpty(characterId)) return false;

            lock (_lock)
            {
                bool exists = _preferences.TryGetValue(characterId, out bool wasEnabled);

                if (exists && wasEnabled == enabled) return true;

                _preferences[characterId] = enabled;
            }

            _logger?.Debug(
                $"Remote audio {(enabled ? "ENABLED" : "DISABLED")} for character: {characterId}",
                LogCategory.Audio);

            RemoteAudioEnabledChanged?.Invoke(characterId, enabled);

            return true;
        }

        /// <inheritdoc />
        public bool IsRemoteAudioEnabled(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return false;

            lock (_lock) return _preferences.TryGetValue(characterId, out bool enabled) && enabled;
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Resolution order:
        ///     1. Match participant identity to a character via registry → use that character's preference
        ///     2. Check if identity is a character ID directly → use that preference
        ///     3. Unknown identity (e.g., "ConvAI-Bot") → allow if ANY character has audio enabled
        ///     Strategy 3 is necessary because LiveKit assigns participant identities like "ConvAI-Bot"
        ///     that don't match character IDs. The participant will be mapped to the correct character
        ///     via OnParticipantConnected after the track is subscribed.
        /// </remarks>
        public bool ShouldSubscribe(string participantIdentity)
        {
            if (string.IsNullOrEmpty(participantIdentity)) return false;

            if (_agentRegistry != null &&
                _agentRegistry.TryGetCharacter(participantIdentity, out IConvaiCharacterAgent agent))
                return IsRemoteAudioEnabled(agent.CharacterId);

            lock (_lock)
            {
                if (_preferences.TryGetValue(participantIdentity, out bool directEnabled) && directEnabled) return true;

                if (_preferences.Count == 0)
                {
                    _logger?.Debug(
                        $"No preferences configured, using default ({DefaultForUnknownIdentity}) for '{participantIdentity}'",
                        LogCategory.Audio);
                    return DefaultForUnknownIdentity;
                }

                bool anyEnabled = _preferences.Values.Any(enabled => enabled);
                if (anyEnabled)
                {
                    _logger?.Debug(
                        $"Unknown identity '{participantIdentity}' allowed (at least one character has audio enabled)",
                        LogCategory.Audio);
                }

                return anyEnabled;
            }
        }

        /// <inheritdoc />
        public void ClearAll()
        {
            lock (_lock) _preferences.Clear();

            _logger?.Debug("All preferences cleared", LogCategory.Audio);
        }
    }
}
