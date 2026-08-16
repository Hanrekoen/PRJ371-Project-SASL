using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Readings;
using UnityEngine;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>
    ///     GUI-free, fully unit-testable sample/marker store backing
    ///     <see cref="ConvaiEmotionTimelineWindow" />. Holds a fixed-capacity ring buffer of
    ///     <see cref="EmotionReading" /> samples plus a separate ring buffer of transition
    ///     markers, both preallocated so steady-state <see cref="Record" />/<see cref="AddMarker" />
    ///     calls never allocate managed memory.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Per-label output scores are recorded against a label array that is locked once,
    ///         at <see cref="StartRecording" /> (resolved from the seeding reading's
    ///         <see cref="EmotionReading.AllScores" /> keys, sorted for a stable, deterministic
    ///         column order). A later sample whose reading no longer carries one of those labels
    ///         records <c>0</c> for it rather than growing/re-locking the label set.
    ///     </para>
    ///     <para>
    ///         Both rings wrap when full: the oldest entry is silently overwritten, exactly like a
    ///         profiler's history buffer. <see cref="Record" /> additionally throttles to
    ///         <see cref="SampleInterval" /> — calls at a shorter interval than the last accepted
    ///         sample are dropped (the first call after <see cref="StartRecording" /> is always
    ///         accepted).
    ///     </para>
    /// </remarks>
    internal sealed class EmotionTimelineRecorder
    {
        /// <summary>Default sample ring capacity (~60s of history at the default 0.1s interval).</summary>
        internal const int DefaultSampleCapacity = 600;

        /// <summary>Default marker ring capacity.</summary>
        internal const int DefaultMarkerCapacity = 128;

        /// <summary>Default minimum spacing, in seconds, between accepted samples.</summary>
        internal const float DefaultSampleInterval = 0.1f;

        /// <summary>Kind of transition an <see cref="EmotionTimelineMarker" /> records.</summary>
        internal enum MarkerKind
        {
            /// <summary>A <c>ConvaiEmotionController.DominantEmotionChanged</c> transition.</summary>
            Dominant,

            /// <summary>A <c>ConvaiEmotionController.MoodChanged</c> transition.</summary>
            Mood
        }

        private readonly float[] _sampleTimes;
        private readonly string[] _sampleDominantLabel;
        private readonly float[] _sampleDominantScore;
        private readonly string[] _sampleMoodLabel;
        private readonly float[] _sampleMoodScore;
        private readonly float[][] _sampleScores;

        private readonly EmotionTimelineMarker[] _markers;

        private int _sampleCount;
        private int _sampleWriteIndex;
        private float _lastRecordedTime;
        private bool _hasRecordedSample;

        private int _markerCount;
        private int _markerWriteIndex;

        private string[] _labels = Array.Empty<string>();

        /// <summary>Constructs a recorder with ctor-injected capacities/throttle, all defaulted.</summary>
        internal EmotionTimelineRecorder(
            int sampleCapacity = DefaultSampleCapacity,
            int markerCapacity = DefaultMarkerCapacity,
            float sampleInterval = DefaultSampleInterval)
        {
            SampleCapacity = Mathf.Max(1, sampleCapacity);
            MarkerCapacity = Mathf.Max(1, markerCapacity);
            SampleInterval = Mathf.Max(0f, sampleInterval);

            _sampleTimes = new float[SampleCapacity];
            _sampleDominantLabel = new string[SampleCapacity];
            _sampleDominantScore = new float[SampleCapacity];
            _sampleMoodLabel = new string[SampleCapacity];
            _sampleMoodScore = new float[SampleCapacity];
            _sampleScores = new float[SampleCapacity][];

            _markers = new EmotionTimelineMarker[MarkerCapacity];
        }

        /// <summary>Fixed sample ring capacity for this recorder instance.</summary>
        internal int SampleCapacity { get; }

        /// <summary>Fixed marker ring capacity for this recorder instance.</summary>
        internal int MarkerCapacity { get; }

        /// <summary>Minimum spacing, in seconds, enforced between accepted samples.</summary>
        internal float SampleInterval { get; }

        /// <summary>Whether <see cref="StartRecording" /> has been called without a matching <see cref="StopRecording" />.</summary>
        internal bool IsRecording { get; private set; }

        /// <summary>
        ///     Labels locked at <see cref="StartRecording" />, in the stable order every sample's
        ///     per-label score column follows. Empty before the first <see cref="StartRecording" />.
        /// </summary>
        internal IReadOnlyList<string> Labels => _labels;

        /// <summary>Number of samples currently held (<c>&lt;= <see cref="SampleCapacity" /></c>).</summary>
        internal int SampleCount => _sampleCount;

        /// <summary>Number of markers currently held (<c>&lt;= <see cref="MarkerCapacity" /></c>).</summary>
        internal int MarkerCount => _markerCount;

        /// <summary>
        ///     Begins a new recording pass: clears both rings, locks <see cref="Labels" /> from
        ///     <paramref name="seedReading" />'s <see cref="EmotionReading.AllScores" /> keys
        ///     (sorted ordinal-case-insensitive for determinism), and (re)allocates the per-slot
        ///     score arrays sized to the new label count so subsequent <see cref="Record" /> calls
        ///     never allocate.
        /// </summary>
        internal void StartRecording(EmotionReading seedReading)
        {
            ClearSamples();
            ClearMarkers();

            IReadOnlyDictionary<string, float> allScores = seedReading.AllScores;
            int labelCount = allScores?.Count ?? 0;
            var labels = new string[labelCount];
            if (allScores != null)
            {
                int i = 0;
                foreach (KeyValuePair<string, float> kvp in allScores)
                    labels[i++] = kvp.Key;
            }
            Array.Sort(labels, StringComparer.OrdinalIgnoreCase);
            _labels = labels;

            for (int slot = 0; slot < SampleCapacity; slot++)
            {
                float[] existing = _sampleScores[slot];
                if (existing == null || existing.Length != labelCount)
                    _sampleScores[slot] = new float[labelCount];
            }

            IsRecording = true;
        }

        /// <summary>Stops accepting new samples/markers. Existing history is left intact.</summary>
        internal void StopRecording()
        {
            IsRecording = false;
        }

        /// <summary>
        ///     Empties both rings. The locked <see cref="Labels" /> set (and its preallocated
        ///     per-slot score arrays) is left intact, so a <c>Clear()</c> issued mid-recording can
        ///     be followed immediately by more zero-allocation <see cref="Record" /> calls without
        ///     needing a fresh <see cref="StartRecording" />.
        /// </summary>
        internal void Clear()
        {
            ClearSamples();
            ClearMarkers();
        }

        private void ClearSamples()
        {
            _sampleCount = 0;
            _sampleWriteIndex = 0;
            _lastRecordedTime = 0f;
            _hasRecordedSample = false;
        }

        private void ClearMarkers()
        {
            _markerCount = 0;
            _markerWriteIndex = 0;
        }

        /// <summary>
        ///     Records one sample of <paramref name="reading" /> at <paramref name="time" /> when
        ///     recording is active and at least <see cref="SampleInterval" /> has elapsed since the
        ///     last accepted sample (the first sample after <see cref="StartRecording" /> is always
        ///     accepted). Missing/unknown labels in <paramref name="reading" /> record <c>0</c> for
        ///     that column. Returns whether the sample was accepted. No allocation on the accepted
        ///     or dropped path.
        /// </summary>
        internal bool Record(float time, EmotionReading reading)
        {
            if (!IsRecording) return false;
            if (_hasRecordedSample && time - _lastRecordedTime < SampleInterval) return false;

            _hasRecordedSample = true;
            _lastRecordedTime = time;

            int slot = _sampleWriteIndex;
            _sampleTimes[slot] = time;
            _sampleDominantLabel[slot] = reading.DominantLabel;
            _sampleDominantScore[slot] = reading.DominantScore;
            _sampleMoodLabel[slot] = reading.MoodLabel;
            _sampleMoodScore[slot] = reading.MoodScore;

            float[] scoreSlot = _sampleScores[slot];
            for (int i = 0; i < _labels.Length; i++)
                scoreSlot[i] = reading.GetScore(_labels[i]);

            _sampleWriteIndex = _sampleWriteIndex + 1 == SampleCapacity ? 0 : _sampleWriteIndex + 1;
            if (_sampleCount < SampleCapacity) _sampleCount++;

            return true;
        }

        /// <summary>
        ///     Appends a transition marker (own ring, wraps independently of the sample ring).
        ///     No allocation.
        /// </summary>
        internal void AddMarker(float time, MarkerKind kind, string label)
        {
            int slot = _markerWriteIndex;
            _markers[slot] = new EmotionTimelineMarker(time, kind, label);
            _markerWriteIndex = _markerWriteIndex + 1 == MarkerCapacity ? 0 : _markerWriteIndex + 1;
            if (_markerCount < MarkerCapacity) _markerCount++;
        }

        /// <summary>Sample time at oldest-to-newest <paramref name="viewIndex" /> in <c>[0, SampleCount)</c>.</summary>
        internal float GetSampleTime(int viewIndex) => _sampleTimes[ResolveSampleIndex(viewIndex)];

        /// <summary>Dominant label at oldest-to-newest <paramref name="viewIndex" />.</summary>
        internal string GetSampleDominantLabel(int viewIndex) => _sampleDominantLabel[ResolveSampleIndex(viewIndex)];

        /// <summary>Dominant score at oldest-to-newest <paramref name="viewIndex" />.</summary>
        internal float GetSampleDominantScore(int viewIndex) => _sampleDominantScore[ResolveSampleIndex(viewIndex)];

        /// <summary>Mood label at oldest-to-newest <paramref name="viewIndex" />.</summary>
        internal string GetSampleMoodLabel(int viewIndex) => _sampleMoodLabel[ResolveSampleIndex(viewIndex)];

        /// <summary>Mood score at oldest-to-newest <paramref name="viewIndex" />.</summary>
        internal float GetSampleMoodScore(int viewIndex) => _sampleMoodScore[ResolveSampleIndex(viewIndex)];

        /// <summary>
        ///     Per-label score at oldest-to-newest <paramref name="viewIndex" /> for
        ///     <see cref="Labels" />[<paramref name="labelIndex" />].
        /// </summary>
        internal float GetSampleScore(int viewIndex, int labelIndex) => _sampleScores[ResolveSampleIndex(viewIndex)][labelIndex];

        /// <summary>Marker at oldest-to-newest <paramref name="viewIndex" /> in <c>[0, MarkerCount)</c>.</summary>
        internal EmotionTimelineMarker GetMarker(int viewIndex) => _markers[ResolveMarkerIndex(viewIndex)];

        private int ResolveSampleIndex(int viewIndex)
        {
            if (viewIndex < 0 || viewIndex >= _sampleCount)
                throw new ArgumentOutOfRangeException(nameof(viewIndex));

            return _sampleCount < SampleCapacity ? viewIndex : (_sampleWriteIndex + viewIndex) % SampleCapacity;
        }

        private int ResolveMarkerIndex(int viewIndex)
        {
            if (viewIndex < 0 || viewIndex >= _markerCount)
                throw new ArgumentOutOfRangeException(nameof(viewIndex));

            return _markerCount < MarkerCapacity ? viewIndex : (_markerWriteIndex + viewIndex) % MarkerCapacity;
        }
    }

    /// <summary>One recorded transition point in an <see cref="EmotionTimelineRecorder" />'s marker ring.</summary>
    internal readonly struct EmotionTimelineMarker
    {
        internal EmotionTimelineMarker(float time, EmotionTimelineRecorder.MarkerKind kind, string label)
        {
            Time = time;
            Kind = kind;
            Label = label;
        }

        /// <summary>Wall/game time (seconds) the transition was observed.</summary>
        internal float Time { get; }

        /// <summary>Which event produced this marker.</summary>
        internal EmotionTimelineRecorder.MarkerKind Kind { get; }

        /// <summary>The new label the transition moved to.</summary>
        internal string Label { get; }
    }
}
