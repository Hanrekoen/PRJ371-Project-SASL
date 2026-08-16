using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeVoiceProcessingSession : IDisposable
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const int AndroidRouteBluetoothBit = 1 << 0;
        private const int AndroidRouteWiredBit = 1 << 1;
        private const int AndroidRouteSpeakerBit = 1 << 2;
#endif

        private bool _isActive;

        public bool SupportsRouteMonitoring
        {
            get
            {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _audioManager;
        private int _previousMode;
        private bool _previousSpeakerphoneOn;
        private bool _speakerphoneWasForced;
#endif

        public void Start()
        {
            if (_isActive)
                return;

            bool started = true;
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                StartAndroid();
#elif UNITY_IOS && !UNITY_EDITOR
                started = StartIos();
#endif
            }
            catch (Exception ex)
            {
                started = false;
                Debug.LogWarning($"[Convai] Failed to enable mobile voice processing for AEC: {ex.Message}");
            }

            _isActive = started;
        }

        public void Stop()
        {
            if (!_isActive)
                return;

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                StopAndroid();
#elif UNITY_IOS && !UNITY_EDITOR
                StopIos();
#endif
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Convai] Failed to restore mobile audio session after AEC: {ex.Message}");
            }
            finally
            {
                _isActive = false;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        public int GetCurrentRouteSignature()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return GetAndroidRouteSignature();
#elif UNITY_IOS && !UNITY_EDITOR
            return GetIosRouteSignature();
#else
            return 0;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void StartAndroid()
        {
            if (!TryEnsureAudioManager())
                return;

            _previousMode = _audioManager.Call<int>("getMode");
            _previousSpeakerphoneOn = _audioManager.Call<bool>("isSpeakerphoneOn");

            int currentRouteSignature = GetAndroidRouteSignature();
            bool hasBluetoothRoute = (currentRouteSignature & AndroidRouteBluetoothBit) != 0;
            bool hasWiredRoute = (currentRouteSignature & AndroidRouteWiredBit) != 0;

            _audioManager.Call("setMode", 3); // MODE_IN_COMMUNICATION
            _speakerphoneWasForced = !hasBluetoothRoute && !hasWiredRoute;
            if (_speakerphoneWasForced)
                _audioManager.Call("setSpeakerphoneOn", true);
        }

        private void StopAndroid()
        {
            if (_audioManager == null)
                return;

            if (_speakerphoneWasForced)
                _audioManager.Call("setSpeakerphoneOn", _previousSpeakerphoneOn);

            _audioManager.Call("setMode", _previousMode);
            _speakerphoneWasForced = false;
        }

        private bool TryEnsureAudioManager()
        {
            if (_audioManager != null)
                return true;

            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            if (activity == null)
                return false;

            _audioManager = activity.Call<AndroidJavaObject>("getSystemService", "audio");
            return _audioManager != null;
        }

        private int GetAndroidRouteSignature()
        {
            if (!TryEnsureAudioManager())
                return 0;

            int signature = 0;
            if (_audioManager.Call<bool>("isBluetoothScoOn") || _audioManager.Call<bool>("isBluetoothA2dpOn"))
                signature |= AndroidRouteBluetoothBit;
            if (_audioManager.Call<bool>("isWiredHeadsetOn"))
                signature |= AndroidRouteWiredBit;
            if (_audioManager.Call<bool>("isSpeakerphoneOn"))
                signature |= AndroidRouteSpeakerBit;

            signature |= _audioManager.Call<int>("getMode") << 8;
            return signature;
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern bool Convai_AecVoiceProcessingSession_Begin();

        [DllImport("__Internal")]
        private static extern void Convai_AecVoiceProcessingSession_End();

        [DllImport("__Internal")]
        private static extern int Convai_AecVoiceProcessingSession_GetRouteSignature();

        private static bool StartIos()
        {
            if (!Convai_AecVoiceProcessingSession_Begin())
            {
                Debug.LogWarning("[Convai] iOS voice-processing session could not be enabled for AEC.");
                return false;
            }

            return true;
        }

        private static void StopIos()
        {
            Convai_AecVoiceProcessingSession_End();
        }

        private static int GetIosRouteSignature() => Convai_AecVoiceProcessingSession_GetRouteSignature();
#endif
    }
}
