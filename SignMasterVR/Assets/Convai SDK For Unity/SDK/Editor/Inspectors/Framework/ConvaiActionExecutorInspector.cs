using System;
using System.Collections.Generic;
using System.Reflection;
using Convai.Editor.Actions;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors.Framework
{
    /// <summary>
    ///     Fallback Convai inspector for every <see cref="ConvaiActionExecutorBase" />-derived
    ///     component, shipped or user-authored : "Action Behavior" header
    ///     with a purpose line from <see cref="ConvaiActionArchetypeAttribute" /> when present, a
    ///     binding status block (which of the owning character's actions run this behavior, or a
    ///     friendly "not used yet" hint with an Actions Editor shortcut), an inline required-peer
    ///     check with a one-click add fix, and the component's own fields through the framework
    ///     renderer. Specialized per-executor editors can still shadow this one for a concrete type.
    /// </summary>
    /// <remarks>
    ///     Binding state is cached and refreshed only on enable and
    ///     <see cref="EditorApplication.hierarchyChanged" /> (plus after the add-component fix) —
    ///     never per repaint.
    /// </remarks>
    [CustomEditor(typeof(ConvaiActionExecutorBase), true)]
    [CanEditMultipleObjects]
    internal sealed class ConvaiActionExecutorInspector : ConvaiInspectorEditor
    {
        private const string SubtitleText = "Action Behavior";
        private const string GenericPurpose =
            "Does something in the scene when one of this Convai Character's actions runs it. " +
            "Connect it to an action in the Actions Editor.";

        private static readonly GUIContent UsedByTitle = new(
            "Used by actions",
            "The actions on this Convai Character that run this behavior when the character is asked to perform them.");

        private static readonly GUIContent NotUsedBody = new(
            "Not used by any action yet. Open the Actions Editor to connect this behavior to an action " +
            "this Convai Character can perform.");

        private static readonly GUIContent NoCharacterBody = new(
            "This component is not under a Convai Character with Convai Actions yet, so no " +
            "action can run it. Add it to a character's hierarchy, then connect it in the Actions Editor.");

        private static readonly GUIContent OpenActionsEditorButton = new(
            "Open Actions Editor",
            "Open the Convai Actions Editor to author this character's actions and connect them to scene behaviors.");

        private static readonly GUIContent MultiEditNote = new(
            "Select a single component to see which actions use it.");

        private static readonly GUIContent ChipInUse = new(
            "In use",
            "At least one action on this Convai Character runs this behavior.");

        private static readonly GUIContent ChipNotConnected = new(
            "Not connected",
            "No action runs this behavior yet. Open the Actions Editor to connect it.");

        private ConvaiActionConfigSource _source;
        private readonly List<GUIContent> _boundActionChips = new();
        private bool _bindingsDirty;
        private string _purpose;
        private Type _peerType;
        private bool _peerMissing;
        private GUIContent _peerMessage;
        private GUIContent _addPeerButton;
        private Type _targetRequirementType;
        private GUIContent _targetRequirementMessage;

        protected override string Subtitle => SubtitleText;

        protected override string Purpose => _purpose;

        protected override GUIContent StatusChip =>
            targets.Length == 1 ? (_boundActionChips.Count > 0 ? ChipInUse : ChipNotConnected) : null;

        protected override Color StatusChipTint =>
            _boundActionChips.Count > 0 ? Theme.StatusReady : Theme.TextMuted;

        protected override void OnEnable()
        {
            var archetype = target != null
                ? target.GetType().GetCustomAttribute<ConvaiActionArchetypeAttribute>()
                : null;
            _purpose = string.IsNullOrWhiteSpace(archetype?.Description) ? GenericPurpose : archetype.Description;

            base.OnEnable();

            EditorApplication.hierarchyChanged += MarkBindingsDirty;
            RefreshBindings();
        }

        protected override void OnDisable()
        {
            EditorApplication.hierarchyChanged -= MarkBindingsDirty;
            base.OnDisable();
        }

        private void MarkBindingsDirty()
        {
            _bindingsDirty = true;
            Repaint();
        }

        protected override void OnBeforeInspectorGUI()
        {
            if (_bindingsDirty)
                RefreshBindings();
        }

        protected override void DrawHeaderExtras()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Command, UsedByTitle);

            if (targets.Length != 1)
            {
                GUILayout.Label(MultiEditNote, Theme.MutedWrapped);
            }
            else
            {
                if (_boundActionChips.Count > 0)
                {
                    DrawActionChips();
                }
                else
                {
                    Theme.BeginPanel(null);
                    GUILayout.Label(_source != null ? NotUsedBody : NoCharacterBody, Theme.MutedWrapped);
                    Theme.EndPanel(0f);
                }

                GUILayout.Space(6f);
                Rect openRect = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
                if (Theme.GhostButton(openRect, OpenActionsEditorButton))
                {
                    if (_source != null)
                        ConvaiActionsEditorWindow.ShowWindowFor(_source);
                    else
                        ConvaiActionsEditorWindow.ShowWindow();
                }

                DrawPeerRequirement();
                DrawTargetRequirement();
            }

            Theme.EndCard(10f);
        }

        private void DrawActionChips()
        {
            float available = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 64f);
            float used = 0f;

            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < _boundActionChips.Count; i++)
            {
                GUIContent chip = _boundActionChips[i];
                float width = Theme.PillWidth(chip) + 6f;
                if (used > 0f && used + width > available)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(4f);
                    EditorGUILayout.BeginHorizontal();
                    used = 0f;
                }

                Rect rect = GUILayoutUtility.GetRect(width, 20f, GUILayout.Width(width));
                Theme.Pill(rect, chip, Theme.Accent);
                GUILayout.Space(4f);
                used += width + 4f;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPeerRequirement()
        {
            if (!_peerMissing || _peerType == null)
                return;

            GUILayout.Space(6f);
            Theme.BeginPanel(Theme.StatusWarn);
            GUILayout.Label(_peerMessage, Theme.BodyWrapped);
            GUILayout.Space(2f);
            Rect addRect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            if (Theme.GhostButton(addRect, _addPeerButton))
            {
                Undo.AddComponent(((Component)target).gameObject, _peerType);
                _bindingsDirty = true;
            }

            Theme.EndPanel(0f);
        }

        /// <summary>
        ///     Explanation-only notice for <see cref="ConvaiActionArchetypeAttribute.RequiredTargetComponent" />:
        ///     unlike <see cref="DrawPeerRequirement" />, there is nothing to add a component to from
        ///     here — the requirement is on whatever object this action ends up pointed at, not on
        ///     this character. The wording is deliberately worded around "targets"/"objects" rather
        ///     than "the character" so the two notices can never be mistaken for each other.
        /// </summary>
        private void DrawTargetRequirement()
        {
            if (_targetRequirementType == null)
                return;

            GUILayout.Space(6f);
            Theme.BeginPanel(null);
            GUILayout.Label(_targetRequirementMessage, Theme.BodyWrapped);
            Theme.EndPanel(0f);
        }

        private void RefreshBindings()
        {
            _bindingsDirty = false;
            _boundActionChips.Clear();
            _source = null;
            _peerMissing = false;
            _targetRequirementType = null;

            if (targets.Length != 1 || target == null)
                return;

            var component = (ConvaiActionExecutorBase)target;
            Type componentType = component.GetType();
            _source = component.GetComponentInParent<ConvaiActionConfigSource>(true);

            if (_source != null)
            {
                IReadOnlyList<ConvaiActionDefinition> definitions = _source.GetEffectiveDefinitions();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < definitions.Count; i++)
                {
                    ConvaiActionDefinition definition = definitions[i];
                    if (definition == null || string.IsNullOrWhiteSpace(definition.ActionName))
                        continue;

                    bool referenced = ReferenceEquals(definition.Executor, component) ||
                                      (!string.IsNullOrWhiteSpace(definition.ExecutorTypeHint) &&
                                       ConvaiActionExecutorBinder.TryResolveType(definition.ExecutorTypeHint, out Type hintType) &&
                                       hintType == componentType);
                    if (!referenced || !seen.Add(definition.ActionName))
                        continue;

                    _boundActionChips.Add(new GUIContent(
                        definition.ActionName,
                        "This action runs this behavior. Click Open Actions Editor to edit it."));
                }
            }

            RefreshPeerRequirement(component, componentType);
            RefreshTargetRequirement(componentType);
        }

        private void RefreshPeerRequirement(Component component, Type componentType)
        {
            var archetype = componentType.GetCustomAttribute<ConvaiActionArchetypeAttribute>();
            string hint = archetype?.RequiredPeerHint;
            if (string.IsNullOrWhiteSpace(hint))
                return;

            _peerType = ConvaiComponentTypeResolver.Resolve(hint.Trim());
            if (_peerType == null)
                return; // Free-text hint naming no loaded component type: nothing to validate.

            bool present = component.GetComponentInParent(_peerType, true) != null ||
                           component.GetComponentInChildren(_peerType, true) != null;
            _peerMissing = !present;
            if (!_peerMissing)
                return;

            string niceName = ConvaiComponentTypeResolver.DisplayName(_peerType);
            _peerMessage = new GUIContent(
                $"This behavior needs a {niceName} component on the character to run.");
            _addPeerButton = new GUIContent(
                $"Add {niceName}",
                "Add the missing component to this object so the behavior can run.");
        }

        /// <summary>
        ///     Resolves <see cref="ConvaiActionArchetypeAttribute.RequiredTargetComponent" /> for the
        ///     unconditional explanation notice <see cref="DrawTargetRequirement" /> renders. Unlike
        ///     <see cref="RefreshPeerRequirement" /> this never checks presence/absence — this
        ///     component has no specific resolved target to check at authoring time, only the
        ///     archetype's declared requirement.
        /// </summary>
        private void RefreshTargetRequirement(Type componentType)
        {
            var archetype = componentType.GetCustomAttribute<ConvaiActionArchetypeAttribute>();
            string hint = archetype?.RequiredTargetComponent;
            if (string.IsNullOrWhiteSpace(hint))
                return;

            _targetRequirementType = ConvaiComponentTypeResolver.Resolve(hint.Trim());
            if (_targetRequirementType == null)
                return; // Free-text hint naming no loaded component type: nothing to explain.

            string niceName = ConvaiComponentTypeResolver.DisplayName(_targetRequirementType);
            _targetRequirementMessage = new GUIContent(
                $"Objects this action targets need a {niceName} component.\n" +
                "Select the object this action points at in the scene to add it there.");
        }
    }
}
