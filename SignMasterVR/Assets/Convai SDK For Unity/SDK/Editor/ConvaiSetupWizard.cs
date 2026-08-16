using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Editor.Utilities;
using Convai.Runtime;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;

namespace Convai.Editor
{
    /// <summary>
    ///     Editor wizard for setting up required Convai SDK components in a scene.
    ///     Provides menu items to add missing components and validate scene setup.
    /// </summary>
    public static class ConvaiSetupWizard
    {
        private const string MenuPath = "GameObject/Convai/";

        /// <summary>
        ///     Adds all required Convai SDK components to the scene if they are missing.
        /// </summary>
        [MenuItem(MenuPath + "Setup Required Components", false, 10)]
        public static void SetupRequiredComponents()
        {
            ConvaiSceneSetupApi.BootstrapResult bootstrap = ConvaiSceneSetupApi.BootstrapScene();
            bool addedAny = bootstrap.AddedManager || bootstrap.AddedRoomManager;

            if (addedAny)
            {
                EditorUtility.DisplayDialog(
                    "Convai Setup Complete",
                    "Required Convai SDK components have been added to the scene.\n\n" +
                    "Next steps:\n" +
                    "1. Configure your API key:\n" +
                    "   Edit > Project Settings > Convai SDK\n\n" +
                    "2. Add ConvaiCharacter to your Characters:\n" +
                    "   Select Character > Add Component > Convai Character\n\n" +
                    "3. Add ConvaiPlayer to your player:\n" +
                    "   Select Player > Add Component > Convai Player",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Convai Setup",
                    "All required components are already in the scene!\n\n" +
                    "Your scene has:\n" +
                    $"{Glyphs.Status.Ok} ConvaiManager\n" +
                    $"{Glyphs.Status.Ok} ConvaiRoomManager",
                    "OK");
            }
        }

        /// <summary>
        ///     Validates the current scene setup and reports any issues.
        /// </summary>
        [MenuItem(MenuPath + "Validate Scene Setup", false, 11)]
        public static void ValidateSceneSetup()
        {
            // Offered before the report, and as a button rather than a sentence: this one is fixable
            // in place, and the alternative is a NullReferenceException on scene open that names
            // TextMeshPro and never mentions the missing import.
            if (!ConvaiTextMeshProEssentials.AreImported) OfferTextMeshProEssentialsImport();

            ConvaiSceneSetupApi.ValidationReport report = ConvaiSceneSetupApi.ValidateCurrentScene();
            ConvaiCharacter[] characters = ConvaiObjectFind.All<ConvaiCharacter>(FindObjectsInactive.Exclude);

            if (report.IsSuccess && report.Warnings.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    $"Validation Passed {Glyphs.Status.Ok}",
                    "Scene setup is correct!\n\n" +
                    $"Found {characters.Length} ConvaiCharacter(s) in scene.",
                    "OK");
                ConvaiLogger.Debug("[Convai Validation] Scene setup is correct.", LogCategory.Editor);
            }
            else
            {
                string message = "";

                if (report.Errors.Count > 0)
                    message += $"{Glyphs.Status.Fail} ERRORS (must fix):\n• " + string.Join("\n• ", report.Errors) + "\n\n";

                if (report.Warnings.Count > 0)
                    message += $"{Glyphs.Status.Warn} WARNINGS:\n• " + string.Join("\n• ", report.Warnings) + "\n\n";

                if (report.NextSteps.Count > 0)
                    message += "How to fix:\n• " + string.Join("\n• ", report.NextSteps);

                EditorUtility.DisplayDialog(
                    report.Errors.Count > 0 ? "Validation Failed" : "Validation Warnings",
                    message,
                    "OK");

                if (report.Errors.Count > 0)
                    ConvaiLogger.Error("[Convai Validation] " + message, LogCategory.Editor);
                else
                    ConvaiLogger.Warning("[Convai Validation] " + message, LogCategory.Editor);
            }
        }

        /// <summary>
        ///     Prompts to import TextMesh Pro's Essential Resources, which Convai's shipped UI
        ///     prefabs and font assets depend on and which Unity only unpacks on request.
        /// </summary>
        private static void OfferTextMeshProEssentialsImport()
        {
            bool importNow = EditorUtility.DisplayDialog(
                "TextMesh Pro Resources Missing",
                "Convai's UI prefabs and fonts use TextMesh Pro, whose runtime shaders and default " +
                "font are imported per project rather than shipped in a package.\n\n" +
                "Without them, opening a scene that contains Convai UI throws a " +
                "NullReferenceException inside TextMeshPro.\n\n" +
                "Import them now?",
                "Import",
                "Not Now");

            if (!importNow) return;

            if (ConvaiTextMeshProEssentials.TryImport()) return;

            // The importer is a Unity menu item, so it can move. Say what to do rather than
            // reporting a success that did not happen.
            ConvaiLogger.Warning(
                "[Convai Setup] Could not start TextMesh Pro's importer automatically. " +
                ConvaiTextMeshProEssentials.ImportInstruction,
                LogCategory.Editor);
        }

        /// <summary>
        ///     Opens the Convai SDK documentation in a browser.
        /// </summary>
        /// <remarks>
        ///     Lives on the <c>Convai</c> menu rather than under <c>GameObject</c>: the GameObject
        ///     menu creates and configures objects in the open scene, and a documentation link is
        ///     neither. The two entries that remain under <c>GameObject/Convai</c> do act on the
        ///     scene, which is why they stayed.
        /// </remarks>
        [MenuItem("Convai/Documentation", false, Convai.Editor.UI.ConvaiEditorMenu.Configuration + 2)]
        public static void OpenDocumentation() =>
            UnityEngine.Application.OpenURL(ConvaiEditorLinks.DocsUnityQuickstartUrl);

        /// <summary>
        ///     Opens the Convai SDK settings in Project Settings.
        /// </summary>
        /// <remarks>
        ///     No menu entry of its own: <c>Convai &gt; Settings</c> already opens the settings
        ///     surface, and two menu paths onto one destination is the duplication this menu was
        ///     cleaned up to remove. Kept public because it is a useful entry point for tooling.
        /// </remarks>
        public static void OpenSDKSettings() => SettingsService.OpenProjectSettings("Project/Convai SDK");
    }
}
