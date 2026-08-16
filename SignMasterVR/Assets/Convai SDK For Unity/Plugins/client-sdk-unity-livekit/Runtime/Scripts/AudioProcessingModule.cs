using System;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;

namespace LiveKit
{
    /// <summary>
    /// Provides access to WebRTC audio processing features exposed by livekit-ffi.
    /// </summary>
    public sealed class AudioProcessingModule
    {
        public const uint FrameDurationMs = 10;

        internal readonly FfiHandle Handle;

        public AudioProcessingModule(
            bool echoCancellationEnabled,
            bool noiseSuppressionEnabled,
            bool highPassFilterEnabled,
            bool gainControllerEnabled)
        {
            using var request = FFIBridge.Instance.NewRequest<NewApmRequest>();
            NewApmRequest newApm = request.request;
            newApm.EchoCancellerEnabled = echoCancellationEnabled;
            newApm.NoiseSuppressionEnabled = noiseSuppressionEnabled;
            newApm.HighPassFilterEnabled = highPassFilterEnabled;
            newApm.GainControllerEnabled = gainControllerEnabled;

            using var response = request.Send();
            FfiResponse ffiResponse = response;
            Handle = FfiHandle.FromOwnedHandle(ffiResponse.NewApm.Apm.Handle);
        }

        public void ProcessStream(AudioFrame data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            using var request = FFIBridge.Instance.NewRequest<ApmProcessStreamRequest>();
            ApmProcessStreamRequest processStream = request.request;
            processStream.ApmHandle = (ulong)Handle.DangerousGetHandle();
            processStream.DataPtr = (ulong)data.Data.ToInt64();
            processStream.Size = (uint)data.Length;
            processStream.SampleRate = data.SampleRate;
            processStream.NumChannels = data.NumChannels;

            using var response = request.Send();
            FfiResponse ffiResponse = response;
            if (ffiResponse.ApmProcessStream.HasError)
                throw new InvalidOperationException(ffiResponse.ApmProcessStream.Error);
        }

        public void ProcessReverseStream(AudioFrame data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            using var request = FFIBridge.Instance.NewRequest<ApmProcessReverseStreamRequest>();
            ApmProcessReverseStreamRequest processReverseStream = request.request;
            processReverseStream.ApmHandle = (ulong)Handle.DangerousGetHandle();
            processReverseStream.DataPtr = (ulong)data.Data.ToInt64();
            processReverseStream.Size = (uint)data.Length;
            processReverseStream.SampleRate = data.SampleRate;
            processReverseStream.NumChannels = data.NumChannels;

            using var response = request.Send();
            FfiResponse ffiResponse = response;
            if (ffiResponse.ApmProcessReverseStream.HasError)
                throw new InvalidOperationException(ffiResponse.ApmProcessReverseStream.Error);
        }

        /// <summary>
        /// Sets the approximate delay in milliseconds between processed far-end playback and the captured echo.
        /// </summary>
        public void SetStreamDelayMs(int delayMs)
        {
            using var request = FFIBridge.Instance.NewRequest<ApmSetStreamDelayRequest>();
            ApmSetStreamDelayRequest setStreamDelay = request.request;
            setStreamDelay.ApmHandle = (ulong)Handle.DangerousGetHandle();
            setStreamDelay.DelayMs = delayMs;

            using var response = request.Send();
            FfiResponse ffiResponse = response;
            if (ffiResponse.ApmSetStreamDelay.HasError)
                throw new InvalidOperationException(ffiResponse.ApmSetStreamDelay.Error);
        }
    }
}
