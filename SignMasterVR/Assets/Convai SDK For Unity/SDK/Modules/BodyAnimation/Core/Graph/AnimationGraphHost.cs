using System;
using Convai.Modules.BodyAnimation.Core.Diagnostics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Convai.Modules.BodyAnimation.Core.Graph
{
    /// <summary>
    ///     Owns the <see cref="PlayableGraph" /> lifetime for one character: creation, output
    ///     binding to the <see cref="Animator" />, play/stop, and leak-safe teardown. Every
    ///     other graph primitive (mixers, slots, blends) creates its playables inside this
    ///     host's graph and is destroyed with it.
    /// </summary>
    internal sealed class AnimationGraphHost : IDisposable
    {
        private readonly AnimTrace _trace;
        private AnimationPlayableOutput _output;
        private Playable _root;
        private AnimationMixerPlayable _rootHandoffMixer;
        private Playable _retiringRoot;
        private float _rootHandoffElapsed;
        private float _rootHandoffDuration;

        public PlayableGraph Graph { get; private set; }

        public bool IsValid => Graph.IsValid();
        public bool IsRootHandoffActive => _rootHandoffMixer.IsValid();
        public int PlayableCount => Graph.IsValid() ? Graph.GetPlayableCount() : 0;

        public AnimationGraphHost(Animator animator, string ownerName, AnimTrace trace)
        {
            if (animator == null) throw new ArgumentNullException(nameof(animator));
            _trace = trace;

            Graph = PlayableGraph.Create($"ConvaiBodyAnimation.{ownerName}");
            Graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _output = AnimationPlayableOutput.Create(Graph, "BodyAnimationOutput", animator);

            _trace?.Detail($"PlayableGraph created for animator '{animator.name}'.");
        }

        /// <summary>Binds the final pose source (normally the layer mixer) to the output.</summary>
        public void SetRoot(Playable root)
        {
            _root = root;
            _output.SetSourcePlayable(root);
        }

        public void BeginRootHandoff(Playable newRoot, float duration)
        {
            if (!_root.IsValid())
            {
                SetRoot(newRoot);
                return;
            }

            CompleteRootHandoff();
            _retiringRoot = _root;
            _root = newRoot;
            _rootHandoffMixer = AnimationMixerPlayable.Create(Graph, 2);
            Graph.Connect(_retiringRoot, 0, _rootHandoffMixer, 0);
            Graph.Connect(newRoot, 0, _rootHandoffMixer, 1);
            _rootHandoffMixer.SetInputWeight(0, 1f);
            _rootHandoffMixer.SetInputWeight(1, 0f);
            _rootHandoffElapsed = 0f;
            _rootHandoffDuration = Mathf.Max(0.01f, duration);
            _output.SetSourcePlayable(_rootHandoffMixer);
        }

        public bool TickRootHandoff(float deltaTime)
        {
            if (!_rootHandoffMixer.IsValid()) return false;
            _rootHandoffElapsed += Mathf.Max(0f, deltaTime);
            float t = Mathf.Clamp01(_rootHandoffElapsed / _rootHandoffDuration);
            float eased = t * t * (3f - 2f * t);
            _rootHandoffMixer.SetInputWeight(0, 1f - eased);
            _rootHandoffMixer.SetInputWeight(1, eased);
            if (t < 1f) return false;
            CompleteRootHandoff();
            return true;
        }

        public Playable TakeRetiringRoot()
        {
            Playable retiring = _retiringRoot;
            _retiringRoot = Playable.Null;
            return retiring;
        }

        public void DestroyRetiredSubgraph(Playable retiringRoot)
        {
            if (retiringRoot.IsValid() && Graph.IsValid())
                Graph.DestroySubgraph(retiringRoot);
        }

        private void CompleteRootHandoff()
        {
            if (!_rootHandoffMixer.IsValid()) return;
            _output.SetSourcePlayable(_root);
            if (_rootHandoffMixer.GetInputCount() > 0) _rootHandoffMixer.DisconnectInput(0);
            if (_rootHandoffMixer.GetInputCount() > 1) _rootHandoffMixer.DisconnectInput(1);
            Graph.DestroyPlayable(_rootHandoffMixer);
            _rootHandoffMixer = default;
        }

        public void Play()
        {
            if (!Graph.IsValid() || Graph.IsPlaying()) return;
            Graph.Play();
            _trace?.Detail("PlayableGraph playing.");
        }

        public void Stop()
        {
            if (!Graph.IsValid() || !Graph.IsPlaying()) return;
            Graph.Stop();
        }

        /// <summary>
        ///     Evaluates the graph immediately so the skeleton wears the current pose this
        ///     very frame — without it the animator renders the avatar's default pose (open
        ///     jaw on CC rigs) until the first scheduled tick: a visible blink at Play.
        /// </summary>
        public void Evaluate()
        {
            if (Graph.IsValid())
                Graph.Evaluate(0f);
        }

        /// <summary>
        ///     Destroys the graph and every playable created in it. Safe to call repeatedly
        ///     and from <c>OnDisable</c>/<c>OnDestroy</c> during teardown or domain reload.
        /// </summary>
        public void Dispose()
        {
            if (!Graph.IsValid()) return;

            Graph.Destroy();
            _trace?.Detail("PlayableGraph destroyed.");
        }
    }
}
