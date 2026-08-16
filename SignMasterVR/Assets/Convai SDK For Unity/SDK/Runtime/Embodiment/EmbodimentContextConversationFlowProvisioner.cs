using System;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     The one seam through which the optional ConversationFlow module teaches
    ///     <see cref="EmbodimentContext" /> how to supply a dialogue-state driver to a character that
    ///     did not author one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is <c>static</c> by necessity, not by accident: layered assemblies such as
    ///         <c>Convai.Runtime</c> must not reference a module assembly, so a module that wants to
    ///         serve a Runtime-level contract has to announce itself at load time. It is therefore a
    ///         deliberate, documented extension seam, and it is named on the static-state guard
    ///         test's allow-list rather than pretending not to exist.
    ///     </para>
    ///     <para>
    ///         The default factory is <b>write-once</b>: a second, different installation is refused
    ///         and logged instead of quietly winning, so behavior cannot depend on assembly load
    ///         order — and every component it creates announces itself, so a character's composition
    ///         can be read from the log rather than inferred.
    ///     </para>
    /// </remarks>
    internal static class EmbodimentContextConversationFlowProvisioner
    {
        private static Func<EmbodimentContext, IConversationFlowSource> _factory;
        private static string _installedBy;

        /// <summary>
        ///     Installs the factory invoked by <see cref="CreateDefault" />. Called once, at module
        ///     load time, by the ConversationFlow module.
        /// </summary>
        public static void RegisterDefaultFactory(
            Func<EmbodimentContext, IConversationFlowSource> factory,
            string installerName = null)
        {
            if (factory == null) return;

            if (_factory != null)
            {
                if (_factory == factory) return;

                ConvaiLogger.Warning(
                    "[EmbodimentContext] A conversation-flow provisioner is already installed by " +
                    $"'{_installedBy ?? "an earlier caller"}'; the registration from " +
                    $"'{installerName ?? "an unnamed caller"}' was refused. Only one may be active.",
                    LogCategory.Character);
                return;
            }

            _factory = factory;
            _installedBy = installerName;
        }

        /// <summary>
        ///     Invokes the installed factory to materialize a driver for <paramref name="context" />.
        ///     Returns <c>null</c> when the ConversationFlow module is not present.
        /// </summary>
        public static IConversationFlowSource CreateDefault(EmbodimentContext context) =>
            _factory?.Invoke(context);

        /// <summary>Whether a provisioner is installed — i.e. whether the module is present.</summary>
        internal static bool HasFactory => _factory != null;

        /// <summary>
        ///     Clears the installed factory. Test-only: a domain reload re-runs installation, and an
        ///     EditMode test that asserts the no-module path needs a clean slate.
        /// </summary>
        internal static void ResetForTests()
        {
            _factory = null;
            _installedBy = null;
        }
    }
}
