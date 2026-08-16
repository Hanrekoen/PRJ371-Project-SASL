using UnityEngine;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Small deterministic random stream used by realtime embodiment components so they
    ///     do not perturb UnityEngine.Random's global state.
    /// </summary>
    internal struct DeterministicEmbodimentRandom
    {
        private uint _state;

        public DeterministicEmbodimentRandom(uint seed)
        {
            _state = seed != 0u ? seed : 0x9E3779B9u;
        }

        public float Value => NextUnit();

        public float Range(float minInclusive, float maxInclusive)
        {
            if (maxInclusive <= minInclusive) return minInclusive;
            return Mathf.Lerp(minInclusive, maxInclusive, NextUnit());
        }

        /// <summary>
        ///     Deterministic <c>[0, 1)</c> draw from a single LCG step on <paramref name="seed" />.
        ///     Implements weighted random selection consistent with dialogue variant selection so weighted
        ///     picks stay bit-stable across refactors.
        /// </summary>
        public static float UnitDrawFromLcgSeed(uint seed)
        {
            unchecked
            {
                uint advanced = seed * 1664525u + 1013904223u;
                return Mathf.Clamp01((advanced & 0xFFFFFFu) / (float)0x1000000u);
            }
        }

        public uint NextUInt()
        {
            unchecked
            {
                _state = _state * 1664525u + 1013904223u;
                return _state;
            }
        }

        public static DeterministicEmbodimentRandom Create(Component component, uint salt = 0u) =>
            new(CreateSeed(component, salt));

        public static uint CreateSeed(Component component, uint salt = 0u)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = Mix(hash, salt);
                if (component != null)
                {
                    hash = Mix(hash, component.GetType().FullName);
                    Transform t = component.transform;
                    while (t != null)
                    {
                        hash = Mix(hash, t.name);
                        hash = Mix(hash, (uint)t.GetSiblingIndex());
                        t = t.parent;
                    }
                }
                return hash != 0u ? hash : 0x9E3779B9u;
            }
        }

        public static int StableStringHash(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            unchecked
            {
                uint hash = 2166136261u;
                hash = Mix(hash, value);
                return (int)hash;
            }
        }

        private float NextUnit()
        {
            unchecked
            {
                return (NextUInt() & 0xFFFFFFu) / (float)0x1000000u;
            }
        }

        private static uint Mix(uint hash, uint value)
        {
            unchecked
            {
                hash ^= value;
                return hash * 16777619u;
            }
        }

        private static uint Mix(uint hash, string value)
        {
            if (string.IsNullOrEmpty(value)) return Mix(hash, 0u);
            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                    hash = Mix(hash, value[i]);
                return hash;
            }
        }
    }
}
