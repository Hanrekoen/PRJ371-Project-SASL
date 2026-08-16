using System;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="IMicrophoneSource" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On WebGL, microphone access is handled through the browser's getUserMedia API.
    ///         This is a placeholder/marker class that represents the microphone source.
    ///         The actual capture is managed by the LiveKit WebGL SDK internally when
    ///         SetMicrophoneEnabled(true) is called on the LocalParticipant.
    ///     </para>
    ///     <para>
    ///         Key differences from NativeMicrophoneSource:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Requires user gesture to access microphone</description>
    ///             </item>
    ///             <item>
    ///                 <description>Browser prompts for permission</description>
    ///             </item>
    ///             <item>
    ///                 <description>Device selection is limited to browser capabilities</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    internal sealed class WebGLMicrophoneSource : IMicrophoneSource
    {
        #region Constructor

        /// <summary>
        ///     Creates a new WebGL microphone source.
        /// </summary>
        /// <param name="deviceName">The device name (for reference only on WebGL).</param>
        /// <param name="deviceIndex">The device index (for reference only on WebGL).</param>
        public WebGLMicrophoneSource(string deviceName = null, int deviceIndex = 0)
        {
            Name = deviceName ?? "default";
            DeviceIndex = deviceIndex;
        }

        #endregion

        #region IDisposable

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopCapture();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private Fields

        private bool _isCapturing;
        private bool _disposed;

        #endregion

        #region IAudioSource Properties

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public bool IsCapturing => _isCapturing && !_disposed;

        #endregion

        #region IMicrophoneSource Properties

        /// <inheritdoc />
        public string DeviceName => Name;

        /// <inheritdoc />
        public int DeviceIndex { get; }

        /// <inheritdoc />
        public event Action<IMicrophoneSource, MicrophoneSessionInvalidationReason> SessionInvalidated
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public bool IsMuted { get; set; }

        /// <inheritdoc />
        public bool EnableAcousticEchoCancellation { get; set; }

        #endregion

        #region IAudioSource Methods

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, this marks the source as "capturing" but actual capture is started
        ///     through the LiveKit SDK's SetMicrophoneEnabled(true) method.
        /// </remarks>
        public void StartCapture()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebGLMicrophoneSource));

            if (_isCapturing)
            {
                ConvaiLogger.Warning("Capture is already started.", LogCategory.Audio);
                return;
            }

            _isCapturing = true;
            ConvaiLogger.Info("Microphone capture marked as started. " +
                              "Actual capture is managed by the LiveKit WebGL SDK.",
                LogCategory.Audio);
        }

        /// <inheritdoc />
        public void StopCapture()
        {
            if (!_isCapturing) return;

            _isCapturing = false;
            ConvaiLogger.Info("Microphone capture marked as stopped.", LogCategory.Audio);
        }

        #endregion
    }
}
