using System;

namespace Convai.Runtime.Core.Async
{
    public sealed class ConvaiOperationException : Exception
    {
        public ConvaiOperationException(string code, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Code = string.IsNullOrWhiteSpace(code) ? "operation.failed" : code;
        }

        public string Code { get; }
    }

    /// <summary>
    ///     Structured error information for operation and stream contracts.
    /// </summary>
    public readonly struct ConvaiError : IEquatable<ConvaiError>
    {
        public ConvaiError(string code, string message, Exception exception = null)
        {
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        public string Code { get; }
        public string Message { get; }
        public Exception Exception { get; }

        public bool IsEmpty => string.IsNullOrEmpty(Code)
                               && string.IsNullOrEmpty(Message)
                               && Exception == null;

        public static ConvaiError FromException(Exception exception, string code = "exception")
        {
            Exception resolved = exception?.GetBaseException() ?? exception;
            if (resolved is ConvaiOperationException operationException)
                return new ConvaiError(operationException.Code, operationException.Message, operationException);

            return new ConvaiError(code, resolved?.Message ?? string.Empty, resolved);
        }

        public bool Equals(ConvaiError other) =>
            string.Equals(Code, other.Code, StringComparison.Ordinal)
            && string.Equals(Message, other.Message, StringComparison.Ordinal)
            && Equals(Exception, other.Exception);

        public override bool Equals(object obj) => obj is ConvaiError other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Code, Message, Exception);

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Code) ? Message : $"{Code}: {Message}";
    }
}
