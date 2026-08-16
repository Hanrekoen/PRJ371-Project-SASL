using System;
using Convai.Domain.Abstractions;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;

namespace Convai.Runtime.Settings
{
    /// <summary>
    ///     Default persistence layer for runtime settings overrides.
    /// </summary>
    public sealed class ConvaiRuntimeSettingsStore : IConvaiRuntimeSettingsStore
    {
        private const string Prefix = "convai.runtime.settings.v1";
        private const string PlayerDisplayNameKey = Prefix + ".player_display_name";
        private const string TranscriptEnabledKey = Prefix + ".transcript_enabled";
        private const string NotificationsEnabledKey = Prefix + ".notifications_enabled";
        private const string PreferredMicDeviceIdKey = Prefix + ".preferred_microphone_device_id";

        private readonly IKeyValueStore _store;

        public ConvaiRuntimeSettingsStore(IKeyValueStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public ConvaiRuntimeSettingsOverrides LoadOverrides()
        {
            var overrides = new ConvaiRuntimeSettingsOverrides
            {
                PlayerDisplayName = ReadOptionalString(PlayerDisplayNameKey),
                TranscriptEnabled = ReadOptionalBool(TranscriptEnabledKey),
                NotificationsEnabled = ReadOptionalBool(NotificationsEnabledKey),
                PreferredMicrophoneDeviceId = ReadOptionalString(PreferredMicDeviceIdKey)
            };

            return overrides;
        }

        public void SaveOverrides(ConvaiRuntimeSettingsOverrides overrides)
        {
            if (overrides == null)
            {
                ClearOverrides();
                return;
            }

            WriteOptionalString(PlayerDisplayNameKey, overrides.PlayerDisplayName);
            WriteOptionalBool(TranscriptEnabledKey, overrides.TranscriptEnabled);
            WriteOptionalBool(NotificationsEnabledKey, overrides.NotificationsEnabled);
            WriteOptionalString(PreferredMicDeviceIdKey, overrides.PreferredMicrophoneDeviceId);
            _store.Save();
        }

        public void ClearOverrides()
        {
            _store.DeleteKey(PlayerDisplayNameKey);
            _store.DeleteKey(TranscriptEnabledKey);
            _store.DeleteKey(NotificationsEnabledKey);
            _store.DeleteKey(PreferredMicDeviceIdKey);
            _store.Save();
        }

        private string ReadOptionalString(string key) => _store.HasKey(key) ? _store.GetString(key) : null;

        private bool? ReadOptionalBool(string key)
        {
            if (!_store.HasKey(key)) return null;

            string raw = _store.GetString(key);
            if (string.IsNullOrWhiteSpace(raw)) return null;

            if (string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
                return false;

            return null;
        }

        private void WriteOptionalString(string key, string value)
        {
            if (value == null)
            {
                _store.DeleteKey(key);
                return;
            }

            _store.SetString(key, value);
        }

        private void WriteOptionalBool(string key, bool? value)
        {
            if (!value.HasValue)
            {
                _store.DeleteKey(key);
                return;
            }

            _store.SetString(key, value.Value ? "1" : "0");
        }
    }
}
