#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Convai.RestAPI.Transport
{
    /// <summary>
    /// Abstraction for HTTP transport layer.
    /// Allows different implementations for different platforms (HttpClient, UnityWebRequest).
    /// </summary>
    public interface IConvaiHttpTransport : IDisposable
    {
        /// <summary>
        /// Sends an HTTP request and returns the response.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <param name="cancellationToken">Cancellation token for the request.</param>
        /// <returns>The HTTP response.</returns>
        Task<ConvaiHttpResponse> SendAsync(ConvaiHttpRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a file as raw bytes.
        /// </summary>
        /// <param name="url">The URL to download from.</param>
        /// <param name="cancellationToken">Cancellation token for the request.</param>
        /// <returns>The file contents as bytes.</returns>
        Task<byte[]> DownloadBytesAsync(Uri url, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Factory for creating HTTP transports appropriate for the current platform.
    /// </summary>
    public static class ConvaiHttpTransportFactory
    {
        /// <summary>
        /// Creates the appropriate transport for the current platform.
        /// </summary>
        /// <param name="defaultTimeout">Default timeout for requests.</param>
        public static IConvaiHttpTransport Create(TimeSpan? defaultTimeout = null)
        {
            var timeout = defaultTimeout ?? TimeSpan.FromSeconds(30);

#if UNITY_WEBGL && !UNITY_EDITOR
            return new UnityWebRequestTransport(timeout);
#else
            return new HttpClientTransport(timeout);
#endif
        }
    }
}
