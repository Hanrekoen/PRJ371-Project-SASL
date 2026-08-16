using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Providers
{
    /// <summary>
    ///     Scene-wide registry of characters that publish themselves as gaze targets for
    ///     other Convai characters (character-to-character mutual gaze). Entries register
    ///     from <see cref="CharacterGazeTargetProvider" /> on enable and unregister on
    ///     disable — no scene scans, no <c>Find*</c>, so it scales with participant count and
    ///     costs nothing when unused.
    /// </summary>
    internal static class ConvaiCharacterGazeRegistry
    {
        /// <summary>One participating character in the mutual-gaze registry.</summary>
        internal sealed class Entry
        {
            /// <summary>The character's embodiment context (dialogue-state source + identity).</summary>
            public EmbodimentContext Context;

            /// <summary>Character root — used as the candidate identity and diagnostics name.</summary>
            public Transform Root;

            /// <summary>
            ///     Eye-line gaze point (head bone). Falls back to <see cref="Root" /> until the
            ///     rig binds; consumers re-resolve lazily because the first rig bind does not
            ///     raise <c>RigBindingChanged</c>.
            /// </summary>
            public Transform HeadAnchor;

            /// <summary>Stable display name for diagnostics.</summary>
            public string DisplayName;

            /// <summary>Vertical lift applied while the gaze point falls back to the root.</summary>
            public float EyeLineOffset = 1.6f;

            /// <summary>Whether the gaze point is still the root fallback (no head bone yet).</summary>
            public bool HeadIsFallback => HeadAnchor == null || HeadAnchor == Root;

            /// <summary>
            ///     Re-resolves the head bone from <paramref name="rigBinding" /> (or the
            ///     context's current binding). Downgrades to the root when the binding has no
            ///     head, so a rebind to a headless rig never leaves a stale anchor behind.
            /// </summary>
            public void RefreshHeadAnchor(IStandardRigBinding rigBinding = null)
            {
                HeadAnchor = Root;

                IStandardRigBinding rig = rigBinding ?? Context?.RigBinding;
                if (rig != null && rig.TryGetBone(StandardBone.Head, out Transform head) && head != null)
                    HeadAnchor = head;
            }

            /// <summary>
            ///     The world-space point other characters gaze at: the head bone when resolved,
            ///     otherwise the root lifted to the eye line. Returns <c>false</c> when the
            ///     entry has no usable transform at all.
            /// </summary>
            public bool TryGetGazePoint(out Vector3 point)
            {
                if (HeadAnchor != null && HeadAnchor != Root)
                {
                    point = HeadAnchor.position;
                    return true;
                }

                if (Root != null)
                {
                    point = Root.position + Vector3.up * Mathf.Max(0f, EyeLineOffset);
                    return true;
                }

                point = default;
                return false;
            }
        }

        private static readonly List<Entry> Entries = new(8);

        /// <summary>Registered characters (observers poll this, skipping their own entry).</summary>
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
