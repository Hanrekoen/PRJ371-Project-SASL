using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace LiveKit.Editor
{
    /// <summary>
    /// Automatically downloads LiveKit FFI native libraries on first import.
    /// This keeps the package size small by downloading platform-specific binaries on-demand.
    /// Downloaded native plugins are written under Assets/Convai/... so installs from a
    /// read-only PackageCache location still have a writable import destination.
    /// </summary>
    [InitializeOnLoad]
    public static class LiveKitFFIDownloader
    {
        private const string FFI_VERSION = "0.12.63";
        private const string FFI_TAG = "livekit-ffi/v" + FFI_VERSION;
        private const string DOWNLOAD_BASE_URL = "https://github.com/livekit/rust-sdks/releases/download";
        private const string DOWNLOAD_COMPLETE_KEY = "LiveKit_FFI_Downloaded_" + FFI_VERSION;

        private static readonly string PluginsPath;
        private static bool _isDownloadInProgress;

        private static readonly Dictionary<string, string[]> PlatformArchitectures = new()
        {
            { "android", new[] { "arm64", "armv7", "x86_64" } },
            { "ios", new[] { "arm64", "sim-arm64" } },
            { "macos", new[] { "arm64", "x86_64" } },
            { "linux", new[] { "x86_64" } },
            { "windows", new[] { "arm64", "x86_64" } }
        };

        private sealed class DownloadResult
        {
            public bool Success { get; }
            public string Platform { get; }
            public string Arch { get; }
            public List<string> AssetPaths { get; }

            private DownloadResult(bool success, string platform, string arch, List<string> assetPaths)
            {
                Success = success;
                Platform = platform;
                Arch = arch;
                AssetPaths = assetPaths ?? new List<string>();
            }

            public static DownloadResult SuccessResult(string platform, string arch, List<string> assetPaths)
                => new DownloadResult(true, platform, arch, assetPaths);

            public static DownloadResult Failure(string platform, string arch)
                => new DownloadResult(false, platform, arch, new List<string>());
        }


        static LiveKitFFIDownloader()
        {
            // Keep downloaded native libraries in a project-local Assets path instead of the
            // package folder because UPM installs may live in a read-only PackageCache.
            PluginsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "Convai", "Plugins", "client-sdk-unity-livekit", "Runtime", "Plugins"));

            if (!Directory.Exists(PluginsPath))
            {
                Directory.CreateDirectory(PluginsPath);
            }

            EditorApplication.delayCall += CheckAndDownloadFFI;
        }

        private static string FullPathToAssetPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return "";

            string normalized = fullPath.Replace("\\", "/");
            string dataPath = Application.dataPath.Replace("\\", "/");
            if (!normalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return "";

            string relative = normalized.Substring(dataPath.Length).TrimStart('/');
            return $"Assets/{relative}";
        }

        private static void CheckAndDownloadFFI()
        {
            if (_isDownloadInProgress)
                return;

            string[] requiredPlatforms = GetRequiredPlatformsForDownload();
            if (requiredPlatforms == null || requiredPlatforms.Length == 0)
                return;

            if (EditorPrefs.GetBool(DOWNLOAD_COMPLETE_KEY, false))
            {
                bool allRequiredExist = requiredPlatforms.All(VerifyFFIFilesExist);
                if (allRequiredExist)
                    return;
            }

            string[] missingPlatforms = requiredPlatforms
                .Where(AreFFIFilesMissing)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (missingPlatforms.Length == 0)
            {
                EditorPrefs.SetBool(DOWNLOAD_COMPLETE_KEY, true);
                return;
            }

            Debug.Log($"[LiveKit] Missing FFI libraries detected for {string.Join(", ", missingPlatforms)}. Starting automatic download.");
            _ = DownloadRequiredPlatformsFFIAsync(missingPlatforms);
        }

        private static async Task DownloadRequiredPlatformsFFIAsync(IEnumerable<string> platforms)
        {
            if (platforms == null)
                return;

            string[] uniquePlatforms = platforms
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (uniquePlatforms.Length == 0)
                return;

            foreach (string platform in uniquePlatforms)
            {
                await DownloadPlatformFFIAsync(platform);
            }

            string[] requiredPlatforms = GetRequiredPlatformsForDownload();
            if (requiredPlatforms.Length > 0 && requiredPlatforms.All(VerifyFFIFilesExist))
            {
                EditorPrefs.SetBool(DOWNLOAD_COMPLETE_KEY, true);
            }
        }

        private static bool AreFFIFilesMissing(string platform)
        {
            string[] archs = GetRequiredArchitecturesForDownload(platform);
            foreach (string arch in archs)
            {
                string folderName = $"ffi-{platform}-{arch}";
                string folderPath = Path.Combine(PluginsPath, folderName);

                if (!Directory.Exists(folderPath))
                    return true;

                if (!HasNativeLibraryInFolder(folderPath))
                    return true;
            }

            return false;
        }

        private static bool VerifyFFIFilesExist(string platform)
        {
            string[] archs = GetRequiredArchitecturesForDownload(platform);
            foreach (string arch in archs)
            {
                string folderName = $"ffi-{platform}-{arch}";
                string folderPath = Path.Combine(PluginsPath, folderName);

                if (!Directory.Exists(folderPath))
                    return false;

                if (!HasNativeLibraryInFolder(folderPath))
                    return false;
            }

            return true;
        }

        private static bool HasNativeLibraryInFolder(string folderPath)
        {
            try
            {
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                    return false;

                foreach (string file in Directory.GetFiles(folderPath))
                {
                    if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string ext = Path.GetExtension(file);
                    if (string.IsNullOrEmpty(ext))
                        continue;

                    if (ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".so", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".dylib", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".a", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Best-effort check only.
            }

            return false;
        }

        private static string GetCurrentEditorPlatform()
        {
#if UNITY_EDITOR_WIN
            return "windows";
#elif UNITY_EDITOR_OSX
            return "macos";
#elif UNITY_EDITOR_LINUX
            return "linux";
#else
            return "windows";
#endif
        }

        internal static string GetCurrentEditorArchitecture()
        {
            var arch = RuntimeInformation.ProcessArchitecture;
            return arch switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X64 => "x86_64",
                Architecture.X86 => "x86",
                _ => "x86_64"
            };
        }

        private static string[] GetRequiredArchitecturesForDownload(string platform)
        {
            if (string.IsNullOrEmpty(platform))
                return Array.Empty<string>();

            List<string> required = new List<string>();

            // The Editor needs only the architecture of the running process.
            string editorPlatform = GetCurrentEditorPlatform();
            if (string.Equals(editorPlatform, platform, StringComparison.OrdinalIgnoreCase))
            {
                required.Add(GetCurrentEditorArchitecture());
            }

            // The active build target may require different or additional architectures. Keep both
            // when it shares the Editor platform (for example, an Apple Silicon Editor and a
            // universal macOS player build).
            string activeBuildPlatform = GetActiveBuildPlatform();
            if (!string.IsNullOrEmpty(activeBuildPlatform) &&
                string.Equals(activeBuildPlatform, platform, StringComparison.OrdinalIgnoreCase))
            {
                string[] buildArchs = GetArchitecturesForActiveBuildTargetPlatform(platform);
                if (buildArchs != null && buildArchs.Length > 0)
                    required.AddRange(buildArchs);
            }

            if (required.Count > 0)
            {
                return required
                    .Where(arch => !string.IsNullOrEmpty(arch))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            // Explicit all-platform downloads still use the complete known architecture list.
            return GetArchitecturesForPlatform(platform);
        }

        private static string[] GetArchitecturesForActiveBuildTargetPlatform(string platform)
        {
            if (string.IsNullOrEmpty(platform))
                return Array.Empty<string>();

            switch (platform)
            {
                case "android":
                    {
                        if (TryGetAndroidEnabledArchitectures(out string[] archs) && archs.Length > 0)
                            return archs;
                        return new[] { "arm64" };
                    }
                case "ios":
                    {
                        if (TryGetIosSdkArchitectures(out string[] archs) && archs.Length > 0)
                            return archs;
                        return new[] { "arm64" };
                    }
                case "macos":
                    {
                        if (TryGetMacOSBuildArchitectures(out string[] archs) && archs.Length > 0)
                            return archs;
                        return new[] { "x86_64" };
                    }
                case "windows":
                    // Unity's StandaloneWindows targets are typically x86_64; users can explicitly download arm64 via menu if needed.
                    return new[] { "x86_64" };
                case "linux":
                    return new[] { "x86_64" };
                default:
                    return GetArchitecturesForPlatform(platform);
            }
        }

        private static bool TryGetMacOSBuildArchitectures(out string[] archs)
        {
            archs = Array.Empty<string>();
            try
            {
                // PlayerSettings.macOSArchitecture exists in newer Unity versions.
                PropertyInfo prop = typeof(PlayerSettings).GetProperty("macOSArchitecture", BindingFlags.Public | BindingFlags.Static);
                if (prop == null)
                    return false;

                object value = prop.GetValue(null, null);
                if (value == null)
                    return false;

                string s = value.ToString() ?? string.Empty;
                // Common values: "x86_64", "ARM64", "x86_64ARM64" (universal).
                bool mentionsArm = s.IndexOf("ARM", StringComparison.OrdinalIgnoreCase) >= 0;
                bool mentionsIntel = s.IndexOf("x86", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0;
                if (mentionsArm && mentionsIntel)
                {
                    archs = new[] { "arm64", "x86_64" };
                    return true;
                }

                if (s.IndexOf("ARM", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    archs = new[] { "arm64" };
                    return true;
                }

                archs = new[] { "x86_64" };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetAndroidEnabledArchitectures(out string[] archs)
        {
            archs = Array.Empty<string>();
            try
            {
                // PlayerSettings.Android.targetArchitectures exists in many Unity versions.
                Type androidType = typeof(PlayerSettings).GetNestedType("Android", BindingFlags.Public | BindingFlags.NonPublic);
                if (androidType == null)
                    return false;

                PropertyInfo prop = androidType.GetProperty("targetArchitectures", BindingFlags.Public | BindingFlags.Static);
                if (prop == null)
                    return false;

                object value = prop.GetValue(null, null);
                if (value == null)
                    return false;

                // It's a flags enum; stringify and look for known tokens.
                string s = value.ToString() ?? string.Empty;
                List<string> enabled = new List<string>();
                if (s.IndexOf("ARM64", StringComparison.OrdinalIgnoreCase) >= 0)
                    enabled.Add("arm64");
                if (s.IndexOf("ARMv7", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("ARMV7", StringComparison.OrdinalIgnoreCase) >= 0)
                    enabled.Add("armv7");
                if (s.IndexOf("X86_64", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("X86-64", StringComparison.OrdinalIgnoreCase) >= 0)
                    enabled.Add("x86_64");

                if (enabled.Count == 0)
                    return false;

                archs = enabled.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetIosSdkArchitectures(out string[] archs)
        {
            archs = Array.Empty<string>();
            try
            {
                // PlayerSettings.iOS.sdkVersion can indicate device vs simulator.
                Type iosType = typeof(PlayerSettings).GetNestedType("iOS", BindingFlags.Public | BindingFlags.NonPublic);
                if (iosType == null)
                    return false;

                PropertyInfo prop = iosType.GetProperty("sdkVersion", BindingFlags.Public | BindingFlags.Static);
                if (prop == null)
                    return false;

                object value = prop.GetValue(null, null);
                if (value == null)
                    return false;

                string s = value.ToString() ?? string.Empty;
                // Common values: "DeviceSDK", "SimulatorSDK".
                if (s.IndexOf("Simulator", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    archs = new[] { "sim-arm64" };
                    return true;
                }

                archs = new[] { "arm64" };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetRequiredPlatformForDownload()
        {
            string buildPlatform = GetActiveBuildPlatform();
            if (!string.IsNullOrEmpty(buildPlatform))
                return buildPlatform;

            return GetCurrentEditorPlatform();
        }

        private static string[] GetRequiredPlatformsForDownload()
        {
            List<string> required = new List<string>();

            string editorPlatform = GetCurrentEditorPlatform();
            if (!string.IsNullOrEmpty(editorPlatform))
                required.Add(editorPlatform);

            string buildPlatform = GetActiveBuildPlatform();
            if (!string.IsNullOrEmpty(buildPlatform))
                required.Add(buildPlatform);

            return required
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string GetActiveBuildPlatform()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            switch (target)
            {
                case BuildTarget.Android:
                    return "android";
                case BuildTarget.iOS:
                    return "ios";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "windows";
                case BuildTarget.StandaloneOSX:
                    return "macos";
                case BuildTarget.StandaloneLinux64:
                    return "linux";
                default:
                    return "";
            }
        }

        private static string[] GetArchitecturesForPlatform(string platform)
        {
            if (string.IsNullOrEmpty(platform))
                return new[] { "x86_64" };

            return PlatformArchitectures.GetValueOrDefault(platform, new[] { "x86_64" });
        }

        [MenuItem("Convai/Platform Support/Download FFI Libraries (Current Platform)")]
        public static void DownloadCurrentPlatformFFI()
        {
            _ = DownloadPlatformFFIAsync(GetCurrentEditorPlatform());
        }

        [MenuItem("Convai/Platform Support/Download FFI Libraries (Active Build Target)")]
        public static void DownloadActiveBuildTargetFFI()
        {
            _ = DownloadPlatformFFIAsync(GetRequiredPlatformForDownload());
        }

        [MenuItem("Convai/Platform Support/Download FFI Libraries (All Platforms)")]
        public static void DownloadAllPlatformsFFI()
        {
            _ = DownloadAllFFIAsync();
        }

        private static void ApplyImportSettingsForFiles(IEnumerable<string> assetPaths, string platform, string arch)
        {
            if (assetPaths == null)
                return;

            AssetDatabase.Refresh();

            HashSet<string> uniquePaths = new HashSet<string>(assetPaths.Where(p => !string.IsNullOrEmpty(p)));
            int total = uniquePaths.Count;
            int current = 0;
            foreach (string assetPath in uniquePaths)
            {
                current++;
                float progress = total > 0 ? (float)current / total : 1f;
                EditorUtility.DisplayProgressBar("Applying LiveKit Import Settings",
                    $"{platform}-{arch}: {Path.GetFileName(assetPath)} ({current}/{total})",
                    progress);

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                if (importer == null)
                {
                    continue;
                }

                if (string.Equals(platform, "android", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetExtension(assetPath), ".jar", StringComparison.OrdinalIgnoreCase))
                {
                    PluginImportSettingsUtil.ApplyAndroidJarImportSettings(importer);
                }
                else
                {
                    PluginImportSettingsUtil.ApplyNativePluginImportSettings(importer, platform, arch);
                }

                importer.SaveAndReimport();
                AssetDatabase.WriteImportSettingsIfDirty(assetPath);
                AssetDatabase.ForceReserializeAssets(new[] { assetPath }, ForceReserializeAssetsOptions.ReserializeAssetsAndMetadata);
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        [MenuItem("Convai/Platform Support/Clear FFI Libraries")]
        public static void ClearFFILibraries()
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Clear FFI Libraries",
                "This will delete all downloaded FFI native libraries.\nYou will need to re-download them to use platform support.\n\nContinue?",
                "Clear",
                "Cancel"
            );

            if (!confirm) return;

            foreach (var platform in PlatformArchitectures)
            {
                foreach (string arch in platform.Value)
                {
                    string folderName = $"ffi-{platform.Key}-{arch}";
                    string folderPath = Path.Combine(PluginsPath, folderName);

                    if (Directory.Exists(folderPath))
                    {
                        foreach (string file in Directory.GetFiles(folderPath))
                        {
                            if (!file.EndsWith(".meta"))
                            {
                                try
                                {
                                    File.Delete(file);
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogWarning($"[LiveKit] Failed to delete {file}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }

            EditorPrefs.DeleteKey(DOWNLOAD_COMPLETE_KEY);
            AssetDatabase.Refresh();
            Debug.Log("[LiveKit] FFI libraries cleared.");
        }

        private static async Task DownloadAllFFIAsync()
        {
            int totalCount = PlatformArchitectures.Sum(p => p.Value.Length);
            int currentCount = 0;
            int successCount = 0;

            foreach (var platform in PlatformArchitectures)
            {
                foreach (string arch in platform.Value)
                {
                    currentCount++;
                    string progressTitle = $"Downloading LiveKit FFI ({currentCount}/{totalCount})";
                    bool includeSharedAndroidJar =
                        !string.Equals(platform.Key, "android", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(arch, "arm64", StringComparison.OrdinalIgnoreCase);

                    DownloadResult result = await DownloadAndExtractFFIAsync(
                        platform.Key,
                        arch,
                        progressTitle,
                        includeSharedAndroidJar);
                    if (result.Success)
                    {
                        successCount++;
                        ApplyImportSettingsForFiles(result.AssetPaths, result.Platform, result.Arch);
                    }
                }
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();

            if (successCount == totalCount)
            {
                EditorPrefs.SetBool(DOWNLOAD_COMPLETE_KEY, true);
                Debug.Log($"[LiveKit] Successfully downloaded all {totalCount} FFI libraries.");
            }
            else
            {
                Debug.LogWarning($"[LiveKit] Downloaded {successCount}/{totalCount} FFI libraries. Some platforms may not work.");
            }
        }

        private static async Task DownloadPlatformFFIAsync(string platform)
        {
            if (_isDownloadInProgress)
            {
                Debug.Log("[LiveKit] FFI download is already in progress. Skipping duplicate request.");
                return;
            }

            _isDownloadInProgress = true;
            if (string.IsNullOrEmpty(platform))
                platform = GetCurrentEditorPlatform();

            try
            {
                string[] archs = GetRequiredArchitecturesForDownload(platform);
                if (archs == null || archs.Length == 0)
                {
                    Debug.LogError($"[LiveKit] No architectures resolved for platform: {platform}");
                    return;
                }

                int successCount = 0;
                for (int i = 0; i < archs.Length; i++)
                {
                    string progressTitle = $"Downloading LiveKit FFI for {platform} ({i + 1}/{archs.Length})";
                    bool includeSharedAndroidJar =
                        !string.Equals(platform, "android", StringComparison.OrdinalIgnoreCase) || i == 0;
                    DownloadResult result = await DownloadAndExtractFFIAsync(
                        platform,
                        archs[i],
                        progressTitle,
                        includeSharedAndroidJar);
                    if (result.Success)
                    {
                        successCount++;
                        ApplyImportSettingsForFiles(result.AssetPaths, result.Platform, result.Arch);
                    }
                }

                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();

                Debug.Log($"[LiveKit] Downloaded {successCount}/{archs.Length} FFI libraries for {platform}.");

                // Ensure we don't trigger auto-download again on next domain reload if everything required is present.
                if (successCount == archs.Length && !AreFFIFilesMissing(platform))
                {
                    EditorPrefs.SetBool(DOWNLOAD_COMPLETE_KEY, true);
                }
            }
            finally
            {
                _isDownloadInProgress = false;
            }
        }

        private static bool IsPluginFileName(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext == ".dll" || ext == ".so" || ext == ".dylib" || ext == ".a" || ext == ".jar";
        }

        private static async Task<DownloadResult> DownloadAndExtractFFIAsync(
            string platform,
            string arch,
            string progressTitle,
            bool includeSharedAndroidJar)
        {
            string folderName = $"ffi-{platform}-{arch}";
            string zipFileName = $"{folderName}.zip";
            string downloadUrl = $"{DOWNLOAD_BASE_URL}/{FFI_TAG}/{zipFileName}";
            string destFolder = Path.Combine(PluginsPath, folderName);
            string tempZipPath = Path.Combine(Application.temporaryCachePath, zipFileName);
            List<string> extractedAssetPaths = new List<string>();

            try
            {
                EditorUtility.DisplayProgressBar(progressTitle, $"Downloading {folderName}...", 0f);

                using (UnityWebRequest request = UnityWebRequest.Get(downloadUrl))
                {
                    var operation = request.SendWebRequest();

                    while (!operation.isDone)
                    {
                        EditorUtility.DisplayProgressBar(progressTitle,
                            $"Downloading {folderName}... ({request.downloadProgress * 100:F0}%)",
                            request.downloadProgress * 0.8f);
                        await Task.Delay(100);
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"[LiveKit] Failed to download {folderName}: {request.error}");
                        return DownloadResult.Failure(platform, arch);
                    }

                    File.WriteAllBytes(tempZipPath, request.downloadHandler.data);
                }

                EditorUtility.DisplayProgressBar(progressTitle, $"Extracting {folderName}...", 0.9f);

                if (!Directory.Exists(destFolder))
                {
                    Directory.CreateDirectory(destFolder);
                }

                using (ZipArchive archive = ZipFile.OpenRead(tempZipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name) || entry.Name.EndsWith(".meta"))
                            continue;

                        if (entry.Name.Equals("livekit_ffi.h", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.Equals("LICENSE.md", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (platform == "android" &&
                            entry.Name.Equals("libwebrtc.jar", StringComparison.OrdinalIgnoreCase) &&
                            !includeSharedAndroidJar)
                            continue;

                        string destPath = Path.Combine(destFolder, entry.Name);
                        string tempExtractPath = Path.Combine(Application.temporaryCachePath, $"{Guid.NewGuid()}_{entry.Name}");

                        try
                        {
                            entry.ExtractToFile(tempExtractPath, overwrite: true);
                            bool moved = TryMoveIntoPlace(tempExtractPath, destPath);
                            if (!moved)
                            {
                                Debug.LogWarning($"[LiveKit] Skipped overwriting locked file: {destPath}");
                            }
                            else if (IsPluginFileName(entry.Name))
                            {
                                string assetPath = FullPathToAssetPath(destPath);
                                if (!string.IsNullOrEmpty(assetPath))
                                {
                                    extractedAssetPaths.Add(assetPath);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[LiveKit] Failed to extract {entry.Name}: {ex.Message}");
                        }
                        finally
                        {
                            if (File.Exists(tempExtractPath))
                            {
                                TryDeleteTemp(tempExtractPath);
                            }
                        }
                    }
                }

                string livekitHeaderPath = Path.Combine(destFolder, "livekit_ffi.h");
                if (File.Exists(livekitHeaderPath))
                {
                    File.Delete(livekitHeaderPath);
                }

                string licensePath = Path.Combine(destFolder, "LICENSE.md");
                if (File.Exists(licensePath))
                {
                    File.Delete(licensePath);
                }

                if (platform == "android" && !includeSharedAndroidJar)
                {
                    string jarPath = Path.Combine(destFolder, "libwebrtc.jar");
                    if (File.Exists(jarPath))
                    {
                        File.Delete(jarPath);
                    }
                }

                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }

                Debug.Log($"[LiveKit] Successfully installed {folderName}");
                return DownloadResult.SuccessResult(platform, arch, extractedAssetPaths);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiveKit] Error downloading {folderName}: {ex.Message}");
                return DownloadResult.Failure(platform, arch);
            }
        }

        private static bool TryMoveIntoPlace(string tempPath, string destPath, int retryCount = 3)
        {
            if (!File.Exists(tempPath))
                return true;

            for (int attempt = 0; attempt < retryCount; attempt++)
            {
                try
                {
                    if (File.Exists(destPath))
                    {
                        File.SetAttributes(destPath, FileAttributes.Normal);
                        File.Delete(destPath);
                    }

                    File.Move(tempPath, destPath);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    Task.Delay(200).Wait();
                }
                catch (IOException)
                {
                    Task.Delay(200).Wait();
                }
            }

            return false;
        }

        private static void TryDeleteTemp(string path)
        {
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            catch
            {
                // Ignore temp cleanup failures.
            }
        }

    }

    public static class PluginImportSettingsUtil
    {
        public static bool ApplyAndroidJarImportSettings(PluginImporter importer)
        {
            bool changed = false;
            changed |= SetCompatibleWithAnyPlatform(importer, false);
            changed |= SetCompatibleWithEditor(importer, false);
            changed |= SetCompatibleWithPlatform(importer, BuildTarget.Android, true);
            changed |= DisableOtherPlatforms(importer, BuildTarget.Android);
            return changed;
        }

        public static bool ApplyNativePluginImportSettings(PluginImporter importer, string platform, string arch)
        {
            bool changed = false;
            changed |= SetCompatibleWithAnyPlatform(importer, false);

            switch (platform)
            {
                case "macos":
                    changed |= ApplyMacSettings(importer, arch);
                    break;
                case "windows":
                    changed |= ApplyWindowsSettings(importer, arch);
                    break;
                case "linux":
                    changed |= ApplyLinuxSettings(importer, arch);
                    break;
                case "android":
                    changed |= ApplyAndroidSettings(importer, arch);
                    break;
                case "ios":
                    changed |= ApplyIosSettings(importer, arch);
                    break;
            }

            return changed;
        }

        private static bool ApplyMacSettings(PluginImporter importer, string arch)
        {
            bool changed = false;
            string currentEditorArch = LiveKitFFIDownloader.GetCurrentEditorArchitecture();
            bool enableForEditor = arch == currentEditorArch;

            changed |= SetCompatibleWithEditor(importer, enableForEditor);
            if (enableForEditor)
            {
                changed |= SetEditorData(importer, "OS", "OSX");
                changed |= SetEditorData(importer, "CPU", arch == "arm64" ? "ARM64" : "x86_64");
            }

            changed |= SetCompatibleWithPlatform(importer, BuildTarget.StandaloneOSX, true);
            changed |= SetPlatformData(importer, BuildTarget.StandaloneOSX, "CPU", arch == "arm64" ? "ARM64" : "x86_64");

            changed |= DisableOtherPlatforms(importer, BuildTarget.StandaloneOSX);
            return changed;
        }

        private static bool ApplyWindowsSettings(PluginImporter importer, string arch)
        {
            bool changed = false;
            string currentEditorArch = LiveKitFFIDownloader.GetCurrentEditorArchitecture();
            bool enableForEditor = arch == currentEditorArch;

            changed |= SetCompatibleWithEditor(importer, enableForEditor);
            if (enableForEditor)
            {
                changed |= SetEditorData(importer, "OS", "Windows");
                changed |= SetEditorData(importer, "CPU", arch == "arm64" ? "ARM64" : "x86_64");
            }

            changed |= SetCompatibleWithPlatform(importer, BuildTarget.StandaloneWindows64, true);
            if (arch == "arm64")
            {
                changed |= SetPlatformData(importer, BuildTarget.StandaloneWindows64, "CPU", "ARM64");
            }
            else
            {
                changed |= SetPlatformData(importer, BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            }

            changed |= DisableOtherPlatforms(importer, BuildTarget.StandaloneWindows64);
            return changed;
        }

        private static bool ApplyLinuxSettings(PluginImporter importer, string arch)
        {
            bool changed = false;
            bool enableForEditor = arch == "x86_64";

            changed |= SetCompatibleWithEditor(importer, enableForEditor);
            if (enableForEditor)
            {
                changed |= SetEditorData(importer, "OS", "Linux");
                changed |= SetEditorData(importer, "CPU", "x86_64");
            }

            changed |= SetCompatibleWithPlatform(importer, BuildTarget.StandaloneLinux64, true);
            changed |= SetPlatformData(importer, BuildTarget.StandaloneLinux64, "CPU", "x86_64");
            changed |= DisableOtherPlatforms(importer, BuildTarget.StandaloneLinux64);
            return changed;
        }

        private static bool ApplyAndroidSettings(PluginImporter importer, string arch)
        {
            bool changed = false;
            changed |= SetCompatibleWithEditor(importer, false);
            changed |= SetCompatibleWithPlatform(importer, BuildTarget.Android, true);

            string androidCpu = arch switch
            {
                "arm64" => "ARM64",
                "armv7" => "ARMv7",
                "x86_64" => "x86_64",
                _ => "ARM64"
            };
            changed |= SetPlatformData(importer, BuildTarget.Android, "CPU", androidCpu);
            changed |= SetPlatformData(importer, BuildTarget.Android, "AndroidCPU", androidCpu);
            changed |= SetPlatformData(importer, BuildTarget.Android, "OS", "Android");

            if (!string.Equals(importer.GetPlatformData(BuildTarget.Android, "CPU"), androidCpu, StringComparison.OrdinalIgnoreCase))
            {
                changed |= ForceAndroidPlatformData(importer, androidCpu);
            }

            changed |= DisableOtherPlatforms(importer, BuildTarget.Android);
            return changed;
        }

        private static bool ApplyIosSettings(PluginImporter importer, string arch)
        {
            bool changed = false;
            changed |= SetCompatibleWithEditor(importer, false);
            changed |= SetCompatibleWithPlatform(importer, BuildTarget.iOS, true);

            if (arch == "arm64" || arch == "sim-arm64")
            {
                changed |= SetPlatformData(importer, BuildTarget.iOS, "CPU", "ARM64");
            }

            changed |= DisableOtherPlatforms(importer, BuildTarget.iOS);
            return changed;
        }

        private static bool DisableOtherPlatforms(PluginImporter importer, BuildTarget activeTarget)
        {
            bool changed = false;
            changed |= SetCompatibleWithPlatform(importer, BuildTarget.Android, activeTarget == BuildTarget.Android);
            changed |= SetCompatibleWithPlatform(importer, BuildTarget.iOS, activeTarget == BuildTarget.iOS);
            changed |= SetCompatibleWithPlatform(importer, BuildTarget.StandaloneWindows64, activeTarget == BuildTarget.StandaloneWindows64);
            changed |= SetCompatibleWithPlatform(importer, BuildTarget.StandaloneOSX, activeTarget == BuildTarget.StandaloneOSX);
            changed |= SetCompatibleWithPlatform(importer, BuildTarget.StandaloneLinux64, activeTarget == BuildTarget.StandaloneLinux64);
            return changed;
        }

        private static bool SetCompatibleWithAnyPlatform(PluginImporter importer, bool value)
        {
            bool current = importer.GetCompatibleWithAnyPlatform();
            if (current == value) return false;
            importer.SetCompatibleWithAnyPlatform(value);
            return true;
        }

        private static bool SetCompatibleWithEditor(PluginImporter importer, bool value)
        {
            bool current = importer.GetCompatibleWithEditor();
            if (current == value) return false;
            importer.SetCompatibleWithEditor(value);
            return true;
        }

        private static bool SetCompatibleWithPlatform(PluginImporter importer, BuildTarget target, bool value)
        {
            bool current = importer.GetCompatibleWithPlatform(target);
            if (current == value) return false;
            importer.SetCompatibleWithPlatform(target, value);
            return true;
        }

        private static bool SetEditorData(PluginImporter importer, string key, string value)
        {
            string current = importer.GetEditorData(key);
            if (current == value) return false;
            importer.SetEditorData(key, value);
            return true;
        }

        private static bool SetPlatformData(PluginImporter importer, BuildTarget target, string key, string value)
        {
            string current = importer.GetPlatformData(target, key);
            if (current == value) return false;
            importer.SetPlatformData(target, key, value);
            return true;
        }

        private static bool ForceAndroidPlatformData(PluginImporter importer, string androidCpu)
        {
            SerializedObject serialized = new SerializedObject(importer);
            SerializedProperty platformData = serialized.FindProperty("m_PlatformData");

            if (platformData == null || !platformData.isArray)
                return false;

            bool changed = false;
            for (int i = 0; i < platformData.arraySize; i++)
            {
                SerializedProperty element = platformData.GetArrayElementAtIndex(i);
                SerializedProperty keyProp = element.FindPropertyRelative("first");
                if (keyProp == null || keyProp.stringValue != "Android")
                    continue;

                SerializedProperty valueProp = element.FindPropertyRelative("second");
                if (valueProp == null)
                    continue;

                SerializedProperty enabledProp = valueProp.FindPropertyRelative("m_Enabled");
                if (enabledProp != null && !enabledProp.boolValue)
                {
                    enabledProp.boolValue = true;
                    changed = true;
                }

                SerializedProperty settingsProp = valueProp.FindPropertyRelative("m_Settings");
                if (settingsProp != null && settingsProp.isArray)
                {
                    changed |= UpsertPlatformSetting(settingsProp, "CPU", androidCpu);
                    changed |= UpsertPlatformSetting(settingsProp, "AndroidCPU", androidCpu);
                    changed |= UpsertPlatformSetting(settingsProp, "OS", "Android");
                }
            }

            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return changed;
        }

        private static bool UpsertPlatformSetting(SerializedProperty settingsArray, string key, string value)
        {
            for (int i = 0; i < settingsArray.arraySize; i++)
            {
                SerializedProperty element = settingsArray.GetArrayElementAtIndex(i);
                SerializedProperty keyProp = element.FindPropertyRelative("first");
                SerializedProperty valueProp = element.FindPropertyRelative("second");

                if (keyProp != null && keyProp.stringValue == key)
                {
                    if (valueProp != null && valueProp.stringValue == value)
                        return false;

                    if (valueProp != null)
                    {
                        valueProp.stringValue = value;
                        return true;
                    }
                }
            }

            int insertIndex = settingsArray.arraySize;
            settingsArray.InsertArrayElementAtIndex(insertIndex);
            SerializedProperty newElement = settingsArray.GetArrayElementAtIndex(insertIndex);
            SerializedProperty newKey = newElement.FindPropertyRelative("first");
            SerializedProperty newValue = newElement.FindPropertyRelative("second");

            if (newKey != null)
                newKey.stringValue = key;
            if (newValue != null)
                newValue.stringValue = value;

            return true;
        }
    }
}
