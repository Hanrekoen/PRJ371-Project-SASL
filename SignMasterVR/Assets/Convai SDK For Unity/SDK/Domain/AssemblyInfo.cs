using System.Runtime.CompilerServices;

// Allow Runtime (which now includes Application layer) to access internal members
[assembly: InternalsVisibleTo("Convai.Runtime")]
[assembly: InternalsVisibleTo("Convai.Modules.Emotion")]
[assembly: InternalsVisibleTo("Convai.Transport.Native")]
[assembly: InternalsVisibleTo("Convai.Transport.WebGL")]

// Editor tooling seam the Actions Editor's Test Run sets the internal
// ConvaiActionCommand.BypassSpeechGate/BypassAvailability flags on locally injected commands.
[assembly: InternalsVisibleTo("Convai.Editor")]

// Allow test assemblies to access internal members for unit testing
[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]
[assembly: InternalsVisibleTo("Convai.Tests.PlayMode")]
