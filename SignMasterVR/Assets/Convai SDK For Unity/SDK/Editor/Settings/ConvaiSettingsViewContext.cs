using System;
using Convai.Editor.Settings.Services;
using Convai.Runtime;
using UnityEditor;

namespace Convai.Editor.Settings
{
    /// <summary>Which surface is hosting the shared settings section views.</summary>
    public enum ConvaiSettingsHostKind
    {
        /// <summary>Edit &gt; Project Settings &gt; Convai SDK.</summary>
        ProjectSettings,

        /// <summary>The Convai Editor (configuration) window.</summary>
        ConfigurationWindow
    }

    /// <summary>
    ///     Shared state for one mounted set of settings section views.
    ///     The host owns the <see cref="SerializedObject" /> lifetime: it creates the
    ///     context on activate/attach and disposes it on deactivate/detach.
    /// </summary>
    public sealed class ConvaiSettingsViewContext : IDisposable
    {
        private static event Action CredentialsChangedAcrossHosts;

        private bool _savePending;
        private bool _disposed;

        public ConvaiSettingsViewContext(SerializedObject settings, ConvaiSettingsHostKind host)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Host = host;
            Validation = new ApiKeyValidationService();
            CredentialsChangedAcrossHosts += OnCredentialsChangedAcrossHosts;
        }

        /// <summary>Serialized view over <see cref="ConvaiSettings.Instance" />.</summary>
        public SerializedObject Settings { get; }

        /// <summary>The hosting surface.</summary>
        public ConvaiSettingsHostKind Host { get; }

        /// <summary>Shared API key validation service for this mount.</summary>
        public ApiKeyValidationService Validation { get; }

        /// <summary>Raised after the API key is saved or cleared through the credentials view.</summary>
        public event Action CredentialsChanged;

        /// <summary>True once the context has been disposed by its host.</summary>
        public bool IsDisposed => _disposed;

        /// <summary>
        ///     Schedules a debounced settings-asset save so rapid edits
        ///     batch into a single disk write.
        /// </summary>
        public void RequestSave()
        {
            if (_savePending || _disposed) return;

            _savePending = true;
            EditorApplication.delayCall += FlushSave;
        }

        /// <summary>Notifies both hosts (and the configuration window gating) that credentials changed.</summary>
        public void NotifyCredentialsChanged() => CredentialsChangedAcrossHosts?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            CredentialsChangedAcrossHosts -= OnCredentialsChangedAcrossHosts;
            EditorApplication.delayCall -= FlushSave;
            _savePending = false;
            SaveSettingsAsset();

            CredentialsChanged = null;
            Settings.Dispose();
        }

        private void OnCredentialsChangedAcrossHosts()
        {
            if (_disposed) return;

            Settings.Update();
            CredentialsChanged?.Invoke();
        }

        private void FlushSave()
        {
            if (!_savePending) return;

            _savePending = false;
            SaveSettingsAsset();
        }

        private void SaveSettingsAsset()
        {
            if (Settings.targetObject != null)
                AssetDatabase.SaveAssetIfDirty(Settings.targetObject);
        }
    }
}
