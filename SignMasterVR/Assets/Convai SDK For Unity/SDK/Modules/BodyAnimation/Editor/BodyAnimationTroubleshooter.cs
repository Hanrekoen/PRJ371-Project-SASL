using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Modules.BodyAnimation.Components;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.Embodiment;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>Severity of a single Body Animation Troubleshooter finding.</summary>
    internal enum BodyAnimationTroubleshooterSeverity
    {
        Ok,
        Info,
        Warning,
        Error
    }

    /// <summary>
    ///     A repair the editor can perform on the user's behalf for a finding that has a purely
    ///     mechanical fix. <see cref="None" /> means the finding needs a human decision (choosing
    ///     content, re-authoring a rig) and is reported without a button.
    /// </summary>
    internal enum BodyAnimationFixId
    {
        /// <summary>No mechanical repair exists; the finding is informational or needs a human.</summary>
        None = 0,

        /// <summary>Assign the SDK's shipped default Body Animation Profile to the controller.</summary>
        AssignDefaultContent,

        /// <summary>Add a <c>ConvaiNavMeshLocomotion</c> so the character can walk.</summary>
        AddMovement,

        /// <summary>Run the Clip Motion Analyzer over the resolved set.</summary>
        AnalyzeClipMetadata,

        /// <summary>Build and assign the set's upper-body overlay mask.</summary>
        GenerateUpperBodyMask,

        /// <summary>Clear the redundant Animator Controller the PlayableGraph already overrides.</summary>
        ClearAnimatorController,

        /// <summary>Turn on beat gestures, so the set's Beat/Emphatic-tagged clips are actually played.</summary>
        EnableBeatGestures,

        /// <summary>Turn on ambient activities, so the set's Ambient-tagged clips are actually played.</summary>
        EnableAmbientActivities,

        /// <summary>Turn on referential gestures, so the set's referential-tagged clips are actually played.</summary>
        EnableReferentialGestures
    }

    /// <summary>
    ///     One actionable finding about a character's body animation setup — the single model every
    ///     surface renders: the Troubleshooter window, the controller inspector, and the animation
    ///     set inspector. Before this existed each surface carried its own severity ladder and its
    ///     own presentation, so the same problem read three different ways.
    /// </summary>
    internal struct BodyAnimationTroubleshooterFinding
    {
        /// <summary>How serious the finding is.</summary>
        public BodyAnimationTroubleshooterSeverity Severity;

        /// <summary>
        ///     Stable, code-facing identifier (e.g. <c>rig.not-humanoid</c>). Never localized and
        ///     never shown to the user — it exists so a surface can attach behavior to a specific
        ///     finding, and so tests can assert on a finding without matching display text.
        /// </summary>
        public string Id;

        /// <summary>Short label, e.g. "Animation Set".</summary>
        public string Title;

        /// <summary>Actionable message stating the consequence and the fix.</summary>
        public string Message;

        /// <summary>The repair a surface may offer as a one-click button, when one exists.</summary>
        public BodyAnimationFixId Fix;
    }

    /// <summary>Stable <see cref="BodyAnimationTroubleshooterFinding.Id" /> values.</summary>
    internal static class BodyAnimationFindingIds
    {
        public const string NoAnimator = "rig.no-animator";
        public const string NotHumanoid = "rig.not-humanoid";
        public const string RedundantAnimatorController = "rig.redundant-animator-controller";
        public const string ApplyRootMotion = "rig.apply-root-motion";
        public const string NoSet = "content.no-set";
        public const string SetValid = "content.set-valid";
        public const string SetIssue = "content.set-issue";
        public const string NoConfig = "content.no-config";
        public const string NoProfile = "content.no-profile";
        public const string NoTalk = "content.no-talk";
        public const string NoListen = "content.no-listen";
        public const string NoThink = "content.no-think";
        public const string NoBeatGesture = "content.no-beat-gesture";
        public const string NoTalkFragments = "content.no-talk-fragments";
        public const string NoUpperBodyMask = "content.no-upper-body-mask";
        public const string MissingClipMetadata = "content.missing-clip-metadata";
        public const string RigMotionScaleCalibrated = "rig.motion-scale-calibrated";
        public const string LocomotionProviderInvalid = "locomotion.provider-invalid";
        public const string LocomotionProviderValid = "locomotion.provider-valid";
        public const string LocomotionNotManaged = "locomotion.not-managed";
        public const string DormantBeatContent = "content.dormant-beat";
        public const string DormantAmbientContent = "content.dormant-ambient";
        public const string DormantReferentialContent = "content.dormant-referential";
        public const string SingleIdleVariant = "content.single-idle-variant";
        public const string SingleTalkVariant = "content.single-talk-variant";
        public const string NoEmotionAffinities = "content.no-emotion-affinities";
    }

    /// <summary>
    ///     Everything the Body Animation Troubleshooter needs to evaluate a character's setup. Gathered
    ///     from the scene/asset state so <see cref="BodyAnimationTroubleshooter.Evaluate" /> stays a
    ///     pure, testable function.
    /// </summary>
    internal struct BodyAnimationTroubleshooterInput
    {
        /// <summary>Whether an Animator was found under the character root.</summary>
        public bool HasAnimator;

        /// <summary>Whether the Animator has a valid Humanoid avatar.</summary>
        public bool IsHumanoid;

        /// <summary>Whether the Animator has a (redundant, overridden) Animator Controller assigned.</summary>
        public bool HasAnimatorController;

        /// <summary>Whether the Animator has Apply Root Motion enabled (auto-disabled at runtime).</summary>
        public bool ApplyRootMotion;

        /// <summary>Whether a Body Animation Profile asset is assigned.</summary>
        public bool HasProfileAsset;

        /// <summary>Whether an animation set is resolved (directly or via the profile).</summary>
        public bool HasSetAssigned;

        /// <summary>Whether a config asset is resolved (directly or via the profile).</summary>
        public bool HasConfigAssigned;

        /// <summary>True when the resolved set has at least one valid idle entry.</summary>
        public bool HasAnyIdle;

        /// <summary>True when the resolved set has at least one valid talk entry.</summary>
        public bool HasAnyTalk;

        /// <summary>True when the resolved set has at least one valid listen entry.</summary>
        public bool HasAnyListen;

        /// <summary>True when the resolved set has at least one valid think entry.</summary>
        public bool HasAnyThink;

        /// <summary>True when at least one action is tagged Beat or Emphatic.</summary>
        public bool HasBeatGesture;
        public bool AdvancedCoSpeechEnabled;
        public bool HasTalkFragments;
        public bool HasCustomLocomotionProvider;
        public bool HasValidLocomotionSource;
        public bool HasLocomotionCommands;
        public bool HasManagedLocomotion;
        public bool HasAnchorAlignment;

        /// <summary>True when the resolved set has an upper-body overlay mask assigned.</summary>
        public bool HasUpperBodyMask;

        /// <summary>
        ///     True when the set actually needs that mask — it authors talk/listen/think, pointing,
        ///     or upper-body actions. A locomotion-only set does not, so the absence is not a fault.
        /// </summary>
        public bool NeedsUpperBodyMask;

        /// <summary>
        ///     How many assigned locomotion clips have no measured ground speed. Non-zero means the
        ///     Clip Motion Analyzer has not been run (or was run before clips changed), which is the
        ///     usual cause of sliding feet.
        /// </summary>
        public int LocomotionClipsMissingMetadata;

        /// <summary>Issues surfaced by <see cref="ConvaiBodyAnimationSet.CollectIssues" />; null when no set is resolved.</summary>
        public List<string> SetIssues;

        /// <summary>
        ///     The same set issues as <see cref="SetIssues" />, but as the typed findings
        ///     <see cref="ConvaiBodyAnimationSet.CollectFindings" /> raises — carries each
        ///     finding's stable id. Preferred by <see cref="EvaluateSet" /> when
        ///     present; <see cref="SetIssues" /> remains the fallback for callers that build the
        ///     input by hand.
        /// </summary>
        public List<BodyAnimationFinding> SetFindings;

        /// <summary>
        ///     The rig motion scale that would be resolved for this character (1 = no
        ///     correction, the common case). Computed the same way the runtime does, from the
        ///     walk clip's authored motion scale, via <c>MotionScaleResolver</c>.
        /// </summary>
        public float RigMotionScale;

        /// <summary>
        ///     What this (set, config) pair can actually perform: which switch-backed features have
        ///     content, which content sits behind a switch that is off, and how much variety the
        ///     pools carry. Computed with the same pure function the runtime uses, so the
        ///     Troubleshooter and the running character can never disagree about it.
        /// </summary>
        public BodyAnimationFeatureAvailability FeatureAvailability;
    }

    /// <summary>
    ///     Evaluates the setup gathered from a character's rig, content assignment, and set
    ///     authoring into actionable findings for the Body Animation Troubleshooter window — "what did
    ///     the body animation stack actually resolve on THIS character?"
    /// </summary>
    internal static class BodyAnimationTroubleshooter
    {
        /// <summary>
        ///     Evaluates <paramref name="input" /> into <paramref name="results" /> (cleared
        ///     first). Pure and allocation-free beyond the list itself, so it is directly
        ///     unit-testable without a scene.
        /// </summary>
        internal static void Evaluate(in BodyAnimationTroubleshooterInput input, List<BodyAnimationTroubleshooterFinding> results)
        {
            results.Clear();

            if (!input.HasAnimator)
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Error,
                    BodyAnimationFindingIds.NoAnimator, "Animator",
                    "No Animator was found under the character root — body animation needs a Humanoid " +
                    "Animator to build its PlayableGraph, so the module stays inactive. Add an Animator " +
                    "with a Humanoid avatar.");
                return;
            }

            if (!input.IsHumanoid)
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Error,
                    BodyAnimationFindingIds.NotHumanoid, "Humanoid Avatar",
                    "The Animator's avatar is not a valid Humanoid rig — body animation cannot resolve a " +
                    "standard bone mapping. Set the model's Animation Type to Humanoid in its import settings.");
                return;
            }

            if (input.HasAnimatorController)
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Info,
                    BodyAnimationFindingIds.RedundantAnimatorController, "Animator Controller",
                    "An Animator Controller is assigned; the body animation PlayableGraph replaces its " +
                    "output while active. Remove it to avoid confusion, or leave it as an inert fallback.",
                    BodyAnimationFixId.ClearAnimatorController);
            }

            if (input.ApplyRootMotion)
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Info,
                    BodyAnimationFindingIds.ApplyRootMotion, "Apply Root Motion",
                    "Apply Root Motion is enabled on the Animator; the controller disables it automatically " +
                    "at runtime because the module drives root displacement and rotation itself.");
            }

            if (!input.HasSetAssigned)
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Error,
                    BodyAnimationFindingIds.NoSet, "Animation Set",
                    "This character has no Animation Set, so it stays still. Assign one to the " +
                    "component or to its profile — or, if this project has no animation clips yet, " +
                    "import the Convai samples or build a set from your own clip folder with " +
                    "Create Animation Set in the Body Animation editor.",
                    BodyAnimationFixId.AssignDefaultContent);
            }
            else
            {
                EvaluateSet(in input, results);
            }

            if (!input.HasConfigAssigned)
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Info,
                    BodyAnimationFindingIds.NoConfig, "Config",
                    "No config asset is assigned (directly or via a profile) — SDK runtime defaults are " +
                    "used. Assign a Body Animation Config to tune transitions and behavior.");
            }

            if (!input.HasProfileAsset)
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Info,
                    BodyAnimationFindingIds.NoProfile, "Profile",
                    "No profile asset is assigned — the controller's direct Animation Set/Config fields " +
                    "are used as-is. Assign a Body Animation Profile to bundle content per character " +
                    "archetype.");
            }

            if (input.HasCustomLocomotionProvider)
            {
                if (!input.HasValidLocomotionSource)
                {
                    Add(results, BodyAnimationTroubleshooterSeverity.Error,
                        BodyAnimationFindingIds.LocomotionProviderInvalid, "Locomotion Provider",
                        "The override does not implement IConvaiLocomotionSource, so locomotion animation is disabled.");
                }
                else
                {
                    Add(results, BodyAnimationTroubleshooterSeverity.Ok,
                        BodyAnimationFindingIds.LocomotionProviderValid, "Locomotion Provider",
                        "Custom locomotion source is valid. Optional capabilities: " +
                        $"commands={(input.HasLocomotionCommands ? "yes" : "no")}, " +
                        $"managed sync={(input.HasManagedLocomotion ? "yes" : "no")}, " +
                        $"anchor alignment={(input.HasAnchorAlignment ? "yes" : "no")}.");
                    if (!input.HasManagedLocomotion)
                        Add(results, BodyAnimationTroubleshooterSeverity.Warning,
                            BodyAnimationFindingIds.LocomotionNotManaged, "Advanced Locomotion",
                            "Managed locomotion capability is absent; starts, planted stops, and animation-slaved turns degrade to simple blending.");
                }
            }
        }

        private static void EvaluateSet(in BodyAnimationTroubleshooterInput input, List<BodyAnimationTroubleshooterFinding> results)
        {
            // Prefer the typed findings — each carries its own stable id from
            // ConvaiBodyAnimationSet.CollectFindings — over the flattened string list, which
            // exists only for callers that hand-build an input without going through
            // CollectFindings (e.g. tests). Severity here stays Warning either way: this
            // surface's own severity ladder (Ok/Info/Warning/Error) predates the finding model
            // and changing it is out of scope for this pass (no behaviour change).
            if (input.SetFindings != null && input.SetFindings.Count > 0)
            {
                for (int i = 0; i < input.SetFindings.Count; i++)
                    Add(results, BodyAnimationTroubleshooterSeverity.Warning,
                        input.SetFindings[i].Id, "Set Issue", input.SetFindings[i].Message);
            }
            else if (input.SetIssues != null && input.SetIssues.Count > 0)
            {
                for (int i = 0; i < input.SetIssues.Count; i++)
                    Add(results, BodyAnimationTroubleshooterSeverity.Warning,
                        BodyAnimationFindingIds.SetIssue, "Set Issue", input.SetIssues[i]);
            }
            else
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Ok,
                    BodyAnimationFindingIds.SetValid, "Animation Set",
                    "Set content validated cleanly — no authoring issues found.");
            }

            // The two set problems with a purely mechanical repair get their own findings rather
            // than hiding inside the generic issue list, so a surface can offer a Fix button
            // without parsing the issue text.
            if (input.NeedsUpperBodyMask && !input.HasUpperBodyMask)
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Error,
                    BodyAnimationFindingIds.NoUpperBodyMask, "Overlay Mask",
                    "The set has talk, pointing, or upper-body gesture content but no upper-body overlay " +
                    "mask, so those layers would drive the FULL skeleton and fight locomotion. A standard " +
                    "mask can be generated for you.",
                    BodyAnimationFixId.GenerateUpperBodyMask);
            }

            if (input.LocomotionClipsMissingMetadata > 0)
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Warning,
                    BodyAnimationFindingIds.MissingClipMetadata, "Clip Measurements",
                    $"{input.LocomotionClipsMissingMetadata} locomotion clip(s) have not been measured, so " +
                    "the character falls back to configured speeds instead of the clips' real ground speed " +
                    "— the usual cause of sliding feet. Directional starts and planted stops also stay off " +
                    "until their motion is measured.",
                    BodyAnimationFixId.AnalyzeClipMetadata);
            }

            // Nothing is wrong here — this just makes an automatic correction visible so it
            // is not mistaken for a bug when the user measures something. The common case (scale
            // 1, no correction) reports nothing, so this never adds noise to the shipped path.
            if (!Mathf.Approximately(input.RigMotionScale, 1f))
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Info,
                    BodyAnimationFindingIds.RigMotionScaleCalibrated, "Rig Scale",
                    $"This rig measures {input.RigMotionScale:F2}x the animation content's reference " +
                    "scale. Walk/jog speeds and stop distances are calibrated automatically.");
            }

            if (!input.HasAnyTalk)
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Warning,
                    BodyAnimationFindingIds.NoTalk, "Talk",
                    "No valid talk entry — the character stays in its idle pose while speaking (the talk " +
                    "layer has nothing to play). Add at least one looping talk clip.");
            }

            if (!input.HasAnyListen)
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Info,
                    BodyAnimationFindingIds.NoListen, "Listen",
                    "No Listen entries authored — listening acting is inactive (the layer releases " +
                    "instead of holding a listen pose while the player speaks). Add clips to enable it.");
            }

            if (!input.HasAnyThink)
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Info,
                    BodyAnimationFindingIds.NoThink, "Think",
                    "No Think entries authored — thinking acting is inactive (the layer releases instead " +
                    "of holding a think pose during the LLM-latency beat). Add clips to enable it.");
            }

            if (!input.HasBeatGesture)
            {
                Add(results, BodyAnimationTroubleshooterSeverity.Info,
                    BodyAnimationFindingIds.NoBeatGesture, "Beat Gestures",
                    "No action is tagged with the Beat or Emphatic conversational cue, so clip-backed " +
                    "speech-rhythm accents have nothing to play and the setting stays off. Tag a short " +
                    "additive accent clip to enable them.");
            }

            if (input.AdvancedCoSpeechEnabled && input.HasAnyTalk && !input.HasTalkFragments)
                Add(results, BodyAnimationTroubleshooterSeverity.Info,
                    BodyAnimationFindingIds.NoTalkFragments, "Talk Motion Phrases",
                    "Advanced co-speech is enabled but no talk motion phrases are authored. Bootstrap phrases, then preview and refine the safe boundaries to hide a limited loop.");

            EvaluateDormantContent(in input, results);
            EvaluateVariantCoverage(in input, results);
        }

        /// <summary>
        ///     The authoring mistake nothing else could catch: the set carries the clips, but the
        ///     setting that would play them is off, so the character silently ignores content the
        ///     user deliberately tagged. Reported as a warning (not info) precisely because the
        ///     user has already done the work and is owed the one click that completes it.
        /// </summary>
        private static void EvaluateDormantContent(
            in BodyAnimationTroubleshooterInput input, List<BodyAnimationTroubleshooterFinding> results)
        {
            BodyAnimationFeatureAvailability availability = input.FeatureAvailability;

            if (availability.BeatGestures.IsContentWithoutEnable)
                Add(results, BodyAnimationTroubleshooterSeverity.Warning,
                    BodyAnimationFindingIds.DormantBeatContent, "Beat Gestures",
                    "This set tags at least one action with the Beat or Emphatic cue, but Beat Gestures " +
                    "is turned off in the config — the clips are never played.",
                    BodyAnimationFixId.EnableBeatGestures);

            if (availability.AmbientActivities.IsContentWithoutEnable)
                Add(results, BodyAnimationTroubleshooterSeverity.Warning,
                    BodyAnimationFindingIds.DormantAmbientContent, "Ambient Activities",
                    "This set tags at least one action as Ambient, but Ambient Activities is turned off " +
                    "in the config — the character never performs them on its own.",
                    BodyAnimationFixId.EnableAmbientActivities);

            if (availability.ReferentialGestures.IsContentWithoutEnable)
                Add(results, BodyAnimationTroubleshooterSeverity.Warning,
                    BodyAnimationFindingIds.DormantReferentialContent, "Referential Gestures",
                    "This set tags at least one action with a referential cue (palm-to-player, " +
                    "hand-to-chest, indicate, enumerate), but Referential Gestures is turned off in the " +
                    "config — the clips are never played.",
                    BodyAnimationFixId.EnableReferentialGestures);
        }

        /// <summary>
        ///     Pool variety. A single-variant pool is a complete, working setup — but the settings
        ///     built to vary it (the idle variant interval, Calmness, Switch Talk Variant On Loop,
        ///     the talk-variant crossfade) then have nothing to act on, and nothing else in the
        ///     editor says so. Informational: this is guidance, not a defect.
        /// </summary>
        private static void EvaluateVariantCoverage(
            in BodyAnimationTroubleshooterInput input, List<BodyAnimationTroubleshooterFinding> results)
        {
            BodyAnimationFeatureAvailability availability = input.FeatureAvailability;

            if (availability.IdleVariantCount == 1)
                Add(results, BodyAnimationTroubleshooterSeverity.Info,
                    BodyAnimationFindingIds.SingleIdleVariant, "Idle Variety",
                    "Only one idle variant is authored, so the character always plays the same idle and " +
                    "the idle variant interval has nothing to swap to. Add a second looping idle to make " +
                    "standing still read as alive.");

            if (availability.TalkVariantCount == 1)
                Add(results, BodyAnimationTroubleshooterSeverity.Info,
                    BodyAnimationFindingIds.SingleTalkVariant, "Talk Variety",
                    "Only one talk variant is authored, so Switch Talk Variant On Loop has nothing to " +
                    "switch to and a long answer repeats one gesture loop. Authoring motion phrases on " +
                    "that clip hides the repetition; a second talk clip removes it.");

            if (!availability.HasEmotionAffinities && availability.IdleVariantCount + availability.TalkVariantCount > 1)
                Add(results, BodyAnimationTroubleshooterSeverity.Info,
                    BodyAnimationFindingIds.NoEmotionAffinities, "Emotion-Aware Selection",
                    "No idle or talk variant carries an emotion affinity, so variant selection is purely " +
                    "weighted and the character picks the same way whatever it feels. Add affinities to " +
                    "bias specific variants toward specific emotions.");
        }

        private static void Add(
            List<BodyAnimationTroubleshooterFinding> results,
            BodyAnimationTroubleshooterSeverity severity,
            string id,
            string title,
            string message,
            BodyAnimationFixId fix = BodyAnimationFixId.None)
        {
            results.Add(new BodyAnimationTroubleshooterFinding
            {
                Severity = severity,
                Id = id,
                Title = title,
                Message = message,
                Fix = fix
            });
        }

        /// <summary>
        ///     Gathers the setup state of <paramref name="controller" />'s character straight
        ///     from the scene/assets (editor-only; not part of the runtime assembly), also
        ///     resolving the set/animator the caller needs for live preview.
        /// </summary>
        internal static BodyAnimationTroubleshooterInput GatherFrom(
            ConvaiBodyAnimationController controller,
            SerializedProperty setProp,
            SerializedProperty configProp,
            SerializedProperty profileProp,
            SerializedProperty animatorOverrideProp,
            List<string> issuesScratch,
            out ConvaiBodyAnimationSet resolvedSet,
            out Animator resolvedAnimator)
        {
            return GatherFrom(controller, setProp, configProp, profileProp, animatorOverrideProp,
                null, issuesScratch, out resolvedSet, out resolvedAnimator);
        }

        internal static BodyAnimationTroubleshooterInput GatherFrom(
            ConvaiBodyAnimationController controller,
            SerializedProperty setProp,
            SerializedProperty configProp,
            SerializedProperty profileProp,
            SerializedProperty animatorOverrideProp,
            SerializedProperty locomotionProviderProp,
            List<string> issuesScratch,
            out ConvaiBodyAnimationSet resolvedSet,
            out Animator resolvedAnimator)
        {
            var input = new BodyAnimationTroubleshooterInput();
            resolvedSet = null;
            resolvedAnimator = null;
            if (controller == null) return input;

            Transform root = ResolveScanRoot(controller);
            var animatorOverride = animatorOverrideProp?.objectReferenceValue as Animator;
            Animator animator = animatorOverride != null ? animatorOverride : root.GetComponentInChildren<Animator>(true);
            resolvedAnimator = animator;

            input.HasAnimator = animator != null;
            input.IsHumanoid =
                animator != null && animator.avatar != null && animator.avatar.isValid && animator.isHuman;
            input.HasAnimatorController = animator != null && animator.runtimeAnimatorController != null;
            input.ApplyRootMotion = animator != null && animator.applyRootMotion;

            var provider = locomotionProviderProp?.objectReferenceValue as MonoBehaviour;
            input.HasCustomLocomotionProvider = provider != null;
            input.HasValidLocomotionSource = provider is Core.Locomotion.IConvaiLocomotionSource;
            input.HasLocomotionCommands = provider is Core.Locomotion.IConvaiLocomotionCommands;
            input.HasManagedLocomotion = provider is Core.Locomotion.IConvaiManagedLocomotion;
            input.HasAnchorAlignment = provider is Core.Locomotion.IConvaiAnchorAlignment;

            var profile = profileProp?.objectReferenceValue as ConvaiBodyAnimationProfile;
            input.HasProfileAsset = profile != null;

            var directSet = setProp?.objectReferenceValue as ConvaiBodyAnimationSet;
            var directConfig = configProp?.objectReferenceValue as ConvaiBodyAnimationConfig;

            ConvaiBodyAnimationSet set = profile != null && profile.AnimationSet != null
                ? profile.AnimationSet
                : directSet;
            ConvaiBodyAnimationConfig config = profile != null && profile.Config != null
                ? profile.Config
                : directConfig;

            resolvedSet = set;
            input.HasSetAssigned = set != null;
            input.HasConfigAssigned = config != null;
            input.AdvancedCoSpeechEnabled = config != null && config.EnableAdvancedCoSpeech;
            input.RigMotionScale = 1f;

            if (set != null)
            {
                input.HasAnyIdle = set.HasAnyIdle;
                input.HasAnyTalk = set.HasAnyTalk;
                input.HasAnyListen = set.HasAnyListen;
                input.HasAnyThink = set.HasAnyThink;
                input.HasBeatGesture = HasBeatGesture(set);
                input.HasTalkFragments = HasTalkFragments(set);
                input.HasUpperBodyMask = set.UpperBodyMask != null;
                input.NeedsUpperBodyMask = NeedsUpperBodyMask(set);
                input.LocomotionClipsMissingMetadata = CountLocomotionClipsMissingMetadata(set);

                if (animator != null && input.IsHumanoid)
                {
                    ClipMotionMetadata walkMeta = set.Locomotion.Walk.Metadata;
                    float authoredWalkScale =
                        walkMeta != null && walkMeta.HasAuthoredMotionScale ? walkMeta.AuthoredMotionScale : 0f;
                    input.RigMotionScale = Core.Locomotion.MotionScaleResolver.Resolve(
                        animator.humanScale, animator.transform.lossyScale, authoredWalkScale);
                }

                issuesScratch?.Clear();
                var findingsScratch = new List<BodyAnimationFinding>();
                set.CollectFindings(findingsScratch);
                for (int i = 0; i < findingsScratch.Count; i++)
                    issuesScratch?.Add(findingsScratch[i].Message);
                input.SetIssues = issuesScratch;
                input.SetFindings = findingsScratch;

                // Computed against the config that will actually be live: the assigned one, or —
                // when none is assigned — the same runtime defaults the controller falls back to,
                // so "you tagged content but the switch is off" is answered correctly either way.
                input.FeatureAvailability = BodyAnimationFeatureAvailability.Compute(
                    set, config != null ? config : DefaultConfigProbe);
            }

            return input;
        }

        /// <summary>
        ///     A single shared, hidden config instance carrying the SDK's runtime defaults, used
        ///     only to answer "what would this set do with no config assigned?". Created once
        ///     rather than per repaint — <see cref="GatherFrom" /> runs on every inspector repaint,
        ///     and allocating a ScriptableObject there would churn continuously.
        /// </summary>
        private static ConvaiBodyAnimationConfig _defaultConfigProbe;

        private static ConvaiBodyAnimationConfig DefaultConfigProbe
        {
            get
            {
                if (_defaultConfigProbe != null) return _defaultConfigProbe;

                _defaultConfigProbe = ConvaiBodyAnimationConfig.CreateDefault();
                // CreateDefault marks the instance HideAndDontSave, which is exactly what keeps a
                // domain reload from collecting it — so it is released explicitly instead of
                // leaking one more instance on every script compile.
                AssemblyReloadEvents.beforeAssemblyReload -= ReleaseDefaultConfigProbe;
                AssemblyReloadEvents.beforeAssemblyReload += ReleaseDefaultConfigProbe;
                return _defaultConfigProbe;
            }
        }

        private static void ReleaseDefaultConfigProbe()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= ReleaseDefaultConfigProbe;
            if (_defaultConfigProbe == null) return;

            Object.DestroyImmediate(_defaultConfigProbe);
            _defaultConfigProbe = null;
        }

        /// <summary>
        ///     Whether the set authors any content that blends through the upper-body overlay mask.
        ///     A locomotion-only set never needs one, so its absence is not reported as a fault.
        ///     Mirrors the same condition <see cref="ConvaiBodyAnimationSet.CollectIssues" /> uses.
        /// </summary>
        private static bool NeedsUpperBodyMask(ConvaiBodyAnimationSet set)
        {
            if (set.HasAnyTalk || set.HasAnyListen || set.HasAnyThink || set.Pointing.HasAny) return true;

            IReadOnlyList<ActionEntry> actions = set.Actions;
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] != null && actions[i].MaskMode == ActionMaskMode.UpperBody) return true;
            }
            return false;
        }

        /// <summary>
        ///     Counts assigned locomotion clips with no measured ground speed. The runtime falls back
        ///     to the config's nominal speeds for those, which is what makes feet slide, and the
        ///     directional-start / planted-stop selectors refuse to run without measured motion.
        /// </summary>
        private static int CountLocomotionClipsMissingMetadata(ConvaiBodyAnimationSet set)
        {
            var assigned = new List<(string slot, LocomotionClip clip)>();
            set.Locomotion.CollectAssigned(assigned);

            int missing = 0;
            for (int i = 0; i < assigned.Count; i++)
            {
                LocomotionClip clip = assigned[i].clip;
                if (clip?.Metadata == null || !clip.Metadata.HasSpeed) missing++;
            }
            return missing;
        }

        /// <summary>
        ///     Evaluates a <see cref="ConvaiBodyAnimationSet" /> on its own — no character, no rig —
        ///     into the same finding model every other surface renders. This is what the set's own
        ///     inspector shows, so an authoring problem reads identically whether the user found it
        ///     from the asset or from the character.
        /// </summary>
        internal static void EvaluateSetAsset(
            ConvaiBodyAnimationSet set,
            List<string> issuesScratch,
            List<BodyAnimationTroubleshooterFinding> results)
        {
            results.Clear();
            if (set == null) return;

            issuesScratch?.Clear();
            var findingsScratch = new List<BodyAnimationFinding>();
            set.CollectFindings(findingsScratch);
            for (int i = 0; i < findingsScratch.Count; i++)
                issuesScratch?.Add(findingsScratch[i].Message);

            var input = new BodyAnimationTroubleshooterInput
            {
                HasSetAssigned = true,
                HasAnyIdle = set.HasAnyIdle,
                HasAnyTalk = set.HasAnyTalk,
                HasAnyListen = set.HasAnyListen,
                HasAnyThink = set.HasAnyThink,
                HasBeatGesture = HasBeatGesture(set),
                HasTalkFragments = HasTalkFragments(set),
                HasUpperBodyMask = set.UpperBodyMask != null,
                NeedsUpperBodyMask = NeedsUpperBodyMask(set),
                LocomotionClipsMissingMetadata = CountLocomotionClipsMissingMetadata(set),
                // No rig is known when evaluating a set asset on its own — 1 = no correction,
                // so the calibration row (which needs a rig to compare against) never appears here.
                RigMotionScale = 1f,
                SetIssues = issuesScratch,
                SetFindings = findingsScratch
            };

            EvaluateSet(in input, results);
        }

        /// <summary>
        ///     The worst severity in a finding list — what a header badge should report. Returns
        ///     <see cref="BodyAnimationTroubleshooterSeverity.Ok" /> for an empty list.
        /// </summary>
        internal static BodyAnimationTroubleshooterSeverity WorstSeverity(
            List<BodyAnimationTroubleshooterFinding> findings)
        {
            var worst = BodyAnimationTroubleshooterSeverity.Ok;
            if (findings == null) return worst;

            for (int i = 0; i < findings.Count; i++)
            {
                if (findings[i].Severity > worst) worst = findings[i].Severity;
            }
            return worst;
        }

        /// <summary>Resolves the character root the same way the runtime component does: the owning EmbodimentContext, or the controller's own transform.</summary>
        internal static Transform ResolveScanRoot(ConvaiBodyAnimationController controller)
        {
            EmbodimentContext context = controller.GetComponentInParent<EmbodimentContext>(true);
            return context != null ? context.transform : controller.transform;
        }

        private static bool HasBeatGesture(ConvaiBodyAnimationSet set)
        {
            IReadOnlyList<ActionEntry> actions = set.Actions;
            for (int i = 0; i < actions.Count; i++)
            {
                GestureCueKind cue = actions[i].Cue;
                if (cue == GestureCueKind.Beat || cue == GestureCueKind.Emphatic) return true;
            }
            return false;
        }

        private static bool HasTalkFragments(ConvaiBodyAnimationSet set)
        {
            for (int i = 0; i < set.Talks.Count; i++)
                if (set.Talks[i] != null && set.Talks[i].HasFragments) return true;
            return false;
        }
    }
}
