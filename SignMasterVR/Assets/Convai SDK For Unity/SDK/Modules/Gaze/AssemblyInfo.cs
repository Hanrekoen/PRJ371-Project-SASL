using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Convai.Editor.Embodiment")]

// The module's own editor assembly reads the bound gaze chain and trace for the Gaze editor
// window's rig report, which is diagnostic surface rather than public API.
[assembly: InternalsVisibleTo("Convai.Modules.Gaze.Editor")]
[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]
[assembly: InternalsVisibleTo("Convai.Tests.PlayMode")]
