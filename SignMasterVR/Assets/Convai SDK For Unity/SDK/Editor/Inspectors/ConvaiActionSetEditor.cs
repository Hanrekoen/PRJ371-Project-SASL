using System.Collections.Generic;
using Convai.Editor.Actions;
using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Editor.UI;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Summary-card inspector for a <see cref="ConvaiActionSet" /> asset. It says what the asset is,
    ///     shows what it holds, and hands authoring back to <see cref="ConvaiActionsEditorWindow" />,
    ///     in place of Unity's default ScriptableObject drawer.
    /// </summary>
    /// <remarks>
    ///     Deliberately read-only. A set's actions are authored in the Actions Editor, where a live
    ///     character supplies the context that makes the editing meaningful — which behavior components
    ///     actually resolve, what the rendered command preview reads like, and which characters an edit
    ///     will affect. Reproducing a second, contextless editing surface here would invite the two to
    ///     drift apart. Built on <see cref="ConvaiInspectorEditor" /> (Convai header/purpose via its
    ///     declared hooks) but owns its own <see cref="OnInspectorGUI" />: this editor is entirely
    ///     computed/read-only content, so the framework's generic per-field section renderer does not
    ///     apply here. There is no direct "open the Actions Editor for this asset" window API (the
    ///     window is character-centric — see <see cref="ConvaiActionsEditorWindow.ShowWindowFor" />),
    ///     so this editor keeps the existing character-mediated affordance: open the editor on the
    ///     first Convai Character in the open scenes that uses this set.
    /// </remarks>
    [CustomEditor(typeof(ConvaiActionSet))]
    internal sealed class ConvaiActionSetEditor : ConvaiInspectorEditor
    {
        private const int MaxPreviewRows = 10;

        /// <summary>
        ///     Characters in the open scenes that use this set, and the unresolved-behavior notices
        ///     for it — both rebuilt on a timer rather than per draw pass.
        /// </summary>
        /// <remarks>
        ///     Both answers cost a full-scene component scan and, for the notices, a type resolution
        ///     per action definition — too much to redo on every repaint. Nothing they report — which
        ///     characters reference this asset, whether a behavior name resolves — can change faster
        ///     than a user can act, so both are throttled by <see cref="ConvaiEditorRefreshTimer" />.
        /// </remarks>
        private readonly List<ConvaiActionConfigSource> _users = new();

        private readonly List<UnresolvedHintNotice> _unresolvedNotices = new();
        private ConvaiEditorRefreshTimer _sceneScanTimer;
        private bool _sceneScanValid;

        /// <summary>One action whose authored behavior name resolves to nothing Convai knows about.</summary>
        private readonly struct UnresolvedHintNotice
        {
            internal UnresolvedHintNotice(string actionName, string message)
            {
                ActionName = actionName;
                Message = message;
            }

            internal string ActionName { get; }
            internal string Message { get; }
        }

        protected override string Title => ConvaiActionsEditorStrings.SetInspectorTitle.text;

        protected override string Purpose => ConvaiActionsEditorStrings.SetInspectorIntro.text;

        /// <summary>
        ///     Owns the whole body: this editor draws only computed/read-only content (stat tiles, a
        ///     read-only action preview, and an open-editor button) — there are no serialized fields
        ///     to hand to the base's generic per-field section renderer.
        /// </summary>
        protected override void DrawBody()
        {
            var set = (ConvaiActionSet)target;

            RefreshSceneScanIfDue(set);

            DrawStatTiles(set, _users.Count);
            EditorGUILayout.Space(8f);

            DrawUnresolvedHintNotices();

            DrawActionList(set, _users);
            EditorGUILayout.Space(10f);

            DrawOpenEditorButton(_users);
            EditorGUILayout.Space(4f);
        }

        /// <summary>
        ///     Rebuilds the scene-derived state — who uses this set, and which of its actions name a
        ///     behavior that resolves to nothing — when the refresh timer allows it.
        /// </summary>
        private void RefreshSceneScanIfDue(ConvaiActionSet set)
        {
            if (!_sceneScanTimer.ShouldRefresh(_sceneScanValid))
                return;

            _sceneScanValid = true;
            RefreshUsers(set);
            RefreshUnresolvedHintNotices(set);
        }

        /// <summary>
        ///     Characters in the open scenes that use this set. Scoped to open scenes because that is
        ///     all an inspector can honestly see — the "USED BY" tile is explicitly about the scenes you
        ///     have open, never a project-wide claim.
        /// </summary>
        private void RefreshUsers(ConvaiActionSet set)
        {
            _users.Clear();
            ConvaiActionConfigSource[] sources =
                ConvaiObjectFind.All<ConvaiActionConfigSource>(FindObjectsInactive.Include);
            for (int i = 0; i < sources.Length; i++)
            {
                if (ConvaiActionsEditorModel.IsSetAssigned(sources[i], set))
                    _users.Add(sources[i]);
            }
        }

        /// <summary>
        ///     Collects an inline, actionable notice for every action whose authored behavior name
        ///     resolves to nothing Convai knows about — a typo, a rename, or a module that is not
        ///     installed. Left unaddressed, such an action never runs and nothing else in the Editor
        ///     says why; this is the earliest surface a set author sees it (also surfaced at runtime,
        ///     log-once, and in the Action Troubleshooter). Explanation-only by design: only the author
        ///     knows which behavior was actually meant, so there is no safe automatic fix.
        /// </summary>
        private void RefreshUnresolvedHintNotices(ConvaiActionSet set)
        {
            _unresolvedNotices.Clear();

            IReadOnlyList<ConvaiActionDefinition> definitions = set.Definitions;
            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                string hint = definition?.ExecutorTypeHint;
                if (definition == null || definition.Executor != null || string.IsNullOrWhiteSpace(hint))
                    continue;

                if (ConvaiActionExecutorBinder.TryResolveType(hint, out _))
                    continue;

                string actionName = string.IsNullOrWhiteSpace(definition.ActionName)
                    ? "(unnamed action)"
                    : definition.ActionName;
                GUIContent notice =
                    ConvaiActionsEditorStrings.BuildSetInspectorHintUnresolvedNotice(actionName, hint.Trim());
                _unresolvedNotices.Add(new UnresolvedHintNotice(actionName, notice.text));
            }
        }

        private void DrawUnresolvedHintNotices()
        {
            if (_unresolvedNotices.Count == 0)
                return;

            for (int i = 0; i < _unresolvedNotices.Count; i++)
            {
                UnresolvedHintNotice notice = _unresolvedNotices[i];
                ConvaiEditorFrame.WarningBox(notice.ActionName, notice.Message);
            }

            EditorGUILayout.Space(4f);
        }

        private static void DrawStatTiles(ConvaiActionSet set, int userCount)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Theme.StatTile(ConvaiActionsEditorStrings.SetInspectorTileActions, set.Definitions.Count.ToString());
                GUILayout.Space(6f);
                Theme.StatTile(ConvaiActionsEditorStrings.SetInspectorTileUsedBy, userCount.ToString());
            }
        }

        private static void DrawActionList(ConvaiActionSet set, List<ConvaiActionConfigSource> users)
        {
            IReadOnlyList<ConvaiActionDefinition> definitions = set.Definitions;
            if (definitions.Count == 0)
            {
                Theme.BeginPanel(null);
                GUILayout.Label(ConvaiActionsEditorStrings.SetInspectorEmptyBody, Theme.MutedWrapped);
                Theme.EndPanel(0f);
                return;
            }

            int shown = Mathf.Min(definitions.Count, MaxPreviewRows);
            for (int i = 0; i < shown; i++)
                DrawPreviewRow(definitions[i], users);

            if (definitions.Count > shown)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(8f);
                    GUILayout.Label(
                        ConvaiActionsEditorStrings.BuildInspectorMoreRow(definitions.Count - shown), Theme.MicroLabel);
                }
            }
        }

        /// <summary>
        ///     One read-only action row. Clicking it deep-links into the Actions Editor on a character
        ///     that uses this set — the row is only clickable when such a character exists, because the
        ///     window edits a set through a character and has nothing to show without one.
        /// </summary>
        private static void DrawPreviewRow(ConvaiActionDefinition definition, List<ConvaiActionConfigSource> users)
        {
            Rect slot = GUILayoutUtility.GetRect(0f, 26f, GUILayout.ExpandWidth(true));
            var card = new Rect(slot.x, slot.y + 1f, slot.width, 24f);
            ConvaiActionConfigSource host = FirstLiveUser(users);
            bool clickable = host != null;
            bool hover = clickable && card.Contains(Event.current.mousePosition);

            Theme.FillRounded(card, hover ? Theme.CardBgHover : Theme.CardBg, 5f);
            Theme.StrokeRounded(card,
                hover ? Theme.Fade(Theme.Accent, 0.7f) : Theme.CardBorder, 5f);

            string displayName = string.IsNullOrWhiteSpace(definition?.ActionName)
                ? "(unnamed action)"
                : definition.ActionName;

            var nameRect = new Rect(card.x + 10f, card.y, card.width - 14f, card.height);
            if (!clickable)
            {
                GUI.Label(nameRect, displayName, Theme.CardName);
                return;
            }

            EditorGUIUtility.AddCursorRect(card, MouseCursor.Link);
            if (GUI.Button(nameRect, ConvaiActionsEditorStrings.BuildSetInspectorRowLabel(displayName), Theme.CardName))
                ConvaiActionsEditorWindow.ShowWindowFor(host, definition);
        }

        /// <summary>
        ///     The first still-live character in the cached user list, or <c>null</c>.
        /// </summary>
        /// <remarks>
        ///     The list is rebuilt on a timer, so for up to one interval it can name a character the
        ///     user has just deleted. Reading <c>name</c> off a destroyed object throws, which would
        ///     take the whole inspector down for a state that resolves itself on the next refresh —
        ///     so the draw path asks for a live one rather than assuming the cache is current.
        /// </remarks>
        private static ConvaiActionConfigSource FirstLiveUser(List<ConvaiActionConfigSource> users)
        {
            for (int i = 0; i < users.Count; i++)
            {
                if (users[i] != null)
                    return users[i];
            }

            return null;
        }

        private static void DrawOpenEditorButton(List<ConvaiActionConfigSource> users)
        {
            ConvaiActionConfigSource first = FirstLiveUser(users);
            if (first == null)
            {
                Theme.BeginPanel(Theme.TextSecondary);
                GUILayout.Label(ConvaiActionsEditorStrings.SetInspectorNoUserBody, Theme.BodyWrapped);
                Theme.EndPanel(0f);
                return;
            }

            GUIContent label = users.Count == 1
                ? ConvaiActionsEditorStrings.BuildSetInspectorOpenOnCharacter(first.name)
                : ConvaiActionsEditorStrings.SetInspectorOpenEditorButton;

            Rect openRect = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
            if (Theme.PrimaryButton(openRect, label))
                ConvaiActionsEditorWindow.ShowWindowFor(first);
        }
    }
}
