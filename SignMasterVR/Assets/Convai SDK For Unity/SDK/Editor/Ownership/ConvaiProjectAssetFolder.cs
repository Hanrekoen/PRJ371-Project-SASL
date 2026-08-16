using System.Text;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Ownership
{
    /// <summary>
    ///     Where a settings asset the SDK creates for a character belongs, and what it is called.
    /// </summary>
    /// <remarks>
    ///     One rule, because a user has to be able to find these files later without being told:
    ///     <b>beside the character's own prefab when it has one, otherwise
    ///     <c>Assets/Convai/&lt;Module&gt;/</c></b>. A settings asset sitting next to the character it
    ///     belongs to explains itself; one in a Convai folder named after its module is the next best
    ///     thing.
    ///     <para>
    ///         Three modules each had their own answer before this: Body Animation copied beside the
    ///         source asset (so a copy of a package asset landed in a Convai folder while a copy of a
    ///         project asset landed wherever the original happened to be), and Emotion dropped new
    ///         profiles in the <c>Assets/</c> root — which, in a real project with a hundred folders,
    ///         is the one place a file is guaranteed to be lost.
    ///     </para>
    /// </remarks>
    internal static class ConvaiProjectAssetFolder
    {
        /// <summary>The project folder every Convai-authored asset lives under.</summary>
        private const string ConvaiRoot = "Assets/Convai";

        /// <summary>
        ///     The folder a new settings asset for <paramref name="owner" /> belongs in, created if
        ///     it does not exist yet.
        /// </summary>
        /// <param name="owner">The character the asset is being created for.</param>
        /// <param name="moduleFolderName">
        ///     The module's folder name — <c>BodyAnimation</c>, <c>Emotion</c>, <c>Gaze</c>. Used
        ///     only for the fallback location.
        /// </param>
        internal static string For(Component owner, string moduleFolderName)
        {
            string beside = BesidePrefabOf(owner);
            if (!string.IsNullOrEmpty(beside)) return beside;

            return EnsureFolder(ConvaiRoot, moduleFolderName);
        }

        /// <summary>
        ///     The folder holding the character's prefab, when it has one that lives in this project.
        ///     Empty for a plain scene object, or for a prefab that somehow lives in a package.
        /// </summary>
        private static string BesidePrefabOf(Component owner)
        {
            if (owner == null) return string.Empty;

            Object source = PrefabUtility.GetCorrespondingObjectFromSource(owner.gameObject);
            if (source == null) return string.Empty;

            string prefabPath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(prefabPath) || !ConvaiAssetOwnership.IsProjectAsset(source))
                return string.Empty;

            string directory = System.IO.Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            return string.IsNullOrEmpty(directory) ? string.Empty : directory;
        }

        /// <summary>Creates <c>parent/child</c> if needed and returns it.</summary>
        private static string EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent))
            {
                string parentOfParent = System.IO.Path.GetDirectoryName(parent)?.Replace('\\', '/');
                string leaf = System.IO.Path.GetFileName(parent);
                if (!string.IsNullOrEmpty(parentOfParent) && !string.IsNullOrEmpty(leaf))
                    AssetDatabase.CreateFolder(parentOfParent, leaf);
            }

            string full = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(full)) AssetDatabase.CreateFolder(parent, child);

            // A folder Unity refused to create would send the asset to a path that does not exist and
            // the copy would fail with a message about the wrong thing entirely.
            return AssetDatabase.IsValidFolder(full) ? full : parent;
        }

        /// <summary>
        ///     Where a copy belongs when there is no character to make it for — a settings asset
        ///     duplicated from its own inspector.
        /// </summary>
        /// <remarks>
        ///     Grouped by asset type rather than by module, because that is all this path knows: the
        ///     user opened a file, not a character. Still under <c>Assets/Convai/</c>, so a project
        ///     never ends up with Convai assets scattered across its root.
        /// </remarks>
        internal static string ForProjectRoot(string assetTypeName) =>
            EnsureFolder(ConvaiRoot, string.IsNullOrEmpty(assetTypeName) ? "Settings" : assetTypeName);

        /// <summary>
        ///     A character's name reduced to something safe in a file name, so an asset created for
        ///     "Nova (Front Desk)" is still findable by searching for Nova.
        /// </summary>
        internal static string SanitizeName(Object owner)
        {
            string name = owner != null ? owner.name : "Character";
            if (string.IsNullOrEmpty(name)) return "Character";

            var builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return builder.ToString();
        }
    }
}
