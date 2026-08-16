using System;

namespace Convai.Runtime.Components
{
    public partial class ConvaiCharacter
    {
        private bool UsesCharacterConfigAsset =>
            ResolveConfigurationSourceMode() == ConvaiConfigSourceMode.Asset && _characterConfigAsset != null;

        private void EnsureConfigurationSourceInitialized()
        {
            if (_configurationSourceInitialized) return;

            _configurationSource = _characterConfigAsset != null
                ? ConvaiConfigSourceMode.Asset
                : ConvaiConfigSourceMode.Inline;
            _configurationSourceInitialized = true;
        }

        private ConvaiConfigSourceMode ResolveConfigurationSourceMode() =>
            !_configurationSourceInitialized
                ? _characterConfigAsset != null ? ConvaiConfigSourceMode.Asset : ConvaiConfigSourceMode.Inline
                : _configurationSource;

        /// <summary>
        ///     Programmatically configures the character ID and name.
        /// </summary>
        /// <param name="characterId">The character ID from the Convai dashboard.</param>
        /// <param name="characterName">Optional display name (defaults to characterId).</param>
        /// <exception cref="InvalidOperationException">Thrown if called during an active conversation.</exception>
        public void Configure(string characterId, string characterName = null)
        {
            if (IsInConversation)
            {
                throw new InvalidOperationException(
                    "Cannot configure character while in conversation. Call StopConversationAsync() first.");
            }

            _characterId = characterId;
            _characterName = characterName ?? characterId;
        }
    }
}
