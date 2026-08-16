using System;
using System.Collections.Generic;
using System.IO;
using Convai.Domain.Logging;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     Everything <see cref="BodyAnimationSetBuilder.Build" /> needs: the target set (or where to
    ///     create one) and the reviewer-confirmed clip proposals to apply.
    /// </summary>
    internal struct BodyAnimationSetBuildRequest
    {
        /// <summary>An existing set to add content to, or <c>null</c> to create a new one.</summary>
        internal ConvaiBodyAnimationSet ExistingSet;

        /// <summary>Project-relative asset path for a newly created set. Ignored when <see cref="ExistingSet" /> is set.</summary>
        internal string NewSetAssetPath;

        /// <summary>Display name written onto the set. Left unchanged when null/blank.</summary>
        internal string DisplayName;

        /// <summary>Every reviewed proposal — only entries with <c>Included == true</c> are written.</summary>
        internal IReadOnlyList<BodyAnimationClipProposal> Proposals;
    }

    /// <summary>
    ///     Applies confirmed <see cref="BodyAnimationClipProposal" /> rows to a
    ///     <see cref="ConvaiBodyAnimationSet" />. This is the one place a wizard-built set is ever
    ///     written, and it is deliberately the FULL pipeline in one call — write content, generate the
    ///     upper-body mask when the content needs one, then run the Clip Motion Analyzer — so a set
    ///     built through here can never end up in the "user forgot to run the analyzer" state that
    ///     silently produces sliding feet and dead directional starts.
    /// </summary>
    internal static class BodyAnimationSetBuilder
    {
        /// <summary>
        ///     Builds or extends an animation set from <paramref name="request" />'s confirmed
        ///     proposals. Every step appends a plain-language line to <paramref name="report" />, so a
        ///     caller can show exactly what happened without inspecting the asset. Returns the built
        ///     set, or <c>null</c> if it could not be resolved or created (see the report for why).
        /// </summary>
        internal static ConvaiBodyAnimationSet Build(BodyAnimationSetBuildRequest request, List<string> report)
        {
            report ??= new List<string>();

            if (request.Proposals == null || request.Proposals.Count == 0)
            {
                report.Add("No clip proposals were supplied — nothing to build.");
                return request.ExistingSet;
            }

            ConvaiBodyAnimationSet set = ResolveTargetSet(request, report);
            if (set == null) return null;

            // A whole-content rewrite of one ScriptableObject asset: Undo.RecordObject captures the
            // pre-write serialized snapshot, and every SerializedProperty write below is folded into
            // that same undo step once ApplyModifiedProperties commits it.
            Undo.RecordObject(set, "Build Animation Set");
            var serialized = new SerializedObject(set);

            WriteDisplayName(serialized, request.DisplayName);
            int idleCount = AppendPool(serialized.FindProperty("_idles"), request.Proposals, BodyAnimationSlotCategory.Idle, report, "idle");
            int talkCount = AppendPool(serialized.FindProperty("_talks"), request.Proposals, BodyAnimationSlotCategory.Talk, report, "talk");
            int locomotionCount = WriteLocomotion(serialized.FindProperty("_locomotion"), request.Proposals, report);
            int pointingCount = WritePointing(serialized.FindProperty("_pointing")?.FindPropertyRelative("_entries"), request.Proposals, report);
            int actionCount = AppendActions(serialized.FindProperty("_actions"), request.Proposals, report);

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            report.Add(
                $"Wrote {idleCount} idle, {talkCount} talk, {locomotionCount} locomotion, " +
                $"{pointingCount} pointing, and {actionCount} action entr{(actionCount == 1 ? "y" : "ies")}.");

            // The structural fix: every set built here is measured before it is ever handed
            // back, so there is no "remember to run the analyzer" step left for the user to forget.
            EnsureUpperBodyMask(set, report);
            int analyzed = ClipMotionAnalyzer.AnalyzeSet(set, confirm: false);
            if (analyzed > 0)
                report.Add($"Measured {analyzed} locomotion clip(s) for zero-slide NavMesh sync.");

            AppendValidationFindings(set, report);
            report.Add(BodyAnimationFixes.DescribeLocomotionCoverage(set));

            ConvaiLogger.Info(
                $"[BodyAnimationSetBuilder] Built '{set.DisplayName}' from {request.Proposals.Count} clip " +
                $"proposal(s) — {locomotionCount}/26 locomotion slots filled.",
                LogCategory.Animation);

            return set;
        }

        // ------------------------------------------------------------------ set resolution

        private static ConvaiBodyAnimationSet ResolveTargetSet(BodyAnimationSetBuildRequest request, List<string> report)
        {
            if (request.ExistingSet != null) return request.ExistingSet;

            if (string.IsNullOrEmpty(request.NewSetAssetPath))
            {
                report.Add("No existing set and no asset path were supplied — cannot create a set.");
                return null;
            }

            var existingAtPath = AssetDatabase.LoadAssetAtPath<ConvaiBodyAnimationSet>(request.NewSetAssetPath);
            if (existingAtPath != null) return existingAtPath;

            EnsureFolderExists(Path.GetDirectoryName(request.NewSetAssetPath)?.Replace('\\', '/'));
            var created = ScriptableObject.CreateInstance<ConvaiBodyAnimationSet>();
            AssetDatabase.CreateAsset(created, request.NewSetAssetPath);
            report.Add($"Created a new animation set at '{request.NewSetAssetPath}'.");
            return created;
        }

        private static void EnsureFolderExists(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolderExists(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // ------------------------------------------------------------------ content writers

        private static void WriteDisplayName(SerializedObject serialized, string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return;
            SerializedProperty property = serialized.FindProperty("_displayName");
            if (property != null) property.stringValue = displayName;
        }

        /// <summary>Appends confirmed clips for one Idle/Talk pool. Skips a clip already present so re-running a build stays idempotent.</summary>
        private static int AppendPool(
            SerializedProperty poolProperty,
            IReadOnlyList<BodyAnimationClipProposal> proposals,
            BodyAnimationSlotCategory category,
            List<string> report,
            string label)
        {
            if (poolProperty == null) return 0;

            var existingClips = new HashSet<AnimationClip>();
            for (int i = 0; i < poolProperty.arraySize; i++)
            {
                var clip = poolProperty.GetArrayElementAtIndex(i).FindPropertyRelative("_clip").objectReferenceValue as AnimationClip;
                if (clip != null) existingClips.Add(clip);
            }

            int added = 0;
            for (int i = 0; i < proposals.Count; i++)
            {
                BodyAnimationClipProposal proposal = proposals[i];
                if (!proposal.Included || proposal.Category != category || proposal.Clip == null) continue;
                if (!existingClips.Add(proposal.Clip))
                {
                    report.Add($"Skipped duplicate {label} clip '{proposal.Clip.name}' — already in the pool.");
                    continue;
                }

                SerializedProperty element = AppendElement(poolProperty);
                // Every field the entry actually reads is set explicitly rather than trusting
                // whatever a freshly grown array element defaults to (Unity does not always honor a
                // [Serializable] type's C# field initializers for array-grown elements).
                element.FindPropertyRelative("_clip").objectReferenceValue = proposal.Clip;
                element.FindPropertyRelative("_weight").floatValue = 1f;
                element.FindPropertyRelative("_affinities")?.ClearArray();

                if (category == BodyAnimationSlotCategory.Talk)
                {
                    element.FindPropertyRelative("_coverage").enumValueIndex = (int)BodyCoverage.UpperBody;
                    element.FindPropertyRelative("_additive").boolValue = false;
                    element.FindPropertyRelative("_additiveClip").objectReferenceValue = null;
                    element.FindPropertyRelative("_introClip").objectReferenceValue = null;
                    element.FindPropertyRelative("_outroClip").objectReferenceValue = null;
                    element.FindPropertyRelative("_useSafeReleaseWindow").boolValue = false;
                    element.FindPropertyRelative("_fragments")?.ClearArray();
                }

                added++;
            }

            return added;
        }

        /// <summary>Writes each confirmed locomotion proposal into its named slot on <see cref="LocomotionSection" />. A slot targeted twice keeps the last write.</summary>
        private static int WriteLocomotion(
            SerializedProperty locomotionProperty,
            IReadOnlyList<BodyAnimationClipProposal> proposals,
            List<string> report)
        {
            if (locomotionProperty == null) return 0;

            int written = 0;
            for (int i = 0; i < proposals.Count; i++)
            {
                BodyAnimationClipProposal proposal = proposals[i];
                if (!proposal.Included || proposal.Category != BodyAnimationSlotCategory.Locomotion || proposal.Clip == null)
                    continue;

                string fieldName = LocomotionFieldName(proposal.LocomotionSlot);
                SerializedProperty slotProperty = fieldName != null ? locomotionProperty.FindPropertyRelative(fieldName) : null;
                if (slotProperty == null)
                {
                    report.Add($"Could not resolve the '{proposal.LocomotionSlot}' locomotion field — skipped '{proposal.Clip.name}'.");
                    continue;
                }

                SerializedProperty clipProperty = slotProperty.FindPropertyRelative("_clip");
                var previousClip = clipProperty.objectReferenceValue as AnimationClip;
                if (previousClip != null && previousClip != proposal.Clip)
                    report.Add($"Overwrote '{proposal.LocomotionSlot}' — was '{previousClip.name}', now '{proposal.Clip.name}'.");

                clipProperty.objectReferenceValue = proposal.Clip;
                written++;
            }

            return written;
        }

        /// <summary>Appends confirmed pointing directions. Skips a clip already present so re-running a build stays idempotent.</summary>
        private static int WritePointing(
            SerializedProperty entriesProperty,
            IReadOnlyList<BodyAnimationClipProposal> proposals,
            List<string> report)
        {
            if (entriesProperty == null) return 0;

            var existingClips = new HashSet<AnimationClip>();
            for (int i = 0; i < entriesProperty.arraySize; i++)
            {
                var clip = entriesProperty.GetArrayElementAtIndex(i).FindPropertyRelative("_clip").objectReferenceValue as AnimationClip;
                if (clip != null) existingClips.Add(clip);
            }

            int written = 0;
            for (int i = 0; i < proposals.Count; i++)
            {
                BodyAnimationClipProposal proposal = proposals[i];
                if (!proposal.Included || proposal.Category != BodyAnimationSlotCategory.Pointing || proposal.Clip == null)
                    continue;
                if (!existingClips.Add(proposal.Clip))
                {
                    report.Add($"Skipped duplicate pointing clip '{proposal.Clip.name}' — already in the set.");
                    continue;
                }

                SerializedProperty element = AppendElement(entriesProperty);
                element.FindPropertyRelative("_clip").objectReferenceValue = proposal.Clip;
                element.FindPropertyRelative("_yawDegrees").floatValue = proposal.PointingYaw;
                element.FindPropertyRelative("_pitchDegrees").floatValue = proposal.PointingPitch;
                written++;
            }

            return written;
        }

        /// <summary>Appends confirmed action proposals. A name collision (case/separator-insensitive, matching <see cref="ActionEntry.NormalizeName" />) is skipped and reported rather than silently overwritten.</summary>
        private static int AppendActions(
            SerializedProperty actionsProperty,
            IReadOnlyList<BodyAnimationClipProposal> proposals,
            List<string> report)
        {
            if (actionsProperty == null) return 0;

            var existingNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < actionsProperty.arraySize; i++)
            {
                string name = actionsProperty.GetArrayElementAtIndex(i).FindPropertyRelative("_actionName").stringValue;
                if (!string.IsNullOrEmpty(name)) existingNames.Add(ActionEntry.NormalizeName(name));
            }

            int added = 0;
            for (int i = 0; i < proposals.Count; i++)
            {
                BodyAnimationClipProposal proposal = proposals[i];
                if (!proposal.Included || proposal.Category != BodyAnimationSlotCategory.Action || proposal.Clip == null)
                    continue;

                string actionName = string.IsNullOrWhiteSpace(proposal.ActionName) ? proposal.Clip.name : proposal.ActionName;
                string normalizedName = ActionEntry.NormalizeName(actionName);
                if (string.IsNullOrEmpty(normalizedName) || !existingNames.Add(normalizedName))
                {
                    report.Add($"Skipped action '{actionName}' — name is empty or already used by another entry.");
                    continue;
                }

                SerializedProperty element = AppendElement(actionsProperty);
                element.FindPropertyRelative("_actionName").stringValue = actionName;

                SerializedProperty aliasesProperty = element.FindPropertyRelative("_aliases");
                aliasesProperty.ClearArray();
                if (proposal.ActionAliases != null)
                {
                    for (int a = 0; a < proposal.ActionAliases.Length; a++)
                    {
                        aliasesProperty.InsertArrayElementAtIndex(a);
                        aliasesProperty.GetArrayElementAtIndex(a).stringValue = proposal.ActionAliases[a];
                    }
                }

                element.FindPropertyRelative("_clip").objectReferenceValue = proposal.Clip;
                element.FindPropertyRelative("_introClip").objectReferenceValue = null;
                element.FindPropertyRelative("_outroClip").objectReferenceValue = null;
                element.FindPropertyRelative("_maskMode").enumValueIndex = (int)proposal.ActionMaskMode;
                element.FindPropertyRelative("_customMask").objectReferenceValue = null;
                element.FindPropertyRelative("_loopMode").enumValueIndex = (int)proposal.ActionLoopMode;
                element.FindPropertyRelative("_loopCount").intValue = 1;
                element.FindPropertyRelative("_speed").floatValue = 1f;
                element.FindPropertyRelative("_targetWeight").floatValue = 1f;
                element.FindPropertyRelative("_suspendsLocomotion").boolValue = proposal.ActionMaskMode == ActionMaskMode.FullBody;
                element.FindPropertyRelative("_interruptible").boolValue = true;
                element.FindPropertyRelative("_fadeInSecondsOverride").floatValue = -1f;
                element.FindPropertyRelative("_fadeOutSecondsOverride").floatValue = -1f;
                element.FindPropertyRelative("_cue").enumValueIndex = (int)proposal.ActionCue;
                element.FindPropertyRelative("_allowConversationOverlays").boolValue = false;
                element.FindPropertyRelative("_ambient").boolValue = false;
                // _anchorOptions is left at whatever the array-insert produced: every one of its
                // fields is clamped to a safe minimum at the property getter (see
                // ActionAnchorOptions), so a zero-valued fresh element is inert, not broken, and it
                // only matters at all once a caller opts an action into PlayActionAt anchor alignment.

                added++;
            }

            return added;
        }

        private static SerializedProperty AppendElement(SerializedProperty arrayProperty)
        {
            int index = arrayProperty.arraySize;
            arrayProperty.InsertArrayElementAtIndex(index);
            return arrayProperty.GetArrayElementAtIndex(index);
        }

        private static string LocomotionFieldName(BodyAnimationLocomotionSlot slot) => slot switch
        {
            BodyAnimationLocomotionSlot.Walk => "_walk",
            BodyAnimationLocomotionSlot.Jog => "_jog",
            BodyAnimationLocomotionSlot.WalkStartForward => "_walkStartForward",
            BodyAnimationLocomotionSlot.WalkStart90Left => "_walkStart90Left",
            BodyAnimationLocomotionSlot.WalkStart90Right => "_walkStart90Right",
            BodyAnimationLocomotionSlot.WalkStart180Left => "_walkStart180Left",
            BodyAnimationLocomotionSlot.WalkStart180Right => "_walkStart180Right",
            BodyAnimationLocomotionSlot.JogStartForward => "_jogStartForward",
            BodyAnimationLocomotionSlot.JogStart90Left => "_jogStart90Left",
            BodyAnimationLocomotionSlot.JogStart90Right => "_jogStart90Right",
            BodyAnimationLocomotionSlot.JogStart180Left => "_jogStart180Left",
            BodyAnimationLocomotionSlot.JogStart180Right => "_jogStart180Right",
            BodyAnimationLocomotionSlot.WalkStopLeftPlant => "_walkStopLeftPlant",
            BodyAnimationLocomotionSlot.WalkStopRightPlant => "_walkStopRightPlant",
            BodyAnimationLocomotionSlot.WalkStopLowSpeed => "_walkStopLowSpeed",
            BodyAnimationLocomotionSlot.WalkStopAbrupt => "_walkStopAbrupt",
            BodyAnimationLocomotionSlot.JogStopLeftPlant => "_jogStopLeftPlant",
            BodyAnimationLocomotionSlot.JogStopAbrupt => "_jogStopAbrupt",
            BodyAnimationLocomotionSlot.WalkToJogLeft => "_walkToJogLeft",
            BodyAnimationLocomotionSlot.WalkToJogRight => "_walkToJogRight",
            BodyAnimationLocomotionSlot.JogToWalkLeft => "_jogToWalkLeft",
            BodyAnimationLocomotionSlot.JogToWalkRight => "_jogToWalkRight",
            BodyAnimationLocomotionSlot.Turn90Left => "_turn90Left",
            BodyAnimationLocomotionSlot.Turn90Right => "_turn90Right",
            BodyAnimationLocomotionSlot.Turn180Left => "_turn180Left",
            BodyAnimationLocomotionSlot.Turn180Right => "_turn180Right",
            _ => null
        };

        // ------------------------------------------------------------------ post-build safety net

        /// <summary>Generates the upper-body overlay mask when the built content needs one and the set does not already carry one. Reuses <see cref="BodyAnimationFixes.GenerateUpperBodyMask" /> rather than a second mask builder.</summary>
        private static void EnsureUpperBodyMask(ConvaiBodyAnimationSet set, List<string> report)
        {
            if (set.UpperBodyMask != null) return;

            bool needsMask = set.HasAnyTalk || set.HasAnyListen || set.HasAnyThink || set.Pointing.HasAny || AnyUpperBodyAction(set);
            if (!needsMask) return;

            AvatarMask mask = BodyAnimationFixes.GenerateUpperBodyMask(set);
            if (mask != null) report.Add("Generated the upper-body overlay mask.");
        }

        private static bool AnyUpperBodyAction(ConvaiBodyAnimationSet set)
        {
            IReadOnlyList<ActionEntry> actions = set.Actions;
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] != null && actions[i].MaskMode == ActionMaskMode.UpperBody) return true;
            }
            return false;
        }

        private static void AppendValidationFindings(ConvaiBodyAnimationSet set, List<string> report)
        {
            var issuesScratch = new List<string>();
            var findings = new List<BodyAnimationTroubleshooterFinding>();
            BodyAnimationTroubleshooter.EvaluateSetAsset(set, issuesScratch, findings);

            for (int i = 0; i < findings.Count; i++)
            {
                if (findings[i].Severity > BodyAnimationTroubleshooterSeverity.Ok)
                    report.Add($"[{findings[i].Severity}] {findings[i].Title}: {findings[i].Message}");
            }
        }
    }
}
