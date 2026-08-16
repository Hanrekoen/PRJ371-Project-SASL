using System;
using System.Collections.Generic;
using Convai.Editor.ConfigurationWindow.Services;
using Convai.Editor.Utilities;
using Convai.Runtime;
using UnityEditor;

namespace Convai.Editor.Settings.Services
{
    /// <summary>One project-level health item, optionally with an automated fix.</summary>
    public sealed class ProjectHealthItem
    {
        public ProjectHealthItem(SetupHealthCheckResult result, string fixLabel = null, Action fix = null)
        {
            Result = result;
            FixLabel = fixLabel;
            Fix = fix;
        }

        public SetupHealthCheckResult Result { get; }

        /// <summary>Label for the fix button; null when no automated fix exists.</summary>
        public string FixLabel { get; }

        /// <summary>Automated fix action; null when the item is informational.</summary>
        public Action Fix { get; }
    }

    /// <summary>
    ///     Project-configuration health checks for the settings Setup Health section.
    ///     Complements the scene-level checks in <see cref="SetupHealthService" />.
    /// </summary>
    public static class ProjectSetupHealthService
    {
        private const string SettingsAssetPath = "Assets/Resources/ConvaiSettings.asset";
        private const string DefaultMicUsageDescription = "Microphone access is required for voice conversations.";

        /// <summary>Builds the project-level health report.</summary>
        public static IReadOnlyList<ProjectHealthItem> BuildProjectReport()
        {
            var items = new List<ProjectHealthItem>
            {
                CheckSettingsAsset(),
                new ProjectHealthItem(SetupHealthService.CheckApiKey()),
                CheckIosMicrophoneUsageDescription(),
                CheckIosPrepareForRecording(),
                CheckAndroidMicrophonePermission()
            };

            items.AddRange(CheckDefineDrift());
            AddPlatformCaveats(items);
            return items;
        }

        private static ProjectHealthItem CheckSettingsAsset()
        {
            bool exists = AssetDatabase.LoadAssetAtPath<ConvaiSettings>(SettingsAssetPath) != null;
            if (exists)
            {
                return new ProjectHealthItem(new SetupHealthCheckResult(
                    "settings-asset", "Settings Asset", SetupHealthStatus.Healthy,
                    $"Present at {SettingsAssetPath}."));
            }

            return new ProjectHealthItem(
                new SetupHealthCheckResult(
                    "settings-asset", "Settings Asset", SetupHealthStatus.Blocked,
                    $"Missing at {SettingsAssetPath}. Runtime code cannot load project defaults."),
                "Create",
                ConvaiSettings.EnsureSettingsAssetExists);
        }

        private static ProjectHealthItem CheckIosMicrophoneUsageDescription()
        {
            if (!string.IsNullOrWhiteSpace(PlayerSettings.iOS.microphoneUsageDescription))
            {
                return new ProjectHealthItem(new SetupHealthCheckResult(
                    "ios-mic-usage", "iOS Microphone Usage Description", SetupHealthStatus.Healthy,
                    "Set in Player Settings."));
            }

            return new ProjectHealthItem(
                new SetupHealthCheckResult(
                    "ios-mic-usage", "iOS Microphone Usage Description", SetupHealthStatus.Warning,
                    "Empty. iOS builds crash on microphone access without a usage description."),
                "Set Default",
                () => PlayerSettings.iOS.microphoneUsageDescription = DefaultMicUsageDescription);
        }

        private static ProjectHealthItem CheckIosPrepareForRecording()
        {
            if (!IosPlayerSettings.TryGetPrepareForRecording(out bool enabled))
            {
                return new ProjectHealthItem(new SetupHealthCheckResult(
                    "ios-prepare-recording", "Prepare iOS for Recording", SetupHealthStatus.Warning,
                    "Unavailable in this Unity version. Verify the iOS audio setting in Player Settings."));
            }

            if (enabled)
            {
                return new ProjectHealthItem(new SetupHealthCheckResult(
                    "ios-prepare-recording", "Prepare iOS for Recording", SetupHealthStatus.Healthy,
                    "Enabled for Convai microphone recording and speaker playback."));
            }

            return new ProjectHealthItem(
                new SetupHealthCheckResult(
                    "ios-prepare-recording", "Prepare iOS for Recording", SetupHealthStatus.Warning,
                    "Disabled. Enable this for Convai microphone recording and speaker playback on iOS."),
                "Enable",
                () => IosPlayerSettings.TrySetPrepareForRecording(IosPlayerSettings.DefaultPrepareForRecording));
        }

        private static ProjectHealthItem CheckAndroidMicrophonePermission()
        {
            // Unity injects RECORD_AUDIO when Microphone APIs are referenced, but custom
            // manifests can strip it - there is no reliable editor-side check, so inform only.
            return new ProjectHealthItem(new SetupHealthCheckResult(
                "android-mic-permission", "Android Microphone Permission", SetupHealthStatus.Healthy,
                $"RECORD_AUDIO must be present in the merged manifest. See {ConvaiEditorLinks.DocsHomeUrl} if you use a custom manifest."));
        }

        private static IEnumerable<ProjectHealthItem> CheckDefineDrift()
        {
            foreach ((string symbol, string label, _) in ScriptingDefineToggleService.FeatureDefines)
            {
                IReadOnlyList<BuildTargetGroup> drifting = ScriptingDefineToggleService.GetDriftingGroups(symbol);
                if (drifting.Count == 0) continue;

                string symbolCopy = symbol;
                yield return new ProjectHealthItem(
                    new SetupHealthCheckResult(
                        $"define-drift-{symbol}", $"Define Drift: {label}", SetupHealthStatus.Warning,
                        $"{symbol} differs from the active build target on: {string.Join(", ", drifting)}."),
                    "Sync All",
                    () => ScriptingDefineToggleService.SyncToAllGroups(symbolCopy));
            }
        }

        private static void AddPlatformCaveats(List<ProjectHealthItem> items)
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
            {
                items.Add(new ProjectHealthItem(new SetupHealthCheckResult(
                    "webgl-caveats", "WebGL Platform", SetupHealthStatus.Warning,
                    "WebGL uses the browser transport path; microphone enumeration and some native features differ from desktop.")));
            }
        }
    }

    /// <summary>
    ///     Accesses Unity's serialized iOS recording switch. Unity exposes this setting in the
    ///     Player Settings inspector but does not provide a public strongly typed API for it.
    /// </summary>
    internal static class IosPlayerSettings
    {
        internal const bool DefaultPrepareForRecording = true;

        private const string PlayerSettingsAssetPath = "ProjectSettings/ProjectSettings.asset";
        private const string PrepareForRecordingPropertyPath = "Prepare IOS For Recording";

        internal static bool TryGetPrepareForRecording(out bool enabled)
        {
            if (!TryFindPrepareForRecordingProperty(out _, out SerializedProperty property))
            {
                enabled = DefaultPrepareForRecording;
                return false;
            }

            enabled = property.boolValue;
            return true;
        }

        internal static bool TrySetPrepareForRecording(bool enabled)
        {
            if (!TryFindPrepareForRecordingProperty(out SerializedObject settings,
                    out SerializedProperty property))
                return false;

            if (property.boolValue == enabled) return true;

            property.boolValue = enabled;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings.targetObject);
            return true;
        }

        private static bool TryFindPrepareForRecordingProperty(
            out SerializedObject settings,
            out SerializedProperty property)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(PlayerSettingsAssetPath);
            if (assets.Length == 0)
            {
                settings = null;
                property = null;
                return false;
            }

            settings = new SerializedObject(assets[0]);
            settings.Update();
            property = settings.FindProperty(PrepareForRecordingPropertyPath);
            return property != null && property.propertyType == SerializedPropertyType.Boolean;
        }
    }
}
