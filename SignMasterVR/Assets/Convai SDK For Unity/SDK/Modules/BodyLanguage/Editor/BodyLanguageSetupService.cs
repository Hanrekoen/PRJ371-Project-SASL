using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Modules;
using Convai.Domain.Embodiment.Semantics;
using Convai.Editor.Ownership;
using Convai.Modules.BodyLanguage.Components;
using Convai.Modules.BodyLanguage.Data;
using Convai.Runtime.Animation;
using Convai.Runtime.Components;
using Convai.Runtime.Embodiment;
using Convai.Shared.Interfaces;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Editor
{
    /// <summary>State of a single preflight row on the Body Language setup card.</summary>
    internal enum BodyLanguageCheckState
    {
        /// <summary>Already satisfied.</summary>
        Ok,

        /// <summary>Not satisfied, but this service can resolve it — and the module still runs meanwhile.</summary>
        Fixable,

        /// <summary>Not satisfied and nothing here can resolve it. The module stays inert until the user acts.</summary>
        Blocked,

        /// <summary>Absent, and that is a legitimate rig rather than a problem — some behavior simply does not apply.</summary>
        Optional
    }

    /// <summary>One preflight row: what is checked, what was found, and whether a repair exists.</summary>
    internal readonly struct BodyLanguageCheck
    {
        internal BodyLanguageCheck(
            string id, string label, string detail, BodyLanguageCheckState state,
            BodyLanguageFixId fix = BodyLanguageFixId.None)
        {
            Id = id;
            Label = label;
            Detail = detail;
            State = state;
            Fix = fix;
        }

        /// <summary>Stable identifier, never shown to the user.</summary>
        public string Id { get; }

        /// <summary>Plain-language name of what is being checked, e.g. "Rig".</summary>
        public string Label { get; }

        /// <summary>What was actually found — a bone summary, an asset name, or why the row is not satisfied.</summary>
        public string Detail { get; }

        public BodyLanguageCheckState State { get; }

        /// <summary>The repair for this row, or <see cref="BodyLanguageFixId.None" />.</summary>
        public BodyLanguageFixId Fix { get; }
    }

    /// <summary>A repair the setup surfaces can offer as a named button.</summary>
    internal enum BodyLanguageFixId
    {
        None = 0,

        /// <summary>Assign the shipped Body Language profile to this controller.</summary>
        AssignDefaultProfile
    }

    /// <summary>
    ///     Who else is moving this character's body, and what Body Language does differently because
    ///     of it — the read-only answer to "why isn't this character swaying?" when the real cause is
    ///     another Convai module holding the pose.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Body Language shares the spine, shoulders and head with Body Animation and Gaze, and
    ///         takes its speech rhythm and emotional colour from Lip Sync and Emotion. Every one of
    ///         those relationships degrades gracefully, which is exactly why they are hard to see: a
    ///         character with no Body Animation is not broken, it simply never has its posture ducked.
    ///         Stating each relationship in the words the documentation uses is the difference
    ///         between a user finding that out and guessing at it.
    ///     </para>
    ///     <para>
    ///         Composed from the components on the character, so it answers before Play. What each
    ///         peer actually contributes is decided at runtime when it registers itself — see
    ///         <see cref="RuntimeCaveat" />, which every surface showing this report must pass on.
    ///     </para>
    /// </remarks>
    internal readonly struct BodyLanguageCoordination
    {
        internal BodyLanguageCoordination(
            bool hasBodyAnimation, bool hasGaze, bool hasEmotion, bool hasLipSync,
            bool emotionModulationEnabled)
        {
            HasBodyAnimation = hasBodyAnimation;
            HasGaze = hasGaze;
            HasEmotion = hasEmotion;
            HasLipSync = hasLipSync;
            EmotionModulationEnabled = emotionModulationEnabled;
        }

        /// <summary>Whether Body Animation is on this character.</summary>
        public bool HasBodyAnimation { get; }

        /// <summary>Whether Gaze is on this character.</summary>
        public bool HasGaze { get; }

        /// <summary>Whether Emotion is on this character.</summary>
        public bool HasEmotion { get; }

        /// <summary>Whether Lip Sync is on this character.</summary>
        public bool HasLipSync { get; }

        /// <summary>Whether the effective personality has Enable Emotion Modulation switched on.</summary>
        public bool EmotionModulationEnabled { get; }

        /// <summary>Who moves the head — Gaze composes head gestures when it is present.</summary>
        public string HeadGestures => HasGaze
            ? "Gaze is on this character, so it composes the head-beats, nods and listening tilts " +
              "over whatever else the head is doing."
            : "No Gaze on this character, so Body Language moves the head and neck itself, at " +
              "conservative limits.";

        /// <summary>Who performs a semantic gesture cue.</summary>
        public string GestureCues => HasBodyAnimation
            ? "Body Animation performs the semantic gesture cues, and an authored gesture always wins."
            : "No Body Animation on this character, so semantic gesture cues are always refused and " +
              "substituted by a head-beat and a posture pulse.";

        /// <summary>What ducks this character's posture, and what deliberately survives it.</summary>
        public string GestureSuppression => HasBodyAnimation
            ? "While this character walks or plays an upper-body action, Body Animation reduces its " +
              "posture and gesticulation — head-beats and breathing deliberately stay at full " +
              "weight. A full-body action fades posture and breathing to zero."
            : "No Body Animation on this character, so gesture suppression always reads None — " +
              "nothing will ever duck this character's posture.";

        /// <summary>Where the co-speech rhythm comes from.</summary>
        public string SpeechRhythm => HasLipSync
            ? "Head-beats and posture pulses ride the character's live speech energy."
            : "No Lip Sync on this character, so the co-speech channel falls back to a randomized " +
              "cadence that is clearly slower than real speech rhythm.";

        /// <summary>Whether emotion colours the body, and whether the personality lets it.</summary>
        public string Emotion => !HasEmotion
            ? "No Emotion on this character, so the reading stays neutral and every modifier resolves " +
              "to no change — which costs a character that does not use Emotion nothing."
            : EmotionModulationEnabled
                ? "Emotion biases this character's posture and scales its gesture and breath dynamics."
                : $"Emotion is on this character, but {BodyLanguageLabels.ForField("enableEmotionModulation")} " +
                  "is off on its personality, so emotion does not bias the posture. Sudden emotion " +
                  "spikes still trigger reactions, which are gated separately.";

        /// <summary>Whether physical effort reaches the breathing.</summary>
        public string Exertion => HasBodyAnimation
            ? "Breathing folds this character's locomotion effort into its own rate and depth."
            : "Nothing publishes locomotion effort on this character, so the exertion rate and depth " +
              "boosts resolve to no change.";

        /// <summary>One line the setup card can show without expanding anything.</summary>
        public string Summary
        {
            get
            {
                var peers = new List<string>(4);
                if (HasBodyAnimation) peers.Add("Body Animation");
                if (HasGaze) peers.Add("Gaze");
                if (HasEmotion) peers.Add("Emotion");
                if (HasLipSync) peers.Add("Lip Sync");

                return peers.Count == 0
                    ? "on its own — nothing ducks its posture, and it moves its own head and neck"
                    : $"{string.Join(", ", peers)} — see below for what each one changes";
            }
        }

        /// <summary>
        ///     The honesty note every surface showing this report has to pass on: peer presence is
        ///     read from the components, but what a peer contributes is settled at runtime.
        /// </summary>
        public const string RuntimeCaveat =
            "Read from the components on this character. What each one actually contributes is " +
            "settled when you press Play.";
    }

    /// <summary>
    ///     The read-only answer to "will this character actually move, and what will it be missing?"
    ///     — recomputed on a short interval so it never reports a rig the user has since changed.
    /// </summary>
    /// <summary>One master switch on a personality, and what turning it off costs the character.</summary>
    internal readonly struct BodyLanguageSwitch
    {
        internal BodyLanguageSwitch(string fieldName, bool isOn, string consequenceWhenOff)
        {
            FieldName = fieldName;
            IsOn = isOn;
            ConsequenceWhenOff = consequenceWhenOff;
        }

        /// <summary>The serialized field this switch is, so its label has exactly one source.</summary>
        public string FieldName { get; }

        /// <summary>The label the personality inspector shows for it.</summary>
        public string Label => BodyLanguageLabels.ForField(FieldName);

        public bool IsOn { get; }

        /// <summary>What the character loses while this is off, as a sentence fragment.</summary>
        public string ConsequenceWhenOff { get; }
    }

    internal readonly struct BodyLanguagePreflight
    {
        internal BodyLanguagePreflight(IReadOnlyList<BodyLanguageCheck> checks)
        {
            Checks = checks;
        }

        public IReadOnlyList<BodyLanguageCheck> Checks { get; }

        /// <summary>
        ///     True when Body Language will produce motion at runtime — i.e. nothing is blocked.
        /// </summary>
        /// <remarks>
        ///     Like Gaze and unlike Body Animation, this module works the moment it is added: it
        ///     resolves the rig from the avatar and falls back to built-in defaults with no profile
        ///     assigned. Only an unusable spine can actually stop it, so a character with optional
        ///     bones missing is functional, not "not set up".
        /// </remarks>
        public bool IsFunctional => !HasBlocker;

        /// <summary>True when at least one row is blocked — the module stays inert until the user acts.</summary>
        public bool HasBlocker
        {
            get
            {
                if (Checks == null) return false;
                for (int i = 0; i < Checks.Count; i++)
                    if (Checks[i].State == BodyLanguageCheckState.Blocked) return true;
                return false;
            }
        }

        /// <summary>The first blocked row, when one exists.</summary>
        public bool TryGetBlocker(out BodyLanguageCheck blocker)
        {
            if (Checks != null)
            {
                for (int i = 0; i < Checks.Count; i++)
                {
                    if (Checks[i].State != BodyLanguageCheckState.Blocked) continue;
                    blocker = Checks[i];
                    return true;
                }
            }

            blocker = default;
            return false;
        }
    }

    /// <summary>
    ///     Inspects a <see cref="ConvaiBodyLanguageController" /> at edit time and reports what the
    ///     character's rig can and cannot do, before the user presses Play.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The runtime resolves its bones through <c>EmbodimentContext.EnsureRigBinding()</c>,
    ///         which only exists while playing. This service answers the same question from what is
    ///         statically knowable: an authored <see cref="StandardRigBinding" /> when the character
    ///         has one, otherwise the Animator's Humanoid avatar — the same two sources the runtime
    ///         binding itself reads, so a row here cannot promise something the runtime then denies.
    ///     </para>
    ///     <para>
    ///         <see cref="Inspect" /> is read-only and allocates one small list per call.
    ///         <see cref="ApplyFix" /> mutates through <see cref="SerializedObject" /> in one undo
    ///         step. Adding the component is deliberately not this service's job — that is Unity's
    ///         own Add Component.
    ///     </para>
    /// </remarks>
    internal static class BodyLanguageSetupService
    {
        internal const string CheckCharacter = "bodylanguage.setup.character";
        internal const string CheckRig = "bodylanguage.setup.rig";
        internal const string CheckTorso = "bodylanguage.setup.torso";
        internal const string CheckShoulders = "bodylanguage.setup.shoulders";
        internal const string CheckStance = "bodylanguage.setup.stance";
        internal const string CheckHands = "bodylanguage.setup.hands";
        internal const string CheckPersonality = "bodylanguage.setup.personality";
        internal const string CheckCoordination = "bodylanguage.setup.coordination";

        /// <summary>
        ///     GUID of the shipped Body Language profile — the same asset the embodiment preset's
        ///     <c>convai.body-language</c> slot points at, so "assign a personality" and "use the
        ///     preset" can never hand the character two different defaults.
        /// </summary>
        private const string DefaultProfileGuid = "62e0132b7feca824c9231edafef52ae0";

        /// <summary>
        ///     The built-in personality a character with nothing assigned runs on. Built once per
        ///     domain reload — see <see cref="ResolveEffectiveProfile" />.
        /// </summary>
        private static ConvaiBodyLanguageProfile _sdkDefaults;

        // ------------------------------------------------------------------ preflight

        /// <summary>
        ///     Evaluates the character without changing anything. Every row states what was found,
        ///     so a user can see what will and will not happen before pressing Play.
        /// </summary>
        internal static BodyLanguagePreflight Inspect(ConvaiBodyLanguageController controller)
        {
            var checks = new List<BodyLanguageCheck>(8);
            if (controller == null) return new BodyLanguagePreflight(checks);

            Transform root = ResolveRoot(controller);
            checks.Add(InspectCharacter(controller));
            AddRigChecks(root, checks);
            checks.Add(InspectPersonality(controller));
            checks.Add(InspectCoordinationRow(root, ResolveEffectiveProfile(controller)));
            return new BodyLanguagePreflight(checks);
        }

        /// <summary>
        ///     Evaluates a character that does not have the component yet, so a caller can answer
        ///     "can this character host Body Language?" before adding anything.
        /// </summary>
        /// <remarks>
        ///     The same rig rows <see cref="Inspect" /> produces, from the same probe — the rows that
        ///     depend on the component (its personality) are simply absent. This exists so that
        ///     adding the component is never the price of finding out that the rig cannot drive it.
        /// </remarks>
        internal static BodyLanguagePreflight InspectCandidate(GameObject characterRoot)
        {
            var checks = new List<BodyLanguageCheck>(7);
            if (characterRoot == null) return new BodyLanguagePreflight(checks);

            Transform root = characterRoot.transform;
            AddRigChecks(root, checks);
            checks.Add(InspectCoordinationRow(root, ResolveEffectiveProfile(null)));
            return new BodyLanguagePreflight(checks);
        }

        private static void AddRigChecks(Transform root, List<BodyLanguageCheck> checks)
        {
            var rig = RigProbe.For(root);
            checks.Add(InspectRig(in rig));

            // Everything below depends on a usable spine. Reporting "no shoulders" underneath a
            // blocked rig row reads as five problems when there is only one.
            if (!rig.HasSpine) return;

            checks.Add(InspectTorso(in rig));
            checks.Add(InspectShoulders(in rig));
            checks.Add(InspectStance(in rig));
            checks.Add(InspectHands(in rig));
        }

        /// <summary>
        ///     Who else moves this character's body, and what Body Language does differently because
        ///     of it. Read-only; never <c>Blocked</c>, because every peer is optional by design.
        /// </summary>
        internal static BodyLanguageCoordination InspectCoordination(ConvaiBodyLanguageController controller)
        {
            if (controller == null) return default;
            return InspectCoordination(ResolveRoot(controller), ResolveEffectiveProfile(controller));
        }

        /// <summary>
        ///     The same report for a character root, so it can be answered before the component
        ///     exists.
        /// </summary>
        internal static BodyLanguageCoordination InspectCoordination(
            Transform root, ConvaiBodyLanguageProfile effectiveProfile)
        {
            if (root == null) return default;

            return new BodyLanguageCoordination(
                HasModule(root, ModuleIds.BodyAnimation),
                HasModule(root, ModuleIds.Gaze),
                HasModule(root, ModuleIds.Emotion),
                root.GetComponentInChildren<ILipSyncCapabilitySource>(true) != null,
                effectiveProfile != null && effectiveProfile.EnableEmotionModulation);
        }

        private static BodyLanguageCheck InspectCoordinationRow(
            Transform root, ConvaiBodyLanguageProfile effectiveProfile)
        {
            BodyLanguageCoordination coordination = InspectCoordination(root, effectiveProfile);

            // Never Blocked and never Fixable: a character with no peers is a legitimate character,
            // and colouring that as a fault would be exactly the false alarm the optional rows above
            // exist to avoid.
            return new BodyLanguageCheck(CheckCoordination, "Works with", coordination.Summary,
                coordination.HasBodyAnimation || coordination.HasGaze
                    ? BodyLanguageCheckState.Ok
                    : BodyLanguageCheckState.Optional);
        }

        /// <summary>
        ///     Whether a Convai embodiment module with <paramref name="moduleId" /> is on this
        ///     character.
        /// </summary>
        /// <remarks>
        ///     Read through the SDK's own <see cref="EmbodimentModuleAttribute" /> rather than by
        ///     referencing the peer's controller type, because modules must never reference each
        ///     other. That attribute is the single declaration of module identity, so this and
        ///     the embodiment module catalog cannot disagree about which module a component is.
        /// </remarks>
        private static bool HasModule(Transform root, string moduleId)
        {
            if (root == null || string.IsNullOrEmpty(moduleId)) return false;

            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null) continue;

                var attribute = (EmbodimentModuleAttribute)Attribute.GetCustomAttribute(
                    components[i].GetType(), typeof(EmbodimentModuleAttribute));
                if (attribute != null &&
                    string.Equals(attribute.ModuleId, moduleId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static BodyLanguageCheck InspectCharacter(ConvaiBodyLanguageController controller)
        {
            var character = controller.GetComponentInParent<ConvaiCharacter>(true);
            if (character != null)
                return new BodyLanguageCheck(CheckCharacter, "Convai Character", character.name,
                    BodyLanguageCheckState.Ok);

            var context = controller.GetComponentInParent<EmbodimentContext>(true);
            return context != null
                ? new BodyLanguageCheck(CheckCharacter, "Convai Character",
                    $"none — using the embodiment context on '{context.name}'", BodyLanguageCheckState.Ok)
                : new BodyLanguageCheck(CheckCharacter, "Convai Character",
                    "none in this hierarchy — the body still breathes and shifts its weight, but the " +
                    "conversation will not drive its posture or gestures",
                    BodyLanguageCheckState.Optional);
        }

        private static BodyLanguageCheck InspectRig(in RigProbe rig)
        {
            if (!rig.HasAnimator)
                return new BodyLanguageCheck(CheckRig, "Rig",
                    "no Animator on this character — Body Language layers motion onto an animated " +
                    "skeleton, so it has nothing to move",
                    BodyLanguageCheckState.Blocked);

            if (!rig.HasSpine)
                return new BodyLanguageCheck(CheckRig, "Rig",
                    rig.IsHumanoid
                        ? "Humanoid avatar, but no Spine bone is mapped — check the Avatar's spine chain"
                        : "the avatar is not Humanoid, so no Spine bone can be resolved",
                    BodyLanguageCheckState.Blocked);

            return new BodyLanguageCheck(CheckRig, "Rig",
                rig.IsHumanoid ? "Humanoid — spine found" : "custom rig — spine found",
                BodyLanguageCheckState.Ok);
        }

        private static BodyLanguageCheck InspectTorso(in RigProbe rig)
        {
            if (rig.HasChest && rig.HasUpperChest)
                return new BodyLanguageCheck(CheckTorso, "Torso", "spine, chest and upper chest",
                    BodyLanguageCheckState.Ok);

            if (rig.HasChest)
                return new BodyLanguageCheck(CheckTorso, "Torso",
                    "spine and chest — no upper chest, so breathing spreads across the bones that exist",
                    BodyLanguageCheckState.Ok);

            return new BodyLanguageCheck(CheckTorso, "Torso",
                "spine only — breathing and posture still work, redistributed onto it, but the torso " +
                "reads flatter than on a full chain",
                BodyLanguageCheckState.Optional);
        }

        private static BodyLanguageCheck InspectShoulders(in RigProbe rig)
        {
            bool both = rig.HasLeftShoulder && rig.HasRightShoulder;
            return both
                ? new BodyLanguageCheck(CheckShoulders, "Shoulders", "both found — breath lift and tension active",
                    BodyLanguageCheckState.Ok)
                : new BodyLanguageCheck(CheckShoulders, "Shoulders",
                    "not both mapped — shoulder lift on inhale and shoulder tension stay off",
                    BodyLanguageCheckState.Optional);
        }

        private static BodyLanguageCheck InspectStance(in RigProbe rig)
        {
            if (!rig.HasHips)
                return new BodyLanguageCheck(CheckStance, "Stance",
                    "no Hips bone — the character will not shift its weight from foot to foot",
                    BodyLanguageCheckState.Optional);

            return rig.HasLegChain
                ? new BodyLanguageCheck(CheckStance, "Stance",
                    "hips and both legs — weight shifts keep the feet planted",
                    BodyLanguageCheckState.Ok)
                : new BodyLanguageCheck(CheckStance, "Stance",
                    "hips found, legs incomplete — weight shifts run at a reduced travel so the feet " +
                    "never visibly slide",
                    BodyLanguageCheckState.Optional);
        }

        private static BodyLanguageCheck InspectHands(in RigProbe rig)
        {
            return rig.HasArmChain
                ? new BodyLanguageCheck(CheckHands, "Arms & hands",
                    "both arms found — idle hand motion and procedural gestures available",
                    BodyLanguageCheckState.Ok)
                : new BodyLanguageCheck(CheckHands, "Arms & hands",
                    "not both arms mapped — idle hand motion and the procedural gesture fallback stay " +
                    "off; authored gestures are unaffected",
                    BodyLanguageCheckState.Optional);
        }

        private static BodyLanguageCheck InspectPersonality(ConvaiBodyLanguageController controller)
        {
            ConvaiBodyLanguageProfile profile = ResolveAssignedProfile(controller);
            if (profile != null)
                return new BodyLanguageCheck(CheckPersonality, "Personality", profile.name,
                    BodyLanguageCheckState.Ok);

            return TryLoadDefaultProfile() != null
                ? new BodyLanguageCheck(CheckPersonality, "Personality",
                    "none assigned — using the SDK defaults, which work; assign one to tune the character",
                    BodyLanguageCheckState.Fixable, BodyLanguageFixId.AssignDefaultProfile)
                : new BodyLanguageCheck(CheckPersonality, "Personality",
                    "none assigned — using the SDK defaults, and no profile asset was found in this project",
                    BodyLanguageCheckState.Optional);
        }

        // ------------------------------------------------------------------ repairs

        /// <summary>Applies one repair from a preflight row. Returns whether anything was written.</summary>
        internal static bool ApplyFix(ConvaiBodyLanguageController controller, BodyLanguageFixId fix)
        {
            if (controller == null) return false;

            return fix switch
            {
                BodyLanguageFixId.AssignDefaultProfile => AssignDefaultProfile(controller),
                _ => false
            };
        }

        /// <summary>Button text for a repair, or <c>null</c> when this service cannot perform it.</summary>
        internal static string DescribeFix(BodyLanguageFixId fix) => fix switch
        {
            BodyLanguageFixId.AssignDefaultProfile => "Add a Personality",
            _ => null
        };

        /// <summary>
        ///     Gives the character its own Body Language profile, built from the SDK defaults.
        /// </summary>
        /// <remarks>
        ///     Same reasoning as Gaze: a Body Language profile is entirely tuning, so assigning the
        ///     one that ships inside the package handed the character settings it could not keep.
        /// </remarks>
        private static bool AssignDefaultProfile(ConvaiBodyLanguageController controller)
        {
            if (ResolveAssignedProfile(controller) != null) return false;

            return ConvaiCopyOnWrite.CreateAndAssign(
                ConvaiBodyLanguageProfile.CreateDefault(), controller,
                "BodyLanguage", "_BodyLanguage", "profile").Succeeded;
        }

        /// <summary>
        ///     The character this controller belongs to — the Convai Character it sits under, the
        ///     embodiment context, or the controller's own object. The same walk the runtime rig
        ///     binding does, so a row here cannot promise a bone the runtime then denies.
        /// </summary>
        internal static Transform ResolveRoot(ConvaiBodyLanguageController controller)
        {
            if (controller == null) return null;

            var character = controller.GetComponentInParent<ConvaiCharacter>(true);
            if (character != null) return character.transform;

            var context = controller.GetComponentInParent<EmbodimentContext>(true);
            return context != null ? context.transform : controller.transform;
        }

        /// <summary>The personality asset assigned to this controller, or <c>null</c>.</summary>
        /// <summary>
        ///     Whether a personality ships inside the Convai package, as plain data.
        /// </summary>
        /// <remarks>
        ///     Answered here so the module's MCP assembly does not have to reach the editor UI
        ///     assembly the ownership vocabulary lives in — and, more importantly, so it stops
        ///     deciding for itself by testing a path, which is how five modules ended up with five
        ///     different answers to one question.
        /// </remarks>
        internal static bool ProfileShipsWithSdk(ConvaiBodyLanguageProfile profile) =>
            ConvaiAssetOwnership.IsSdkAsset(profile);

        /// <summary>
        ///     The master switches a user can turn off, paired with what each one stops when it is off.
        /// </summary>
        /// <remarks>
        ///     One table, read by the personality listing, by the setup report and by the "why isn't
        ///     this character moving?" answer, so those three can never name a switch differently. It
        ///     lives here rather than with the assistant tools that project it because that assembly
        ///     only exists when Unity's AI Assistant package is installed — a user without it would
        ///     otherwise have no way to be told which switch is holding their character still.
        /// </remarks>
        internal static IReadOnlyList<BodyLanguageSwitch> SwitchesOf(ConvaiBodyLanguageProfile profile)
        {
            if (profile == null) return Array.Empty<BodyLanguageSwitch>();

            return new[]
            {
                new BodyLanguageSwitch("enableWeightShifts", profile.EnableWeightShifts,
                    "this character will never shift its weight from foot to foot"),
                new BodyLanguageSwitch("enableLegCompensation", profile.EnableLegCompensation,
                    "weight shifts run at a reduced travel so the feet never visibly slide"),
                new BodyLanguageSwitch("enableAmbientSway", profile.EnableAmbientSway,
                    "this character will never sway"),
                new BodyLanguageSwitch("enableHandMicro", profile.EnableHandMicro,
                    "the hands sit still between authored gestures"),
                new BodyLanguageSwitch("enableProceduralGestureFallback",
                    profile.EnableProceduralGestureFallback,
                    "a refused gesture cue falls back to a head-beat and posture pulse only, with no " +
                    "arm motion"),
                new BodyLanguageSwitch("enableReactions", profile.EnableReactions,
                    "no startle flinch and no amused bounce, autonomous or scripted"),
                new BodyLanguageSwitch("enableEmotionModulation", profile.EnableEmotionModulation,
                    "emotion does not bias the posture or scale the gesture and breath dynamics"),
                new BodyLanguageSwitch("enableCatchBreath", profile.EnableCatchBreath,
                    "no sharp intake when the character is interrupted"),
                new BodyLanguageSwitch("enableSigh", profile.EnableSigh,
                    "no long breath as the character settles"),
                new BodyLanguageSwitch("enableInhaleBeforeSpeaking", profile.EnableInhaleBeforeSpeaking,
                    "no breath is taken before a line starts"),
                new BodyLanguageSwitch("enableBreathAdaptiveLayering",
                    profile.EnableBreathAdaptiveLayering,
                    "the procedural breath no longer ducks under breathing baked into the idle clips"),
                new BodyLanguageSwitch("enableIdleMacroCycles", profile.EnableIdleMacroCycles,
                    "a long idle settles into a more perceptibly repeating baseline"),
                new BodyLanguageSwitch("enableCameraDistanceLod", profile.EnableCameraDistanceLod,
                    "micro-motion is no longer scaled for how close the camera is")
            };
        }

        /// <summary>A short line naming what a personality is like, for a listing.</summary>
        internal static string HeadlineOf(ConvaiBodyLanguageProfile profile)
        {
            if (profile == null) return string.Empty;

            var off = new List<string>(4);
            IReadOnlyList<BodyLanguageSwitch> switches = SwitchesOf(profile);
            for (int i = 0; i < switches.Count; i++)
                if (!switches[i].IsOn) off.Add(switches[i].Label);

            string expressiveness = profile.ExpressivenessPreset.ToString();
            return off.Count == 0
                ? $"{expressiveness} — every behaviour switched on"
                : $"{expressiveness} — off: {string.Join(", ", off)}";
        }

        internal static ConvaiBodyLanguageProfile ResolveAssignedProfile(ConvaiBodyLanguageController controller)
        {
            if (controller == null) return null;

            var serialized = new SerializedObject(controller);
            return serialized.FindProperty("profile")?.objectReferenceValue as ConvaiBodyLanguageProfile;
        }

        /// <summary>
        ///     The personality this character actually runs on: the assigned asset, or the SDK
        ///     defaults it falls back to when none is assigned.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Read-only, and the caller must treat it as such. The defaults instance is the same
        ///         one the runtime builds from <c>ConvaiBodyLanguageProfile.CreateDefault</c>, held
        ///         once per domain reload and marked <see cref="HideFlags.HideAndDontSave" /> — it is
        ///         never written to disk, never enters a scene, and is not an asset.
        ///     </para>
        ///     <para>
        ///         It exists so that "Sway On The Spot is off" is answerable on a character that
        ///         has no personality assigned. Without it, the one configuration a first-time user
        ///         is most likely to be in is the one nothing can explain.
        ///     </para>
        /// </remarks>
        internal static ConvaiBodyLanguageProfile ResolveEffectiveProfile(ConvaiBodyLanguageController controller)
        {
            ConvaiBodyLanguageProfile assigned = ResolveAssignedProfile(controller);
            if (assigned != null) return assigned;

            if (_sdkDefaults == null)
            {
                _sdkDefaults = ConvaiBodyLanguageProfile.CreateDefault();
                if (_sdkDefaults != null)
                {
                    _sdkDefaults.name = "SDK defaults";
                    _sdkDefaults.hideFlags = HideFlags.HideAndDontSave;
                }
            }

            return _sdkDefaults;
        }

        private static ConvaiBodyLanguageProfile TryLoadDefaultProfile()
        {
            string path = AssetDatabase.GUIDToAssetPath(DefaultProfileGuid);
            if (!string.IsNullOrEmpty(path))
            {
                var shipped = AssetDatabase.LoadAssetAtPath<ConvaiBodyLanguageProfile>(path);
                if (shipped != null) return shipped;
            }

            // The shipped asset lives in the samples, which a project need not have imported. Any
            // profile the user already made is a better answer than none.
            string[] guids = AssetDatabase.FindAssets("t:ConvaiBodyLanguageProfile");
            for (int i = 0; i < guids.Length; i++)
            {
                var candidate = AssetDatabase.LoadAssetAtPath<ConvaiBodyLanguageProfile>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (candidate != null) return candidate;
            }

            return null;
        }

        // ------------------------------------------------------------------ rig probe

        /// <summary>
        ///     What the character's skeleton offers, read the same way the runtime binding reads it:
        ///     an authored <see cref="StandardRigBinding" /> first, then the Humanoid avatar.
        /// </summary>
        private readonly struct RigProbe
        {
            private RigProbe(
                bool hasAnimator, bool isHumanoid, bool hasSpine, bool hasChest, bool hasUpperChest,
                bool hasLeftShoulder, bool hasRightShoulder, bool hasHips, bool hasLegChain, bool hasArmChain)
            {
                HasAnimator = hasAnimator;
                IsHumanoid = isHumanoid;
                HasSpine = hasSpine;
                HasChest = hasChest;
                HasUpperChest = hasUpperChest;
                HasLeftShoulder = hasLeftShoulder;
                HasRightShoulder = hasRightShoulder;
                HasHips = hasHips;
                HasLegChain = hasLegChain;
                HasArmChain = hasArmChain;
            }

            public bool HasAnimator { get; }
            public bool IsHumanoid { get; }
            public bool HasSpine { get; }
            public bool HasChest { get; }
            public bool HasUpperChest { get; }
            public bool HasLeftShoulder { get; }
            public bool HasRightShoulder { get; }
            public bool HasHips { get; }
            public bool HasLegChain { get; }
            public bool HasArmChain { get; }

            internal static RigProbe For(Transform root)
            {
                if (root == null) return default;

                var binding = root.GetComponentInChildren<StandardRigBinding>(true);
                var animator = root.GetComponentInChildren<Animator>(true);
                bool isHumanoid = animator != null && animator.avatar != null &&
                                  animator.avatar.isValid && animator.avatar.isHuman;

                bool Has(StandardBone bone, HumanBodyBones humanBone)
                {
                    if (binding != null && binding.TryGetBone(bone, out Transform mapped) && mapped != null)
                        return true;
                    return isHumanoid && animator.GetBoneTransform(humanBone) != null;
                }

                bool hasLegChain =
                    Has(StandardBone.LeftUpperLeg, HumanBodyBones.LeftUpperLeg) &&
                    Has(StandardBone.LeftLowerLeg, HumanBodyBones.LeftLowerLeg) &&
                    Has(StandardBone.LeftFoot, HumanBodyBones.LeftFoot) &&
                    Has(StandardBone.RightUpperLeg, HumanBodyBones.RightUpperLeg) &&
                    Has(StandardBone.RightLowerLeg, HumanBodyBones.RightLowerLeg) &&
                    Has(StandardBone.RightFoot, HumanBodyBones.RightFoot);

                // Hand micro-life and the procedural gesture fallback resolve their bones straight
                // off the Animator's Humanoid mapping, so an arm chain is only ever available on a
                // Humanoid avatar — a StandardRigBinding override cannot substitute for it.
                bool hasArmChain = isHumanoid &&
                    animator.GetBoneTransform(HumanBodyBones.LeftUpperArm) != null &&
                    animator.GetBoneTransform(HumanBodyBones.LeftLowerArm) != null &&
                    animator.GetBoneTransform(HumanBodyBones.LeftHand) != null &&
                    animator.GetBoneTransform(HumanBodyBones.RightUpperArm) != null &&
                    animator.GetBoneTransform(HumanBodyBones.RightLowerArm) != null &&
                    animator.GetBoneTransform(HumanBodyBones.RightHand) != null;

                return new RigProbe(
                    animator != null,
                    isHumanoid,
                    Has(StandardBone.Spine, HumanBodyBones.Spine),
                    Has(StandardBone.Chest, HumanBodyBones.Chest),
                    Has(StandardBone.UpperChest, HumanBodyBones.UpperChest),
                    Has(StandardBone.LeftShoulder, HumanBodyBones.LeftShoulder),
                    Has(StandardBone.RightShoulder, HumanBodyBones.RightShoulder),
                    Has(StandardBone.Hips, HumanBodyBones.Hips),
                    hasLegChain,
                    hasArmChain);
            }
        }
    }
}
