using System;
using System.Collections.Generic;
using System.Reflection;
using Convai.Editor.Diagnostics;
using Convai.Editor.UI;
using Convai.Editor.Utilities;
using Convai.Runtime.Actions;
using UnityEditor;
using UnityEngine;
using Frame = Convai.Editor.UI.ConvaiEditorFrame;
using Controls = Convai.Editor.UI.ConvaiEditorControls;
using Styles = Convai.Editor.UI.ConvaiEditorStyles;
using Tokens = Convai.Editor.UI.ConvaiEditorTokens;

namespace Convai.Editor.Inspectors.Framework
{
    /// <summary>
    ///     The single base class for every Convai component and asset inspector. It owns the
    ///     Convai editor frame — icon, accent title, muted subtitle, status pill, purpose strip, section
    ///     cards and the Play-mode live block — so a concrete editor only declares what is specific
    ///     to it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Template Method.</b> <see cref="OnInspectorGUI" /> is sealed and owns the
    ///         <c>EnsureStyles → Update → header → body → live → ApplyModifiedProperties</c> sequence.
    ///         This is deliberate: the two bases this class replaced had that sequence copy-pasted
    ///         into their subclasses, which is how a header ended up drawn twice on one editor and
    ///         omitted on another. Subclasses fill hooks; they never re-run the orchestration.
    ///     </para>
    ///     <para>
    ///         <b>Two body strategies.</b> <see cref="DrawBody" /> defaults to an attribute-driven
    ///         renderer that groups the target's serialized fields by
    ///         <see cref="ConvaiInspectorSectionAttribute" /> — the right choice for components whose
    ///         fields speak for themselves. An editor that needs bespoke copy per field, or draws
    ///         purely computed content, overrides <see cref="DrawBody" /> and takes the body over
    ///         completely while keeping the frame.
    ///     </para>
    ///     <para>
    ///         <b>No per-repaint model rebuilding.</b> The section model,
    ///         <see cref="SerializedProperty" /> handles, labels and state keys are built once per
    ///         enable in <see cref="RebuildInspectorModel" /> and never per repaint — an inspector
    ///         repaints continuously in Play mode. Expansion is cached in memory too, so a repaint
    ///         never reads <see cref="EditorPrefs" /> (a registry hit on Windows).
    ///     </para>
    ///     <para>
    ///         What this does <em>not</em> claim is a zero-allocation repaint. Two things allocate by
    ///         necessity: the <see cref="Action" /> body closures the section idiom takes, and the
    ///         formatting of live telemetry text (<see cref="FormatVector3" /> and friends), because
    ///         producing a string for IMGUI to draw allocates one. Both are confined to editor drawing
    ///         and neither grows with time; the runtime per-frame budget this package holds itself to is
    ///         a separate, stricter rule that applies to <c>SDK/Runtime</c> and <c>SDK/Modules</c>.
    ///     </para>
    ///     <para>
    ///         <b>Serialized pipeline preserved.</b> Every field still routes through
    ///         <see cref="SerializedObject" />, so Undo, prefab overrides and revert behave exactly as
    ///         they do in the default inspector.
    ///     </para>
    /// </remarks>
    internal abstract class ConvaiInspectorEditor : UnityEditor.Editor
    {
        #region Cached model

        private sealed class FieldRow
        {
            internal SerializedProperty Property;
            internal GUIContent Label;
        }

        private sealed class SectionRow
        {
            internal GUIContent Title;
            internal bool Advanced;
            internal string SectionId;
            internal readonly List<FieldRow> Fields = new();
        }

        private const string SectionGlyph = ConvaiEditorGlyphs.Section;

        private List<SectionRow> _sections;
        private GUIContent _titleContent;
        private GUIContent _subtitleContent;
        private GUIContent _purposeContent;

        /// <summary>
        ///     Cached section expansion, so a repaint never touches <see cref="EditorPrefs" />.
        ///     Reading a preference per section per repaint is a registry hit on Windows.
        /// </summary>
        private readonly Dictionary<string, bool> _expansion = new(StringComparer.Ordinal);

        #endregion

        #region Declarative surface

        /// <summary>Header title. Defaults to the nicified component type name.</summary>
        protected virtual string Title =>
            target != null ? ObjectNames.NicifyVariableName(target.GetType().Name) : string.Empty;

        /// <summary>Muted line under the title (for example "Action Behavior"). Null hides it.</summary>
        protected virtual string Subtitle => null;

        /// <summary>One-line plain-language purpose strip under the header. Null hides it.</summary>
        protected virtual string Purpose => null;

        /// <summary>Right-aligned header status pill. Null hides the pill.</summary>
        protected virtual GUIContent StatusChip => null;

        /// <summary>Tint for <see cref="StatusChip" />.</summary>
        protected virtual Color StatusChipTint => Tokens.StatusReady;

        /// <summary>
        ///     The Convai capability this inspector belongs to — <c>convai.actions</c>,
        ///     <c>convai.gaze</c>. Set it and the status chip becomes a way into the Troubleshooter,
        ///     opened on this character and scrolled to this capability.
        /// </summary>
        /// <remarks>
        ///     Null means the chip stays an inert label, which is the right answer for a component
        ///     whose capability has no checks behind it yet: a chip that opens a report with nothing
        ///     to say about it would be a worse lie than the dead pixel it replaced.
        /// </remarks>
        protected virtual string TroubleshooterModuleId => null;

        /// <summary>
        ///     Whether the chip is reporting something worth opening. Each editor answers for itself;
        ///     the default is <c>false</c>, so a chip is inert until someone decides it should not be.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A <c>Ready</c> chip is deliberately not clickable. Opening a whole window to be told
        ///         nothing is wrong is a worse answer than the word <c>Ready</c> itself — the route to
        ///         "what was actually checked" is the Troubleshooter's own menu item, which is always
        ///         there. So a live chip always means there is something to see.
        ///     </para>
        ///     <para>
        ///         <b>Why this is not inferred.</b> The first version of this hook worked out the answer
        ///         by comparing <see cref="StatusChip" /> by reference against the healthy entries of
        ///         <see cref="ConvaiEditorChips" />. That is magic that happens to hold: an editor that
        ///         builds its own "Ready" content — several do, with their own wording — would have been
        ///         silently classified as reporting a problem, and its chip would have opened a report
        ///         about a healthy character. An editor knows whether it is reporting work; asking it is
        ///         both shorter and correct.
        ///     </para>
        /// </remarks>
        protected virtual bool StatusChipIsActionable => false;

        /// <summary>
        ///     What the status chip does when clicked. Overriding this is rarely needed — the default
        ///     opens the Troubleshooter on this character's <see cref="TroubleshooterModuleId" />.
        /// </summary>
        protected virtual void OnStatusChipClicked() => OpenTroubleshooter(TroubleshooterModuleId);

        /// <summary>
        ///     Opens the Troubleshooter on the Convai Character this inspector's target belongs to,
        ///     expanded at <paramref name="moduleId" /> and flashing <paramref name="findingId" />.
        /// </summary>
        protected void OpenTroubleshooter(string moduleId = null, string findingId = null)
        {
            var component = target as Component;
            ConvaiTroubleshooterWindow.ShowFor(
                component != null ? component.gameObject : null, moduleId, findingId);
        }

        /// <summary>
        ///     The chip's click handler, bound once per enable. Built here rather than in the draw
        ///     path because a delegate created per repaint is a per-repaint allocation, and this
        ///     inspector repaints continuously in Play mode.
        /// </summary>
        private Action _chipAction;

        /// <summary>
        ///     Documentation URL for this component. The header shows a small "?" button that opens
        ///     it — the shortest path from "what is this?" to the docs. Null hides the button.
        /// </summary>
        /// <remarks>
        ///     Defaults to the Unity SDK documentation hub, so every Convai inspector has a working
        ///     way out to the docs without each editor having to remember to wire one. Override with a
        ///     more specific page where one exists; see <see cref="ConvaiEditorLinks.DocsUnitySdkUrl" />
        ///     for why per-module deep links are not hardcoded yet.
        /// </remarks>
        protected virtual string HelpUrl => ConvaiEditorLinks.DocsUnitySdkUrl;

        /// <summary>Namespace for this editor's persisted section state.</summary>
        protected virtual string EditorStateHostId => GetType().Name;

        #endregion

        #region Lifecycle hooks

        /// <summary>Builds the cached inspector model. Overrides must call the base implementation.</summary>
        protected virtual void OnEnable()
        {
            _chipAction = OnStatusChipClicked;
            RebuildInspectorModel();
        }

        /// <summary>Flushes cached section state. Overrides must call the base implementation.</summary>
        protected virtual void OnDisable() => FlushExpansion();

        /// <summary>
        ///     Called once per pass after <see cref="SerializedObject.Update" /> and before any
        ///     drawing — the place to refresh dirty cached state. Never scan scenes per repaint;
        ///     flag-and-refresh instead.
        /// </summary>
        protected virtual void OnBeforeInspectorGUI()
        {
        }

        /// <summary>Extra Convai-styled blocks drawn between the header/purpose strip and the body.</summary>
        protected virtual void DrawHeaderExtras()
        {
        }

        /// <summary>
        ///     The inspector body. Defaults to the attribute-driven section renderer; override to
        ///     draw a bespoke body while keeping the Convai editor frame.
        /// </summary>
        protected virtual void DrawBody() => DrawGeneratedSections();

        /// <summary>Live diagnostics block. Only called while the editor is in Play mode.</summary>
        protected virtual void DrawLiveSection()
        {
        }

        #endregion

        /// <summary>
        ///     Orchestrates one inspector pass. Sealed by design — see the Template Method note in the
        ///     class remarks. An editor that genuinely needs full control should derive from
        ///     <see cref="UnityEditor.Editor" /> instead of taking this frame and then discarding it.
        /// </summary>
        public sealed override void OnInspectorGUI()
        {
            Styles.EnsureStyles();
            if (_sections == null)
                RebuildInspectorModel();
            if (_sections == null)
                return;

            // Re-arm the container guard for this pass, remembering whatever an enclosing draw pass
            // had open so this inspector cannot swallow its containers.
            int enclosingContainers = Frame.EnterDrawScope();
            try
            {
                serializedObject.Update();
                OnBeforeInspectorGUI();

                Frame.InspectorHeader(
                    _titleContent, _subtitleContent, StatusChip, StatusChipTint, HelpUrl,
                    StatusChipIsActionable ? _chipAction : null);
                if (_purposeContent != null)
                {
                    GUILayout.Label(_purposeContent, Styles.MutedWrapped);
                    GUILayout.Space(6f);
                }

                DrawHeaderExtras();
                DrawBody();

                if (EditorApplication.isPlaying)
                    DrawLiveSection();

                CloseSectionCard();
                serializedObject.ApplyModifiedProperties();
            }
            finally
            {
                Frame.ExitDrawScope(enclosingContainers);
            }
        }

        #region Model

        /// <summary>
        ///     Rebuilds the cached header content, section model and property handles from the current
        ///     target. Call after changing what <see cref="Title" /> or <see cref="Purpose" /> return.
        /// </summary>
        protected void RebuildInspectorModel()
        {
            _sections = null;
            if (target == null)
                return;

            _titleContent = new GUIContent(Title);
            _subtitleContent = string.IsNullOrEmpty(Subtitle) ? null : new GUIContent(Subtitle);
            _purposeContent = string.IsNullOrEmpty(Purpose) ? null : new GUIContent(Purpose);

            Type targetType = target.GetType();
            var metadata = new List<ConvaiInspectorFieldMetadata>();
            var labels = new Dictionary<string, GUIContent>(StringComparer.Ordinal);

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                string fieldName = iterator.propertyPath;
                if (fieldName == "m_Script")
                    continue;

                FieldAttributes attributes = GetFieldAttributes(targetType, fieldName);

                string label = iterator.displayName;
                labels[fieldName] = new GUIContent(
                    label, string.IsNullOrEmpty(attributes.Tooltip) ? label : attributes.Tooltip);
                metadata.Add(new ConvaiInspectorFieldMetadata(
                    fieldName, attributes.Section, attributes.Order, attributes.Advanced));
            }

            List<ConvaiInspectorSectionModel> models = ConvaiInspectorSectionLayout.Build(metadata);
            var sections = new List<SectionRow>(models.Count);
            for (int s = 0; s < models.Count; s++)
            {
                ConvaiInspectorSectionModel model = models[s];
                var row = new SectionRow
                {
                    Title = new GUIContent(model.Name),
                    Advanced = model.Advanced,
                    SectionId = model.Name
                };

                for (int f = 0; f < model.FieldNames.Count; f++)
                {
                    string fieldName = model.FieldNames[f];
                    SerializedProperty property = serializedObject.FindProperty(fieldName);
                    if (property == null)
                        continue;

                    row.Fields.Add(new FieldRow { Property = property, Label = labels[fieldName] });
                }

                if (row.Fields.Count > 0)
                    sections.Add(row);
            }

            _sections = sections;
        }

        /// <summary>
        ///     What the section renderer needs to know about one serialized field, after the
        ///     reflection that answers it has been paid for once.
        /// </summary>
        private readonly struct FieldAttributes
        {
            internal FieldAttributes(string section, int order, bool advanced, string tooltip)
            {
                Section = section;
                Order = order;
                Advanced = advanced;
                Tooltip = tooltip;
            }

            internal string Section { get; }
            internal int Order { get; }
            internal bool Advanced { get; }
            internal string Tooltip { get; }
        }

        /// <summary>
        ///     Field attributes by component type, resolved once per type per domain.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Why this is cached and the rest of the model is not.</b> The section model,
        ///         property handles and labels are rebuilt per enable because they belong to one
        ///         <see cref="SerializedObject" />. The attributes do not — they are a property of the
        ///         C# type, identical for every instance of it and for the whole life of the loaded
        ///         assembly.
        ///     </para>
        ///     <para>
        ///         <b>Why it matters.</b> Resolving them means a <see cref="FieldInfo" /> lookup up the
        ///         base-type chain plus two <see cref="Attribute" /> materialisations, which .NET does
        ///         not cache, for every visible field. A character carrying eight Convai components at
        ///         thirty to forty fields each paid that fifteen hundred to twenty-five hundred times
        ///         on <em>every</em> selection click — the one cost in this framework that grew with
        ///         the number of Convai components on the object, which is exactly the shape a real
        ///         character has.
        ///     </para>
        ///     <para>
        ///         <b>Why it cannot go stale.</b> Attributes only change when the script changes, and a
        ///         script change reloads the domain, which discards this dictionary with the rest of
        ///         the editor's statics. There is nothing to invalidate by hand.
        ///     </para>
        /// </remarks>
        private static readonly Dictionary<Type, Dictionary<string, FieldAttributes>> AttributesByType = new();

        /// <summary>Number of component types resolved so far. Exposed so a test can assert reuse.</summary>
        internal static int AttributeCacheTypeCount => AttributesByType.Count;

        private static FieldAttributes GetFieldAttributes(Type targetType, string fieldName)
        {
            if (!AttributesByType.TryGetValue(targetType, out Dictionary<string, FieldAttributes> byField))
            {
                byField = new Dictionary<string, FieldAttributes>(StringComparer.Ordinal);
                AttributesByType[targetType] = byField;
            }

            if (byField.TryGetValue(fieldName, out FieldAttributes cached))
                return cached;

            FieldInfo field = FindField(targetType, fieldName);
            var section = field?.GetCustomAttribute<ConvaiInspectorSectionAttribute>();
            var tooltip = field?.GetCustomAttribute<TooltipAttribute>();

            var resolved = new FieldAttributes(
                section?.Section, section?.Order ?? 0, section?.Advanced ?? false, tooltip?.tooltip);
            byField[fieldName] = resolved;
            return resolved;
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }

            return null;
        }

        #endregion

        #region Default body renderer

        /// <summary>Renders the attribute-driven sections built by <see cref="RebuildInspectorModel" />.</summary>
        protected void DrawGeneratedSections()
        {
            for (int s = 0; s < _sections.Count; s++)
                DrawGeneratedSection(_sections[s]);
        }

        private void DrawGeneratedSection(SectionRow section)
        {
            Frame.BeginCard();

            // Every generated section collapses. An all-advanced section starts collapsed and is
            // session-scoped, so beginners meet only the essentials and it returns to the safe
            // default next launch; a regular section starts open and its fold persists as a
            // layout preference.
            if (section.Advanced)
            {
                bool expanded = GetSessionExpansion(section.SectionId, false);
                bool now = Frame.CollapsibleSectionHeader(SectionGlyph, section.Title, expanded);
                if (now != expanded)
                    SetSessionExpansion(section.SectionId, now);
                if (now)
                    DrawFields(section);
            }
            else
            {
                bool expanded = GetExpansion(section.SectionId, true);
                bool now = Frame.CollapsibleSectionHeader(SectionGlyph, section.Title, expanded);
                if (now != expanded)
                    SetExpansion(section.SectionId, now);
                if (now)
                    DrawFields(section);
            }

            Frame.EndCard(8f);
        }

        private static void DrawFields(SectionRow section)
        {
            for (int f = 0; f < section.Fields.Count; f++)
            {
                FieldRow field = section.Fields[f];
                EditorGUILayout.PropertyField(field.Property, field.Label, true);
            }
        }

        #endregion

        #region Section state

        /// <summary>
        ///     Whether <see cref="DrawSection(string,string,string,bool,Color?,string)" /> left a
        ///     section card open for the body that follows. Closed by <see cref="DrawSectionBody" />,
        ///     by the next section, or by the end of the pass — so a caller that draws its body
        ///     directly still balances.
        /// </summary>
        private bool _sectionCardOpen;

        /// <summary>
        ///     <b>The</b> collapsible-section idiom for Convai editors: draws the section card and its
        ///     colour-coded header, and returns whether the body should be drawn. Follow an expanded
        ///     section with <see cref="DrawSectionBody" />, which draws the recessed, indented body
        ///     panel and closes the card.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Expansion is owned here — persisted per <see cref="EditorStateHostId" /> and
        ///         <paramref name="sectionId" />, cached so a repaint never touches
        ///         <see cref="EditorPrefs" />. An editor holding its own <c>_showX</c> bool per section
        ///         has to re-implement that persistence, or forgets to and its folds reset on every
        ///         selection change; there is nothing left here for a call site to get wrong.
        ///     </para>
        ///     <para>
        ///         This is the only section entry point, by design. Offer a card-less variant, a
        ///         caller-owned-expansion overload and the raw frame primitive alongside it and editors
        ///         pick different ones, after which their sections stop looking alike — exactly the
        ///         divergence a design system exists to prevent.
        ///     </para>
        /// </remarks>
        /// <param name="sectionId">Stable id for persisted expansion. Never localise it.</param>
        /// <param name="title">Section title, in Title Case.</param>
        /// <param name="glyph">A <see cref="ConvaiEditorGlyphs" /> constant, chosen by meaning.</param>
        /// <param name="defaultExpanded">
        ///     Whether the section starts open the first time a user meets it. Advanced or diagnostic
        ///     sections pass <c>false</c> so beginners meet only the essentials.
        /// </param>
        /// <param name="accent">
        ///     Colours the glyph, title and underline together. Defaults to the brand accent
        ///     (configuration); pass <see cref="ConvaiEditorTheme.StatusInfo" /> for Play-mode
        ///     telemetry and <see cref="ConvaiEditorTheme.StatusWarn" /> for validation.
        /// </param>
        /// <param name="summary">
        ///     Optional right-aligned state summary so a collapsed section still reports what it
        ///     holds.
        /// </param>
        protected bool DrawSection(
            string sectionId, string title, string glyph,
            bool defaultExpanded = true, Color? accent = null, string summary = null)
        {
            CloseSectionCard();
            Frame.BeginCard();

            bool expanded = GetExpansion(sectionId, defaultExpanded);
            var spec = new ConvaiEditorSectionSpec(
                EditorStateHostId,
                sectionId,
                title,
                glyph,
                accent ?? Tokens.Accent,
                Tokens.SectionIconFontSize,
                summary);

            bool now = ConvaiEditorSections.DrawHeader(in spec, expanded);
            if (now != expanded)
                SetExpansion(sectionId, now);

            if (now)
                _sectionCardOpen = true;
            else
                Frame.EndCard(8f);

            return now;
        }

        /// <summary>
        ///     <see cref="GUIContent" />-titled form, for editors that already cache their section
        ///     titles as content (which is what keeps a repaint allocation-free).
        /// </summary>
        protected bool DrawSection(
            string sectionId, GUIContent title, string glyph,
            bool defaultExpanded = true, Color? accent = null, string summary = null) =>
            DrawSection(sectionId, title.text, glyph, defaultExpanded, accent, summary);

        /// <summary>
        ///     Draws a whole section — card, header and body — in one call. <b>Prefer this form.</b>
        /// </summary>
        /// <remarks>
        ///     The two-step form asks the caller to remember that an expanded
        ///     <see cref="DrawSection(string,string,string,bool,Color?,string)" /> must be followed by
        ///     <see cref="DrawSectionBody" />, and quietly does something slightly wrong if it is not:
        ///     the body renders without its recessed panel and indent. Passing the body in makes that
        ///     mistake unrepresentable. Reach for the two-step form only when the body genuinely cannot
        ///     be expressed as one closure — for instance when it has to return out of the enclosing
        ///     method partway through.
        /// </remarks>
        protected void DrawSection(
            string sectionId, string title, string glyph, Action body,
            bool defaultExpanded = true, Color? accent = null, string summary = null,
            Color? bodyBackground = null)
        {
            if (DrawSection(sectionId, title, glyph, defaultExpanded, accent, summary))
                DrawSectionBody(body, bodyBackground);
        }

        /// <inheritdoc cref="DrawSection(string,string,string,Action,bool,Color?,string,Color?)" />
        protected void DrawSection(
            string sectionId, GUIContent title, string glyph, Action body,
            bool defaultExpanded = true, Color? accent = null, string summary = null,
            Color? bodyBackground = null) =>
            DrawSection(sectionId, title.text, glyph, body, defaultExpanded, accent, summary, bodyBackground);

        /// <summary>Draws <paramref name="draw" /> inside a section body panel.</summary>
        protected void DrawSectionBody(Action draw, Color? background = null)
        {
            using (ConvaiEditorSections.Body(background))
                draw?.Invoke();
            CloseSectionCard();
        }

        /// <summary>
        ///     Ends the section card left open by an expanded <see cref="DrawSection" />. Called
        ///     automatically by <see cref="DrawSectionBody" />, the next <see cref="DrawSection" />
        ///     and the end of the pass; call it directly after drawing a section's content by hand
        ///     (without <see cref="DrawSectionBody" />) when unrelated content follows.
        /// </summary>
        protected void CloseSectionCard()
        {
            if (!_sectionCardOpen)
                return;

            Frame.EndCard(8f);
            _sectionCardOpen = false;
        }

        /// <summary>Reads persisted expansion, caching so repaints never touch <see cref="EditorPrefs" />.</summary>
        protected bool GetExpansion(string sectionId, bool defaultExpanded)
        {
            if (_expansion.TryGetValue(sectionId, out bool cached))
                return cached;

            bool stored = ConvaiEditorSectionState.Get(EditorStateHostId, sectionId, defaultExpanded);
            _expansion[sectionId] = stored;
            return stored;
        }

        /// <summary>Updates cached expansion. Written through on <see cref="OnDisable" />.</summary>
        protected void SetExpansion(string sectionId, bool value) => _expansion[sectionId] = value;

        private bool GetSessionExpansion(string sectionId, bool defaultExpanded) =>
            ConvaiEditorSectionState.GetSession(EditorStateHostId, sectionId, defaultExpanded);

        private void SetSessionExpansion(string sectionId, bool value) =>
            ConvaiEditorSectionState.SetSession(EditorStateHostId, sectionId, value);

        private void FlushExpansion()
        {
            foreach (KeyValuePair<string, bool> entry in _expansion)
                ConvaiEditorSectionState.Set(EditorStateHostId, entry.Key, entry.Value);
            _expansion.Clear();
        }

        #endregion

        #region Frame shortcuts

        /// <summary>Draws <paramref name="body" /> inside a rounded section card.</summary>
        protected static void Card(Action body, float bottomSpace = Tokens.CardSpacing)
        {
            using (Frame.Card(bottomSpace))
                body?.Invoke();
        }

        /// <summary>Draws <paramref name="body" /> inside a panel nested in a card.</summary>
        protected static void Panel(Action body, Color? statusColor = null, float bottomSpace = 6f)
        {
            using (Frame.Panel(statusColor, bottomSpace))
                body?.Invoke();
        }

        /// <summary>Neutral explanation box.</summary>
        protected static void InfoBox(string title, string message) => Frame.InfoBox(title, message);

        /// <summary>Actionable warning box with an optional one-click fix.</summary>
        protected static void WarningBox(string title, string message, string buttonText = null, Action fix = null) =>
            Frame.WarningBox(title, message, buttonText, fix);

        /// <summary>Blocking error box with an optional one-click fix.</summary>
        protected static void ErrorBox(string title, string message, string buttonText = null, Action fix = null) =>
            Frame.ErrorBox(title, message, buttonText, fix);

        /// <summary>Labelled live telemetry readout.</summary>
        protected static void LiveCell(string label, string value, Color color, float width = 104f, bool bold = false) =>
            Controls.LiveCell(label, value, color, width, bold);

        /// <summary>Centred note shown where live data appears once the scene is playing.</summary>
        protected static void OfflinePlaceholder(string message = "Enter Play Mode to view live telemetry.") =>
            Frame.OfflinePlaceholder(message);

        /// <summary>Centred stat tile — a number over a caption.</summary>
        protected static void StatTile(GUIContent label, string value, Color? numberTint = null) =>
            Controls.StatTile(label, value, numberTint);

        /// <summary>Full-width accent call-to-action button.</summary>
        protected static bool PrimaryButton(GUIContent content, float height = 28f) =>
            Controls.PrimaryButtonLayout(content, height);

        /// <summary>Full-width outlined secondary button.</summary>
        protected static bool GhostButton(GUIContent content, float height = 22f) =>
            Controls.GhostButtonLayout(content, height);

        #endregion

        #region Formatting helpers

        /// <summary>Compact three-component vector readout for live cells.</summary>
        protected static string FormatVector3(Vector3 value) => $"{value.x:0.00}, {value.y:0.00}, {value.z:0.00}";

        /// <summary>Name of an assigned asset, or "Default" when none is assigned.</summary>
        protected static string ObjectStatus(UnityEngine.Object value) => value != null ? value.name : "Default";

        #endregion
    }
}
