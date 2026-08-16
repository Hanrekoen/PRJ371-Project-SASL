using Convai.Editor.Ownership;
using System.Collections.Generic;
using Convai.Modules.BodyAnimation.Components;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.Components;
using Convai.Runtime.Embodiment;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>State of a single preflight row on the setup card.</summary>
    internal enum BodyAnimationCheckState
    {
        /// <summary>Already satisfied.</summary>
        Ok,

        /// <summary>Not satisfied, but setup can resolve it.</summary>
        Fixable,

        /// <summary>Not satisfied and setup cannot resolve it — the user must act first.</summary>
        Blocked,

        /// <summary>
        ///     Not satisfied, and only animation content can satisfy it. Unfinished rather than
        ///     broken: nothing about the character is wrong, it simply has no clips yet — which is
        ///     the normal state of a project that has not imported the Convai samples.
        /// </summary>
        NeedsContent,

        /// <summary>Not present, and that is a legitimate choice rather than a problem.</summary>
        Optional
    }

    /// <summary>One preflight row: what is checked, what was found, and whether setup can fix it.</summary>
    internal readonly struct BodyAnimationCheck
    {
        public BodyAnimationCheck(string id, string label, string detail, BodyAnimationCheckState state)
        {
            Id = id;
            Label = label;
            Detail = detail;
            State = state;
        }

        /// <summary>Stable identifier, never shown to the user.</summary>
        public string Id { get; }

        /// <summary>Plain-language name of what is being checked, e.g. "Humanoid rig".</summary>
        public string Label { get; }

        /// <summary>What was actually found — an asset name, or the reason it is not satisfied.</summary>
        public string Detail { get; }

        public BodyAnimationCheckState State { get; }
    }

    /// <summary>
    ///     The read-only answer to "is this character ready, and what will the setup button do?" —
    ///     computed fresh on every inspector repaint, so it never promises something it cannot
    ///     deliver.
    /// </summary>
    internal readonly struct BodyAnimationPreflight
    {
        public BodyAnimationPreflight(IReadOnlyList<BodyAnimationCheck> checks)
        {
            Checks = checks;
        }

        public IReadOnlyList<BodyAnimationCheck> Checks { get; }

        /// <summary>True when nothing is blocked and nothing remains for setup to do.</summary>
        public bool IsConfigured
        {
            get
            {
                if (Checks == null) return false;
                for (int i = 0; i < Checks.Count; i++)
                {
                    if (Checks[i].State is BodyAnimationCheckState.Fixable
                        or BodyAnimationCheckState.Blocked
                        or BodyAnimationCheckState.NeedsContent)
                        return false;
                }
                return true;
            }
        }

        /// <summary>
        ///     True when at least one row is blocked — setup cannot complete on its own.
        /// </summary>
        /// <remarks>
        ///     Missing animation content deliberately does not count. A character with a sound rig
        ///     and no clips is not broken, it is unfinished, and the two have different answers: one
        ///     needs the rig fixed, the other needs content. That distinction is carried by
        ///     <see cref="BodyAnimationCheckState.NeedsContent" /> rather than by an exception for
        ///     the content row, so every surface that colours or counts a row inherits it instead of
        ///     having to remember the exception.
        /// </remarks>
        public bool HasBlocker
        {
            get
            {
                if (Checks == null) return false;
                for (int i = 0; i < Checks.Count; i++)
                {
                    if (Checks[i].State == BodyAnimationCheckState.Blocked) return true;
                }
                return false;
            }
        }

        /// <summary>
        ///     True when the only thing standing between this character and a working setup is
        ///     animation content.
        /// </summary>
        public bool NeedsContent
        {
            get
            {
                if (Checks == null) return false;
                for (int i = 0; i < Checks.Count; i++)
                {
                    if (Checks[i].State == BodyAnimationCheckState.NeedsContent) return true;
                }
                return false;
            }
        }
    }

    /// <summary>What the setup button should do, beyond the mandatory content assignment.</summary>
    internal struct BodyAnimationSetupOptions
    {
        /// <summary>Add NavMesh movement so the character can walk, jog, turn, and stop.</summary>
        public bool IncludeMovement;

        public static BodyAnimationSetupOptions Default => new() { IncludeMovement = true };
    }

    /// <summary>Outcome of a setup run, rendered identically by every caller.</summary>
    internal readonly struct BodyAnimationSetupResult
    {
        public BodyAnimationSetupResult(bool changed, string summary, IReadOnlyList<string> notes)
        {
            Changed = changed;
            Summary = summary;
            Notes = notes;
        }

        /// <summary>Whether anything was actually written.</summary>
        public bool Changed { get; }

        /// <summary>One-line result the surface shows immediately.</summary>
        public string Summary { get; }

        /// <summary>Everything that happened, and everything that could not be done.</summary>
        public IReadOnlyList<string> Notes { get; }
    }

    /// <summary>
    ///     The single code path that configures body animation on a character. The controller
    ///     inspector, the Troubleshooter window, and any scripted setup all go through here, so every
    ///     surface produces the same result and the same wording.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Inspect" /> is read-only and safe to call every repaint; it drives the
    ///         setup card's checklist. <see cref="Apply" /> mutates, in one undo group.
    ///     </para>
    ///     <para>
    ///         Adding the component itself is deliberately NOT this service's job — that is Unity's
    ///         own <c>Add Component</c>, which is the gesture every Unity user already knows. This
    ///         service starts from a controller that already exists.
    ///     </para>
    /// </remarks>
    internal static class BodyAnimationSetupService
    {
        internal const string CheckRig = "setup.rig";
        internal const string CheckCharacter = "setup.character";
        internal const string CheckContent = "setup.content";
        internal const string CheckMovement = "setup.movement";

        /// <summary>GUID of the SDK's shipped default profile (Female set + tuned config).</summary>
        private const string DefaultProfileGuid = "c836e2e8642f55544ab1c8051087d0c3";

        // ------------------------------------------------------------------ preflight

        /// <summary>
        ///     Evaluates the character without changing anything. Every row states what was found,
        ///     so the user can see what setup will and will not be able to do before pressing it.
        /// </summary>
        internal static BodyAnimationPreflight Inspect(ConvaiBodyAnimationController controller)
        {
            var checks = new List<BodyAnimationCheck>(4);
            if (controller == null) return new BodyAnimationPreflight(checks);

            checks.Add(InspectRig(controller));
            checks.Add(InspectCharacter(controller));
            checks.Add(InspectContent(controller));
            checks.Add(InspectMovement(controller));
            return new BodyAnimationPreflight(checks);
        }

        private static BodyAnimationCheck InspectRig(ConvaiBodyAnimationController controller) =>
            InspectRig(controller.ResolveTargetAnimator());

        /// <summary>
        ///     The rig verdict on its own, for a caller that does not have a controller yet — a
        ///     setup path deciding whether a character can host the module at all. The
        ///     controller-based checklist routes through this same method, so "not Humanoid" is
        ///     worded once and cannot be answered two ways.
        /// </summary>
        internal static BodyAnimationCheck InspectRig(Animator animator)
        {
            if (animator == null)
            {
                return new BodyAnimationCheck(CheckRig, "Humanoid rig",
                    "no Animator found on this character",
                    BodyAnimationCheckState.Blocked);
            }

            if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            {
                return new BodyAnimationCheck(CheckRig, "Humanoid rig",
                    $"'{animator.name}' is not Humanoid — set the model's Rig → Animation Type to Humanoid",
                    BodyAnimationCheckState.Blocked);
            }

            return new BodyAnimationCheck(CheckRig, "Humanoid rig",
                animator.avatar.name, BodyAnimationCheckState.Ok);
        }

        private static BodyAnimationCheck InspectCharacter(ConvaiBodyAnimationController controller)
        {
            var character = controller.GetComponentInParent<ConvaiCharacter>(true);
            if (character != null)
            {
                return new BodyAnimationCheck(CheckCharacter, "Convai Character",
                    character.name, BodyAnimationCheckState.Ok);
            }

            // A context without a character still routes behavior correctly (a custom rig, a test
            // scene), so this is not a blocker — but it is worth naming, since profile delivery and
            // cross-module handshakes are character-scoped.
            var context = controller.GetComponentInParent<EmbodimentContext>(true);
            return context != null
                ? new BodyAnimationCheck(CheckCharacter, "Convai Character",
                    $"none — using the embodiment context on '{context.name}'", BodyAnimationCheckState.Ok)
                : new BodyAnimationCheck(CheckCharacter, "Convai Character",
                    "none in this hierarchy — the character will still animate, but behavior routing is limited",
                    BodyAnimationCheckState.Optional);
        }

        private static BodyAnimationCheck InspectContent(ConvaiBodyAnimationController controller)
        {
            ConvaiBodyAnimationSet set = ResolveAssignedSet(controller);
            if (set != null)
            {
                return new BodyAnimationCheck(CheckContent, "Animation content",
                    set.DisplayName, BodyAnimationCheckState.Ok);
            }

            return TryLoadDefaultProfile() != null
                ? new BodyAnimationCheck(CheckContent, "Animation content",
                    "none assigned — the SDK's default set can be assigned for you",
                    BodyAnimationCheckState.Fixable)
                : new BodyAnimationCheck(CheckContent, "Animation content",
                    "no animation clips in this project yet — import the Convai samples, or build a " +
                    "set from your own clip folder",
                    BodyAnimationCheckState.NeedsContent);
        }

        private static BodyAnimationCheck InspectMovement(ConvaiBodyAnimationController controller)
        {
            ConvaiNavMeshLocomotion locomotion = FindLocomotion(controller);
            if (locomotion != null)
            {
                return new BodyAnimationCheck(CheckMovement, "Movement",
                    "walking, jogging, animated turns and stops", BodyAnimationCheckState.Ok);
            }

            return new BodyAnimationCheck(CheckMovement, "Movement",
                "not set up — the character idles, talks, gestures and points in place",
                BodyAnimationCheckState.Optional);
        }

        // ------------------------------------------------------------------ apply

        /// <summary>
        ///     Configures the character in a single undo step and reports exactly what happened.
        ///     Anything it cannot do is reported rather than silently skipped.
        /// </summary>
        internal static BodyAnimationSetupResult Apply(
            ConvaiBodyAnimationController controller, BodyAnimationSetupOptions options)
        {
            var notes = new List<string>(4);
            if (controller == null)
                return new BodyAnimationSetupResult(false, "No Body Animation component to set up.", notes);

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Set Up Body Animation");
            bool changed = false;

            changed |= AssignDefaultContent(controller, notes);
            if (options.IncludeMovement) changed |= AddMovement(controller, notes);

            Undo.CollapseUndoOperations(undoGroup);

            string summary = changed
                ? "Body animation is set up on this character."
                : "Nothing to change — this character was already set up.";
            return new BodyAnimationSetupResult(changed, summary, notes);
        }

        /// <summary>
        ///     Applies one character-scoped repair from a Troubleshooter finding. Set-scoped repairs
        ///     live in <see cref="BodyAnimationFixes" />.
        /// </summary>
        internal static bool ApplyFix(ConvaiBodyAnimationController controller, BodyAnimationFixId fix)
        {
            if (controller == null) return false;
            var notes = new List<string>(1);

            switch (fix)
            {
                case BodyAnimationFixId.AssignDefaultContent:
                    return AssignDefaultContent(controller, notes);

                case BodyAnimationFixId.AddMovement:
                    return AddMovement(controller, notes);

                case BodyAnimationFixId.ClearAnimatorController:
                    return ClearAnimatorController(controller);

                default:
                    return false;
            }
        }

        /// <summary>Button text for a character-scoped fix, or <c>null</c> when this service cannot perform it.</summary>
        internal static string DescribeFix(BodyAnimationFixId fix) => fix switch
        {
            BodyAnimationFixId.AssignDefaultContent => "Assign Default Content",
            BodyAnimationFixId.AddMovement => "Add Movement",
            BodyAnimationFixId.ClearAnimatorController => "Clear Controller",
            _ => null
        };

        // ------------------------------------------------------------------ individual steps

        /// <summary>
        ///     Gives the character its own Body Animation profile, carrying the animation content the
        ///     SDK ships.
        /// </summary>
        /// <remarks>
        ///     This used to assign the shipped profile itself, which is how a brand-new character
        ///     ended up reading a settings asset from inside the package — un-editable in a normally
        ///     installed project, replaced by the next update, and shared with every other character
        ///     that ran setup.
        ///     <para>
        ///         The two halves are separated instead. The <b>animation set</b> is content: clips
        ///         that are consumed, not tuned, and it stays in the package and is referenced. The
        ///         <b>config</b> is tuning, and is deliberately left <c>null</c> — the runtime falls
        ///         back to <c>ConvaiBodyAnimationConfig.CreateDefault()</c>, which is the same
        ///         starting point the shipped asset described, and a config asset is born only if the
        ///         user actually changes something.
        ///     </para>
        /// </remarks>
        private static bool AssignDefaultContent(ConvaiBodyAnimationController controller, List<string> notes)
        {
            if (ResolveAssignedSet(controller) != null) return false;

            ConvaiBodyAnimationProfile shipped = TryLoadDefaultProfile();
            if (shipped == null || shipped.AnimationSet == null)
            {
                notes.Add(
                    "No Body Animation content was found in this project, so none was assigned. " +
                    "Assign an Animation Set or Profile yourself, or create one from a clip folder " +
                    "in the Body Animation editor.");
                return false;
            }

            var profile = ScriptableObject.CreateInstance<ConvaiBodyAnimationProfile>();
            profile.Initialize(shipped.AnimationSet, null, shipped.AutoCreateConversationFlow);

            ConvaiCopyOnWriteResult created = ConvaiCopyOnWrite.CreateAndAssign(
                profile, controller, "BodyAnimation", "_BodyAnimation", "profile");

            if (!created.Succeeded)
            {
                notes.Add(created.FailureReason);
                return false;
            }

            notes.Add(
                $"Created '{System.IO.Path.GetFileNameWithoutExtension(created.AssetPath)}' for this " +
                "character — idle, talk, gestures, pointing and locomotion content, with personality " +
                "on the SDK defaults until you change it.");
            return true;
        }

        private static bool AddMovement(ConvaiBodyAnimationController controller, List<string> notes)
        {
            if (FindLocomotion(controller) != null) return false;

            GameObject target = ResolveMovementOwner(controller);
            Undo.AddComponent<ConvaiNavMeshLocomotion>(target);
            notes.Add($"Added NavMesh movement to '{target.name}' — call MoveTo() to walk the character.");

            if (!HasBakedNavMesh())
            {
                notes.Add(
                    "This scene has no baked NavMesh, so movement requests will not go anywhere yet. " +
                    "Everything else — idle, talk, gestures, pointing — works regardless.");
            }

            return true;
        }

        private static bool ClearAnimatorController(ConvaiBodyAnimationController controller)
        {
            Animator animator = controller.ResolveTargetAnimator();
            if (animator == null || animator.runtimeAnimatorController == null) return false;

            Undo.RecordObject(animator, "Clear Animator Controller");
            animator.runtimeAnimatorController = null;
            EditorUtility.SetDirty(animator);
            return true;
        }

        // ------------------------------------------------------------------ resolution helpers

        /// <summary>The set the controller would actually use: its profile's, or its direct field.</summary>
        internal static ConvaiBodyAnimationSet ResolveAssignedSet(ConvaiBodyAnimationController controller)
        {
            if (controller == null) return null;

            var serialized = new SerializedObject(controller);
            var profile = serialized.FindProperty("profile")?.objectReferenceValue as ConvaiBodyAnimationProfile;
            if (profile != null && profile.AnimationSet != null) return profile.AnimationSet;

            return serialized.FindProperty("_animationSet")?.objectReferenceValue as ConvaiBodyAnimationSet;
        }

        /// <summary>
        ///     The config asset the controller would actually use: its profile's, or its direct
        ///     field. <c>null</c> means no asset is assigned and the runtime falls back to built-in
        ///     defaults — there is no asset to write to in that case.
        /// </summary>
        internal static ConvaiBodyAnimationConfig ResolveAssignedConfig(ConvaiBodyAnimationController controller)
        {
            if (controller == null) return null;

            var serialized = new SerializedObject(controller);
            var profile = serialized.FindProperty("profile")?.objectReferenceValue as ConvaiBodyAnimationProfile;
            if (profile != null && profile.Config != null) return profile.Config;

            return serialized.FindProperty("_config")?.objectReferenceValue as ConvaiBodyAnimationConfig;
        }

        /// <summary>
        ///     Loads the SDK's shipped default profile by GUID, falling back to a project-wide search
        ///     so a relocated or re-imported package still resolves.
        /// </summary>
        internal static ConvaiBodyAnimationProfile TryLoadDefaultProfile()
        {
            string path = AssetDatabase.GUIDToAssetPath(DefaultProfileGuid);
            if (!string.IsNullOrEmpty(path))
            {
                var byGuid = AssetDatabase.LoadAssetAtPath<ConvaiBodyAnimationProfile>(path);
                if (byGuid != null && byGuid.AnimationSet != null) return byGuid;
            }

            string[] candidates = AssetDatabase.FindAssets($"t:{nameof(ConvaiBodyAnimationProfile)}");
            for (int i = 0; i < candidates.Length; i++)
            {
                string candidatePath = AssetDatabase.GUIDToAssetPath(candidates[i]);
                var profile = AssetDatabase.LoadAssetAtPath<ConvaiBodyAnimationProfile>(candidatePath);
                if (profile != null && profile.AnimationSet != null) return profile;
            }

            return null;
        }

        private static ConvaiNavMeshLocomotion FindLocomotion(ConvaiBodyAnimationController controller)
        {
            ConvaiNavMeshLocomotion locomotion = controller.GetComponentInParent<ConvaiNavMeshLocomotion>(true);
            return locomotion != null
                ? locomotion
                : controller.GetComponentInChildren<ConvaiNavMeshLocomotion>(true);
        }

        /// <summary>
        ///     Movement belongs on the transform that carries the character, since the NavMeshAgent
        ///     moves that transform and the animation layer drives its rotation — split them across
        ///     two GameObjects and the two authorities fight.
        /// </summary>
        private static GameObject ResolveMovementOwner(ConvaiBodyAnimationController controller)
        {
            var character = controller.GetComponentInParent<ConvaiCharacter>(true);
            if (character != null) return character.gameObject;

            var context = controller.GetComponentInParent<EmbodimentContext>(true);
            return context != null ? context.gameObject : controller.gameObject;
        }

        private static bool HasBakedNavMesh()
        {
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            return triangulation.indices != null && triangulation.indices.Length > 0;
        }
    }
}
