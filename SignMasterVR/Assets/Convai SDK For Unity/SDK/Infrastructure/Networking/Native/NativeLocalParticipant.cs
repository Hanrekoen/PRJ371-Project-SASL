using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Convai.Infrastructure.Networking;
using LiveKit;
using LiveKit.Proto;

#pragma warning disable CS0067

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Native implementation of <see cref="ILocalParticipant" /> wrapping LiveKit.LocalParticipant.
    /// </summary>
    internal class NativeLocalParticipant : ILocalParticipant
    {
        #region Private Fields

        private readonly List<ILocalTrack> _localTracks = new();

        #endregion

        #region Constructor

        public NativeLocalParticipant(LocalParticipant participant)
        {
            UnderlyingParticipant = participant ?? throw new ArgumentNullException(nameof(participant));
        }

        #endregion

        #region ILocalParticipant Properties

        /// <inheritdoc />
        public IReadOnlyList<ILocalTrack> LocalTracks => _localTracks;

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
        public event Action<ParticipantMetadata> MetadataUpdated;

        #endregion

        #region ILocalParticipant Events

        /// <inheritdoc />
        public event Action<ILocalTrack> TrackPublished;

        /// <inheritdoc />
        public event Action<ILocalTrack> TrackUnpublished;

        #endregion

        #region ILocalParticipant Methods

        /// <inheritdoc />
        public async Task<ILocalAudioTrack> PublishAudioTrackAsync(
            IAudioSource source,
            AudioPublishOptions options = default,
            CancellationToken ct = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (!UnderlyingParticipant.Room.TryGetTarget(out Room room))
                throw new InvalidOperationException("Participant room reference is unavailable.");

            RtcAudioSource rtcSource = ResolveRtcAudioSource(source);
            source.StartCapture();

            string trackName = string.IsNullOrWhiteSpace(source.Name) ? "microphone" : source.Name;
            var track = LocalAudioTrack.CreateAudioTrack(trackName, rtcSource, room);

            var publishOptions = new TrackPublishOptions
            {
                Source = options.Source == AudioTrackSource.Microphone
                    ? TrackSource.SourceMicrophone
                    : TrackSource.SourceUnknown,
                Dtx = options.Dtx
            };

            YieldInstruction publishInstruction = UnderlyingParticipant.PublishTrack(track, publishOptions);
            await WaitForInstructionAsync(publishInstruction, ct);

            if (publishInstruction.IsError)
            {
                source.StopCapture();
                throw new InvalidOperationException("Failed to publish audio track.");
            }

            var wrapper = new NativeLocalAudioTrack(track, source);
            _localTracks.Add(wrapper);
            TrackPublished?.Invoke(wrapper);

            return wrapper;
        }

        private static RtcAudioSource ResolveRtcAudioSource(IAudioSource source) =>
            source switch
            {
                NativeMicrophoneSource nativeMicSource => nativeMicSource.UnderlyingSource,
                NativeAudioClipMicrophoneSource clipSource => clipSource.UnderlyingSource,
                _ => throw new ArgumentException(
                    "Source must be a NativeMicrophoneSource or NativeAudioClipMicrophoneSource",
                    nameof(source))
            };

        /// <inheritdoc />
        public async Task<ILocalVideoTrack> PublishVideoTrackAsync(
            IVideoSource source,
            VideoPublishOptions options = default,
            CancellationToken ct = default)
        {
            if (source is not NativeTextureVideoSource nativeVideoSource)
                throw new ArgumentException("Source must be a NativeTextureVideoSource", nameof(source));

            if (!UnderlyingParticipant.Room.TryGetTarget(out Room room))
                throw new InvalidOperationException("Participant room reference is unavailable.");

            string trackName = !string.IsNullOrWhiteSpace(options.TrackName)
                ? options.TrackName
                : string.IsNullOrWhiteSpace(source.Name)
                    ? "video"
                    : source.Name;
            var track = LocalVideoTrack.CreateVideoTrack(trackName, nativeVideoSource.UnderlyingSource, room);

            int maxBitrate = options.MaxBitrate > 0
                ? options.MaxBitrate
                : 1_500_000;

            int maxFrameRate = options.MaxFrameRate > 0
                ? options.MaxFrameRate
                : options.Source == VideoTrackSource.ScreenShare
                    ? 15
                    : 30;

            var publishOptions = new TrackPublishOptions
            {
                Source = options.Source switch
                {
                    VideoTrackSource.ScreenShare => TrackSource.SourceScreenshare,
                    VideoTrackSource.Camera => TrackSource.SourceCamera,
                    _ => TrackSource.SourceUnknown
                },
                VideoCodec = MapVideoCodec(options.Codec),
                VideoEncoding = new VideoEncoding { MaxBitrate = (ulong)maxBitrate, MaxFramerate = (uint)maxFrameRate },
                Simulcast = options.Simulcast
            };

            YieldInstruction publishInstruction = UnderlyingParticipant.PublishTrack(track, publishOptions);
            await WaitForInstructionAsync(publishInstruction, ct);

            if (publishInstruction.IsError) throw new InvalidOperationException("Failed to publish video track.");

            nativeVideoSource.StartCapture();

            var wrapper = new NativeLocalVideoTrack(track, nativeVideoSource);
            _localTracks.Add(wrapper);
            TrackPublished?.Invoke(wrapper);

            return wrapper;
        }

        /// <inheritdoc />
        public async Task UnpublishTrackAsync(ILocalTrack track, CancellationToken ct = default)
        {
            if (track is NativeLocalAudioTrack audioTrack)
            {
                // Stop capture first to avoid feeding frames into a track that is tearing down.
                audioTrack.Source.StopCapture();

                YieldInstruction unpublishInstruction =
                    UnderlyingParticipant.UnpublishTrack(audioTrack.UnderlyingTrack, true);
                await WaitForInstructionAsync(unpublishInstruction, ct);

                audioTrack.MarkUnpublished();
                _localTracks.Remove(track);
                TrackUnpublished?.Invoke(track);
            }
            else if (track is NativeLocalVideoTrack videoTrack)
            {
                YieldInstruction unpublishInstruction =
                    UnderlyingParticipant.UnpublishTrack(videoTrack.UnderlyingTrack, true);
                await WaitForInstructionAsync(unpublishInstruction, ct);

                videoTrack.MarkUnpublished();
                videoTrack.Source.StopCapture();
                _localTracks.Remove(track);
                TrackUnpublished?.Invoke(track);
            }
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

        private static LiveKit.Proto.VideoCodec MapVideoCodec(VideoCodec codec)
        {
            return codec switch
            {
                VideoCodec.VP8 => LiveKit.Proto.VideoCodec.Vp8,
                VideoCodec.VP9 => LiveKit.Proto.VideoCodec.Vp9,
                VideoCodec.H264 => LiveKit.Proto.VideoCodec.H264,
                VideoCodec.AV1 => LiveKit.Proto.VideoCodec.Av1,
                _ => LiveKit.Proto.VideoCodec.Vp8
            };
        }

        private static async Task WaitForInstructionAsync(YieldInstruction instruction, CancellationToken ct)
        {
            if (instruction == null) throw new ArgumentNullException(nameof(instruction));

            while (!instruction.IsDone)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(50, ct);
            }
        }

        internal LocalParticipant UnderlyingParticipant { get; }

        #endregion
    }
}
