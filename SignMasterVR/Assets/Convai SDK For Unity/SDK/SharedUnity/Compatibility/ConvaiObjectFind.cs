using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Shared.Compatibility
{
    /// <summary>
    ///     Version-stable scene queries. Every <c>FindObjectsByType</c> call in the package goes
    ///     through here so that no call site has to know which overloads the running editor offers.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Unity offers no single <c>FindObjectsByType</c> form that compiles warning-free across
    ///         the supported range (measured from each editor's <c>UnityEngine.CoreModule.dll</c>):
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <b>6000.0 – 6000.3</b> expose only
    ///             <c>FindObjectsByType&lt;T&gt;(FindObjectsInactive, FindObjectsSortMode)</c>.
    ///             The short overloads do not exist and are a compile error.
    ///         </item>
    ///         <item>
    ///             <b>6000.4</b> adds <c>FindObjectsByType&lt;T&gt;()</c> and
    ///             <c>FindObjectsByType&lt;T&gt;(FindObjectsInactive)</c>.
    ///         </item>
    ///         <item>
    ///             <b>6000.5</b> deprecates both <c>FindObjectsSortMode</c> and every overload that
    ///             takes it: <i>"InstanceID will be replaced in the future with EntityId and previous
    ///             sort order cannot be maintained."</i>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         The two branches below are behaviourally identical, not merely similar. A measurement
    ///         probe on 6000.4 and 6000.5 confirmed that each short overload returns the same
    ///         elements in the same order as its <see cref="FindObjectsSortMode.None" /> counterpart,
    ///         over a population built to discriminate the two sort modes. Callers therefore see one
    ///         behaviour on every supported editor.
    ///     </para>
    ///     <para>
    ///         Results are unsorted on every version. Callers that need a stable order must sort
    ///         explicitly — do not depend on the order this returns.
    ///     </para>
    /// </remarks>
    internal static class ConvaiObjectFind
    {
        /// <summary>
        ///     Returns every loaded object of type <typeparamref name="T" />, optionally including
        ///     components on inactive GameObjects. Order is unspecified.
        /// </summary>
        internal static T[] All<T>(bool includeInactive) where T : Object =>
            All<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);

        /// <summary>
        ///     Returns every loaded object of type <typeparamref name="T" /> under an explicit
        ///     <see cref="FindObjectsInactive" /> mode. Order is unspecified.
        /// </summary>
        internal static T[] All<T>(FindObjectsInactive inactiveMode) where T : Object
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>(inactiveMode);
#else
            return Object.FindObjectsByType<T>(inactiveMode, FindObjectsSortMode.None);
#endif
        }
    }
}
