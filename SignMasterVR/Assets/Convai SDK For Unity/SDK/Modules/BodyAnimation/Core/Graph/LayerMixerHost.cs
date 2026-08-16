using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Convai.Modules.BodyAnimation.Core.Graph
{
    /// <summary>
    ///     Wraps the root <see cref="AnimationLayerMixerPlayable" />: layer registration with
    ///     optional avatar masks and additive mode, plus weight reads/writes. Layer index 0 is
    ///     the full-body base; higher indices override it inside their mask.
    /// </summary>
    internal sealed class LayerMixerHost
    {
        private readonly PlayableGraph _graph;
        private AnimationLayerMixerPlayable _mixer;
        private readonly AvatarMask[] _masks;
        private readonly bool[] _additive;

        public Playable Mixer => _mixer;
        public int LayerCount { get; }

        public LayerMixerHost(PlayableGraph graph, int layerCount)
        {
            if (layerCount < 1) throw new ArgumentOutOfRangeException(nameof(layerCount));

            _graph = graph;
            LayerCount = layerCount;
            _masks = new AvatarMask[layerCount];
            _additive = new bool[layerCount];
            _mixer = AnimationLayerMixerPlayable.Create(graph, layerCount);
        }

        /// <summary>
        ///     Connects a layer's pose source to the given port. The layer starts at weight 0
        ///     (the base layer is expected to raise itself to 1 on its first tick).
        /// </summary>
        public void ConnectLayer(int port, Playable source, AvatarMask mask = null, bool additive = false)
        {
            if (port < 0 || port >= LayerCount)
                throw new ArgumentOutOfRangeException(nameof(port));

            _graph.Connect(source, 0, _mixer, port);
            _mixer.SetInputWeight(port, 0f);
            _mixer.SetLayerAdditive((uint)port, additive);
            _additive[port] = additive;
            if (mask != null)
            {
                _mixer.SetLayerMaskFromAvatarMask((uint)port, mask);
                _masks[port] = mask;
            }
        }

        public void SetLayerWeight(int port, float weight) =>
            _mixer.SetInputWeight(port, Mathf.Clamp01(weight));

        public float GetLayerWeight(int port) => _mixer.GetInputWeight(port);
        public AvatarMask GetLayerMask(int port) => _masks[port];
        public bool IsLayerAdditive(int port) => _additive[port];

        /// <summary>Re-masks a layer at runtime (e.g. talk coverage upper-body ↔ full-body).</summary>
        public void SetLayerMask(int port, AvatarMask mask)
        {
            if (mask != null)
            {
                _mixer.SetLayerMaskFromAvatarMask((uint)port, mask);
                _masks[port] = mask;
            }
        }

        /// <summary>
        ///     Switches a layer between override and additive blending at runtime (e.g. an
        ///     additive talk gesture entry). Additive inputs must be delta poses (clips with
        ///     an additive reference pose baked at import).
        /// </summary>
        public void SetLayerAdditive(int port, bool additive)
        {
            _mixer.SetLayerAdditive((uint)port, additive);
            _additive[port] = additive;
        }
    }
}
