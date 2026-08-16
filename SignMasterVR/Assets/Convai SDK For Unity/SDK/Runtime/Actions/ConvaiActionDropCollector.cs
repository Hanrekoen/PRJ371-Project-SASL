using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Whether anything is currently interested in *why* an action command was dropped.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Describing a drop costs a sentence, a candidate list and the joins that build them.
    ///         That is a price worth paying while someone is diagnosing and pure waste in a shipped
    ///         build, so the price is only paid when the answer would actually be read: the console
    ///         is listening at <see cref="LogCategory.Actions" />, or a tool is attached.
    ///     </para>
    ///     <para>
    ///         The tool count exists because a project can turn the Actions category down to
    ///         <c>Error</c> and still expect the Actions Editor to show what it dropped — the window
    ///         is the diagnosis, so it must not go blank as a side effect of quietening the console.
    ///     </para>
    /// </remarks>
    internal static class ConvaiActionDropReporting
    {
        private static int _attachedTools;

        /// <summary>
        ///     True when a dropped command's explanation will reach someone. Checked before building
        ///     one, so that a build nobody is diagnosing does no string work on the drop path at all.
        /// </summary>
        internal static bool DetailWanted =>
            _attachedTools > 0 || LoggingConfig.IsWarningEnabled(LogCategory.Actions);

        /// <summary>Registers a tool that displays drop detail regardless of console verbosity.</summary>
        internal static void AttachTool() => _attachedTools++;

        /// <summary>Balances <see cref="AttachTool" />; never goes below zero.</summary>
        internal static void DetachTool() => _attachedTools = Math.Max(0, _attachedTools - 1);
    }

    /// <summary>
    ///     Gathers the outcome of one action-response filter pass: how many commands were dropped for
    ///     each reason, and — when anyone is listening — why each of them was.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Counting and explaining are deliberately split. The counts are cheap, always collected,
    ///         and are what the summary line and the existing diagnostic event have always reported.
    ///         The explanations are expensive, optional, and are what makes a drop actionable.
    ///     </para>
    ///     <para>
    ///         Nothing here logs. A collector describes what happened; deciding how loudly to say it —
    ///         and how often to repeat it — belongs to whoever owns the channel, which keeps the
    ///         parser free of console policy and lets one dropped-command story be told once no matter
    ///         how many times the model repeats it.
    ///     </para>
    /// </remarks>
    internal sealed class ConvaiActionDropCollector
    {
        private static readonly IReadOnlyList<ConvaiActionDropReport> NoReports =
            Array.Empty<ConvaiActionDropReport>();

        private static readonly Dictionary<string, int> NoCounts = new(StringComparer.Ordinal);

        private readonly bool _wantsDetail;
        private Dictionary<string, int> _countsByReason;
        private List<ConvaiActionDropReport> _reports;

        internal ConvaiActionDropCollector()
            : this(ConvaiActionDropReporting.DetailWanted)
        {
        }

        /// <summary>Test seam: pins whether detail is gathered, independent of console verbosity.</summary>
        internal ConvaiActionDropCollector(bool wantsDetail) => _wantsDetail = wantsDetail;

        /// <summary>
        ///     Whether to build an explanation for the drop being recorded. Call sites check this
        ///     before constructing a report, so the strings are never built for nobody.
        /// </summary>
        internal bool WantsDetail => _wantsDetail;

        /// <summary>How many commands were dropped, across every reason.</summary>
        internal int DroppedCount { get; private set; }

        /// <summary>Per-reason counts, keyed by the stable wire name of the reason.</summary>
        internal IReadOnlyDictionary<string, int> CountsByReason => _countsByReason ?? NoCounts;

        /// <summary>The explanations gathered, or an empty list when nobody asked for them.</summary>
        internal IReadOnlyList<ConvaiActionDropReport> Reports => _reports ?? NoReports;

        /// <summary>Counts one dropped command without explaining it.</summary>
        internal void Count(ConvaiActionDropReason reason, int howMany = 1)
        {
            if (howMany <= 0) return;

            DroppedCount += howMany;
            _countsByReason ??= new Dictionary<string, int>(StringComparer.Ordinal);
            string key = ConvaiActionDropReport.ReasonKey(reason);
            _countsByReason[key] = _countsByReason.TryGetValue(key, out int existing)
                ? existing + howMany
                : howMany;
        }

        /// <summary>
        ///     Counts a dropped command and keeps the explanation of why. The report is only built by
        ///     the caller when <see cref="WantsDetail" /> is true.
        /// </summary>
        internal void Add(in ConvaiActionDropReport report)
        {
            Count(report.Reason);
            _reports ??= new List<ConvaiActionDropReport>();
            _reports.Add(report);
        }
    }
}
