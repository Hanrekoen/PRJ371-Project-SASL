using Convai.Editor.Inspectors.Framework;
using Convai.Runtime;
using UnityEditor;
using UnityEngine;
using Chips = Convai.Editor.UI.ConvaiEditorChips;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai inspector for the <see cref="ConvaiSettings" /> asset — the project-wide defaults
    ///     every Convai scene starts from: which Convai service to talk to, how audio behaves, how much
    ///     the SDK logs, and which optional systems are on.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Most people should never open this asset. The Convai Settings window edits the same
    ///         values with validation, an API-key field that does not show the key, and a setup health
    ///         check — so the purpose strip here points there rather than pretending this is the
    ///         intended route.
    ///     </para>
    ///     <para>
    ///         This inspector deliberately shows exactly the fields the default one showed. Narrowing
    ///         it would strip a working escape hatch from anyone who scripts against the asset or is
    ///         debugging a project whose settings window will not open.
    ///     </para>
    /// </remarks>
    [CustomEditor(typeof(ConvaiSettings))]
    internal sealed class ConvaiSettingsEditor : ConvaiInspectorEditor
    {
        private const string OpenSettingsButton = "Open Convai Settings";

        protected override string Title => "Convai Settings";

        protected override string Subtitle => "Project-wide defaults";

        protected override string Purpose =>
            "The defaults every Convai scene in this project starts from. The Convai Settings window " +
            "edits the same values with validation and a safer credentials field — prefer it over " +
            "editing this asset by hand.";

        protected override string EditorStateHostId => "ConvaiSettingsEditor";

        /// <summary>
        ///     Reports whether the project has credentials at all, which is the one thing about this
        ///     asset that can stop every Convai scene from working.
        /// </summary>
        /// <remarks>
        ///     Reads only whether the obfuscated field is empty. It never decodes, prints or logs the
        ///     key — a credential that reaches the screen reaches screenshots and bug reports with it.
        /// </remarks>
        protected override GUIContent StatusChip =>
            HasApiKey ? Chips.Ready.Content : Chips.ActionNeeded.Content;

        protected override Color StatusChipTint =>
            HasApiKey ? Chips.Ready.Tint : Chips.ActionNeeded.Tint;

        private bool HasApiKey
        {
            get
            {
                SerializedProperty obfuscated = serializedObject.FindProperty("_apiKeyObfuscated");
                return obfuscated != null && !string.IsNullOrWhiteSpace(obfuscated.stringValue);
            }
        }

        protected override void DrawHeaderExtras()
        {
            if (HasApiKey)
                return;

            WarningBox(
                "No API key yet",
                "Convai characters cannot connect until this project has an API key. Add one in the " +
                "Convai Settings window, which stores it without ever showing it back.",
                OpenSettingsButton,
                OpenSettingsWindow);
        }

        /// <summary>
        ///     Opens the settings window on the next editor tick.
        /// </summary>
        /// <remarks>
        ///     Deferred because this runs from a button inside a layout scope, and anything that opens
        ///     a window or a dialog from there corrupts the surrounding layout for the rest of the
        ///     session.
        /// </remarks>
        private static void OpenSettingsWindow() =>
            EditorApplication.delayCall += () => SettingsService.OpenProjectSettings("Project/Convai SDK");
    }
}
