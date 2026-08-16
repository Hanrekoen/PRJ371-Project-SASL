using System.Collections.Generic;
using Convai.Modules.Emotion.Components;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Emotion.Core
{
    /// <summary>
    ///     Scene-wide registry of characters that publish themselves as emotional-contagion
    ///     witnesses/sources for other Convai characters, so one character can pick up the mood of
    ///     another standing near it. Entries register from <see cref="ConvaiEmotionController" /> on enable and
    ///     unregister on disable — no scene scans, no <c>Find*</c>, so it scales with participant
    ///     count and costs nothing when the feature is unused (the default, disabled state).
    /// </summary>
    /// <remarks>
    ///     Mirrors <c>Convai.Modules.Gaze.Providers.ConvaiCharacterGazeRegistry</c>'s shape and
    ///     lifecycle conventions so the two registries read as one family. Every emotion-bearing
    ///     character registers automatically — opting IN to actually reacting to witnessed
    ///     emotions is a per-receiving-character profile setting (<c>Contagion Enabled</c>),
    ///     never a registry-level gate.
    /// </remarks>
    internal static class EmotionContagionRegistry
    {
        /// <summary>One participating character in the contagion registry.</summary>
        internal sealed class Entry
        {
            /// <summary>The character's embodiment context (emotion-state source + identity).</summary>
            public EmbodimentContext Context;

            /// <summary>Character root — used for distance computation during a witness scan.</summary>
            public Transform Root;

            /// <summary>
            ///     The registering controller. Kept so a witness scan can exclude its own entry by
            ///     reference without depending on Transform/GameObject identity.
            /// </summary>
            public ConvaiEmotionController Controller;
        }

        private static readonly List<Entry> Entries = new(8);

        /// <summary>Registered characters (witnesses poll this, skipping their own entry).</summary>
        internal static IReadOnlyList<Entry> All => Entries;

        internal static void Register(Entry entry)
        {
            if (entry == null || Entries.Contains(entry)) return;
            Entries.Add(entry);
        }

        internal static void Unregister(Entry entry)
        {
            if (entry != null) Entries.Remove(entry);
        }

        /// <summary>Test/reset seam — empties the registry.</summary>
        internal static void Clear() => Entries.Clear();
    }
}
