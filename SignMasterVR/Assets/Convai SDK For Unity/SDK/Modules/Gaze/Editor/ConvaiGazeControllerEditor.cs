using System;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Editor.Inspectors.Framework;
using Convai.Modules.Gaze.Components;
using Convai.Modules.Gaze.Core;
using Convai.Modules.Gaze.Core.Diagnostics;
using Convai.Modules.Gaze.Data;
using Convai.Modules.Gaze.Editor;
using UnityEditor;
using UnityEngine;
using Convai.Editor.Ownership;
using Convai.Editor.UI;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Modules.Gaze.Editor
{
    /// <summary>
    ///     The Gaze component's inspector, and the module's primary product surface.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The design decision that shapes everything here.</b> Gaze already works with no
    ///         configuration: it resolves the head, neck and eye bones from the avatar, creates a
    ///         player anchor on the main camera, and runs on SDK defaults with no profile asset.
    ///         Add the component, press Play, and the character looks at you.
    ///     </para>
    ///     <para>
    ///         The previous inspector never said so. It opened five expanded sections and led with
    ///         eight controls named <i>Player Anchor Override</i>, <i>Aim Point</i>, <i>Local Aim
    ///         Offset</i>, <i>Focus Fidelity</i>, <i>Allow Scripted Overrides</i>, <i>Lock Blocks
    ///         Glances</i> and <i>Auto Create Player Anchor</i>, plus a paragraph about priority
    ///         tiers and interest budgets. It read like a system that must be configured before it
    ///         will do anything.
    ///     </para>
    ///     <para>
    ///         So this surface's job is the opposite of Body Animation's, whose inspector had to
    ///         <em>get the module working</em>. This one has to <b>prove it is already working and
    ///         hand over the one dial that matters</b>. Only a rig it cannot drive produces a
    ///         "not working" state; everything else is a suggestion, not a warning.
    ///     </para>
    ///     <para>
    ///         Deliberately narrow. The full profile surface, scene-wide target authoring, the rig
    ///         report and the six advanced targeting fields live in
    ///         <see cref="ConvaiGazeEditorWindow" />, reached from the footer link. Cramming those in
    ///         here is what made the previous version unusable.
    ///     </para>
    /// </remarks>
    [CustomEditor(typeof(ConvaiGazeController))]
    internal sealed class ConvaiGazeControllerEditor : ConvaiInspectorEditor
    {
        private const string SectionPersonality = "Personality";
        private const string SectionWhoItWatches = "WhoItWatches";
        private const string SectionLive = "LiveGaze";
        private const int TraceTailLength = 5;

        private static readonly GUIContent AddPersonalityButton = new(
            "Add a Personality", "Assigns a default Gaze Profile so this character looks around its own way.");

        /// <summary>
        ///     The personality slot. This Inspector had no such field at all: the button above was
        ///     the only entry point and it creates a new asset, so assigning one of the SDK's
        ///     shipped personalities meant the Debug inspector or hand-editing the scene file.
        /// </summary>
        private static readonly GUIContent PersonalityField = new(
            "Personality",
            "The asset holding how this character looks around. Can be shared with other characters.");

        private SerializedProperty _profile;
        private SerializedProperty _playerAnchorOverride;
        private SerializedProperty _eyeContactMode;

        private readonly GazeSnapshot _snapshot = new();
        private GazePreflight _preflight;
        private ConvaiEditorRefreshTimer _preflightTimer;
        private bool _preflightValid;

        /// <summary>
        ///     How long a preflight result stays good for. <see cref="RequiresConstantRepaint" /> is
        ///     true in Play Mode so the live readout stays current, which used to mean a full
        ///     preflight — component scans, an avatar walk, a camera lookup — ran once per rendered
        ///     frame. None of what it inspects can change faster than a user can act.
        /// </summary>
        private const double PreflightIntervalSeconds = 0.25d;

        /// <summary>The personality asset's serialized view, rebuilt only when the asset changes.</summary>
        private SerializedObject _profileSerialized;

        protected override void OnEnable()
        {
            base.OnEnable();
            _profile = serializedObject.FindProperty("profile");
            _playerAnchorOverride = serializedObject.FindProperty("playerAnchorOverride");
            _eyeContactMode = serializedObject.FindProperty("eyeContactMode");
            _preflightValid = false;
        }

        protected override string Title => "Gaze";

        protected override string Subtitle => "Eye & head contact";

        protected override GUIContent StatusChip => CurrentChip.Content;

        protected override Color StatusChipTint => CurrentChip.Tint;

        private ConvaiEditorChip CurrentChip
        {
            get
            {
                if (!_preflight.IsFunctional) return ConvaiEditorChips.ActionNeeded;
                return EditorApplication.isPlaying ? ConvaiEditorChips.Live : ConvaiEditorChips.Ready;
            }
        }

        /// <summary>Keeps the live gaze readout updating while the scene plays.</summary>
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        protected override void OnBeforeInspectorGUI()
        {
            // A click may have just fixed the very thing the checklist reports, so a changed GUI
            // re-preflights at the top of the next pass instead of waiting out the interval.
            if (GUI.changed)
                _preflightTimer.Invalidate(true);

            if (!_preflightTimer.ShouldRefresh(_preflightValid, PreflightIntervalSeconds))
                return;

            _preflightValid = true;
            _preflight = GazeSetupService.Inspect((ConvaiGazeController)target);
        }

        protected override void DrawBody()
        {
            var controller = (ConvaiGazeController)target;

            if (!_preflight.IsFunctional)
            {
                DrawNotWorkingState(controller);
                return;
            }

            DrawSuggestions(controller);
            DrawPersonalitySection(controller);
            DrawWhoItWatchesSection(controller);
            DrawLiveGazeSection(controller);
            DrawFooterLink(controller);
        }

        // ------------------------------------------------------------------ not working

        /// <summary>
        ///     What a user sees when the rig cannot drive gaze. A checklist stating what is and is
        ///     not ready <em>before</em> they press anything, one named blocker, and its fix.
        /// </summary>
        private void DrawNotWorkingState(ConvaiGazeController controller)
        {
            InfoBox(
                "What this does",
                "Aims the character's eyes, head and body at whoever it is talking to — with idle " +
                "life, blinking, nodding, and full-body turns for anything far off to the side. " +
                "No setup beyond adding this component.");

            EditorGUILayout.LabelField("Before this works", ConvaiEditorStyles.SectionTitle);
            DrawChecklist();

            EditorGUILayout.Space(6f);
            if (!_preflight.TryGetBlocker(out GazeCheck blocker)) return;

            const string body =
                "Gaze rotates the character's head and eye bones, so it cannot run until it can find " +
                "them. Set the model's Rig → Animation Type to Humanoid, or add a Standard Rig " +
                "Binding and point its Head at the character's head bone.";

            // The message box carries the repair inline, so there is no loose button beneath it.
            string fixLabel = GazeSetupService.DescribeFix(blocker.Fix);
            ErrorBox("One thing needs you first", $"{blocker.Detail}.\n\n{body}",
                fixLabel, fixLabel == null ? null : () => GazeSetupService.ApplyFix(controller, blocker.Fix));
        }

        private void DrawChecklist()
        {
            if (_preflight.Checks == null) return;

            for (int i = 0; i < _preflight.Checks.Count; i++)
            {
                GazeCheck check = _preflight.Checks[i];
                (string glyph, Color color) = GlyphFor(check.State);

                using (new EditorGUILayout.HorizontalScope())
                {
                    Color previous = GUI.color;
                    GUI.color = color;
                    GUILayout.Label(glyph, GUILayout.Width(16f));
                    GUI.color = previous;

                    GUILayout.Label(check.Label, ConvaiEditorStyles.RowLabel, GUILayout.Width(118f));
                    GUILayout.Label(check.Detail, ConvaiEditorStyles.MicroLabel);
                }
            }
        }

        private static (string glyph, Color color) GlyphFor(GazeCheckState state) => state switch
        {
            GazeCheckState.Ok => (ConvaiEditorGlyphs.Status.Ok, Theme.AccentBright),
            GazeCheckState.Fixable => ("•", Theme.StatusInfo),
            GazeCheckState.Blocked => (ConvaiEditorGlyphs.Status.Fail, Theme.StatusError),
            _ => (ConvaiEditorGlyphs.Status.Neutral, Theme.StatusIdle)
        };

        /// <summary>
        ///     Fixable rows as a compact suggestion strip rather than as warning boxes. A character
        ///     with no personality assigned is working correctly, and colouring that as a problem
        ///     is the false alarm this rewrite removes.
        /// </summary>
        private void DrawSuggestions(ConvaiGazeController controller)
        {
            if (_preflight.Checks == null) return;

            for (int i = 0; i < _preflight.Checks.Count; i++)
            {
                GazeCheck check = _preflight.Checks[i];
                if (check.State != GazeCheckState.Fixable) continue;

                string fixLabel = GazeSetupService.DescribeFix(check.Fix);
                if (fixLabel == null) continue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    Color previous = GUI.color;
                    GUI.color = Theme.StatusInfo;
                    GUILayout.Label("•", GUILayout.Width(14f));
                    GUI.color = previous;

                    GUILayout.Label(check.Detail, ConvaiEditorStyles.CaptionWrapped);
                    Rect fixRect = GUILayoutUtility.GetRect(140f, 20f, GUILayout.Width(140f));
                    if (ConvaiEditorControls.GhostButton(fixRect, new GUIContent(fixLabel)))
                        GazeSetupService.ApplyFix(controller, check.Fix);
                }
            }
        }

        // ------------------------------------------------------------------ personality

        private void DrawPersonalitySection(ConvaiGazeController controller)
        {
            if (!DrawSection(
                    SectionPersonality, "Personality", ConvaiEditorGlyphs.Profile,
                    // Read from the bound property, not GazeSetupService.ResolveAssignedProfile:
                    // the service builds a SerializedObject per call, and this runs on every
                    // repaint of a surface that repaints continuously in Play Mode. Gaze has no
                    // preset indirection, so both read the same field.
                    summary: ConvaiEditorProfileField.Summarize(
                        _profile.objectReferenceValue,
                        GazePersonality.IsCustomized(_profile.objectReferenceValue as ConvaiGazeProfile))))
                return;

            DrawSectionBody(() =>
            {
                // First row, and drawn whether or not one is assigned. Before this the only entry
                // point was the button below, which creates a new asset — so picking one of the
                // SDK's shipped personalities was not possible from this Inspector at all.
                ConvaiEditorProfileField.Draw(_profile, PersonalityField);

                // Re-read after the field: the user may have just assigned or cleared it, and the
                // dials below must not be drawn against the previous asset for a frame.
                var profile = _profile.objectReferenceValue as ConvaiGazeProfile;
                if (profile == null)
                {
                    InfoBox(
                        "Using the SDK defaults",
                        "This character has no personality asset, so it uses the built-in defaults — " +
                        "which work. Assign one above, or make it a fresh one of its own.");
                    if (GhostButton(AddPersonalityButton))
                        GazeSetupService.ApplyFix(controller, GazeFixId.AssignDefaultProfile);
                    return;
                }

                if (_profileSerialized == null || _profileSerialized.targetObject != profile)
                    _profileSerialized = new SerializedObject(profile);

                // The dials write into the asset named one row above, which may be shared. Without
                // this, moving one silently re-tuned every character on that personality.
                GazePersonality.DrawSharingNotice(profile, controller);
                GazePersonality.DrawArchetypeRow(profile, controller);
                EditorGUILayout.Space(4f);
                GazePersonality.DrawDials(profile, _profileSerialized, controller);
                ConvaiCopyReceipts.Draw(controller);
            });
        }

        // ------------------------------------------------------------------ who it watches

        /// <summary>
        ///     Two controls. Everything the previous version showed here — auto-create, aim point,
        ///     aim offset, focus fidelity, scripted overrides, glance blocking, the live provider
        ///     list and the arbitration paragraph — is in the editor window's Targets tab, where
        ///     the roughly one project in ten that needs it can find it.
        /// </summary>
        private void DrawWhoItWatchesSection(ConvaiGazeController controller)
        {
            if (!DrawSection(SectionWhoItWatches, "Who It Looks At", ConvaiEditorGlyphs.Discovery)) return;

            DrawSectionBody(() =>
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_playerAnchorOverride, new GUIContent(
                    "Who is the player?",
                    "Leave empty and the character watches the main camera — right for almost every " +
                    "project. Set this for split-screen, multiplayer, or a cutscene rig."));
                if (EditorGUI.EndChangeCheck() && EditorApplication.isPlaying)
                {
                    // Route play-mode edits through the runtime property so the live provider
                    // re-targets immediately instead of on the next enable.
                    serializedObject.ApplyModifiedProperties();
                    controller.PlayerAnchorOverride = _playerAnchorOverride.objectReferenceValue as Transform;
                }

                DrawResolvedAnchorHint();
                EditorGUILayout.Space(2f);
                DrawEyeContactPopup(controller);
            });
        }

        /// <summary>
        ///     What an empty "Who is the player?" field actually resolves to. Without this the field
        ///     is ambiguous — empty could read as "broken" as easily as "the main camera".
        /// </summary>
        private void DrawResolvedAnchorHint()
        {
            var explicitAnchor = _playerAnchorOverride.objectReferenceValue as Transform;
            string resolved = explicitAnchor != null
                ? $"→ {explicitAnchor.name}"
                : Camera.main != null
                    ? $"→ {Camera.main.name} (the main camera)"
                    : "→ no camera found yet — the main camera will be used once one exists";

            Rect row = EditorGUILayout.GetControlRect(false, 14f);
            row.xMin += EditorGUIUtility.labelWidth;
            GUI.Label(row, resolved, ConvaiEditorStyles.MicroLabel);
        }

        /// <summary>
        ///     The eye-contact mode as four plain-English choices with a sentence each, drawn
        ///     through a display-name table so the serialized enum is untouched — renaming it would
        ///     be a breaking public API change for no user benefit.
        /// </summary>
        private void DrawEyeContactPopup(ConvaiGazeController controller)
        {
            var current = (GazeEyeContactMode)_eyeContactMode.enumValueIndex;
            int index = Array.IndexOf(EyeContactOrder, current);
            if (index < 0) index = 0;

            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(
                new GUIContent("Eye contact style", "How much the character commits to holding your gaze."),
                index, EyeContactLabels);
            if (EditorGUI.EndChangeCheck())
            {
                _eyeContactMode.enumValueIndex = (int)EyeContactOrder[next];
                serializedObject.ApplyModifiedProperties();
                if (EditorApplication.isPlaying) controller.EyeContactMode = EyeContactOrder[next];
            }

            Rect row = EditorGUILayout.GetControlRect(false, 26f);
            row.xMin += EditorGUIUtility.labelWidth;
            GUI.Label(row, EyeContactDescriptions[next], ConvaiEditorStyles.CaptionWrapped);
        }

        private static readonly GazeEyeContactMode[] EyeContactOrder =
        {
            GazeEyeContactMode.Natural,
            GazeEyeContactMode.SpeakingFocus,
            GazeEyeContactMode.ConversationLock,
            GazeEyeContactMode.AlwaysLock
        };

        private static readonly GUIContent[] EyeContactLabels =
        {
            new("Natural"),
            new("Focused while speaking"),
            new("Holds eye contact in conversation"),
            new("Always holds eye contact")
        };

        private static readonly string[] EyeContactDescriptions =
        {
            "Looks at you, glances away naturally — like a person.",
            "Locks on while it talks, natural the rest of the time.",
            "Never looks away mid-conversation. Still has a life of its own when idle.",
            "Stares at the player at all times, including idle. For kiosks and presenters."
        };

        // ------------------------------------------------------------------ live

        private void DrawLiveGazeSection(ConvaiGazeController controller)
        {
            if (!DrawSection(SectionLive, "Live", ConvaiEditorGlyphs.Live, accent: Theme.StatusInfo)) return;

            DrawSectionBody(() =>
            {
                if (!EditorApplication.isPlaying)
                {
                    OfflinePlaceholder();
                    return;
                }

                controller.CaptureSnapshot(_snapshot);

                string targetName = _snapshot.Reading.Target != null
                    ? _snapshot.Reading.Target.name
                    : _snapshot.Reading.TargetKind == GazeTargetKind.None
                        ? "nothing"
                        : _snapshot.Reading.TargetKind.ToString();

                using (new EditorGUILayout.HorizontalScope())
                {
                    LiveCell("Looking at", targetName, Theme.AccentBright, 130f);
                    LiveCell("State", _snapshot.DialogueState.ToString(), Theme.StatusInfo, 110f);
                    LiveCell("Committed", _snapshot.Reading.Engagement.ToString("0.00"),
                        Theme.TextPrimary, 100f);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    LiveCell("Eyes", _snapshot.EyePhase, Theme.TextPrimary, 130f);
                    LiveCell("Blink", _snapshot.BlinkWeight.ToString("0.00"), Theme.TextPrimary, 110f);
                    LiveCell("Turning", _snapshot.IsReorienting ? "yes" : "—",
                        _snapshot.IsReorienting ? Theme.StatusWarn : Theme.TextPrimary, 100f);
                }

                if (_snapshot.RecentTrace.Count > 0)
                {
                    EditorGUILayout.Space(4f);
                    int first = Mathf.Max(0, _snapshot.RecentTrace.Count - TraceTailLength);
                    for (int i = first; i < _snapshot.RecentTrace.Count; i++)
                    {
                        GazeTraceEntry entry = _snapshot.RecentTrace[i];
                        GUILayout.Label($"[{entry.Time:0.0}s] {entry.Message}", ConvaiEditorStyles.MicroLabel);
                    }
                }
            });
        }

        // ------------------------------------------------------------------ footer

        private static readonly GUIContent AdvancedSettingsButton = new(
            "Advanced settings & targets  →",
            "Opens the Gaze editor window: the full personality surface, scene-wide target authoring, " +
            "the rig report and the advanced targeting fields.");

        private static void DrawFooterLink(ConvaiGazeController controller)
        {
            EditorGUILayout.Space(6f);
            // The only documented route to the deeper surface. The full personality surface,
            // scene-wide target authoring, the rig report and the advanced targeting fields live
            // there, never here.
            if (GhostButton(AdvancedSettingsButton))
                ConvaiGazeEditorWindow.ShowFor(controller);
        }
    }

    /// <summary>
    ///     Scene-view gaze gizmos for the selected character.
    /// </summary>
    /// <remarks>
    ///     Draws in Edit Mode too. The module's hardest-to-discover requirement — the head bone's
    ///     local +Z must be the character's visual forward — used to be verifiable only by pressing
    ///     Play and watching the character stare sideways. The rest-forward ray makes it visible
    ///     while authoring, alongside the line to whoever this character treats as the player.
    /// </remarks>
    internal static class ConvaiGazeGizmoDrawer
    {
        // Gizmo colours carry the same meanings as the inspector's, so they come from the same
        // tokens: brand green = what it is looking at, info blue = the head chain, warn amber = the
        // player anchor.
        private static Color TargetColor => Theme.Fade(Theme.Accent, 0.9f);
        private static Color HeadColor => Theme.Fade(Theme.StatusInfo, 0.75f);
        private static Color AnchorColor => Theme.Fade(Theme.StatusWarn, 0.6f);

        /// <summary>
        ///     Resolved head bone and player anchor for the character the gizmo last drew.
        /// </summary>
        /// <remarks>
        ///     Both resolves are expensive relative to a gizmo callback — a full
        ///     <c>GetComponentsInChildren&lt;Transform&gt;</c> walk and a <see cref="SerializedObject" />
        ///     construction — and this runs on every Scene view repaint, which is continuous while
        ///     the user is orbiting. Neither answer can change without a selection change or an
        ///     inspector edit, so a short-lived cache is exact rather than approximate.
        /// </remarks>
        private const double EditTimeResolveIntervalSeconds = 0.5d;

        private static ConvaiGazeController _cachedController;
        private static Transform _cachedHead;
        private static Transform _cachedAnchor;
        private static double _nextEditTimeResolve;

        [DrawGizmo(GizmoType.Selected | GizmoType.Active, typeof(ConvaiGazeController))]
        private static void DrawGazeGizmos(ConvaiGazeController controller, GizmoType gizmoType)
        {
            if (UnityEngine.Application.isPlaying) DrawRuntimeGizmos(controller);
            else DrawEditTimeGizmos(controller);
        }

        /// <summary>Refreshes the edit-time cache when it is stale or aimed at another character.</summary>
        private static void EnsureEditTimeCache(ConvaiGazeController controller)
        {
            if (_cachedController == controller &&
                EditorApplication.timeSinceStartup < _nextEditTimeResolve)
                return;

            _cachedController = controller;
            _nextEditTimeResolve = EditorApplication.timeSinceStartup + EditTimeResolveIntervalSeconds;
            _cachedHead = FindHeadBone(controller);
            _cachedAnchor = ResolveEditTimeAnchor(controller);
        }

        private static void DrawRuntimeGizmos(ConvaiGazeController controller)
        {
            var chain = controller.Chain;
            if (chain == null || !chain.IsBound || !chain.HasHeadChain) return;

            Vector3 headPivot = chain.HeadPivotPosition;
            Vector3 headForward = chain.CurrentEyeRestForward;
            if (headForward.sqrMagnitude > 1e-6f)
            {
                Gizmos.color = HeadColor;
                Gizmos.DrawRay(headPivot, headForward.normalized * 0.6f);
            }

            GazeReading reading = controller.Current;
            if (reading.TargetKind == GazeTargetKind.None) return;

            Gizmos.color = TargetColor;
            Gizmos.DrawLine(chain.EyeCenterPosition, reading.WorldPoint);
            Gizmos.DrawWireSphere(reading.WorldPoint, 0.05f);
        }

        /// <summary>
        ///     Edit-time: the head pivot, the direction the head believes is forward, and a line to
        ///     whoever this character treats as the player. All three are questions a user
        ///     otherwise has to guess at.
        /// </summary>
        private static void DrawEditTimeGizmos(ConvaiGazeController controller)
        {
            EnsureEditTimeCache(controller);

            Transform head = _cachedHead;
            if (head == null) return;

            Gizmos.color = HeadColor;
            Gizmos.DrawWireSphere(head.position, 0.03f);
            Gizmos.DrawRay(head.position, head.forward * 0.5f);

            Transform anchor = _cachedAnchor;
            if (anchor == null) return;

            Gizmos.color = AnchorColor;
            Gizmos.DrawLine(head.position, anchor.position);
            Gizmos.DrawWireCube(anchor.position, Vector3.one * 0.06f);
        }

        private static Transform FindHeadBone(ConvaiGazeController controller)
        {
            Transform root = GazeSetupService.ResolveRoot(controller);
            if (root == null) return null;

            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman && animator.avatar != null)
            {
                Transform mapped = animator.GetBoneTransform(HumanBodyBones.Head);
                if (mapped != null) return mapped;
            }

            // Name fallback, mirroring the rig binding's own generic tables — enough for a gizmo,
            // and it never adds or rebuilds a component just to draw one.
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                if (string.Equals(transforms[i].name, "Head", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(transforms[i].name, "CC_Base_Head", StringComparison.OrdinalIgnoreCase))
                    return transforms[i];

            return null;
        }

        private static Transform ResolveEditTimeAnchor(ConvaiGazeController controller)
        {
            var serialized = new SerializedObject(controller);
            var explicitAnchor = serialized.FindProperty("playerAnchorOverride")?.objectReferenceValue as Transform;
            if (explicitAnchor != null) return explicitAnchor;
            return Camera.main != null ? Camera.main.transform : null;
        }
    }
}
