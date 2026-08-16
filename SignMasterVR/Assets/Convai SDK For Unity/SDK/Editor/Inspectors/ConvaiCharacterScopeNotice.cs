using Convai.Editor.UI;
using Convai.Runtime.Components;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     The one notice a character-scoped Convai component shows when it is not on a Convai
    ///     character: what will happen, and the two ways to fix it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         There is exactly one of these because the notice has to keep matching the runtime it
    ///         describes. A misplaced character-scoped component is <em>disabled</em> — the
    ///         composition root refuses to grow a context on a non-character object — and this is the
    ///         one message the user sees at the moment they make that mistake, so it must say that
    ///         and not something the runtime no longer does.
    ///     </para>
    ///     <para>
    ///         It lives in <c>Convai.Editor</c> rather than beside the embodiment inspectors because
    ///         the module inspectors are spread across several editor assemblies and every one of
    ///         them already references this one. It is a static helper rather than a base class, so
    ///         that embodiment vocabulary stays off the shared inspector base.
    ///     </para>
    /// </remarks>
    internal static class ConvaiCharacterScopeNotice
    {
        /// <summary>
        ///     Whether <paramref name="component" /> sits on a Convai character, or under one.
        /// </summary>
        /// <remarks>
        ///     Matches the runtime's own resolution rule
        ///     (<c>GetComponentInParent&lt;ConvaiCharacter&gt;(true)</c>), so the inspector and Play
        ///     Mode can never disagree about whether a component is placed correctly.
        /// </remarks>
        internal static bool IsOnConvaiCharacter(Component component) =>
            component != null && component.GetComponentInParent<ConvaiCharacter>(true) != null;

        /// <summary>
        ///     Draws the misplacement notice, or nothing when the component is placed correctly.
        /// </summary>
        /// <remarks>
        ///     Silent on the healthy path on purpose: a correctly configured scene must never carry a
        ///     permanent complaint. Drawn as an error rather than a warning because the consequence
        ///     is total — the component disables itself and does nothing at all.
        /// </remarks>
        /// <param name="component">The inspected component.</param>
        /// <param name="featureName">
        ///     What the user calls this feature — "Body Language", not the class name.
        /// </param>
        internal static void DrawIfMisplaced(Component component, string featureName)
        {
            if (component == null || IsOnConvaiCharacter(component)) return;

            string feature = string.IsNullOrWhiteSpace(featureName) ? "This component" : featureName;
            GameObject owner = component.gameObject;

            ConvaiEditorFrame.ErrorBox(
                "Not On A Convai Character",
                $"{feature} has nothing to drive here, so it switches itself off. Move it onto the " +
                "object that has the Convai Character component (or one of that object's children) — " +
                $"or add a Convai Character to '{owner.name}' if this is meant to be one.",
                "Add Convai Character",
                () => Undo.AddComponent<ConvaiCharacter>(owner));
        }
    }
}
