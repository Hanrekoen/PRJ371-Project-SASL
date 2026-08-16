using System.Collections.Generic;
using Convai.Editor.Actions;
using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Controls = Convai.Editor.UI.ConvaiEditorControls;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Summary-card inspector for <see cref="ConvaiActionConfigSource" /> :
    ///     the Convai header with a health pill, stat tiles (actions / action sets / issues), a
    ///     bounded, scrollable list of every action on the character (each row deep-links into the
    ///     Actions Editor window), and Troubleshooter / Validate shortcuts. All authoring lives in
    ///     <see cref="ConvaiActionsEditorWindow" /> — this inspector never mutates data.
    /// </summary>
    /// <remarks>
    ///     Built on <see cref="ConvaiInspectorEditor" /> (Convai header/status-chip/purpose via its
    ///     declared hooks) but owns its own <see cref="OnInspectorGUI" />: this editor is entirely
    ///     computed/read-only content (no direct serialized-field editing), so the framework's
    ///     generic per-field section renderer does not apply here.
    /// </remarks>
    [CustomEditor(typeof(ConvaiActionConfigSource))]
    internal sealed class ConvaiActionConfigSourceEditor : ConvaiInspectorEditor
    {
        /// <summary>Height of one action row, including the pixel of breathing room above it.</summary>
        private const float RowHeight = 26f;

        /// <summary>Height of one category header row inside the list.</summary>
        private const float HeaderHeight = 24f;

        /// <summary>
        ///     How many rows the list shows before it starts scrolling. Every action stays reachable —
        ///     this only bounds how much of the inspector the list is allowed to take.
        /// </summary>
        private const int MaxVisibleRows = 8;

        /// <summary>
        ///     How many action behaviors have to be on the Convai Character before this inspector
        ///     mentions that they could live on a child object instead. Set at roughly the point where
        ///     the character's own components stop fitting on one screen — below it, the offer would
        ///     be a choice with no problem behind it.
        /// </summary>
        private const int BehaviorHostOfferThreshold = 6;

        /// <summary>
        ///     Height of the one-line "where the behaviors are" row. Sized to a caption rather than to
        ///     a control: the row reports a fact, and anything taller reads as a section.
        /// </summary>
        private const float MetaRowHeight = 20f;

        private IReadOnlyList<ConvaiActionConfigDiagnostic> _diagnostics;

        /// <summary>
        ///     The character-wide setup report behind the header chip and the Issues tile.
        /// </summary>
        /// <remarks>
        ///     Not the validator's diagnostic list, which is only part of the picture: it knows
        ///     nothing about a missing dispatcher, an unbound behavior, or a component an action needs
        ///     on its target. This card and the Action Troubleshooter both read
        ///     <see cref="ConvaiActionSetupReport" />, so they always agree on the count. The per-row
        ///     statuses below still come from <see cref="_diagnostics" />, because a row can only be
        ///     coloured by findings that name that action.
        /// </remarks>
        private ConvaiActionSetupReport _report = ConvaiActionSetupReport.Empty;

        private Vector2 _actionListScroll;

        protected override string Title => ConvaiActionsEditorStrings.InspectorStatusCardTitle.text;

        /// <summary>
        ///     What this component is, said inside the header plate next to the title — the slot every
        ///     other Convai inspector fills and this one had left empty, which is why its identity had
        ///     to be re-stated in a sentence below the header instead.
        /// </summary>
        protected override string Subtitle => ConvaiActionsEditorStrings.InspectorHeaderSubtitle;

        /// <summary>
        ///     What is left once the header carries the identity: the one fact neither the header nor
        ///     the body makes obvious — that nothing here is editable, and where the editing happens.
        /// </summary>
        protected override string Purpose => ConvaiActionsEditorStrings.InspectorStatusCardTitle.tooltip;

        protected override GUIContent StatusChip =>
            _report.IsHealthy
                ? ConvaiActionsEditorStrings.InspectorHealthChipReady
                : ConvaiActionsEditorStrings.BuildInspectorHealthChipIssues(_report.IssueCount);

        protected override Color StatusChipTint
        {
            get
            {
                if (_report.IsHealthy) return Theme.StatusReady;
                return _report.ErrorCount > 0 ? Theme.StatusError : Theme.StatusWarn;
            }
        }

        /// <summary>
        ///     The capability this card reports on, so its chip opens the Troubleshooter already
        ///     scrolled to Actions rather than at the top of a report about six other things.
        /// </summary>
        protected override string TroubleshooterModuleId => ConvaiActionsModuleId;

        /// <summary>
        ///     The chip is live exactly when it reports work: clicking it opens the shared
        ///     Troubleshooter on the findings behind the count — title, message, and a one-click fix.
        /// </summary>
        protected override bool StatusChipIsActionable => !_report.IsHealthy;

        /// <summary>Stable id of the Actions capability, matching its setup-health provider.</summary>
        private const string ConvaiActionsModuleId = "convai.actions";

        protected override void OnEnable()
        {
            base.OnEnable();
            RefreshDiagnostics();

            // Running the checks sweeps the scene, so they cannot run per repaint — but a card that
            // only recomputed when it was first drawn went stale the moment the author fixed
            // something in the Actions Editor, and then disagreed with every other surface again.
            // These two are the events that can change the answer without this inspector being
            // rebuilt; the Re-check button covers everything else.
            EditorApplication.hierarchyChanged += RefreshDiagnostics;
            Undo.undoRedoPerformed += RefreshDiagnostics;
        }

        protected override void OnDisable()
        {
            EditorApplication.hierarchyChanged -= RefreshDiagnostics;
            Undo.undoRedoPerformed -= RefreshDiagnostics;
            base.OnDisable();
        }

        protected override void OnBeforeInspectorGUI()
        {
            if (_diagnostics == null)
                RefreshDiagnostics();
        }

        /// <summary>
        ///     Owns the whole body: this editor draws only computed/read-only content (stat tiles,
        ///     a compact action preview, and shortcut buttons) — there are no serialized fields to
        ///     hand to the base's generic per-field section renderer.
        /// </summary>
        protected override void DrawBody()
        {
            var source = (ConvaiActionConfigSource)target;

            DrawStatTiles(source);
            EditorGUILayout.Space(8f);
            DrawCompactActionList(source);
            DrawBehaviorHostNotices(source);
            EditorGUILayout.Space(10f);
            DrawButtons(source);
            DrawBehaviorHostStrip(source);
            EditorGUILayout.Space(4f);
        }

        /// <summary>
        ///     The two states of behavior hosting that are worth interrupting the reader for: a
        ///     behaviors object that is not part of this character, and — once there are enough
        ///     behaviors to have made this inspector hard to read — the offer to create one.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Four states, three weights, and the weight follows the problem rather than the
        ///         subject. A behaviors object outside the character is a real break: actions bound to
        ///         it silently do nothing, so it gets a full error panel, above the buttons, where the
        ///         reader cannot miss it. The offer is a teaching moment and sits in the same place
        ///         for the same reason. A character with only a handful of behaviors is told nothing
        ///         at all — both layouts are supported and the character is the default, so someone
        ///         whose inspector is still readable is never shown a choice they have no reason to
        ///         make. And the settled state is not a notice at all: it is chrome, and it lives at
        ///         the bottom of the inspector in <see cref="DrawBehaviorHostStrip" />.
        ///     </para>
        ///     <para>
        ///         The offer creates the child object and nothing else. It never moves the existing
        ///         components: moving a component in Unity means recreating it and re-pointing every
        ///         reference to it, and a missed reference is an action that silently stops working.
        ///         Existing behaviors are moved with the strip menu's own copy/remove pair, with this
        ///         inspector's action list confirming each one stays bound in between.
        ///     </para>
        /// </remarks>
        private void DrawBehaviorHostNotices(ConvaiActionConfigSource source)
        {
            if (!source.HasValidBehaviorHost)
            {
                EditorGUILayout.Space(8f);
                Theme.BeginPanel(Theme.StatusError);
                GUILayout.Label(ConvaiActionsEditorStrings.BehaviorHostInvalid, Theme.BodyWrapped);
                EditorGUILayout.Space(6f);
                Rect clearRect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
                if (Theme.GhostButton(clearRect, ConvaiActionsEditorStrings.BehaviorHostClearButton))
                    ConvaiActionBehaviorHosting.SetBehaviorHost(source, null);
                Theme.EndPanel(0f);
                return;
            }

            if (source.ConfiguredBehaviorHost != null)
                return;

            int onCharacter = CountBehaviorsOn(source.gameObject);
            if (onCharacter < BehaviorHostOfferThreshold)
                return;

            EditorGUILayout.Space(8f);
            Theme.BeginPanel(null);
            GUILayout.Label(ConvaiActionsEditorStrings.BuildBehaviorHostOffer(onCharacter), Theme.BodyWrapped);
            EditorGUILayout.Space(6f);
            Rect createRect = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
            if (Theme.GhostButton(createRect, ConvaiActionsEditorStrings.CreateBehaviorHostButton))
                ConvaiActionBehaviorHosting.CreateBehaviorHost(source);
            Theme.EndPanel(0f);
        }

        /// <summary>
        ///     The settled state, as one line at the very bottom of the inspector: a caption, the
        ///     object itself, an optional still-on-the-character pill, and the overflow menu.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Below the buttons on purpose. Where components sit is chrome — a fact to read once,
        ///         not a step in the reader's path — and the path through this inspector is <em>what
        ///         this character can do</em> then <em>open the editor and change it</em>. Anything
        ///         between those two interrupts it, which is exactly how this line felt when it sat
        ///         under the action list. A rule above it and the inspector's bottom edge below it
        ///         make a strip, so the reader can see the line is not content without reading it.
        ///     </para>
        ///     <para>
        ///         Two typography rules keep a one-line meta strip from reading as something it is
        ///         not. Its two halves are one type step apart — a caption next to a value set three
        ///         sizes larger reads as a heading with a stray word in front of it. And the object
        ///         carries no leading mark: a glyph at value size reads as a control, and a disc reads
        ///         as an empty radio button, an affordance this line does not have.
        ///     </para>
        ///     <para>
        ///         What the name does have is a hover chip and a link cursor: clicking it selects and
        ///         pings the object, which answers "where is that?" by showing it in the Hierarchy —
        ///         the most useful thing this line can do for someone new to Unity, and the only
        ///         interaction they ever need from it.
        ///     </para>
        /// </remarks>
        private static void DrawBehaviorHostStrip(ConvaiActionConfigSource source)
        {
            if (!source.HasValidBehaviorHost)
                return;

            Transform host = source.ConfiguredBehaviorHost;
            if (host == null)
                return;

            int onCharacter = CountBehaviorsOn(source.gameObject);

            Theme.HorizontalRule(Theme.Divider, 10f, 4f);

            Rect slot = GUILayoutUtility.GetRect(0f, MetaRowHeight, GUILayout.ExpandWidth(true));
            var row = new Rect(slot.x + 2f, slot.y, slot.width - 4f, MetaRowHeight);

            GUIContent caption = ConvaiActionsEditorStrings.BehaviorHostRowLabel;
            float captionWidth = Theme.TextWidth(Theme.MicroLabel, caption) + 8f;
            GUI.Label(new Rect(row.x, row.y, captionWidth, row.height), caption, Theme.MicroLabel);

            var menuRect = new Rect(row.xMax - 18f, row.y + 1f, 18f, 18f);

            // The split state is the only one that colours anything on this row, and the pill is the
            // door to the commands that end it — so the fix is never something to go looking for.
            GUIContent pill = onCharacter > 0
                ? ConvaiActionsEditorStrings.BuildBehaviorHostRemainingPill(onCharacter)
                : null;
            float pillWidth = pill == null ? 0f : Theme.PillWidth(pill);
            var pillRect = new Rect(menuRect.x - 8f - pillWidth, row.y + 2f, pillWidth, 16f);

            GUIContent name = ConvaiActionsEditorStrings.BuildBehaviorHostRowName(host.name);
            float nameX = row.x + captionWidth;
            float nameMax = (pill == null ? menuRect.x : pillRect.x) - 8f;

            // Measured rather than stretched to the free space: a hover chip as wide as the row would
            // light up half the inspector when the cursor is nowhere near the name.
            float nameWidth = Mathf.Min(Theme.TextWidth(Theme.ReadingLabel, name) + 10f, nameMax - nameX);
            var nameRect = new Rect(nameX, row.y, Mathf.Max(24f, nameWidth), row.height);

            if (nameRect.Contains(Event.current.mousePosition))
                Theme.FillRounded(
                    new Rect(nameRect.x - 4f, nameRect.y + 2f, nameRect.width + 4f, nameRect.height - 4f),
                    Theme.Fade(Theme.Accent, 0.10f), 4f);

            EditorGUIUtility.AddCursorRect(nameRect, MouseCursor.Link);
            if (GUI.Button(nameRect, name, Theme.ReadingLabel))
            {
                Selection.activeGameObject = host.gameObject;
                EditorGUIUtility.PingObject(host.gameObject);
            }

            if (pill != null && Theme.PillButton(pillRect, pill, Theme.StatusWarn))
                ShowBehaviorHostMenu(source, host, onCharacter);

            if (Theme.IconButton(menuRect, ConvaiActionsEditorStrings.BehaviorHostRowMenuButton))
                ShowBehaviorHostMenu(source, host, onCharacter);
        }

        /// <summary>
        ///     Everything this row can do. The copy/remove pair appears only while behaviors are still
        ///     on the character: a command that would do nothing is left out rather than shown greyed,
        ///     so the menu never offers a step that has already been taken.
        /// </summary>
        private static void ShowBehaviorHostMenu(ConvaiActionConfigSource source, Transform host, int onCharacter)
        {
            var menu = new GenericMenu();

            menu.AddItem(ConvaiActionsEditorStrings.BehaviorHostMenuSelect, false, () =>
            {
                Selection.activeGameObject = host.gameObject;
                EditorGUIUtility.PingObject(host.gameObject);
            });

            if (onCharacter > 0)
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(
                    ConvaiActionsEditorStrings.BehaviorHostMenuCopy, false, () => CopyBehaviorsToHost(source));
                menu.AddItem(
                    ConvaiActionsEditorStrings.BehaviorHostMenuRemove, false, () => RemoveCopiedOriginals(source));
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(
                ConvaiActionsEditorStrings.BehaviorHostMenuClear, false,
                () => ConvaiActionBehaviorHosting.SetBehaviorHost(source, null));

            menu.ShowAsContext();
        }

        /// <summary>
        ///     Copies the behaviors still on the character onto its behaviors object and reports what
        ///     happened, including anything that would break if the originals were removed. The report
        ///     is a dialog rather than a console line because the next step — removing the originals —
        ///     is the author's to take, and they need to know what it costs before taking it.
        /// </summary>
        private static void CopyBehaviorsToHost(ConvaiActionConfigSource source)
        {
            (int copied, int repointed) = ConvaiActionBehaviorHosting.CopyBehaviorsToHost(source);

            EditorUtility.DisplayDialog(
                ConvaiActionsEditorStrings.BehaviorHostMenuCopy.text,
                ConvaiActionsEditorStrings.BuildCopyBehaviorsResult(copied, repointed),
                "OK");
        }

        /// <summary>
        ///     Removes the originals that have been copied and are provably unused. Reports everything
        ///     it refused, by name and reason — a command that quietly skips half its work would leave
        ///     the author believing the character is tidy when it is not.
        /// </summary>
        private static void RemoveCopiedOriginals(ConvaiActionConfigSource source)
        {
            bool isPrefabInstance = ConvaiActionBehaviorHosting.IsPrefabInstance(source);
            (int removed, List<string> blocked) = ConvaiActionBehaviorHosting.RemoveCopiedOriginals(source);

            EditorUtility.DisplayDialog(
                ConvaiActionsEditorStrings.BehaviorHostMenuRemove.text,
                ConvaiActionsEditorStrings.BuildRemoveBehaviorsResult(removed, blocked, isPrefabInstance),
                "OK");
        }

        /// <summary>
        ///     How many action behaviors sit directly on <paramref name="host" /> — not counting its
        ///     children, because the question this answers is "how crowded is this one object".
        /// </summary>
        private static int CountBehaviorsOn(GameObject host)
        {
            if (host == null)
                return 0;

            var components = host.GetComponents<MonoBehaviour>();
            int count = 0;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is IConvaiActionExecutor)
                    count++;
            }

            return count;
        }

        /// <summary>
        ///     Re-runs both passes: the character-wide report behind the headline count, and the
        ///     validator diagnostics that colour individual rows. Both are scene-aware — validating
        ///     against the runtime target registry would report every Known entry answered by a
        ///     scene target as broken, because that registry is always empty outside play mode.
        /// </summary>
        private void RefreshDiagnostics()
        {
            var source = target as ConvaiActionConfigSource;
            if (source == null)
            {
                // The component was deleted out from under a subscribed event; leave the last
                // answer alone rather than replacing it with a fabricated one.
                return;
            }

            _diagnostics = ConvaiActionSetupReport.Validate(source);
            _report = ConvaiActionSetupReport.RunFor(source);
            Repaint();
        }

        private void DrawStatTiles(ConvaiActionConfigSource source)
        {
            IReadOnlyList<ConvaiActionDefinition> effective = source.GetEffectiveDefinitions();
            int setCount = 0;
            IReadOnlyList<ConvaiActionSet> sets = source.ActionSets;
            for (int i = 0; sets != null && i < sets.Count; i++)
                if (sets[i] != null)
                    setCount++;

            // The same number as the header chip and the Action Troubleshooter's pill — this tile is
            // the one the reader looks at when the chip says there is work, so it must never be a
            // second, smaller count of a different thing.
            int issues = _report.IssueCount;
            Color? issueTint = issues == 0 ? null : _report.ErrorCount > 0 ? Theme.StatusError : Theme.StatusWarn;

            using (new EditorGUILayout.HorizontalScope())
            {
                Theme.StatTile(ConvaiActionsEditorStrings.InspectorTileActions, effective.Count.ToString());
                GUILayout.Space(6f);
                Theme.StatTile(ConvaiActionsEditorStrings.InspectorTileSets, setCount.ToString());
                GUILayout.Space(6f);

                // The Issues tile leads exactly where the header chip does — it is the same number,
                // and two doors onto one report must not open onto different rooms. At zero it is an
                // inert label: a click that opens a report saying nothing is wrong is a worse answer
                // than the number the user can already read.
                if (Controls.StatTile(
                        ConvaiActionsEditorStrings.InspectorTileIssues, issues.ToString(), issueTint, issues > 0))
                    OpenTroubleshooter(ConvaiActionsModuleId);
            }
        }

        private void DrawCompactActionList(ConvaiActionConfigSource source)
        {
            List<ConvaiActionGroup> groups = ConvaiActionsEditorModel.BuildGroups(source, _diagnostics, null);

            int rowCount = 0;
            for (int g = 0; g < groups.Count; g++)
                rowCount += groups[g].Rows.Count;

            if (rowCount == 0)
            {
                Theme.BeginPanel(null);
                GUILayout.Label(ConvaiActionsEditorStrings.InspectorNoActionsBody, Theme.MutedWrapped);
                Theme.EndPanel(0f);
                return;
            }

            // The same categories the Actions Editor shows, in the same order, folded the same way —
            // but only once the user has actually filed something. A character with no categories
            // reads as one plain list, exactly as it did before categories existed.
            bool grouped = ConvaiActionsGrouping.HasAnyCategory(source);
            if (grouped)
                groups = ConvaiActionsGrouping.Regroup(groups, ConvaiActionsGroupAxis.Category);

            // Every action is listed, not the first few with an "…and N more" note under them: the
            // list is bounded to MaxVisibleRows and scrolls past that, so a character with twenty
            // actions is fully readable here without the card swallowing the inspector.
            _actionListScroll = Theme.BeginScrollRegion(
                _actionListScroll, MeasureListHeight(groups, grouped), MaxVisibleRows * RowHeight);

            for (int g = 0; g < groups.Count; g++)
            {
                ConvaiActionGroup group = groups[g];
                if (grouped)
                    DrawGroupHeader(source, group);

                if (grouped && ConvaiActionsGroupCollapseState.IsCollapsed(group.Key))
                    continue;

                for (int r = 0; r < group.Rows.Count; r++)
                    DrawPreviewRow(source, group.Rows[r]);
            }

            Theme.EndScrollRegion();
        }

        /// <summary>
        ///     How tall the list wants to be, so the bounded region knows whether it has to scroll at
        ///     all: every row it will draw, plus a header per group it will draw one for.
        /// </summary>
        private static float MeasureListHeight(List<ConvaiActionGroup> groups, bool grouped)
        {
            float height = 0f;
            for (int g = 0; g < groups.Count; g++)
            {
                if (grouped)
                    height += HeaderHeight;

                if (grouped && ConvaiActionsGroupCollapseState.IsCollapsed(groups[g].Key))
                    continue;

                height += groups[g].Rows.Count * RowHeight;
            }

            return height;
        }

        /// <summary>
        ///     A read-only category header: the category's own colour, its name, how many actions it
        ///     holds and how many of them still need work. Clicking it folds the group away — the one
        ///     interaction this inspector owns, because it changes what is shown and nothing else. The
        ///     fold is shared with the Actions Editor, so the two never disagree about this character.
        /// </summary>
        private static void DrawGroupHeader(ConvaiActionConfigSource source, ConvaiActionGroup group)
        {
            Rect slot = GUILayoutUtility.GetRect(0f, HeaderHeight, GUILayout.ExpandWidth(true));
            var row = new Rect(slot.x + 2f, slot.y + 4f, slot.width - 4f, HeaderHeight - 6f);

            bool collapsed = ConvaiActionsGroupCollapseState.IsCollapsed(group.Key);

            GUI.Label(
                new Rect(row.x, row.y, 14f, row.height),
                collapsed ? Glyphs.Affordance.DisclosureClosed : Glyphs.Affordance.DisclosureOpen,
                Theme.MicroLabel);

            // No name-derived category colour here either — see the Actions Editor's group header.
            float textX = row.x + 14f;

            GUIContent countPill = ConvaiActionsEditorStrings.BuildCountPill(group.Rows.Count);
            float countWidth = Theme.PillWidth(countPill);
            GUIContent attentionPill = group.UnhealthyCount > 0
                ? ConvaiActionsEditorStrings.BuildGroupAttentionPill(group.UnhealthyCount)
                : null;
            float attentionWidth = attentionPill == null ? 0f : Theme.PillWidth(attentionPill) + 6f;

            var titleRect = new Rect(
                textX, row.y, Mathf.Max(24f, row.xMax - textX - countWidth - attentionWidth - 6f), row.height);
            GUI.Label(titleRect, group.Title, Theme.ListGroupHeader);

            Theme.Pill(
                new Rect(row.xMax - countWidth, row.y + 2f, countWidth, 16f), countPill, Theme.TextMuted);

            if (attentionPill != null)
            {
                Color tint = group.WorstStatus == ConvaiActionRowStatus.Broken ? Theme.StatusError : Theme.StatusWarn;
                Theme.Pill(
                    new Rect(row.xMax - countWidth - attentionWidth, row.y + 2f, attentionWidth - 6f, 16f),
                    attentionPill, tint, true);
            }

            EditorGUIUtility.AddCursorRect(row, MouseCursor.Link);
            if (GUI.Button(row, GUIContent.none, Theme.InvisibleButton))
                ConvaiActionsGroupCollapseState.SetCollapsed(group.Key, !collapsed);
        }

        private static void DrawPreviewRow(ConvaiActionConfigSource source, ConvaiActionRow row)
        {
            Rect slot = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
            var card = new Rect(slot.x, slot.y + 1f, slot.width, RowHeight - 2f);
            bool hover = card.Contains(Event.current.mousePosition);

            Theme.FillRounded(card, hover ? Theme.CardBgHover : Theme.CardBg, 5f);
            Theme.StrokeRounded(card,
                hover ? Theme.Fade(Theme.Accent, 0.7f) : Theme.CardBorder, 5f);

            Color statusColor = row.Status switch
            {
                ConvaiActionRowStatus.Ready => Theme.StatusReady,
                ConvaiActionRowStatus.NeedsAttention => Theme.StatusWarn,
                _ => Theme.StatusError
            };
            Theme.StatusDot(new Vector2(card.x + 13f, card.y + (card.height * 0.5f)), statusColor, hover);

            var nameRect = new Rect(card.x + 25f, card.y, card.width - 30f, card.height);
            EditorGUIUtility.AddCursorRect(card, MouseCursor.Link);
            if (GUI.Button(nameRect, ConvaiActionsEditorStrings.BuildInspectorRowLabel(row.DisplayName), Theme.CardName))
                ConvaiActionsEditorWindow.ShowWindowFor(source, row.Definition);
        }

        private void DrawButtons(ConvaiActionConfigSource source)
        {
            Rect openRect = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
            if (Theme.PrimaryButton(openRect, ConvaiActionsEditorStrings.InspectorOpenWindowButton))
                ConvaiActionsEditorWindow.ShowWindowFor(source);

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect troubleshooterRect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
                if (Theme.GhostButton(troubleshooterRect, ConvaiActionsEditorStrings.InspectorTroubleshooterButton))
                    OpenTroubleshooter(ConvaiActionsModuleId);

                GUILayout.Space(6f);
                Rect validateRect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
                if (Theme.GhostButton(validateRect, ConvaiActionsEditorStrings.InspectorValidateButton))
                    RefreshDiagnostics();
            }
        }
    }
}
