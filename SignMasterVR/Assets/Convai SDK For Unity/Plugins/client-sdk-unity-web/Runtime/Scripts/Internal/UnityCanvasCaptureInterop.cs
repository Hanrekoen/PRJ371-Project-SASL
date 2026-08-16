using System;

namespace LiveKit
{
    internal static class UnityCanvasCaptureInterop
    {
        private const string UnityCanvasId = "unity-canvas";
        private const string AttachedStreamPropertyName = "__lkUnityCanvasStream";

        internal static MediaStreamTrack CaptureVideoTrack(int targetFrameRate)
        {
#if !UNITY_EDITOR && UNITY_WEBGL
            JSHandle canvas = GetUnityCanvasHandle();
            if (!IsUsableObject(canvas))
            {
                throw new InvalidOperationException(
                    "Browser canvas capture is unavailable. Ensure the Unity WebGL canvas is present in the DOM.");
            }

            JSHandle stream = CaptureCanvasStream(canvas, targetFrameRate);
            if (!IsUsableObject(stream))
            {
                throw new InvalidOperationException(
                    "Browser canvas capture is unavailable. Ensure the Unity WebGL canvas supports captureStream().");
            }

            JSHandle videoTracks = JSNative.CallMethod(stream, "getVideoTracks");
            int videoTrackCount = GetArrayLength(videoTracks);
            if (videoTrackCount < 1)
            {
                throw new InvalidOperationException("Canvas capture returned no video tracks.");
            }

            JSHandle trackHandle = GetArrayItem(videoTracks, 0);
            if (!IsUsableObject(trackHandle))
            {
                throw new InvalidOperationException("Canvas capture returned an invalid video track.");
            }

            AttachStream(trackHandle, stream);
            return JSRef.Acquire<MediaStreamTrack>(trackHandle);
#else
            throw new PlatformNotSupportedException("Unity canvas capture is only available in WebGL player builds.");
#endif
        }

        internal static void StopVideoTrack(MediaStreamTrack track)
        {
#if !UNITY_EDITOR && UNITY_WEBGL
            if (track == null)
            {
                return;
            }

            JSHandle stream = GetAttachedStream(track.NativeHandle);
            if (IsUsableObject(stream))
            {
                JSHandle streamTracks = JSNative.CallMethod(stream, "getTracks");
                int trackCount = GetArrayLength(streamTracks);
                for (int i = 0; i < trackCount; i++)
                {
                    JSHandle streamTrack = GetArrayItem(streamTracks, i);
                    if (IsUsableObject(streamTrack))
                    {
                        JSNative.CallMethod(streamTrack, "stop");
                    }
                }

                ClearAttachedStream(track.NativeHandle);
                return;
            }

            track.Stop();
#else
            if (track == null)
            {
                return;
            }
#endif
        }

#if !UNITY_EDITOR && UNITY_WEBGL
        private static JSHandle CaptureCanvasStream(JSHandle canvas, int targetFrameRate)
        {
            JSNative.PushString("captureStream");
            JSHandle captureStreamMethod = JSNative.GetProperty(canvas);
            if (captureStreamMethod == null || captureStreamMethod.IsClosed || captureStreamMethod.IsInvalid || JSNative.IsNull(captureStreamMethod) || JSNative.IsUndefined(captureStreamMethod))
            {
                return null;
            }

            if (targetFrameRate > 0)
            {
                JSNative.PushNumber(targetFrameRate);
            }

            return JSNative.CallMethod(canvas, "captureStream");
        }

        private static JSHandle GetUnityCanvasHandle()
        {
            JSHandle document = GetWindowProperty("document");
            if (!IsUsableObject(document))
            {
                return null;
            }

            JSNative.PushString(UnityCanvasId);
            JSHandle canvas = JSNative.CallMethod(document, "getElementById");
            if (IsUsableObject(canvas))
            {
                return canvas;
            }

            JSNative.PushString("canvas");
            return JSNative.CallMethod(document, "querySelector");
        }

        private static JSHandle GetWindowProperty(string propertyName)
        {
            JSNative.PushString(propertyName);
            return JSNative.GetProperty(JSNative.Window);
        }

        private static JSHandle GetAttachedStream(JSHandle trackHandle)
        {
            JSNative.PushString(AttachedStreamPropertyName);
            return JSNative.GetProperty(trackHandle);
        }

        private static void AttachStream(JSHandle trackHandle, JSHandle streamHandle)
        {
            JSNative.PushString(AttachedStreamPropertyName);
            JSNative.PushObject(streamHandle);
            JSNative.SetProperty(trackHandle);
        }

        private static void ClearAttachedStream(JSHandle trackHandle)
        {
            JSNative.PushString(AttachedStreamPropertyName);
            JSNative.PushNull();
            JSNative.SetProperty(trackHandle);
        }

        private static int GetArrayLength(JSHandle arrayHandle)
        {
            if (!IsUsableObject(arrayHandle))
            {
                return 0;
            }

            JSNative.PushString("length");
            JSHandle lengthHandle = JSNative.GetProperty(arrayHandle);
            return lengthHandle != null && JSNative.IsNumber(lengthHandle)
                ? (int)JSNative.GetNumber(lengthHandle)
                : 0;
        }

        private static JSHandle GetArrayItem(JSHandle arrayHandle, int index)
        {
            JSNative.PushNumber(index);
            return JSNative.GetProperty(arrayHandle);
        }

        private static bool IsUsableObject(JSHandle handle)
        {
            return handle != null &&
                !handle.IsClosed &&
                !handle.IsInvalid &&
                !JSNative.IsNull(handle) &&
                !JSNative.IsUndefined(handle) &&
                JSNative.IsObject(handle);
        }
#endif
    }
}
