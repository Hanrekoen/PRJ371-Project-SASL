using Convai.Domain.Logging;
using Convai.Runtime.Presentation.Services;
using Convai.Shared.Abstractions;

namespace Convai.Runtime.Core.DependencyInjection
{
    /// <summary>
    ///     Typed dependency bundle for <see cref="Convai.Runtime.Components.ConvaiPlayer" /> components.
    ///     Provides compile-time safety for dependency injection.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Components implement <c>IInjectable&lt;IConvaiPlayerDependencies&gt;</c>
    ///         for typed dependency injection.
    ///     </para>
    ///     <para>
    ///         All dependencies in this bundle are optional (may be null).
    ///         The player component can function with minimal configuration.
    ///     </para>
    /// </remarks>
    public interface IConvaiPlayerDependencies
    {
        /// <summary>
        ///     Player input service for text input handling and player registration.
        /// </summary>
        /// <remarks>
        ///     Optional dependency. May be null if text input is not required.
        ///     When present, the player is registered with <c>SetPlayer(this)</c>.
        /// </remarks>
        public IPlayerInputService PlayerInputService { get; }

        /// <summary>
        ///     Runtime settings service for player display name and preferences.
        /// </summary>
        /// <remarks>
        ///     Optional dependency. May be null if runtime settings are not available.
        ///     When present, player display name is updated from settings.
        /// </remarks>
        public IConvaiRuntimeSettingsService RuntimeSettingsService { get; }

        /// <summary>
        ///     Logger for diagnostic output.
        /// </summary>
        /// <remarks>Optional dependency. May be null.</remarks>
        public ILogger Logger { get; }
    }
}
