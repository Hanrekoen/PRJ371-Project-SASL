using Convai.Shared.Types;

namespace Convai.Shared.Abstractions
{
    /// <summary>
    ///     Persistence abstraction for runtime/user settings overrides.
    /// </summary>
    public interface IConvaiRuntimeSettingsStore
    {
        /// <summary>
        ///     Loads user overrides from persistent storage.
        /// </summary>
        public ConvaiRuntimeSettingsOverrides LoadOverrides();

        /// <summary>
        ///     Persists user overrides to storage.
        /// </summary>
        public void SaveOverrides(ConvaiRuntimeSettingsOverrides overrides);

        /// <summary>
        ///     Clears all runtime settings overrides.
        /// </summary>
        public void ClearOverrides();
    }
}
