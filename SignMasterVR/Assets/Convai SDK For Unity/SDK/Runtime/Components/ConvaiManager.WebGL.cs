/// <summary>
///     ConvaiManager partial: WebGL-specific voice start arming, gesture consumption,
///     pointer/touch input detection, and UI overlay checks.
/// </summary>

using System;
using System.Reflection;
using Convai.Domain.Logging;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Logging;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Convai.Runtime.Components
{
    public partial class ConvaiManager
    {
        private void UpdateWebGLVoiceStartArmState()
        {
            if (!ShouldArmWebGLVoiceStart())
            {
                _webGLVoiceStartArmed = false;
                return;
            }

            _webGLVoiceStartArmed = true;
        }

        private bool ShouldArmWebGLVoiceStart()
        {
            if (!CanArmWebGLVoiceStart(
                    _enableVoiceOnFirstSceneClickAfterConnectInWebGL,
                    UnityEngine.Application.platform,
                    TryGetRoomManager(out ConvaiRoomManager roomManager),
                    roomManager?.IsConnected ?? false,
                    roomManager?.RequiresUserGestureForAudio ?? false,
                    roomManager?.IsAudioPlaybackActive ?? false))
                return false;

            return true;
        }

        private void TryConsumeWebGLVoiceStartGesture()
        {
            if (!_webGLVoiceStartArmed) return;

            if (!ShouldArmWebGLVoiceStart())
            {
                _webGLVoiceStartArmed = false;
                return;
            }

            if (!ShouldConsumeWebGLVoiceStartGesture(WasScenePointerPressedThisFrame(), IsPointerOverUI())) return;

            _webGLVoiceStartArmed = false;

            if (_debugLogging)
            {
                ConvaiLogger.Debug(
                    "Consuming first WebGL scene interaction to enable audio playback and microphone capture.",
                    LogCategory.SDK);
            }

            EnableAudioAndStartListening();
        }

        private static bool CanArmWebGLVoiceStart(
            bool featureEnabled,
            RuntimePlatform platform,
            bool hasRoomManager,
            bool isConnected,
            bool requiresUserGestureForAudio,
            bool isAudioPlaybackActive)
        {
            return featureEnabled
                   && platform == RuntimePlatform.WebGLPlayer
                   && hasRoomManager
                   && isConnected
                   && requiresUserGestureForAudio
                   && !isAudioPlaybackActive;
        }

        private static bool ShouldConsumeWebGLVoiceStartGesture(bool pointerPressedThisFrame, bool pointerOverUi) =>
            pointerPressedThisFrame && !pointerOverUi;

        private static bool WasScenePointerPressedThisFrame()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0)) return true;

            for (int i = 0; i < Input.touchCount; i++)
            {
                if (Input.GetTouch(i).phase == TouchPhase.Began)
                    return true;
            }
#endif

#if ENABLE_INPUT_SYSTEM
            if (WasPressedThisFrame("UnityEngine.InputSystem.Mouse, Unity.InputSystem", "leftButton")) return true;

            return WasPressedThisFrame("UnityEngine.InputSystem.Touchscreen, Unity.InputSystem", "primaryTouch",
                "press");
#else
            return false;
#endif
        }

        private static bool WasPressedThisFrame(string typeName, params string[] memberPath)
        {
            var type = Type.GetType(typeName);
            if (type == null) return false;

            object device = type.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (device == null) return false;

            object current = device;
            for (int i = 0; i < memberPath.Length; i++)
            {
                current = current?.GetType().GetProperty(memberPath[i], BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(current);
                if (current == null) return false;
            }

            return current.GetType().GetProperty("wasPressedThisFrame", BindingFlags.Public | BindingFlags.Instance)
                       ?.GetValue(current) is bool wasPressed
                   && wasPressed;
        }

        private static bool IsPointerOverUI()
        {
            EventSystem current = EventSystem.current;
            if (current == null) return false;

#if ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began && current.IsPointerOverGameObject(touch.fingerId)) return true;
            }
#endif

            return current.IsPointerOverGameObject();
        }
    }
}
