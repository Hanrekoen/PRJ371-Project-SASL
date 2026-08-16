using System;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Reports embodiment setup problems so they reach the user even before any log sink is
    ///     installed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="ConvaiLogger" /> dispatches to registered sinks, so with none installed a
    ///         message goes nowhere. That is the right default for chatty diagnostics and the wrong
    ///         one for "your component is not going to work": a user who has not configured logging is
    ///         exactly the user who needs to be told.
    ///     </para>
    ///     <para>
    ///         So these route through <see cref="ConvaiLogger" /> normally — keeping the category and
    ///         the filtering — and fall back to Unity's console when nothing would otherwise carry
    ///         them. The registry and the composition root both had their own copy of this fallback;
    ///         this is the one implementation, and the only file the logging guard test allow-lists.
    ///     </para>
    /// </remarks>
    internal static class EmbodimentDiagnostics
    {
        /// <summary>A setup mistake that stops something working. Always reaches the console.</summary>
        internal static void SetupError(string message)
        {
            ConvaiLogger.Error(message, LogCategory.Character);
            if (ConvaiLogger.SinkCount == 0) Debug.LogError(message);
        }

        /// <summary>A configuration problem that degrades behavior. Always reaches the console.</summary>
        internal static void SetupWarning(string message)
        {
            ConvaiLogger.Warning(message, LogCategory.Character);
            if (ConvaiLogger.SinkCount == 0) Debug.LogWarning(message);
        }

        /// <summary>
        ///     An exception raised by a subscriber or module, attributed and never rethrown.
        /// </summary>
        internal static void SubscriberException(Exception exception, string message)
        {
            ConvaiLogger.Exception(exception, LogCategory.Character);
            if (ConvaiLogger.SinkCount == 0)
                Debug.LogError($"{message}\n{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}");
        }
    }
}
