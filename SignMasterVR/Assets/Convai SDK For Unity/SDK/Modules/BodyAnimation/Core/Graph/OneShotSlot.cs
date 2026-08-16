using System;
using UnityEngine;
using UnityEngine.Playables;

namespace Convai.Modules.BodyAnimation.Core.Graph
{
    /// <summary>Repetition policy for a <see cref="OneShotSlot" /> main clip.</summary>
    internal enum OneShotLoop
    {
        Once = 0,
        Count = 1,
        Hold = 2
    }

    /// <summary>Everything a <see cref="OneShotSlot" /> needs to run one playback request.</summary>
    internal struct OneShotSpec
    {
        public AnimationClip Intro;   // optional
        public AnimationClip Main;    // required
        public AnimationClip Outro;   // optional
        public OneShotLoop Loop;
        public int LoopCount;         // used when Loop == Count
        public float Speed;
        public float ChainFadeSeconds; // fade between intro→main→outro segments

        public static OneShotSpec For(AnimationClip main) => new()
        {
            Main = main,
            Loop = OneShotLoop.Once,
            LoopCount = 1,
            Speed = 1f,
            ChainFadeSeconds = 0.2f
        };
    }

    /// <summary>
    ///     Montage-style playback slot: plays an optional intro, a main clip under a loop
    ///     policy (once / N times / hold until stopped), then an optional outro, crossfading
    ///     between segments. The owning layer drives its layer weight; the slot only manages
    ///     the clip chain and reports lifecycle.
    /// </summary>
    internal sealed class OneShotSlot
    {
        public enum SlotPhase
        {
            Idle = 0,
            Intro = 1,
            Main = 2,
            Outro = 3
        }

        private readonly CrossfadeMixer _mixer;
        private OneShotSpec _spec;
        private int _completedLoops;
        private float _previousMainPhase;
        private bool _stopRequested;

        /// <summary>Raised when the chain has fully finished (after outro / final loop).</summary>
        public event Action Completed;

        public SlotPhase Phase { get; private set; } = SlotPhase.Idle;
        public Playable Playable => _mixer.Playable;
        public string ActiveClipName => _mixer.CurrentClip != null ? _mixer.CurrentClip.name : "(none)";
        public float NormalizedTime => _mixer.CurrentNormalizedTime;
        public bool IsPlaying => Phase != SlotPhase.Idle;
        public int CompletedLoops => _completedLoops;

        public OneShotSlot(PlayableGraph graph, AnimationCurve blendCurve, Action<string> overflowReported = null)
        {
            _mixer = new CrossfadeMixer(graph, blendCurve) { OverflowReported = overflowReported };
        }

        /// <summary>Starts (or replaces) the chain. Entry fading is the caller's concern.</summary>
        public void Play(in OneShotSpec spec, float entryFadeSeconds)
        {
            if (spec.Main == null) return;

            _spec = spec;
            _completedLoops = 0;
            _previousMainPhase = 0f;
            _stopRequested = false;

            var settings = ClipPlaySettings.Default;
            settings.Speed = spec.Speed <= 0f ? 1f : spec.Speed;

            if (spec.Intro != null)
            {
                Phase = SlotPhase.Intro;
                _mixer.Play(spec.Intro, entryFadeSeconds, settings, restartIfSame: true);
            }
            else
            {
                Phase = SlotPhase.Main;
                _mixer.Play(spec.Main, entryFadeSeconds, settings, restartIfSame: true);
            }
        }

        /// <summary>
        ///     Requests a graceful finish: hold/looping mains stop looping and (if present)
        ///     the outro plays. Returns immediately; <see cref="Completed" /> fires when done.
        /// </summary>
        public void RequestStop()
        {
            if (Phase == SlotPhase.Idle) return;
            _stopRequested = true;
        }

        /// <summary>Hard-clears the slot (layer already faded out). No Completed event.</summary>
        public void Clear()
        {
            _mixer.Clear();
            Phase = SlotPhase.Idle;
            _stopRequested = false;
        }

        /// <summary>Freezes the current and any fading clip (rate 0) so the pose holds static —
        /// used by an immediate layer blend-out that must dissolve a frozen pose.</summary>
        public void Freeze() => _mixer.FreezeAll();

        public void Tick(float deltaTime)
        {
            _mixer.Tick(deltaTime);

            switch (Phase)
            {
                case SlotPhase.Intro:
                    TickIntro();
                    break;
                case SlotPhase.Main:
                    TickMain();
                    break;
                case SlotPhase.Outro:
                    TickOutro();
                    break;
            }
        }

        private void TickIntro()
        {
            if (!_mixer.IsCurrentClipFinished) return;

            Phase = SlotPhase.Main;
            var settings = ClipPlaySettings.Default;
            settings.Speed = _spec.Speed <= 0f ? 1f : _spec.Speed;
            _mixer.Play(_spec.Main, _spec.ChainFadeSeconds, settings, restartIfSame: true);
        }

        private void TickMain()
        {
            bool mainDone = false;

            if (_spec.Main.isLooping)
            {
                // Count cycle wraps on looping clips.
                float phase = _mixer.CurrentNormalizedTime;
                if (phase < _previousMainPhase)
                    _completedLoops++;
                _previousMainPhase = phase;

                mainDone = _spec.Loop switch
                {
                    OneShotLoop.Once => _completedLoops >= 1,
                    OneShotLoop.Count => _completedLoops >= Mathf.Max(1, _spec.LoopCount),
                    _ => false
                };
            }
            else
            {
                if (_mixer.IsCurrentClipFinished)
                {
                    _completedLoops++;
                    int wanted = _spec.Loop switch
                    {
                        OneShotLoop.Once => 1,
                        OneShotLoop.Count => Mathf.Max(1, _spec.LoopCount),
                        _ => int.MaxValue
                    };

                    if (_completedLoops < wanted || (_spec.Loop == OneShotLoop.Hold && !_stopRequested))
                    {
                        var settings = ClipPlaySettings.Default;
                        settings.Speed = _spec.Speed <= 0f ? 1f : _spec.Speed;
                        _mixer.Play(_spec.Main, _spec.ChainFadeSeconds, settings, restartIfSame: true);
                        return;
                    }

                    mainDone = true;
                }
            }

            if (_stopRequested)
                mainDone = true;

            if (mainDone)
                AdvancePastMain();
        }

        private void AdvancePastMain()
        {
            if (_spec.Outro != null)
            {
                Phase = SlotPhase.Outro;
                var settings = ClipPlaySettings.Default;
                settings.Speed = _spec.Speed <= 0f ? 1f : _spec.Speed;
                _mixer.Play(_spec.Outro, _spec.ChainFadeSeconds, settings, restartIfSame: true);
                return;
            }

            Finish();
        }

        private void TickOutro()
        {
            if (_mixer.IsCurrentClipFinished)
                Finish();
        }

        private void Finish()
        {
            Phase = SlotPhase.Idle;
            _stopRequested = false;
            Completed?.Invoke();
        }
    }
}
