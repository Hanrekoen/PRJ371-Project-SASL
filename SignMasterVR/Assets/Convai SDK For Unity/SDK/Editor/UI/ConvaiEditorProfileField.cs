using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Editor.UI
{
    /// <summary>
    ///     The one way a Convai component shows which settings asset it is running on, and lets the
    ///     user change it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Five profile-bearing components had five answers. Gaze and Body Animation offered a
    ///         create-a-new-one button and no field at all, so assigning a shipped profile meant the
    ///         Debug inspector or hand-editing the scene — which is how the LipSync sample ended up
    ///         with two dangling references. Emotion had the field, filed under a collapsed
    ///         <c>Advanced</c> section while its name was appended to a header the user could not
    ///         act on. Body Language and Conversation Flow had a plain field and said nothing about
    ///         its state.
    ///     </para>
    ///     <para>
    ///         The rule this encodes: <b>the settings asset is the first row of the section that
    ///         tunes it, drawn whether or not one is assigned</b> — so seeing it, swapping it and
    ///         clearing it are the same gesture — and the section header carries
    ///         <see cref="Summarize" /> as its right-aligned summary, so a collapsed section still
    ///         reports which asset the character is on.
    ///     </para>
    ///     <para>
    ///         Deliberately thin. The value is that one file owns the empty-state wording and the row
    ///         shape; anything more would start deciding for modules whose assets genuinely differ.
    ///     </para>
    /// </remarks>
    internal static class ConvaiEditorProfileField
    {
        /// <summary>
        ///     What a section header says when the character has no settings asset of its own.
        /// </summary>
        /// <remarks>
        ///     Not "None" and not an empty summary. Both read as a fault, and neither is one: a
        ///     character with no profile runs on the SDK's built-in defaults, which work.
        /// </remarks>
        internal const string BuiltInDefaultsSummary = "SDK defaults";

        /// <summary>
        ///     What the personality row and the section header say when the settings no longer match
        ///     a named type. Short enough for a pill; the sentence that explains it is
        ///     <see cref="CustomCaption" />.
        /// </summary>
        internal const string CustomLabel = "Custom";

        /// <summary>
        ///     What to do once the type pills have gone quiet. Reassures first — tuning away from a
        ///     named type is not a fault — then names the one next step.
        /// </summary>
        internal const string CustomCaption =
            "These settings no longer match a named type, which is fine. Click one to apply it.";

        /// <summary>
        ///     The section-header summary for a settings asset: its name, or
        ///     <see cref="BuiltInDefaultsSummary" /> when there is none.
        /// </summary>
        /// <param name="asset">
        ///     The asset the character actually resolves at runtime — not necessarily the one in the
        ///     component's own field, since an Embodiment Preset can deliver it. A header that named
        ///     the field rather than the resolution would report "SDK defaults" for a character
        ///     visibly running someone else's personality.
        /// </param>
        /// <param name="customized">
        ///     True when the asset's values no longer match a named character type. The name stays;
        ///     <see cref="CustomLabel" /> is appended so a collapsed section never looks empty.
        ///     Ignored when there is no asset — "SDK defaults (Custom)" would invent a state the
        ///     character is not in.
        /// </param>
        internal static string Summarize(Object asset, bool customized = false)
        {
            if (asset == null) return BuiltInDefaultsSummary;
            return customized ? asset.name + " (" + CustomLabel + ")" : asset.name;
        }

        /// <summary>
        ///     Draws the settings-asset row that opens a personality or profile section.
        /// </summary>
        /// <remarks>
        ///     Always drawn, including when the slot is empty: an empty object field is how a user
        ///     assigns one, and hiding it behind a "create a new one" button is what made a shipped
        ///     profile unreachable from the Inspector.
        /// </remarks>
        internal static void Draw(SerializedProperty property, GUIContent label)
        {
            if (property == null) return;

            EditorGUILayout.PropertyField(property, label);
            EditorGUILayout.Space(4f);
        }
    }
}
