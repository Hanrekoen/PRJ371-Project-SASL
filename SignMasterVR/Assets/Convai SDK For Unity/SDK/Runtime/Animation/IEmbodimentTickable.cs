namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Phase a tickable registers for within the <see cref="EmbodimentTickScheduler" />.
    /// </summary>
    /// <remarks>
    ///     Phases execute strictly in enum-declared order each frame. Order <em>within</em> a phase
    ///     comes from <see cref="IEmbodimentTickable.TickOrder" />, not from registration order — see
    ///     that member for why.
    /// </remarks>
    public enum EmbodimentTickPhase
    {
        /// <summary>
        ///     Cognition step: conversation flow, gaze, and emotion directors sample signals and
        ///     update their readings.
        /// </summary>
        Cognition = 0,

        /// <summary>
        ///     Expression step: gaze, body, and facial actuators translate cognition readings into
        ///     bone / blendshape / animator-parameter writes.
        /// </summary>
        Expression = 1,

        /// <summary>
        ///     Late finalization: the facial compositor and animator conductor finalize their writes
        ///     after both cognition and expression have settled.
        /// </summary>
        Finalize = 2
    }

    /// <summary>
    ///     Contract for an embodiment module component that wants to be ticked in a deterministic
    ///     order by <see cref="EmbodimentTickScheduler" /> rather than relying on Unity's
    ///     per-component <c>Update</c> / <c>LateUpdate</c> ordering.
    /// </summary>
    public interface IEmbodimentTickable
    {
        /// <summary>
        ///     Phase this tickable runs in. Read <b>once</b>, when the tickable registers, and
        ///     remembered — so this must not depend on mutable state that changes while registered.
        /// </summary>
        EmbodimentTickPhase Phase { get; }

        /// <summary>
        ///     Sort key within <see cref="Phase" />: lower ticks earlier. Ties are broken by
        ///     registration sequence.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This exists because "registration order" is not determinism. Registration order is
        ///         <c>OnEnable</c> order, which is hierarchy order — so reparenting a child, or a
        ///         module that enables one frame late, silently changed which module wrote a shared
        ///         bone first. Declaring the order makes the contract inspectable and stable.
        ///     </para>
        ///     <para>
        ///         Use the constants in <c>EmbodimentExecutionOrders</c> as the reference scale where a
        ///         module already has one. The default of <c>0</c> is correct for any tickable whose
        ///         position genuinely does not matter.
        ///     </para>
        /// </remarks>
        int TickOrder => 0;

        /// <summary>
        ///     Per-frame tick, called with <see cref="UnityEngine.Time.deltaTime" />.
        /// </summary>
        /// <remarks>
        ///     An exception here is caught, attributed to this component, and reported <em>once</em>;
        ///     the module keeps being ticked so it can recover, and the other modules on the character
        ///     are unaffected.
        /// </remarks>
        void EmbodimentTick(float deltaTime);
    }
}
