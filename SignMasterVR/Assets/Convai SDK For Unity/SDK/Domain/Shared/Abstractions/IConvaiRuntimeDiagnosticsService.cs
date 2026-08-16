using Convai.Shared.Types;

namespace Convai.Shared.Abstractions
{
    /// <summary>
    ///     Read-only diagnostics surface for the active Convai runtime.
    /// </summary>
    public interface IConvaiRuntimeDiagnosticsService
    {
        /// <summary>
        ///     Captures the current runtime diagnostics snapshot.
        /// </summary>
        public ConvaiRuntimeDiagnosticsSnapshot CaptureSnapshot();
    }
}
