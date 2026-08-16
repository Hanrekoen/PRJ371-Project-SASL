using Convai.Editor.Inspectors.Framework;
using Convai.Editor.UI;
using Convai.Runtime.Embodiment;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Embodiment.Inspectors
{
    /// <summary>
    ///     Inspector for <see cref="ConvaiTravelIntent" /> — what the character thinks it is doing,
    ///     and where that belief came from.
    /// </summary>
    /// <remarks>
    ///     This component normally appears by itself at play time and is never serialized, so most
    ///     users meet it here for the first time while wondering why a walking character is looking
    ///     where it is looking. The Live section is therefore the point of this inspector, not a
    ///     footnote to it: it names the source of the reading and, when nothing has declared what the
    ///     journey is about, says so and says what to do — the one thing a customer with their own
    ///     movement executor needs to know and has no other way to discover.
    /// </remarks>
    [CustomEditor(typeof(ConvaiTravelIntent))]
    internal sealed class ConvaiTravelIntentEditor : ConvaiInspectorEditor
    {
        private const string SectionDetection = "Detection";
        private const string SectionLive = "LiveTravel";

        private SerializedProperty _detectMovementAutomatically;
        private SerializedProperty _movementSpeedThreshold;
        private SerializedProperty _movementSustainSeconds;
        private SerializedProperty _reportTimeoutSeconds;
        private SerializedProperty _referenceTravelSpeed;

        protected override void OnEnable()
        {
            base.OnEnable();
            _detectMovementAutomatically = serializedObject.FindProperty("detectMovementAutomatically");
            _movementSpeedThreshold = serializedObject.FindProperty("movementSpeedThreshold");
            _movementSustainSeconds = serializedObject.FindProperty("movementSustainSeconds");
            _reportTimeoutSeconds = serializedObject.FindProperty("reportTimeoutSeconds");
            _referenceTravelSpeed = serializedObject.FindProperty("referenceTravelSpeed");
        }

        protected override string Title => "Travel Intent";

        protected override string Subtitle => "Where this character is going";

        protected override GUIContent StatusChip => CurrentChip.Content;

        protected override Color StatusChipTint => CurrentChip.Tint;

        private ConvaiEditorChip CurrentChip
        {
            get
            {
                if (!EditorApplication.isPlaying) return ConvaiEditorChips.Ready;
                return ConvaiEditorChips.Running(((ConvaiTravelIntent)target).IsTraveling);
            }
        }

        /// <summary>The live readout is the reason to open this inspector at all.</summary>
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        protected override void DrawBody()
        {
            InfoBox(
                "What this does",
                "Tells the rest of the character that it is going somewhere, so it can behave " +
                "accordingly — most visibly, so gaze watches the path ahead while walking instead of " +
                "staring at the destination the whole way. It appears by itself the first time the " +
                "character moves; you only need to add it by hand to change the settings below.");

            DrawLiveSection((ConvaiTravelIntent)target);
            DrawDetectionSection();
        }

        private void DrawLiveSection(ConvaiTravelIntent travel)
        {
            if (!DrawSection(SectionLive, "Live", ConvaiEditorGlyphs.Live, accent: Theme.StatusInfo)) return;

            DrawSectionBody(() =>
            {
                if (!EditorApplication.isPlaying)
                {
                    OfflinePlaceholder();
                    return;
                }

                EditorGUILayout.BeginHorizontal();
                LiveCell("State", travel.IsTraveling ? "Travelling" : "Standing still",
                    travel.IsTraveling ? Theme.AccentBright : Theme.StatusIdle, 130f);
                LiveCell("Knows this from", DescribeSource(travel.Source), Theme.StatusInfo, 130f);
                EditorGUILayout.EndHorizontal();

                if (!travel.IsTraveling) return;

                if (travel.HasSubject)
                {
                    EditorGUILayout.LabelField(
                        "Checking on", "Whatever the journey is about, every few seconds.");
                    return;
                }

                // The message that earns this inspector its place. Someone who moved the character
                // with their own code gets correct road-watching for free, and finds out here — at
                // the moment they are asking the question — what the missing half is and how to
                // switch it on.
                InfoBox(
                    "Nothing has said what this journey is about",
                    "The character is watching the path, which is right, but it never glances at " +
                    "where it is going because nothing named a destination. Action steps that name a " +
                    "target do this automatically; from your own code, call SetSubject(transform) or " +
                    "SetSubject(position) when the journey starts.");
            });
        }

        private void DrawDetectionSection()
        {
            if (!DrawSection(SectionDetection, "Noticing Movement", ConvaiEditorGlyphs.Motion,
                    defaultExpanded: false))
                return;

            DrawSectionBody(() =>
            {
                EditorGUILayout.PropertyField(_detectMovementAutomatically, new GUIContent(
                    "Notice Movement Itself",
                    "Watch the character move and work out that it is going somewhere, without any " +
                    "Convai locomotion component or code saying so. This is what covers a character " +
                    "driven by a Character Controller, root motion, a tween, or your own navigation."));
                EditorGUILayout.PropertyField(_movementSpeedThreshold, new GUIContent(
                    "Counts As Moving Above",
                    "How fast the character has to move before it counts as going somewhere, in " +
                    "metres per second. Below this is settling, jitter, or turning on the spot."));
                EditorGUILayout.PropertyField(_movementSustainSeconds, new GUIContent(
                    "Only After Moving For",
                    "How long that movement has to keep up before it counts, in seconds. Stops a " +
                    "single shove or a one-frame teleport from reading as a journey."));

                EditorGUILayout.Space(4f);
                EditorGUILayout.PropertyField(_reportTimeoutSeconds, new GUIContent(
                    "Reports Expire After",
                    "How long a journey reported from your own code stays valid without being " +
                    "repeated. Code that stops reporting — or is destroyed mid-move — falls back " +
                    "instead of leaving the character travelling forever."));
                EditorGUILayout.PropertyField(_referenceTravelSpeed, new GUIContent(
                    "Treat This Speed As Full Pace",
                    "The speed treated as full effort when working out how fast the character is " +
                    "going, which sets how far ahead it looks. Only used when nothing else supplies one."));

                InfoBox(
                    "Standing on something that moves is not travelling",
                    "Movement is measured relative to whatever the character is parented to, so a " +
                    "character riding a lift or a moving platform is not mistaken for one walking.");
            });
        }

        private static string DescribeSource(ConvaiTravelIntent.TravelSource source) => source switch
        {
            ConvaiTravelIntent.TravelSource.Reported => "Your code",
            ConvaiTravelIntent.TravelSource.Locomotion => "Convai locomotion",
            ConvaiTravelIntent.TravelSource.Observed => "Watching it move",
            _ => "—"
        };
    }
}
