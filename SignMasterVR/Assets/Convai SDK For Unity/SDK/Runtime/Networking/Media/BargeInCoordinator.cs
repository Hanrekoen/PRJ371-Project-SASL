using System;
using System.Collections.Generic;
using System.Diagnostics;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Runtime.Adapters.Networking;

namespace Convai.Runtime.Networking.Media
{
    internal enum BargeInTrigger
    {
        Manual = 0,
        ServerVoiceActivity = 1,
        ClientVoiceActivity = 2
    }

    internal enum BargeInMarkerStage
    {
        ClientSpeechCandidate = 0,
        ClientSpeechConfirmed = 1,
        DuckStarted = 2,
        DuckCancelled = 3,
        FadeStarted = 4,
        PlaybackRestored = 5,
        InterruptRequested = 6,
        InterruptSent = 7,
        ServerSpeechStarted = 8
    }

    internal readonly struct BargeInMarker
    {
        internal BargeInMarker(
            BargeInMarkerStage stage,
            BargeInTrigger trigger,
            int affectedStreams,
            long timestampTicks)
        {
            Stage = stage;
            Trigger = trigger;
            AffectedStreams = affectedStreams;
            TimestampTicks = timestampTicks;
        }

        internal BargeInMarkerStage Stage { get; }
        internal BargeInTrigger Trigger { get; }
        internal int AffectedStreams { get; }
        internal long TimestampTicks { get; }

        internal static BargeInMarker Create(
            BargeInMarkerStage stage,
            BargeInTrigger trigger,
            int affectedStreams = 0) =>
            new(stage, trigger, affectedStreams, Stopwatch.GetTimestamp());
    }

    /// <summary>
    ///     Owns the client-side presentation state for a character interruption.
    ///     Transport messages, server VAD, and local VAD all converge here so duplicate
    ///     signals cannot restart envelopes or suppress multiple turns.
    /// </summary>
    internal sealed class BargeInCoordinator : IDisposable
    {
        internal const float DefaultDuckGain = 0.25f;
        internal const float DefaultDuckSeconds = 0.05f;
        internal const float DefaultRestoreSeconds = 0.1f;

        private readonly Func<AudioTrackManager> _audioTrackManagerProvider;
        private readonly Func<ResolvedTurnTakingOptions> _turnTakingOptionsProvider;
        private readonly Action<BargeInMarker> _markerSink;
        private readonly HashSet<string> _duckedCharacterIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BargeInTrigger> _interruptionTriggers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _interruptedUtteranceIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _lastUtteranceIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _speakingCharacterIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _stoppedAfterInterruption = new(StringComparer.Ordinal);
        private BargeInTrigger _activeTrigger;

        internal BargeInCoordinator(
            Func<AudioTrackManager> audioTrackManagerProvider,
            Func<ResolvedTurnTakingOptions> turnTakingOptionsProvider,
            Action<BargeInMarker> markerSink = null)
        {
            _audioTrackManagerProvider = audioTrackManagerProvider ??
                                         throw new ArgumentNullException(nameof(audioTrackManagerProvider));
            _turnTakingOptionsProvider = turnTakingOptionsProvider ??
                                         throw new ArgumentNullException(nameof(turnTakingOptionsProvider));
            _markerSink = markerSink;
        }

        internal bool IsInterrupted => _interruptedUtteranceIds.Count > 0;
        internal bool IsDucked => _duckedCharacterIds.Count > 0;

        internal bool HasActivePlayback
        {
            get
            {
                AudioTrackManager manager = _audioTrackManagerProvider();
                if (manager == null)
                    return false;

                foreach (string characterId in _speakingCharacterIds)
                {
                    if (manager.IsCharacterAudioPlaybackActive(characterId))
                        return true;
                }

                return false;
            }
        }

        internal bool Duck(BargeInTrigger trigger)
        {
            if (IsDucked)
                return false;

            ResolvedTurnTakingOptions options = ResolveOptions();
            if (!options.SmoothInterruption)
                return false;

            AudioTrackManager manager = _audioTrackManagerProvider();
            if (manager == null)
                return false;

            int affected = 0;
            foreach (string characterId in _speakingCharacterIds)
            {
                if (!manager.DuckCharacterAudio(characterId, DefaultDuckGain, DefaultDuckSeconds))
                    continue;

                _duckedCharacterIds.Add(characterId);
                affected++;
            }

            if (affected == 0)
                return false;

            _activeTrigger = trigger;
            Record(BargeInMarkerStage.DuckStarted, trigger, affected);
            return true;
        }

        internal bool Commit(BargeInTrigger trigger)
        {
            ResolvedTurnTakingOptions options = ResolveOptions();
            if (!options.SmoothInterruption)
                return false;

            AudioTrackManager manager = _audioTrackManagerProvider();
            if (manager == null)
                return false;

            bool hadDuckedTargets = IsDucked;
            if (hadDuckedTargets)
            {
                foreach (string characterId in _duckedCharacterIds)
                {
                    if (!_speakingCharacterIds.Contains(characterId))
                    {
                        manager.RestoreInterruptedCharacterAudio(
                            characterId,
                            DefaultRestoreSeconds);
                    }
                }
            }

            int affected = 0;
            foreach (string characterId in _speakingCharacterIds)
            {
                if (!manager.CommitCharacterAudioInterruption(
                        characterId,
                        options.BargeInFadeOutSeconds))
                {
                    if (hadDuckedTargets && _duckedCharacterIds.Contains(characterId))
                    {
                        manager.RestoreInterruptedCharacterAudio(
                            characterId,
                            DefaultRestoreSeconds);
                    }

                    continue;
                }

                _lastUtteranceIds.TryGetValue(characterId, out string utteranceId);
                _interruptedUtteranceIds[characterId] = utteranceId ?? string.Empty;
                _interruptionTriggers[characterId] = trigger;
                _stoppedAfterInterruption.Remove(characterId);
                affected++;
            }

            _duckedCharacterIds.Clear();
            if (affected == 0)
                return false;

            _activeTrigger = trigger;
            Record(BargeInMarkerStage.FadeStarted, trigger, affected);
            return true;
        }

        internal bool CancelDuck()
        {
            if (!IsDucked)
                return false;

            AudioTrackManager manager = _audioTrackManagerProvider();
            int affected = 0;
            if (manager != null)
            {
                foreach (string characterId in _duckedCharacterIds)
                {
                    if (manager.RestoreInterruptedCharacterAudio(
                            characterId,
                            DefaultRestoreSeconds))
                        affected++;
                }
            }

            Record(BargeInMarkerStage.DuckCancelled, _activeTrigger, affected);
            _duckedCharacterIds.Clear();
            if (!IsInterrupted)
                _activeTrigger = default;
            return true;
        }

        internal void ObserveCharacterSpeech(CharacterSpeechStateChanged speechEvent)
        {
            string characterId = speechEvent.CharacterId;
            if (string.IsNullOrWhiteSpace(characterId))
                return;

            if (!speechEvent.IsSpeaking)
            {
                _speakingCharacterIds.Remove(characterId);
                if (_interruptedUtteranceIds.ContainsKey(characterId))
                    _stoppedAfterInterruption.Add(characterId);
                return;
            }

            _speakingCharacterIds.Add(characterId);
            if (_interruptedUtteranceIds.TryGetValue(
                    characterId,
                    out string interruptedUtteranceId))
            {
                bool utteranceChanged =
                    !string.IsNullOrWhiteSpace(interruptedUtteranceId) &&
                    !string.IsNullOrWhiteSpace(speechEvent.UtteranceId) &&
                    !string.Equals(
                        speechEvent.UtteranceId,
                        interruptedUtteranceId,
                        StringComparison.Ordinal);

                if (_stoppedAfterInterruption.Contains(characterId) || utteranceChanged)
                    Restore(characterId);
            }

            _lastUtteranceIds[characterId] = speechEvent.UtteranceId ?? string.Empty;
        }

        internal void Reset()
        {
            AudioTrackManager manager = _audioTrackManagerProvider();
            if (manager != null)
            {
                foreach (string characterId in _duckedCharacterIds)
                    manager.RestoreInterruptedCharacterAudio(characterId, 0f);
                foreach (string characterId in _interruptedUtteranceIds.Keys)
                    manager.RestoreInterruptedCharacterAudio(characterId, 0f);
            }

            ResetForConnectionBoundary();
        }

        /// <summary>
        ///     Drops connection-scoped speech evidence without touching playback. Teardown clears
        ///     the registrations immediately afterward, so restoring here could briefly republish
        ///     an inaudible WebGL element as active.
        /// </summary>
        internal void ResetForConnectionBoundary()
        {
            _duckedCharacterIds.Clear();
            _interruptionTriggers.Clear();
            _interruptedUtteranceIds.Clear();
            _lastUtteranceIds.Clear();
            _speakingCharacterIds.Clear();
            _stoppedAfterInterruption.Clear();
            _activeTrigger = default;
        }

        public void Dispose() => ResetForConnectionBoundary();

        private void Restore(string characterId)
        {
            BargeInTrigger trigger = _interruptionTriggers.TryGetValue(
                characterId,
                out BargeInTrigger interruptedTrigger)
                ? interruptedTrigger
                : _activeTrigger;
            bool restored = _audioTrackManagerProvider()?.RestoreInterruptedCharacterAudio(
                characterId,
                DefaultRestoreSeconds) ?? false;

            _interruptionTriggers.Remove(characterId);
            _interruptedUtteranceIds.Remove(characterId);
            _stoppedAfterInterruption.Remove(characterId);
            Record(BargeInMarkerStage.PlaybackRestored, trigger, restored ? 1 : 0);
            if (!IsInterrupted && !IsDucked)
                _activeTrigger = default;
        }

        private ResolvedTurnTakingOptions ResolveOptions() =>
            _turnTakingOptionsProvider() ?? ResolvedTurnTakingOptions.DefaultHandsFree;

        private void Record(
            BargeInMarkerStage stage,
            BargeInTrigger trigger,
            int affectedStreams = 0) =>
            _markerSink?.Invoke(BargeInMarker.Create(stage, trigger, affectedStreams));
    }
}
