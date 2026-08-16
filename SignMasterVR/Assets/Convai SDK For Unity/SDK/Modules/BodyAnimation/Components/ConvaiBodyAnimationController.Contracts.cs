using Convai.Domain.Embodiment.Interfaces;
using Convai.Runtime.Embodiment;

namespace Convai.Modules.BodyAnimation.Components
{
    /// <summary>
    ///     How this controller publishes the cross-module contracts it owns.
    /// </summary>
    /// <remarks>
    ///     Most contracts live exactly as long as the component and are published through
    ///     <see cref="ConvaiCharacterModule{TProfile}.ProvideService{TContract}" />, which releases them
    ///     automatically. Three do not, and so hold their own withdrawal token:
    ///     <list type="bullet">
    ///         <item><description>the exertion signal follows a config toggle,</description></item>
    ///         <item><description>the co-speech planner is recreated on a runtime rebuild,</description></item>
    ///         <item><description>the gesture performer is recreated on an animation-set handoff.</description></item>
    ///     </list>
    /// </remarks>
    public sealed partial class ConvaiBodyAnimationController
    {
        private void ProvideExertionSource()
        {
            if (Context == null || _exertionToken.IsValid) return;
            _exertionToken = Context.Provide<IExertionSource>(this);
        }

        private void ReleaseExertionSource()
        {
            _exertionToken.Release();
            _exertionToken = default;
        }
    }
}
