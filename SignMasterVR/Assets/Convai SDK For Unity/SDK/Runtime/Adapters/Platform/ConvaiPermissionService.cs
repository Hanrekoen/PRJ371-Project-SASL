using System;
using Convai.Shared.Abstractions;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

#if UNITY_IOS
using System.Collections;
using UnityEngine;
#endif

namespace Convai.Runtime.Adapters.Platform
{
    /// <summary>
    ///     Platform-agnostic permission service for audio and related capabilities.
    ///     Implements the shared IConvaiPermissionService interface for cross-assembly compatibility.
    /// </summary>
    internal class ConvaiPermissionService : IConvaiPermissionService
    {
        /// <inheritdoc />
        public bool HasMicrophonePermission()
        {
#if UNITY_ANDROID
            return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#elif UNITY_IOS
            return UnityEngine.Application.HasUserAuthorization(UserAuthorization.Microphone);
#else
            return true;
#endif
        }

        /// <inheritdoc />
        public void RequestMicrophonePermission(Action<bool> callback)
        {
#if UNITY_ANDROID
            PermissionCallbacks permissionCallbacks = new();
            permissionCallbacks.PermissionGranted += str => callback(true);
            permissionCallbacks.PermissionDenied += str => callback(false);
            Permission.RequestUserPermission(Permission.Microphone, permissionCallbacks);
#elif UNITY_IOS
            PermissionRequester.RequestMicrophone(callback);
#else
            callback?.Invoke(true);
#endif
        }

#if UNITY_IOS
        private sealed class PermissionRequester : MonoBehaviour
        {
            public static void RequestMicrophone(Action<bool> callback)
            {
                GameObject go = new("ConvaiPermissionService");
                DontDestroyOnLoad(go);
                PermissionRequester requester = go.AddComponent<PermissionRequester>();
                requester.StartCoroutine(requester.RequestCoroutine(callback));
            }

            private IEnumerator RequestCoroutine(Action<bool> callback)
            {
                yield return UnityEngine.Application.RequestUserAuthorization(UserAuthorization.Microphone);
                bool granted = UnityEngine.Application.HasUserAuthorization(UserAuthorization.Microphone);
                callback?.Invoke(granted);
                Destroy(gameObject);
            }
        }
#endif
    }
}
