using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;

// This file lives in the core editor assembly, not in Convai.Editor.AI where it was written, because
// Convai.Editor.AI is gated behind the CONVAI_UNITY_MCP define constraint — it does not exist at all
// unless the Unity AI Assistant package is installed. The vocabulary below is what the Convai
// Troubleshooter, every status chip and the MCP tools all speak, so a user without that package
// would otherwise have an editor that cannot describe their character. The namespace is deliberately
// unchanged: these types are public API and every module registers through them, so renaming it
// would be a breaking change bought for nothing.
namespace Convai.Editor.AI
{
    /// <summary>How serious a module survey finding is.</summary>
    /// <remarks>
    ///     Deliberately the same four levels every module setup service already uses, so a module
    ///     reporting into the survey never has to translate its own severities into a different
    ///     ladder — translation is where two surfaces start disagreeing about the same character.
    /// </remarks>
    public enum ConvaiModuleFindingSeverity
    {
        /// <summary>Already correct; reported so a survey can show what is working.</summary>
        Ok,

        /// <summary>Worth knowing, but nothing is wrong.</summary>
        Info,

        /// <summary>The module runs, but not as well as it could on this character.</summary>
        Warning,

        /// <summary>The module cannot do its job until someone acts.</summary>
        Error
    }

    /// <summary>One thing a module noticed about a character, and what to do about it.</summary>
    public readonly struct ConvaiModuleSurveyFinding
    {
        public ConvaiModuleSurveyFinding(ConvaiModuleFindingSeverity severity, string title, string message)
        {
            Severity = severity;
            Title = title;
            Message = message;
        }

        /// <summary>How serious it is.</summary>
        public ConvaiModuleFindingSeverity Severity { get; }

        /// <summary>Short label, e.g. "Head Bone".</summary>
        public string Title { get; }

        /// <summary>
        ///     What is wrong <em>and what to do next</em>, in the labels the user sees in the
        ///     editor. A message that only states the fact fails this contract.
        /// </summary>
        public string Message { get; }
    }

    /// <summary>How far a character is from actually getting a capability's behaviour.</summary>
    /// <remarks>
    ///     <para>
    ///         Four states rather than a present/working pair, because "it does nothing" has three
    ///         causes with three different next steps, and telling them apart is the difference
    ///         between a survey an assistant can act on and one it can only repeat.
    ///     </para>
    ///     <para>
    ///         Each capability already knows which of these it is in — Body Animation, Body Language
    ///         and Emotions each grew a private four-state enum of their own shape, and Gaze carries
    ///         the same distinction as a preflight verdict plus a blocker. This is the word they map
    ///         onto so the layer-wide survey does not have to guess, and guessing is where two
    ///         surfaces start disagreeing about the same character.
    ///     </para>
    /// </remarks>
    public enum ConvaiCapabilityReadiness
    {
        /// <summary>The capability's component is not on this character.</summary>
        NotInstalled,

        /// <summary>Installed, but something stops it working at all — usually the rig.</summary>
        Blocked,

        /// <summary>
        ///     Installed and unblocked, and still nothing will visibly happen: no content assigned,
        ///     or a setting that switches the whole capability off. <see cref="ConvaiModuleSurveyResult.Blocker" />
        ///     carries which.
        /// </summary>
        Inert,

        /// <summary>Set up. The character will actually do this.</summary>
        Working
    }

    /// <summary>What one module has to say about one character.</summary>
    public readonly struct ConvaiModuleSurveyResult
    {
        public ConvaiModuleSurveyResult(
            string moduleId,
            string displayName,
            ConvaiCapabilityReadiness readiness,
            string summary,
            string blocker,
            IReadOnlyList<ConvaiModuleSurveyFinding> findings)
        {
            ModuleId = moduleId;
            DisplayName = displayName;
            Readiness = readiness;
            Summary = summary;
            Blocker = blocker ?? string.Empty;
            Findings = findings ?? Array.Empty<ConvaiModuleSurveyFinding>();
        }

        /// <summary>Stable module id, e.g. <c>convai.gaze</c>.</summary>
        public string ModuleId { get; }

        /// <summary>What a user calls it, e.g. "Gaze".</summary>
        public string DisplayName { get; }

        /// <summary>How far this character is from getting the capability's behaviour.</summary>
        public ConvaiCapabilityReadiness Readiness { get; }

        /// <summary>Whether the module's component is on this character at all.</summary>
        public bool IsPresent => Readiness != ConvaiCapabilityReadiness.NotInstalled;

        /// <summary>
        ///     Whether it will actually do something at runtime. Present and functional are not the
        ///     same question: a module can be configured and still be inert, and a module can be
        ///     working perfectly while a preference is left unset.
        /// </summary>
        public bool IsFunctional => Readiness == ConvaiCapabilityReadiness.Working;

        /// <summary>One line a survey can show without expanding anything.</summary>
        public string Summary { get; }

        /// <summary>
        ///     One sentence naming what stops this capability working, or empty when nothing does.
        ///     A sentence in the user's words, never a code — this is what a beginner reads when a
        ///     character is <see cref="ConvaiCapabilityReadiness.Blocked" /> or
        ///     <see cref="ConvaiCapabilityReadiness.Inert" />.
        /// </summary>
        public string Blocker { get; }

        /// <summary>Everything worth acting on, never <c>null</c>.</summary>
        public IReadOnlyList<ConvaiModuleSurveyFinding> Findings { get; }

        /// <summary>Whether anything here blocks the module from working.</summary>
        public bool HasBlockingFinding
        {
            get
            {
                for (int i = 0; i < Findings.Count; i++)
                    if (Findings[i].Severity == ConvaiModuleFindingSeverity.Error) return true;
                return false;
            }
        }
    }

    /// <summary>
    ///     Lets a module tell the scene-wide tools what it knows about a character, without the
    ///     scene-wide tools having to know the module exists.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why this exists.</b> <c>Convai.InspectScene</c> and <c>Convai.ValidateSetup</c>
    ///         live in the SDK's core editor assembly, which the module assemblies are layered on
    ///         top of. Referencing a module from here would invert that, and doing it once would
    ///         mean doing it for every module. So the dependency points the other way: a module
    ///         registers itself, and the core tools report whatever registered.
    ///     </para>
    ///     <para>
    ///         An implementation must be a projection of the module's own setup service, not a
    ///         second opinion about the same character. If the survey and the module's own
    ///         diagnostic can disagree, the implementation is wrong.
    ///     </para>
    /// </remarks>
    public interface IConvaiModuleSurveyor
    {
        /// <summary>Stable module id, e.g. <c>convai.gaze</c>. Identity for registration.</summary>
        string ModuleId { get; }

        /// <summary>What a user calls this module.</summary>
        string DisplayName { get; }

        /// <summary>
        ///     Reports what this module makes of <paramref name="characterRoot" />. Read-only:
        ///     surveys run inside diagnostic tools and must never change a scene.
        /// </summary>
        ConvaiModuleSurveyResult Survey(GameObject characterRoot);
    }

    /// <summary>
    ///     The modules that have made themselves visible to the scene-wide Convai tools.
    /// </summary>
    /// <remarks>
    ///     Registration happens from each module's editor assembly at editor load. Nothing is
    ///     required: with no module registered, the scene-wide tools behave exactly as they did
    ///     before this seam existed.
    /// </remarks>
    public static class ConvaiModuleSurveyRegistry
    {
        private static readonly List<IConvaiModuleSurveyor> Surveyors = new(8);

        /// <summary>
        ///     Registers a surveyor, replacing any previous one with the same
        ///     <see cref="IConvaiModuleSurveyor.ModuleId" />. Idempotent, because a domain reload
        ///     re-runs every registration and a module must not appear twice.
        /// </summary>
        public static void Register(IConvaiModuleSurveyor surveyor)
        {
            if (surveyor == null || string.IsNullOrEmpty(surveyor.ModuleId)) return;

            for (int i = 0; i < Surveyors.Count; i++)
            {
                if (!string.Equals(Surveyors[i].ModuleId, surveyor.ModuleId, StringComparison.Ordinal)) continue;
                Surveyors[i] = surveyor;
                return;
            }

            Surveyors.Add(surveyor);
        }

        /// <summary>Every registered surveyor, in registration order.</summary>
        internal static IReadOnlyList<IConvaiModuleSurveyor> All => Surveyors;

        /// <summary>
        ///     Surveys <paramref name="characterRoot" /> with every registered module. A surveyor
        ///     that throws is skipped rather than failing the whole tool call — one module's bug
        ///     must not blind an assistant to the other four.
        /// </summary>
        internal static IReadOnlyList<ConvaiModuleSurveyResult> SurveyAll(GameObject characterRoot)
        {
            if (characterRoot == null || Surveyors.Count == 0)
                return Array.Empty<ConvaiModuleSurveyResult>();

            var results = new List<ConvaiModuleSurveyResult>(Surveyors.Count);
            for (int i = 0; i < Surveyors.Count; i++)
            {
                try
                {
                    results.Add(Surveyors[i].Survey(characterRoot));
                }
                catch (Exception exception)
                {
                    ConvaiLogger.Warning(
                        $"[Convai] The {Surveyors[i].DisplayName} module could not be surveyed on " +
                        $"'{characterRoot.name}': {exception.Message}",
                        LogCategory.Editor);
                }
            }

            return results;
        }
    }
}
