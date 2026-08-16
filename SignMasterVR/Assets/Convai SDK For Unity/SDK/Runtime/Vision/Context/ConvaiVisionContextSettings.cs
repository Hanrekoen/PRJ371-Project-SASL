using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.RestAPI.Services;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Runtime.Vision.Context
{
    /// <summary>Controls whether a room opts into backend dynamic context vision.</summary>
    public enum ConvaiVisionContextMode
    {
        /// <summary>
        ///     Follow the configured Connection Type: vision is enabled only when the room is already
        ///     set to Video. Never upgrades an Audio room on its own.
        /// </summary>
        Auto = 0,

        /// <summary>Always enable dynamic vision; forces the room connection to Video.</summary>
        Enabled = 1,

        /// <summary>
        ///     Never send dynamic-vision config. The configured Connection Type is left untouched, so
        ///     legacy native-video paths keep working.
        /// </summary>
        Disabled = 2
    }

    /// <summary>
    ///     The runtime-adjustable input lanes whose respond mode can be changed mid-session via
    ///     <see cref="Convai.Runtime.Room.IConvaiRoomConnectionService.UpdateRespondMode" />.
    ///     User text and voice always respond and cannot be changed.
    /// </summary>
    public enum ConvaiRespondModeLane
    {
        /// <summary>Newly sampled vision frames (backend modality <c>vision</c>).</summary>
        Vision = 0,

        /// <summary>Dynamic-context text updates (backend modality <c>context_update</c>).</summary>
        ContextUpdate = 1,

        /// <summary>Explicit vision triggers without a per-request mode (backend modality <c>trigger</c>).</summary>
        Trigger = 2,

        /// <summary>Scene-metadata updates (backend modality <c>scene_metadata</c>).</summary>
        SceneMetadata = 3
    }

    /// <summary>Conversions between <see cref="ConvaiRespondModeLane" /> and its backend modality strings.</summary>
    public static class ConvaiRespondModeLaneExtensions
    {
        /// <summary>Maps a runtime-adjustable lane to the exact backend modality string.</summary>
        public static string ToWireString(this ConvaiRespondModeLane lane) =>
            lane switch
            {
                ConvaiRespondModeLane.ContextUpdate => "context_update",
                ConvaiRespondModeLane.Trigger => "trigger",
                ConvaiRespondModeLane.SceneMetadata => "scene_metadata",
                _ => "vision"
            };
    }

    /// <summary>
    ///     One horizon of non-uniform frame sampling: pick <see cref="Count" /> frames spaced
    ///     <see cref="IntervalMs" /> apart. Combine windows for dense-recent + sparse-older selection.
    /// </summary>
    [Serializable]
    public sealed class ConvaiVisionSamplingWindowSettings
    {
        // Backend contract limits (core-service VisionSamplingWindowConfig).
        internal const int MaxCount = 20;
        internal const int MinIntervalMs = 1;
        internal const int MaxIntervalMs = 60000;

        [SerializeField] [Min(0)] [Tooltip("Frames to pick from this horizon (1–20). 0 disables the window.")]
        private int _count;

        [SerializeField] [Min(0)] [Tooltip("Spacing between frames for this horizon, in ms (1–60000). 0 disables the window.")]
        private int _intervalMs;

        // 0 is kept as a "disable this window" sentinel on BOTH fields; the backend itself
        // requires 1–20 / 1–60000. A half-configured window (count set, interval left at the
        // default 0) must be dropped, never clamped up to a 1 ms horizon — the backend samples at
        // the fastest window's interval, so a stray 1 ms would request maximal capture load.
        /// <summary>Frames to pick from this horizon (clamped to 0–20; 0 disables the window).</summary>
        public int Count => Mathf.Clamp(_count, 0, MaxCount);

        /// <summary>Spacing between frames for this horizon in milliseconds (clamped to 1–60000).</summary>
        public int IntervalMs => Mathf.Clamp(_intervalMs, MinIntervalMs, MaxIntervalMs);

        /// <summary>True when both fields are configured; disabled/half-configured windows are never sent.</summary>
        public bool IsConfigured => _count > 0 && _intervalMs > 0;

        /// <summary>Creates a window picking <paramref name="count" /> frames spaced <paramref name="intervalMs" /> apart.</summary>
        public static ConvaiVisionSamplingWindowSettings Create(int count, int intervalMs) =>
            new() { _count = count, _intervalMs = intervalMs };
    }

    /// <summary>
    ///     Backend frame-sampling configuration for dynamic context vision, sent as
    ///     <c>vision_input_config</c> on room connect. All accessors clamp into the backend's
    ///     validated ranges so the inspector can never produce a rejected connect request.
    /// </summary>
    [Serializable]
    public sealed class ConvaiVisionInputSettings
    {
        // Backend contract limits (core-service VisionInputConfig). Values are clamped to these so the
        // inspector can never emit a config the backend rejects (422) at /connect.
        private const float MinSampleIntervalSecs = 0.1f;
        private const float MaxSampleIntervalSecs = 60f;
        private const int MinFramesPerTurn = 1;
        private const int MaxFramesPerTurn = 20;
        private const int MaxBufferFrames = 120;
        private const float MinStalenessSecs = 0.1f;
        private const float MaxStalenessSecs = 120f;
        private const int MinResolutionPx = 64;
        private const int MaxResolutionPx = 2048;

        [SerializeField] [Min(0.1f)] [Tooltip("How often the backend grabs a frame into its buffer, in seconds (0.1–60).")]
        private float _sampleIntervalSeconds = 1f;

        // Defaults deliberately mirror the backend's own defaults (frames_per_turn=5, single
        // horizon, backend-sized buffer). Attached frames cost image tokens on every LLM turn,
        // so anything richer (e.g. dual-horizon windows) is an explicit per-project opt-in.
        [SerializeField] [Min(1)] [Tooltip("How many buffered frames the backend attaches to the model on a turn (1–20).")]
        private int _framesPerTurn = 5;

        [SerializeField] [Min(0)] [Tooltip("Max frames in the backend's rolling buffer. 0 = backend default (= Frames Per Turn). Must be ≥ Frames Per Turn; max 120.")]
        private int _bufferFrames;

        [SerializeField] [Tooltip("Optional non-uniform frame selection (e.g. dense recent + sparse older). Total count across windows must be ≤ Frames Per Turn. Empty = uniform sampling at Sample Interval.")]
        private List<ConvaiVisionSamplingWindowSettings> _samplingWindows = new();

        [SerializeField] [Min(0.1f)] [Tooltip("Frames older than this are treated as stale/unavailable, in seconds (0.1–120).")]
        private float _stalenessSeconds = 10f;

        [SerializeField] [Min(0)] [Tooltip("Downscale cap (longest side) before the vision model. 0 = backend default; otherwise 64–2048.")]
        private int _maxResolution;

        [SerializeField] [Tooltip("When true, each vision attach replaces prior frames in context instead of accumulating.")]
        private bool _replacePreviousVisionContext = true;

        /// <summary>How often the backend samples a frame into its buffer, in seconds (clamped to 0.1–60).</summary>
        public float SampleIntervalSeconds => Mathf.Clamp(_sampleIntervalSeconds, MinSampleIntervalSecs, MaxSampleIntervalSecs);

        /// <summary>How many buffered frames the backend attaches to the model per turn (clamped to 1–20).</summary>
        public int FramesPerTurn => Mathf.Clamp(_framesPerTurn, MinFramesPerTurn, MaxFramesPerTurn);

        // 0 means "unset" → omit so the backend defaults to Frames Per Turn. When set, the backend
        // requires buffer ≥ frames_per_turn and ≤ 120, so clamp into that range.
        /// <summary>Rolling buffer size, or null to use the backend default (= <see cref="FramesPerTurn" />).</summary>
        public int? BufferFrames => _bufferFrames > 0 ? Mathf.Clamp(_bufferFrames, FramesPerTurn, MaxBufferFrames) : null;

        /// <summary>Optional non-uniform sampling horizons; empty means uniform sampling at <see cref="SampleIntervalSeconds" />.</summary>
        public IReadOnlyList<ConvaiVisionSamplingWindowSettings> SamplingWindows => _samplingWindows;

        /// <summary>Frames older than this are skipped at attach time, in seconds (clamped to 0.1–120).</summary>
        public float StalenessSeconds => Mathf.Clamp(_stalenessSeconds, MinStalenessSecs, MaxStalenessSecs);

        // 0 means "unset" → omit so the provider-aware default applies; otherwise the backend requires 64–2048.
        /// <summary>Downscale cap (longest side, px), or null for the provider-aware backend default (Gemini 384, others 768).</summary>
        public int? MaxResolution => _maxResolution > 0 ? Mathf.Clamp(_maxResolution, MinResolutionPx, MaxResolutionPx) : null;

        /// <summary>When true, each attach replaces the previous vision frames in context instead of accumulating.</summary>
        public bool ReplacePreviousVisionContext => _replacePreviousVisionContext;

        /// <summary>Creates settings with the backend's own defaults (5 frames per turn, single horizon).</summary>
        public static ConvaiVisionInputSettings CreateDefault() => new();

        /// <summary>Deep-copies these settings, including the sampling windows list.</summary>
        public ConvaiVisionInputSettings Clone()
        {
            var clone = new ConvaiVisionInputSettings
            {
                _sampleIntervalSeconds = _sampleIntervalSeconds,
                _framesPerTurn = _framesPerTurn,
                _bufferFrames = _bufferFrames,
                _stalenessSeconds = _stalenessSeconds,
                _maxResolution = _maxResolution,
                _replacePreviousVisionContext = _replacePreviousVisionContext,
                _samplingWindows = _samplingWindows == null
                    ? new List<ConvaiVisionSamplingWindowSettings>()
                    : new List<ConvaiVisionSamplingWindowSettings>(_samplingWindows)
            };
            return clone;
        }

        /// <summary>
        ///     Converts to the wire DTO sent on connect, trimming sampling windows into the
        ///     frames-per-turn budget so the backend never rejects the request.
        /// </summary>
        public RoomVisionInputConfig ToRoomVisionInputConfig()
        {
            int framesPerTurn = FramesPerTurn;
            var config = new RoomVisionInputConfig
            {
                Enabled = true,
                SampleIntervalSecs = SampleIntervalSeconds,
                FramesPerTurn = framesPerTurn,
                BufferFrames = BufferFrames,
                StalenessSeconds = StalenessSeconds,
                MaxResolution = MaxResolution,
                ReplacePreviousVisionContext = ReplacePreviousVisionContext
            };

            if (_samplingWindows is { Count: > 0 })
            {
                // The backend requires the total frame count across windows to be <= frames_per_turn.
                // Fill greedily in order (favoring the earliest / most-recent horizons) so we can never
                // emit a config the backend rejects, and warn once if anything had to be trimmed.
                int budget = framesPerTurn;
                bool trimmed = false;
                var windows = new List<RoomVisionSamplingWindow>();

                foreach (ConvaiVisionSamplingWindowSettings window in _samplingWindows)
                {
                    if (window == null || !window.IsConfigured)
                        continue;

                    if (budget <= 0)
                    {
                        trimmed = true;
                        break;
                    }

                    int count = Mathf.Min(window.Count, budget);
                    if (count < window.Count)
                        trimmed = true;

                    windows.Add(new RoomVisionSamplingWindow { Count = count, IntervalMs = window.IntervalMs });
                    budget -= count;
                }

                config.SamplingWindows = windows.Count > 0 ? windows : null;

                if (trimmed)
                    ConvaiLogger.Warning(
                        $"Total sampling-window frame count exceeded Frames Per Turn ({framesPerTurn}); " +
                        "trimmed to fit. Lower the window counts or raise Frames Per Turn to silence this.",
                        LogCategory.Vision);
            }

            return config;
        }
    }

    /// <summary>
    ///     Per-lane respond-mode defaults sent as <c>respond_modes</c> on room connect. Only the
    ///     overridable lanes are exposed: user text and voice always respond and cannot be lowered.
    /// </summary>
    [Serializable]
    public sealed class ConvaiVisionRespondModeSettings
    {
        [SerializeField] [Tooltip("How newly arriving frames affect speech. Usually Silent.")]
        private ConvaiRespondMode _vision = ConvaiRespondMode.Silent;

        [SerializeField] [Tooltip("How a dynamic-context text update affects speech. Usually Auto.")]
        private ConvaiRespondMode _contextUpdate = ConvaiRespondMode.Auto;

        [SerializeField] [Tooltip("Default behavior for an explicit vision trigger. Usually MustRespond.")]
        private ConvaiRespondMode _trigger = ConvaiRespondMode.MustRespond;

        [SerializeField] [Tooltip("How scene-metadata updates affect speech. Usually Silent.")]
        private ConvaiRespondMode _sceneMetadata = ConvaiRespondMode.Silent;

        /// <summary>How newly sampled vision frames affect speech (default Silent: absorb, never speak).</summary>
        public ConvaiRespondMode Vision => _vision;

        /// <summary>How dynamic-context text updates affect speech (default Auto: model decides when idle).</summary>
        public ConvaiRespondMode ContextUpdate => _contextUpdate;

        /// <summary>Default mode for explicit vision triggers when the request doesn't set one (default MustRespond).</summary>
        public ConvaiRespondMode Trigger => _trigger;

        /// <summary>How scene-metadata updates affect speech (default Silent).</summary>
        public ConvaiRespondMode SceneMetadata => _sceneMetadata;

        /// <summary>Creates settings with the backend's default lane policy.</summary>
        public static ConvaiVisionRespondModeSettings CreateDefault() => new();

        /// <summary>Copies these settings.</summary>
        public ConvaiVisionRespondModeSettings Clone() =>
            new()
            {
                _vision = _vision,
                _contextUpdate = _contextUpdate,
                _trigger = _trigger,
                _sceneMetadata = _sceneMetadata
            };

        /// <summary>Converts to the wire DTO sent on connect. User text/audio lanes are never included.</summary>
        public RoomRespondModesConfig ToRoomRespondModesConfig() =>
            new()
            {
                Vision = _vision.ToWireString(),
                ContextUpdate = _contextUpdate.ToWireString(),
                Trigger = _trigger.ToWireString(),
                SceneMetadata = _sceneMetadata.ToWireString()
            };
    }
}
