using System;
using System.Collections.Generic;

namespace Convai.Modules.LipSync
{
    /// <summary>One per-frame measurement of audio-vs-visual alignment for a character.</summary>
    internal readonly struct LipSyncDriftSample
    {
        public LipSyncDriftSample(
            float timeSeconds,
            double audioTargetSeconds,
            double visualClockSeconds,
            float errorMs,
            float cumulativeCorrectionMs,
            float bufferedSeconds,
            float headroomSeconds,
            PlaybackState state,
            bool audioActive)
        {
            TimeSeconds = timeSeconds;
            AudioTargetSeconds = audioTargetSeconds;
            VisualClockSeconds = visualClockSeconds;
            ErrorMs = errorMs;
            CumulativeCorrectionMs = cumulativeCorrectionMs;
            BufferedSeconds = bufferedSeconds;
            HeadroomSeconds = headroomSeconds;
            State = state;
            AudioActive = audioActive;
        }

        /// <summary>Realtime-since-startup when the sample was taken.</summary>
        public float TimeSeconds { get; }

        /// <summary>Measured audio position on the turn timeline (device playhead based).</summary>
        public double AudioTargetSeconds { get; }

        /// <summary>Visual clock elapsed the engine sampled with this frame.</summary>
        public double VisualClockSeconds { get; }

        /// <summary>audio target - visual clock, in milliseconds. Positive = visuals behind audio.</summary>
        public float ErrorMs { get; }

        /// <summary>
        ///     Total signed correction (slew + rebase) applied to the visual clock since it started.
        ///     A steadily growing value means a real underlying drift source; its slope is the drift rate.
        /// </summary>
        public float CumulativeCorrectionMs { get; }

        public float BufferedSeconds { get; }
        public float HeadroomSeconds { get; }
        public PlaybackState State { get; }
        public bool AudioActive { get; }
    }

    /// <summary>A discrete lip sync lifecycle event (gate open, anchor, cancel, ...).</summary>
    internal readonly struct LipSyncDriftEvent
    {
        public LipSyncDriftEvent(float timeSeconds, string label)
        {
            TimeSeconds = timeSeconds;
            Label = label ?? string.Empty;
        }

        public float TimeSeconds { get; }
        public string Label { get; }
    }

    /// <summary>
    ///     Opt-in drift diagnostics for the lip sync pipeline. The runtime controller records one
    ///     sample per frame and the bridge records lifecycle events, but only while
    ///     <see cref="Enabled" /> is true (single branch when off — safe to ship). The editor drift
    ///     monitor window reads the ring buffers to visualize audio-vs-visual alignment live.
    /// </summary>
    internal static class LipSyncDriftMonitor
    {
        private const int MaxSamples = 1800; // 30 s at 60 fps
        private const int MaxEvents = 256;

        private static readonly object Sync = new();
        private static readonly Dictionary<string, Track> Tracks = new(StringComparer.Ordinal);

        /// <summary>Master switch. Keep false in normal play; the drift monitor window toggles it.</summary>
        public static bool Enabled { get; set; }

        /// <summary>
        ///     Live A/V calibration override in seconds (replaces LipSyncEngineConfig.TimeOffsetSeconds
        ///     while set). Negative values make the mouth lead the audio. Applied by the runtime
        ///     controller on the next frame; null restores the configured value.
        /// </summary>
        public static float? TimeOffsetOverrideSeconds { get; set; }

        /// <summary>
        ///     Supplies timestamps for events recorded from pure-C# code (the bridge has no Unity
        ///     dependency). The runtime controller assigns this from Time.realtimeSinceStartup.
        /// </summary>
        public static Func<float> TimeSource { get; set; }

        public static void RecordSample(string characterId, in LipSyncDriftSample sample)
        {
            if (!Enabled || string.IsNullOrEmpty(characterId)) return;

            lock (Sync)
            {
                GetTrack(characterId).Push(sample);
            }
        }

        public static void RecordEvent(string characterId, string label)
        {
            if (!Enabled || string.IsNullOrEmpty(characterId)) return;

            float time = TimeSource?.Invoke() ?? 0f;
            lock (Sync)
            {
                Track track = GetTrack(characterId);
                track.Events.Add(new LipSyncDriftEvent(time, label));
                if (track.Events.Count > MaxEvents) track.Events.RemoveRange(0, track.Events.Count - MaxEvents);
            }
        }

        /// <summary>Fills <paramref name="buffer" /> with all tracked character ids.</summary>
        public static void GetCharacterIds(List<string> buffer)
        {
            buffer.Clear();
            lock (Sync)
            {
                foreach (string key in Tracks.Keys) buffer.Add(key);
            }
        }

        /// <summary>Copies the sample ring (oldest to newest) into <paramref name="buffer" />.</summary>
        public static void CopySamples(string characterId, List<LipSyncDriftSample> buffer)
        {
            buffer.Clear();
            lock (Sync)
            {
                if (!Tracks.TryGetValue(characterId, out Track track)) return;

                for (int i = 0; i < track.Count; i++) buffer.Add(track.Get(i));
            }
        }

        /// <summary>Copies recorded events (oldest to newest) into <paramref name="buffer" />.</summary>
        public static void CopyEvents(string characterId, List<LipSyncDriftEvent> buffer)
        {
            buffer.Clear();
            lock (Sync)
            {
                if (!Tracks.TryGetValue(characterId, out Track track)) return;

                buffer.AddRange(track.Events);
            }
        }

        public static void Clear()
        {
            lock (Sync)
            {
                Tracks.Clear();
            }
        }

        private static Track GetTrack(string characterId)
        {
            if (!Tracks.TryGetValue(characterId, out Track track))
            {
                track = new Track();
                Tracks[characterId] = track;
            }

            return track;
        }

        private sealed class Track
        {
            private readonly LipSyncDriftSample[] _ring = new LipSyncDriftSample[MaxSamples];
            private int _head;

            public readonly List<LipSyncDriftEvent> Events = new();
            public int Count { get; private set; }

            public void Push(in LipSyncDriftSample sample)
            {
                _ring[(_head + Count) % MaxSamples] = sample;
                if (Count < MaxSamples)
                    Count++;
                else
                    _head = (_head + 1) % MaxSamples;
            }

            public LipSyncDriftSample Get(int logicalIndex) => _ring[(_head + logicalIndex) % MaxSamples];
        }
    }
}
