using System;

namespace Convai.Modules.LipSync
{
    internal sealed class CompiledBlendshapeMapping
    {
        public CompiledBlendshapeMapping(
            SourceRoute[] routes,
            float globalMultiplier,
            float globalOffset,
            int channelCount)
        {
            Routes = routes ?? Array.Empty<SourceRoute>();
            GlobalMultiplier = globalMultiplier;
            GlobalOffset = globalOffset;
            ChannelCount = Math.Max(0, channelCount);
        }

        public SourceRoute[] Routes { get; }
        public float GlobalMultiplier { get; }
        public float GlobalOffset { get; }
        public int ChannelCount { get; }

        internal readonly struct TargetBinding
        {
            public TargetBinding(int meshCacheIndex, int blendshapeIndex)
            {
                MeshCacheIndex = meshCacheIndex;
                BlendshapeIndex = blendshapeIndex;
            }

            public int MeshCacheIndex { get; }
            public int BlendshapeIndex { get; }
        }

        internal readonly struct SourceRoute
        {
            public SourceRoute(
                int sourceIndex,
                bool enabled,
                bool useOverrideValue,
                float overrideValue,
                bool ignoreGlobalModifiers,
                float multiplier,
                float offset,
                float curveExponent,
                float clampMinValue,
                float clampMaxValue,
                TargetBinding[] targets)
            {
                SourceIndex = sourceIndex;
                Enabled = enabled;
                UseOverrideValue = useOverrideValue;
                OverrideValue = overrideValue;
                IgnoreGlobalModifiers = ignoreGlobalModifiers;
                Multiplier = multiplier;
                Offset = offset;
                CurveExponent = curveExponent;
                ClampMinValue = clampMinValue;
                ClampMaxValue = clampMaxValue;
                Targets = targets;
            }

            public int SourceIndex { get; }
            public bool Enabled { get; }
            public bool UseOverrideValue { get; }
            public float OverrideValue { get; }
            public bool IgnoreGlobalModifiers { get; }
            public float Multiplier { get; }
            public float Offset { get; }
            public float CurveExponent { get; }
            public float ClampMinValue { get; }
            public float ClampMaxValue { get; }
            public TargetBinding[] Targets { get; }
        }
    }
}
