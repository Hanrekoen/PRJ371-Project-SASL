using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Per-character registry of the contracts embodiment modules publish to each other.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One dictionary-backed registry serves every cross-module contract, instead of a
    ///         hand-written slot, property, event and register/unregister pair per seam. Adding a
    ///         contract is a Domain interface and nothing else — the composition root does not change.
    ///     </para>
    ///     <para>
    ///         Two registration shapes, deliberately distinct so the intent is visible at the call
    ///         site:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 <see cref="Provide{TContract}" /> — single writer per character. A second
    ///                 provider is <em>rejected</em> and both instances are named in a warning; the
    ///                 first registration keeps the contract. This preserves the guarantee consumers
    ///                 already rely on: whoever holds the contract holds it for the character's
    ///                 lifetime, so a cached reference cannot be swapped underneath them.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 <see cref="Contribute{TContract}" /> — many writers, registration order
    ///                 preserved, read through <see cref="GetAll{TContract}" />. Only for contracts
    ///                 whose interface documents fan-out (for example an action-performance reactor
    ///                 that gaze, emotion and body language each observe independently).
    ///             </description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         <b>The type argument is the contract, never the concrete type.</b> Write
    ///         <c>Provide&lt;IGazeSource&gt;(this)</c>: letting C# infer it from the instance would
    ///         key the entry on the controller type, and <c>TryGet&lt;IGazeSource&gt;</c> would never
    ///         find it. A guard test rejects a call whose type argument is not an interface.
    ///     </para>
    ///     <para>
    ///         Cost: registration allocates one dictionary entry (and, for a contribution, one list
    ///         node) on an <c>OnEnable</c>-frequency path. <see cref="ServiceToken" /> is a struct
    ///         returned by value and never stored as <see cref="IDisposable" />, so releasing a
    ///         registration does not box. Reads are a single dictionary probe; consumers cache the
    ///         resolved reference and refresh from <see cref="AddChangedHandler{TContract}" />, so
    ///         steady-state per-frame cost is zero allocations.
    ///     </para>
    /// </remarks>
    internal sealed class CharacterServiceRegistry
    {
        private readonly Dictionary<Type, object> _single = new();
        private readonly Dictionary<Type, List<object>> _multi = new();
        private readonly Dictionary<Type, ChangedHandlerList> _changed = new();

        private readonly EmbodimentContext _owner;

        internal CharacterServiceRegistry(EmbodimentContext owner)
        {
            _owner = owner;
        }

        // ── single-writer contracts ─────────────────────────────────────────────────

        /// <summary>
        ///     Publishes <paramref name="service" /> as the character's provider of
        ///     <typeparamref name="TContract" />.
        /// </summary>
        /// <returns>
        ///     A token whose <see cref="ServiceToken.Release" /> withdraws this registration, or
        ///     <c>default</c> when the registration was rejected or redundant. Releasing a
        ///     <c>default</c> token is safe and does nothing.
        /// </returns>
        internal ServiceToken Provide<TContract>(TContract service) where TContract : class
        {
            if (service == null) return default;

            Type contract = typeof(TContract);

            if (_single.TryGetValue(contract, out object existing))
            {
                // Re-providing the same instance is idempotent, not a conflict.
                if (ReferenceEquals(existing, service)) return new ServiceToken(this, contract, service);

                LogWarning(
                    $"[EmbodimentContext] Duplicate {DescribeContract(contract)} '{Describe(service)}' " +
                    $"ignored. '{Describe(existing)}' is already registered on this character. " +
                    "Remove the duplicate component — only one per character is supported.");
                return default;
            }

            _single[contract] = service;
            RaiseChanged(contract, service);
            return new ServiceToken(this, contract, service);
        }

        /// <summary>
        ///     Resolves the character's provider of <typeparamref name="TContract" />.
        /// </summary>
        internal bool TryGet<TContract>(out TContract service) where TContract : class
        {
            if (_single.TryGetValue(typeof(TContract), out object stored))
            {
                service = stored as TContract;
                return service != null;
            }

            service = null;
            return false;
        }

        /// <summary>Resolves the provider of <typeparamref name="TContract" />, or <c>null</c>.</summary>
        internal TContract Get<TContract>() where TContract : class =>
            _single.TryGetValue(typeof(TContract), out object stored) ? stored as TContract : null;

        // ── multi-writer contracts ──────────────────────────────────────────────────

        /// <summary>
        ///     Adds <paramref name="service" /> to the character's contributors for
        ///     <typeparamref name="TContract" />. Registration order is preserved.
        /// </summary>
        internal ServiceToken Contribute<TContract>(TContract service) where TContract : class
        {
            if (service == null) return default;

            Type contract = typeof(TContract);
            if (!_multi.TryGetValue(contract, out List<object> contributors))
            {
                contributors = new List<object>(4);
                _multi[contract] = contributors;
            }

            if (contributors.Contains(service)) return new ServiceToken(this, contract, service, multi: true);

            contributors.Add(service);
            RaiseChanged(contract, service);
            return new ServiceToken(this, contract, service, multi: true);
        }

        /// <summary>
        ///     Copies the contributors for <typeparamref name="TContract" /> into
        ///     <paramref name="buffer" />. Clears the buffer first; allocates nothing when the
        ///     buffer already has capacity, so this is safe to call per frame.
        /// </summary>
        internal void GetAll<TContract>(List<TContract> buffer) where TContract : class
        {
            if (buffer == null) return;
            buffer.Clear();

            if (!_multi.TryGetValue(typeof(TContract), out List<object> contributors)) return;

            for (int i = 0; i < contributors.Count; i++)
            {
                if (contributors[i] is TContract typed) buffer.Add(typed);
            }
        }

        /// <summary>Whether any contributor is registered for <typeparamref name="TContract" />.</summary>
        internal bool HasAny<TContract>() where TContract : class =>
            _multi.TryGetValue(typeof(TContract), out List<object> c) && c.Count > 0;

        // ── change notification ─────────────────────────────────────────────────────

        /// <summary>
        ///     Subscribes to registration and withdrawal of <typeparamref name="TContract" />. The
        ///     handler receives the new provider, or <c>null</c> when the contract became vacant, so
        ///     no follow-up lookup is needed.
        /// </summary>
        internal void AddChangedHandler<TContract>(Action<TContract> handler) where TContract : class
        {
            if (handler == null) return;

            Type contract = typeof(TContract);
            if (!_changed.TryGetValue(contract, out ChangedHandlerList list))
            {
                list = new ChangedHandlerList<TContract>();
                _changed[contract] = list;
            }

            ((ChangedHandlerList<TContract>)list).Add(handler);
        }

        /// <summary>Removes a handler added by <see cref="AddChangedHandler{TContract}" />.</summary>
        internal void RemoveChangedHandler<TContract>(Action<TContract> handler) where TContract : class
        {
            if (handler == null) return;
            if (_changed.TryGetValue(typeof(TContract), out ChangedHandlerList list))
                ((ChangedHandlerList<TContract>)list).Remove(handler);
        }

        // ── withdrawal ──────────────────────────────────────────────────────────────

        /// <summary>
        ///     Withdraws <paramref name="service" /> from <typeparamref name="TContract" /> without
        ///     needing the token, for callers that already hold the instance. Only clears the
        ///     contract when <paramref name="service" /> is the current holder.
        /// </summary>
        /// <remarks>
        ///     Prefer the token returned by <see cref="Provide{TContract}" />: it cannot be pointed
        ///     at the wrong contract, and the module base class releases it automatically. This
        ///     overload exists for the cases where the registration and the withdrawal are far apart
        ///     and threading a token through would obscure rather than clarify.
        /// </remarks>
        internal void Withdraw<TContract>(TContract service) where TContract : class
        {
            if (service == null) return;

            Type contract = typeof(TContract);
            bool isContributor =
                _multi.TryGetValue(contract, out List<object> contributors) && contributors.Contains(service);

            Release(contract, service, isContributor);
        }

        /// <summary>
        ///     Withdraws a registration. Only clears a single-writer contract when
        ///     <paramref name="service" /> is the instance currently holding it, so a rejected
        ///     duplicate releasing its token cannot evict the real provider.
        /// </summary>
        private void Release(Type contract, object service, bool multi)
        {
            if (contract == null || service == null) return;

            if (multi)
            {
                if (_multi.TryGetValue(contract, out List<object> contributors) && contributors.Remove(service))
                    RaiseChanged(contract, null);
                return;
            }

            if (_single.TryGetValue(contract, out object current) && ReferenceEquals(current, service))
            {
                _single.Remove(contract);
                RaiseChanged(contract, null);
            }
        }

        /// <summary>
        ///     Dispatches a change to each handler individually.
        /// </summary>
        /// <remarks>
        ///     Per-handler, not one <c>try</c> around a multicast delegate: each handler is invoked in
        ///     its own try/catch, so one handler that throws cannot stop later handlers from learning
        ///     the contract had changed.
        /// </remarks>
        private void RaiseChanged(Type contract, object value)
        {
            if (_changed.TryGetValue(contract, out ChangedHandlerList list))
                list.Raise(value, this, contract);
        }

        private void LogWarning(string message)
        {
            // The character's injected logger when there is one, so the message carries session
            // context; otherwise the shared reporter, which guarantees it reaches the console even
            // with no sink installed.
            ILogger logger = _owner != null ? _owner.Logger : null;
            if (logger != null)
            {
                logger.Warning(message, LogCategory.Character);
                return;
            }

            EmbodimentDiagnostics.SetupWarning(message);
        }

        private void LogSubscriberException(Exception ex, Type contract)
        {
            string message =
                $"[EmbodimentContext] A subscriber threw while handling a change to " +
                $"{DescribeContract(contract)}. Other subscribers were still notified.";

            ILogger logger = _owner != null ? _owner.Logger : null;
            if (logger != null)
            {
                logger.Error(ex, message, LogCategory.Character);
                return;
            }

            EmbodimentDiagnostics.SubscriberException(ex, message);
        }

        /// <summary>Turns <c>IConversationFlowSource</c> into "conversation flow source" for messages.</summary>
        private static string DescribeContract(Type contract)
        {
            string name = contract.Name;
            if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1])) name = name.Substring(1);

            var builder = new System.Text.StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c)) builder.Append(' ');
                builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }

        private static string Describe(object value)
        {
            if (value is Component component) return component.name;
            if (value is UnityEngine.Object unityObject) return unityObject.name;
            return value != null ? value.GetType().Name : "<null>";
        }

        /// <summary>
        ///     Type-erased base so the registry can hold one handler list per contract in a single
        ///     dictionary while dispatch stays strongly typed — no <c>DynamicInvoke</c>, which would
        ///     box the argument and pay reflection cost on every change.
        /// </summary>
        private abstract class ChangedHandlerList
        {
            internal abstract void Raise(object value, CharacterServiceRegistry registry, Type contract);
        }

        private sealed class ChangedHandlerList<TContract> : ChangedHandlerList where TContract : class
        {
            private readonly List<Action<TContract>> _handlers = new(4);

            /// <summary>
            ///     Snapshot buffers, one per nesting level, reused across raises.
            /// </summary>
            /// <remarks>
            ///     A handler is explicitly allowed to provide or release a service while being
            ///     notified, including for <em>this</em> contract, which re-enters <see cref="Raise" />.
            ///     Depth-indexed buffers give each nesting level its own list, so the re-entrant call
            ///     cannot clear or truncate the outer pass and handlers after the re-entering one still
            ///     see the outer change — while still allocating nothing in the steady state.
            /// </remarks>
            private readonly List<List<Action<TContract>>> _snapshotsByDepth = new(2);

            private int _depth;

            internal void Add(Action<TContract> handler)
            {
                if (!_handlers.Contains(handler)) _handlers.Add(handler);
            }

            internal void Remove(Action<TContract> handler) => _handlers.Remove(handler);

            internal override void Raise(object value, CharacterServiceRegistry registry, Type contract)
            {
                if (_handlers.Count == 0) return;

                // Snapshot because a handler may subscribe, unsubscribe, or provide/release a
                // service while being notified.
                List<Action<TContract>> snapshot = RentSnapshot();
                snapshot.AddRange(_handlers);

                var typed = value as TContract;
                try
                {
                    for (int i = 0; i < snapshot.Count; i++)
                    {
                        try
                        {
                            snapshot[i].Invoke(typed);
                        }
                        catch (Exception ex)
                        {
                            registry.LogSubscriberException(ex, contract);
                        }
                    }
                }
                finally
                {
                    snapshot.Clear();
                    _depth--;
                }
            }

            /// <summary>Returns this nesting level's buffer, cleared and ready, and claims the level.</summary>
            private List<Action<TContract>> RentSnapshot()
            {
                while (_snapshotsByDepth.Count <= _depth)
                    _snapshotsByDepth.Add(new List<Action<TContract>>(4));

                List<Action<TContract>> snapshot = _snapshotsByDepth[_depth];
                snapshot.Clear();
                _depth++;
                return snapshot;
            }
        }

        /// <summary>
        ///     Handle to one registration. A <c>struct</c> so withdrawing a registration allocates
        ///     nothing; never stored as <see cref="IDisposable" />, which would box it.
        ///     <see cref="Release" /> is idempotent and safe on a <c>default</c> token.
        /// </summary>
        internal readonly struct ServiceToken
        {
            private readonly CharacterServiceRegistry _registry;
            private readonly Type _contract;
            private readonly object _service;
            private readonly bool _multi;

            internal ServiceToken(CharacterServiceRegistry registry, Type contract, object service, bool multi = false)
            {
                _registry = registry;
                _contract = contract;
                _service = service;
                _multi = multi;
            }

            /// <summary>Whether this token refers to a live registration.</summary>
            internal bool IsValid => _registry != null && _contract != null && _service != null;

            /// <summary>Withdraws the registration. Safe to call more than once.</summary>
            internal void Release() => _registry?.Release(_contract, _service, _multi);
        }
    }
}
