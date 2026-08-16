using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiveKit;
using UnityEngine;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="ILocalParticipant" /> wrapping the LiveKit WebGL LocalParticipant.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Key differences from NativeLocalParticipant:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Uses coroutines instead of async/await for publishing operations</description>
    ///             </item>
    ///             <item>
    ///                 <description>Microphone operations require user gesture context</description>
    ///             </item>
    ///             <item>
    ///                 <description>Video track publishing has browser-specific constraints</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    internal class WebGLLocalParticipant : ILocalParticipant
    {
        #region Constructor

        /// <summary>
        ///     Creates a new WebGL local participant wrapper.
        /// </summary>
        /// <param name="participant">The LiveKit local participant to wrap.</param>
        /// <param name="coroutineRunner">MonoBehaviour for running coroutines.</param>
        public WebGLLocalParticipant(LocalParticipant participant, MonoBehaviour coroutineRunner)
        {
            UnderlyingParticipant = participant ?? throw new ArgumentNullException(nameof(participant));
            _coroutineRunner = coroutineRunner ?? throw new ArgumentNullException(nameof(coroutineRunner));
        }

        #endregion

        #region ILocalParticipant Properties

        /// <inheritdoc />
        public IReadOnlyList<ILocalTrack> LocalTracks => _localTracks;

        #endregion

        #region Private Fields

        private readonly List<ILocalTrack> _localTracks = new();
        private readonly MonoBehaviour _coroutineRunner;

        #endregion

        #region IParticipant Properties

        /// <inheritdoc />
        public string Sid => UnderlyingParticipant.Sid;

        /// <inheritdoc />
        public string Identity => UnderlyingParticipant.Identity;

        /// <inheritdoc />
        public string Name => UnderlyingParticipant.Name;

        /// <inheritdoc />
        public ParticipantMetadata Metadata => new(
            UnderlyingParticipant.Metadata,
            UnderlyingParticipant.Name
        );

        /// <inheritdoc />
        public bool IsAgent => false;

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, metadata update events are not currently supported by the underlying SDK.
        ///     This event is declared for interface compliance but will not fire.
        /// </remarks>
#pragma warning disable CS0067 // Event is never used
        public event Action<ParticipantMetadata> MetadataUpdated;
#pragma warning restore CS0067

        #endregion

        #region ILocalParticipant Events

        /// <inheritdoc />
        public event Action<ILocalTrack> TrackPublished;

        /// <inheritdoc />
        public event Action<ILocalTrack> TrackUnpublished;

        #endregion

        #region ILocalParticipant Methods

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, this requires a user gesture context (e.g., button click).
        ///     Audio publishing uses the browser's getUserMedia API.
        /// </remarks>
        public Task<ILocalAudioTrack> PublishAudioTrackAsync(
            IAudioSource source,
            AudioPublishOptions options = default,
            CancellationToken ct = default)
        {
            // On WebGL, we typically use SetMicrophoneEnabled instead of publishing a custom source
            // The WebGL SDK manages the microphone track internally
            var tcs = new TaskCompletionSource<ILocalAudioTrack>();
            _coroutineRunner.StartCoroutine(PublishAudioTrackCoroutine(source, options, tcs, ct));
            return tcs.Task;
        }

        private IEnumerator PublishAudioTrackCoroutine(
            IAudioSource source,
            AudioPublishOptions options,
            TaskCompletionSource<ILocalAudioTrack> tcs,
            CancellationToken ct)
        {
            // For WebGL, we use SetMicrophoneEnabled which handles getUserMedia internally
            JSPromise<LocalTrackPublication> micPromise = UnderlyingParticipant.SetMicrophoneEnabled(true);

            while (!micPromise.IsDone)
            {
                if (ct.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(ct);
                    yield break;
                }

                yield return null;
            }

            if (micPromise.IsError)
            {
                JSError error = UnderlyingParticipant.LastMicrophoneError();
                tcs.TrySetException(new InvalidOperationException(
                    $"Failed to enable microphone: {error?.Message ?? "Unknown error"}"));
                yield break;
            }

            // Create a wrapper for the WebGL audio track
            var wrapper = new WebGLLocalAudioTrack(source);
            _localTracks.Add(wrapper);
            TrackPublished?.Invoke(wrapper);

            tcs.TrySetResult(wrapper);
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Video publishing on WebGL uses browser APIs and may have limitations
        ///     compared to native platforms.
        /// </remarks>
        public Task<ILocalVideoTrack> PublishVideoTrackAsync(
            IVideoSource source,
            VideoPublishOptions options = default,
            CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<ILocalVideoTrack>();
            _coroutineRunner.StartCoroutine(PublishVideoTrackCoroutine(source, options, tcs, ct));
            return tcs.Task;
        }

        private IEnumerator PublishVideoTrackCoroutine(
            IVideoSource source,
            VideoPublishOptions options,
            TaskCompletionSource<ILocalVideoTrack> tcs,
            CancellationToken ct)
        {
            bool isScreenShare = options.Source == VideoTrackSource.ScreenShare;

            if (source is WebGLCanvasVideoSource canvasSource)
            {
                MediaStreamTrack mediaStreamTrack = canvasSource.MediaStreamTrack;
                if (mediaStreamTrack == null)
                {
                    tcs.TrySetException(new InvalidOperationException(
                        "WebGL canvas source does not have an active MediaStreamTrack. StartCapture() must succeed before publishing."));
                    yield break;
                }

                JSPromise<LocalTrackPublication> publishPromise = UnderlyingParticipant.PublishTrack(
                    mediaStreamTrack,
                    BuildTrackPublishOptions(source, options));

                while (!publishPromise.IsDone)
                {
                    if (ct.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(ct);
                        yield break;
                    }

                    yield return null;
                }

                if (publishPromise.IsError)
                {
                    tcs.TrySetException(new InvalidOperationException("Failed to publish WebGL canvas video track"));
                    yield break;
                }

                LocalTrackPublication publication = publishPromise.ResolveValue;
                WebGLLocalVideoTrack canvasWrapper = new(
                    source,
                    isScreenShare,
                    mediaStreamTrack,
                    publication?.TrackSid);
                _localTracks.Add(canvasWrapper);
                TrackPublished?.Invoke(canvasWrapper);

                tcs.TrySetResult(canvasWrapper);
                yield break;
            }

            JSPromise<LocalTrackPublication> promise = isScreenShare
                ? UnderlyingParticipant.SetScreenShareEnabled(true)
                : UnderlyingParticipant.SetCameraEnabled(true);

            while (!promise.IsDone)
            {
                if (ct.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(ct);
                    yield break;
                }

                yield return null;
            }

            if (promise.IsError)
            {
                tcs.TrySetException(new InvalidOperationException("Failed to enable video"));
                yield break;
            }

            LocalTrackPublication enabledPublication = promise.ResolveValue;
            WebGLLocalVideoTrack wrapper = new(source, isScreenShare, sid: enabledPublication?.TrackSid);
            _localTracks.Add(wrapper);
            TrackPublished?.Invoke(wrapper);

            tcs.TrySetResult(wrapper);
        }

        /// <inheritdoc />
        public Task UnpublishTrackAsync(ILocalTrack track, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>();
            _coroutineRunner.StartCoroutine(UnpublishTrackCoroutine(track, tcs, ct));
            return tcs.Task;
        }

        private IEnumerator UnpublishTrackCoroutine(
            ILocalTrack track,
            TaskCompletionSource<bool> tcs,
            CancellationToken ct)
        {
            if (track is WebGLLocalAudioTrack audioTrack)
            {
                JSPromise<LocalTrackPublication> micPromise = UnderlyingParticipant.SetMicrophoneEnabled(false);

                while (!micPromise.IsDone)
                {
                    if (ct.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(ct);
                        yield break;
                    }

                    yield return null;
                }

                audioTrack.MarkUnpublished();
                _localTracks.Remove(track);
                TrackUnpublished?.Invoke(track);
            }
            else if (track is WebGLLocalVideoTrack videoTrack)
            {
                if (videoTrack.MediaStreamTrack != null)
                    UnderlyingParticipant.UnpublishTrack(videoTrack.MediaStreamTrack, true);
                else
                {
                    JSPromise promise;
                    if (videoTrack.IsScreenShare)
                        promise = UnderlyingParticipant.SetScreenShareEnabled(false);
                    else
                        promise = UnderlyingParticipant.SetCameraEnabled(false);

                    while (!promise.IsDone)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(ct);
                            yield break;
                        }

                        yield return null;
                    }
                }

                videoTrack.MarkUnpublished();
                _localTracks.Remove(track);
                TrackUnpublished?.Invoke(track);
            }

            tcs.TrySetResult(true);
        }

        /// <inheritdoc />
        public void SetAudioMuted(bool muted)
        {
            foreach (ILocalAudioTrack track in _localTracks.OfType<ILocalAudioTrack>()) track.SetMuted(muted);
        }

        /// <inheritdoc />
        public void SetVideoMuted(bool muted)
        {
            foreach (ILocalVideoTrack track in _localTracks.OfType<ILocalVideoTrack>()) track.SetMuted(muted);
        }

        #endregion

        #region Helper Methods

        internal LocalParticipant UnderlyingParticipant { get; }

        private static TrackPublishOptions BuildTrackPublishOptions(IVideoSource source, VideoPublishOptions options)
        {
            return new TrackPublishOptions
            {
                Name = string.IsNullOrWhiteSpace(options.TrackName) ? source?.Name : options.TrackName,
                Source = ToLiveKitTrackSource(options.Source),
                Simulcast = options.Simulcast,
                VideoCodec = ToLiveKitVideoCodec(options.Codec),
                videoEncoding = new VideoEncoding
                {
                    MaxBitrate = options.MaxBitrate > 0 ? options.MaxBitrate : null,
                    MaxFramerate = options.MaxFrameRate > 0 ? options.MaxFrameRate : null
                }
            };
        }

        private static TrackSource ToLiveKitTrackSource(VideoTrackSource source)
        {
            return source switch
            {
                VideoTrackSource.Camera => TrackSource.Camera,
                VideoTrackSource.ScreenShare => TrackSource.ScreenShare,
                _ => TrackSource.Unknown
            };
        }

        private static LiveKit.VideoCodec ToLiveKitVideoCodec(VideoCodec codec)
        {
            return codec switch
            {
                VideoCodec.AV1 => LiveKit.VideoCodec.AV1,
                VideoCodec.H264 => LiveKit.VideoCodec.H264,
                VideoCodec.VP9 => LiveKit.VideoCodec.VP9,
                _ => LiveKit.VideoCodec.VP8
            };
        }

        #endregion
    }
}
