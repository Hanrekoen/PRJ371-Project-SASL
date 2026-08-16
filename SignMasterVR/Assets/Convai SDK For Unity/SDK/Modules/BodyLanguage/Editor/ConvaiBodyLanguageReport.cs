using System.Text;
using Convai.Modules.BodyLanguage.Components;
using Convai.Modules.BodyLanguage.Data;
using UnityEngine;

// Moved out of the Editor.AI assembly with the surveyor that reads it — see
// ConvaiBodyLanguageModuleSurveyor. The namespace is deliberately unchanged.
namespace Convai.Modules.BodyLanguage.Editor.AI
{
    /// <summary>
    ///     How far along a character's Body Language setup is.
    /// </summary>
    /// <remarks>
    ///     Three states rather than a boolean, because "not moving" has two causes with two very
    ///     different next steps: the component is not there at all, or it is there and the rig cannot
    ///     drive it. Unlike Body Animation this module needs no content and no profile — it works the
    ///     moment it is added — so there is deliberately no <c>NeedsContent</c> state here. Inventing
    ///     one would report a perfectly good character as unfinished.
    /// </remarks>
    internal enum BodyLanguageReadiness
    {
        /// <summary>No Body Language component on this character at all.</summary>
        NotInstalled,

        /// <summary>Present, but the rig cannot drive it, so it stays inert until someone acts.</summary>
        Blocked,

        /// <summary>Set up. The character breathes, shifts its weight and gestures as it talks.</summary>
        Working
    }

    /// <summary>
    ///     Everything the Convai Body Language tools know about one character, gathered once.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every verdict here comes from <see cref="BodyLanguageSetupService" /> — the same code
    ///         the component inspector's <b>This Character</b> card draws. Nothing in this assembly
    ///         evaluates a character itself, so an assistant and the editor cannot describe the same
    ///         character differently.
    ///     </para>
    ///     <para>
    ///         Shared by all three tools and the scene surveyor so those four cannot drift apart
    ///         either.
    ///     </para>
    /// </remarks>
    internal readonly struct ConvaiBodyLanguageReport
    {
        private ConvaiBodyLanguageReport(
            ConvaiBodyLanguageController controller,
            BodyLanguagePreflight preflight,
            BodyLanguageCoordination coordination,
            ConvaiBodyLanguageProfile assignedProfile,
            ConvaiBodyLanguageProfile effectiveProfile)
        {
            Controller = controller;
            Preflight = preflight;
            Coordination = coordination;
            AssignedProfile = assignedProfile;
            EffectiveProfile = effectiveProfile;
        }

        internal ConvaiBodyLanguageController Controller { get; }
        internal BodyLanguagePreflight Preflight { get; }
        internal BodyLanguageCoordination Coordination { get; }

        /// <summary>The personality asset on the component, or <c>null</c> when none is assigned.</summary>
        internal ConvaiBodyLanguageProfile AssignedProfile { get; }

        /// <summary>The personality the character actually runs on — assigned, or the SDK defaults.</summary>
        internal ConvaiBodyLanguageProfile EffectiveProfile { get; }

        internal bool IsPresent => Controller != null;

        /// <summary>Whether no personality is assigned and the SDK defaults are in use.</summary>
        internal bool UsingSdkDefaults => AssignedProfile == null;

        /// <summary>Whether the character will actually move at runtime.</summary>
        internal bool IsWorking => State == BodyLanguageReadiness.Working;

        internal BodyLanguageReadiness State
        {
            get
            {
                if (Controller == null) return BodyLanguageReadiness.NotInstalled;
                return Preflight.HasBlocker
                    ? BodyLanguageReadiness.Blocked
                    : BodyLanguageReadiness.Working;
            }
        }

        /// <summary>The one line a survey shows without expanding anything.</summary>
        internal string Summary => State switch
        {
            // The module's own [EmbodimentModule] Absence text, said the same way here so a user
            // reading the survey and a user reading the Embodiment window hear one sentence.
            BodyLanguageReadiness.NotInstalled =>
                "Not on this character — the body holds a still pose between animations, without " +
                "breathing or shifting its weight.",
            BodyLanguageReadiness.Blocked => Blocker,
            _ => UsingSdkDefaults
                ? "Breathing, shifting its weight and gesturing, on the SDK defaults."
                : $"Breathing, shifting its weight and gesturing, tuned by '{AssignedProfile.name}'."
        };

        /// <summary>The first preflight row that stops the module working, or an empty string.</summary>
        internal string Blocker =>
            Preflight.TryGetBlocker(out BodyLanguageCheck blocker)
                ? $"{blocker.Label}: {blocker.Detail}."
                : string.Empty;

        /// <summary>
        ///     Gathers the report for <paramref name="characterRoot" />. Read-only and safe from any
        ///     diagnostic path; returns a <see cref="BodyLanguageReadiness.NotInstalled" /> report
        ///     rather than throwing when the character has no controller.
        /// </summary>
        internal static ConvaiBodyLanguageReport For(GameObject characterRoot)
        {
            ConvaiBodyLanguageController controller = characterRoot != null
                ? characterRoot.GetComponentInChildren<ConvaiBodyLanguageController>(true)
                : null;

            return For(controller);
        }

        internal static ConvaiBodyLanguageReport For(ConvaiBodyLanguageController controller)
        {
            if (controller == null)
                return new ConvaiBodyLanguageReport(null, default, default, null, null);

            return new ConvaiBodyLanguageReport(
                controller,
                BodyLanguageSetupService.Inspect(controller),
                BodyLanguageSetupService.InspectCoordination(controller),
                BodyLanguageSetupService.ResolveAssignedProfile(controller),
                BodyLanguageSetupService.ResolveEffectiveProfile(controller));
        }

        /// <summary>
        ///     A stable issue code from a check's own id — <c>bodylanguage.setup.rig</c> becomes
        ///     <c>BODY_LANGUAGE_SETUP_RIG</c>. Derived rather than tabulated, so the codes an
        ///     assistant sees stay a projection of the one check engine instead of a second table
        ///     that has to be kept in step with it.
        /// </summary>
        internal static string IssueCode(string checkId)
        {
            if (string.IsNullOrEmpty(checkId)) return "BODY_LANGUAGE_ISSUE";

            // The ids are already namespaced with the module, so repeating it would read
            // BODY_LANGUAGE_BODYLANGUAGE_SETUP_RIG.
            const string prefix = "bodylanguage.";
            string tail = checkId.StartsWith(prefix, System.StringComparison.Ordinal)
                ? checkId.Substring(prefix.Length)
                : checkId;

            var builder = new StringBuilder("BODY_LANGUAGE_", tail.Length + 14);
            for (int i = 0; i < tail.Length; i++)
            {
                char character = tail[i];
                builder.Append(char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_');
            }

            return builder.ToString();
        }
    }
}
