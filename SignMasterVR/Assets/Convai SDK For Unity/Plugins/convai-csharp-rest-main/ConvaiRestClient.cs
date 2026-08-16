#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.RestAPI.Services;
using Convai.RestAPI.Transport;

namespace Convai.RestAPI
{
    /// <summary>
    /// The main client for interacting with the Convai REST API.
    /// Thread-safe and reusable. Create one instance and reuse it for the lifetime of your application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This client provides access to all Convai REST API features through organized service properties:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="Characters"/> - Character management operations</item>
    /// <item><see cref="Users"/> - User account and API key operations</item>
    /// <item><see cref="EndUsers"/> - End-user identity management operations</item>
    /// <item><see cref="Memory"/> - Character-scoped long-term memory operations</item>
    /// <item><see cref="Narrative"/> - Narrative design operations</item>
    /// <item><see cref="Animations"/> - Server animation operations</item>
    /// <item><see cref="Rooms"/> - Room connection operations</item>
    /// </list>
    /// <para>
    /// All methods are async and support cancellation. Errors are thrown as <see cref="ConvaiRestException"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create the client
    /// var options = new ConvaiRestClientOptions("your-api-key");
    /// using var client = new ConvaiRestClient(options);
    /// 
    /// // Get character details
    /// var character = await client.Characters.GetDetailsAsync("character-id");
    /// Console.WriteLine($"Character: {character.CharacterName}");
    /// 
    /// // Fetch an end user
    /// var endUser = await client.EndUsers.GetAsync("player-42");
    /// </code>
    /// </example>
    public sealed class ConvaiRestClient : IDisposable
    {
        private readonly IConvaiHttpTransport _transport;
        private readonly bool _ownsTransport;
        private bool _disposed;

        /// <summary>
        /// Character management operations.
        /// </summary>
        public CharacterService Characters { get; }

        /// <summary>
        /// User account and API key operations.
        /// </summary>
        public UserService Users { get; }

        /// <summary>
        /// End-user identity management operations.
        /// </summary>
        public EndUsersService EndUsers { get; }

        /// <summary>
        /// Character-scoped long-term memory operations.
        /// </summary>
        public MemoryService Memory { get; }

        /// <summary>
        /// Narrative design operations.
        /// </summary>
        public NarrativeService Narrative { get; }

        /// <summary>
        /// Server animation operations.
        /// </summary>
        public AnimationService Animations { get; }

        /// <summary>
        /// Room connection operations.
        /// </summary>
        public RoomService Rooms { get; }

        /// <summary>
        /// The options used to configure this client.
        /// </summary>
        public ConvaiRestClientOptions Options { get; }

        /// <summary>
        /// Creates a new Convai REST client with the specified options.
        /// </summary>
        /// <param name="options">The client configuration options.</param>
        public ConvaiRestClient(ConvaiRestClientOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));

            if (options.CustomTransport != null)
            {
                _transport = options.CustomTransport;
                _ownsTransport = false;
            }
            else
            {
                _transport = ConvaiHttpTransportFactory.Create(options.DefaultTimeout);
                _ownsTransport = true;
            }

            // Initialize all services
            Characters = new CharacterService(options, _transport);
            Users = new UserService(options, _transport);
            EndUsers = new EndUsersService(options, _transport);
            Memory = new MemoryService(options, _transport);
            Narrative = new NarrativeService(options, _transport);
            Animations = new AnimationService(options, _transport);
            Rooms = new RoomService(options, _transport);
        }

        /// <summary>
        /// Creates a new Convai REST client with just an API key (uses default options).
        /// </summary>
        /// <param name="apiKey">The Convai API key.</param>
        public ConvaiRestClient(string apiKey) : this(new ConvaiRestClientOptions(apiKey))
        {
        }

        /// <summary>
        /// Downloads a file from a URL as raw bytes.
        /// </summary>
        /// <param name="url">The URL to download from.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The file contents as bytes.</returns>
        public async Task<byte[]> DownloadFileAsync(string url, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await _transport.DownloadBytesAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads a file from a URL as raw bytes.
        /// </summary>
        /// <param name="url">The URL to download from.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The file contents as bytes.</returns>
        public async Task<byte[]> DownloadFileAsync(Uri url, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await _transport.DownloadBytesAsync(url, cancellationToken).ConfigureAwait(false);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConvaiRestClient));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_ownsTransport)
            {
                _transport.Dispose();
            }
        }
    }
}
