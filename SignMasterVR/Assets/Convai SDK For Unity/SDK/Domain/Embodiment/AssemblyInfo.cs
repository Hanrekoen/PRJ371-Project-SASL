using System.Runtime.CompilerServices;

// The cross-module contracts in this assembly are internal: they are how one Convai feature talks to
// another, not API a customer binds to. Every assembly that implements or consumes one needs
// friendship, so this list IS the boundary — extending it is a deliberate review decision, and a
// guard test asserts it matches the reviewed set.
//
// Honest limitation: friendship is assembly-wide, so this reduces the SDK's *public* surface without
// giving the embodiment layer real encapsulation. Extracting a Convai.Runtime.Embodiment assembly
// with a narrow friend list is the correct end state and is deliberately deferred.
[assembly: InternalsVisibleTo("Convai.Runtime")]
[assembly: InternalsVisibleTo("Convai.Modules.BodyAnimation")]
[assembly: InternalsVisibleTo("Convai.Modules.BodyLanguage")]
[assembly: InternalsVisibleTo("Convai.Modules.ConversationFlow")]
[assembly: InternalsVisibleTo("Convai.Modules.Embodiment")]
[assembly: InternalsVisibleTo("Convai.Modules.Emotion")]
[assembly: InternalsVisibleTo("Convai.Modules.Gaze")]
[assembly: InternalsVisibleTo("Convai.Modules.LipSync")]
[assembly: InternalsVisibleTo("Convai.Editor")]
[assembly: InternalsVisibleTo("Convai.Editor.Embodiment")]
[assembly: InternalsVisibleTo("Convai.Modules.BodyAnimation.Editor")]
[assembly: InternalsVisibleTo("Convai.Modules.BodyLanguage.Editor")]
[assembly: InternalsVisibleTo("Convai.Modules.Emotion.Editor")]
[assembly: InternalsVisibleTo("Convai.Modules.Gaze.Editor")]
[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]
[assembly: InternalsVisibleTo("Convai.Tests.PlayMode")]
