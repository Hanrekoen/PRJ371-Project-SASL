namespace Convai.Runtime.Core.Async
{
    /// <summary>
    ///     Execution state for a streaming Convai operation.
    /// </summary>
    public enum StreamStatus
    {
        Created = 0,
        Streaming = 1,
        Completed = 2,
        Faulted = 3,
        Canceled = 4
    }
}
