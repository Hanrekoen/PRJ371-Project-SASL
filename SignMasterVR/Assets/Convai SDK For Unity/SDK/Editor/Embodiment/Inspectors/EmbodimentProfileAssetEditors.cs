using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Editor.Inspectors;
using Convai.Modules.Embodiment.Presets;
using Convai.Modules.ConversationFlow.Profiles;
using Convai.Modules.BodyAnimation.Data;
using Convai.Modules.BodyAnimation.Editor;
using Convai.Modules.Emotion.Editor;
using Convai.Modules.Emotion.Profiles;
using Convai.Modules.Emotion.Taxonomy;
using UnityEditor;
using UnityEngine;
using Convai.Editor.UI;

namespace Convai.Editor.Embodiment.Inspectors
{
    [CustomEditor(typeof(ConvaiEmbodimentPresetLibrary))]
    internal sealed class EmbodimentPresetLibraryInspector : ConvaiEmbodimentProfileEditorBase<ConvaiEmbodimentPresetLibrary>
    {
        internal const string SectionPresets = "Presets";
        internal const string SectionDiagnostics = "Diagnostics";

        protected override string HeaderTitle => "Embodiment Preset Library";
        protected override string HeaderSubtitle => "Character preset catalog";
        protected override string HeaderStatus => Profile.HasDuplicatePresetIds(out _) ? "Warning" : "Ready";
        protected override Color HeaderStatusColor => Profile.HasDuplicatePresetIds(out _)
            ? ConvaiEditorTheme.StatusWarn
            : ConvaiEditorTheme.AccentBright;

        protected override void DrawProfileInspector()
        {
            if (DrawSection(SectionPresets, "Presets", ConvaiEditorGlyphs.Content))
                DrawSectionBody(() => DrawProperty("presets"));

            if (DrawSection(SectionDiagnostics, "Diagnostics", ConvaiEditorGlyphs.Validation,
                    accent: ConvaiEditorTheme.StatusWarn))
            {
                DrawSectionBody(() =>
                {
                    int count = Profile.Presets?.Count ?? 0;
                    EditorGUILayout.LabelField("Preset Count", count.ToString());
                    if (count == 0)
                        WarningBox("Empty Library", "Add at least one Character Embodiment Preset.");
                    else if (Profile.HasDuplicatePresetIds(out string message))
                        WarningBox("Duplicate Preset IDs", message);
                    else
                        InfoBox("Library Ready", "Preset IDs are unique.");
                });
            }
        }
    }

    [CustomEditor(typeof(ConvaiConversationFlowProfile))]
    internal sealed class ConversationFlowProfileInspector : ConvaiEmbodimentProfileEditorBase<ConvaiConversationFlowProfile>
    {
        internal const string SectionTransition = "Transition";
        internal const string SectionDialogueBeats = "DialogueBeats";
        internal const string SectionEnergy = "Energy";

        protected override string HeaderTitle => "Conversation Flow Profile";
        protected override string HeaderSubtitle => "Dialogue state timing";

        protected override void DrawProfileInspector()
        {
            if (DrawSection(SectionTransition, "Transition", ConvaiEditorGlyphs.Motion))
                DrawSectionBody(() => DrawProperty("transitionDuration"));

            if (DrawSection(SectionDialogueBeats, "Dialogue Beats", ConvaiEditorGlyphs.Content))
            {
                DrawSectionBody(() =>
                {
                    DrawProperty("thinkingMinHold");
                    DrawProperty("thinkingMaxHold");
                    DrawProperty("attendingGracePeriod");
                    DrawProperty("settlingDuration");
                    DrawProperty("idleReturnDelay");
                    DrawProperty("interruptedFreezeDuration");
                });
            }

            if (DrawSection(SectionEnergy, "Energy", ConvaiEditorGlyphs.Range))
                DrawSectionBody(() => DrawProperty("speakingBaseEnergy"));
        }
    }

    /// <summary>
    ///     Inspector for <see cref="ConvaiEmotionProfile" />. Renders the shared section table from
    ///     <see cref="EmotionConfigSections" />, so every one of the asset's settings has a named,
    ///     plain-worded home — and so this inspector and the Emotion editor window can never drift
    ///     apart.
    /// </summary>
    /// <remarks>
    ///     What this replaces: thirteen sections, eleven of them expanded on open, titled with the
    ///     module's internal vocabulary — Response Shaping, Blending &amp; Hysteresis, Per-Emotion
    ///     Dynamics — over field labels Unity derived straight from identifiers like
    ///     <c>lerpSpeed</c> and <c>complementBlendScale</c>.
    /// </remarks>
    [CustomEditor(typeof(ConvaiEmotionProfile))]
    internal sealed class EmotionProfileInspector : ConvaiEmbodimentProfileEditorBase<ConvaiEmotionProfile>
    {
        // Unknown-label findings, recomputed only when the asset changes rather than on every
        // idle repaint.
        private readonly List<EmotionProfileValidation.Finding> _labelFindings = new();
        private bool _labelFindingsDirty = true;

        protected override string HeaderTitle => "Emotion Personality";
        protected override string HeaderSubtitle => "How this character feels and shows it";
        protected override string HeaderStatus => "Ready";

        protected override void OnEnable()
        {
            base.OnEnable();
            _labelFindingsDirty = true;
            Undo.undoRedoPerformed += MarkLabelFindingsDirty;
        }

        protected override void OnDisable()
        {
            Undo.undoRedoPerformed -= MarkLabelFindingsDirty;
            base.OnDisable();
        }

        private void MarkLabelFindingsDirty() => _labelFindingsDirty = true;

        protected override void DrawProfileInspector()
        {
            if (_labelFindingsDirty)
            {
                EmotionProfileValidation.Validate(Profile, _labelFindings);
                _labelFindingsDirty = false;
            }

            InfoBox(
                "One personality, many characters",
                "This asset holds a character's emotional temperament only. It names no blendshapes, " +
                "so the same personality works on every supported face rig and can be shared by any " +
                "number of characters that should feel alike.");

            EmotionPersonality.DrawArchetypeRow(Profile);
            EditorGUILayout.Space(6f);

            EmotionConfigDrawer.DrawAllSections(serializedObject, DrawConfigSection);
            CloseSectionCard();

            DrawUnknownLabelWarnings();

            // An edit this frame invalidates the cached findings for the next repaint — the
            // just-applied values are not visible on Profile until ApplyModifiedProperties runs
            // right after this method returns.
            if (GUI.changed || serializedObject.hasModifiedProperties) _labelFindingsDirty = true;
        }

        /// <summary>Draws one section of the emotion config table in this inspector's Convai editor frame.</summary>
        private void DrawConfigSection(EmotionConfigSection section, Action drawFields)
        {
            if (DrawSection(section.Id, section.Title, EmotionConfigDrawer.GlyphFor(section.Id),
                    section.ExpandedByDefault))
                DrawSectionBody(drawFields);
        }


        /// <summary>
        ///     One warning box listing every unknown emotion label the asset carries, with a
        ///     did-you-mean suggestion when one is close enough.
        /// </summary>
        private void DrawUnknownLabelWarnings()
        {
            const int maxListed = 4;
            if (_labelFindings.Count == 0) return;

            var body = new System.Text.StringBuilder();
            for (int i = 0; i < _labelFindings.Count && i < maxListed; i++)
            {
                EmotionProfileValidation.Finding finding = _labelFindings[i];
                if (body.Length > 0) body.Append(' ');
                body.Append(string.IsNullOrEmpty(finding.Suggestion)
                    ? $"This character's vocabulary has no emotion called '{finding.Label}'."
                    : $"This character's vocabulary has no emotion called '{finding.Label}' — did you mean '{finding.Suggestion}'?");
            }

            if (_labelFindings.Count > maxListed)
                body.Append($" And {_labelFindings.Count - maxListed} more.");

            WarningBox("Unknown emotions", body.ToString());
        }
    }

    [CustomEditor(typeof(EmotionTaxonomyAsset))]
    internal sealed class EmotionTaxonomyAssetInspector : ConvaiEmbodimentProfileEditorBase<EmotionTaxonomyAsset>
    {
        internal const string SectionEntries = "Entries";
        internal const string SectionDiagnostics = "Diagnostics";

        protected override string HeaderTitle => "Emotion Taxonomy";
        protected override string HeaderSubtitle => "Emotion vocabulary";

        protected override void DrawProfileInspector()
        {
            if (DrawSection(SectionEntries, "Entries", ConvaiEditorGlyphs.Content))
                DrawSectionBody(() => DrawProperty("entries"));

            if (DrawSection(SectionDiagnostics, "Diagnostics", ConvaiEditorGlyphs.Validation,
                    accent: ConvaiEditorTheme.StatusWarn))
            {
                DrawSectionBody(() =>
                {
                    int entryCount = ArraySize("entries");
                    EditorGUILayout.LabelField("Authored Entries", entryCount.ToString());
                    if (entryCount == 0)
                        WarningBox("Empty Taxonomy", "Runtime will synthesize neutral only if no valid entries exist.");
                    else
                        InfoBox("Taxonomy Authored", "Runtime canonicalizes labels and aliases case-insensitively.");
                });
            }
        }
    }

    [CustomEditor(typeof(ConvaiBodyAnimationProfile))]
    internal sealed class BodyAnimationProfileInspector : ConvaiEmbodimentProfileEditorBase<ConvaiBodyAnimationProfile>
    {
        internal const string SectionContent = "Content";
        internal const string SectionRuntime = "Runtime";
        internal const string SectionDiagnostics = "Diagnostics";

        protected override string HeaderTitle => "Body Animation Profile";
        protected override string HeaderSubtitle => "Animation content routing";
        protected override string HeaderStatus => HasMissingCoreReferences() ? "Warning" : "Ready";
        protected override Color HeaderStatusColor => HasMissingCoreReferences()
            ? ConvaiEditorTheme.StatusWarn
            : ConvaiEditorTheme.AccentBright;

        protected override void DrawProfileInspector()
        {
            if (DrawSection(SectionContent, "Content", ConvaiEditorGlyphs.Content))
                DrawSectionBody(() => DrawProperties(Find("_animationSet")));

            if (DrawSection(SectionRuntime, "Runtime", ConvaiEditorGlyphs.Animator))
                DrawSectionBody(() => DrawProperties(Find("_config"),
                    Find("_autoCreateConversationFlow")));

            if (DrawSection(SectionDiagnostics, "Diagnostics", ConvaiEditorGlyphs.Validation,
                    accent: ConvaiEditorTheme.StatusWarn))
            {
                DrawSectionBody(() =>
                {
                    if (Profile.AnimationSet == null)
                        WarningBox("Animation Set Missing", "Assign a Body Animation Set before using this profile in a preset.");
                    if (Profile.Config == null)
                        WarningBox("Config Missing", "Assign a config or controller defaults will be used.");
                    if (!HasMissingCoreReferences())
                        InfoBox("Profile Ready", "Animation set and config are assigned.");
                });
            }
        }

        private bool HasMissingCoreReferences() =>
            Profile.AnimationSet == null || Profile.Config == null;
    }

    /// <summary>
    ///     Inspector for <see cref="ConvaiBodyAnimationConfig" />. Renders the shared section table
    ///     from <see cref="BodyAnimationConfigSections" />, so every one of the asset's ~90 settings
    ///     has a named, plain-worded home — and so this inspector and the Body Animation editor
    ///     window can never drift apart.
    /// </summary>
    /// <remarks>
    ///     What this replaces: five hand-curated sections covering about a third of the fields, with
    ///     the remainder behind an "All Runtime Fields" raw property iterator. Everything that makes
    ///     a character feel like a person lived in that dump.
    /// </remarks>
    [CustomEditor(typeof(ConvaiBodyAnimationConfig))]
    internal sealed class BodyAnimationConfigInspector : ConvaiEmbodimentProfileEditorBase<ConvaiBodyAnimationConfig>
    {
        protected override string HeaderTitle => "Body Animation Config";
        protected override string HeaderSubtitle => "How this character behaves";
        protected override string HeaderStatus => "Ready";

        private static GUIStyle SummaryStyle => ConvaiEditorStyles.CaptionWrapped;

        protected override void DrawProfileInspector()
        {
            InfoBox(
                "One config, many characters",
                "This asset shapes behaviour only — timings, gait choices, and feature toggles. " +
                "Animation content lives in the Body Animation Set, so one config can be shared " +
                "across every character that should act alike.");

            for (int i = 0; i < BodyAnimationConfigSections.Sections.Length; i++)
                DrawConfigSection(BodyAnimationConfigSections.Sections[i]);
        }

        private void DrawConfigSection(BodyAnimationConfigSection section)
        {
            if (!DrawSection(section.Id, section.Title, IconFor(section.Id), section.ExpandedByDefault))
                return;

            DrawSectionBody(() =>
            {
                if (!string.IsNullOrEmpty(section.Summary))
                    EditorGUILayout.LabelField(section.Summary, SummaryStyle);

                if (section.Id == BodyAnimationConfigSections.Personality)
                {
                    var config = (ConvaiBodyAnimationConfig)target;

                    // No character is selected here, so there is nothing to make a private copy
                    // for — but the reason the controls are unavailable still has to be given,
                    // or a config opened straight from the Project window reads as broken.
                    BodyAnimationPersonality.DrawOwnershipNotice(
                        BodyAnimationPersonality.OfCachedFor(config), config, null);
                    BodyAnimationPersonality.DrawArchetypeRow(config);
                    EditorGUILayout.Space(4f);
                }

                // A gated section's values only apply while its master toggle is on; disabling them
                // says so far more clearly than a sentence the user has to read.
                string gateField = BodyAnimationConfigSections.GateFieldFor(section.Id);
                bool gateOpen = gateField == null || (Find(gateField)?.boolValue ?? true);

                for (int i = 0; i < section.Fields.Length; i++)
                {
                    string fieldName = section.Fields[i];
                    SerializedProperty property = Find(fieldName);
                    if (property == null) continue;

                    bool isGate = fieldName == gateField;
                    using (new EditorGUI.DisabledScope(!gateOpen && !isGate))
                    {
                        EditorGUILayout.PropertyField(property, BodyAnimationConfigLabels.For(property), true);
                    }
                }
            });
        }

        private static string IconFor(string sectionId) => sectionId switch
        {
            BodyAnimationConfigSections.Personality => ConvaiEditorGlyphs.Profile,
            BodyAnimationConfigSections.Talking => ConvaiEditorGlyphs.Content,
            BodyAnimationConfigSections.TalkingWhileWalking => ConvaiEditorGlyphs.Motion,
            BodyAnimationConfigSections.ListeningThinking => ConvaiEditorGlyphs.Discovery,
            BodyAnimationConfigSections.Reacting => ConvaiEditorGlyphs.Reaction,
            BodyAnimationConfigSections.Presence => ConvaiEditorGlyphs.Range,
            BodyAnimationConfigSections.Walking => ConvaiEditorGlyphs.Motion,
            BodyAnimationConfigSections.Transitions => ConvaiEditorGlyphs.Routing,
            BodyAnimationConfigSections.AdvancedCoSpeech => ConvaiEditorGlyphs.Contract,
            BodyAnimationConfigSections.Integration => ConvaiEditorGlyphs.Contract,
            _ => ConvaiEditorGlyphs.Validation
        };
    }
}
