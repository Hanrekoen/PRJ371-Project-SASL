#if UNITY_EDITOR_OSX && UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace Convai.Editor
{
    internal static class ConvaiIosBuildPostProcessor
    {
        private const string LiveKitLibraryComment = "liblivekit_ffi.a in Frameworks";

        private static readonly string[] RequiredFrameworks =
        {
            "OpenGLES.framework",
            "MetalKit.framework",
            "GLKit.framework",
            "VideoToolbox.framework",
            "Network.framework"
        };

        [PostProcessBuild]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            string projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            string unityFrameworkTargetGuid = project.GetUnityFrameworkTargetGuid();
            project.AddBuildProperty(unityFrameworkTargetGuid, "OTHER_LDFLAGS", "-ObjC");

            foreach (string framework in RequiredFrameworks)
            {
                project.AddFrameworkToProject(unityFrameworkTargetGuid, framework, false);
            }

            project.WriteToFile(projectPath);
            EnsureLiveKitLibraryIsLinkedFirst(projectPath, project.GetFrameworksBuildPhaseByTarget(unityFrameworkTargetGuid));

            Debug.Log($"[Convai] Added '-ObjC', linked iOS frameworks, and placed {LiveKitLibraryComment} first.");
        }

        private static void EnsureLiveKitLibraryIsLinkedFirst(string projectPath, string frameworksBuildPhaseGuid)
        {
            if (string.IsNullOrEmpty(projectPath) || string.IsNullOrEmpty(frameworksBuildPhaseGuid) || !File.Exists(projectPath))
                return;

            string projectContents = File.ReadAllText(projectPath);
            string phaseMarker = $"\t\t{frameworksBuildPhaseGuid} /* Frameworks */ = {{";
            int phaseStart = projectContents.IndexOf(phaseMarker);
            if (phaseStart < 0)
                return;

            int filesStart = projectContents.IndexOf("files = (", phaseStart);
            if (filesStart < 0)
                return;

            int listStart = projectContents.IndexOf('\n', filesStart);
            if (listStart < 0)
                return;

            listStart += 1;

            int listEnd = projectContents.IndexOf("\n\t\t\t);", listStart);
            if (listEnd < 0)
                return;

            string filesBlock = projectContents.Substring(listStart, listEnd - listStart);
            string[] entries = filesBlock.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);

            int liveKitIndex = FindEntryIndex(entries, LiveKitLibraryComment);
            if (liveKitIndex <= 0)
                return;

            string liveKitEntry = entries[liveKitIndex];
            var reorderedEntries = new System.Collections.Generic.List<string>(entries);
            reorderedEntries.RemoveAt(liveKitIndex);
            reorderedEntries.Insert(0, liveKitEntry);

            string reorderedBlock = string.Join("\n", reorderedEntries) + "\n";
            string updatedContents = projectContents.Substring(0, listStart) + reorderedBlock + projectContents.Substring(listEnd);
            File.WriteAllText(projectPath, updatedContents);
        }

        private static int FindEntryIndex(string[] entries, string libraryComment)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Contains(libraryComment))
                    return i;
            }

            return -1;
        }
    }
}
#endif
