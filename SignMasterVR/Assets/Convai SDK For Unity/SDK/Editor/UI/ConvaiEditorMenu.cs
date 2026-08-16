namespace Convai.Editor.UI
{
    /// <summary>
    ///     The priority bands that give the <c>Convai</c> menu its groups, so its entries arrive in a
    ///     deliberate order with separators between kinds of thing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Unity orders menu entries by priority and draws a separator wherever consecutive
    ///         priorities differ by 11 or more. Left to individual files, that mechanism produced a
    ///         flat list: two windows shared priority 6, two more shared 7 — so the order between
    ///         those siblings was whatever the assembly happened to load first — and three entries
    ///         declared no priority at all, which parks them at the end in an order nobody chose.
    ///     </para>
    ///     <para>
    ///         Declaring the bands here instead means an entry says which <em>group</em> it belongs
    ///         to, and the spacing between groups is a property of this file rather than an
    ///         accident. Add a new entry as <c>Band + n</c> with an <c>n</c> no other entry in that
    ///         band uses.
    ///     </para>
    ///     <para>
    ///         The remaining bands start far enough apart for Unity to draw separators between
    ///         configuration, authoring, diagnostics, and developer tools.
    ///     </para>
    ///     <para>
    ///         <b>What earns a row.</b> A menu entry is a destination, not a table of contents. The
    ///         configuration band once carried seven entries that all opened the same window at a
    ///         different section of its own navigation rail, and the diagnostics band carried three
    ///         measurement instruments that mean nothing until a character or an asset has been
    ///         picked — which is a thing the menu cannot do and the surface that owns the subject
    ///         already has. Both are now reached from where their subject lives. Before adding an
    ///         entry here, check that it opens something that is not already open, that it does not
    ///         need a selection the user has not made yet, and that no other row leads to it.
    ///         <c>ConvaiEditorMenuGuardTests</c> holds the line on the mechanical half of that.
    ///     </para>
    /// </remarks>
    internal static class ConvaiEditorMenu
    {
        /// <summary>Sections hosted by the main Convai Editor window.</summary>
        internal const int Configuration = 1;

        /// <summary>The per-feature authoring windows — the ones a user opens to build something.</summary>
        internal const int FeatureEditors = 20;

        /// <summary>Tools that explain why something is not working.</summary>
        internal const int Diagnostics = 40;

        /// <summary>The <c>Convai/Developer</c> submenu — for people working on the SDK itself.</summary>
        internal const int Developer = 210;
    }
}
