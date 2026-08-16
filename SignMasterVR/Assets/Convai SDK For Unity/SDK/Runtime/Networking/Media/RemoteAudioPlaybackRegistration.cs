using System;
using Convai.Infrastructure.Networking;
using UnityEngine;

namespace Convai.Runtime.Networking.Media
{
    /// <summary>
    ///     Owns one participant's remote audio stream and its playback callbacks.
    /// </summary>
    internal sealed class RemoteAudioPlaybackRegistration : IDisposable
    {
        private readonly Action _startedHandler;
        private readonly Action _stoppedHandler;
        private bool _disposed;
        private bool _playbackTrackingStarted;

        internal RemoteAudioPlaybackRegistration(
            string participantSid,
            string characterId,
            IRemoteAudioTrack track,
            IRemoteParticipant participant,
            AudioSource audioSource,
            IDisposable stream,
            Action startedHandler,
            Action stoppedHandler)
        {
            ParticipantSid = participantSid ?? throw new ArgumentNullException(nameof(participantSid));
            CharacterId = characterId ?? throw new ArgumentNullException(nameof(characterId));
            Track = track;
            Participant = participant;
            AudioSource = audioSource;
            Stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _startedHandler = startedHandler;
            _stoppedHandler = stoppedHandler;
        }

        /// <summary>
        ///     Starts playback callbacks after the owner has made this registration discoverable.
        ///     WebGL may invoke <see cref="IAudioPlaybackStateSource.PlaybackStarted" /> immediately
        ///     when a handler is added to an element that is already playing.
        /// </summary>
        internal void StartPlaybackTracking()
        {
            if (_disposed || _playbackTrackingStarted)
                return;

            if (Stream is IAudioPlaybackStateSource playbackSource)
            {
                _playbackTrackingStarted = true;
                playbackSource.PlaybackStopped += HandlePlaybackStopped;
                // Subscribe to the immediate-capable event last. WebGL's add accessor may invoke
                // this callback synchronously for an element that is already playing.
                playbackSource.PlaybackStarted += HandlePlaybackStarted;
            }
        }

        internal string ParticipantSid { get; }
        internal string CharacterId { get; }
        internal IRemoteAudioTrack Track { get; }
        internal IRemoteParticipant Participant { get; }
        internal AudioSource AudioSource { get; }
        internal IDisposable Stream { get; }
        internal bool IsPlaying { get; private set; }
        internal bool IsDucked { get; private set; }
        internal bool IsInterrupted { get; private set; }

        internal bool Duck(float targetGain, float durationSeconds)
        {
            if (_disposed || !IsPlaying || Stream is not IBargeInPlaybackControl playback)
                return false;

            playback.Duck(targetGain, durationSeconds);
            IsDucked = true;
            return true;
        }

        internal bool CommitInterruption(float durationSeconds)
        {
            if (_disposed || (!IsPlaying && !IsDucked) || Stream is not IBargeInPlaybackControl playback)
                return false;

            IsDucked = false;
            IsInterrupted = true;
            playback.CommitInterruption(durationSeconds);
            HandlePlaybackStopped();
            return true;
        }

        internal bool Restore(float durationSeconds)
        {
            if (_disposed || (!IsDucked && !IsInterrupted) || Stream is not IBargeInPlaybackControl playback)
                return false;

            bool playbackAlreadyActive = playback.Restore(durationSeconds);
            IsDucked = false;
            IsInterrupted = false;
            if (playbackAlreadyActive)
                HandlePlaybackStarted();
            return true;
        }

        internal bool TryGetPlayhead(out double playedSeconds)
        {
            playedSeconds = 0d;
            if (_disposed || Stream is not IAudioPlayheadSource source) return false;
            playedSeconds = source.PlayedSinceStartSeconds;
            return true;
        }

        internal bool TryGetTimeline(out AudioTimelineSnapshot snapshot)
        {
            snapshot = default;
            return !_disposed && Stream is IAudioTimelineSnapshotSource source &&
                   source.TryGetAudioTimelineSnapshot(out snapshot);
        }

        internal bool TryGetMediaTimeline(out AudioMediaTimelineSnapshot snapshot)
        {
            snapshot = default;
            return !_disposed && !IsInterrupted &&
                   Stream is IAudioMediaTimelineSnapshotSource source &&
                   source.TryGetAudioMediaTimelineSnapshot(out snapshot);
        }

        internal void StopOutput()
        {
            if (AudioSource == null) return;
            AudioSource.Stop();
            AudioSource.clip = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_playbackTrackingStarted && Stream is IAudioPlaybackStateSource playbackSource)
            {
                playbackSource.PlaybackStarted -= HandlePlaybackStarted;
                playbackSource.PlaybackStopped -= HandlePlaybackStopped;
            }

            _playbackTrackingStarted = false;
            Stream.Dispose();
        }

        private void HandlePlaybackStarted()
        {
            if (IsInterrupted || IsPlaying) return;
            IsPlaying = true;
            _startedHandler?.Invoke();
        }

        private void HandlePlaybackStopped()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            _stoppedHandler?.Invoke();
        }
    }
}
