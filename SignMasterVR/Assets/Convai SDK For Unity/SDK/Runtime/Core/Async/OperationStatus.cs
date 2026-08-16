namespace Convai.Runtime.Core.Async
{
    /// <summary>
    ///     Execution state for a one-shot Convai operation.
    /// </summary>
    public enum OperationStatus
    {
        Created = 0,
        Running = 1,
        Succeeded = 2,
        Faulted = 3,
        Canceled = 4
    }
}
