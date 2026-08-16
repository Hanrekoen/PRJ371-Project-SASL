#if UNITY_EDITOR
using System;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Runtime.Vision.Sources;
using Convai.Shared.Types;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Editor-only partial class for ConvaiRoomManager.
    ///     Contains vision component auto-setup workflow that runs in the editor only.
    /// </summary>
    public partial class ConvaiRoomManager
    {
        private const string VisionRootName = "ConvaiVisionRoot";

        private const string VideoPublisherTypeName =
            "Convai.Modules.Vision.ConvaiVisionPublisher, Convai.Modules.Vision";

        // Non-serialized editor-only field
        private bool _visionSetupQueued;

        private void OnValidate()
        {
            if (UnityEngine.Application.isPlaying) return;

            EnsureConfigurationSourceInitialized();

            ConvaiConnectionType effectiveConnectionType = EffectiveConnectionType;
            // Gate on the resolved vision capability, not the raw connection type: a Disabled+Video
            // room (legacy native-video path) must not be nagged about dynamic-vision components.
            if (!EffectiveVisionContextEnabled)
            {
                _lastValidatedConnectionType = effectiveConnectionType;
                _visionSetupPrompted = false;
                _visionSetupQueued = false;
                return;
            }

            bool connectionTypeChanged = _lastValidatedConnectionType != effectiveConnectionType;
            _lastValidatedConnectionType = effectiveConnectionType;

            (bool hasPublisher, bool hasFrameSource) = GetVisionComponentFlags();
            if (hasPublisher && hasFrameSource)
            {
                _visionSetupPrompted = false;
                _visionSetupQueued = false;
                return;
            }

            if (_visionSetupPrompted && !connectionTypeChanged) return;

            _visionSetupPrompted = true;
            QueueVisionSetupPrompt();
        }

        private void QueueVisionSetupPrompt()
        {
            if (_visionSetupQueued) return;

            _visionSetupQueued = true;
            EditorApplication.delayCall += () =>
            {
                _visionSetupQueued = false;
                if (this == null) return;

                if (UnityEngine.Application.isPlaying || !EffectiveVisionContextEnabled) return;

                (bool hasPublisher, bool hasFrameSource) = GetVisionComponentFlags();
                if (hasPublisher && hasFrameSource)
                {
                    _visionSetupPrompted = false;
                    return;
                }

                bool addComponents = EditorUtility.DisplayDialog(
                    "Convai Vision Setup",
                    "Dynamic vision context requires a video publisher and frame source.\n\nAdd ConvaiVisionPublisher and CameraVisionFrameSource under this ConvaiRoomManager?",
                    "Add Components",
                    "Later");

                if (addComponents)
                {
                    if (!TryAutoAddVisionComponents()) LogVisionSetupWarning();
                }
                else
                    LogVisionSetupWarning();
            };
        }

        private bool TryAutoAddVisionComponents()
        {
            (bool hasPublisher, bool hasFrameSource) = GetVisionComponentFlags();
            if (hasPublisher && hasFrameSource) return true;

            GameObject visionRoot = GetOrCreateVisionRoot();
            if (visionRoot == null) return false;

            bool success = true;
            if (!hasPublisher)
            {
                var videoPublisherType = Type.GetType(VideoPublisherTypeName);
                if (videoPublisherType == null)
                {
                    ConvaiLogger.Warning(
                        "ConvaiVisionPublisher type not found. Ensure Convai.Modules.Vision is installed.",
                        LogCategory.Vision);
                    success = false;
                }
                else if (visionRoot.GetComponent(videoPublisherType) == null)
                {
                    Component addedPublisher = Undo.AddComponent(visionRoot, videoPublisherType);
                    if (addedPublisher == null) success = false;
                }
            }

            if (!hasFrameSource && visionRoot.GetComponent<CameraVisionFrameSource>() == null)
            {
                Component addedFrameSource = Undo.AddComponent<CameraVisionFrameSource>(visionRoot);
                if (addedFrameSource == null) success = false;
            }

            EditorUtility.SetDirty(visionRoot);
            if (gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(gameObject.scene);

            return success;
        }

        private GameObject GetOrCreateVisionRoot()
        {
            Transform existing = transform.Find(VisionRootName);
            if (existing != null) return existing.gameObject;

            var visionRoot = new GameObject(VisionRootName);
            Undo.RegisterCreatedObjectUndo(visionRoot, "Create Convai Vision Root");
            Undo.SetTransformParent(visionRoot.transform, transform, "Parent Convai Vision Root");
            visionRoot.transform.localPosition = Vector3.zero;
            visionRoot.transform.localRotation = Quaternion.identity;
            visionRoot.transform.localScale = Vector3.one;
            return visionRoot;
        }

        private void LogVisionSetupWarning()
        {
            ConvaiLogger.Warning(
                "Dynamic vision context is enabled, but vision components are missing. Add ConvaiVisionPublisher and CameraVisionFrameSource (or another IVisionFrameSource) under this ConvaiRoomManager to enable vision streaming.",
                LogCategory.Vision);
        }
    }
}
#endif
