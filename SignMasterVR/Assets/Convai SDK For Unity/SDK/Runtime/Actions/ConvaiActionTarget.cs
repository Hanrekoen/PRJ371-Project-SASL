using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>Which characters an enabled <see cref="ConvaiActionTarget" /> registers onto.</summary>
    public enum ConvaiActionTargetApplyScope
    {
        /// <summary>Every currently enabled <see cref="ConvaiCharacter" />.</summary>
        AllCharacters = 0,

        /// <summary>Only the characters listed in the authored character list.</summary>
        SpecificCharacters = 1
    }

    /// <summary>
    ///     Marks any GameObject as a runtime action grounding target — drag-drop, no code required
    ///     (mirrors the Gaze module's <c>ConvaiGazeTarget</c> precedent). While enabled,
    ///     it is visible to <see cref="ConvaiActionTargetApplyScope" />-selected characters' merged
    ///     action config (<c>ConvaiCharacter.GetRuntimeActionConfig()</c>), participating in the
    ///     resolution ladder exactly like an authored object/character, except that an authored
    ///     entry of the same name always wins.
    /// </summary>
    /// <remarks>
    ///     Enumeration/late-enable: this component keeps its own static
    ///     <see cref="ActiveTargets" /> list (poll-based, mirroring <c>ConvaiGazeTarget</c>) rather
    ///     than pushing itself into every applicable character's registry on enable. Each
    ///     character's merged-config builder polls <see cref="ActiveTargets" /> at read time
    ///     (once per action batch — not a per-frame path), which trivially covers both orderings:
    ///     a target enabling after characters already exist, and a character enabling after
    ///     targets already exist, with no bidirectional bookkeeping.
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Convai Action Target")]
    [DisallowMultipleComponent]
    public sealed class ConvaiActionTarget : MonoBehaviour
    {
        private static readonly List<ConvaiActionTarget> Registry = new(16);

        /// <summary>Enabled targets in the scene (polled by <c>ConvaiCharacter</c>'s merged config builder).</summary>
        internal static IReadOnlyList<ConvaiActionTarget> ActiveTargets => Registry;

        [SerializeField]
        [Tooltip("Target name the backend/resolution ladder matches. Defaults to this GameObject's name when blank.")]
        private string _targetName;

        [SerializeField]
        [Tooltip("Whether this is an actionable object or an actionable character.")]
        private ConvaiActionTargetKind _kind = ConvaiActionTargetKind.Object;

        [SerializeField]
        [Tooltip("Object description sent to the backend for grounding (Object kind).")]
        private string _description;

        [SerializeField]
        [Tooltip("Character bio sent to the backend for grounding (Character kind).")]
        private string _bio;

        [SerializeField]
        [Tooltip("Alternate names the resolution ladder matches exactly before falling back to normalized/contains matching.")]
        private List<string> _aliases = new();

        [SerializeField]
        [Tooltip("Optional explicit point to move to / aim at. Falls back to this GameObject's transform when empty.")]
        private Transform _interactionPoint;

        [SerializeField]
        [Tooltip("Which characters register this target while enabled.")]
        private ConvaiActionTargetApplyScope _applyTo = ConvaiActionTargetApplyScope.AllCharacters;

        [SerializeField]
        [Tooltip("Characters to register onto when Apply To is Specific Characters.")]
        private List<ConvaiCharacter> _specificCharacters = new();

        [SerializeField]
        [Tooltip("Register automatically on enable / unregister on disable.")]
        private bool _registerOnEnable = true;

        private bool _registered;
        private bool _emptyNameWarned;

        /// <summary>Target name, defaulting to the GameObject's name when left blank.</summary>
        public string TargetName
        {
            get => string.IsNullOrWhiteSpace(_targetName) ? gameObject.name : _targetName;
            set => _targetName = value;
        }

        /// <summary>Whether this is an actionable object or character.</summary>
        public ConvaiActionTargetKind Kind
        {
            get => _kind;
            set => _kind = value;
        }

        /// <summary>Object description sent to the backend for grounding (Object kind).</summary>
        public string Description
        {
            get => _description;
            set => _description = value;
        }

        /// <summary>Character bio sent to the backend for grounding (Character kind).</summary>
        public string Bio
        {
            get => _bio;
            set => _bio = value;
        }

        /// <summary>Alternate names the resolution ladder matches exactly (ladder step 2).</summary>
        public List<string> Aliases
        {
            get => _aliases;
            set => _aliases = value ?? new List<string>();
        }

        /// <summary>Optional explicit interaction point; falls back to this transform when null.</summary>
        public Transform InteractionPoint
        {
            get => _interactionPoint;
            set => _interactionPoint = value;
        }

        /// <summary>Which characters register this target while enabled.</summary>
        public ConvaiActionTargetApplyScope ApplyTo
        {
            get => _applyTo;
            set => _applyTo = value;
        }

        /// <summary>Characters to register onto when <see cref="ApplyTo" /> is <c>SpecificCharacters</c>.</summary>
        public List<ConvaiCharacter> SpecificCharacters
        {
            get => _specificCharacters;
            set => _specificCharacters = value ?? new List<ConvaiCharacter>();
        }

        /// <summary>Whether this target registers automatically on enable / unregisters on disable.</summary>
        public bool RegisterOnEnable
        {
            get => _registerOnEnable;
            set => _registerOnEnable = value;
        }

        private void OnEnable()
        {
            if (_registerOnEnable) HandleEnable();
        }

        private void OnDisable() => HandleDisable();

        /// <summary>
        ///     Full enable path (registration into <see cref="ActiveTargets" />). Internal seam so
        ///     EditMode tests can drive the lifecycle explicitly — Unity does not invoke
        ///     <c>OnEnable</c> for plain MonoBehaviours outside play mode.
        /// </summary>
        internal void HandleEnable()
        {
            if (_registered) return;

            if (string.IsNullOrWhiteSpace(TargetName))
            {
                if (!_emptyNameWarned)
                {
                    _emptyNameWarned = true;
                    ConvaiLogger.Warning(
                        $"[ConvaiActionTarget] '{gameObject.name}' has no target name and no GameObject name to fall back to; ignoring.",
                        LogCategory.Character);
                }

                return;
            }

            Registry.Add(this);
            _registered = true;
        }

        /// <summary>Disable counterpart of <see cref="HandleEnable" /> (test seam).</summary>
        internal void HandleDisable()
        {
            if (!_registered) return;
            Registry.Remove(this);
            _registered = false;
        }

        /// <summary>Whether this target is currently visible to <paramref name="character" />'s merged config.</summary>
        internal bool AppliesToCharacter(ConvaiCharacter character)
        {
            if (character == null) return false;
            if (_applyTo == ConvaiActionTargetApplyScope.AllCharacters) return true;

            if (_specificCharacters == null) return false;
            for (int i = 0; i < _specificCharacters.Count; i++)
            {
                if (_specificCharacters[i] == character) return true;
            }

            return false;
        }
    }
}
