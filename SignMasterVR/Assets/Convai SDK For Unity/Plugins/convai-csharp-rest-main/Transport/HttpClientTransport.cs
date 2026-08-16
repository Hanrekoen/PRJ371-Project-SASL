#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Convai.RestAPI.Transport
{
    /// <summary>
    /// HTTP transport implementation using System.Net.Http.HttpClient.
    /// Used for Editor, Standalone, and Mobile builds (not WebGL runtime).
    /// </summary>
    public sealed class HttpClientTransport : IConvaiHttpTransport
    {
        private readonly HttpClient _httpClient;
        private readonly TimeSpan _defaultTimeout;
        private bool _disposed;

        /// <summary>
        /// Creates a new HttpClient transport with the specified timeout.
        /// </summary>
        public HttpClientTransport(TimeSpan defaultTimeout)
        {
            _defaultTimeout = defaultTimeout;
            _httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan // We handle timeouts per-request
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <inheritdoc/>
        public async Task<ConvaiHttpResponse> SendAsync(ConvaiHttpRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var timeout = request.Timeout ?? _defaultTimeout;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            HttpResponseMessage? response = null;
            try
            {
                using var httpRequest = CreateHttpRequestMessage(request);
                response = await _httpClient.SendAsync(httpRequest, cts.Token).ConfigureAwait(false);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return ConvaiHttpResponse.Success(
                        response.StatusCode,
                        body,
                        request.Url,
                        response.ReasonPhrase);
                }

                return ConvaiHttpResponse.Failure(
                    response.StatusCode,
                    body,
                    request.Url,
                    response.ReasonPhrase);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return ConvaiHttpResponse.TransportFailure(
                    request.Url,
                    $"Request timed out after {timeout.TotalSeconds:F1} seconds");
            }
            catch (OperationCanceledException)
            {
                return ConvaiHttpResponse.TransportFailure(
                    request.Url,
                    "Request was cancelled");
            }
            catch (HttpRequestException ex)
            {
                return ConvaiHttpResponse.TransportFailure(
                    request.Url,
                    $"HTTP request failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                return ConvaiHttpResponse.TransportFailure(
                    request.Url,
                    $"Unexpected error: {ex.GetType().Name} - {ex.Message}");
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <inheritdoc/>
        public async Task<byte[]> DownloadBytesAsync(Uri url, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_defaultTimeout);

            return await _httpClient.GetByteArrayAsync(url).ConfigureAwait(false);
        }

        private static HttpRequestMessage CreateHttpRequestMessage(ConvaiHttpRequest request)
        {
            var method = request.Method switch
            {
                Transport.HttpMethod.Get => System.Net.Http.HttpMethod.Get,
                Transport.HttpMethod.Post => System.Net.Http.HttpMethod.Post,
                Transport.HttpMethod.Put => System.Net.Http.HttpMethod.Put,
                Transport.HttpMethod.Delete => System.Net.Http.HttpMethod.Delete,
                _ => throw new ArgumentOutOfRangeException(nameof(request.Method))
            };

            var httpRequest = new HttpRequestMessage(method, request.Url);

            // Add headers to the request message (not the client)
            foreach (var header in request.Headers)
            {
                httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Add body for methods that support it
            if (!string.IsNullOrEmpty(request.Body) && method != System.Net.Http.HttpMethod.Get)
            {
                httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
            }

            return httpRequest;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HttpClientTransport));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
        }
    }
}
