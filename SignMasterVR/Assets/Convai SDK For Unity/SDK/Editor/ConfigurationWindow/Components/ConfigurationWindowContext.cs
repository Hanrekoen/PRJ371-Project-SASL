using System;
using Convai.Runtime;

namespace Convai.Editor.ConfigurationWindow.Components
{
    /// <summary>
    ///     Shared context for a single configuration window instance.
    ///     Provides instance-scoped state and change notifications.
    /// </summary>
    public sealed class ConfigurationWindowContext
    {
        public ConfigurationWindowContext()
        {
            RefreshApiKeyAvailability(false);
        }

        /// <summary>
        ///     Gets a value indicating whether an API key is currently configured.
        /// </summary>
        public bool IsApiKeyAvailable { get; private set; }

        /// <summary>
        ///     Gets the number of active API-key subscribers.
        /// </summary>
        public int ApiKeyAvailabilitySubscriberCount => _apiKeyAvailabilityChanged?.GetInvocationList().Length ?? 0;

        private event Action<bool> _apiKeyAvailabilityChanged;

        /// <summary>
        ///     Raised when API key availability or configured credentials change.
        ///     Parameter is true when an API key is present after the refresh.
        /// </summary>
        public event Action<bool> ApiKeyAvailabilityChanged
        {
            add => _apiKeyAvailabilityChanged += value;
            remove => _apiKeyAvailabilityChanged -= value;
        }

        /// <summary>
        ///     Re-evaluates API key availability from <see cref="ConvaiSettings" />.
        /// </summary>
        /// <param name="raiseEvent">Whether to raise <see cref="ApiKeyAvailabilityChanged" /> when changed.</param>
        /// <returns>True if API key availability is true after refresh.</returns>
        public bool RefreshApiKeyAvailability(bool raiseEvent = true)
        {
            bool available = ConvaiSettings.Instance != null && ConvaiSettings.Instance.HasApiKey;
            bool changed = available != IsApiKeyAvailable;
            IsApiKeyAvailable = available;

            if (raiseEvent && changed) _apiKeyAvailabilityChanged?.Invoke(available);

            return available;
        }

        /// <summary>
        ///     Signals that API-key-related state may have changed.
        /// </summary>
        public void NotifyApiKeyUpdated()
        {
            RefreshApiKeyAvailability(false);
            _apiKeyAvailabilityChanged?.Invoke(IsApiKeyAvailable);
        }

        internal void ClearSubscribers() => _apiKeyAvailabilityChanged = null;
    }
}
