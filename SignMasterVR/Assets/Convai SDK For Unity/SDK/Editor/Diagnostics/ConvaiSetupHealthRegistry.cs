using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Shared.Compatibility;
using UnityEditor;
using Convai.Editor.AI;
using UnityEngine;

namespace Convai.Editor.Diagnostics
{
    /// <summary>
    ///     Everything every Convai module knows about one character, gathered once and shared by every
    ///     surface that reports it.
    /// </summary>
    public sealed class ConvaiSetupHealthSnapshot
    {
        private static readonly ConvaiSetupHealthResult[] NoResults = Array.Empty<ConvaiSetupHealthResult>();

        internal ConvaiSetupHealthSnapshot(
            GameObject character, IReadOnlyList<ConvaiSetupHealthResult> results, double checkedAt)
        {
            Character = character;
            Results = results ?? NoResults;
            CheckedAt = checkedAt;

            for (int i = 0; i < Results.Count; i++)
            {
                ConvaiSetupHealthResult result = Results[i];
                ErrorCount += result.ErrorCount;
                WarningCount += result.WarningCount;
                FixableCount += result.FixableCount;
            }
        }

        /// <summary>The character these results are about. Null for the empty snapshot.</summary>
        public GameObject Character { get; }

        /// <summary>One result per module that had something to say, in display order.</summary>
        public IReadOnlyList<ConvaiSetupHealthResult> Results { get; }

        /// <summary><see cref="EditorApplication.timeSinceStartup" /> when this was built.</summary>
        public double CheckedAt { get; }

        public int ErrorCount { get; }

        public int WarningCount { get; }

        public int FixableCount { get; }

        /// <summary>The one number every surface shows as "N to fix".</summary>
        public int IssueCount => ErrorCount + WarningCount;

        public bool IsHealthy => IssueCount == 0;

        /// <summary>
        ///     Whether any module is installed but will visibly do nothing. Distinct from having an
        ///     issue: an inert module is not broken, and a user who is told "Ready" while nothing
        ///     happens has been told the wrong thing.
        /// </summary>
        public bool HasInertModule
        {
            get
            {
                for (int i = 0; i < Results.Count; i++)
                    if (Results[i].Readiness == ConvaiCapabilityReadiness.Inert) return true;
                return false;
            }
        }

        /// <summary>This character's result for one module, or null when that module said nothing.</summary>
        public ConvaiSetupHealthResult Find(string moduleId)
        {
            for (int i = 0; i < Results.Count; i++)
                if (string.Equals(Results[i].ModuleId, moduleId, StringComparison.Ordinal)) return Results[i];
            return null;
        }

        internal static ConvaiSetupHealthSnapshot Empty { get; } =
            new(null, NoResults, double.NegativeInfinity);
    }

    /// <summary>
    ///     The modules that have made themselves visible to Convai's shared diagnostic surfaces, and
    ///     the per-character cache in front of them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>One engine, three consumers.</b> The Troubleshooter window, the inspector status
    ///         chips and the MCP tools all read this. If any two of them could disagree about the same
    ///         character at the same moment — one saying "1 to fix" while another says "5 to fix" —
    ///         the implementation would be wrong. Agreement here is a property of the design, not
    ///         something each surface is trusted to maintain.
    ///     </para>
    ///     <para>
    ///         <b>Nothing here runs in a repaint path.</b> Providers sweep scenes and walk rigs, and an
    ///         IMGUI inspector repaints on every mouse move. <see cref="Get" /> serves a cached
    ///         snapshot for <see cref="CacheSeconds" /> and rebuilds only when the answer can have
    ///         changed; anything that mutates a character from outside the normal edit flow calls
    ///         <see cref="Invalidate" />.
    ///     </para>
    ///     <para>
    ///         <b>Registration is idempotent</b>, because a domain reload re-runs every registration
    ///         and a module must not appear twice. Registering here also registers with
    ///         <see cref="ConvaiModuleSurveyRegistry" />, so a module implements one interface and
    ///         both the editor and the MCP tools see it.
    ///     </para>
    /// </remarks>
    [InitializeOnLoad]
    public static class ConvaiSetupHealthRegistry
    {
        /// <summary>
        ///     How long a snapshot may be reused. Short enough that a stale count is never something a
        ///     person notices, long enough that dragging a window does not sweep the scene sixty times
        ///     a second.
        /// </summary>
        private const double CacheSeconds = 0.5d;

        private static readonly List<IConvaiSetupHealthProvider> Providers = new(8);

        /// <summary>
        ///     Cached snapshots keyed by <see cref="ConvaiObjectId" /> — the compatibility seam, not
        ///     <c>GetInstanceID</c>, which is a warning on Unity 6000.4 and a hard error on 6000.5.
        /// </summary>
        private static readonly Dictionary<long, Entry> Cache = new();

        private readonly struct Entry
        {
            internal Entry(ConvaiSetupHealthSnapshot snapshot, double builtAt)
            {
                Snapshot = snapshot;
                BuiltAt = builtAt;
            }

            internal ConvaiSetupHealthSnapshot Snapshot { get; }
            internal double BuiltAt { get; }
        }

        static ConvaiSetupHealthRegistry()
        {
            // The events that can change the answer without any surface being rebuilt. The
            // Troubleshooter's Re-check covers everything else.
            EditorApplication.hierarchyChanged += Invalidate;
            Undo.undoRedoPerformed += Invalidate;
            EditorApplication.playModeStateChanged += _ => Invalidate();
        }

        /// <summary>
        ///     Registers a provider, replacing any previous one with the same
        ///     <see cref="IConvaiSetupHealthProvider.ModuleId" />.
        /// </summary>
        public static void Register(IConvaiSetupHealthProvider provider)
        {
            if (provider == null || string.IsNullOrEmpty(provider.ModuleId)) return;

            for (int i = 0; i < Providers.Count; i++)
            {
                if (!string.Equals(Providers[i].ModuleId, provider.ModuleId, StringComparison.Ordinal)) continue;
                Providers[i] = provider;
                Invalidate();
                ConvaiModuleSurveyRegistry.Register(new SurveyorAdapter(provider));
                return;
            }

            Providers.Add(provider);
            ConvaiModuleSurveyRegistry.Register(new SurveyorAdapter(provider));
            Invalidate();
        }

        /// <summary>Every registered provider, in registration order. Test and window seam.</summary>
        internal static IReadOnlyList<IConvaiSetupHealthProvider> All => Providers;

        /// <summary>Drops every cached snapshot, so the next reader recomputes.</summary>
        public static void Invalidate() => Cache.Clear();

        /// <summary>
        ///     Everything known about <paramref name="characterRoot" />, recomputed only when it may
        ///     have changed. Safe to call from a repaint path.
        /// </summary>
        public static ConvaiSetupHealthSnapshot Get(GameObject characterRoot)
        {
            if (characterRoot == null)
                return ConvaiSetupHealthSnapshot.Empty;

            long key = ConvaiObjectId.Of(characterRoot);
            if (Cache.TryGetValue(key, out Entry entry) &&
                EditorApplication.timeSinceStartup - entry.BuiltAt < CacheSeconds &&
                entry.Snapshot.Character != null)
                return entry.Snapshot;

            ConvaiSetupHealthSnapshot snapshot = Build(characterRoot);
            Cache[key] = new Entry(snapshot, EditorApplication.timeSinceStartup);
            return snapshot;
        }

        /// <summary>
        ///     Rebuilds <paramref name="characterRoot" />'s snapshot now, ignoring the cache — what a
        ///     Re-check button and the code that runs a fix both need.
        /// </summary>
        public static ConvaiSetupHealthSnapshot Refresh(GameObject characterRoot)
        {
            if (characterRoot == null)
                return ConvaiSetupHealthSnapshot.Empty;

            Cache.Remove(ConvaiObjectId.Of(characterRoot));
            return Get(characterRoot);
        }

        /// <summary>
        ///     Runs every provider that claims this character. A provider that throws is skipped with
        ///     one warning rather than taking the whole report down — one module's bug must not blind
        ///     the user to the other six.
        /// </summary>
        private static ConvaiSetupHealthSnapshot Build(GameObject characterRoot)
        {
            var results = new List<ConvaiSetupHealthResult>(Providers.Count);

            for (int i = 0; i < Providers.Count; i++)
            {
                IConvaiSetupHealthProvider provider = Providers[i];
                try
                {
                    if (!provider.AppliesTo(characterRoot)) continue;

                    ConvaiSetupHealthResult result = provider.Inspect(characterRoot);
                    if (result != null)
                        results.Add(result);
                }
                catch (Exception exception)
                {
                    ConvaiLogger.Warning(
                        $"[Convai] The {provider.DisplayName} module could not be checked on " +
                        $"'{characterRoot.name}': {exception.Message}",
                        LogCategory.Editor);
                }
            }

            AddLegacySurveyors(characterRoot, results);

            results.Sort(static (left, right) =>
            {
                int byOrder = left.Order.CompareTo(right.Order);
                return byOrder != 0
                    ? byOrder
                    : string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
            });

            return new ConvaiSetupHealthSnapshot(characterRoot, results, EditorApplication.timeSinceStartup);
        }

        /// <summary>
        ///     Folds in modules that still register only as <see cref="IConvaiModuleSurveyor" />, so a
        ///     module appears in the Troubleshooter the day the shared surfaces exist rather than the
        ///     day it is ported. Their findings carry no fix and no locate target — which is precisely
        ///     what porting a module to <see cref="IConvaiSetupHealthProvider" /> adds.
        /// </summary>
        private static void AddLegacySurveyors(GameObject characterRoot, List<ConvaiSetupHealthResult> results)
        {
            IReadOnlyList<IConvaiModuleSurveyor> surveyors = ConvaiModuleSurveyRegistry.All;
            for (int i = 0; i < surveyors.Count; i++)
            {
                IConvaiModuleSurveyor surveyor = surveyors[i];
                if (surveyor is SurveyorAdapter) continue;
                if (ContainsModule(results, surveyor.ModuleId)) continue;

                try
                {
                    ConvaiModuleSurveyResult survey = surveyor.Survey(characterRoot);
                    if (survey.Readiness == ConvaiCapabilityReadiness.NotInstalled && survey.Findings.Count == 0)
                        continue;

                    results.Add(FromSurvey(survey));
                }
                catch (Exception exception)
                {
                    ConvaiLogger.Warning(
                        $"[Convai] The {surveyor.DisplayName} module could not be checked on " +
                        $"'{characterRoot.name}': {exception.Message}",
                        LogCategory.Editor);
                }
            }
        }

        private static bool ContainsModule(List<ConvaiSetupHealthResult> results, string moduleId)
        {
            for (int i = 0; i < results.Count; i++)
                if (string.Equals(results[i].ModuleId, moduleId, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        ///     Lifts a survey result into the richer shape. Ids are synthesised from the module id and
        ///     the finding's position, because a survey finding has none — stable for as long as the
        ///     module reports the same findings in the same order, which is all a fold state needs.
        /// </summary>
        private static ConvaiSetupHealthResult FromSurvey(ConvaiModuleSurveyResult survey)
        {
            var findings = new ConvaiSetupFinding[survey.Findings.Count];
            for (int i = 0; i < survey.Findings.Count; i++)
            {
                ConvaiModuleSurveyFinding finding = survey.Findings[i];
                findings[i] = new ConvaiSetupFinding(
                    $"{survey.ModuleId}.survey.{i}", finding.Severity, finding.Title, finding.Message);
            }

            return new ConvaiSetupHealthResult(
                survey.ModuleId, survey.DisplayName, survey.Readiness, survey.Summary, survey.Blocker, findings);
        }

        /// <summary>
        ///     Presents a provider to the survey registry, so the MCP tools keep reading one list and
        ///     keep agreeing with the editor by construction.
        /// </summary>
        private sealed class SurveyorAdapter : IConvaiModuleSurveyor
        {
            private readonly IConvaiSetupHealthProvider _provider;

            internal SurveyorAdapter(IConvaiSetupHealthProvider provider) => _provider = provider;

            public string ModuleId => _provider.ModuleId;

            public string DisplayName => _provider.DisplayName;

            public ConvaiModuleSurveyResult Survey(GameObject characterRoot)
            {
                if (characterRoot == null || !_provider.AppliesTo(characterRoot))
                    return ConvaiSetupHealthResult
                        .None(_provider.ModuleId, _provider.DisplayName)
                        .ToSurveyResult();

                return _provider.Inspect(characterRoot).ToSurveyResult();
            }
        }
    }
}
