namespace Convai.Modules.BodyAnimation.Data
{
    public enum BodyAnimationValidationSeverity
    {
        Info = 0,
        Warning = 1,
        ReleaseBlocker = 2,
        Error = 3
    }

    public readonly struct BodyAnimationValidationFinding
    {
        public BodyAnimationValidationSeverity Severity { get; }
        public string Path { get; }
        public string Message { get; }

        public BodyAnimationValidationFinding(
            BodyAnimationValidationSeverity severity, string path, string message)
        {
            Severity = severity;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }
}
