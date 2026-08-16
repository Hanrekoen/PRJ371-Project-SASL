using System.Collections.Generic;
using Convai.Editor.Utilities;
using Convai.Runtime;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using Convai.Runtime.Vision.Sources;
using Convai.Shared.Compatibility;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor
{
    /// <summary>
    ///     Structured scene setup and validation for editor automation (MCP, tests) without modal dialogs.
    /// </summary>
    public static class ConvaiSceneSetupApi
    {
        public sealed class ValidationReport
        {
            public List<string> Errors { get; } = new();
            public List<string> Warnings { get; } = new();
            public List<string> NextSteps { get; } = new();
            public bool IsSuccess => Errors.Count == 0;
        }

        public sealed class BootstrapResult
        {
            public bool AddedManager;
            public bool AddedRoomManager;
            public string ManagerObjectName;
            public List<string> ActionsTaken { get; } = new();
        }

        public static ValidationReport ValidateCurrentScene()
        {
            var report = new ValidationReport();

            ConvaiManager[] managers = ConvaiObjectFind.All<ConvaiManager>(FindObjectsInactive.Include);
            if (managers.Length == 0)
                report.Errors.Add("Missing ConvaiManager");

            ConvaiRoomManager[] roomManagers = ConvaiObjectFind.All<ConvaiRoomManager>(FindObjectsInactive.Include);
            if (roomManagers.Length == 0)
                report.Errors.Add("Missing ConvaiRoomManager");

            ConvaiSettings settings = ConvaiSettings.Instance;
            if (settings == null || !settings.HasApiKey)
                report.Warnings.Add("API key not configured (Edit > Project Settings > Convai SDK)");

            // A project condition rather than a scene one, but it belongs in the same report: it is
            // the difference between Convai's UI rendering and it throwing on scene open, and a
            // first-time user has no way to guess it from the symptom.
            if (!ConvaiTextMeshProEssentials.AreImported)
            {
                report.Errors.Add(
                    "TextMesh Pro Essential Resources are not imported. Convai's UI prefabs and " +
                    "fonts reference them, and a scene containing Convai UI throws on open without them.");
                report.NextSteps.Add(ConvaiTextMeshProEssentials.ImportInstruction);
            }

            ConvaiCharacter[] characters = ConvaiObjectFind.All<ConvaiCharacter>(FindObjectsInactive.Include);
            if (characters.Length == 0)
                report.Errors.Add("No ConvaiCharacter components found in scene");
            else
            {
                foreach (ConvaiCharacter character in characters)
                {
                    if (string.IsNullOrWhiteSpace(character.CharacterId))
                        report.Errors.Add(
                            $"ConvaiCharacter on '{character.gameObject.name}' has no Character ID");
                }
            }

            ConvaiPlayer[] players = ConvaiObjectFind.All<ConvaiPlayer>(FindObjectsInactive.Include);
            if (players.Length == 0)
                report.Errors.Add("No ConvaiPlayer component found in scene");

            foreach (ConvaiRoomManager room in roomManagers)
            {
                if (room.EffectiveConnectionType != ConvaiConnectionType.Video)
                    continue;

                bool hasPublisher = false;
                bool hasFrameSource = false;
                foreach (UnityEngine.Component c in room.GetComponentsInChildren<UnityEngine.Component>(true))
                {
                    if (c is IVisionPublisher)
                        hasPublisher = true;
                    if (c is IVisionFrameSource)
                        hasFrameSource = true;
                }

                if (!hasPublisher || !hasFrameSource)
                    report.Warnings.Add(
                        $"Video mode: ConvaiRoomManager on '{room.gameObject.name}' should have a vision publisher and frame source under its GameObject hierarchy (see Vision module README).");
            }

            if (report.Errors.Count > 0)
            {
                foreach (string issue in report.Errors)
                {
                    if (issue.Contains("ConvaiManager") &&
                        !report.NextSteps.Contains("Run Convai.BootstrapScene or GameObject > Convai > Setup Required Components"))
                        report.NextSteps.Add("Run Convai.BootstrapScene or GameObject > Convai > Setup Required Components");
                    if (issue.Contains("ConvaiCharacter"))
                        report.NextSteps.Add("Add ConvaiCharacter to NPC objects and set Character ID");
                    if (issue.Contains("ConvaiPlayer"))
                        report.NextSteps.Add("Add ConvaiPlayer to an explicit player GameObject");
                }
            }

            return report;
        }

        public static BootstrapResult BootstrapScene()
        {
            var result = new BootstrapResult();
            Undo.SetCurrentGroupName("Convai MCP Bootstrap");
            int undoGroup = Undo.GetCurrentGroup();

            ConvaiManager[] managers = ConvaiObjectFind.All<ConvaiManager>(FindObjectsInactive.Include);
            GameObject managerGo;
            if (managers.Length == 0)
            {
                managerGo = new GameObject("[Convai Manager]");
                Undo.RegisterCreatedObjectUndo(managerGo, "Create ConvaiManager");
                Undo.AddComponent<ConvaiManager>(managerGo);
                result.AddedManager = true;
                result.ActionsTaken.Add("Created GameObject with ConvaiManager");
            }
            else
            {
                managerGo = managers[0].gameObject;
            }

            result.ManagerObjectName = managerGo != null ? managerGo.name : null;

            if (managerGo != null && managerGo.GetComponent<ConvaiRoomManager>() == null)
            {
                Undo.AddComponent<ConvaiRoomManager>(managerGo);
                result.AddedRoomManager = true;
                result.ActionsTaken.Add("Added ConvaiRoomManager to manager object");
            }

            Undo.CollapseUndoOperations(undoGroup);
            return result;
        }

        /// <summary>
        ///     Returns the path to the com.convai.convai-sdk-for-unity package root (POSIX, Unity-style).
        /// </summary>
        public static bool TryGetConvaiSdkPackageRoot(out string packageRoot)
        {
            packageRoot = null;
            string[] guids = AssetDatabase.FindAssets("ConvaiSceneSetupApi t:MonoScript");
            if (guids == null || guids.Length == 0)
                return false;

            string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            if (string.IsNullOrEmpty(scriptPath))
                return false;

            // .../Packages/com.convai.convai-sdk-for-unity/SDK/Editor/ConvaiSceneSetupApi.cs
            string editorDir = System.IO.Path.GetDirectoryName(scriptPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(editorDir))
                return false;

            string sdkDir = System.IO.Path.GetDirectoryName(editorDir)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(sdkDir))
                return false;

            packageRoot = System.IO.Path.GetDirectoryName(sdkDir)?.Replace('\\', '/');
            return !string.IsNullOrEmpty(packageRoot);
        }
    }
}
