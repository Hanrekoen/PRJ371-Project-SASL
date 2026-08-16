using Convai.Modules.BodyAnimation.Data;
using UnityEditor;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>One locomotion slot in the coverage table: what it is called, and where it lives.</summary>
    internal readonly struct LocomotionSlotRef
    {
        internal LocomotionSlotRef(string label, string fieldName)
        {
            Label = label;
            FieldName = fieldName;
        }

        /// <summary>Plain-language name of the slot, e.g. "90° Left".</summary>
        internal string Label { get; }

        /// <summary>Serialized field name on <c>LocomotionSection</c>.</summary>
        internal string FieldName { get; }
    }

    /// <summary>
    ///     One cell of the coverage grid — a group of related slots (all the walk starts, all the
    ///     planted stops) and what stays unavailable while the group is empty.
    /// </summary>
    internal readonly struct LocomotionCoverageCell
    {
        internal LocomotionCoverageCell(
            string columnLabel, string disabledFeatureText, params LocomotionSlotRef[] slots)
        {
            ColumnLabel = columnLabel;
            DisabledFeatureText = disabledFeatureText;
            Slots = slots;
        }

        /// <summary>Column heading, e.g. "Starts".</summary>
        internal string ColumnLabel { get; }

        /// <summary>What the character loses while this cell is empty, and what happens instead.</summary>
        internal string DisabledFeatureText { get; }

        /// <summary>The slots this cell covers; empty for a cell that only carries a note.</summary>
        internal LocomotionSlotRef[] Slots { get; }
    }

    /// <summary>
    ///     Which locomotion slots a <see cref="ConvaiBodyAnimationSet" /> fills, and what each gap
    ///     costs — the single description of locomotion coverage every surface reads.
    /// </summary>
    /// <remarks>
    ///     The Body Animation Editor window draws this as a grid; the MCP content tool serialises
    ///     the same numbers. Neither owns the table, so a slot added to the set in a future release
    ///     cannot appear in one surface and be missing from the other.
    /// </remarks>
    internal static class BodyAnimationContentCoverage
    {
        /// <summary>Total locomotion slots a set can fill.</summary>
        internal const int TotalSlots = 26;

        internal static readonly LocomotionCoverageCell[] WalkCells =
        {
            new(BodyAnimationEditorStrings.LocomotionColLoop,
                "No walk clip — the character cannot move at all.",
                new LocomotionSlotRef(BodyAnimationEditorStrings.LocomotionRowWalk, "_walk")),
            new(BodyAnimationEditorStrings.LocomotionColStarts,
                "No directional starts — the character blends into movement instead of playing a scripted start.",
                new LocomotionSlotRef("Forward", "_walkStartForward"),
                new LocomotionSlotRef("90° Left", "_walkStart90Left"),
                new LocomotionSlotRef("90° Right", "_walkStart90Right"),
                new LocomotionSlotRef("180° Left", "_walkStart180Left"),
                new LocomotionSlotRef("180° Right", "_walkStart180Right")),
            new(BodyAnimationEditorStrings.LocomotionColStops,
                "No planted stops — stops blend down instead of matching footfall.",
                new LocomotionSlotRef("Left Plant", "_walkStopLeftPlant"),
                new LocomotionSlotRef("Right Plant", "_walkStopRightPlant"),
                new LocomotionSlotRef("Low Speed", "_walkStopLowSpeed"),
                new LocomotionSlotRef("Abrupt", "_walkStopAbrupt")),
            new(BodyAnimationEditorStrings.LocomotionColSpeed,
                "No speed-change clips — walk and jog blend directly into each other.",
                new LocomotionSlotRef("To Jog (Left)", "_walkToJogLeft"),
                new LocomotionSlotRef("To Jog (Right)", "_walkToJogRight")),
            new(BodyAnimationEditorStrings.LocomotionColTurns,
                "No turn-in-place — turns blend through movement instead of a scripted pivot.",
                new LocomotionSlotRef("90° Left", "_turn90Left"),
                new LocomotionSlotRef("90° Right", "_turn90Right"),
                new LocomotionSlotRef("180° Left", "_turn180Left"),
                new LocomotionSlotRef("180° Right", "_turn180Right"))
        };

        internal static readonly LocomotionCoverageCell[] JogCells =
        {
            new(BodyAnimationEditorStrings.LocomotionColLoop,
                "No jog clip — the character stays at walking pace.",
                new LocomotionSlotRef(BodyAnimationEditorStrings.LocomotionRowJog, "_jog")),
            new(BodyAnimationEditorStrings.LocomotionColStarts,
                "No directional jog starts — jog movement blends in instead of playing a scripted start.",
                new LocomotionSlotRef("Forward", "_jogStartForward"),
                new LocomotionSlotRef("90° Left", "_jogStart90Left"),
                new LocomotionSlotRef("90° Right", "_jogStart90Right"),
                new LocomotionSlotRef("180° Left", "_jogStart180Left"),
                new LocomotionSlotRef("180° Right", "_jogStart180Right")),
            new(BodyAnimationEditorStrings.LocomotionColStops,
                "No planted jog stops — jog stops blend down instead of matching footfall.",
                new LocomotionSlotRef("Left Plant", "_jogStopLeftPlant"),
                new LocomotionSlotRef("Abrupt", "_jogStopAbrupt")),
            new(BodyAnimationEditorStrings.LocomotionColSpeed,
                "No speed-change clips — jog and walk blend directly into each other.",
                new LocomotionSlotRef("To Walk (Left)", "_jogToWalkLeft"),
                new LocomotionSlotRef("To Walk (Right)", "_jogToWalkRight")),
            new(BodyAnimationEditorStrings.LocomotionColTurns,
                BodyAnimationEditorStrings.LocomotionTurnsSharedNote)
        };

        /// <summary>The clip property behind one slot, or <c>null</c> when the field is unknown.</summary>
        internal static SerializedProperty ClipPropertyFor(SerializedProperty locomotionProperty, string fieldName)
        {
            SerializedProperty slot = locomotionProperty?.FindPropertyRelative(fieldName);
            return slot?.FindPropertyRelative("_clip");
        }

        /// <summary>How many of a cell's slots carry a clip.</summary>
        internal static int CountFilled(SerializedProperty locomotionProperty, in LocomotionCoverageCell cell)
        {
            if (locomotionProperty == null || cell.Slots == null) return 0;

            int filled = 0;
            for (int i = 0; i < cell.Slots.Length; i++)
            {
                SerializedProperty clip = ClipPropertyFor(locomotionProperty, cell.Slots[i].FieldName);
                if (clip != null && clip.objectReferenceValue != null) filled++;
            }
            return filled;
        }

        /// <summary>
        ///     The set's own locomotion property, for callers holding a set rather than an already
        ///     open <see cref="SerializedObject" />. Returns <c>null</c> for a null set.
        /// </summary>
        internal static SerializedProperty LocomotionPropertyOf(ConvaiBodyAnimationSet set) =>
            set != null ? new SerializedObject(set).FindProperty("_locomotion") : null;
    }
}
