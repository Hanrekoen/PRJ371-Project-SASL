using Convai.Editor.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Editor.Ownership
{
    /// <summary>
    ///     Tells the user what copy-on-write just created for them, once, where they were looking.
    /// </summary>
    /// <remarks>
    ///     Copy-on-write is silent by design — nothing is asked before the user is allowed to change
    ///     something. Silent is not the same as secret: an asset appeared in their project and they
    ///     are entitled to know its name and where it went. So the receipt is drawn <b>after</b> the
    ///     controls that caused it, not as a banner above them, and it is cleared as soon as another
    ///     character is inspected. It reports something that just happened; it is not a standing
    ///     notice, and a box that never goes away teaches people to stop reading boxes.
    ///     <para>
    ///         One receipt at a time, held here rather than in each module's static fields — three
    ///         modules keeping their own copy of "what did I last create, and for whom" is how the
    ///         same idea ends up worded three ways.
    ///     </para>
    /// </remarks>
    internal static class ConvaiCopyReceipts
    {
        private static Object s_owner;
        private static string s_assetPath;
        private static Object s_asset;

        /// <summary>Records a copy so the surface that caused it can report it on the next repaint.</summary>
        internal static void Record(Object owner, string assetPath, Object asset)
        {
            s_owner = owner;
            s_assetPath = assetPath;
            s_asset = asset;
        }

        /// <summary>
        ///     Draws the receipt when the last copy was made for <paramref name="owner" />, and
        ///     forgets it when the user has moved on to something else.
        /// </summary>
        internal static void Draw(Object owner)
        {
            if (owner == null || owner != s_owner)
            {
                if (owner != s_owner) Clear();
                return;
            }

            if (string.IsNullOrEmpty(s_assetPath)) return;

            EditorGUILayout.Space(4f);
            ConvaiEditorFrame.InfoBox(
                "These settings are now this character's own",
                $"Created {System.IO.Path.GetFileNameWithoutExtension(s_assetPath)} in " +
                $"{System.IO.Path.GetDirectoryName(s_assetPath)?.Replace('\\', '/')}, so this change " +
                "belongs to this character and nothing else was affected.",
                s_asset != null ? "Show Me" : null,
                s_asset != null ? () => EditorGUIUtility.PingObject(s_asset) : null);
        }

        private static void Clear()
        {
            s_owner = null;
            s_assetPath = null;
            s_asset = null;
        }
    }
}
