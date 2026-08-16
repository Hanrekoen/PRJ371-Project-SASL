#if CONVAI_ENABLE_SERVER_ANIMATION
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.RestAPI;
using Convai.RestAPI.Internal;
using Convai.Runtime.Logging;
using Convai.Runtime;
using UnityEditor;
using UnityEngine;
using UnityApplication = UnityEngine.Application;

namespace Convai.Editor.ConfigurationWindow.Components.Sections.ServerAnimation
{
    /// <summary>
    /// Service for importing server animations.
    /// Uses ConvaiSettings for API key access.
    /// </summary>
    internal static class ServerAnimationService
    {
        private const string DIRECTORY_SAVE_KEY = "CONVAI_SERVER_ANIMATION_SAVE_PATH";
        private static string _savePath;
        private static int _downloadCount;
        private static int _totalDownloads;

        /// <summary>
        /// Imports server animations by downloading FBX files from the Convai backend.
        /// Runs asynchronously since this is an Editor operation triggered by UI callbacks.
        /// </summary>
        /// <param name="animations">List of animations to import.</param>
        /// <param name="onSuccess">Callback invoked when all animations are successfully imported.</param>
        /// <param name="onError">Callback invoked if an error occurs during import.</param>
        /// <remarks>
        /// This method is designed for Unity Editor UI callbacks where the caller cannot await the result.
        /// Errors are handled via the onError callback.
        /// </remarks>
        public static void ImportAnimations(List<ServerAnimationItemResponse> animations, Action onSuccess, Action<string> onError)
        {
            _ = ImportAnimationsAsync(animations, onSuccess, onError);
        }

        private static async Task ImportAnimationsAsync(List<ServerAnimationItemResponse> animations, Action onSuccess, Action<string> onError)
        {
            ConvaiSettings settings = ConvaiSettings.Instance;
            if (settings == null || !settings.HasApiKey)
            {
                ConvaiLogger.Error("ConvaiSettings not found or API key not set.", LogCategory.Editor);
                onError?.Invoke("ConvaiSettings not found or API key not set.");
                return;
            }

            if (animations.Count == 0)
            {
                EditorUtility.DisplayDialog("Import Animation Process", "Cannot start import process since no animations are selected", "Ok");
                onError?.Invoke("No animations selected");
                return;
            }

            _savePath = UpdateAnimationSavePath();
            if (string.IsNullOrEmpty(_savePath))
            {
                EditorUtility.DisplayDialog("Failed", "Import Operation Cancelled", "Ok");
                onError?.Invoke("No animations selected");
                return;
            }

            List<string> allAnimations = animations.Select(x => x.AnimationName).ToList();
            List<string> successfulImports = new();
            List<string> failedImports = new();
            _totalDownloads = animations.Count;
            _downloadCount = 0;
            EditorUtility.DisplayProgressBar("Importing Animations", "Downloading Animations", 0f);
            ConvaiRestClientOptions options = ConvaiRestOptionsFactory.Create(settings.ApiKey);
            using ConvaiRestClient client = new ConvaiRestClient(options);

            foreach (ServerAnimationItemResponse anim in animations)
            {
                try
                {
                    ServerAnimationDataResponse data = await client.Animations.GetAsync(anim.AnimationID);
                    byte[] bytes = await client.DownloadFileAsync(data.Animation.FbxGcpFile);
                    successfulImports.Add(anim.AnimationName);
                    await SaveAnimationAsync(bytes, anim.AnimationName);
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(ex.Message, LogCategory.REST);
                    failedImports.Add(anim.AnimationName);
                }
                finally
                {
                    _downloadCount++;
                    EditorUtility.DisplayProgressBar(
                        "Importing Animations",
                        $"Downloading Animations {_downloadCount}/{_totalDownloads}",
                        (float)_downloadCount / _totalDownloads);
                }
            }

            EditorUtility.ClearProgressBar();
            LogResult(successfulImports, allAnimations);
            AssetDatabase.Refresh();
            if (failedImports.Count > 0)
            {
                onError?.Invoke($"Failed to import: {string.Join(", ", failedImports)}");
            }
            else
            {
                onSuccess?.Invoke();
            }
        }

        private static void LogResult(List<string> successfulImports, List<string> animPaths)
        {
            string dialogMessage = $"Successfully Imported{Environment.NewLine}";
            successfulImports.ForEach(x => dialogMessage += x + Environment.NewLine);
            List<string> unSuccessFullImports = animPaths.Except(successfulImports).ToList();
            if (unSuccessFullImports.Count > 0)
            {
                dialogMessage += $"Could not import{Environment.NewLine}";
                unSuccessFullImports.ForEach(x => dialogMessage += x + Environment.NewLine);
            }

            EditorUtility.DisplayDialog("Import Animation Result", dialogMessage, "Ok");
        }

        private static string UpdateAnimationSavePath()
        {
            string selectedPath;
            string currentPath = EditorPrefs.GetString(DIRECTORY_SAVE_KEY, UnityApplication.dataPath);
            while (true)
            {
                selectedPath = EditorUtility.OpenFolderPanel("Select folder within project", currentPath, "");
                if (string.IsNullOrEmpty(selectedPath))
                {
                    selectedPath = string.Empty;
                    break;
                }

                if (!IsSubfolder(selectedPath, UnityApplication.dataPath))
                {
                    EditorUtility.DisplayDialog("Invalid Folder Selected", "Please select a folder within the project", "Ok");
                    continue;
                }

                EditorPrefs.SetString(DIRECTORY_SAVE_KEY, selectedPath);
                break;
            }

            return selectedPath;
        }

        private static bool IsSubfolder(string pathA, string pathB)
        {
            string fullPathA = Path.GetFullPath(pathA);
            string fullPathB = Path.GetFullPath(pathB);

            Uri uriA = new(fullPathA);
            Uri uriB = new(fullPathB);

            return uriB.IsBaseOf(uriA);
        }

        private static async Task SaveAnimationAsync(byte[] bytes, string newFileName)
        {
            if (!Directory.Exists(_savePath))
            {
                Directory.CreateDirectory(_savePath);
            }

            string filePath = Path.Combine(_savePath, $"{newFileName}.fbx");
            int counter = 1;

            while (File.Exists(filePath))
            {
                filePath = Path.Combine(_savePath, $"{newFileName}_{counter}.fbx");
                counter++;
            }

            await File.WriteAllBytesAsync(filePath, bytes);
            string relativePath = filePath.Substring(UnityApplication.dataPath.Length + 1).Replace('\\', '/');
            relativePath = "Assets/" + relativePath;
            AssetDatabase.Refresh();
            ModelImporter importer = AssetImporter.GetAtPath(relativePath) as ModelImporter;
            if (importer != null)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.importAnimatedCustomProperties = true;
                importer.materialLocation = ModelImporterMaterialLocation.External;
                importer.SaveAndReimport();
            }
            else
            {
                ConvaiLogger.Error("Failed to get importer for " + filePath, LogCategory.Editor);
            }
        }
    }
}
#endif
