using Convai.Shared.Compatibility;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Editor.Compatibility
{
    /// <summary>
    ///     The editor half of Convai's object-identity seam. Resolves an id produced by
    ///     <c>ConvaiObjectId.Of</c> back to its object, and additionally resolves <b>historical
    ///     <c>int</c> instance IDs</b> that the runtime half cannot.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>This is narrower than it looks, and the narrowness is the point.</b> The obvious
    ///         story — that editor code can load unloaded assets where runtime code cannot — is
    ///         <i>false</i> on the supported floor. Measured on 6000.4: once Unity has unloaded an
    ///         asset, its current id resolves through neither <c>Resources.EntityIdToObject</c> nor
    ///         <see cref="EditorUtility.EntityIdToObject" />. The two behave identically, so for a
    ///         current id this class adds nothing at all.
    ///     </para>
    ///     <para>
    ///         <b>What it does add</b> is the legacy path. Before the <c>EntityId</c> migration an id
    ///         was a plain <c>int</c>, and <see cref="EditorUtility.InstanceIDToObject" /> loads such
    ///         an object from the asset database where <c>Resources.InstanceIDToObject</c> does not.
    ///         <c>ConvaiObjectId</c> documents that legacy retry as a promise rather than an
    ///         implementation detail, and the promise is reachable: <c>ConvaiMcpEntityRef.Resolve</c>
    ///         takes its id from outside the editor, so an assistant replaying an id it saved in an
    ///         earlier session can still deliver one.
    ///     </para>
    ///     <para>
    ///         <b>Editor code must call this, not the runtime seam directly.</b> A guard test enforces
    ///         it. The two differ only for legacy ids, which arrive rarely and from outside — so a
    ///         call site on the runtime path works every time it is tried by hand and fails only for
    ///         the caller who kept an id from last week.
    ///     </para>
    /// </remarks>
    internal static class ConvaiEditorObjectId
    {
        /// <summary>
        ///     Resolves <paramref name="id" /> to its object, including a historical <c>int</c>
        ///     instance ID that the runtime seam cannot reach. Returns <c>false</c> when the id is
        ///     <c>0</c>, unknown, or names an object that no longer exists.
        /// </summary>
        internal static bool TryResolve(long id, out Object value)
        {
            if (ConvaiObjectId.TryResolve(id, out value)) return true;
            if (id == 0L) return false;

            // Mirrors ConvaiObjectId.TryResolve's two branches exactly, against EditorUtility rather
            // than Resources. The native form comes first: from 6000.4 an id is a packed 64-bit
            // value, so testing the legacy int range before trying it rejects every current id.
#if UNITY_6000_4_OR_NEWER
            value = EditorUtility.EntityIdToObject(EntityId.FromULong(unchecked((ulong)id)));
            if (value != null) return true;
#endif
            if (id < int.MinValue || id > int.MaxValue) return false;

#if UNITY_6000_3_OR_NEWER
#pragma warning disable CS0618 // Deprecated from 6000.4, but the only legacy-id bridge that is not an error on 6000.5.
            value = EditorUtility.EntityIdToObject((EntityId)(int)id);
#pragma warning restore CS0618
#else
            value = EditorUtility.InstanceIDToObject((int)id);
#endif

            return value != null;
        }

        /// <summary>
        ///     Resolves <paramref name="id" /> to a specific type, unwrapping the usual
        ///     GameObject/Component relationship the same way the runtime seam does.
        /// </summary>
        internal static bool TryResolve<T>(long id, out T value) where T : Object
        {
            value = null;
            if (!TryResolve(id, out Object resolved)) return false;

            switch (resolved)
            {
                case T direct:
                    value = direct;
                    return true;
                case UnityEngine.GameObject gameObject
                    when typeof(UnityEngine.Component).IsAssignableFrom(typeof(T)):
                    value = gameObject.GetComponent(typeof(T)) as T;
                    return value != null;
                case UnityEngine.Component component when component.gameObject is T carrier:
                    value = carrier;
                    return true;
                default:
                    return false;
            }
        }
    }
}
