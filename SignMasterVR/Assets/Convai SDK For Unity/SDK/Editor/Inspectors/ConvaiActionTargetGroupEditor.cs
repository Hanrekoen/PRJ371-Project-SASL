using System;
using System.Collections.Generic;
using System.Reflection;
using Convai.Editor.Actions;
using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Actions;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiActionTargetGroup" />: explains that this is a
    ///     named, ordered set of targets that Count Target Group can count, and that the Convai
    ///     Character can talk about as one place or set. Surfaces order-matters guidance for
    ///     the member list, and validates the member list and group name against the exact
    ///     conditions <see cref="ConvaiActionTargetGroup" /> and its consumers silently misbehave on.
    /// </summary>
    [CustomEditor(typeof(ConvaiActionTargetGroup))]
    [CanEditMultipleObjects]
    internal sealed class ConvaiActionTargetGroupEditor : ConvaiInspectorEditor
    {
        private const string TitleText = "Action Target Group";
        private const string SubtitleText = "Scene Knowledge";
        private const string PurposeText =
            "A named, ordered list of objects that Count Target Group can count, and that the " +
            "Convai Character can talk about as one place or set.";

        private static readonly GUIContent GroupNameLabel = new(
            "Name", "What actions reference this group by. Leave blank to use the GameObject's name.");

        private static readonly GUIContent DescriptionLabel = new(
            "Description", "A short description sent to the Convai Character so it understands what this group is.");

        private static readonly GUIContent MembersLabel = new(
            "Members", "Members in authored order. When Order Matters is on, this is a sequence; when off, it is a set the character can address as a whole.");

        private static readonly GUIContent IsOrderedLabel = new(
            "Order Matters", "On for a sequence actions should follow in order (a tour, a patrol route). " +
                              "Off for a set actions can address as a whole in any order.");

        private static readonly GUIContent RegisterOnEnableLabel = new(
            "Register Automatically",
            "Adds this group when the object is enabled and removes it when disabled. Turn off to control registration from code.");

        private static readonly GUIContent MembersTitle = new("Members");
        private static readonly GUIContent IdentityTitle = new("Identity");
        private static readonly GUIContent VisibilityTitle = new("Visibility");
        private static readonly GUIContent UsedByTitle = new("Used By");

        private static readonly GUIContent ReorderHint = new(
            "Drag the handle on the left of each row to reorder members — order is what ordered actions follow.");

        private static readonly GUIContent EmptyMembersWarning = new(
            "This group has no members yet. Actions that step through it will have nothing to visit.");

        private static readonly GUIContent MissingMembersWarningFormat = new(
            "This group has {0} missing member slot(s) (deleted or unassigned objects). They are silently " +
            "skipped, so the group visits fewer stops than the list shows.");

        private static readonly GUIContent DuplicateMembersWarningFormat = new(
            "{0} member(s) appear more than once in this group. Duplicates are visited more than once " +
            "in the same pass — remove the repeats if that was not intended.");

        private static readonly GUIContent NoNameWarning = new(
            "This group and its GameObject both have no usable name, so it cannot register — actions " +
            "will never be able to address it, and this is logged once as a warning at runtime.");

        private static readonly GUIContent UnaddressableMembersWarningFormat = new(
            "{0} member(s) are disabled right now, so they cannot be addressed individually by name — " +
            "the group will still try to visit them.");

        private static readonly GUIContent NoUsagesHint = new(
            "No action in the open scene currently references this group by name. It will do nothing until one does.");

        private const string UsedBySingular = "Used by 1 action in this scene:";
        private const string UsedByPluralFormat = "Used by {0} actions in this scene:";

        private SerializedProperty _groupNameProp;
        private SerializedProperty _descriptionProp;
        private SerializedProperty _membersProp;
        private SerializedProperty _isOrderedProp;
        private SerializedProperty _registerOnEnableProp;

        // Member-list validation results: recomputed every draw from the small local serialized
        // list only (no scene scan here, so no dirty-flag caching is needed for this part).
        private int _missingMemberCount;
        private int _duplicateMemberCount;
        private int _disabledMemberCount;

        // Scene "used by" scan cache (mirrors ConvaiActionTargetEditor's reverse-view scan): only
        // recomputed on enable, on hierarchy changes, or after this inspector's own edits — never
        // per repaint.
        private readonly List<string> _usedByLabels = new();
        private bool _usedByDirty = true;

        protected override string Title => TitleText;
        protected override string Subtitle => SubtitleText;
        protected override string Purpose => PurposeText;

        protected override void OnEnable()
        {
            base.OnEnable();

            _groupNameProp = serializedObject.FindProperty("_groupName");
            _descriptionProp = serializedObject.FindProperty("_description");
            _membersProp = serializedObject.FindProperty("_members");
            _isOrderedProp = serializedObject.FindProperty("_isOrdered");
            _registerOnEnableProp = serializedObject.FindProperty("_registerOnEnable");

            EditorApplication.hierarchyChanged += MarkUsedByDirty;
        }

        protected override void OnDisable()
        {
            EditorApplication.hierarchyChanged -= MarkUsedByDirty;
            base.OnDisable();
        }

        private void MarkUsedByDirty()
        {
            _usedByDirty = true;
            Repaint();
        }

        protected override void OnBeforeInspectorGUI()
        {
            RefreshMemberValidation();
            if (_usedByDirty)
                RefreshUsedBy();
        }

        // Curated sections replace the generic field renderer, as in ConvaiActionTargetEditor.
        protected override void DrawBody()
        {
            DrawIdentitySection();
            DrawMembersSection();
            DrawVisibilitySection();
            DrawUsedBySection();
        }

        private void DrawIdentitySection()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Identity, IdentityTitle);

            EditorGUILayout.PropertyField(_groupNameProp, GroupNameLabel);
            if (targets.Length == 1 && string.IsNullOrWhiteSpace(_groupNameProp.stringValue))
            {
                var owner = (ConvaiActionTargetGroup)target;
                GUILayout.Label($"Uses the GameObject name: '{owner.gameObject.name}'", Theme.MutedWrapped);
            }

            if (targets.Length == 1 && IsGroupUnnamed((ConvaiActionTargetGroup)target))
            {
                GUILayout.Space(4f);
                Theme.BeginPanel(Theme.StatusError);
                GUILayout.Label(NoNameWarning, Theme.BodyWrapped);
                Theme.EndPanel(0f);
            }

            GUILayout.Space(4f);
            EditorGUILayout.PropertyField(_descriptionProp, DescriptionLabel);

            Theme.EndCard();
        }

        private void DrawMembersSection()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Identity, MembersTitle);

            EditorGUILayout.PropertyField(_isOrderedProp, IsOrderedLabel);
            GUILayout.Space(4f);

            EditorGUILayout.PropertyField(_membersProp, MembersLabel, true);
            GUILayout.Label(ReorderHint, Theme.MutedWrapped);

            if (_membersProp.arraySize == 0)
            {
                GUILayout.Space(4f);
                Theme.BeginPanel(Theme.StatusWarn);
                GUILayout.Label(EmptyMembersWarning, Theme.BodyWrapped);
                Theme.EndPanel(0f);
            }
            else
            {
                if (_missingMemberCount > 0)
                {
                    GUILayout.Space(4f);
                    Theme.BeginPanel(Theme.StatusWarn);
                    GUILayout.Label(new GUIContent(string.Format(MissingMembersWarningFormat.text, _missingMemberCount)),
                        Theme.BodyWrapped);
                    Theme.EndPanel(0f);
                }

                if (_duplicateMemberCount > 0)
                {
                    GUILayout.Space(4f);
                    Theme.BeginPanel(Theme.StatusWarn);
                    GUILayout.Label(new GUIContent(string.Format(DuplicateMembersWarningFormat.text, _duplicateMemberCount)),
                        Theme.BodyWrapped);
                    Theme.EndPanel(0f);
                }

                if (_disabledMemberCount > 0)
                {
                    GUILayout.Space(4f);
                    Theme.BeginPanel(null);
                    GUILayout.Label(new GUIContent(string.Format(UnaddressableMembersWarningFormat.text, _disabledMemberCount)),
                        Theme.BodyWrapped);
                    Theme.EndPanel(0f);
                }
            }

            Theme.EndCard();
        }

        private void DrawVisibilitySection()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Visibility, VisibilityTitle);
            EditorGUILayout.PropertyField(_registerOnEnableProp, RegisterOnEnableLabel);
            Theme.EndCard();
        }

        private void DrawUsedBySection()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Animator, UsedByTitle);

            if (targets.Length != 1)
            {
                Theme.EndCard();
                return;
            }

            if (_usedByLabels.Count == 0)
            {
                GUILayout.Label(NoUsagesHint, Theme.MutedWrapped);
            }
            else
            {
                string header = _usedByLabels.Count == 1
                    ? UsedBySingular
                    : string.Format(UsedByPluralFormat, _usedByLabels.Count);
                GUILayout.Label(header, Theme.BodyWrapped);
                GUILayout.Space(2f);
                for (int i = 0; i < _usedByLabels.Count; i++)
                    GUILayout.Label("•  " + _usedByLabels[i], Theme.MutedWrapped);
            }

            Theme.EndCard();
        }

        // --- Member validation -------------------------------------------------------------

        private static bool IsGroupUnnamed(ConvaiActionTargetGroup group) =>
            group != null && string.IsNullOrWhiteSpace(group.GroupName);

        private void RefreshMemberValidation()
        {
            _missingMemberCount = 0;
            _duplicateMemberCount = 0;
            _disabledMemberCount = 0;

            if (targets.Length != 1)
                return;

            var seen = new HashSet<UnityEngine.Object>();
            var duplicates = new HashSet<UnityEngine.Object>();

            for (int i = 0; i < _membersProp.arraySize; i++)
            {
                SerializedProperty element = _membersProp.GetArrayElementAtIndex(i);
                var member = element.objectReferenceValue as ConvaiActionTarget;
                if (member == null)
                {
                    _missingMemberCount++;
                    continue;
                }

                if (!seen.Add(member) && duplicates.Add(member))
                    _duplicateMemberCount++;

                if (!member.isActiveAndEnabled)
                    _disabledMemberCount++;
            }
        }

        // --- "Used by" scene scan -----------------------------------------------------------

        /// <summary>
        ///     Scans every <see cref="ConvaiActionExecutorBase" /> in the open scenes for a field
        ///     that either directly references this group (a <see cref="ConvaiActionTargetGroup" />
        ///     field) or names it by string in a "group"-named field (the style the group-driven
        ///     attention behaviors use) — the two binding styles group consumers actually use,
        ///     discovered generically by
        ///     reflection so this editor never needs a compile-time reference into an optional
        ///     module's executor types.
        /// </summary>
        private void RefreshUsedBy()
        {
            _usedByDirty = false;
            _usedByLabels.Clear();

            if (targets.Length != 1 || target == null)
                return;

            var owner = (ConvaiActionTargetGroup)target;
            GameObject go = owner.gameObject;
            if (EditorUtility.IsPersistent(go))
                return;

            string groupName = owner.GroupName;
            ConvaiActionExecutorBase[] executors =
                ConvaiObjectFind.All<ConvaiActionExecutorBase>(FindObjectsInactive.Include);

            for (int i = 0; i < executors.Length; i++)
            {
                ConvaiActionExecutorBase executor = executors[i];
                if (executor == null)
                    continue;

                if (!ReferencesGroup(executor, owner, groupName))
                    continue;

                string niceName = ConvaiComponentTypeResolver.DisplayName(executor.GetType());
                _usedByLabels.Add($"{niceName} on '{executor.gameObject.name}'");
            }
        }

        private static bool ReferencesGroup(ConvaiActionExecutorBase executor, ConvaiActionTargetGroup owner, string groupName)
        {
            for (Type type = executor.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                FieldInfo[] fields = type.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                for (int f = 0; f < fields.Length; f++)
                {
                    FieldInfo field = fields[f];

                    if (field.FieldType == typeof(ConvaiActionTargetGroup))
                    {
                        if (ReferenceEquals(field.GetValue(executor), owner))
                            return true;
                    }
                    else if (field.FieldType == typeof(string) &&
                             field.Name.IndexOf("group", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var value = field.GetValue(executor) as string;
                        if (!string.IsNullOrWhiteSpace(value) &&
                            string.Equals(value, groupName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }

            return false;
        }
    }
}
