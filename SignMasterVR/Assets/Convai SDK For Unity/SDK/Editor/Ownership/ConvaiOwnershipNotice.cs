using System;
using Convai.Editor.UI;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Convai.Editor.Ownership
{
    /// <summary>
    ///     The one way a Convai module tells the user who owns the settings they are looking at.
    /// </summary>
    /// <remarks>
    ///     Drawn from one place so five modules cannot describe the same situation five ways — which
    ///     they did: Body Animation warned and greyed its controls out, Emotion warned and left them
    ///     live, Gaze and Body Language and Conversation Flow said nothing while the write went
    ///     nowhere.
    ///     <para>
    ///         Tone is load-bearing here. A <b>shared</b> asset is a caution: the user is about to
    ///         change more than they can see, so it is drawn in the warning colour. An
    ///         <b>SDK-owned</b> asset is not a problem at all — it is simply not theirs yet — so it
    ///         is drawn as information with a way forward. Painting "this ships with the SDK" in
    ///         warning orange tells someone opening Unity for the first time that they have broken
    ///         something, and they have not.
    ///     </para>
    /// </remarks>
    internal static class ConvaiOwnershipNotice
    {
        /// <summary>Button text for giving a character its own copy of a shared project asset.</summary>
        internal const string MakeUniqueButton = "Make Unique For This Character";

        /// <summary>Button text for lifting an SDK asset into the user's project.</summary>
        internal const string CreateProjectCopyButton = "Create A Project Copy";

        /// <summary>
        ///     Says who owns this asset, and offers the one action that resolves it. Silent when the
        ///     asset is this character's alone — there is nothing a user needs to know then, and a
        ///     box that appears on every character teaches people to stop reading boxes.
        /// </summary>
        /// <param name="ownership">The verdict, normally from <c>OfCached</c> on a draw path.</param>
        /// <param name="onCopyRequested">
        ///     Performs the copy and rewires the character. <c>null</c> when there is no character to
        ///     act for — the explanation is still shown, without a button that could not work.
        /// </param>
        /// <remarks>
        ///     <b>Do not draw the SDK-owned notice on a character.</b> It reads "cannot be changed
        ///     here", which is true of an asset opened on its own and false on a character, where
        ///     copy-on-write makes the controls live and the first change makes the settings theirs.
        ///     A caller holding a character returns before calling this when the SDK owns the asset;
        ///     the shared-asset notice is the one that belongs there.
        /// </remarks>
        internal static void Draw(ConvaiAssetOwnership ownership, Action onCopyRequested)
        {
            if (!ownership.HasNotice) return;

            string button = onCopyRequested == null
                ? null
                : ownership.Kind == ConvaiAssetOwnershipKind.SdkOwned
                    ? CreateProjectCopyButton
                    : MakeUniqueButton;

            if (ownership.Kind == ConvaiAssetOwnershipKind.SdkOwned)
                ConvaiEditorFrame.InfoBox(ownership.NoticeTitle, ownership.NoticeMessage, button, onCopyRequested);
            else
                ConvaiEditorFrame.WarningBox(ownership.NoticeTitle, ownership.NoticeMessage, button, onCopyRequested);

            EditorGUILayout.Space(4f);
        }

        /// <summary>
        ///     Guards a settings asset's own inspector: explains an SDK-owned asset, offers a project
        ///     copy, and makes the controls below read-only for as long as the scope is open.
        /// </summary>
        /// <remarks>
        ///     This is the one place the read-only story survives, and the only place it should.
        ///     On a character, an SDK-owned asset is a non-event — copy-on-write makes the settings
        ///     the user's the moment they change anything. Here there is no character to copy
        ///     <i>for</i>: the user opened a file that belongs to the SDK. Disabling the controls is
        ///     then the honest answer rather than a shrug, and the way forward is a plain duplicate
        ///     they can then assign wherever they like.
        /// </remarks>
        internal static ReadOnlyAssetScope BeginAssetEdit(Object asset)
        {
            bool sdkOwned = ConvaiAssetOwnership.IsSdkAsset(asset);
            if (sdkOwned)
            {
                // The button is offered only when there is a file to copy. An object that is not a
                // saved asset at all reaches this the same way a package asset does, and a button
                // that quietly does nothing when pressed is worse than no button.
                bool canCopy = !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset));
                bool shipsWithConvai = ConvaiAssetOwnership.IsConvaiPackageAsset(asset);

                ConvaiEditorFrame.InfoBox(
                    ConvaiAssetOwnership.ReadOnlyTitle(shipsWithConvai),
                    $"{ConvaiAssetOwnership.ReadOnlyLead(shipsWithConvai)} Make a copy in your " +
                    "project to edit it, or just tune the character that uses it — a character gets " +
                    "its own copy automatically the first time you change anything.",
                    canCopy ? CreateProjectCopyButton : null,
                    canCopy ? () => DuplicateIntoProject(asset) : null);
                EditorGUILayout.Space(4f);
            }

            return new ReadOnlyAssetScope(sdkOwned);
        }

        /// <summary>Duplicates an SDK asset into the project and selects the copy.</summary>
        private static void DuplicateIntoProject(Object asset)
        {
            string sourcePath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(sourcePath)) return;

            string directory = ConvaiProjectAssetFolder.ForProjectRoot(asset.GetType().Name);
            string newPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{asset.name}.asset");

            if (!AssetDatabase.CopyAsset(sourcePath, newPath))
            {
                EditorUtility.DisplayDialog(
                    "Convai",
                    $"Unity could not copy '{sourcePath}' to '{directory}'. Check that the folder is " +
                    "writable, then try again.",
                    "OK");
                return;
            }

            AssetDatabase.SaveAssets();
            Object copy = AssetDatabase.LoadAssetAtPath<Object>(newPath);
            Selection.activeObject = copy;
            EditorGUIUtility.PingObject(copy);
        }

        /// <summary>Disables the inspector body while an SDK-owned asset is on screen.</summary>
        internal readonly struct ReadOnlyAssetScope : IDisposable
        {
            private readonly bool _disabled;

            internal ReadOnlyAssetScope(bool disabled)
            {
                _disabled = disabled;
                if (disabled) EditorGUI.BeginDisabledGroup(true);
            }

            public void Dispose()
            {
                if (_disabled) EditorGUI.EndDisabledGroup();
            }
        }
    }
}
