using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     A one-click action injection shown as a button in the Actions Editor's Live mode
    ///     (Advanced &gt; Send a Raw Command).
    /// </summary>
    public sealed class ConvaiActionDebugInjectionPreset
    {
        /// <summary>Button label.</summary>
        public string Label { get; }

        /// <summary>Action name to inject.</summary>
        public string ActionName { get; }

        /// <summary>Optional raw target/parameter blob to inject with the action.</summary>
        public string Target { get; }

        /// <summary>Creates an injection preset.</summary>
        public ConvaiActionDebugInjectionPreset(string label, string actionName, string target = null)
        {
            Label = label;
            ActionName = actionName;
            Target = target;
        }
    }

    /// <summary>
    ///     Supplies project-specific content to the Actions Editor's Live mode (Advanced &gt; Send a
    ///     Raw Command) without the shipped SDK referencing sample or dev types. Register
    ///     implementations via <see cref="ConvaiActionDebugPresetRegistry.Register" /> (for example
    ///     from an InitializeOnLoad class in a dev or sample editor assembly).
    /// </summary>
    public interface IConvaiActionDebugPresetProvider
    {
        /// <summary>Section heading shown in the Live mode Advanced card.</summary>
        string DisplayName { get; }

        /// <summary>Action templates that can be applied to the selected config source; null when none.</summary>
        IReadOnlyList<ConvaiActionDefinition> BuildTemplates() => null;

        /// <summary>One-click injections shown as buttons; null when none.</summary>
        IReadOnlyList<ConvaiActionDebugInjectionPreset> GetInjectionPresets() => null;

        /// <summary>Optional status line for the selected character (for example held-object state); null to hide.</summary>
        string DescribeCharacterState(ConvaiCharacter character) => null;
    }

    /// <summary>
    ///     Registry of debug preset providers consumed by the Actions Editor's Live mode (Advanced
    ///     &gt; Send a Raw Command).
    /// </summary>
    public static class ConvaiActionDebugPresetRegistry
    {
        private static readonly List<IConvaiActionDebugPresetProvider> RegisteredProviders = new();

        /// <summary>Currently registered providers, in registration order.</summary>
        public static IReadOnlyList<IConvaiActionDebugPresetProvider> Providers => RegisteredProviders;

        /// <summary>Registers a provider; duplicate registrations are ignored.</summary>
        public static void Register(IConvaiActionDebugPresetProvider provider)
        {
            if (provider != null && !RegisteredProviders.Contains(provider))
                RegisteredProviders.Add(provider);
        }

        /// <summary>Removes a previously registered provider.</summary>
        public static void Unregister(IConvaiActionDebugPresetProvider provider) =>
            RegisteredProviders.Remove(provider);
    }
}
