using System;

namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Transport error information.
    /// </summary>
    public struct TransportError
    {
        /// <summary>Human-readable error message.</summary>
        public string Message { get; set; }

        /// <summary>Error code for programmatic handling.</summary>
        public TransportErrorCode Code { get; set; }

        /// <summary>Original exception if available.</summary>
        public Exception Exception { get; set; }

        /// <summary>
        ///     Creates a new TransportError.
        /// </summary>
        public TransportError(string message, TransportErrorCode code, Exception exception = null)
        {
            Message = message;
            Code = code;
            Exception = exception;
        }

        /// <summary>
        ///     Creates an error from an exception.
        /// </summary>
        public static TransportError
            FromException(Exception ex, TransportErrorCode code = TransportErrorCode.Unknown) =>
            new(ex.Message, code, ex);
    }
}
