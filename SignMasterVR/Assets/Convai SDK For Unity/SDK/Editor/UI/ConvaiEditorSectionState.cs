using UnityEditor;

namespace Convai.Editor.UI
{
    /// <summary>
    ///     The single store for collapsible-section expansion state across every Convai editor
    ///     surface.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two lifetimes, deliberately distinct:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <b>Persistent</b> (<see cref="Get" />/<see cref="Set" />, <see cref="EditorPrefs" />)
    ///             — how a user prefers a component laid out. Survives editor restarts, because
    ///             re-collapsing the same three sections on every launch is friction.
    ///         </item>
    ///         <item>
    ///             <b>Session</b> (<see cref="GetSession" />/<see cref="SetSession" />,
    ///             <see cref="SessionState" />) — transient disclosure such as an "Advanced" foldout,
    ///             which should return to its safe default in a fresh editor session.
    ///         </item>
    ///     </list>
    ///     <para>
    ///         Keys are namespaced by host (usually the editor type name) and section id, so two
    ///         editors that happen to name a section "Advanced" never share state.
    ///     </para>
    /// </remarks>
    internal static class ConvaiEditorSectionState
    {
        private const string Prefix = "Convai.Editor";
        private const string Suffix = "Expanded";

        /// <summary>Reads persisted expansion state for a section.</summary>
        internal static bool Get(string hostId, string sectionId, bool defaultValue) =>
            EditorPrefs.GetBool(BuildKey(hostId, sectionId), defaultValue);

        /// <summary>Persists expansion state for a section.</summary>
        internal static void Set(string hostId, string sectionId, bool value) =>
            EditorPrefs.SetBool(BuildKey(hostId, sectionId), value);

        /// <summary>Reads session-scoped expansion state for a section.</summary>
        internal static bool GetSession(string hostId, string sectionId, bool defaultValue) =>
            SessionState.GetBool(BuildKey(hostId, sectionId), defaultValue);

        /// <summary>Stores session-scoped expansion state for a section.</summary>
        internal static void SetSession(string hostId, string sectionId, bool value) =>
            SessionState.SetBool(BuildKey(hostId, sectionId), value);

        /// <summary>Builds the storage key for a section. Internal so tests can assert the scheme.</summary>
        internal static string BuildKey(string hostId, string sectionId) =>
            $"{Prefix}.{Normalize(hostId)}.{Normalize(sectionId)}.{Suffix}";

        private static string Normalize(string raw) =>
            string.IsNullOrWhiteSpace(raw) ? "Unknown" : raw.Trim().Replace(" ", string.Empty);
    }
}
