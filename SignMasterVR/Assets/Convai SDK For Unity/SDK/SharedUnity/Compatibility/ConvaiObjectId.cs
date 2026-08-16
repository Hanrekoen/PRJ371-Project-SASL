using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Shared.Compatibility
{
    /// <summary>
    ///     Version-stable object identity. Every place in the package that needs a stable handle for
    ///     a <see cref="Object" /> — dictionary keys, de-duplication sets, MCP tool payloads — goes
    ///     through here instead of touching Unity's identity APIs directly.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Unity is mid-migration from 32-bit instance IDs to <c>EntityId</c>, and the migration
    ///         lands in three bands (measured from each editor's <c>UnityEngine.CoreModule.dll</c>):
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <b>6000.0 – 6000.1</b> have no <c>EntityId</c> type at all;
    ///             <c>GetInstanceID()</c> is the only identity API and is not deprecated.
    ///         </item>
    ///         <item>
    ///             <b>6000.2 – 6000.3</b> add <c>EntityId</c>, its <c>int</c> conversions and
    ///             <c>Resources.EntityIdToObject</c>, but not the <c>ULong</c> marshalling.
    ///         </item>
    ///         <item>
    ///             <b>6000.4+</b> add <c>EntityId.ToULong</c>/<c>FromULong</c> and deprecate
    ///             <c>GetInstanceID()</c> — as a hard error on 6000.5, where the
    ///             <c>EntityId</c>-to-<c>int</c> operator is also an error:
    ///             <i>"EntityId will not be representable by an int in the future."</i>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         There is therefore no representation that yields the same numeric value on every
    ///         supported editor, and a probe on 6000.5 confirmed the point is already reached:
    ///         <c>ToULong</c> returns a packed 64-bit value unrelated to the object's instance ID.
    ///         This seam returns each editor's native identity widened to <see cref="long" />.
    ///     </para>
    ///     <para>
    ///         <b>An id is valid only inside the session that produced it.</b> That is not a new
    ///         constraint introduced by the version split — Unity instance IDs never survived an
    ///         editor restart either. Ids must not be serialized, written to disk, or compared across
    ///         editor versions; nothing in the package does so.
    ///     </para>
    /// </remarks>
    internal static class ConvaiObjectId
    {
        /// <summary>
        ///     Returns a stable session-scoped id for <paramref name="value" />, or <c>0</c> when it
        ///     is <c>null</c> or destroyed.
        /// </summary>
        internal static long Of(Object value)
        {
            if (value == null) return 0L;

#if UNITY_6000_4_OR_NEWER
            return unchecked((long)EntityId.ToULong(value.GetEntityId()));
#elif UNITY_6000_2_OR_NEWER
            return (int)value.GetEntityId();
#else
            return value.GetInstanceID();
#endif
        }

        /// <summary>
        ///     Resolves an id produced by <see cref="Of" /> back to its object. Returns <c>false</c>
        ///     when the id is <c>0</c>, unknown, or names an object that no longer exists.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Ids that fit in an <c>int</c> are additionally retried as a legacy instance ID.
        ///         Convai's MCP tools have accepted raw instance IDs since before the
        ///         <c>EntityId</c> migration and callers may still send them; that contract is kept.
        ///     </para>
        ///     <para>
        ///         The legacy bridge is <c>EntityIdToObject((EntityId)(int)id)</c> rather than
        ///         <c>Resources.InstanceIDToObject</c>, because that method is obsolete-<i>as-error</i>
        ///         on 6000.5 and <c>#pragma warning disable</c> does not suppress an error. The
        ///         <c>int</c>-to-<c>EntityId</c> conversion is deprecated from 6000.4 but is still
        ///         only a warning on 6000.5, so it remains the one usable bridge. When Unity removes
        ///         it, this method is the single place that has to change.
        ///     </para>
        /// </remarks>
        internal static bool TryResolve(long id, out Object value)
        {
            value = null;
            if (id == 0L) return false;

#if UNITY_6000_4_OR_NEWER
            value = Resources.EntityIdToObject(EntityId.FromULong(unchecked((ulong)id)));
            if (value != null) return true;
#endif
            if (id < int.MinValue || id > int.MaxValue) return false;

#if UNITY_6000_2_OR_NEWER
#pragma warning disable CS0618 // Deprecated from 6000.4, but the only legacy-ID bridge that is not an error on 6000.5.
            value = Resources.EntityIdToObject((EntityId)(int)id);
#pragma warning restore CS0618
#else
            value = Resources.InstanceIDToObject((int)id);
#endif

            return value != null;
        }

        /// <summary>
        ///     Resolves an id to a specific type, unwrapping the usual GameObject/Component
        ///     relationship: a GameObject id resolves to a component it carries, and a component id
        ///     resolves to its GameObject, whichever the caller asked for.
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
                case GameObject gameObject when typeof(Component).IsAssignableFrom(typeof(T)):
                    value = gameObject.GetComponent(typeof(T)) as T;
                    return value != null;
                case Component component when component.gameObject is T carrier:
                    value = carrier;
                    return true;
                default:
                    return false;
            }
        }
    }
}
