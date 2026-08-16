using Convai.Editor.Ownership;
using System;
using System.Collections.Generic;
using Convai.Modules.Gaze.Components;
using Convai.Modules.Gaze.Core;
using Convai.Modules.Gaze.Core.Solvers;
using Convai.Modules.Gaze.Data;
using Convai.Modules.Gaze.Providers;
using Convai.Runtime.Animation;
using Convai.Runtime.Components;
using Convai.Runtime.Embodiment;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Gaze.Editor
{
    /// <summary>State of a single preflight row on the gaze setup card.</summary>
    internal enum GazeCheckState
    {
        /// <summary>Already satisfied.</summary>
        Ok,

        /// <summary>Not satisfied, but setup can resolve it — and gaze still works meanwhile.</summary>
        Fixable,

        /// <summary>Not satisfied and setup cannot resolve it. Gaze is inert until the user acts.</summary>
        Blocked,

        /// <summary>Not present, and that is a legitimate choice rather than a problem.</summary>
        Optional
    }

    /// <summary>One preflight row: what is checked, what was found, and whether setup can fix it.</summary>
    internal readonly struct GazeCheck
    {
        internal GazeCheck(string id, string label, string detail, GazeCheckState state, GazeFixId fix = GazeFixId.None)
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

        /// <summary>What was actually found — a bone summary, an asset name, or the reason it is not satisfied.</summary>
        public string Detail { get; }

        public GazeCheckState State { get; }

        /// <summary>The repair for this row, or <see cref="GazeFixId.None" />.</summary>
        public GazeFixId Fix { get; }
    }

    /// <summary>A repair the setup surfaces can offer as a named button.</summary>
    internal enum GazeFixId
    {
        None = 0,
        AssignDefaultProfile,
        AddRigBinding,
        UseSceneCameraAsPlayer,
        AddPlayerAnchor,
        RemoveDuplicateRigBinding
    }

    /// <summary>
    ///     The read-only answer to "is this character ready, and what will the setup button do?" —
    ///     computed fresh on every repaint, so it never promises something it cannot deliver.
    /// </summary>
    internal readonly struct GazePreflight
    {
        internal GazePreflight(IReadOnlyList<GazeCheck> checks)
        {
            Checks = checks;
        }

        public IReadOnlyList<GazeCheck> Checks { get; }

        /// <summary>
        ///     True when nothing is blocked and nothing remains for setup to do. Note that a
        ///     character can be perfectly functional without being "ready" in this sense — see
        ///     <see cref="IsFunctional" />.
        /// </summary>
        public bool IsReady
        {
            get
            {
                if (Checks == null) return false;
                for (int i = 0; i < Checks.Count; i++)
                    if (Checks[i].State is GazeCheckState.Fixable or GazeCheckState.Blocked)
                        return false;
                return true;
            }
        }

        /// <summary>
        ///     True when gaze will actually do something at runtime — i.e. nothing is blocked.
        /// </summary>
        /// <remarks>
        ///     This is the distinction that makes the gaze setup card different from Body
        ///     Animation's. Body Animation is inert without content, so "not configured" and "not
        ///     working" are the same thing. Gaze works out of the box: it resolves the rig from the
        ///     avatar, auto-creates a player anchor on the main camera, and falls back to SDK
        ///     defaults with no profile asset. Only a missing head bone can actually stop it.
        ///     Telling a working character it is "not set up" is precisely the failure this whole
        ///     round exists to fix.
        /// </remarks>
        public bool IsFunctional => !HasBlocker;

        /// <summary>True when at least one row is blocked — gaze cannot run until the user acts.</summary>
        public bool HasBlocker
        {
            get
            {
                if (Checks == null) return false;
                for (int i = 0; i < Checks.Count; i++)
                    if (Checks[i].State == GazeCheckState.Blocked) return true;
                return false;
            }
        }

        /// <summary>The first blocked row, for the "one thing needs you first" callout.</summary>
        public bool TryGetBlocker(out GazeCheck blocker)
        {
            if (Checks != null)
            {
                for (int i = 0; i < Checks.Count; i++)
                {
                    if (Checks[i].State != GazeCheckState.Blocked) continue;
                    blocker = Checks[i];
                    return true;
                }
            }

            blocker = default;
            return false;
        }
    }

    /// <summary>Outcome of the facing-direction check on a character's head bone.</summary>
    internal enum GazeFacingState
    {
        /// <summary>The bone could not be measured — no head bone, or no character root.</summary>
        Unknown,

        /// <summary>The head bone faces the same way the character does.</summary>
        Pass,

        /// <summary>The head bone's forward axis does not match the character's; gaze will aim wrong.</summary>
        Fail,

        /// <summary>The rig authors its own gaze axes, so there is nothing to infer.</summary>
        Calibrated
    }

    /// <summary>
    ///     The facing-direction check: the module's hardest-to-discover requirement, as a measured
    ///     angle rather than a paragraph of prose.
    /// </summary>
    internal readonly struct GazeFacingReport
    {
        internal GazeFacingReport(GazeFacingState state, float angleDegrees, string detail)
        {
            State = state;
            AngleDegrees = angleDegrees;
            Detail = detail;
        }

        public GazeFacingState State { get; }

        /// <summary>Angle between the head bone's forward and the character's, or <c>NaN</c> when unmeasured.</summary>
        public float AngleDegrees { get; }

        /// <summary>What it means and what to do about it, in the words the documentation uses.</summary>
        public string Detail { get; }

        /// <summary>Whether an angle was actually measured.</summary>
        public bool Measured => State is GazeFacingState.Pass or GazeFacingState.Fail;
    }

    /// <summary>Which link of the player-anchor chain actually decided what this character watches.</summary>
    /// <remarks>
    ///     Mirrors <c>ConvaiGazeController.TryResolvePlayerAnchor</c> — Player Anchor Override, then
    ///     the active Gaze Player Anchor provider, then <c>Camera.main</c>. Naming the winning link
    ///     is the difference between "it is looking at the wrong thing" and knowing which field to
    ///     change.
    /// </remarks>
    internal enum GazeAnchorSource
    {
        /// <summary>Nothing resolves — the scene has no camera and no anchor yet.</summary>
        Unresolved,

        /// <summary>The controller's own Player Anchor Override field won.</summary>
        PlayerAnchorOverride,

        /// <summary>A Gaze Player Anchor on the character carries an Explicit Anchor, and it won.</summary>
        PlayerAnchorProvider,

        /// <summary>Nothing overrides it, so the camera tagged MainCamera is the player.</summary>
        MainCamera,

        /// <summary>No camera is tagged MainCamera, so one will be guessed at runtime.</summary>
        GuessedCamera
    }

    /// <summary>The resolved answer to "what does this character treat as the player, and why?"</summary>
    internal readonly struct GazeAnchorReport
    {
        internal GazeAnchorReport(
            GazeAnchorSource source,
            Transform anchor,
            bool providerPresent,
            Transform providerExplicitAnchor,
            bool mainCameraTagged,
            string detail)
        {
            Source = source;
            Anchor = anchor;
            ProviderPresent = providerPresent;
            ProviderExplicitAnchor = providerExplicitAnchor;
            MainCameraTagged = mainCameraTagged;
            Detail = detail;
        }

        /// <summary>Which link of the chain won.</summary>
        public GazeAnchorSource Source { get; }

        /// <summary>The transform this character will treat as the player, when one resolves.</summary>
        public Transform Anchor { get; }

        /// <summary>Whether an enabled Gaze Player Anchor exists on the character.</summary>
        public bool ProviderPresent { get; }

        /// <summary>The provider's own Explicit Anchor, when it carries one.</summary>
        public Transform ProviderExplicitAnchor { get; }

        /// <summary>Whether any camera in the scene carries the MainCamera tag.</summary>
        public bool MainCameraTagged { get; }

        /// <summary>What was found, phrased for the setup card's "Who it watches" row.</summary>
        public string Detail { get; }
    }

    /// <summary>What the setup button should do, beyond assigning a personality.</summary>
    internal struct GazeSetupOptions
    {
        /// <summary>Optional capabilities the user ticked on the setup card.</summary>
        public IReadOnlyList<GazeCapabilityId> Capabilities;

        /// <summary>
        ///     Whether setup should give the character a Gaze Profile of its own, creating the asset.
        ///     True for a person pressing a setup button, who asked for it and can see what appeared.
        ///     False for callers that must not write to the project — a character with no profile is
        ///     working, so leaving it out finishes the setup rather than half-doing it.
        /// </summary>
        public bool AssignProfile;

        /// <summary>The capabilities a first-time user is best served by, pre-ticked.</summary>
        internal static readonly GazeCapabilityId[] RecommendedCapabilities =
        {
            GazeCapabilityId.PlayerAttention,
            GazeCapabilityId.AttentionGrounding
        };

        public static GazeSetupOptions Default =>
            new() { Capabilities = RecommendedCapabilities, AssignProfile = true };
    }

    /// <summary>Outcome of a setup run, rendered identically by every caller.</summary>
    internal readonly struct GazeSetupResult
    {
        internal GazeSetupResult(bool changed, string summary, IReadOnlyList<string> notes)
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
    ///     The single code path that inspects and configures gaze on a character. The controller
    ///     inspector and the Gaze editor window both go through here, so every surface produces the
    ///     same verdict, the same repairs, and the same wording.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Inspect" /> is read-only and safe to call every repaint.
    ///         <see cref="Apply" /> mutates, in one undo group, and reports everything it did and
    ///         everything it could not do.
    ///     </para>
    ///     <para>
    ///         Adding the component itself is deliberately NOT this service's job — that is Unity's
    ///         own <c>Add Component</c>, the gesture every Unity user already knows. This service
    ///         starts from a controller that already exists.
    ///     </para>
    ///     <para>
    ///         <see cref="GazeSetupTroubleshooter" /> remains the finding engine; this service is
    ///         its consumer plus the repair mapping. The finding logic is not duplicated here.
    ///     </para>
    /// </remarks>
    internal static class GazeSetupService
    {
        internal const string CheckRig = "gaze.setup.rig";
        internal const string CheckCharacter = "gaze.setup.character";
        internal const string CheckWatches = "gaze.setup.watches";
        internal const string CheckPersonality = "gaze.setup.personality";

        /// <summary>
        ///     GUID of the shipped Gaze Profile. This is the same asset the embodiment preset's
        ///     gaze slot points at, so "add a personality" and "use the preset" cannot disagree —
        ///     one shipped default, not two that drift.
        /// </summary>
        private const string DefaultProfileGuid = "6ff0277dba48b3c4b9739a1f3c4021d1";

        /// <summary>
        ///     Angle within which the head bone counts as facing the same way as the character. Wide
        ///     on purpose: this catches a rig exported sideways or backwards, not a few degrees of
        ///     authored head tilt in the bind pose.
        /// </summary>
        private const float FacingToleranceDegrees = 45f;

        // ------------------------------------------------------------------ preflight

        /// <summary>
        ///     Evaluates the character without changing anything. Every row states what was found,
        ///     so the user can see what setup will and will not do before pressing it.
        /// </summary>
        internal static GazePreflight Inspect(ConvaiGazeController controller)
        {
            var checks = new List<GazeCheck>(4);
            if (controller == null) return new GazePreflight(checks);

            checks.Add(InspectRig(controller));
            checks.Add(InspectCharacter(controller));
            checks.Add(InspectWatches(controller));
            checks.Add(InspectPersonality(controller));
            return new GazePreflight(checks);
        }

        private static GazeCheck InspectRig(ConvaiGazeController controller)
        {
            Transform root = ResolveRoot(controller);
            var input = GazeSetupTroubleshooter.GatherFrom(controller, FindProfileProperty(controller), true);

            if (input.RigBindingCount > 1)
            {
                return new GazeCheck(CheckRig, "Rig",
                    "more than one Character Rig under this character — keep exactly one",
                    GazeCheckState.Blocked, GazeFixId.RemoveDuplicateRigBinding);
            }

            if (!input.HasHeadBone)
            {
                return new GazeCheck(CheckRig, "Rig",
                    "no head bone found — gaze rotates the head and eye bones, so it cannot run yet",
                    GazeCheckState.Blocked,
                    root != null && root.GetComponentInChildren<StandardRigBinding>(true) == null
                        ? GazeFixId.AddRigBinding
                        : GazeFixId.None);
            }

            string eyes = GazeSetupTroubleshooter.ResolveEyeBackend(in input) switch
            {
                GazeEyeBackend.EyeBones => "head, neck and both eyes found",
                GazeEyeBackend.EyeLookBlendshapes => "head found, eyes driven by blendshapes",
                _ => "head found, eyes will follow the head (no eye bones or shapes)"
            };

            if (!input.HasNeckBone)
                eyes = eyes.Replace("head, neck and", "head and");

            return new GazeCheck(CheckRig, "Rig",
                input.IsHumanoid ? $"Humanoid — {eyes}" : $"custom rig — {eyes}", GazeCheckState.Ok);
        }

        private static GazeCheck InspectCharacter(ConvaiGazeController controller)
        {
            var character = controller.GetComponentInParent<ConvaiCharacter>(true);
            if (character != null)
                return new GazeCheck(CheckCharacter, "Convai Character", character.name, GazeCheckState.Ok);

            var context = controller.GetComponentInParent<EmbodimentContext>(true);
            return context != null
                ? new GazeCheck(CheckCharacter, "Convai Character",
                    $"none — using the embodiment context on '{context.name}'", GazeCheckState.Ok)
                : new GazeCheck(CheckCharacter, "Convai Character",
                    "none in this hierarchy — gaze still runs, but conversation states will not drive it",
                    GazeCheckState.Optional);
        }

        /// <summary>
        ///     Renders <see cref="InspectAnchor" /> as a checklist row. The resolution itself is not
        ///     repeated here — the row and every other surface read the same report, so the card
        ///     cannot name one anchor while the character watches another.
        /// </summary>
        private static GazeCheck InspectWatches(ConvaiGazeController controller)
        {
            GazeAnchorReport anchor = InspectAnchor(controller);
            GazeCheckState state = anchor.Source switch
            {
                GazeAnchorSource.GuessedCamera => GazeCheckState.Fixable,
                GazeAnchorSource.Unresolved => GazeCheckState.Optional,
                _ => GazeCheckState.Ok
            };

            GazeFixId fix = anchor.Source == GazeAnchorSource.GuessedCamera
                ? GazeFixId.UseSceneCameraAsPlayer
                : GazeFixId.None;

            return new GazeCheck(CheckWatches, "Who it watches", anchor.Detail, state, fix);
        }

        /// <summary>
        ///     Resolves what this character treats as the player, and which link of the chain
        ///     decided it — Player Anchor Override, then the character's Gaze Player Anchor, then
        ///     the camera tagged MainCamera.
        /// </summary>
        /// <remarks>
        ///     The order mirrors <c>ConvaiGazeController.TryResolvePlayerAnchor</c> exactly. This is
        ///     the single most common cause of "why isn't this character looking at me?", and
        ///     reporting only the first and last link — as this check used to — is wrong on any
        ///     character whose Gaze Player Anchor carries an Explicit Anchor of its own.
        /// </remarks>
        internal static GazeAnchorReport InspectAnchor(ConvaiGazeController controller)
        {
            if (controller == null)
                return new GazeAnchorReport(
                    GazeAnchorSource.Unresolved, null, false, null, Camera.main != null,
                    "no Gaze component to resolve an anchor for");

            Transform root = ResolveRoot(controller);
            var provider = root != null
                ? root.GetComponentInChildren<PlayerAnchorTargetProvider>(true)
                : null;
            bool providerPresent = provider != null && provider.isActiveAndEnabled;
            Transform providerAnchor = providerPresent ? provider.ExplicitAnchor : null;
            bool mainCameraTagged = Camera.main != null;

            var serialized = new SerializedObject(controller);
            var overrideAnchor = serialized.FindProperty("playerAnchorOverride")?.objectReferenceValue as Transform;
            if (overrideAnchor != null)
            {
                return new GazeAnchorReport(
                    GazeAnchorSource.PlayerAnchorOverride, overrideAnchor, providerPresent, providerAnchor,
                    mainCameraTagged, $"'{overrideAnchor.name}'");
            }

            if (providerAnchor != null)
            {
                return new GazeAnchorReport(
                    GazeAnchorSource.PlayerAnchorProvider, providerAnchor, true, providerAnchor,
                    mainCameraTagged,
                    $"'{providerAnchor.name}', set as the Explicit Anchor on this character's Player Anchor");
            }

            if (mainCameraTagged)
            {
                return new GazeAnchorReport(
                    GazeAnchorSource.MainCamera, Camera.main.transform, providerPresent, null, true,
                    Camera.main.name);
            }

            Camera fallback = FirstEnabledCamera();
            return fallback != null
                ? new GazeAnchorReport(
                    GazeAnchorSource.GuessedCamera, fallback.transform, providerPresent, null, false,
                    $"no camera is tagged MainCamera — '{fallback.name}' will be guessed at runtime")
                : new GazeAnchorReport(
                    GazeAnchorSource.Unresolved, null, providerPresent, null, false,
                    "no camera in the scene yet — the main camera will be used once one exists");
        }

        /// <summary>
        ///     Measures the head bone's forward against the character's — the requirement that
        ///     catches people out, because a rig exported any other way aims sideways and the only
        ///     symptom is a character looking at nothing.
        /// </summary>
        /// <remarks>
        ///     Works in Edit Mode: when the gaze chain has not bound yet it resolves the head bone
        ///     the same way the runtime will. In Play Mode the bound chain is preferred, because it
        ///     is authoritative and knows whether the rig authors its own axes.
        /// </remarks>
        internal static GazeFacingReport InspectFacing(ConvaiGazeController controller)
        {
            if (controller == null)
                return new GazeFacingReport(GazeFacingState.Unknown, float.NaN, GazeEditorStrings.ForwardAxisUnknown);

            Transform root = ResolveRoot(controller);
            Transform aim;

            GazeChainCalibration chain = controller.Chain;
            if (chain != null && chain.IsBound)
            {
                // A calibrated rig has told us its axes explicitly, so there is nothing to infer.
                if (chain.TryGetGazeReferenceFrame(out _))
                    return new GazeFacingReport(
                        GazeFacingState.Calibrated, float.NaN, GazeEditorStrings.ForwardAxisCalibrated);

                aim = chain.Head != null ? chain.Head : chain.Neck;
            }
            else
            {
                if (HasAuthoredGazeAxes(root))
                    return new GazeFacingReport(
                        GazeFacingState.Calibrated, float.NaN, GazeEditorStrings.ForwardAxisCalibrated);

                GazeBoneReport bones = GazeSetupTroubleshooter.ResolveBones(root);
                aim = bones.Head != null ? bones.Head : bones.Neck;
            }

            if (aim == null || root == null)
                return new GazeFacingReport(GazeFacingState.Unknown, float.NaN, GazeEditorStrings.ForwardAxisUnknown);

            // Measure the HEAD BONE against the character, not the chain's rest forward. The rest
            // forward is captured from the character root at bind time, so comparing the two is
            // tautological — it reads ~0° on every rig, including a badly mis-oriented one, and
            // would pass exactly the rigs this check exists to catch.
            float error = Vector3.Angle(aim.forward, root.forward);
            return error < FacingToleranceDegrees
                ? new GazeFacingReport(GazeFacingState.Pass, error, GazeEditorStrings.ForwardAxisPass)
                : new GazeFacingReport(GazeFacingState.Fail, error, GazeEditorStrings.ForwardAxisFail);
        }

        /// <summary>The bones gaze resolves on this character, as transforms rather than booleans.</summary>
        internal static GazeBoneReport ResolveBones(ConvaiGazeController controller) =>
            GazeSetupTroubleshooter.ResolveBones(ResolveRoot(controller));

        /// <summary>
        ///     Whether the character's rig binding authors its own gaze axes. Read through the
        ///     serialized field so this works from Edit Mode without binding the chain.
        /// </summary>
        private static bool HasAuthoredGazeAxes(Transform root)
        {
            StandardRigBinding binding = GazeSetupTroubleshooter.ResolveBinding(root);
            if (binding == null) return false;

            SerializedProperty enabled =
                new SerializedObject(binding).FindProperty("gazeAxisCalibrationEnabled");
            return enabled != null && enabled.boolValue;
        }

        private static GazeCheck InspectPersonality(ConvaiGazeController controller)
        {
            ConvaiGazeProfile profile = ResolveAssignedProfile(controller);
            if (profile != null)
                return new GazeCheck(CheckPersonality, "Personality", profile.name, GazeCheckState.Ok);

            return TryLoadDefaultProfile() != null
                ? new GazeCheck(CheckPersonality, "Personality",
                    "none assigned — using SDK defaults, which work; assign one to tune the character",
                    GazeCheckState.Fixable, GazeFixId.AssignDefaultProfile)
                : new GazeCheck(CheckPersonality, "Personality",
                    "none assigned — using SDK defaults, and no profile asset was found in this project",
                    GazeCheckState.Optional);
        }

        // ------------------------------------------------------------------ apply

        /// <summary>
        ///     Configures the character in a single undo step and reports exactly what happened.
        ///     Anything it cannot do is reported rather than silently skipped.
        /// </summary>
        internal static GazeSetupResult Apply(ConvaiGazeController controller, GazeSetupOptions options)
        {
            var notes = new List<string>(6);
            if (controller == null)
                return new GazeSetupResult(false, "No Gaze component to set up.", notes);

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Set Up Gaze");
            bool changed = false;

            if (options.AssignProfile) changed |= AssignDefaultProfile(controller, notes);
            changed |= UseSceneCameraAsPlayer(controller, notes);
            changed |= ApplyCapabilities(controller, options.Capabilities, notes);

            Undo.CollapseUndoOperations(undoGroup);

            string summary = changed
                ? "Gaze is set up on this character."
                : "Nothing to change — this character was already set up.";
            return new GazeSetupResult(changed, summary, notes);
        }

        /// <summary>Applies one repair from a preflight row or a troubleshooter finding.</summary>
        internal static bool ApplyFix(ConvaiGazeController controller, GazeFixId fix)
        {
            if (controller == null) return false;
            var notes = new List<string>(1);

            return fix switch
            {
                GazeFixId.AssignDefaultProfile => AssignDefaultProfile(controller, notes),
                GazeFixId.AddRigBinding => AddRigBinding(controller, notes),
                GazeFixId.UseSceneCameraAsPlayer => UseSceneCameraAsPlayer(controller, notes),
                GazeFixId.AddPlayerAnchor => AddPlayerAnchor(controller, notes),
                GazeFixId.RemoveDuplicateRigBinding => PingDuplicateRigBindings(controller),
                _ => false
            };
        }

        /// <summary>Button text for a repair, or <c>null</c> when this service cannot perform it.</summary>
        internal static string DescribeFix(GazeFixId fix) => fix switch
        {
            GazeFixId.AssignDefaultProfile => "Add a Personality",
            GazeFixId.AddRigBinding => "Add Rig Binding",
            GazeFixId.UseSceneCameraAsPlayer => "Use This Camera",
            GazeFixId.AddPlayerAnchor => "Add Player Anchor",
            GazeFixId.RemoveDuplicateRigBinding => "Show Me",
            _ => null
        };

        // ------------------------------------------------------------------ capabilities

        /// <summary>
        ///     Adds every capability in <paramref name="wanted" /> that is missing, and removes
        ///     every one that is present but not wanted — so the setup card's checkboxes behave
        ///     like checkboxes rather than like one-way buttons.
        /// </summary>
        internal static bool ApplyCapabilities(
            ConvaiGazeController controller, IReadOnlyList<GazeCapabilityId> wanted, List<string> notes)
        {
            Transform root = ResolveRoot(controller);
            if (root == null) return false;

            bool changed = false;
            foreach (GazeCapabilityId id in (GazeCapabilityId[])Enum.GetValues(typeof(GazeCapabilityId)))
            {
                bool want = Contains(wanted, id);
                bool have = GazeCapabilities.IsPresentUnder(root, GazeCapabilities.ProviderTypeOf(id));
                if (want == have) continue;

                changed |= want
                    ? AddCapability(root, id, notes)
                    : RemoveCapability(root, id, notes);
            }

            return changed;
        }

        private static bool AddCapability(Transform root, GazeCapabilityId id, List<string> notes)
        {
            Type providerType = GazeCapabilities.ProviderTypeOf(id);
            if (providerType == null) return false;

            // A disabled instance already exists: re-enabling is the honest repair, because adding
            // a second one would leave two components fighting over the same job.
            var existing = root.GetComponentInChildren(providerType, true) as MonoBehaviour;
            if (existing != null)
            {
                Undo.RecordObject(existing, "Enable Gaze Capability");
                existing.enabled = true;
                EditorUtility.SetDirty(existing);
                notes?.Add($"Re-enabled \"{GazeCapabilities.DisplayNameOf(id)}\" on '{existing.gameObject.name}'.");
                return true;
            }

            Undo.AddComponent(root.gameObject, providerType);
            notes?.Add($"Added \"{GazeCapabilities.DisplayNameOf(id)}\".");
            return true;
        }

        private static bool RemoveCapability(Transform root, GazeCapabilityId id, List<string> notes)
        {
            Type providerType = GazeCapabilities.ProviderTypeOf(id);
            if (providerType == null) return false;

            var existing = root.GetComponentInChildren(providerType, true) as MonoBehaviour;
            if (existing == null) return false;

            Undo.DestroyObjectImmediate(existing);
            notes?.Add($"Removed \"{GazeCapabilities.DisplayNameOf(id)}\".");
            return true;
        }

        private static bool Contains(IReadOnlyList<GazeCapabilityId> list, GazeCapabilityId id)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i] == id) return true;
            return false;
        }

        // ------------------------------------------------------------------ individual steps

        /// <summary>
        ///     Gives the character its own Gaze profile, built from the SDK defaults.
        /// </summary>
        /// <remarks>
        ///     This used to assign a profile that lives inside the package, so a fresh character was
        ///     immediately tuning something it could not keep. Nothing in a Gaze profile is content —
        ///     it is all tuning — so there is nothing to reference from the package at all: the
        ///     character gets a profile of its own, seeded from
        ///     <c>ConvaiGazeProfile.CreateDefault()</c>, which is exactly what the shipped asset held.
        /// </remarks>
        private static bool AssignDefaultProfile(ConvaiGazeController controller, List<string> notes)
        {
            if (ResolveAssignedProfile(controller) != null) return false;

            ConvaiCopyOnWriteResult created = ConvaiCopyOnWrite.CreateAndAssign(
                ConvaiGazeProfile.CreateDefault(), controller, "Gaze", "_Gaze", "profile");

            if (!created.Succeeded)
            {
                notes?.Add(created.FailureReason);
                return false;
            }

            notes?.Add(
                $"Created '{System.IO.Path.GetFileNameWithoutExtension(created.AssetPath)}' for this " +
                "character, so tuning it affects nobody else.");
            return true;
        }

        private static bool AddRigBinding(ConvaiGazeController controller, List<string> notes)
        {
            Transform root = ResolveRoot(controller);
            if (root == null) return false;
            if (root.GetComponentInChildren<StandardRigBinding>(true) != null) return false;

            Undo.AddComponent<StandardRigBinding>(root.gameObject);
            notes?.Add(
                $"Added Character Rig to '{root.name}'. Point its Head at the character's head " +
                "bone — gaze cannot author a rig for you.");
            return true;
        }

        private static bool UseSceneCameraAsPlayer(ConvaiGazeController controller, List<string> notes)
        {
            var serialized = new SerializedObject(controller);
            SerializedProperty property = serialized.FindProperty("playerAnchorOverride");
            if (property == null || property.objectReferenceValue != null) return false;

            // Only when the answer is unambiguous. Guessing between several cameras would be worse
            // than leaving the runtime's own fallback to do it.
            if (Camera.main != null) return false;

            Camera camera = SingleEnabledCamera();
            if (camera == null) return false;

            property.objectReferenceValue = camera.transform;
            serialized.ApplyModifiedProperties();
            notes?.Add(
                $"Set '{camera.name}' as the player, because no camera in this scene is tagged " +
                "MainCamera. Tagging it instead would work for every character at once.");
            return true;
        }

        private static bool AddPlayerAnchor(ConvaiGazeController controller, List<string> notes)
        {
            Transform root = ResolveRoot(controller);
            if (root == null) return false;
            if (root.GetComponentInChildren<PlayerAnchorTargetProvider>(true) != null) return false;

            Undo.AddComponent<PlayerAnchorTargetProvider>(root.gameObject);
            notes?.Add("Added a player anchor.");
            return true;
        }

        private static bool PingDuplicateRigBindings(ConvaiGazeController controller)
        {
            Transform root = ResolveRoot(controller);
            StandardRigBinding[] bindings = root != null
                ? root.GetComponentsInChildren<StandardRigBinding>(true)
                : Array.Empty<StandardRigBinding>();
            if (bindings.Length <= 1) return false;

            // Deleting one is the user's call — which is the "right" binding is a content decision,
            // and picking wrong would silently rebind the character to the wrong skeleton.
            EditorGUIUtility.PingObject(bindings[bindings.Length - 1].gameObject);
            Selection.activeGameObject = bindings[bindings.Length - 1].gameObject;
            return false;
        }

        // ------------------------------------------------------------------ resolution helpers

        /// <summary>The profile the controller would actually use, or <c>null</c> for SDK defaults.</summary>
        internal static ConvaiGazeProfile ResolveAssignedProfile(ConvaiGazeController controller)
        {
            if (controller == null) return null;
            var serialized = new SerializedObject(controller);
            return serialized.FindProperty("profile")?.objectReferenceValue as ConvaiGazeProfile;
        }

        /// <summary>
        ///     Loads the shipped Gaze Profile by GUID, falling back to a project-wide search so a
        ///     relocated or re-imported package still resolves.
        /// </summary>
        internal static ConvaiGazeProfile TryLoadDefaultProfile()
        {
            string path = AssetDatabase.GUIDToAssetPath(DefaultProfileGuid);
            if (!string.IsNullOrEmpty(path))
            {
                var byGuid = AssetDatabase.LoadAssetAtPath<ConvaiGazeProfile>(path);
                if (byGuid != null) return byGuid;
            }

            string[] candidates = AssetDatabase.FindAssets($"t:{nameof(ConvaiGazeProfile)}");
            for (int i = 0; i < candidates.Length; i++)
            {
                var profile = AssetDatabase.LoadAssetAtPath<ConvaiGazeProfile>(
                    AssetDatabase.GUIDToAssetPath(candidates[i]));
                if (profile != null) return profile;
            }

            return null;
        }

        /// <summary>The character root gaze considers its scope: the embodiment context, or this transform.</summary>
        internal static Transform ResolveRoot(ConvaiGazeController controller)
        {
            if (controller == null) return null;
            var context = controller.GetComponentInParent<EmbodimentContext>(true);
            return context != null ? context.transform : controller.transform;
        }

        private static SerializedProperty FindProfileProperty(ConvaiGazeController controller) =>
            new SerializedObject(controller).FindProperty("profile");

        private static Camera FirstEnabledCamera()
        {
            Camera[] cameras = ConvaiObjectFind.All<Camera>(FindObjectsInactive.Exclude);
            return cameras.Length > 0 ? cameras[0] : null;
        }

        private static Camera SingleEnabledCamera()
        {
            Camera[] cameras = ConvaiObjectFind.All<Camera>(FindObjectsInactive.Exclude);
            return cameras.Length == 1 ? cameras[0] : null;
        }
    }
}
