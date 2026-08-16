using System;
using UnityEngine.Scripting;

namespace LiveKit
{

    public class MediaStreamTrack : JSObject
    {

        [Preserve]
        internal MediaStreamTrack(JSHandle handle) : base(handle) 
        {

        }

        public void Stop()
        {
            JSNative.CallMethod(NativeHandle, "stop");
        }
    }
}
