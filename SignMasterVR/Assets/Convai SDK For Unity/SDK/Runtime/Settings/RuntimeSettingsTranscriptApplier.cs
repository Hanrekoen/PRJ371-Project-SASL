using System;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;

namespace Convai.Runtime.Settings
{
    /// <summary>
    ///     Applies transcript-related runtime settings to transcript presentation services.
    /// </summary>
    internal sealed class RuntimeSettingsTranscriptApplier : IDisposable
    {
        private readonly Action<bool> _setPresentationEnabled;
        private readonly IConvaiRuntimeSettingsService _settings;

        public RuntimeSettingsTranscriptApplier(IConvaiRuntimeSettingsService settings,
            Action<bool> setPresentationEnabled)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _setPresentationEnabled = setPresentationEnabled ??
                                      throw new ArgumentNullException(nameof(setPresentationEnabled));

            _settings.Changed += OnSettingsChanged;
            Apply(_settings.Current);
        }

        public void Dispose() => _settings.Changed -= OnSettingsChanged;

        private void OnSettingsChanged(ConvaiRuntimeSettingsChanged changed)
        {
            if ((changed.Mask & ConvaiRuntimeSettingsChangeMask.TranscriptEnabled) == 0)
                return;

            Apply(changed.Current);
        }

        private void Apply(ConvaiRuntimeSettingsSnapshot snapshot)
        {
            _setPresentationEnabled(snapshot.TranscriptEnabled);
        }
    }
}
