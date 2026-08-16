using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Editor.Ownership;
using Convai.Editor.UI;
using Convai.Modules.BodyAnimation.Components;
using Convai.Modules.BodyAnimation.Data;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     What "Make Unique For This Character" actually did — which assets were written, or why
    ///     nothing was.
    /// </summary>
    internal readonly struct BodyAnimationMakeUniqueResult
    {
        private BodyAnimationMakeUniqueResult(
            ConvaiBodyAnimationConfig config, string configAssetPath, string profileAssetPath, string failureReason)
        {
            Config = config;
            ConfigAssetPath = configAssetPath;
            ProfileAssetPath = profileAssetPath;
            FailureReason = failureReason;
        }

        /// <summary>The character's own copy of the config, or <c>null</c> when nothing was written.</summary>
        internal ConvaiBodyAnimationConfig Config { get; }

        /// <summary>Project path of the new config asset; empty on failure.</summary>
        internal string ConfigAssetPath { get; }

        /// <summary>
        ///     Project path of the new profile asset, when the config arrived through one. Empty
        ///     when the config was assigned directly and no profile needed copying.
        /// </summary>
        internal string ProfileAssetPath { get; }

        /// <summary>Why nothing usable was produced, in the user's own terms; empty on success.</summary>
        internal string FailureReason { get; }

        internal static BodyAnimationMakeUniqueResult Succeeded(
            ConvaiBodyAnimationConfig config, string configAssetPath, string profileAssetPath) =>
            new(config, configAssetPath, profileAssetPath, string.Empty);

        internal static BodyAnimationMakeUniqueResult Failed(string reason) =>
            new(null, string.Empty, string.Empty, reason);
    }

    /// <summary>
    ///     One documented personality preset. Not a vibe — a named set of field values, so two
    ///     characters sharing one animation library can read as two different people.
    /// </summary>
    internal readonly struct BodyAnimationArchetype
    {
        public BodyAnimationArchetype(
            CharacterDemeanor demeanor, string description, float liveliness, float calmness,
            float ambientIntervalSeconds)
        {
            Demeanor = demeanor;
            Description = description;
            Liveliness = liveliness;
            Calmness = calmness;
            AmbientIntervalSeconds = ambientIntervalSeconds;
        }

        public CharacterDemeanor Demeanor { get; }

        /// <summary>
        ///     Read from <see cref="CharacterDemeanors" /> rather than spelled here: three modules
        ///     show this word and they are only guaranteed to show the same one if none of them
        ///     owns a copy of it.
        /// </summary>
        public string Name => CharacterDemeanors.DisplayName(Demeanor);

        public string Description { get; }
        public float Liveliness { get; }
        public float Calmness { get; }
        public float AmbientIntervalSeconds { get; }
    }

    /// <summary>
    ///     The personality controls shared by the Body Animation inspector and editor window: four
    ///     archetype presets, three plain-language sliders, and the shared-config warning that stops
    ///     them being a trap.
    /// </summary>
    /// <remarks>
    ///     Every write goes through <see cref="SerializedObject" /> on the config asset so it is
    ///     undoable and correctly dirties the asset. Field names are the config's serialized names;
    ///     they are private by design, which is why this lives in the module's editor assembly
    ///     rather than reaching through a public API that does not exist.
    /// </remarks>
    internal static class BodyAnimationPersonality
    {
        private const string LivelinessField = "_gestureLiveliness";
        private const string CalmnessField = "_calmness";
        private const string AmbientEnabledField = "_enableAmbientActivities";
        private const string AmbientIntervalField = "_ambientIntervalSeconds";

        internal static readonly BodyAnimationArchetype[] Archetypes =
        {
            new(CharacterDemeanor.Composed, "Receptionist, clerk, guide. Settles slowly, gestures sparingly.",
                0.7f, 1.4f, 30f),
            new(CharacterDemeanor.Warm, "The SDK default. Conversational and expressive without being busy.",
                1f, 1f, 20f),
            new(CharacterDemeanor.Energetic, "Tour guide, host, streamer. Fast, broad, restless.",
                1.5f, 0.6f, 12f),
            new(CharacterDemeanor.Reserved, "Guard, officiant, receptionist on a bad day. Nearly still.",
                0.4f, 1.6f, 45f)
        };

        // ------------------------------------------------------------------ archetypes

        /// <summary>Labels for <see cref="Archetypes" />, built once — the picker draws every repaint.</summary>
        private static GUIContent[] s_archetypeLabels;

        private static readonly GUIContent ArchetypeRowLabel = new("Archetype");

        /// <summary>Draws the archetype picker, applying one on click (confirmed first).</summary>
        /// <param name="owner">
        ///     The character these settings belong to. Present, the picker stays live and an
        ///     SDK-owned config is copied for this character on click. <c>null</c> only where there
        ///     is no character to copy for, and there the picker is correctly unavailable.
        /// </param>
        internal static void DrawArchetypeRow(
            ConvaiBodyAnimationConfig config, ConvaiBodyAnimationController owner = null)
        {
            if (config == null) return;

            if (s_archetypeLabels == null)
            {
                s_archetypeLabels = new GUIContent[Archetypes.Length];
                for (int i = 0; i < Archetypes.Length; i++)
                    s_archetypeLabels[i] = new GUIContent(Archetypes[i].Name, Archetypes[i].Description);
            }

            int selected = IndexOf(config);
            string explanation = selected >= 0
                ? Archetypes[selected].Description
                : ConvaiEditorProfileField.CustomCaption;

            int clicked;
            using (new EditorGUI.DisabledScope(!CanEdit(config, owner)))
            {
                clicked = ConvaiEditorControls.PresetPicker(
                    ArchetypeRowLabel, s_archetypeLabels, selected, explanation);
            }

            if (clicked < 0 || clicked == selected) return;

            ConfirmAndApply(config, owner, Archetypes[clicked]);
        }

        /// <summary>
        ///     Whether this config no longer matches any of the four named archetypes. False when
        ///     there is no asset — "custom" without an asset is a state the character is not in.
        /// </summary>
        internal static bool IsCustomized(ConvaiBodyAnimationConfig config) =>
            config != null && IndexOf(config) < 0;

        private static int IndexOf(ConvaiBodyAnimationConfig config)
        {
            if (config == null) return -1;
            for (int i = 0; i < Archetypes.Length; i++)
            {
                if (!Matches(config, Archetypes[i])) continue;
                return i;
            }

            return -1;
        }

        /// <summary>
        ///     Whether the user may drive these controls at all. False only when the SDK owns the
        ///     config and there is no character to make a copy for — everything else is either the
        ///     user's already or becomes theirs the moment they change it.
        /// </summary>
        private static bool CanEdit(ConvaiBodyAnimationConfig config, ConvaiBodyAnimationController owner) =>
            owner != null || ConvaiAssetOwnership.IsProjectAsset(config);

        /// <summary>
        ///     Confirms and applies after the current IMGUI pass. The confirmation is modal, and a
        ///     modal raised from inside a layout scope discards the layout state the enclosing scope is
        ///     about to close, leaving the surface throwing on every later repaint.
        /// </summary>
        private static void ConfirmAndApply(
            ConvaiBodyAnimationConfig config,
            ConvaiBodyAnimationController owner,
            BodyAnimationArchetype archetype)
        {
            EditorApplication.delayCall += () =>
            {
                if (config == null) return;

                bool confirmed = EditorUtility.DisplayDialog(
                    $"Apply the {archetype.Name} archetype?",
                    $"{archetype.Description}\n\nThis replaces this character's personality values.",
                    "Apply", "Cancel");

                if (!confirmed) return;

                ConvaiBodyAnimationConfig target = EnsureWritable(config, owner);
                if (target != null) Apply(target, archetype);
            };
        }

        /// <summary>Whether the config's current values already match this archetype.</summary>
        /// <remarks>
        ///     Compares exactly the fields <see cref="Apply" /> writes, so "what an archetype sets"
        ///     and "what counts as being that archetype" cannot disagree. They did: Apply wrote the
        ///     ambient interval and this compared only the two sliders, so a config whose ambient
        ///     interval an author had retimed still reported itself as untouched — the picker
        ///     highlighted an archetype the config was no longer on. Emotion's equivalent carries
        ///     the same rule for the same reason.
        /// </remarks>
        internal static bool Matches(ConvaiBodyAnimationConfig config, BodyAnimationArchetype archetype)
        {
            if (config == null) return false;
            var serialized = new SerializedObject(config);
            return Same(serialized, LivelinessField, archetype.Liveliness) &&
                   Same(serialized, CalmnessField, archetype.Calmness) &&
                   Same(serialized, AmbientIntervalField, archetype.AmbientIntervalSeconds);
        }

        /// <summary>
        ///     A serialized float against its authored source. A missing property compares unequal
        ///     rather than throwing: a renamed field must show as "no archetype", never as a match.
        /// </summary>
        private static bool Same(SerializedObject serialized, string field, float expected)
        {
            SerializedProperty property = serialized.FindProperty(field);
            return property != null && Mathf.Approximately(property.floatValue, expected);
        }

        /// <summary>Writes an archetype's values as one undoable step.</summary>
        internal static void Apply(ConvaiBodyAnimationConfig config, BodyAnimationArchetype archetype)
        {
            if (config == null) return;

            var serialized = new SerializedObject(config);
            SetFloat(serialized, LivelinessField, archetype.Liveliness);
            SetFloat(serialized, CalmnessField, archetype.Calmness);
            SetFloat(serialized, AmbientIntervalField, archetype.AmbientIntervalSeconds);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(config);
        }

        private static void SetFloat(SerializedObject serialized, string field, float value)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property != null) property.floatValue = value;
        }

        // ------------------------------------------------------------------ sliders

        /// <summary>
        ///     The three controls a non-technical user can reason about, worded as behaviour rather
        ///     than as the fields they drive.
        /// </summary>
        /// <remarks>
        ///     Written the ordinary way - read a property, write a property - inside a
        ///     <see cref="ConvaiOwnedEdit" />. The scope is what makes a character on the SDK's
        ///     shipped settings quietly get its own copy on the first change, so this code does not
        ///     have to know that any of that is happening.
        /// </remarks>
        /// <param name="set">
        ///     The animation set this character resolves, when known. "Keeps busy when alone" plays
        ///     authored Ambient-tagged clips, so a set carrying none makes the checkbox a switch
        ///     with nothing behind it - passing the set lets it be shown as unavailable, with the
        ///     reason, instead of silently doing nothing when ticked.
        /// </param>
        /// <param name="owner">The character these settings belong to.</param>
        internal static void DrawSliders(
            ConvaiBodyAnimationConfig config,
            ConvaiBodyAnimationSet set = null,
            ConvaiBodyAnimationController owner = null)
        {
            if (config == null) return;

            using var edit = ConvaiOwnedEdit.Begin(config, owner, Copier);
            using var readOnly = new EditorGUI.DisabledScope(!edit.CanEdit);

            SerializedObject serialized = edit.Serialized;
            SerializedProperty liveliness = serialized.FindProperty(LivelinessField);
            SerializedProperty calmness = serialized.FindProperty(CalmnessField);
            SerializedProperty ambientEnabled = serialized.FindProperty(AmbientEnabledField);
            SerializedProperty ambientInterval = serialized.FindProperty(AmbientIntervalField);

            if (liveliness != null)
            {
                liveliness.floatValue = EditorGUILayout.Slider(
                    new GUIContent("How expressive",
                        "How large and how frequent the character's talking gestures are. " +
                        "1 is the SDK default; 0 is still, 2 is maximally lively."),
                    liveliness.floatValue, 0f, 2f);
            }

            if (calmness != null)
            {
                calmness.floatValue = EditorGUILayout.Slider(
                    new GUIContent("How calm",
                        "How long the character holds a pose and how gently it settles between them. " +
                        "Higher reads as more composed and deliberate."),
                    calmness.floatValue, 0f, 2f);
            }

            if (ambientEnabled == null) return;

            bool hasAmbientContent = set == null || HasAmbientContent(set);

            using (new EditorGUI.DisabledScope(!hasAmbientContent))
            {
                bool next = EditorGUILayout.ToggleLeft(
                    new GUIContent("Keeps busy when alone",
                        "Performs small activities on its own when nobody has spoken to it for a " +
                        "while, instead of standing motionless. Plays actions tagged Ambient in " +
                        "the animation set."),
                    ambientEnabled.boolValue && hasAmbientContent);

                // Only ever written while the control is live. Showing it unticked because this set
                // has no ambient content must not WRITE that back - the config can be shared by
                // other characters whose sets do have the content, and merely selecting this
                // character would silently turn the feature off for all of them.
                if (hasAmbientContent) ambientEnabled.boolValue = next;
            }

            if (!hasAmbientContent)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    GUILayout.Label(
                        "No action in this animation set is tagged Ambient, so there is nothing for " +
                        "the character to do. Tag one in the Content tab to enable this.",
                        ConvaiEditorTheme.CaptionWrapped);
                }
            }
            else if (ambientEnabled.boolValue && ambientInterval != null)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    ambientInterval.floatValue = EditorGUILayout.Slider(
                        new GUIContent("How often", "Average seconds between idle activities."),
                        ambientInterval.floatValue, 5f, 120f);
                }
            }
        }

        /// <summary>Whether the set authors at least one playable action tagged Ambient.</summary>
        private static bool HasAmbientContent(ConvaiBodyAnimationSet set)
        {
            IReadOnlyList<ActionEntry> actions = set.Actions;
            for (int i = 0; i < actions.Count; i++)
                if (actions[i] != null && actions[i].IsValid && actions[i].Ambient) return true;
            return false;
        }

        // ------------------------------------------------------------------ ownership

        /// <summary>
        ///     The whole personality block for a character: the ownership notice, the archetype row,
        ///     and the three sliders.
        /// </summary>
        /// <remarks>
        ///     Drawn by one method rather than three calls per surface, so the component inspector
        ///     and the editor window's Feel mode cannot describe the same config differently — or, as
        ///     happened before, one of them forgetting to say anything at all.
        ///     <para>
        ///         There is no notice at all for a character on the SDK's shipped settings, which is
        ///         the common case on a brand-new character. Nothing is wrong there and nothing is
        ///         required of the user: the controls work, and the first change makes the settings
        ///         theirs. Only a config genuinely <i>shared</i> with other characters in the open
        ///         scenes is worth a word before the fact, because that is the one case where a
        ///         slider changes something the user cannot see.
        ///     </para>
        /// </remarks>
        internal static void DrawPersonalitySection(
            ConvaiBodyAnimationConfig config,
            ConvaiBodyAnimationSet set,
            ConvaiBodyAnimationController owner)
        {
            if (config == null) return;

            DrawOwnershipNotice(OfCachedFor(config), config, owner);

            DrawArchetypeRow(config, owner);
            EditorGUILayout.Space(4f);
            DrawSliders(config, set, owner);

            ConvaiCopyReceipts.Draw(owner);
        }

        /// <summary>
        ///     Says who owns this config, through the one notice every Convai module draws.
        /// </summary>
        /// <remarks>
        ///     Silent for an SDK-owned config whenever a character is selected: copy-on-write has
        ///     already made that a non-event, and a warning about something the SDK handles for you
        ///     is noise that teaches people to stop reading warnings. The read-only explanation
        ///     survives only where there is no character to act for.
        /// </remarks>
        internal static void DrawOwnershipNotice(
            ConvaiAssetOwnership ownership,
            ConvaiBodyAnimationConfig config,
            ConvaiBodyAnimationController owner)
        {
            if (config == null) return;
            if (owner != null && ownership.Kind == ConvaiAssetOwnershipKind.SdkOwned) return;

            ConvaiOwnershipNotice.Draw(
                ownership, owner != null ? () => MakeUnique(config, owner) : null);
        }

        // ------------------------------------------------------------------ copy-on-write

        /// <summary>
        ///     How Body Animation gives a character its own config.
        /// </summary>
        /// <remarks>
        ///     The one module that cannot use <see cref="ConvaiFieldSettingsCopier" />: its config
        ///     usually arrives through a profile, and copying the config alone would leave the profile
        ///     still supplying the shared one, so the character would keep reading it and the copy
        ///     would be dead weight. <see cref="TryMakeUnique" /> handles both arrangements.
        /// </remarks>
        private sealed class ConfigCopier : IConvaiSettingsCopier
        {
            public string SettingsNoun => "animation settings";

            public ConvaiCopyOnWriteResult CopyForOwner(Object asset, Component owner)
            {
                if (asset is not ConvaiBodyAnimationConfig config ||
                    owner is not ConvaiBodyAnimationController controller)
                    return ConvaiCopyOnWriteResult.Failed("There are no animation settings here to copy.");

                return TryMakeUnique(config, controller, out BodyAnimationMakeUniqueResult made)
                    ? ConvaiCopyOnWriteResult.Made(made.Config, made.ConfigAssetPath)
                    : ConvaiCopyOnWriteResult.Failed(made.FailureReason);
            }
        }

        internal static readonly IConvaiSettingsCopier Copier = new ConfigCopier();

        /// <summary>
        ///     The config a command must write to, giving this character its own copy of the SDK's
        ///     shipped settings first if that is what it is looking at.
        /// </summary>
        /// <remarks>
        ///     The imperative counterpart of <see cref="ConvaiOwnedEdit" />, for writes that do not
        ///     happen inside a draw pass - applying an archetype, which is confirmed in a modal and
        ///     therefore lands a frame later. Same copier, so both routes produce the same copy.
        /// </remarks>
        internal static ConvaiBodyAnimationConfig EnsureWritable(
            ConvaiBodyAnimationConfig config, ConvaiBodyAnimationController owner)
        {
            ConvaiCopyOnWriteResult result = ConvaiCopyOnWrite.EnsureWritable(
                config, owner, () => Copier.CopyForOwner(config, owner));

            if (!result.Succeeded)
            {
                if (!string.IsNullOrEmpty(result.FailureReason))
                    EditorUtility.DisplayDialog("Body Animation", result.FailureReason, "OK");
                return null;
            }

            if (result.Copied) ConvaiCopyReceipts.Record(owner, result.AssetPath, result.Target);
            return result.Target as ConvaiBodyAnimationConfig;
        }

        /// <summary>
        ///     Who owns a config, exactly, scanning the open scenes now. For commands, tools and
        ///     tests. The resolver is the runtime's own — a count that disagreed with how the
        ///     character actually resolves its config would make the notice untrustworthy.
        /// </summary>
        internal static ConvaiAssetOwnership OwnershipOf(ConvaiBodyAnimationConfig config) =>
            ConvaiAssetOwnership.Of<ConvaiBodyAnimationController>(
                config, BodyAnimationSetupService.ResolveAssignedConfig);


        /// <summary>
        ///     Ownership as plain data, for callers that must not name the editor's ownership
        ///     vocabulary.
        /// </summary>
        /// <remarks>
        ///     The Convai MCP assemblies deliberately reach only <c>Convai.Editor.AI</c> and their own
        ///     module's editor assembly — never the editor UI assembly the vocabulary lives in. They
        ///     are consumers of an ownership <i>verdict</i>, not of the vocabulary that forms it, and
        ///     widening three asmdefs so a tool could name a struct it only serialises would trade a
        ///     clean boundary for nothing. The verdict itself still comes from the one shared scan, so
        ///     a tool and the inspector cannot disagree about a character.
        /// </remarks>
        internal static void DescribeOwnership(
            ConvaiBodyAnimationConfig config,
            out bool shipsWithSdk, out int userCount, out bool editingAffectsOthers)
        {
            ConvaiAssetOwnership ownership = OwnershipOf(config);
            shipsWithSdk = ownership.RequiresProjectCopy;
            userCount = ownership.UserCount;
            editingAffectsOthers = ownership.EditingAffectsOthers;
        }

        /// <summary>The same answer for draw paths, reusing the shared throttled scan.</summary>
        internal static ConvaiAssetOwnership OfCachedFor(ConvaiBodyAnimationConfig config) =>
            ConvaiAssetOwnership.OfCached<ConvaiBodyAnimationController>(
                config, BodyAnimationSetupService.ResolveAssignedConfig);

        /// <summary>
        ///     Counts controllers in the loaded scenes that resolve to <paramref name="config" />,
        ///     through the one scan every surface shares.
        /// </summary>
        internal static int CountCharactersUsing(ConvaiBodyAnimationConfig config) =>
            BodyAnimationUsage.CountUsing(config);

        /// <summary>
        ///     Duplicates the shared config for this character alone, reporting what happened. When
        ///     the config arrives via a profile, the profile is duplicated too — otherwise the
        ///     profile would keep supplying the shared config and the new one would never be read.
        /// </summary>
        /// <remarks>
        ///     Headless on purpose: no dialogs, no selection changes, no console output. The
        ///     inspector's button and the Convai MCP tuning tool both call this, so the copy is
        ///     performed one way and a failure reads the same wherever it surfaces.
        /// </remarks>
        internal static bool TryMakeUnique(
            ConvaiBodyAnimationConfig config,
            ConvaiBodyAnimationController owner,
            out BodyAnimationMakeUniqueResult result)
        {
            if (config == null || owner == null)
            {
                result = BodyAnimationMakeUniqueResult.Failed(
                    "There is no config to copy for this character.");
                return false;
            }

            string configPath = AssetDatabase.GetAssetPath(config);
            if (string.IsNullOrEmpty(configPath))
            {
                result = BodyAnimationMakeUniqueResult.Failed(
                    "This config is not a saved project asset, so it cannot be duplicated.");
                return false;
            }

            string directory = ConvaiProjectAssetFolder.For(owner, "BodyAnimation");
            string newConfigPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{ConvaiProjectAssetFolder.SanitizeName(owner)}_BodyAnimationConfig.asset");
            if (!AssetDatabase.CopyAsset(configPath, newConfigPath))
            {
                result = BodyAnimationMakeUniqueResult.Failed(
                    $"Unity could not copy '{configPath}'. Check that the folder is writable, then try again.");
                return false;
            }

            var uniqueConfig = AssetDatabase.LoadAssetAtPath<ConvaiBodyAnimationConfig>(newConfigPath);
            var serializedOwner = new SerializedObject(owner);
            var profile = serializedOwner.FindProperty("profile")?.objectReferenceValue
                as ConvaiBodyAnimationProfile;

            string newProfilePath = string.Empty;
            if (profile != null && profile.Config == config)
            {
                string profilePath = AssetDatabase.GetAssetPath(profile);
                newProfilePath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{directory}/{ConvaiProjectAssetFolder.SanitizeName(owner)}_BodyAnimationProfile.asset");
                if (!AssetDatabase.CopyAsset(profilePath, newProfilePath))
                {
                    // The profile still supplies the shared config, so the character would keep
                    // reading it and the copy would be dead weight. Reported rather than swallowed:
                    // silently leaving the character on the shared config is the exact trap this
                    // whole command exists to prevent.
                    result = BodyAnimationMakeUniqueResult.Failed(
                        $"The config was copied to '{newConfigPath}', but Unity could not copy the " +
                        $"'{profile.name}' profile that supplies it, so this character still reads " +
                        "the shared config. Duplicate that profile yourself and point its Config " +
                        "field at the new copy.");
                    return false;
                }

                var uniqueProfile = AssetDatabase.LoadAssetAtPath<ConvaiBodyAnimationProfile>(newProfilePath);
                uniqueProfile.Initialize(profile.AnimationSet, uniqueConfig, profile.AutoCreateConversationFlow);
                EditorUtility.SetDirty(uniqueProfile);
                serializedOwner.FindProperty("profile").objectReferenceValue = uniqueProfile;
            }
            else
            {
                serializedOwner.FindProperty("_config").objectReferenceValue = uniqueConfig;
            }

            serializedOwner.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            // This character no longer reads the old config, so every ownership notice on screen
            // must stop describing an arrangement that ended the moment the copy landed.
            ConvaiAssetOwnership.Invalidate();

            result = BodyAnimationMakeUniqueResult.Succeeded(uniqueConfig, newConfigPath, newProfilePath);
            return true;
        }

        /// <summary>
        ///     <see cref="TryMakeUnique" /> for the inspector's button: reports a failure in a
        ///     dialog and pings the new asset so the user can see where it landed.
        /// </summary>
        internal static void MakeUnique(ConvaiBodyAnimationConfig config, ConvaiBodyAnimationController owner)
        {
            if (TryMakeUnique(config, owner, out BodyAnimationMakeUniqueResult result))
            {
                EditorGUIUtility.PingObject(result.Config);
                return;
            }

            EditorUtility.DisplayDialog("Body Animation", result.FailureReason, "OK");
        }

        /// <summary>
        ///     The config a controller actually uses: its profile's, or its direct field. Resolved
        ///     by the setup service, so every surface asking "which config is live on this
        ///     character?" gets its answer from one place.
        /// </summary>
        internal static ConvaiBodyAnimationConfig ResolveConfig(ConvaiBodyAnimationController controller) =>
            BodyAnimationSetupService.ResolveAssignedConfig(controller);
    }
}
