using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     The small window that names a category — used both to create one and to rename an existing
    ///     one. It offers the names already in use, warns when a new name is a hair away from one of
    ///     them, and says out loud when the change reaches beyond this character.
    /// </summary>
    /// <remarks>
    ///     A plain text prompt would have been half the code and most of the reason category lists rot:
    ///     with nothing offering "Tour" back to you, "Tours" gets typed, and from then on the user's own
    ///     grouping means less than it did. Everything extra in here exists to make picking an existing
    ///     name easier than inventing a new one.
    /// </remarks>
    internal sealed class ConvaiCategoryPromptWindow : EditorWindow
    {
        private const string NameControlName = "ConvaiCategoryPrompt.Name";
        private const float Width = 420f;

        private string _name = string.Empty;
        private List<string> _existing = new();
        private string _summary;
        private int _sharedCount;
        private bool _isRename;
        private bool _focusRequested;
        private Action<string> _onAccept;
        private Vector2 _existingScroll;

        /// <summary>Opens the prompt for a brand new category.</summary>
        internal static void ShowNew(List<string> existing, int sharedCount, Action<string> onAccept) =>
            Open(string.Empty, existing, null, sharedCount, false, onAccept);

        /// <summary>Opens the prompt pre-filled with the category being renamed.</summary>
        internal static void ShowRename(
            string category, List<string> existing, string summary, int sharedCount, Action<string> onAccept) =>
            Open(category, existing, summary, sharedCount, true, onAccept);

        private static void Open(
            string name, List<string> existing, string summary, int sharedCount, bool isRename, Action<string> onAccept)
        {
            var window = CreateInstance<ConvaiCategoryPromptWindow>();
            window.titleContent = new GUIContent(
                isRename
                    ? ConvaiActionsEditorStrings.CategoryHeaderMenuRename.text
                    : ConvaiActionsEditorStrings.RowMenuNewCategory.text,
                UI.ConvaiEditorIcons.Emblem());
            window._name = name ?? string.Empty;
            window._existing = existing ?? new List<string>();
            window._summary = summary;
            window._sharedCount = sharedCount;
            window._isRename = isRename;
            window._onAccept = onAccept;
            window._focusRequested = true;

            float height = 150f + (window._existing.Count > 0 ? 78f : 0f) + (summary != null ? 26f : 0f) +
                           (sharedCount > 0 ? 34f : 0f);
            window.minSize = new Vector2(Width, height);
            window.maxSize = new Vector2(Width, height + 120f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(Theme.PaneContent))
            {
                HandleKeyboard();

                if (!string.IsNullOrEmpty(_summary))
                {
                    GUILayout.Label(_summary, Theme.MutedWrapped);
                    GUILayout.Space(6f);
                }

                GUI.SetNextControlName(NameControlName);
                _name = EditorGUILayout.TextField(ConvaiActionsEditorStrings.CategoryPromptNameField, _name);
                if (_focusRequested && Event.current.type == EventType.Repaint)
                {
                    EditorGUI.FocusTextInControl(NameControlName);
                    _focusRequested = false;
                }

                string nearDuplicate = _isRename ? null : ConvaiActionsGrouping.FindNearDuplicate(_existing, _name);
                if (nearDuplicate != null)
                {
                    GUILayout.Space(4f);
                    Theme.BeginPanel(Theme.StatusWarn);
                    GUILayout.Label(
                        ConvaiActionsEditorStrings.BuildCategoryNearDuplicateWarning(nearDuplicate).text,
                        Theme.MutedWrapped);
                    Theme.EndPanel(0f);
                }

                if (_sharedCount > 0)
                {
                    GUILayout.Space(4f);
                    Theme.BeginPanel(Theme.StatusWarn);
                    GUILayout.Label(
                        ConvaiActionsEditorStrings.BuildCategorySharedNote(_sharedCount).text, Theme.MutedWrapped);
                    Theme.EndPanel(0f);
                }

                if (_existing.Count > 0)
                    DrawExistingNames();

                GUILayout.FlexibleSpace();
                DrawButtons();
            }
        }

        /// <summary>
        ///     The names already in use, one click away. This is the part that keeps a category list
        ///     short: the cheapest thing to do is reuse a name, not type a new one.
        /// </summary>
        private void DrawExistingNames()
        {
            GUILayout.Space(8f);
            GUILayout.Label(ConvaiActionsEditorStrings.CategoryPromptExistingLabel, Theme.MicroLabel);
            GUILayout.Space(2f);

            _existingScroll = EditorGUILayout.BeginScrollView(_existingScroll, GUILayout.Height(56f));
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < _existing.Count; i++)
                {
                    GUIContent label = ConvaiActionsEditorStrings.BuildCategoryMenuItem(_existing[i]);
                    float width = Theme.PillWidth(label) + 10f;
                    Rect pill = GUILayoutUtility.GetRect(width, 22f, GUILayout.Width(width), GUILayout.Height(22f));
                    if (Theme.GhostButton(pill, label))
                        _name = _existing[i];

                    GUILayout.Space(4f);
                }

                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                Rect cancelRect = GUILayoutUtility.GetRect(90f, 26f, GUILayout.Width(90f), GUILayout.Height(26f));
                if (Theme.GhostButton(cancelRect, ConvaiActionsEditorStrings.CategoryPromptCancelButton))
                    Close();

                GUILayout.Space(8f);

                using (new EditorGUI.DisabledScope(!CanAccept))
                {
                    Rect acceptRect = GUILayoutUtility.GetRect(110f, 26f, GUILayout.Width(110f), GUILayout.Height(26f));
                    GUIContent accept = _isRename
                        ? ConvaiActionsEditorStrings.CategoryPromptRenameButton
                        : ConvaiActionsEditorStrings.CategoryPromptCreateButton;
                    if (Theme.PrimaryButton(acceptRect, accept))
                        Accept();
                }
            }
        }

        private bool CanAccept => !string.IsNullOrWhiteSpace(_name);

        private void HandleKeyboard()
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown)
                return;

            if (current.keyCode == KeyCode.Escape)
            {
                current.Use();
                Close();
                return;
            }

            if ((current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter) && CanAccept)
            {
                current.Use();
                Accept();
            }
        }

        private void Accept()
        {
            Action<string> callback = _onAccept;
            string chosen = _name;

            // Closed first, so the callback's own dialogs and repaints do not run underneath a window
            // that is about to disappear.
            Close();
            callback?.Invoke(chosen);
        }
    }
}
