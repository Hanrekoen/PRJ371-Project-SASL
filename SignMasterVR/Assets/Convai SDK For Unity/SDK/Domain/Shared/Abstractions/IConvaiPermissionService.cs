using System;

namespace Convai.Shared.Abstractions
{
    /// <summary>
    ///     Platform-agnostic permission service for audio and related capabilities.
    /// </summary>
    /// <remarks>
    ///     This interface is defined in Convai.Shared to enable cross-assembly type safety
    ///     without creating cyclic dependencies between Convai.Runtime and Convai.Modules.*.
    /// </remarks>
    public interface IConvaiPermissionService
    {
        /// <summary>
        ///     Checks if the application has microphone permission.
        /// </summary>
        /// <returns>True if microphone permission is granted; otherwise false.</returns>
        public bool HasMicrophonePermission();

        /// <summary>
        ///     Requests microphone permission from the user.
        /// </summary>
        /// <param name="callback">Callback invoked with the result of the permission request.</param>
        public void RequestMicrophonePermission(Action<bool> callback);
    }
}
