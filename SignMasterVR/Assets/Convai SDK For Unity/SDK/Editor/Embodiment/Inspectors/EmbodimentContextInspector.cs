using System.Collections.Generic;
using Convai.Editor.Embodiment.Setup;
using Convai.Editor.Inspectors.Framework;
using Convai.Editor.UI;
using Convai.Runtime.Embodiment;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Embodiment.Inspectors
{
    /// <summary>
    ///     Inspector for <see cref="EmbodimentContext" />: the map of what this character can do.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This component used to be stamped <c>HideInInspector</c> outside Play Mode, so this
    ///         editor could never draw — dead editor code guarding an invisible component, on an
    ///         object that had been silently serialized into both shipped sample scenes. The component
    ///         is visible now, and this is what it says: which features are present, which are
    ///         missing and what each missing one costs, and what the rig resolved to.
    ///     </para>
    ///     <para>
    ///         Read-only by design. Nothing here is a setting; every value is either resolved from
    ///         the character or owned by a feature's own component.
    ///     </para>
    /// </remarks>
    [CustomEditor(typeof(EmbodimentContext))]
    internal sealed class EmbodimentContextInspector : ConvaiInspectorEditor
    {
        private const string SectionFeatures = "Features";
        private const string SectionRig = "Rig";
        private const string SectionAdvanced = "Advanced";

        protected override string Title => "Character Features";

        protected override string Subtitle => "What this character can do";

        protected override GUIContent StatusChip => CurrentChip.Content;

        protected override Color StatusChipTint => CurrentChip.Tint;

        private static ConvaiEditorChip CurrentChip =>
            EditorApplication.isPlaying ? ConvaiEditorChips.Live : ConvaiEditorChips.Ready;

        protected override void DrawBody()
        {
            var context = (EmbodimentContext)target;

            InfoBox(
                "What this is",
                "Convai adds this to a character to connect its features to each other. There is nothing " +
                "to configure here — it is the map, not a setting. Each feature is configured on its " +
                "own component.");

            DrawFeaturesSection(context);
            DrawRigSection(context);
            DrawAdvancedSection();
        }

        // ── features ────────────────────────────────────────────────────────────────

        private void DrawFeaturesSection(EmbodimentContext context)
        {
            if (!DrawSection(SectionFeatures, "Features", ConvaiEditorGlyphs.Routing)) return;

            DrawSectionBody(() =>
            {
                IReadOnlyList<EmbodimentModuleDescriptor> all = EmbodimentModuleCatalog.Modules;
                if (all.Count == 0)
                {
                    WarningBox("No Features Found",
                        "No Convai character features are present in this project.");
                    return;
                }

                GameObject root = context.gameObject;
                int presentCount = 0;

                for (int i = 0; i < all.Count; i++)
                {
                    EmbodimentModuleDescriptor module = all[i];
                    Component component = root.GetComponentInChildren(module.ControllerType, true);
                    bool present = component != null;
                    if (present) presentCount++;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        // The shared glyph vocabulary rather than Unity's built-in test-runner icons:
                        // those are coloured bitmaps that ignore the row's tint and read as "a test
                        // passed", which is not what a missing optional module means.
                        GUILayout.Label(
                            present ? ConvaiEditorGlyphs.Status.Ok : ConvaiEditorGlyphs.Status.Neutral,
                            ConvaiEditorStyles.SectionIconTinted(
                                present ? ConvaiEditorTheme.StatusReady : ConvaiEditorTheme.TextMuted,
                                ConvaiEditorTokens.SectionIconFontSize),
                            GUILayout.Width(20f), GUILayout.Height(18f));

                        EditorGUILayout.LabelField(module.DisplayName, GUILayout.MinWidth(130f));

                        if (present)
                        {
                            using (new EditorGUI.DisabledScope(true))
                                EditorGUILayout.ObjectField(component, module.ControllerType, false);
                        }
                        else
                        {
                            EditorGUILayout.LabelField("not added", ConvaiEditorStyles.MicroLabel);
                            if (GUILayout.Button("Add", GUILayout.Width(48f)))
                                AddFeature(root, module);
                        }
                    }

                    // The module's own sentence, not a transformation of its description: building
                    // this line by lower-casing the description shipped text like "Without it: no
                    // where the character looks".
                    if (!present && !string.IsNullOrEmpty(module.Absence))
                    {
                        using (new EditorGUI.IndentLevelScope())
                            EditorGUILayout.LabelField(
                                $"Without it, {module.Absence}", ConvaiEditorStyles.CaptionWrapped);
                    }
                }

                GUILayout.Space(4f);
                EditorGUILayout.LabelField(
                    $"{presentCount} of {all.Count} features on this character.",
                    ConvaiEditorStyles.MicroLabel);
            });
        }

        private static void AddFeature(GameObject root, EmbodimentModuleDescriptor module)
        {
            Undo.AddComponent(root, module.ControllerType);
            EditorUtility.SetDirty(root);
        }

        // ── rig ─────────────────────────────────────────────────────────────────────

        private void DrawRigSection(EmbodimentContext context)
        {
            if (!DrawSection(SectionRig, "Rig", ConvaiEditorGlyphs.Validation)) return;

            DrawSectionBody(() =>
            {
                var binding = context.GetComponentInChildren<Convai.Runtime.Animation.StandardRigBinding>(true);
                if (binding == null)
                {
                    InfoBox(
                        "Rig not resolved yet",
                        "Convai works out this character's bones and face meshes when it starts. Add the " +
                        "Character Rig component if you want to see or correct the result before then.");
                    return;
                }

                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("Rig", binding, typeof(Object), true);

                EditorGUILayout.LabelField("Detected Convention", binding.DetectedConvention.ToString());
                EditorGUILayout.LabelField("Confidence", $"{binding.DetectionConfidence:P0}");
                EditorGUILayout.LabelField("Face Meshes",
                    (binding.FacialMeshes?.Count ?? 0).ToString());
            });
        }

        // ── advanced ────────────────────────────────────────────────────────────────

        private void DrawAdvancedSection()
        {
            if (!DrawSection(SectionAdvanced, "Advanced", ConvaiEditorGlyphs.Profile, defaultExpanded: false))
                return;

            DrawSectionBody(() =>
            {
                SerializedProperty facialOverride =
                    serializedObject.FindProperty("_facialCompositionProfileOverride");
                if (facialOverride != null)
                {
                    EditorGUILayout.PropertyField(facialOverride,
                        new GUIContent("Face Blending Override",
                            "Leave empty unless you need to change how expression, lip sync and eye " +
                            "movement blend on the face."));
                }
            });
        }
    }
}
