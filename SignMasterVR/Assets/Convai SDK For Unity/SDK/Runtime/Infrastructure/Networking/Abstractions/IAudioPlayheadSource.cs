namespace Convai.Infrastructure.Networking
{
    internal enum AudioTimelinePlaybackState
    {
        Idle,
        Playing,
        Underrun,
        Disposed
    }

    internal enum AudioTimelineClockMode
    {
        SampleLocked,
        BrowserMediaLocked,
        BrowserMediaFallback,
        LegacyPlayhead,
        WallClock
    }

    /// <summary>
    ///     Read-only timing reported by the browser audio element. The logical position is monotonic
    ///     across element replacement; signal onset is measured by a non-output Web Audio analyser.
    /// </summary>
    internal readonly struct AudioMediaTimelineSnapshot
    {
        public AudioMediaTimelineSnapshot(
            double logicalPositionSeconds,
            AudioTimelinePlaybackState state,
            double signalStartPositionSeconds = -1d,
            int signalGeneration = 0,
            int discontinuityGeneration = 0,
            bool analyserAvailable = false,
            int stallCount = 0,
            int elementReplacementCount = 0)
        {
            LogicalPositionSeconds = logicalPositionSeconds;
            State = state;
            SignalStartPositionSeconds = signalStartPositionSeconds;
            SignalGeneration = signalGeneration;
            DiscontinuityGeneration = discontinuityGeneration;
            AnalyserAvailable = analyserAvailable;
            StallCount = stallCount;
            ElementReplacementCount = elementReplacementCount;
        }

        public double LogicalPositionSeconds { get; }
        public AudioTimelinePlaybackState State { get; }
        public double SignalStartPositionSeconds { get; }
        public int SignalGeneration { get; }
        public int DiscontinuityGeneration { get; }
        public bool AnalyserAvailable { get; }
        public int StallCount { get; }
        public int ElementReplacementCount { get; }
        public bool HasSignalStart => SignalGeneration > 0 && SignalStartPositionSeconds >= 0d;
        public bool IsValid => LogicalPositionSeconds >= 0d &&
                               !double.IsNaN(LogicalPositionSeconds) &&
                               !double.IsInfinity(LogicalPositionSeconds);
    }

    internal readonly struct AudioTimelineSnapshot
    {
        public AudioTimelineSnapshot(
            long absoluteSourceFrame,
            int sampleRate,
            int channels,
            int formatGeneration,
            long bufferedFrames,
            AudioTimelinePlaybackState state,
            long receivedFrames,
            long renderedFrames,
            long skippedFrames,
            long overflowCount,
            long underrunCount,
            long committedSourceFrame = -1,
            long signalStartAbsoluteSourceFrame = -1,
            int signalGeneration = 0,
            int discontinuityGeneration = 0)
        {
            AbsoluteSourceFrame = absoluteSourceFrame;
            SampleRate = sampleRate;
            Channels = channels;
            FormatGeneration = formatGeneration;
            BufferedFrames = bufferedFrames;
            State = state;
            ReceivedFrames = receivedFrames;
            RenderedFrames = renderedFrames;
            SkippedFrames = skippedFrames;
            OverflowCount = overflowCount;
            UnderrunCount = underrunCount;
            CommittedSourceFrame = committedSourceFrame >= 0 ? committedSourceFrame : absoluteSourceFrame;
            SignalStartAbsoluteSourceFrame = signalStartAbsoluteSourceFrame;
            SignalGeneration = signalGeneration;
            DiscontinuityGeneration = discontinuityGeneration;
        }

        public long AbsoluteSourceFrame { get; }
        public int SampleRate { get; }
        public int Channels { get; }
        public int FormatGeneration { get; }
        public long BufferedFrames { get; }
        public AudioTimelinePlaybackState State { get; }
        public long ReceivedFrames { get; }
        public long RenderedFrames { get; }
        public long SkippedFrames { get; }
        public long OverflowCount { get; }
        public long UnderrunCount { get; }
        /// <summary>End of source data committed by the audio callback, before DSP interpolation.</summary>
        public long CommittedSourceFrame { get; }
        public long SignalStartAbsoluteSourceFrame { get; }
        public int SignalGeneration { get; }
        public int DiscontinuityGeneration { get; }
        public bool HasSignalStart => SignalGeneration > 0 && SignalStartAbsoluteSourceFrame >= 0;
        public bool IsValid => SampleRate > 0 && Channels > 0 && FormatGeneration > 0;
    }

    internal readonly struct AudioTimelineSampleAnchor
    {
        public AudioTimelineSampleAnchor(
            string characterId,
            string responseId,
            int? turnId,
            int? epoch,
            int? sequence,
            int sampleRate,
            long responseAudioStartSample,
            long? finalAudioSample)
        {
            CharacterId = characterId ?? string.Empty;
            ResponseId = responseId ?? string.Empty;
            TurnId = turnId;
            Epoch = epoch;
            Sequence = sequence;
            SampleRate = sampleRate;
            ResponseAudioStartSample = responseAudioStartSample;
            FinalAudioSample = finalAudioSample;
        }

        public string CharacterId { get; }
        public string ResponseId { get; }
        public int? TurnId { get; }
        public int? Epoch { get; }
        public int? Sequence { get; }
        public int SampleRate { get; }
        public long ResponseAudioStartSample { get; }
        public long? FinalAudioSample { get; }
        public bool IsValid => SampleRate > 0 && ResponseAudioStartSample >= 0 &&
                               !string.IsNullOrWhiteSpace(ResponseId);
    }

    /// <summary>Read-only native timing capability. Deliberately exposes no audio controls.</summary>
    internal interface IAudioTimelineSnapshotSource
    {
        bool TryGetAudioTimelineSnapshot(out AudioTimelineSnapshot snapshot);
    }

    /// <summary>Read-only WebGL media timing capability. It deliberately exposes no playback controls.</summary>
    internal interface IAudioMediaTimelineSnapshotSource
    {
        bool TryGetAudioMediaTimelineSnapshot(out AudioMediaTimelineSnapshot snapshot);
    }

    /// <summary>
    ///     Exposes the measured audio playhead of a remote stream: how much source audio has
    ///     actually been rendered to the output device since playback started. Unlike a wall
    ///     clock, this freezes during underruns and accounts for drift-correction skips, so
    ///     visuals sampled against it stay locked to what is audible.
    /// </summary>
    public interface IAudioPlayheadSource
    {
        /// <summary>
        ///     Seconds of source audio consumed by the device since the current playback signal
        ///     started (the instant <see cref="IAudioPlaybackStateSource.PlaybackStarted" /> refers to).
        ///     Freezes on underrun/stop; resets when a new playback signal starts.
        /// </summary>
        public double PlayedSinceStartSeconds { get; }
    }
}
