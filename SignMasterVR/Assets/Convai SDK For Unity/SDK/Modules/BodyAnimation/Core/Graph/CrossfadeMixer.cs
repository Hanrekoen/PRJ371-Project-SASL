using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Convai.Modules.BodyAnimation.Core.Graph
{
    /// <summary>Playback settings for a clip started on a <see cref="CrossfadeMixer" />.</summary>
    internal struct ClipPlaySettings
    {
        public float Speed;
        public float StartNormalizedTime;
        public bool ApplyFootIK;

        public static ClipPlaySettings Default => new() { Speed = 1f };
    }

    /// <summary>
    ///     Interruption-safe crossfade engine on top of an <see cref="AnimationMixerPlayable" />.
    ///     One source is "current"; starting a new source captures the live weights of every
    ///     connected source and fades them out collectively while the incoming source fades
    ///     in, so the total pose weight stays exactly 1 no matter how often transitions are
    ///     interrupted mid-blend.
    /// </summary>
    /// <remarks>
    ///     Sources are either clips (playables created and owned here) or external playables
    ///     (e.g. a <see cref="Blend1D" /> movement blend) whose subgraph the caller owns.
    ///     External sources are never destroyed by the mixer and are reclaimed intact when
    ///     they are re-played while still fading out — the locomotion layer relies on this to
    ///     hop Move → Stop → Move without rebuilding the movement blend.
    /// </remarks>
    internal sealed class CrossfadeMixer
    {
        private sealed class Source
        {
            public Playable Playable;
            public AnimationClip Clip;      // null for external sources
            public int Port = -1;
            public bool Owned;              // destroy playable on release
            public float SnapshotWeight;    // weight captured at transition start
            public double Time;             // manually driven clock (owned clips only)
            public float Speed = 1f;        // rate applied to the manual clock

            // Not part of the logical clip state (never touched by ResetSource callers other
            // than the pool itself): guards against a pooled instance being pushed twice, which
            // would otherwise hand the same object to two live sources. Set true exactly once by
            // ReleaseSource, cleared exactly once by RentSource.
            public bool Released;
        }

        /// <summary>
        ///     Upper bound on pooled, released <see cref="Source" /> instances kept per mixer.
        ///     Cheap enough that a single character never needs more, and small enough that a
        ///     pathological burst of transitions cannot make the pool itself the leak.
        /// </summary>
        private const int MaxPooledSources = 8;

        /// <summary>
        ///     Upper bound on sources waiting to finish fading out. A caller that keeps
        ///     interrupting its own transitions (re-aimed <c>PointAt</c>, a <c>MoveTo</c> retarget
        ///     storm, rapid talk-variant switches) would otherwise never reach the normal
        ///     completion release and grow the mixer without bound.
        /// </summary>
        private const int MaxConcurrentFadingSources = 8;

        private readonly PlayableGraph _graph;
        private readonly AnimationCurve _curve;
        private readonly List<Source> _fading = new(4);
        private readonly Stack<int> _freePorts = new(4);
        private readonly Stack<Source> _sourcePool = new(MaxPooledSources);
        private AnimationMixerPlayable _mixer;
        private Source _current;
        private float _fadeElapsed;
        private float _fadeDuration;
        private float _incomingStartWeight;
        private bool _transitioning;
        private bool _overflowReported;

        /// <summary>
        ///     Optional diagnostic hook set by the owning layer. Invoked at most once per mixer
        ///     instance, the first time the fading-source cap is hit, so a pathological call
        ///     pattern is diagnosable instead of an invisible, slowly-growing graph.
        /// </summary>
        internal Action<string> OverflowReported;

        /// <summary>Raised when a fade fully completes (not when it is interrupted).</summary>
        public event Action TransitionCompleted;

        public Playable Playable => _mixer;
        public bool IsTransitioning => _transitioning;
        public AnimationClip CurrentClip => _current?.Clip;
        public bool HasCurrent => _current != null;

        /// <summary>Sum of all connected input weights. Invariant: ≈1 while posed (tests/diagnostics).</summary>
        public float TotalPoseWeight
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < _mixer.GetInputCount(); i++)
                {
                    if (_mixer.GetInput(i).IsValid())
                        sum += _mixer.GetInputWeight(i);
                }
                return sum;
            }
        }

        /// <summary>Live mixer weight of the current source (tests/diagnostics).</summary>
        public float CurrentSourceWeight =>
            _current != null ? _mixer.GetInputWeight(_current.Port) : 0f;

        public CrossfadeMixer(PlayableGraph graph, AnimationCurve blendCurve, int initialCapacity = 4)
        {
            _graph = graph;
            _curve = blendCurve ?? AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            _mixer = AnimationMixerPlayable.Create(graph, Mathf.Max(2, initialCapacity));
            for (int port = _mixer.GetInputCount() - 1; port >= 0; port--)
                _freePorts.Push(port);
        }

        /// <summary>Seconds the current clip has played (unwrapped). 0 for external sources.</summary>
        public float CurrentTime =>
            _current != null && _current.Clip != null ? (float)_current.Time : 0f;

        internal float CurrentSpeedForTests => _current?.Clip != null ? _current.Speed : 0f;

        /// <summary>Number of released <see cref="Source" /> instances currently pooled (guard tests).</summary>
        internal int PooledSourceCountForTests => _sourcePool.Count;

        /// <summary>
        ///     Normalized time of the current clip, wrapped for looping clips and clamped at 1
        ///     for one-shots. 0 when the current source is external or empty.
        /// </summary>
        public float CurrentNormalizedTime
        {
            get
            {
                if (_current?.Clip == null || _current.Clip.length <= 0f) return 0f;

                float normalized = CurrentTime / _current.Clip.length;
                return _current.Clip.isLooping ? Mathf.Repeat(normalized, 1f) : Mathf.Clamp01(normalized);
            }
        }

        /// <summary>True when the current source is a non-looping clip that has played out.</summary>
        public bool IsCurrentClipFinished =>
            _current?.Clip != null && !_current.Clip.isLooping &&
            CurrentTime >= _current.Clip.length - 1e-4f;

        /// <summary>Playback-rate control for the current clip (rate warping). No-op for external sources.</summary>
        public void SetCurrentSpeed(float speed)
        {
            if (_current?.Clip != null)
                _current.Speed = speed;
        }

        /// <summary>
        ///     Freezes the current clip AND every still-fading clip source (rate 0) so the
        ///     whole on-screen pose is held static — used for an immediate blend-out that must
        ///     dissolve a frozen pose even if it lands mid-crossfade. External sources are skipped.
        /// </summary>
        public void FreezeAll()
        {
            if (_current?.Clip != null)
                _current.Speed = 0f;
            for (int i = 0; i < _fading.Count; i++)
            {
                if (_fading[i]?.Clip != null)
                    _fading[i].Speed = 0f;
            }
        }

        /// <summary>
        ///     Crossfades to <paramref name="clip" /> over <paramref name="fadeSeconds" />.
        ///     Returns false (no-op) when the clip is already current and
        ///     <paramref name="restartIfSame" /> is false.
        /// </summary>
        public bool Play(
            AnimationClip clip,
            float fadeSeconds,
            in ClipPlaySettings settings,
            bool restartIfSame = false)
        {
            if (clip == null) return false;
            if (!restartIfSame && _current != null && _current.Clip == clip && !IsCurrentClipFinished)
                return false;

            // Clip clocks are driven manually from Tick so playback follows the embodiment
            // delta time exactly (and stays deterministic in EditMode tests).
            var clipPlayable = AnimationClipPlayable.Create(_graph, clip);
            clipPlayable.SetApplyFootIK(settings.ApplyFootIK);
            clipPlayable.SetSpeed(0d);

            double startTime = settings.StartNormalizedTime > 0f && clip.length > 0f
                ? settings.StartNormalizedTime * clip.length
                : 0d;
            clipPlayable.SetTime(startTime);

            Source source = RentSource();
            source.Playable = clipPlayable;
            source.Clip = clip;
            source.Owned = true;
            source.Time = startTime;
            source.Speed = settings.Speed <= 0f ? 1f : settings.Speed;

            BeginTransition(source, fadeSeconds);
            return true;
        }

        /// <summary>
        ///     Crossfades to an externally owned playable (e.g. a movement blend). If the
        ///     playable is still connected and fading out, it is reclaimed without a rebuild;
        ///     if it is already current, this is a no-op.
        /// </summary>
        public void PlayExternal(Playable external, float fadeSeconds)
        {
            if (_current != null && _current.Playable.Equals(external))
                return;

            Source reclaimed = null;
            for (int i = 0; i < _fading.Count; i++)
            {
                if (_fading[i].Playable.Equals(external))
                {
                    reclaimed = _fading[i];
                    _fading.RemoveAt(i);
                    break;
                }
            }

            Source incoming = reclaimed;
            if (incoming == null)
            {
                incoming = RentSource();
                incoming.Playable = external;
                incoming.Owned = false;
            }

            BeginTransition(incoming, fadeSeconds, alreadyConnected: reclaimed != null);
        }

        /// <summary>Advances clip clocks and fade envelopes. Call exactly once per frame.</summary>
        public void Tick(float deltaTime)
        {
            AdvanceClock(_current, deltaTime);
            for (int i = 0; i < _fading.Count; i++)
                AdvanceClock(_fading[i], deltaTime);

            if (!_transitioning) return;

            _fadeElapsed += deltaTime;
            float t = _fadeDuration <= 0f ? 1f : Mathf.Clamp01(_fadeElapsed / _fadeDuration);
            float k = Mathf.Clamp01(_curve.Evaluate(t));

            _mixer.SetInputWeight(_current.Port, Mathf.Lerp(_incomingStartWeight, 1f, k));
            for (int i = 0; i < _fading.Count; i++)
                _mixer.SetInputWeight(_fading[i].Port, _fading[i].SnapshotWeight * (1f - k));

            if (t < 1f) return;

            for (int i = 0; i < _fading.Count; i++)
                ReleaseSource(_fading[i]);
            _fading.Clear();

            _mixer.SetInputWeight(_current.Port, 1f);
            _transitioning = false;
            TransitionCompleted?.Invoke();
        }

        /// <summary>
        ///     Disconnects and destroys every owned source. External playables are only
        ///     disconnected; their owners destroy them with the graph.
        /// </summary>
        public void Clear()
        {
            if (_current != null)
            {
                ReleaseSource(_current);
                _current = null;
            }

            for (int i = 0; i < _fading.Count; i++)
                ReleaseSource(_fading[i]);
            _fading.Clear();
            _transitioning = false;
        }

        private void BeginTransition(Source incoming, float fadeSeconds, bool alreadyConnected = false)
        {
            // Release anything that has already faded to nothing and, failing that, cap the
            // number of sources still waiting to finish — before snapshotting weights, so a
            // just-released port is available for reuse this same call. Neither the incoming
            // source nor the current one are ever in _fading at this point (the current source
            // is only appended below), so both are structurally safe from pruning.
            PruneFadingSources();

            // Snapshot the live weight of everything currently contributing.
            if (_current != null)
            {
                _current.SnapshotWeight = _mixer.GetInputWeight(_current.Port);
                _fading.Add(_current);
            }

            for (int i = 0; i < _fading.Count; i++)
                _fading[i].SnapshotWeight = _mixer.GetInputWeight(_fading[i].Port);

            if (!alreadyConnected)
            {
                incoming.Port = AcquirePort();
                _graph.Connect(incoming.Playable, 0, _mixer, incoming.Port);
                _mixer.SetInputWeight(incoming.Port, 0f);
                _incomingStartWeight = 0f;
            }
            else
            {
                // Reclaimed while fading out: continue rising from its live weight, no pop.
                _incomingStartWeight = _mixer.GetInputWeight(incoming.Port);
            }

            _current = incoming;
            _fadeElapsed = 0f;
            _fadeDuration = Mathf.Max(0f, fadeSeconds);
            _transitioning = true;

            if (_fadeDuration <= 0f)
                Tick(0f); // completes instantly: releases outgoing, sets weight 1
        }

        private static void AdvanceClock(Source source, float deltaTime)
        {
            if (source?.Clip == null || !source.Playable.IsValid()) return;

            source.Time += deltaTime * source.Speed;
            source.Playable.SetTime(source.Time);
        }

        private int AcquirePort()
        {
            if (_freePorts.Count > 0)
                return _freePorts.Pop();

            int oldCount = _mixer.GetInputCount();
            _mixer.SetInputCount(oldCount + 2);
            _freePorts.Push(oldCount + 1);
            return oldCount;
        }

        /// <summary>
        ///     Drops decayed sources and, if still over the cap, the oldest surviving ones.
        ///     External (caller-owned) sources are never touched: <see cref="PlayExternal" />
        ///     reclaims a still-fading external source by identity, and the locomotion layer
        ///     relies on that to hop Move → Stop → Move without rebuilding its blend.
        /// </summary>
        private void PruneFadingSources()
        {
            for (int i = _fading.Count - 1; i >= 0; i--)
            {
                Source source = _fading[i];
                if (!source.Owned || source.Port < 0) continue;
                if (_mixer.GetInputWeight(source.Port) > 1e-3f) continue;

                _fading.RemoveAt(i);
                ReleaseSource(source);
            }

            if (_fading.Count < MaxConcurrentFadingSources) return;

            bool prunedForCap = false;
            int i2 = 0;
            while (_fading.Count >= MaxConcurrentFadingSources && i2 < _fading.Count)
            {
                Source source = _fading[i2];
                if (!source.Owned)
                {
                    i2++;
                    continue;
                }

                _fading.RemoveAt(i2);
                ReleaseSource(source);
                prunedForCap = true;
            }

            if (prunedForCap && !_overflowReported)
            {
                _overflowReported = true;
                OverflowReported?.Invoke(
                    $"exceeded {MaxConcurrentFadingSources} concurrent fading sources; " +
                    "the oldest were released early. This usually means something is replaying " +
                    "before its previous transition completes.");
            }
        }

        /// <summary>
        ///     Rents a pooled <see cref="Source" /> or allocates a fresh one when the pool is
        ///     empty. Returned instances are always in the clean, reset state.
        /// </summary>
        private Source RentSource()
        {
            if (_sourcePool.Count == 0)
                return new Source();

            Source source = _sourcePool.Pop();
            source.Released = false;
            return source;
        }

        /// <summary>
        ///     Disconnects and (if owned) destroys the source's playable, then resets and pools
        ///     it for reuse. Safe to call more than once on the same instance: <see cref="Source.Released" />
        ///     is set exactly once here, and every later call is a no-op, so a source can never
        ///     be pushed onto the pool twice and handed out to two live sources at once.
        /// </summary>
        private void ReleaseSource(Source source)
        {
            if (source.Released) return;

            if (source.Port >= 0)
            {
                // Disconnect first: Disconnect resets the port weight, so the zero must win.
                _graph.Disconnect(_mixer, source.Port);
                _mixer.SetInputWeight(source.Port, 0f);
                _freePorts.Push(source.Port);
            }

            if (source.Owned && source.Playable.IsValid())
                source.Playable.Destroy();

            source.Released = true;
            ResetSource(source);

            if (_sourcePool.Count < MaxPooledSources)
                _sourcePool.Push(source);
        }

        /// <summary>
        ///     Clears every field that could otherwise leak state into the next rental: a stale
        ///     <see cref="Source.Clip" /> would keep an <see cref="AnimationClip" /> alive, and a
        ///     stale <see cref="Source.Speed" /> would silently apply to whatever clip is played
        ///     next. Single place to update when a field is added to <see cref="Source" />.
        /// </summary>
        private static void ResetSource(Source source)
        {
            source.Playable = default;
            source.Clip = null;
            source.Port = -1;
            source.Owned = false;
            source.SnapshotWeight = 0f;
            source.Time = 0d;
            source.Speed = 1f;
        }
    }
}
