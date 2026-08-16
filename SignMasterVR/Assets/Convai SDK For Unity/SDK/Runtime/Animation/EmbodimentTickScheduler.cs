using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Unity.Profiling;
using UnityEngine;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Drives <see cref="IEmbodimentTickable" /> instances in a deterministic
    ///     cognition → expression → finalize order every frame.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each embodiment module component registers itself on <c>OnEnable</c> and unregisters on
    ///         <c>OnDisable</c>; the scheduler runs every registered tickable exactly once per frame
    ///         in its declared phase.
    ///     </para>
    ///     <para>
    ///         <b>Order within a phase is declared, not accidental.</b> Registrations are kept sorted
    ///         by <see cref="IEmbodimentTickable.TickOrder" /> (ties broken by registration sequence),
    ///         so reordering children in the hierarchy or a module enabling a frame late cannot change
    ///         who writes first.
    ///     </para>
    ///     <para>
    ///         The phase band is pinned by execution order: the scheduler runs at <c>18000</c>,
    ///         before the <see cref="AnimatorConductor" /> at <c>19000</c> and the
    ///         <see cref="FacialBlendshapeCompositorHost" /> at <c>20000</c>. Concretely: Unity
    ///         <c>Update</c> (all MonoBehaviours) → scheduler tick → animator resampling, then Unity
    ///         <c>LateUpdate</c> → the pose actuators and the compositor flush, in that same order.
    ///     </para>
    ///     <para>
    ///         The scheduler itself ticks from <c>Update</c>. Deciding happens before the Animator
    ///         poses the skeleton; the writes that have to land on top of that pose are made from
    ///         <c>LateUpdate</c> by the modules themselves, ordered by the same constants — see
    ///         <c>EmbodimentExecutionOrders</c>.
    ///     </para>
    ///     <para>
    ///         Registration churn during a tick is safe: a tickable may register or unregister
    ///         mid-tick and the change applies on the next frame.
    ///     </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(Convai.Runtime.Embodiment.EmbodimentExecutionOrders.TickScheduler)]
    [AddComponentMenu("")]
    internal sealed class EmbodimentTickScheduler : MonoBehaviour
    {
        private static readonly ProfilerMarker CognitionMarker = new("Convai.Embodiment.Cognition");
        private static readonly ProfilerMarker ExpressionMarker = new("Convai.Embodiment.Expression");
        private static readonly ProfilerMarker FinalizeMarker = new("Convai.Embodiment.Finalize");

        private readonly List<Registration> _cognition = new(8);
        private readonly List<Registration> _expression = new(8);
        private readonly List<Registration> _finalize = new(4);

        /// <summary>
        ///     Phase captured at registration time, so unregistering always targets the bucket the
        ///     tickable was actually filed in, even if its phase changed after registration. Re-reading
        ///     <c>Phase</c> on the way out instead would leak a tickable into the loop forever if its
        ///     phase was computed or if the getter touched a destroyed dependency.
        /// </summary>
        private readonly Dictionary<IEmbodimentTickable, EmbodimentTickPhase> _registeredPhase = new();

        /// <summary>Modules whose tick has already thrown, so the error is reported once, not per frame.</summary>
        private readonly HashSet<IEmbodimentTickable> _faulted = new();

        private int _iterationDepth;
        private int _registrationSequence;
        private readonly List<PendingChange> _pendingChanges = new();

        /// <summary>Locates or creates a scheduler on the supplied component's character root.</summary>
        public static EmbodimentTickScheduler GetOrCreate(Component context)
        {
            if (context == null) return null;

            EmbodimentTickScheduler existing = context.GetComponentInParent<EmbodimentTickScheduler>(true);
            if (existing != null) return existing;

            if (!UnityEngine.Application.isPlaying) return null;

            EmbodimentTickScheduler created = context.gameObject.AddComponent<EmbodimentTickScheduler>();
            created.hideFlags = Convai.Runtime.Embodiment.EmbodimentContext.RuntimeInfrastructureHideFlags();
            ConvaiLogger.Info(
                $"[EmbodimentTickScheduler] Added to '{context.gameObject.name}' so embodiment modules " +
                "on this character can tick in a deterministic order.",
                LogCategory.Character);
            return created;
        }

        private void Awake()
        {
            hideFlags = Convai.Runtime.Embodiment.EmbodimentContext.RuntimeInfrastructureHideFlags();
        }

        /// <summary>Registers a tickable so it is included in subsequent frames.</summary>
        public void Register(IEmbodimentTickable tickable)
        {
            if (tickable == null) return;

            if (_iterationDepth > 0)
            {
                _pendingChanges.Add(new PendingChange(tickable, add: true));
                return;
            }

            ApplyRegister(tickable);
        }

        /// <summary>Unregisters a previously registered tickable. Safe to call from <c>OnDisable</c>.</summary>
        public void Unregister(IEmbodimentTickable tickable)
        {
            if (tickable == null) return;

            if (_iterationDepth > 0)
            {
                _pendingChanges.Add(new PendingChange(tickable, add: false));
                return;
            }

            ApplyUnregister(tickable);
        }

        private void ApplyRegister(IEmbodimentTickable tickable)
        {
            if (_registeredPhase.ContainsKey(tickable)) return;

            // Read Phase exactly once, here, and remember it.
            EmbodimentTickPhase phase = tickable.Phase;
            _registeredPhase[tickable] = phase;

            List<Registration> bucket = GetBucket(phase);
            var registration = new Registration(tickable, tickable.TickOrder, _registrationSequence++);

            // Insert in declared order; buckets are small (single digits per character), so a linear
            // insertion is cheaper than sorting and keeps the list stable.
            int index = bucket.Count;
            for (int i = 0; i < bucket.Count; i++)
            {
                if (registration.CompareTo(bucket[i]) >= 0) continue;
                index = i;
                break;
            }

            bucket.Insert(index, registration);
        }

        private void ApplyUnregister(IEmbodimentTickable tickable)
        {
            if (!_registeredPhase.TryGetValue(tickable, out EmbodimentTickPhase phase)) return;

            _registeredPhase.Remove(tickable);
            _faulted.Remove(tickable);

            List<Registration> bucket = GetBucket(phase);
            for (int i = bucket.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(bucket[i].Tickable, tickable)) bucket.RemoveAt(i);
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            using (CognitionMarker.Auto()) TickBucket(_cognition, deltaTime);
            using (ExpressionMarker.Auto()) TickBucket(_expression, deltaTime);
            using (FinalizeMarker.Auto()) TickBucket(_finalize, deltaTime);

            FlushPendingChanges();
        }

        private void TickBucket(List<Registration> bucket, float deltaTime)
        {
            _iterationDepth++;
            try
            {
                for (int i = 0; i < bucket.Count; i++)
                {
                    IEmbodimentTickable tickable = bucket[i].Tickable;
                    if (tickable == null) continue;

                    // A destroyed MonoBehaviour must not be ticked; it is also not something the
                    // owner can unregister anymore, so drop it on sight.
                    if (tickable is MonoBehaviour behaviour && behaviour == null)
                    {
                        _pendingChanges.Add(new PendingChange(tickable, add: false));
                        continue;
                    }

                    try
                    {
                        tickable.EmbodimentTick(deltaTime);
                    }
                    catch (Exception ex)
                    {
                        ReportTickFailure(tickable, ex);
                    }
                }
            }
            finally
            {
                _iterationDepth--;
            }
        }

        /// <summary>
        ///     Logs a module's tick failure once and keeps ticking the others.
        /// </summary>
        /// <remarks>
        ///     Once per module, not once per frame: a module that throws every frame would otherwise
        ///     bury every other diagnostic in the console under an unattributed exception 60 times a
        ///     second. The first failure is reported with the component's name and category; after
        ///     that the module is silent but still ticked, so it can recover.
        /// </remarks>
        private void ReportTickFailure(IEmbodimentTickable tickable, Exception ex)
        {
            if (!_faulted.Add(tickable)) return;

            string owner = tickable is Component component && component != null
                ? component.GetType().Name + " on '" + component.name + "'"
                : tickable.GetType().Name;

            ConvaiLogger.Exception(
                new InvalidOperationException(
                    $"[EmbodimentTickScheduler] {owner} threw during its embodiment tick and will be " +
                    "reported only once. Other modules on this character are unaffected.", ex),
                LogCategory.Character);
        }

        private void FlushPendingChanges()
        {
            if (_pendingChanges.Count == 0) return;

            for (int i = 0; i < _pendingChanges.Count; i++)
            {
                PendingChange change = _pendingChanges[i];
                if (change.Tickable == null) continue;

                if (change.Add) ApplyRegister(change.Tickable);
                else ApplyUnregister(change.Tickable);
            }

            _pendingChanges.Clear();
        }

        private List<Registration> GetBucket(EmbodimentTickPhase phase) => phase switch
        {
            EmbodimentTickPhase.Cognition => _cognition,
            EmbodimentTickPhase.Expression => _expression,
            EmbodimentTickPhase.Finalize => _finalize,
            _ => _expression
        };

        /// <summary>Number of tickables currently registered in <paramref name="phase" />.</summary>
        internal int CountFor(EmbodimentTickPhase phase) => GetBucket(phase).Count;

        /// <summary>Copies the tick order of <paramref name="phase" /> into <paramref name="buffer" />.</summary>
        internal void GetPhaseOrder(EmbodimentTickPhase phase, List<IEmbodimentTickable> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();

            List<Registration> bucket = GetBucket(phase);
            for (int i = 0; i < bucket.Count; i++) buffer.Add(bucket[i].Tickable);
        }

        /// <summary>One registration, carrying the sort key that makes intra-phase order declared.</summary>
        private readonly struct Registration : IComparable<Registration>
        {
            public Registration(IEmbodimentTickable tickable, int order, int sequence)
            {
                Tickable = tickable;
                Order = order;
                Sequence = sequence;
            }

            public IEmbodimentTickable Tickable { get; }
            private int Order { get; }
            private int Sequence { get; }

            public int CompareTo(Registration other)
            {
                int byOrder = Order.CompareTo(other.Order);
                return byOrder != 0 ? byOrder : Sequence.CompareTo(other.Sequence);
            }
        }

        private readonly struct PendingChange
        {
            public PendingChange(IEmbodimentTickable tickable, bool add)
            {
                Tickable = tickable;
                Add = add;
            }

            public IEmbodimentTickable Tickable { get; }
            public bool Add { get; }
        }
    }
}
