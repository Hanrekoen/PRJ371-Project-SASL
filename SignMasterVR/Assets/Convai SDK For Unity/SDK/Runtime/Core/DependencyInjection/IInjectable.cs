namespace Convai.Runtime.Core.DependencyInjection
{
    /// <summary>
    ///     Generic interface for components that receive typed dependency bundles
    ///     with compile-time type safety.
    /// </summary>
    /// <typeparam name="TDependencies">
    ///     The dependency bundle type that this component requires.
    ///     Must be a reference type (e.g., <see cref="IConvaiCharacterDependencies" />,
    ///     <see cref="IConvaiPlayerDependencies" />, <see cref="IConvaiRoomManagerDependencies" />).
    /// </typeparam>
    /// <remarks>
    ///     <para>
    ///         The typed approach provides:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <description>Compile-time verification of dependency availability</description>
    ///         </item>
    ///         <item>
    ///             <description>No runtime container resolution (no <c>container.Get&lt;T&gt;()</c>)</description>
    ///         </item>
    ///         <item>
    ///             <description>Clear documentation of component requirements</description>
    ///         </item>
    ///         <item>
    ///             <description>Easier testing with mock dependency bundles</description>
    ///         </item>
    ///     </list>
    /// </remarks>
    /// <example>
    ///     <code>
    ///     public class ConvaiCharacter : MonoBehaviour, IInjectable&lt;IConvaiCharacterDependencies&gt;
    ///     {
    ///         private IConvaiCharacterDependencies _deps;
    /// 
    ///         public void InjectDependencies(IConvaiCharacterDependencies dependencies)
    ///         {
    ///             _deps = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    ///         }
    ///     }
    ///     </code>
    /// </example>
    public interface IInjectable<in TDependencies> where TDependencies : class
    {
        /// <summary>
        ///     Injection order for deterministic initialization sequence.
        ///     Lower values are injected first. Default is 0.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Infrastructure components that provide services to others should use
        ///         negative values to ensure they initialize first. For example:
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             <description><c>ConvaiRoomManager</c>: -100 (provides room services)</description>
        ///         </item>
        ///         <item>
        ///             <description><c>ConvaiCharacter</c>: 0 (default, consumes room services)</description>
        ///         </item>
        ///         <item>
        ///             <description><c>ConvaiPlayer</c>: 0 (default)</description>
        ///         </item>
        ///     </list>
        /// </remarks>
        public int InjectionOrder => 0;

        /// <summary>
        ///     Receives the typed dependency bundle during composition.
        ///     Called by the composition root after all services are registered.
        /// </summary>
        /// <param name="dependencies">
        ///     The dependency bundle containing all required and optional dependencies.
        ///     Required dependencies are guaranteed non-null; optional dependencies may be null.
        /// </param>
        /// <exception cref="System.ArgumentNullException">
        ///     Implementations should throw if <paramref name="dependencies" /> is null.
        /// </exception>
        public void InjectDependencies(TDependencies dependencies);
    }
}
