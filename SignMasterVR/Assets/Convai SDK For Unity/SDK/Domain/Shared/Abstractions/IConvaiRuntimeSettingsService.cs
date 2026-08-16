using System;
using Convai.Shared.Types;

namespace Convai.Shared.Abstractions
{
    /// <summary>
    ///     Single source of truth for runtime settings state.
    /// </summary>
    public interface IConvaiRuntimeSettingsService
    {
        /// <summary>
        ///     Gets current effective runtime settings.
        /// </summary>
        public ConvaiRuntimeSettingsSnapshot Current { get; }

        /// <summary>
        ///     Raised when runtime settings have changed.
        /// </summary>
        public event Action<ConvaiRuntimeSettingsChanged> Changed;

        /// <summary>
        ///     Applies a runtime settings patch atomically.
        /// </summary>
        public ConvaiRuntimeSettingsApplyResult Apply(ConvaiRuntimeSettingsPatch patch);

        /// <summary>
        ///     Resets runtime settings to project defaults and clears overrides.
        /// </summary>
        public ConvaiRuntimeSettingsApplyResult ResetToDefaults();
    }
}
