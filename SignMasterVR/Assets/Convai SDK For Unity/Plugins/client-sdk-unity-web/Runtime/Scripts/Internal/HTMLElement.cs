using System;
using UnityEngine.Scripting;

namespace LiveKit
{
    public abstract class HTMLElement : JSObject
    {
        internal static Func<JSHandle, string, JSNative.JSDelegate, JSHandle, JSRef> EventListenerRegistrar =
            RegisterEventListener;

        internal static Action<JSHandle, string, JSRef> EventListenerRemover = UnregisterEventListener;

        [Preserve]
        internal HTMLElement(JSHandle handle) : base(handle)
        {
        
        }

        internal JSRef AddEventListener(string e, JSNative.JSDelegate callback, JSHandle identifier = null)
        {
            if (identifier == null)
                identifier = NativeHandle;

            return EventListenerRegistrar(NativeHandle, e, callback, identifier);
        }

        internal void RemoveEventListener(string e, JSRef listenerRef)
        {
            if (listenerRef == null)
                return;

            EventListenerRemover(NativeHandle, e, listenerRef);
        }

        private static JSRef RegisterEventListener(JSHandle elementHandle, string e, JSNative.JSDelegate callback,
            JSHandle identifier)
        {
            var listenerRef = new JSRef();
            SetKeepAlive(listenerRef, true);

            JSNative.PushFunction(identifier, callback, $"HTMLElement - EventListener: {e}");
            JSNative.SetRef(listenerRef.NativeHandle);
            JSNative.PushString(e);
            JSNative.PushObject(listenerRef.NativeHandle);
            JSNative.CallMethod(elementHandle, "addEventListener");

            return listenerRef;
        }

        private static void UnregisterEventListener(JSHandle elementHandle, string e, JSRef listenerRef)
        {
            JSNative.PushString(e);
            JSNative.PushObject(listenerRef.NativeHandle);
            JSNative.CallMethod(elementHandle, "removeEventListener");
            SetKeepAlive(listenerRef, false);
        }

        internal static void ResetEventListenerHooks()
        {
            EventListenerRegistrar = RegisterEventListener;
            EventListenerRemover = UnregisterEventListener;
        }
    }
}
