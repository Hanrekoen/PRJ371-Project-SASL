using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Shows the proposed starting categories for a character whose actions are all unfiled, with
    ///     every proposed name editable before anything is written.
    /// </summary>
    /// <remarks>
    ///     The proposal is shown, never applied silently. An organization feature that files a user's
    ///     work for them without asking is not organizing, it is rearranging — and the names it picks
    ///     are the SDK's words, not theirs. Clearing a name here leaves those actions where they were.
    /// </remarks>
    internal sealed class ConvaiCategorySuggestionWindow : EditorWindow
    {
        private const float Width = 440f;

        private List<ConvaiActionsGrouping.CategorySuggestion> _suggestions = new();
        private Action<List<ConvaiActionsGrouping.CategorySuggestion>> _onAccept;
        private Vector2 _scroll;

        internal static void Show(
            List<ConvaiActionsGrouping.CategorySuggestion> suggestions,
            Action<List<ConvaiActionsGrouping.CategorySuggestion>> onAccept)
        {
            if (suggestions == null || suggestions.Count == 0)
                return;

            var window = CreateInstance<ConvaiCategorySuggestionWindow>();
            window.titleContent = new GUIContent(
                ConvaiActionsEditorStrings.SuggestCategoriesButton.text, UI.ConvaiEditorIcons.Emblem());
            window._suggestions = suggestions;
            window._onAccept = onAccept;

            float height = Mathf.Min(420f, 150f + (suggestions.Count * 30f));
            window.minSize = new Vector2(Width, height);
            window.maxSize = new Vector2(Width, 520f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(Theme.PaneContent))
            {
                GUILayout.Label(ConvaiActionsEditorStrings.SuggestPromptExplainer, Theme.MutedWrapped);
                GUILayout.Space(8f);

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                for (int i = 0; i < _suggestions.Count; i++)
                    DrawSuggestionRow(_suggestions[i]);
                EditorGUILayout.EndScrollView();

                GUILayout.Space(8f);
                DrawButtons();
            }
        }

        private static void DrawSuggestionRow(ConvaiActionsGrouping.CategorySuggestion suggestion)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                suggestion.Category = EditorGUILayout.TextField(suggestion.Category);

                GUIContent count = ConvaiActionsEditorStrings.BuildCountPill(suggestion.Rows.Count);
                float width = Theme.PillWidth(count);
                Rect pill = GUILayoutUtility.GetRect(width, 18f, GUILayout.Width(width), GUILayout.Height(18f));
                Theme.Pill(Theme.CenteredSlice(pill, 16f), count, Theme.TextMuted);
            }

            GUILayout.Space(4f);
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

                Rect applyRect = GUILayoutUtility.GetRect(120f, 26f, GUILayout.Width(120f), GUILayout.Height(26f));
                if (Theme.PrimaryButton(applyRect, ConvaiActionsEditorStrings.SuggestPromptApplyButton))
                    Accept();
            }
        }

        private void Accept()
        {
            Action<List<ConvaiActionsGrouping.CategorySuggestion>> callback = _onAccept;
            List<ConvaiActionsGrouping.CategorySuggestion> accepted = _suggestions;

            Close();
            callback?.Invoke(accepted);
        }
    }
}
