using Convai.Domain.Logging;
using Convai.Runtime.Presentation.Services;
using Convai.Shared.Abstractions;

namespace Convai.Runtime.Core.DependencyInjection
{
    /// <summary>
    ///     Concrete implementation of <see cref="IConvaiPlayerDependencies" />.
    ///     Created from the manager-owned service graph during runtime binding.
    /// </summary>
    internal sealed class ConvaiPlayerDependencies : IConvaiPlayerDependencies
    {
        /// <summary>
        ///     Creates a new dependency bundle with all optional dependencies.
        /// </summary>
        /// <param name="playerInputService">Optional: Player input service.</param>
        /// <param name="runtimeSettingsService">Optional: Runtime settings service.</param>
        /// <param name="logger">Optional: Logger for diagnostics.</param>
        public ConvaiPlayerDependencies(
            IPlayerInputService playerInputService = null,
            IConvaiRuntimeSettingsService runtimeSettingsService = null,
            ILogger logger = null)
        {
            PlayerInputService = playerInputService;
            RuntimeSettingsService = runtimeSettingsService;
            Logger = logger.WithTag("ConvaiPlayer");
        }

        /// <inheritdoc />
        public IPlayerInputService PlayerInputService { get; }

        /// <inheritdoc />
        public IConvaiRuntimeSettingsService RuntimeSettingsService { get; }

        /// <inheritdoc />
        public ILogger Logger { get; }
    }
}
