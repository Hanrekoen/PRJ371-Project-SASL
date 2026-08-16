using System;
using LiveKit;
using UnityEngine;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Wraps LiveKit <see cref="AudioStream" /> to implement <see cref="IAudioPlaybackStateSource" />
    ///     so that AudioTrackManager can subscribe to playback start/stop and publish CharacterAudioPlaybackStateChanged.
    /// </summary>
    public sealed class NativeAudioStreamPlaybackAdapter : IDisposable, IAudioPlaybackStateSource, IAudioPlayheadSource,
        IAudioTimelineSnapshotSource, IBargeInPlaybackControl
    {
        private readonly AudioStream _inner;
        private bool _disposed;

        public NativeAudioStreamPlaybackAdapter(AudioStream inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <inheritdoc />
        public double PlayedSinceStartSeconds => _disposed ? 0d : _inner.PlayedSinceSignalStartSeconds;

        bool IAudioTimelineSnapshotSource.TryGetAudioTimelineSnapshot(out AudioTimelineSnapshot snapshot)
        {
            if (_disposed)
            {
                snapshot = default;
                return false;
            }

            AudioStreamPlaybackSnapshot source = _inner.PlaybackSnapshot;
            long renderedSourceFrame = source.EstimateRenderedSourceFrame(AudioSettings.dspTime);
            snapshot = new AudioTimelineSnapshot(
                renderedSourceFrame,
                source.SampleRate,
                source.Channels,
                source.FormatGeneration,
                source.BufferedFrames,
                MapPlaybackState(source.State),
                source.ReceivedFrames,
                source.RenderedFrames,
                source.SkippedFrames,
                source.OverflowCount,
                source.UnderrunCount,
                source.AbsoluteSourceFrame,
                source.SignalStartAbsoluteSourceFrame,
                source.SignalGeneration,
                source.DiscontinuityGeneration);
            return source.IsValid;
        }

        private static AudioTimelinePlaybackState MapPlaybackState(AudioStreamPlaybackState state)
        {
            return state switch
            {
                AudioStreamPlaybackState.Idle => AudioTimelinePlaybackState.Idle,
                AudioStreamPlaybackState.Playing => AudioTimelinePlaybackState.Playing,
                AudioStreamPlaybackState.Underrun => AudioTimelinePlaybackState.Underrun,
                AudioStreamPlaybackState.Disposed => AudioTimelinePlaybackState.Disposed,
                _ => AudioTimelinePlaybackState.Idle
            };
        }

        public event Action PlaybackStarted
        {
            add => _inner.PlaybackStarted += value;
            remove => _inner.PlaybackStarted -= value;
        }

        public event Action PlaybackStopped
        {
            add => _inner.PlaybackStopped += value;
            remove => _inner.PlaybackStopped -= value;
        }

        void IBargeInPlaybackControl.Duck(float targetGain, float durationSeconds)
        {
            if (!_disposed)
                _inner.DuckPlayback(targetGain, durationSeconds);
        }

        void IBargeInPlaybackControl.CommitInterruption(float durationSeconds)
        {
            if (!_disposed)
                _inner.CommitPlaybackInterruption(durationSeconds);
        }

        bool IBargeInPlaybackControl.Restore(float durationSeconds)
        {
            if (_disposed)
                return false;

            _inner.RestorePlayback(durationSeconds);

            // Native playback emits a fresh signal once new PCM reaches the output.
            // Waiting for it keeps the lip-sync clock anchored to the new response.
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _inner.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
