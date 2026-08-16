using System.Runtime.CompilerServices;

// Every module editor assembly draws its UI with the shared Convai editor design system
// (SDK/Editor/UI), which is internal to this assembly. This list is therefore the design system's
// consumer boundary — a module editor missing from it cannot render the Convai editor frame and will
// grow its own fork, which is exactly what this system was built to end.
[assembly: InternalsVisibleTo("Convai.Editor.Embodiment")]

// The AI/MCP authoring assembly additionally shares this assembly's authoring seams — notably
// ConvaiActionBehaviorHosting, the one place an action behavior component is created. Authoring an
// action through MCP has to land the behavior on the same object the Actions Editor would, or the
// two tools disagree about the character's layout and each quietly undoes the other's arrangement.
[assembly: InternalsVisibleTo("Convai.Editor.AI")]

// The module survey vocabulary (SDK/Editor/Diagnostics/ConvaiModuleSurvey.cs) lives here rather than
// in Convai.Editor.AI, because that assembly is gated behind CONVAI_UNITY_MCP and a user without the
// AI Assistant package would otherwise have a Troubleshooter that cannot describe their character.
// Its read side — SurveyAll and All — is deliberately internal: modules register through the public
// interface and read nothing back. The Embodiment aggregator is the one legitimate reader, so it
// keeps the access it had before the file moved.
[assembly: InternalsVisibleTo("Convai.Editor.Embodiment.AI")]
[assembly: InternalsVisibleTo("Convai.Modules.BodyAnimation.Editor")]
[assembly: InternalsVisibleTo("Convai.Modules.BodyLanguage.Editor")]
[assembly: InternalsVisibleTo("Convai.Modules.Gaze.Editor")]
[assembly: InternalsVisibleTo("Convai.Modules.Emotion.Editor")]
[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]
[assembly: InternalsVisibleTo("Convai.Tests.EditMode.AI")]
