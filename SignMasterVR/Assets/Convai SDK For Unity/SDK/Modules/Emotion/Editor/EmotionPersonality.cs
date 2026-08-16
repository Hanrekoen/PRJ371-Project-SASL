using Convai.Domain.Embodiment.Semantics;
using System.Collections.Generic;
using Convai.Modules.Emotion.Components;
using Convai.Modules.Emotion.Profiles;
using UnityEditor;
using UnityEngine;
using Convai.Editor.Ownership;
using Convai.Editor.UI;
using Convai.Shared.Compatibility;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>One documented character type: a named set of field values, not a vibe.</summary>
    internal readonly struct EmotionArchetype
    {
        public EmotionArchetype(CharacterDemeanor type, string description)
        {
            Type = type;
            Description = description;
        }

        public CharacterDemeanor Type { get; }

        /// <summary>
        ///     Read from <see cref="CharacterDemeanors" /> rather than spelled here: three modules
        ///     show this word and they are only guaranteed to show the same one if none of them
        ///     owns a copy of it.
        /// </summary>
        public string Name => CharacterDemeanors.DisplayName(Type);

        public string Description { get; }
    }

    /// <summary>
    ///     What "Make Unique For This Character" actually did — which asset was written, or why
    ///     nothing was.
    /// </summary>
    internal readonly struct EmotionMakeUniqueResult
    {
        private EmotionMakeUniqueResult(ConvaiEmotionProfile profile, string assetPath, string failureReason)
        {
            Profile = profile;
            AssetPath = assetPath;
            FailureReason = failureReason;
        }

        /// <summary>The character's own copy of the personality, or <c>null</c> when nothing was written.</summary>
        internal ConvaiEmotionProfile Profile { get; }

        /// <summary>Project path of the new personality asset; empty on failure.</summary>
        internal string AssetPath { get; }

        /// <summary>Why nothing usable was produced, in the user's own terms; empty on success.</summary>
        internal string FailureReason { get; }

        internal static EmotionMakeUniqueResult Succeeded(ConvaiEmotionProfile profile, string assetPath) =>
            new(profile, assetPath, string.Empty);

        internal static EmotionMakeUniqueResult Failed(string reason) =>
            new(null, string.Empty, reason);
    }

    /// <summary>
    ///     The personality controls shared by the Emotion inspector and the Emotion editor window:
    ///     four character types, a handful of plain-language controls, and the shared-profile
    ///     warning that stops them being a trap.
    /// </summary>
    /// <remarks>
    ///     Every write goes through <see cref="SerializedObject" /> on the profile asset so it is
    ///     undoable and correctly dirties the asset. Field names are the profile's serialized names;
    ///     they are private by design, which is why this lives in the module's editor assembly.
    /// </remarks>
    internal static class EmotionPersonality
    {
        internal static readonly EmotionArchetype[] Archetypes =
        {
            new(CharacterDemeanor.Composed, "Receptionist, clerk, guide. Reads calm and even."),
            new(CharacterDemeanor.Warm, "The default. Approachable and easy to read."),
            new(CharacterDemeanor.Energetic, "Host, tour guide, streamer. Big, fast reactions."),
            new(CharacterDemeanor.Reserved, "Guard, officiant. Barely shows anything.")
        };

        // ------------------------------------------------------------------ character type

        /// <summary>Draws the character-type buttons, applying one on click (confirmed first).</summary>
        /// <remarks>
        ///     When an author has tuned the profile away from all four, the row shows
        ///     <see cref="ConvaiEditorProfileField.CustomLabel" /> rather than four unselected pills.
        ///     Pressing a type then says what it will overwrite. It must not, however, be the state
        ///     Convai's own shipped personalities are in; a guard test asserts each of them still
        ///     identifies as its type.
        /// </remarks>
        internal static void DrawArchetypeRow(
            ConvaiEmotionProfile profile, ConvaiEmotionController owner = null)
        {
            if (profile == null) return;

            if (s_archetypeLabels == null)
            {
                s_archetypeLabels = new GUIContent[Archetypes.Length];
                for (int i = 0; i < Archetypes.Length; i++)
                    s_archetypeLabels[i] = new GUIContent(Archetypes[i].Name, Archetypes[i].Description);
            }

            int selected = IndexOf(EmotionDemeanorTooling.Identify(profile));
            string explanation = selected >= 0
                ? Archetypes[selected].Description
                : ConvaiEditorProfileField.CustomCaption;

            int clicked;
            using (new EditorGUI.DisabledScope(
                       owner == null && ConvaiAssetOwnership.IsSdkAsset(profile)))
            {
                clicked = ConvaiEditorControls.PresetPicker(
                    CharacterTypeLabel, s_archetypeLabels, selected, explanation);
            }

            if (clicked < 0 || clicked == selected) return;

            ConfirmAndApply(profile, owner, Archetypes[clicked]);
        }

        /// <summary>
        ///     Whether this personality no longer matches any of the four named types. False when
        ///     there is no asset — "custom" without an asset is a state the character is not in.
        /// </summary>
        internal static bool IsCustomized(ConvaiEmotionProfile profile) =>
            profile != null && !EmotionDemeanorTooling.Identify(profile).HasValue;

        private static int IndexOf(CharacterDemeanor? type)
        {
            if (!type.HasValue) return -1;
            for (int i = 0; i < Archetypes.Length; i++)
            {
                if (Archetypes[i].Type != type.Value) continue;
                return i;
            }

            return -1;
        }

        /// <summary>Labels for <see cref="Archetypes" />, built once — the picker draws every repaint.</summary>
        private static GUIContent[] s_archetypeLabels;

        private static readonly GUIContent CharacterTypeLabel = new("Character Type");

        /// <summary>
        ///     Confirms and applies after the current IMGUI pass — a modal raised from inside a layout
        ///     scope discards the layout state the enclosing scope is about to close.
        /// </summary>
        private static void ConfirmAndApply(
            ConvaiEmotionProfile profile, ConvaiEmotionController owner, EmotionArchetype archetype)
        {
            EditorApplication.delayCall += () =>
            {
                if (profile == null || !Confirm(archetype)) return;

                ConvaiEmotionProfile target = EnsureWritable(profile, owner);
                if (target != null) Apply(target, archetype.Type);
            };
        }

        /// <summary>
        ///     The personality a command must write to, copying the SDK's shipped one for this
        ///     character first when that is what it is looking at.
        /// </summary>
        /// <remarks>
        ///     The imperative counterpart of <see cref="ConvaiOwnedEdit" />, for writes that land
        ///     after the draw pass - applying a character type, which is confirmed in a modal.
        /// </remarks>
        internal static ConvaiEmotionProfile EnsureWritable(
            ConvaiEmotionProfile profile, ConvaiEmotionController owner)
        {
            ConvaiCopyOnWriteResult result = ConvaiCopyOnWrite.EnsureWritable(
                profile, owner, () => Copier.CopyForOwner(profile, owner));

            if (!result.Succeeded)
            {
                if (!string.IsNullOrEmpty(result.FailureReason))
                    EditorUtility.DisplayDialog("Emotions", result.FailureReason, "OK");
                return null;
            }

            if (result.Copied) ConvaiCopyReceipts.Record(owner, result.AssetPath, result.Target);
            return result.Target as ConvaiEmotionProfile;
        }

        private static bool Confirm(EmotionArchetype archetype) =>
            EditorUtility.DisplayDialog(
                $"Make this character {archetype.Name}?",
                $"{archetype.Description}\n\nThis rewrites the personality settings on this asset: " +
                "how fast it reacts, its resting mood, mixing, small movements and beat reactions, " +
                "and the per-emotion tables. The emotion vocabulary, the expressions and the overall " +
                "strength trim are left alone. You can undo it (Ctrl+Z).",
                "Apply", "Cancel");

        /// <summary>Whether the profile's current values already match this character type.</summary>
        /// <remarks>
        ///     Forwards to <see cref="EmotionDemeanorTooling.Matches" /> so the comparison and the
        ///     write are the same field list. The previous implementation built a whole reference
        ///     <c>ConvaiEmotionProfile</c> — including the entire default expression-recipe library,
        ///     around forty-five <c>AnimationCurve</c>s — and destroyed it again, once per character
        ///     type, on every inspector repaint.
        /// </remarks>
        internal static bool Matches(ConvaiEmotionProfile profile, CharacterDemeanor type) =>
            EmotionDemeanorTooling.Matches(profile, type);

        /// <summary>Writes a character type's values as one undoable step.</summary>
        internal static void Apply(ConvaiEmotionProfile profile, CharacterDemeanor type)
        {
            if (profile == null) return;

            var serialized = new SerializedObject(profile);
            serialized.Update();
            EmotionDemeanorTooling.Apply(serialized, type, profile.Taxonomy);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
        }

        /// <summary>
        ///     How this module gives a character its own copy: one asset behind the component's
        ///     <c>profile</c> field, which is the ordinary arrangement.
        /// </summary>
        internal static readonly IConvaiSettingsCopier Copier =
            new ConvaiFieldSettingsCopier("personality", "Emotion", "_Emotion", "profile");

        // ------------------------------------------------------------------ the controls that matter

        /// <summary>
        ///     The handful of controls a non-technical user can reason about, worded as behaviour
        ///     rather than as the fields they drive. Everything else lives behind the footer link.
        /// </summary>
        /// <param name="otherCharactersInScene">
        ///     Whether the scene has a second Convai character. "Picks up other characters' moods"
        ///     has nothing to react to on its own, so it is shown as unavailable with the reason
        ///     rather than as a switch that silently does nothing.
        /// </param>
        /// <param name="owner">
        ///     The character these settings belong to. Present, the controls stay live and an
        ///     SDK-owned personality is copied for this character on the first change.
        /// </param>
        internal static void DrawControls(
            ConvaiEmotionProfile profile, bool otherCharactersInScene, ConvaiEmotionController owner = null)
        {
            if (profile == null) return;

            using var edit = ConvaiOwnedEdit.Begin(profile, owner, Copier);
            using var readOnly = new EditorGUI.DisabledScope(!edit.CanEdit);

            SerializedObject serialized = edit.Serialized;

            DrawRestingMood(serialized, profile);

            EditorGUILayout.Space(2f);
            DrawSlider(serialized, "intensityOffset", "How strongly it shows", -0.25f, 0.25f,
                "Nudges every expression up or down. Leave at the middle unless the character reads " +
                "as generally too flat or too much.");
            DrawSlider(serialized, "lerpSpeed", "How quickly it reacts", 0.1f, 20f,
                "Higher reads as quick and reactive; lower as measured and slow to show its hand.");

            EditorGUILayout.Space(2f);
            DrawToggle(serialized, "microExpressionsEnabled", "Never sits perfectly still",
                "Keeps a trace of movement in the face so a resting character does not read as a frozen mask.");
            DrawToggle(serialized, "moodDriftEnabled", "Mood follows the conversation",
                "A long stretch of one feeling leaves the character in that mood for a while afterwards.");
            DrawToggle(serialized, "enableEmotionBlending", "Shows more than one emotion at once",
                "Related emotions come along at reduced strength — anger with a trace of disgust, " +
                "fear with a trace of surprise.");

            using (new EditorGUI.DisabledScope(!otherCharactersInScene))
            {
                SerializedProperty contagion = serialized.FindProperty("contagionEnabled");
                bool next = EditorGUILayout.ToggleLeft(
                    new GUIContent("Picks up other characters' moods",
                        "Catches a faint echo of a strong feeling from another Convai character nearby."),
                    contagion.boolValue && otherCharactersInScene);

                // Only ever written while the control is live. Showing it unticked because this
                // scene has one character must not WRITE that back — the profile can be shared with
                // scenes that do have a second character.
                if (otherCharactersInScene) contagion.boolValue = next;
            }

            if (!otherCharactersInScene)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    GUILayout.Label(
                        "There is only one Convai character in the open scenes, so there is nobody to " +
                        "react to yet.",
                        ConvaiEditorStyles.CaptionWrapped);
                }
            }

        }

        private static void DrawSlider(
            SerializedObject serialized, string field, string label, float min, float max, string tooltip)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) return;
            property.floatValue = EditorGUILayout.Slider(
                new GUIContent(label, tooltip), property.floatValue, min, max);
        }

        private static void DrawToggle(
            SerializedObject serialized, string field, string label, string tooltip)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) return;
            property.boolValue = EditorGUILayout.ToggleLeft(
                new GUIContent(label, tooltip), property.boolValue);
        }

        // ------------------------------------------------------------------ resting mood

        /// <summary>
        ///     Resting mood as one dropdown plus a strength slider, rather than a free-text label
        ///     the user has to spell correctly and an intensity that silently defeats it at 0.
        /// </summary>
        private static void DrawRestingMood(SerializedObject serialized, ConvaiEmotionProfile profile)
        {
            SerializedProperty label = serialized.FindProperty("baselineEmotionLabel");
            SerializedProperty intensity = serialized.FindProperty("baselineIntensity");
            if (label == null || intensity == null) return;

            List<string> options = BuildMoodOptions(profile, label.stringValue);
            int current = IndexOfMood(options, label.stringValue, intensity.floatValue);

            // Entry 0 is this dropdown's own word for "no resting mood", not an emotion name, so
            // only the emotions themselves are capitalized for display.
            string[] shown = options.ToArray();
            for (int i = 1; i < shown.Length; i++) shown[i] = EmotionLabelCatalog.DisplayName(shown[i]);

            int next = EditorGUILayout.Popup(
                new GUIContent("Resting mood",
                    "What the face settles to when nothing is happening."),
                current, shown);

            if (next != current)
            {
                if (next == 0)
                {
                    label.stringValue = string.Empty;
                    intensity.floatValue = 0f;
                }
                else
                {
                    label.stringValue = options[next];
                    if (intensity.floatValue <= 0f)
                        intensity.floatValue = EmotionPersonalityTable.DefaultRestingMoodIntensity;
                }
            }

            if (label.stringValue.Length > 0 && intensity.floatValue > 0f)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    intensity.floatValue = EditorGUILayout.Slider(
                        new GUIContent("How strong", "How much of that mood shows at rest."),
                        intensity.floatValue, 0.05f, 1f);
                }
            }
        }

        /// <summary>"Neutral" first, then every non-neutral emotion the vocabulary defines.</summary>
        /// <remarks>
        ///     Reads the cached <see cref="EmotionLabelCatalog" /> rather than synthesizing a
        ///     taxonomy asset per repaint. "Neutral" is this dropdown's own word for "no resting
        ///     mood", which is why the vocabulary's own neutral entry is filtered out — offering both
        ///     would be two options meaning the same thing.
        /// </remarks>
        private static List<string> BuildMoodOptions(ConvaiEmotionProfile profile, string current)
        {
            var options = new List<string>(10) { "Neutral" };

            string[] labels = EmotionLabelCatalog.LabelsFor(profile != null ? profile.Taxonomy : null);
            for (int i = 0; i < labels.Length; i++)
            {
                if (string.Equals(labels[i], "neutral", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (EmotionLabelCatalog.IndexOf(options, labels[i]) < 0) options.Add(labels[i]);
            }

            // A hand-edited label the vocabulary no longer defines must stay selectable, or opening
            // the inspector would silently rewrite it.
            if (!string.IsNullOrWhiteSpace(current) && EmotionLabelCatalog.IndexOf(options, current) < 0)
                options.Add(current);
            return options;
        }

        private static int IndexOfMood(List<string> options, string label, float intensity)
        {
            if (string.IsNullOrWhiteSpace(label) || intensity <= 0f) return 0;
            int index = options.IndexOf(label);
            return index >= 0 ? index : 0;
        }

        // ------------------------------------------------------------------ sharing

        /// <summary>
        ///     A profile asset can be referenced by many characters, so moving a slider silently
        ///     changes every one of them. Without this notice the personality controls are a trap
        ///     rather than a feature.
        /// </summary>
        internal static void DrawSharingNotice(ConvaiEmotionProfile profile, ConvaiEmotionController owner)
        {
            if (profile == null || owner == null) return;

            ConvaiAssetOwnership ownership = OfCachedFor(profile);

            // Silent for an SDK-owned personality: copy-on-write has already made that a non-event,
            // and warning about something the SDK handles for you teaches people to stop reading
            // warnings. Sharing still gets a word - that is the one case where a control changes
            // something the user cannot see.
            if (ownership.Kind == ConvaiAssetOwnershipKind.SdkOwned) return;

            EditorGUILayout.Space(4f);
            ConvaiOwnershipNotice.Draw(ownership, () => MakeUnique(profile, owner));
        }

        /// <summary>
        ///     Who owns a personality, exactly, scanning the open scenes now. For commands, tools
        ///     and tests.
        /// </summary>
        internal static ConvaiAssetOwnership OwnershipOf(ConvaiEmotionProfile profile) =>
            ConvaiAssetOwnership.Of<ConvaiEmotionController>(
                profile, EmotionSetupService.ResolveAssignedProfile);


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
            ConvaiEmotionProfile profile,
            out bool shipsWithSdk, out int userCount, out bool editingAffectsOthers,
            out string noticeMessage)
        {
            ConvaiAssetOwnership ownership = OwnershipOf(profile);
            shipsWithSdk = ownership.RequiresProjectCopy;
            userCount = ownership.UserCount;
            editingAffectsOthers = ownership.EditingAffectsOthers;

            // The tool hands the same sentence back to an assistant that the inspector shows the
            // user, so the two cannot explain the same situation differently.
            noticeMessage = ownership.NoticeMessage;
        }

        /// <summary>
        ///     Where a copy for <paramref name="owner" /> would land, as plain data.
        /// </summary>
        /// <remarks>
        ///     Answered by the same resolver that performs the copy. The MCP tool used to predict
        ///     <c>Assets/&lt;Name&gt;_Emotion.asset</c> in its dry run from a string it built itself,
        ///     which stopped being true the moment the destination rule moved to "beside the
        ///     character's prefab, else Assets/Convai/Emotion". A dry run that names the wrong path is
        ///     worse than one that names none.
        /// </remarks>
        internal static string PredictedCopyPath(ConvaiEmotionController owner) =>
            owner == null
                ? string.Empty
                : $"{ConvaiProjectAssetFolder.For(owner, "Emotion")}/" +
                  $"{ConvaiProjectAssetFolder.SanitizeName(owner)}_Emotion.asset";

        /// <summary>Whether a personality ships inside the Convai package, as plain data.</summary>
        internal static bool ShipsWithSdk(ConvaiEmotionProfile profile) =>
            ConvaiAssetOwnership.IsSdkAsset(profile);

        /// <summary>The same answer for draw paths, reusing the shared throttled scan.</summary>
        internal static ConvaiAssetOwnership OfCachedFor(ConvaiEmotionProfile profile) =>
            ConvaiAssetOwnership.OfCached<ConvaiEmotionController>(
                profile, EmotionSetupService.ResolveAssignedProfile);

        internal static int CountCharactersUsing(ConvaiEmotionProfile profile)
        {
            if (profile == null) return 0;

            ConvaiEmotionController[] controllers =
                ConvaiObjectFind.All<ConvaiEmotionController>(FindObjectsInactive.Include);

            int count = 0;
            for (int i = 0; i < controllers.Length; i++)
                if (EmotionSetupService.ResolveAssignedProfile(controllers[i]) == profile) count++;
            return count;
        }

        /// <summary>
        ///     Duplicates a personality for one character alone and assigns the copy, reporting
        ///     what happened.
        /// </summary>
        /// <remarks>
        ///     Headless on purpose: no dialogs, no selection changes, no console output. The
        ///     inspector's <b>Make Unique For This Character</b> button and the Convai MCP tuning
        ///     tool both call this, so the copy is performed one way and a failure reads the same
        ///     wherever it surfaces. Mirrors <c>BodyAnimationPersonality.TryMakeUnique</c>.
        /// </remarks>
        internal static bool TryMakeUnique(
            ConvaiEmotionProfile profile,
            ConvaiEmotionController owner,
            out EmotionMakeUniqueResult result)
        {
            if (profile == null || owner == null)
            {
                result = EmotionMakeUniqueResult.Failed("There is no personality to copy for this character.");
                return false;
            }

            string path = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(path))
            {
                result = EmotionMakeUniqueResult.Failed(
                    "This personality is not a saved project asset, so it cannot be duplicated.");
                return false;
            }

            string directory = ConvaiProjectAssetFolder.For(owner, "Emotion");
            string newPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{ConvaiProjectAssetFolder.SanitizeName(owner)}_Emotion.asset");
            if (!AssetDatabase.CopyAsset(path, newPath))
            {
                result = EmotionMakeUniqueResult.Failed(
                    $"Unity could not copy '{path}'. Check that the folder is writable, then try again.");
                return false;
            }

            var unique = AssetDatabase.LoadAssetAtPath<ConvaiEmotionProfile>(newPath);
            var serialized = new SerializedObject(owner);
            serialized.FindProperty("profile").objectReferenceValue = unique;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            // This character no longer reads the old profile, so every ownership notice on screen
            // must stop describing an arrangement that ended the moment the copy landed.
            ConvaiAssetOwnership.Invalidate();

            result = EmotionMakeUniqueResult.Succeeded(unique, newPath);
            return true;
        }

        /// <summary>
        ///     <see cref="TryMakeUnique" /> for the inspector's button: reports a failure in a
        ///     dialog and pings the new asset so the user can see where it landed.
        /// </summary>
        internal static void MakeUnique(ConvaiEmotionProfile profile, ConvaiEmotionController owner)
        {
            if (TryMakeUnique(profile, owner, out EmotionMakeUniqueResult result))
            {
                EditorGUIUtility.PingObject(result.Profile);
                return;
            }

            EditorUtility.DisplayDialog("Emotions", result.FailureReason, "OK");
        }

        /// <summary>Seconds the second-character scan stays valid. Matches the shared ownership scan.</summary>
        private const double CacheLifetimeSeconds = 1d;

        private static ConvaiEmotionController s_cachedOtherOwner;
        private static double s_cachedOtherAt;
        private static bool s_cachedOther;

        /// <summary>Whether the open scenes hold a second emotion-bearing character.</summary>
        /// <remarks>
        ///     Throttled on the same clock as the shared ownership scan, and for the same
        ///     reason: this walks every object in the loaded scenes, and it is read twice per
        ///     inspector repaint — by the troubleshooter and by the personality controls — on a
        ///     surface that repaints every frame in Play Mode. It was the one scan left unthrottled
        ///     two lines below the comment explaining why the other one had to be.
        /// </remarks>
        internal static bool HasOtherCharacters(ConvaiEmotionController owner)
        {
            double now = EditorApplication.timeSinceStartup;
            if (owner == s_cachedOtherOwner && now - s_cachedOtherAt < CacheLifetimeSeconds)
                return s_cachedOther;

            ConvaiEmotionController[] controllers =
                ConvaiObjectFind.All<ConvaiEmotionController>(FindObjectsInactive.Include);

            bool found = false;
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i] == owner) continue;
                found = true;
                break;
            }

            s_cachedOtherOwner = owner;
            s_cachedOtherAt = now;
            s_cachedOther = found;
            return found;
        }
    }
}
