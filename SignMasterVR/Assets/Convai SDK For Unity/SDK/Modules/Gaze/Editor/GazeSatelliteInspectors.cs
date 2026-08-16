using Convai.Editor.Inspectors.Framework;
using Convai.Editor.UI;
using Convai.Modules.Gaze.Components;
using Convai.Modules.Gaze.Providers;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Modules.Gaze.Editor
{
    /// <summary>
    ///     Shared frame for the nine optional gaze components.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         All nine are <c>public</c>, all nine carry an Add Component entry, and until now not
    ///         one had a custom inspector — so the component a user adds to a painting to make
    ///         characters glance at it presented as four raw fields, one of whose tooltips read
    ///         "Priority tier. The player anchor publishes at 10 — keep below it…". Every other
    ///         Convai component in the SDK wears the Convai styling; these were the gap.
    ///     </para>
    ///     <para>
    ///         Each subclass says what its component does in one paragraph, then draws its fields
    ///         with plain-English labels. Nothing here changes serialized data — this is display
    ///         only, exactly like the profile's label table.
    ///     </para>
    /// </remarks>
    internal abstract class GazeSatelliteInspector : ConvaiInspectorEditor
    {
        private static readonly GUIContent ChipLive = new("Live", "Running and active on this character.");
        private static readonly GUIContent ChipOn = new("On", "Enabled; takes effect in Play mode.");
        private static readonly GUIContent ChipOff = new("Off", "Disabled — this component does nothing.");

        /// <summary>Product name shown in the header — never the class name.</summary>
        protected abstract string DisplayTitle { get; }

        /// <summary>One short phrase under the title.</summary>
        protected abstract string DisplaySubtitle { get; }

        /// <summary>The paragraph that answers "what is this and do I need it?".</summary>
        protected abstract string WhatThisDoes { get; }

        protected sealed override string Title => DisplayTitle;
        protected sealed override string Subtitle => DisplaySubtitle;

        protected sealed override GUIContent StatusChip
        {
            get
            {
                var component = (MonoBehaviour)target;
                if (!component.enabled) return ChipOff;
                return EditorApplication.isPlaying && component.isActiveAndEnabled ? ChipLive : ChipOn;
            }
        }

        protected sealed override Color StatusChipTint
        {
            get
            {
                var component = (MonoBehaviour)target;
                if (!component.enabled) return Theme.StatusIdle;
                return EditorApplication.isPlaying && component.isActiveAndEnabled
                    ? Theme.AccentBright
                    : Theme.StatusInfo;
            }
        }

        /// <summary>Several of these carry live readouts, so keep them refreshing while playing.</summary>
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        /// <summary>
        ///     The "what is this and do I need it?" paragraph, above whatever body the subclass draws.
        /// </summary>
        protected sealed override void DrawHeaderExtras() => InfoBox("What this does", WhatThisDoes);

        /// <summary>Draws a serialized field with an explicit plain-English label.</summary>
        protected void DrawField(string fieldName, string label, string tooltip)
        {
            SerializedProperty property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                WarningBox("Missing Setting", $"{target.GetType().Name}.{fieldName} was not found.");
                return;
            }

            EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip), true);
        }
    }

    // ---------------------------------------------------------------------- gaze target

    /// <summary>
    ///     The "make characters notice this object" component, and the one with the most
    ///     user contact of the nine.
    /// </summary>
    /// <remarks>
    ///     Its <c>priority</c> field is an int the tooltip asked the user to compare against 10.
    ///     That is arbiter vocabulary; what the user actually wants to express is "should this beat
    ///     the player or not?", so it is drawn as one checkbox with the raw value still reachable
    ///     under Advanced for anyone building a finer-grained hierarchy.
    /// </remarks>
    [CustomEditor(typeof(ConvaiGazeTarget))]
    internal sealed class ConvaiGazeTargetInspector : GazeSatelliteInspector
    {
        /// <summary>The priority the player anchor publishes at — the threshold "more important" crosses.</summary>
        private const int PlayerPriority = 10;

        private const int AbovePlayerPriority = 15;
        private const int BelowPlayerPriority = 5;

        private const string SectionAdvanced = "GazeTargetAdvanced";

        protected override string DisplayTitle => "Gaze Target";
        protected override string DisplaySubtitle => "Something worth looking at";

        protected override string WhatThisDoes =>
            "Convai characters nearby will glance at this object while they are idle. During a " +
            "conversation the player still wins, unless you mark it as more important below.";

        protected override void DrawBody()
        {
            DrawField("baseRelevance", "How interesting",
                "How strongly this pulls a character's attention compared with other things nearby.");
            DrawField("maxDistance", "Noticed within",
                "Metres. Beyond this the object is not a candidate at all.");
            DrawField("fullRelevanceDistance", "Fully interesting under",
                "Metres. Closer than this, interest is at its maximum.");
            DrawField("aimOffset", "Aim at",
                "Local offset from this transform to the exact point the eyes aim at — the top of a " +
                "painting rather than its centre, for example.");

            EditorGUILayout.Space(4f);
            DrawImportanceToggle();

            if (!DrawSection(SectionAdvanced, "Advanced", ConvaiEditorGlyphs.Contract, defaultExpanded: false))
                return;

            DrawSectionBody(() =>
            {
                DrawField("priority", "Priority",
                    "The raw tier. The player publishes at 10; anything higher outranks the player. " +
                    "Set this directly only if you are building a finer hierarchy than the checkbox above.");
            });
        }

        /// <summary>
        ///     The int-as-checkbox. Reading it as "> player" rather than "== 15" means a
        ///     hand-authored 12 still shows as ticked, instead of the control silently disagreeing
        ///     with the value beneath it.
        /// </summary>
        private void DrawImportanceToggle()
        {
            SerializedProperty priority = serializedObject.FindProperty("priority");
            if (priority == null) return;

            bool current = priority.intValue > PlayerPriority;

            EditorGUI.BeginChangeCheck();
            bool next = EditorGUILayout.ToggleLeft(
                new GUIContent("More important than the player",
                    "Characters will look here even in the middle of a conversation."),
                current);
            if (EditorGUI.EndChangeCheck())
                priority.intValue = next ? AbovePlayerPriority : BelowPlayerPriority;

            Rect hint = EditorGUILayout.GetControlRect(false, 14f);
            hint.xMin += 18f;
            GUI.Label(hint,
                next ? "Interrupts conversation." : "Waits until the character is idle.",
                ConvaiEditorStyles.MicroLabel);
        }
    }

    // ---------------------------------------------------------------------- player anchor

    [CustomEditor(typeof(PlayerAnchorTargetProvider))]
    internal sealed class PlayerAnchorTargetProviderInspector : GazeSatelliteInspector
    {
        protected override string DisplayTitle => "Player Anchor";
        protected override string DisplaySubtitle => "What the character treats as \"you\"";

        protected override string WhatThisDoes =>
            "Most projects never add this by hand — the Gaze component creates one automatically " +
            "and points it at the main camera. Add it yourself only to control the anchor's own " +
            "settings, or when you turned automatic creation off.";

        // Body: the base's Convai per-field section renderer. This component has no field that
        // needs bespoke wording, so re-labelling them by hand would only risk drift.
    }

    // ---------------------------------------------------------------------- world object target

    [CustomEditor(typeof(WorldObjectGazeTargetProvider))]
    internal sealed class WorldObjectGazeTargetProviderInspector : GazeSatelliteInspector
    {
        protected override string DisplayTitle => "World Object Target";
        protected override string DisplaySubtitle => "A described object worth looking at";

        protected override string WhatThisDoes =>
            "Like a Gaze Target, but for objects that already carry Convai scene metadata — the " +
            "character can then also talk about what it is looking at. For a plain object with no " +
            "description, use Gaze Target instead.";

        protected override void DrawBody()
        {
            DrawField("priority", "Priority",
                "The player publishes at 10; keep this below so the player wins during conversation.");
            DrawField("baseRelevance", "How interesting",
                "How strongly this pulls a character's attention compared with other things nearby.");
            DrawField("maxDistance", "Noticed within", "Metres.");
            DrawField("fullRelevanceDistance", "Fully interesting under", "Metres.");
        }
    }

    // ---------------------------------------------------------------------- character gaze

    [CustomEditor(typeof(CharacterGazeTargetProvider))]
    internal sealed class CharacterGazeTargetProviderInspector : GazeSatelliteInspector
    {
        protected override string DisplayTitle => "Looks At Other Characters";
        protected override string DisplaySubtitle => "Character-to-character eye contact";

        protected override string WhatThisDoes =>
            "For scenes with more than one Convai character. Listeners look at whoever is speaking, " +
            "and idle characters exchange occasional glances. Add one to each character that should " +
            "take part. The player still outranks other characters during a conversation.";

        // Body: the base's Convai per-field section renderer. This component has no field that
        // needs bespoke wording, so re-labelling them by hand would only risk drift.
    }

    // ---------------------------------------------------------------------- attention sensor

    [CustomEditor(typeof(PlayerAttentionSensor))]
    internal sealed class PlayerAttentionSensorInspector : GazeSatelliteInspector
    {
        protected override string DisplayTitle => "Notices When You Look";
        protected override string DisplaySubtitle => "Detects the player's attention";

        protected override string WhatThisDoes =>
            "Tells the character whether you are looking at it. It reacts sooner when you are, and " +
            "the answer is reported to the backend so the conversation can use it. Works from the " +
            "main camera on desktop; XR eye tracking plugs in without an XR package.";

        protected override void DrawBody()
        {
            if (EditorApplication.isPlaying && target is PlayerAttentionSensor sensor)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    LiveCell("You are",
                        sensor.IsPlayerLooking ? "looking at it" : "looking away",
                        sensor.IsPlayerLooking ? Theme.AccentBright : Theme.TextPrimary, 150f);
                    LiveCell("Confidence", sensor.PlayerAttention.ToString("0.00"), Theme.StatusInfo, 110f);
                }

                EditorGUILayout.Space(4f);
            }

            DrawGeneratedSections();
        }
    }

    // ---------------------------------------------------------------------- joint attention

    [CustomEditor(typeof(GazeJointAttention))]
    internal sealed class GazeJointAttentionInspector : GazeSatelliteInspector
    {
        protected override string DisplayTitle => "Notices What You Look At";
        protected override string DisplaySubtitle => "Follows the player's attention";

        protected override string WhatThisDoes =>
            "When you look at a marked object for a moment, the character notices and glances there " +
            "too — the \"I see what caught your eye\" beat. Needs objects marked with Gaze Target, " +
            "and works best alongside \"Notices when you look\".";

        // Body: the base's Convai per-field section renderer. This component has no field that
        // needs bespoke wording, so re-labelling them by hand would only risk drift.
    }

    // ---------------------------------------------------------------------- referential glances

    [CustomEditor(typeof(GazeReferentialGlances))]
    internal sealed class GazeReferentialGlancesInspector : GazeSatelliteInspector
    {
        protected override string DisplayTitle => "Looks At What It Mentions";
        protected override string DisplaySubtitle => "Glances at objects it names";

        protected override string WhatThisDoes =>
            "When the character says the name of an object in the scene — \"take a look at the " +
            "painting\" — it glances at that object. Needs the objects marked with Gaze Target so " +
            "there is a name to match.";

        // Body: the base's Convai per-field section renderer. This component has no field that
        // needs bespoke wording, so re-labelling them by hand would only risk drift.
    }

    // ---------------------------------------------------------------------- dynamic context bridge

    [CustomEditor(typeof(GazeDynamicContextBridge))]
    internal sealed class GazeDynamicContextBridgeInspector : GazeSatelliteInspector
    {
        protected override string DisplayTitle => "Talks About What It Sees";
        protected override string DisplaySubtitle => "Reports its attention to the backend";

        protected override string WhatThisDoes =>
            "Tells the backend which object currently has the character's attention, so \"it\" and " +
            "\"that\" in conversation resolve to the thing it is actually looking at.";

        protected override void DrawBody()
        {
            DrawField("_engagementThreshold", "Report only when committed",
                "How strongly the character must be looking at something before its name is sent. " +
                "Higher means only deliberate looks are reported.");
        }
    }

    // ---------------------------------------------------------------------- pupil driver

    [CustomEditor(typeof(ConvaiEyePupilDriver))]
    internal sealed class ConvaiEyePupilDriverInspector : GazeSatelliteInspector
    {
        protected override string DisplayTitle => "Pupil Response";
        protected override string DisplaySubtitle => "Pupils widen with excitement";

        protected override string WhatThisDoes =>
            "Close-up and VR polish: the pupils dilate as the character's arousal rises. Needs eye " +
            "materials that expose a pupil-scale property — without one this component does nothing " +
            "and says so in the console.";

        // Body: the base's Convai per-field section renderer. This component has no field that
        // needs bespoke wording, so re-labelling them by hand would only risk drift.
    }

    // ---------------------------------------------------------------------- attention requests

    /// <summary>
    ///     The one component in this file the user did not choose to add.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="GazeAttentionRequests" /> is <c>internal</c> and carries an empty
    ///         <c>AddComponentMenu</c>, but <c>[RequireComponent]</c> still puts it on every
    ///         character that has Gaze — so a customer opening their character finds a second
    ///         component they never added, with no fields and, until this inspector existed, no
    ///         explanation. Hiding it was considered and rejected: the project's own rule
    ///         (<c>EmbodimentContext.RuntimeInfrastructureHideFlags</c>) is that infrastructure a
    ///         user cannot see is infrastructure they cannot debug.
    ///     </para>
    ///     <para>
    ///         So it stays visible and answers the only two questions it raises: what is it, and may
    ///         I delete it. It has no serialized fields, so the paragraph is the whole surface.
    ///     </para>
    /// </remarks>
    [CustomEditor(typeof(GazeAttentionRequests))]
    internal sealed class GazeAttentionRequestsInspector : GazeSatelliteInspector
    {
        protected override string DisplayTitle => "Attention Requests";
        protected override string DisplaySubtitle => "How other systems point this character's gaze";

        protected override string WhatThisDoes =>
            "Added automatically by Gaze — nothing to configure. It is the doorway actions and other " +
            "Convai systems use to say \"look at this\": Look At, Scan Environment and Watch The Player all " +
            "arrive here. Removing or disabling it leaves the character's own eye contact working " +
            "but stops those actions from being able to aim it.";

        // Body: none. A component with no settings should not draw an empty section just to have one.
        protected override void DrawBody()
        {
        }
    }
}
