namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     One problem <see cref="ConvaiBodyAnimationSet.CollectFindings" /> found while auditing a
    ///     set. Severity is assigned at the call site that raises the finding — never inferred from
    ///     the message text — so rewording <see cref="Message" /> can never reclassify it.
    ///     <see cref="Id" /> is the stable, code-facing contract: it is what a consumer
    ///     (the troubleshooter, tests) should key off, never <see cref="Message" />.
    /// </summary>
    public readonly struct BodyAnimationFinding
    {
        /// <summary>
        ///     Stable, never-localized identifier, e.g. <c>set.idle.missing</c>. Kept stable across
        ///     releases — treat renaming one as a breaking change to tooling that keys off it.
        /// </summary>
        public string Id { get; }

        /// <summary>How serious the finding is, assigned where the finding is raised.</summary>
        public BodyAnimationValidationSeverity Severity { get; }

        /// <summary>Human-readable, actionable description. Free to reword without touching <see cref="Id" /> or <see cref="Severity" />.</summary>
        public string Message { get; }

        /// <summary>
        ///     Identifier of a mechanical repair a surface may offer for this finding, matching the
        ///     name the editor's fix plumbing uses (e.g. <c>"GenerateUpperBodyMask"</c>). Empty when
        ///     the finding needs a human decision and has no one-click fix.
        /// </summary>
        public string FixId { get; }

        public BodyAnimationFinding(string id, BodyAnimationValidationSeverity severity, string message, string fixId = "")
        {
            Id = id ?? string.Empty;
            Severity = severity;
            Message = message ?? string.Empty;
            FixId = fixId ?? string.Empty;
        }
    }
}
