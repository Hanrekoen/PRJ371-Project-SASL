using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Editor.Ownership
{
    /// <summary>
    ///     What happened when a control was about to write to settings the character does not own.
    /// </summary>
    internal readonly struct ConvaiCopyOnWriteResult
    {
        private ConvaiCopyOnWriteResult(Object target, bool copied, string assetPath, string failureReason)
        {
            Target = target;
            Copied = copied;
            AssetPath = assetPath;
            FailureReason = failureReason;
        }

        /// <summary>The asset the edit must be applied to; <c>null</c> when nothing usable exists.</summary>
        internal Object Target { get; }

        /// <summary>Whether a copy was made, i.e. whether the user needs telling.</summary>
        internal bool Copied { get; }

        /// <summary>Project path of the copy; empty when none was made.</summary>
        internal string AssetPath { get; }

        /// <summary>Why the copy could not be made, in the user's own terms; empty on success.</summary>
        internal string FailureReason { get; }

        internal bool Succeeded => Target != null;

        internal static ConvaiCopyOnWriteResult Unchanged(Object target) =>
            new(target, false, string.Empty, string.Empty);

        internal static ConvaiCopyOnWriteResult Made(Object target, string assetPath) =>
            new(target, true, assetPath, string.Empty);

        internal static ConvaiCopyOnWriteResult Failed(string reason) =>
            new(null, false, string.Empty, reason);
    }

    /// <summary>
    ///     Makes a character's settings writable at the moment the user tries to change them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The SDK used to hand a character a settings asset from inside the package and then
    ///         disable every control that would edit it, with a paragraph explaining why and a button
    ///         to press first. That is a correct explanation of a problem the SDK created, delivered
    ///         to the one person who did not create it. A user opening Unity for the first time meets
    ///         a headline feature by reading a disclaimer.
    ///     </para>
    ///     <para>
    ///         So the controls stay live. The first edit copies the asset into the project, points the
    ///         character at the copy, applies the edit there, and says afterwards what it did. Nothing
    ///         is asked of the user before they are allowed to try; the SDK does the bookkeeping it
    ///         created the need for. Unity's own Input System does the same with its project-wide
    ///         actions asset.
    ///     </para>
    ///     <para>
    ///         <b>Undo.</b> The character's reference change is undoable and is named for what
    ///         happened, so the undo history reads "Create Nova's own animation settings" rather than
    ///         "Inspector". The asset file itself is not: Unity's undo system does not cover
    ///         <see cref="AssetDatabase" /> creation. The file therefore stays in the project after an
    ///         undo, where the user can see it and delete it — deleting someone's file behind their
    ///         back on Ctrl-Z would be the worse of the two behaviours.
    ///     </para>
    /// </remarks>
    internal static class ConvaiCopyOnWrite
    {
        /// <summary>
        ///     Returns the asset an edit should be applied to, copying it into the project first when
        ///     the SDK owns it.
        /// </summary>
        /// <param name="asset">The settings asset the control was drawn against.</param>
        /// <param name="owner">
        ///     The character the copy is for. When <c>null</c> there is nobody to copy for — an asset
        ///     inspected on its own in the Project window — and an SDK asset is reported as
        ///     unwritable rather than silently duplicated into a project it does not belong to.
        /// </param>
        /// <param name="copy">
        ///     Performs the module's own copy: duplicates the asset (and anything that supplies it),
        ///     repoints <paramref name="owner" />, and returns the copy. Only called for an SDK-owned
        ///     asset, so a module cannot accidentally copy a project asset the user meant to share.
        /// </param>
        internal static ConvaiCopyOnWriteResult EnsureWritable(
            Object asset, Component owner, Func<ConvaiCopyOnWriteResult> copy)
        {
            if (asset == null)
                return ConvaiCopyOnWriteResult.Failed("There are no settings here to change.");

            // A project asset is already the user's, shared or not. Sharing is reported by the
            // ownership notice and left alone: changing several characters at once is a choice
            // somebody can legitimately make, and silently forking it would take that away.
            if (ConvaiAssetOwnership.IsProjectAsset(asset))
                return ConvaiCopyOnWriteResult.Unchanged(asset);

            if (owner == null)
            {
                return ConvaiCopyOnWriteResult.Failed(
                    "These settings ship with the Convai SDK and no character is selected, so there " +
                    "is nobody to make a copy for. Select the character you want to change.");
            }

            ConvaiCopyOnWriteResult result = copy();
            if (result.Succeeded) ConvaiAssetOwnership.Invalidate();
            return result;
        }

        /// <summary>
        ///     The undo step name for a copy made on this character's behalf, phrased as the thing
        ///     that happened rather than as the mechanism.
        /// </summary>
        internal static string UndoName(Component owner, string what) =>
            $"Create {(owner != null ? owner.name : "this character")}'s own {what}";

        /// <summary>
        ///     Moves an edit that exists only in memory onto the asset that may keep it.
        /// </summary>
        /// <remarks>
        ///     <paramref name="source" /> wraps the SDK-owned original and has deliberately never been
        ///     applied, so the user's change lives nowhere but in it. The copy on disk is a byte-exact
        ///     duplicate of the original, so transferring the visible properties is enough to land the
        ///     change and leave everything else as it was.
        ///     <para>
        ///         Iterating <c>NextVisible</c> skips <c>[HideInInspector]</c> fields, which is correct
        ///         rather than a gap: the user cannot have edited a field no surface draws, and the
        ///         copy already carries its on-disk value. <c>m_Script</c> is skipped because writing
        ///         it would repoint the copy's script reference at the original's.
        ///     </para>
        /// </remarks>
        internal static void TransferPendingEdits(SerializedObject source, Object target)
        {
            if (source == null || target == null) return;

            var destination = new SerializedObject(target);
            SerializedProperty property = source.GetIterator();

            if (property.NextVisible(true))
            {
                do
                {
                    if (property.propertyPath == "m_Script") continue;
                    destination.CopyFromSerializedProperty(property);
                }
                while (property.NextVisible(false));
            }

            destination.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        /// <summary>
        ///     Saves a freshly built settings asset into the project for one character and points
        ///     that character's component at it.
        /// </summary>
        /// <remarks>
        ///     What a module's setup action does instead of handing the character an asset from
        ///     inside the package. The module builds the instance — only it knows what a sensible
        ///     starting point is, and for Body Animation that includes the shipped animation set,
        ///     which is content and rightly stays in the package. Everything after that is the same
        ///     for every module, so it is written once.
        ///     <para>
        ///         <c>hideFlags</c> is cleared first: several modules' <c>CreateDefault</c> factories
        ///         mark their instance <see cref="HideFlags.HideAndDontSave" /> because they normally
        ///         produce a throwaway runtime object, and an asset saved with that flag is written
        ///         to disk and then silently dropped on the next domain reload.
        ///     </para>
        /// </remarks>
        internal static ConvaiCopyOnWriteResult CreateAndAssign(
            Object created, Component owner, string moduleFolder, string assetNameSuffix, string ownerFieldName)
        {
            if (created == null || owner == null)
                return ConvaiCopyOnWriteResult.Failed("There are no settings to create here.");

            created.hideFlags = HideFlags.None;

            string directory = ConvaiProjectAssetFolder.For(owner, moduleFolder);
            string newPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{ConvaiProjectAssetFolder.SanitizeName(owner)}{assetNameSuffix}.asset");

            AssetDatabase.CreateAsset(created, newPath);
            AssetDatabase.SaveAssets();

            var serializedOwner = new SerializedObject(owner);
            SerializedProperty field = serializedOwner.FindProperty(ownerFieldName);
            if (field == null)
            {
                return ConvaiCopyOnWriteResult.Failed(
                    $"The settings were created at '{newPath}', but this component has no " +
                    $"'{ownerFieldName}' field to point at them.");
            }

            field.objectReferenceValue = created;
            serializedOwner.ApplyModifiedProperties();

            ConvaiAssetOwnership.Invalidate();
            return ConvaiCopyOnWriteResult.Made(created, newPath);
        }

        /// <summary>
        ///     Duplicates a settings asset into the project for one character and points that
        ///     character's component at the copy.
        /// </summary>
        /// <remarks>
        ///     The whole of copy-on-write for the ordinary arrangement — one asset behind one
        ///     serialized field — which is four of the five embodiment modules. Written once here so
        ///     they cannot disagree about where the copy lands, what it is called, or what happens
        ///     when Unity refuses.
        /// </remarks>
        internal static ConvaiCopyOnWriteResult CopyAndRepoint(
            Object asset, Component owner, string moduleFolder, string assetNameSuffix, string ownerFieldName)
        {
            if (asset == null || owner == null)
                return ConvaiCopyOnWriteResult.Failed("There are no settings here to copy.");

            string sourcePath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(sourcePath))
            {
                return ConvaiCopyOnWriteResult.Failed(
                    "These settings are not a saved project asset, so they cannot be copied.");
            }

            string directory = ConvaiProjectAssetFolder.For(owner, moduleFolder);
            string newPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{ConvaiProjectAssetFolder.SanitizeName(owner)}{assetNameSuffix}.asset");

            if (!AssetDatabase.CopyAsset(sourcePath, newPath))
            {
                return ConvaiCopyOnWriteResult.Failed(
                    $"Unity could not copy '{sourcePath}' to '{directory}'. Check that the folder is " +
                    "writable, then try again.");
            }

            var copy = AssetDatabase.LoadAssetAtPath<Object>(newPath);
            var serializedOwner = new SerializedObject(owner);
            SerializedProperty field = serializedOwner.FindProperty(ownerFieldName);

            if (field == null)
            {
                // Reported rather than swallowed: leaving the character on the SDK's asset while a
                // copy sits unused in the project is the exact trap this mechanism exists to prevent.
                return ConvaiCopyOnWriteResult.Failed(
                    $"The settings were copied to '{newPath}', but this component has no " +
                    $"'{ownerFieldName}' field to point at the copy, so the character still reads the " +
                    "shipped settings. Assign the copy yourself.");
            }

            field.objectReferenceValue = copy;
            serializedOwner.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            return ConvaiCopyOnWriteResult.Made(copy, newPath);
        }
    }
}
