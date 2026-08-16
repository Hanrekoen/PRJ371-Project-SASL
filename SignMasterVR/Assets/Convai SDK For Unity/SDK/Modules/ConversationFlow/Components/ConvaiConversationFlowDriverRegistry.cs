using System;
using System.Collections.Generic;

namespace Convai.Modules.ConversationFlow.Components
{
    /// <summary>
    ///     Process-wide registry of active <see cref="ConvaiConversationFlowController" />
    ///     instances. Replaces per-enable / per-disable
    ///     <see cref="UnityEngine.Object.FindObjectsByType{T}(UnityEngine.FindObjectsInactive, UnityEngine.FindObjectsSortMode)" />
    ///     scans with O(1) lookup so multi-character scenes do not pay an O(n) tax on every
    ///     scope-change cycle.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Drivers register themselves in <c>OnEnable</c> and unregister in
    ///         <c>OnDisable</c>. The registry fires <see cref="ScopeChanged" /> whenever a
    ///         driver enters or leaves the active set so each controller can reapply its
    ///         player-signal scoping policy.
    ///     </para>
    /// </remarks>
    internal static class ConvaiConversationFlowDriverRegistry
    {
        private static readonly List<ConvaiConversationFlowController> _drivers = new(8);

        /// <summary>Raised after a driver registers or unregisters.</summary>
        public static event Action ScopeChanged;

        /// <summary>Number of active drivers currently in the registry.</summary>
        public static int ActiveCount => _drivers.Count;

        public static void Register(ConvaiConversationFlowController driver)
        {
            if (driver == null) return;
            if (_drivers.Contains(driver)) return;
            _drivers.Add(driver);
            RaiseScopeChanged();
        }

        public static void Unregister(ConvaiConversationFlowController driver)
        {
            if (driver == null) return;
            if (!_drivers.Remove(driver)) return;
            RaiseScopeChanged();
        }

        /// <summary>
        ///     Clears all registered drivers and suppresses any pending scope-changed
        ///     notification. <b>Only call from test [SetUp] / [TearDown].</b>
        /// </summary>
        internal static void Reset()
        {
            _drivers.Clear();
        }

        private static void RaiseScopeChanged()
        {
            Action handler = ScopeChanged;
            if (handler == null) return;

            try
            {
                handler.Invoke();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogException(ex);
            }
        }
    }
}
