using Convai.Domain.Embodiment.Semantics;
using Convai.Editor.Ownership;
using Convai.Editor.UI;
using Convai.Modules.Gaze.Components;
using Convai.Modules.Gaze.Data;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Gaze.Editor
{
    /// <summary>Which of the three high-level gaze feel controls a value belongs to.</summary>
    internal enum GazePersonalityDial
    {
        /// <summary>How much the character holds your gaze rather than breaking away.</summary>
        EyeContact = 0,

        /// <summary>How busy the eyes are — micro-motion, face scanning, blinking, idle looks.</summary>
        Liveliness = 1,

        /// <summary>How much the head participates rather than the eyes doing all the work.</summary>
        HeadMovement = 2
    }

    /// <summary>
    ///     The three controls a non-technical user can reason about, sitting over the profile's 111
    ///     fields — and the archetype row that fills them all at once.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why this exists.</b> Before it, gaze tuning was five discrete archetype presets or
    ///         111 raw fields, with nothing in between. A user who wanted "like Warm, but a bit more
    ///         eye contact" had to hand-edit eight rows of a per-state policy table.
    ///     </para>
    ///     <para>
    ///         <b>Derived, never stored.</b> Reading a dial infers its value from the fields it
    ///         drives rather than from a hidden serialized scalar. That is the load-bearing design
    ///         decision: a stored slider silently overwrites hand-authored fields the first time the
    ///         inspector redraws, and lies about the profile the moment anyone edits one of those
    ///         fields directly. Here, hand-editing a state row moves the dial — the dial never
    ///         quietly moves the row back.
    ///     </para>
    ///     <para>
    ///         Writes only happen on an actual user change, go through <see cref="SerializedObject" />
    ///         in one undo group, and clamp into each field's own <c>[Range]</c>, so an applied dial
    ///         always survives <c>OnValidate</c> — the same invariant
    ///         <see cref="GazeProfileArchetypes" /> guarantees by construction.
    ///     </para>
    /// </remarks>
    internal static class GazePersonality
    {
        // Authored bounds for each dial's driven fields. The midpoint of every pair is roughly the
        // shipped default, so a fresh profile reads near 0.5 on all three.
        private const float EngagementFloor = 0.35f;
        private const float EngagementCeiling = 1f;
        private const float AversionFloor = 0f;
        private const float AversionCeiling = 0.8f;

        private const float MicroSaccadeSlowest = 3.0f;
        private const float MicroSaccadeFastest = 0.6f;
        private const float FaceScanSlowest = 4.5f;
        private const float FaceScanFastest = 1.0f;
        private const float BlinkSlowest = 7.0f;
        private const float BlinkFastest = 2.5f;

        private const float HeadContributionFloor = 0.15f;
        private const float HeadContributionCeiling = 1f;
        /// <summary>
        ///     Head-entry angles the "how much does the head move" dial sweeps between: a
        ///     reluctant character's head only joins a look this far off-axis, a mobile one's
        ///     joins almost immediately.
        /// </summary>
        private const float HeadEntryLatest = 25f;
        private const float HeadEntryEarliest = 2f;

        // ------------------------------------------------------------------ reading

        /// <summary>
        ///     The current value of <paramref name="dial" /> on <paramref name="profile" />, in
        ///     0..1, inferred from the fields it drives. Returns 0.5 for a profile it cannot read,
        ///     never throws.
        /// </summary>
        internal static float Read(ConvaiGazeProfile profile, GazePersonalityDial dial)
        {
            if (profile == null) return 0.5f;

            var serialized = new SerializedObject(profile);
            return Read(serialized, dial);
        }

        private static float Read(SerializedObject serialized, GazePersonalityDial dial)
        {
            switch (dial)
            {
                case GazePersonalityDial.EyeContact:
                {
                    // The Idle row deliberately runs at zero engagement (the character is not
                    // engaged with anyone), so averaging it in would drag every profile toward the
                    // bottom of the dial and make the control feel broken. Conversational rows only.
                    float engagement = AverageConversationalRow(serialized, "Engagement", EngagementFloor, EngagementCeiling);
                    float aversion = AverageConversationalRow(serialized, "AversionStrength", AversionFloor, AversionCeiling);
                    return Mathf.Clamp01((engagement + (1f - aversion)) * 0.5f);
                }

                case GazePersonalityDial.Liveliness:
                {
                    float micro = InverseNormalize(FloatOf(serialized, "microSaccadeIntervalMean", 1.6f), MicroSaccadeSlowest, MicroSaccadeFastest);
                    float faceScan = InverseNormalize(FloatOf(serialized, "faceScanIntervalMean", 2.4f), FaceScanSlowest, FaceScanFastest);
                    float blink = InverseNormalize(FloatOf(serialized, "blinkIntervalMean", 4.2f), BlinkSlowest, BlinkFastest);
                    return Mathf.Clamp01((micro + faceScan + blink) / 3f);
                }

                case GazePersonalityDial.HeadMovement:
                {
                    float contribution = AverageConversationalRow(
                        serialized, "HeadContribution", HeadContributionFloor, HeadContributionCeiling);
                    float entry = InverseNormalize(
                        FloatOf(serialized, "headEntryDegrees", 12f),
                        HeadEntryLatest, HeadEntryEarliest);
                    return Mathf.Clamp01((contribution + entry) * 0.5f);
                }

                default:
                    return 0.5f;
            }
        }

        // ------------------------------------------------------------------ ownership

        /// <summary>
        ///     How Gaze gives a character its own copy: one profile behind the controller's
        ///     <c>profile</c> field, which is the ordinary arrangement.
        /// </summary>
        internal static readonly IConvaiSettingsCopier Copier =
            new ConvaiFieldSettingsCopier("gaze settings", "Gaze", "_Gaze", "profile");

        /// <summary>
        ///     The profile a write must go to, giving this character its own copy of the SDK's
        ///     shipped settings first when that is what it is looking at.
        /// </summary>
        /// <remarks>
        ///     Gaze commits imperatively — <see cref="Apply(ConvaiGazeProfile, GazePersonalityDial, float)" />
        ///     builds its own <see cref="SerializedObject" /> and writes immediately — so it takes
        ///     this rather than the <c>ConvaiOwnedEdit</c> scope the field-by-field surfaces use.
        ///     Same copier either way, so a dial and a slider in another module produce the same copy
        ///     in the same place.
        /// </remarks>
        internal static ConvaiGazeProfile EnsureWritable(ConvaiGazeProfile profile, ConvaiGazeController owner)
        {
            ConvaiCopyOnWriteResult result = ConvaiCopyOnWrite.EnsureWritable(
                profile, owner, () => Copier.CopyForOwner(profile, owner));

            if (!result.Succeeded)
            {
                if (!string.IsNullOrEmpty(result.FailureReason))
                    EditorUtility.DisplayDialog("Convai Gaze", result.FailureReason, "OK");
                return null;
            }

            if (result.Copied) ConvaiCopyReceipts.Record(owner, result.AssetPath, result.Target);
            return result.Target as ConvaiGazeProfile;
        }

        /// <summary>
        ///     Whether the user may drive these controls. False only when the SDK owns the profile
        ///     and there is no character to make a copy for.
        /// </summary>
        private static bool CanEdit(ConvaiGazeProfile profile, ConvaiGazeController owner) =>
            owner != null || ConvaiAssetOwnership.IsProjectAsset(profile);

        /// <summary>Who owns a personality, for draw paths, reusing the shared throttled scan.</summary>
        internal static ConvaiAssetOwnership OfCachedFor(ConvaiGazeProfile profile) =>
            ConvaiAssetOwnership.OfCached<ConvaiGazeController>(
                profile, GazeSetupService.ResolveAssignedProfile);

        /// <summary>
        ///     A personality asset can be referenced by many characters, so moving a dial silently
        ///     changes every one of them. Without this notice the dials are a trap rather than a
        ///     feature.
        /// </summary>
        /// <remarks>
        ///     Gaze wrote into the profile through <see cref="Apply" /> and said nothing at all — the
        ///     one module with live controls and no ownership notice. Silent for an SDK-owned
        ///     personality for the reason
        ///     <see cref="ConvaiOwnershipNotice.Draw" /> documents: copy-on-write has already made
        ///     that a non-event, and warning about something the SDK handles for you teaches people
        ///     to stop reading warnings. The asset itself is named by the field one row above and by
        ///     the section header, so the notice does not repeat it.
        /// </remarks>
        internal static void DrawSharingNotice(ConvaiGazeProfile profile, ConvaiGazeController owner)
        {
            if (profile == null || owner == null) return;

            ConvaiAssetOwnership ownership = OfCachedFor(profile);
            if (ownership.Kind == ConvaiAssetOwnershipKind.SdkOwned) return;

            EditorGUILayout.Space(4f);
            ConvaiOwnershipNotice.Draw(ownership, () => MakeUnique(profile, owner));
        }

        /// <summary>
        ///     Duplicates a personality for one character alone and assigns the copy, reporting a
        ///     failure in a dialog and pinging the new asset so the user can see where it landed.
        /// </summary>
        /// <remarks>
        ///     Goes through <see cref="Copier" />, so the button here and the first change to a
        ///     dial on an SDK-owned personality produce the same copy in the same place. Mirrors
        ///     <c>EmotionPersonality.MakeUnique</c>.
        /// </remarks>
        internal static void MakeUnique(ConvaiGazeProfile profile, ConvaiGazeController owner)
        {
            if (profile == null || owner == null) return;

            ConvaiCopyOnWriteResult result = Copier.CopyForOwner(profile, owner);
            if (!result.Succeeded)
            {
                EditorUtility.DisplayDialog(
                    "Convai Gaze",
                    string.IsNullOrEmpty(result.FailureReason)
                        ? "These settings could not be copied for this character."
                        : result.FailureReason,
                    "OK");
                return;
            }

            ConvaiCopyReceipts.Record(owner, result.AssetPath, result.Target);

            // This character no longer reads the old personality, so every ownership notice on
            // screen must stop describing an arrangement that ended the moment the copy landed.
            ConvaiAssetOwnership.Invalidate();
            EditorGUIUtility.PingObject(result.Target);
        }

        // ------------------------------------------------------------------ writing

        /// <summary>
        ///     Writes <paramref name="value" /> (0..1) into every field <paramref name="dial" />
        ///     drives, as one undoable step. Idempotent: applying the value a dial already reads
        ///     changes nothing perceptible.
        /// </summary>
        internal static void Apply(ConvaiGazeProfile profile, GazePersonalityDial dial, float value)
        {
            if (profile == null) return;

            value = Mathf.Clamp01(value);
            var serialized = new SerializedObject(profile);
            serialized.Update();

            switch (dial)
            {
                case GazePersonalityDial.EyeContact:
                    SetConversationalRows(serialized, "Engagement", Mathf.Lerp(EngagementFloor, EngagementCeiling, value));
                    SetConversationalRows(serialized, "AversionStrength", Mathf.Lerp(AversionCeiling, AversionFloor, value));
                    // A character that holds contact hard also stops looking away to think.
                    SetFloat(serialized, "planningBreakProbability", Mathf.Lerp(1f, 0.15f, value));
                    break;

                case GazePersonalityDial.Liveliness:
                    SetConversationalRows(serialized, "FixationLiveliness", Mathf.Lerp(0.7f, 1.4f, value));
                    SetFloat(serialized, "microSaccadeIntervalMean", Mathf.Lerp(MicroSaccadeSlowest, MicroSaccadeFastest, value));
                    SetFloat(serialized, "faceScanIntervalMean", Mathf.Lerp(FaceScanSlowest, FaceScanFastest, value));
                    SetFloat(serialized, "blinkIntervalMean", Mathf.Lerp(BlinkSlowest, BlinkFastest, value));
                    SetFloat(serialized, "ambientIntervalMin", Mathf.Lerp(3.2f, 1.1f, value));
                    SetFloat(serialized, "ambientIntervalMax", Mathf.Lerp(8f, 3.2f, value));
                    break;

                case GazePersonalityDial.HeadMovement:
                    SetConversationalRows(serialized, "HeadContribution",
                        Mathf.Lerp(HeadContributionFloor, HeadContributionCeiling, value));
                    SetFloat(serialized, "headEntryDegrees",
                        Mathf.Lerp(HeadEntryLatest, HeadEntryEarliest, value));
                    SetFloat(serialized, "ambientHeadFollow", Mathf.Lerp(0.1f, 0.7f, value));
                    break;
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>
        ///     Draws the three dials. Writes only on an actual user change — never on a plain
        ///     repaint, which is what keeps a hand-edited profile from being silently normalized
        ///     just because someone selected it.
        /// </summary>
        /// <param name="profile">The profile the dials read from and write to.</param>
        /// <param name="serializedProfile">
        ///     The caller's serialized view of <paramref name="profile" />, so reading three dials
        ///     does not construct three <see cref="SerializedObject" />s per repaint. Passing
        ///     <c>null</c> falls back to constructing one, which is what the tests do.
        /// </param>
        /// <param name="owner">
        ///     The character these settings belong to. Present, the dials stay live and an SDK-owned
        ///     profile is copied for this character on the first change.
        /// </param>
        internal static void DrawDials(
            ConvaiGazeProfile profile,
            SerializedObject serializedProfile = null,
            ConvaiGazeController owner = null)
        {
            if (profile == null) return;

            using var readOnly = new EditorGUI.DisabledScope(!CanEdit(profile, owner));

            SerializedObject serialized = serializedProfile != null && serializedProfile.targetObject == profile
                ? serializedProfile
                : new SerializedObject(profile);

            // Once, before the three reads rather than inside each. Safe at this point in the draw:
            // Apply() below writes through its own SerializedObject and commits immediately, so a
            // caller's view is never holding an uncommitted edit when the dials are reached — but it
            // may be holding a stale one, which is exactly what this refreshes.
            serialized.Update();

            DrawDial(profile, owner, serialized, GazePersonalityDial.EyeContact, "Eye contact",
                "How much the character holds your gaze instead of breaking away.", "glances away", "holds it");
            DrawDial(profile, owner, serialized, GazePersonalityDial.Liveliness, "Liveliness",
                "How busy the eyes are: micro-movement, face scanning, blinking and idle looks.", "calm", "lively");
            DrawDial(profile, owner, serialized, GazePersonalityDial.HeadMovement, "Head movement",
                "How much the head turns rather than the eyes doing all the work.", "eyes only", "full head");
        }

        private static void DrawDial(
            ConvaiGazeProfile profile, ConvaiGazeController owner, SerializedObject serialized,
            GazePersonalityDial dial,
            string label, string tooltip, string lowCaption, string highCaption)
        {
            // Read through the caller's view so a repaint costs a property read, not a rebuild.
            float current = Read(serialized, dial);

            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.Slider(new GUIContent(label, tooltip), current, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                ConvaiGazeProfile target = EnsureWritable(profile, owner);
                if (target != null)
                {
                    Undo.RecordObject(target, $"Set gaze {label.ToLowerInvariant()}");
                    Apply(target, dial, next);
                }
            }

            Rect captionRow = EditorGUILayout.GetControlRect(false, 12f);
            captionRow.xMin += EditorGUIUtility.labelWidth;
            var right = new Rect(captionRow) { xMin = captionRow.xMin + captionRow.width * 0.5f };
            GUI.Label(captionRow, lowCaption, ConvaiEditorStyles.MicroLabel);
            GUI.Label(right, highCaption, RightAlignedMiniLabel);
        }


        /// <summary>From the shared style set — the style guide forbids allocating in a draw path.</summary>
        private static GUIStyle RightAlignedMiniLabel => ConvaiEditorStyles.MicroLabelRight;

        // ------------------------------------------------------------------ archetypes

        /// <summary>
        ///     The archetype row, with the currently applied one highlighted and <b>Custom</b> shown
        ///     when none match. Unlike the raw button row it replaces, this answers "which personality
        ///     is this profile on right now?" — which is the first thing anyone looking at it wants to
        ///     know, including after they have tuned it away from every named preset.
        /// </summary>
        /// <param name="owner">
        ///     The character these settings belong to; see <see cref="EnsureWritable" />.
        /// </param>
        internal static void DrawArchetypeRow(
            ConvaiGazeProfile profile, ConvaiGazeController owner = null)
        {
            if (profile == null) return;

            using var readOnly = new EditorGUI.DisabledScope(!CanEdit(profile, owner));

            if (s_archetypeLabels == null)
            {
                s_archetypeLabels = new GUIContent[GazeProfileArchetypes.All.Length];
                for (int i = 0; i < GazeProfileArchetypes.All.Length; i++)
                {
                    GazeProfileArchetypes.GazeArchetype entry = GazeProfileArchetypes.All[i];
                    s_archetypeLabels[i] = new GUIContent(entry.Name, entry.Description);
                }
            }

            GazeProfileArchetypes.GazeArchetype active = ActiveArchetype(profile);
            int selected = -1;
            for (int i = 0; i < GazeProfileArchetypes.All.Length; i++)
            {
                if (!ReferenceEquals(GazeProfileArchetypes.All[i], active)) continue;
                selected = i;
                break;
            }

            string explanation = active != null
                ? active.Description
                : ConvaiEditorProfileField.CustomCaption;

            int clicked = ConvaiEditorControls.PresetPicker(
                CharacterTypeLabel, s_archetypeLabels, selected, explanation);
            if (clicked >= 0 && clicked != selected)
                ConfirmAndApply(profile, owner, GazeProfileArchetypes.All[clicked]);
        }

        /// <summary>
        ///     Whether this profile no longer matches any named personality. False when there is no
        ///     asset — "custom" without an asset is a state the character is not in.
        /// </summary>
        internal static bool IsCustomized(ConvaiGazeProfile profile) =>
            profile != null && ActiveArchetype(profile) == null;

        /// <summary>Labels for the archetype row, built once — the picker draws every repaint.</summary>
        private static GUIContent[] s_archetypeLabels;

        private static readonly GUIContent CharacterTypeLabel = new("Character Type");

        /// <summary>
        ///     Confirms and applies after the current IMGUI pass — a modal raised from inside a layout
        ///     scope discards the layout state the enclosing scope is about to close.
        /// </summary>
        private static void ConfirmAndApply(
            ConvaiGazeProfile profile, ConvaiGazeController owner,
            GazeProfileArchetypes.GazeArchetype archetype)
        {
            EditorApplication.delayCall += () =>
            {
                if (profile == null || !Confirm(archetype)) return;

                ConvaiGazeProfile target = EnsureWritable(profile, owner);
                if (target == null) return;

                // Through the target's own view rather than the caller's: after a copy the caller is
                // still holding a view of the SDK's asset, and writing that one would land the
                // archetype where it can never stay. The caller's view is also a pass old by now.
                var write = new SerializedObject(target);

                write.Update();
                GazeProfileArchetypes.Apply(write, archetype);
                write.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            };
        }

        private static bool Confirm(GazeProfileArchetypes.GazeArchetype archetype) =>
            EditorUtility.DisplayDialog(
                $"Apply the {archetype.Name} personality?",
                $"{archetype.Description}\n\nThis overwrites the per-state behaviour table and the " +
                "idle-life, blink and face-scan settings on this profile. You can undo it (Ctrl+Z).",
                "Apply", "Cancel");

        /// <summary>
        ///     The archetype this profile currently matches, or <c>null</c> when it has been
        ///     customised. Compared on the state table, which is what an archetype actually
        ///     defines — matching on one scalar would light a pill up for a profile that no longer
        ///     behaves anything like it.
        /// </summary>
        internal static GazeProfileArchetypes.GazeArchetype ActiveArchetype(ConvaiGazeProfile profile)
        {
            if (profile == null) return null;

            foreach (GazeProfileArchetypes.GazeArchetype archetype in GazeProfileArchetypes.All)
                if (Matches(profile, archetype)) return archetype;

            return null;
        }

        /// <summary>Whether every value <paramref name="archetype" /> authors matches the profile.</summary>
        /// <remarks>
        ///     Compares exactly what <see cref="GazeProfileArchetypes.Apply" /> writes: the state
        ///     table and the nine feel values beneath it. It used to compare the table alone, so a
        ///     profile whose blink rate or ambient look-around an author had retuned still lit up
        ///     the pill of a preset it had left — and because the picker ignores a click on the pill
        ///     that is already lit, there was then no way to put those nine values back. The caption
        ///     under the row asserted the preset's description too, describing behaviour the profile
        ///     no longer had.
        /// </remarks>
        internal static bool Matches(ConvaiGazeProfile profile, GazeProfileArchetypes.GazeArchetype archetype)
        {
            if (profile == null || archetype?.States == null) return false;
            if (profile.StatePolicies == null || profile.StatePolicies.Count != archetype.States.Length) return false;

            for (int i = 0; i < archetype.States.Length; i++)
            {
                GazeProfileArchetypes.StateRow row = archetype.States[i];
                if (!TryFindPolicy(profile, row.State, out GazeStatePolicy policy)) return false;

                if (!Approximately(policy.Engagement, row.Engagement)) return false;
                if (policy.AllowPlayerTarget != row.AllowPlayerTarget) return false;
                if (!Approximately(policy.HeadContribution, row.HeadContribution)) return false;
                if (policy.AllowBodyTurn != row.AllowBodyTurn) return false;
                if (policy.AversionMode != row.AversionMode) return false;
                if (!Approximately(policy.AversionStrength, row.AversionStrength)) return false;
                if (!Approximately(policy.FixationLiveliness, row.FixationLiveliness)) return false;
            }

            return Approximately(profile.AmbientYawRangeDegrees, archetype.AmbientYawRangeDegrees) &&
                   Approximately(profile.AmbientIntervalMin, archetype.AmbientIntervalMin) &&
                   Approximately(profile.AmbientIntervalMax, archetype.AmbientIntervalMax) &&
                   Approximately(profile.AmbientHeadFollow, archetype.AmbientHeadFollow) &&
                   Approximately(profile.AmbientRecenterBias, archetype.AmbientRecenterBias) &&
                   profile.EnableCuriosityGlances == archetype.EnableCuriosityGlances &&
                   Approximately(profile.BlinkIntervalMean, archetype.BlinkIntervalMean) &&
                   Approximately(profile.FaceScanRadiusDegrees, archetype.FaceScanRadiusDegrees) &&
                   Approximately(profile.PlanningBreakProbability, archetype.PlanningBreakProbability);
        }

        // ------------------------------------------------------------------ helpers

        private static bool TryFindPolicy(
            ConvaiGazeProfile profile, DialogueState state, out GazeStatePolicy result)
        {
            for (int i = 0; i < profile.StatePolicies.Count; i++)
            {
                if (profile.StatePolicies[i].State != state) continue;
                result = profile.StatePolicies[i];
                return true;
            }

            result = default;
            return false;
        }

        private static bool Approximately(float a, float b) => Mathf.Abs(a - b) < 0.005f;

        /// <summary>
        ///     Mean of a per-state field across the conversational rows, normalized into 0..1
        ///     against the dial's authored bounds. Idle is excluded — see the note in
        ///     <see cref="Read(SerializedObject, GazePersonalityDial)" />.
        /// </summary>
        private static float AverageConversationalRow(
            SerializedObject serialized, string relativeField, float floor, float ceiling)
        {
            SerializedProperty policies = GazeProfileSerializedPaths.Find(serialized, "statePolicies");
            if (policies == null || !policies.isArray || policies.arraySize == 0) return 0.5f;

            float total = 0f;
            int counted = 0;
            for (int i = 0; i < policies.arraySize; i++)
            {
                SerializedProperty element = policies.GetArrayElementAtIndex(i);
                SerializedProperty state = element.FindPropertyRelative("State");
                if (state != null && state.enumValueIndex == (int)DialogueState.Idle)
                    continue;

                SerializedProperty value = element.FindPropertyRelative(relativeField);
                if (value == null) continue;

                total += value.floatValue;
                counted++;
            }

            if (counted == 0) return 0.5f;
            return Normalize(total / counted, floor, ceiling);
        }

        private static void SetConversationalRows(SerializedObject serialized, string relativeField, float value)
        {
            SerializedProperty policies = GazeProfileSerializedPaths.Find(serialized, "statePolicies");
            if (policies == null || !policies.isArray) return;

            for (int i = 0; i < policies.arraySize; i++)
            {
                SerializedProperty element = policies.GetArrayElementAtIndex(i);
                SerializedProperty state = element.FindPropertyRelative("State");
                if (state != null && state.enumValueIndex == (int)DialogueState.Idle)
                    continue;

                SerializedProperty target = element.FindPropertyRelative(relativeField);
                if (target != null) target.floatValue = value;
            }
        }

        private static float FloatOf(SerializedObject serialized, string field, float fallback)
        {
            SerializedProperty property = GazeProfileSerializedPaths.Find(serialized, field);
            return property != null ? property.floatValue : fallback;
        }

        private static void SetFloat(SerializedObject serialized, string field, float value)
        {
            SerializedProperty property = GazeProfileSerializedPaths.Find(serialized, field);
            if (property != null) property.floatValue = value;
        }

        private static float Normalize(float value, float floor, float ceiling) =>
            Mathf.Approximately(ceiling, floor) ? 0.5f : Mathf.Clamp01((value - floor) / (ceiling - floor));

        /// <summary>Normalizes a field whose SMALLER values mean MORE of the dial (intervals, thresholds).</summary>
        private static float InverseNormalize(float value, float atZero, float atOne) =>
            Mathf.Approximately(atOne, atZero) ? 0.5f : Mathf.Clamp01((value - atZero) / (atOne - atZero));
    }
}
