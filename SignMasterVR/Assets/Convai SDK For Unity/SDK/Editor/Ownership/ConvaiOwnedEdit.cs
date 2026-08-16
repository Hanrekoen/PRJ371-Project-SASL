using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Editor.Ownership
{
    /// <summary>
    ///     How one module gives a character its own copy of a settings asset.
    /// </summary>
    /// <remarks>
    ///     The single genuinely per-module part of copy-on-write, and the reason this is an interface
    ///     rather than another branch inside the mechanism. Most modules hand a character one asset
    ///     through one serialized field, and <see cref="ConvaiFieldSettingsCopier" /> already does
    ///     that; Body Animation's config arrives through a profile that has to be duplicated with it,
    ///     or the profile would keep supplying the shared config and the copy would be dead weight.
    ///     A sixth module with a sixth arrangement implements this instead of editing the mechanism.
    /// </remarks>
    internal interface IConvaiSettingsCopier
    {
        /// <summary>
        ///     What these settings are called in a sentence — "animation settings", "personality".
        ///     Reads back to the user in the undo step ("Create Nova's own personality").
        /// </summary>
        string SettingsNoun { get; }

        /// <summary>
        ///     Duplicates <paramref name="asset" /> into the project for <paramref name="owner" /> and
        ///     repoints the character at the copy. Only ever called for an SDK-owned asset.
        /// </summary>
        ConvaiCopyOnWriteResult CopyForOwner(Object asset, Component owner);
    }

    /// <summary>
    ///     The copier for the ordinary arrangement: one settings asset, referenced by one serialized
    ///     field on the character's component.
    /// </summary>
    /// <remarks>
    ///     Written once because four of the five embodiment modules are shaped exactly like this.
    ///     A module that needs something else implements <see cref="IConvaiSettingsCopier" /> — it
    ///     does not add a flag here.
    /// </remarks>
    internal sealed class ConvaiFieldSettingsCopier : IConvaiSettingsCopier
    {
        private readonly string _moduleFolder;
        private readonly string _assetNameSuffix;
        private readonly string _ownerFieldName;

        /// <param name="settingsNoun">See <see cref="IConvaiSettingsCopier.SettingsNoun" />.</param>
        /// <param name="moduleFolder">Fallback folder name under <c>Assets/Convai/</c>.</param>
        /// <param name="assetNameSuffix">Appended to the character's name, e.g. <c>_Emotion</c>.</param>
        /// <param name="ownerFieldName">The component's serialized field holding the asset.</param>
        internal ConvaiFieldSettingsCopier(
            string settingsNoun, string moduleFolder, string assetNameSuffix, string ownerFieldName)
        {
            SettingsNoun = settingsNoun;
            _moduleFolder = moduleFolder;
            _assetNameSuffix = assetNameSuffix;
            _ownerFieldName = ownerFieldName;
        }

        public string SettingsNoun { get; }

        public ConvaiCopyOnWriteResult CopyForOwner(Object asset, Component owner) =>
            ConvaiCopyOnWrite.CopyAndRepoint(
                asset, owner, _moduleFolder, _assetNameSuffix, _ownerFieldName);
    }

    /// <summary>
    ///     A block of inspector controls bound to a settings asset the character may not own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Replaces the <c>new SerializedObject(asset)</c> … <c>ApplyModifiedProperties()</c> pair
    ///         that every Convai settings surface is built from. Inside the scope nothing changes:
    ///         controls read and write <see cref="Serialized" /> exactly as before, at any field
    ///         count, through whatever helper methods the module already has. What changes is where
    ///         the write lands.
    ///     </para>
    ///     <para>
    ///         <b>The rule.</b> An SDK-owned asset never sees <c>ApplyModifiedProperties</c>. If the
    ///         user changed something, the asset is copied into the project first, the character is
    ///         repointed at the copy, and the edit — which until now existed only in this scope's
    ///         in-memory <see cref="SerializedObject" /> — is transferred onto the copy and applied
    ///         there. The package is never written to, not even momentarily.
    ///     </para>
    ///     <para>
    ///         This is why the mechanism is a scope rather than a per-field rewrite. The first version
    ///         lifted Body Animation's four values into locals by hand; Emotion has eight spread over
    ///         four helper methods and Gaze has a dial table, and none of that has to be touched.
    ///     </para>
    /// </remarks>
    internal readonly struct ConvaiOwnedEdit : IDisposable
    {
        private readonly Object _asset;
        private readonly Component _owner;
        private readonly IConvaiSettingsCopier _copier;
        private readonly int _undoGroup;

        private ConvaiOwnedEdit(
            Object asset, Component owner, IConvaiSettingsCopier copier,
            SerializedObject serialized, int undoGroup)
        {
            _asset = asset;
            _owner = owner;
            _copier = copier;
            _undoGroup = undoGroup;
            Serialized = serialized;
        }

        /// <summary>The object controls read and write. Never applied to an asset the SDK owns.</summary>
        internal SerializedObject Serialized { get; }

        /// <summary>
        ///     Whether the user may drive these controls. False only when the SDK owns the asset and
        ///     there is no character to copy it for — everything else is the user's already, or
        ///     becomes theirs the moment they change it.
        /// </summary>
        internal bool CanEdit =>
            _asset != null && (_owner != null || ConvaiAssetOwnership.IsProjectAsset(_asset));

        /// <summary>Opens the scope. Returns a disposed-safe empty scope when there is no asset.</summary>
        /// <param name="asset">The settings asset the controls are bound to.</param>
        /// <param name="owner">
        ///     The character these settings belong to, or <c>null</c> when the asset is being
        ///     inspected on its own — there, an SDK-owned asset is read-only rather than copied into
        ///     a project it was never asked to join.
        /// </param>
        /// <param name="copier">How to copy for this character; see <see cref="IConvaiSettingsCopier" />.</param>
        internal static ConvaiOwnedEdit Begin(
            Object asset, Component owner, IConvaiSettingsCopier copier)
        {
            if (asset == null) return default;

            var serialized = new SerializedObject(asset);
            serialized.Update();
            return new ConvaiOwnedEdit(asset, owner, copier, serialized, Undo.GetCurrentGroup());
        }

        /// <summary>
        ///     Commits the block: straight through for an asset the user owns, via a copy for one the
        ///     SDK owns, and not at all when nothing changed.
        /// </summary>
        public void Dispose()
        {
            if (Serialized == null || _asset == null) return;
            if (!Serialized.hasModifiedProperties) return;

            if (ConvaiAssetOwnership.IsProjectAsset(_asset))
            {
                // The user's own asset, shared or not. Sharing is reported before the fact by the
                // ownership notice; forking it here would take away a choice somebody may have made
                // deliberately.
                Serialized.ApplyModifiedProperties();
                return;
            }

            // Unreachable through a correctly drawn surface, which disables its controls when
            // CanEdit is false. Dropping the edit rather than writing it is still the right answer
            // if one ever forgets: a write here would be discarded by Unity anyway, silently.
            if (_owner == null || _copier == null) return;

            ConvaiCopyOnWriteResult result = _copier.CopyForOwner(_asset, _owner);
            if (!result.Succeeded)
            {
                if (!string.IsNullOrEmpty(result.FailureReason))
                    EditorUtility.DisplayDialog("Convai", result.FailureReason, "OK");
                return;
            }

            ConvaiCopyOnWrite.TransferPendingEdits(Serialized, result.Target);
            ConvaiAssetOwnership.Invalidate();
            ConvaiCopyReceipts.Record(_owner, result.AssetPath, result.Target);

            // One entry in the undo history, named for what the user perceives happening. The asset
            // file is not part of it - see ConvaiCopyOnWrite for why that is deliberate.
            Undo.SetCurrentGroupName(ConvaiCopyOnWrite.UndoName(_owner, _copier.SettingsNoun));
            Undo.CollapseUndoOperations(_undoGroup);
        }
    }
}
