#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Convai.RestAPI.Transport
{
    /// <summary>
    /// HTTP transport implementation using UnityWebRequest.
    /// Required for WebGL builds where System.Net.Http is not available.
    /// </summary>
    public sealed class UnityWebRequestTransport : IConvaiHttpTransport
    {
        private readonly TimeSpan _defaultTimeout;
        private bool _disposed;

        /// <summary>
        /// Creates a new UnityWebRequest transport with the specified timeout.
        /// </summary>
        public UnityWebRequestTransport(TimeSpan defaultTimeout)
        {
            _defaultTimeout = defaultTimeout;
        }

        /// <inheritdoc/>
        public async Task<ConvaiHttpResponse> SendAsync(ConvaiHttpRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var timeout = request.Timeout ?? _defaultTimeout;

            try
            {
                using var webRequest = CreateWebRequest(request);
                webRequest.timeout = (int)Math.Ceiling(timeout.TotalSeconds);

                var operation = webRequest.SendWebRequest();

                // Wait for completion with cancellation support
                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        webRequest.Abort();
                        return ConvaiHttpResponse.TransportFailure(
                            request.Url,
                            "Request was cancelled");
                    }
                    await Task.Yield();
                }

                return ProcessResponse(webRequest, request.Url);
            }
            catch (Exception ex)
            {
                return ConvaiHttpResponse.TransportFailure(
                    request.Url,
                    $"Unexpected error: {ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<byte[]> DownloadBytesAsync(Uri url, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            using var webRequest = UnityWebRequest.Get(url);
            webRequest.timeout = (int)Math.Ceiling(_defaultTimeout.TotalSeconds);

            var operation = webRequest.SendWebRequest();

            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    webRequest.Abort();
                    throw new OperationCanceledException(cancellationToken);
                }
                await Task.Yield();
            }

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                throw new Exception($"Download failed: {webRequest.error}");
            }

            return webRequest.downloadHandler.data;
        }

        private static UnityWebRequest CreateWebRequest(ConvaiHttpRequest request)
        {
            UnityWebRequest webRequest;

            switch (request.Method)
            {
                case HttpMethod.Get:
                    webRequest = UnityWebRequest.Get(request.Url);
                    break;

                case HttpMethod.Post:
                    webRequest = new UnityWebRequest(request.Url, "POST");
                    if (!string.IsNullOrEmpty(request.Body))
                    {
                        byte[] bodyRaw = Encoding.UTF8.GetBytes(request.Body);
                        webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    }
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    break;

                case HttpMethod.Put:
                    webRequest = new UnityWebRequest(request.Url, "PUT");
                    if (!string.IsNullOrEmpty(request.Body))
                    {
                        byte[] bodyRaw = Encoding.UTF8.GetBytes(request.Body);
                        webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    }
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    break;

                case HttpMethod.Delete:
                    webRequest = UnityWebRequest.Delete(request.Url);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(request.Method));
            }

            // Set content type for requests with body
            if (!string.IsNullOrEmpty(request.Body))
            {
                webRequest.SetRequestHeader("Content-Type", "application/json");
            }

            // Set Accept header
            webRequest.SetRequestHeader("Accept", "application/json");

            // Add custom headers
            foreach (var header in request.Headers)
            {
                webRequest.SetRequestHeader(header.Key, header.Value);
            }

            return webRequest;
        }

        private static ConvaiHttpResponse ProcessResponse(UnityWebRequest webRequest, Uri url)
        {
            var statusCode = (HttpStatusCode)webRequest.responseCode;
            string body = webRequest.downloadHandler?.text ?? string.Empty;

            // Check for network/transport errors
            if (webRequest.result == UnityWebRequest.Result.ConnectionError)
            {
                return ConvaiHttpResponse.TransportFailure(url, $"Connection error: {webRequest.error}");
            }

            if (webRequest.result == UnityWebRequest.Result.DataProcessingError)
            {
                return ConvaiHttpResponse.TransportFailure(url, $"Data processing error: {webRequest.error}");
            }

            // HTTP response received (may be success or failure status)
            bool isSuccess = webRequest.result == UnityWebRequest.Result.Success;

            if (isSuccess)
            {
                return ConvaiHttpResponse.Success(statusCode, body, url);
            }

            return ConvaiHttpResponse.Failure(statusCode, body, url, webRequest.error);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UnityWebRequestTransport));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // UnityWebRequest instances are disposed individually via 'using'
        }
    }
}
