using System.Linq;

namespace Convai.Editor.AI
{
    public static partial class ConvaiMcpTools
    {
        private static Guidance BuildGuidance(ConvaiGuidanceTopic topic)
        {
            const string docs = "Packages/com.convai.convai-sdk-for-unity/Documentation~/";
            switch (topic)
            {
                case ConvaiGuidanceTopic.Setup:
                    return new Guidance(
                        "Complete runnable Convai scene setup proactively. An explicit setup request authorizes safe, reversible defaults; do not stop for placeholder, voice-mode, auto-connect, or save confirmations.",
                        new[] { "Convai SDK installed", "Credential status checked; API key configured outside MCP before runtime verification", "Open target scene" },
                        new[]
                        {
                            "Read project status, inspect the scene, validate setup, and capture exact instance IDs.",
                            "Preview Convai.SetupConversationScene, then apply it without duplicate confirmation for an explicit setup request.",
                            "Let the orchestrator reuse unambiguous targets or create standalone Convai Player and visible Capsule Convai Character placeholders; never repurpose Main Camera.",
                            "Default to Audio, HandsFree input, automatic connection, and inline configuration unless the user requested otherwise.",
                            "Do all independent work first. Ask only for irreducible values such as a missing Character ID or an ambiguous authored target.",
                            "Inspect, validate, and run Convai.DiagnoseConversation. Keep the scene dirty and do not save unless explicitly requested."
                        },
                        AllToolIds,
                        new[] { "Unity.ManageGameObject", "Unity.ManageScene" },
                        new[]
                        {
                            docs + "SETUP.md",
                            docs + "API-ENTRYPOINTS.md",
                            "Packages/com.convai.convai-sdk-for-unity/AIAssistantSkills/convai-unity-sdk/references/workflows.md"
                        });
                case ConvaiGuidanceTopic.Actions:
                    return FeatureGuidance(
                        "Author explicit action affordances and bind local executors; never infer affordances from scene metadata.",
                        new[] { "Convai.ConfigureActions", "Convai.DiagnoseActions", "Convai.SimulateAction" },
                        new[] { docs + "ACTIONS.md", docs + "ACTIONS-INTEGRATION-TUTORIAL.md" });
                case ConvaiGuidanceTopic.DynamicContext:
                    return TopicGuidance(
                        "Send state, events, and attention-object changes through the character dynamic-context facade.",
                        docs + "DYNAMIC-CONTEXT.md");
                case ConvaiGuidanceTopic.Vision:
                    return TopicGuidance(
                        "Configure a vision publisher and one frame source under the room hierarchy before enabling video mode.",
                        docs + "DYNAMIC-VISION-CONTEXT.md",
                        "Packages/com.convai.convai-sdk-for-unity/SDK/Modules/Vision/README.md");
                case ConvaiGuidanceTopic.Narrative:
                    return FeatureGuidance(
                        "Use the Narrative module for section state and named or inline trigger workflows.",
                        new[] { "Convai.ConfigureNarrative", "Convai.DiagnoseNarrative" },
                        new[] { "Packages/com.convai.convai-sdk-for-unity/SDK/Modules/Narrative/README.md" });
                case ConvaiGuidanceTopic.Embodiment:
                    return new Guidance(
                        "Embodiment is Convai's name for the features that give a character a body and a face: where it looks, what it feels, how it moves, and how it holds itself. They are separate, optional components that cooperate — add the ones you want and leave the rest out. Adding Convai Character and then the features is the whole setup; everything else is worked out automatically.",
                        new[]
                        {
                            "A Convai Character in the open scene",
                            "A Humanoid rig for head and eye aiming and posture — a Generic rig still gets expression and lip sync if the face has blendshapes",
                            "Face blendshapes on the head mesh for expression and lip sync",
                            "An Animator, only if the character should play body animation"
                        },
                        new[]
                        {
                            "Start with Convai.DiagnoseEmbodiment. One call reports the rig, every feature the character has, which of them will actually do something, and what is stopping the rest. It names the per-feature tool to call next, so you do not have to guess which of the five you need.",
                            "Read the readiness word before concluding anything is broken. NotInstalled means the component is not there and that is often a deliberate choice — a character that should not gesture is correctly configured without Body Animation. Blocked means the rig cannot support it. Inert means it is set up, unblocked, and still will not visibly do anything; the blocker line says why. Working means it will.",
                            "The rig is the one thing every feature shares, so read it first. A face rig Convai could not recognize confidently is the documented usual cause of \"expression does nothing\" — the fix is to set the convention manually on the Character Rig component or supply a Custom Rig Convention Map, not to change the feature.",
                            "Set a character up with Convai.ConfigureEmbodiment: it works out the rig and adds the features you name in one undoable step. It previews by default; call it again with dryRun false to apply. It adds components only — every knob belongs to the feature's own Configure tool.",
                            "Then tune each feature with its own tools: Gaze, Emotion, Body Animation and Body Language each have a Configure and a Diagnose, and Lip Sync has its own pair.",
                            "A feature with no settings asset is working, not unfinished. A settings asset shapes a character; it is not how a feature is turned on, and the built-in defaults are tuned to look right.",
                            "Presets are optional. One preset hands a settings asset to every feature at once — run Convai.InspectEmbodimentPresets to see which the project has and whether they are valid. Never create a preset or a settings asset through these tools; name the menu path instead.",
                            "Every feature degrades gracefully when a peer is absent. Gaze with no Emotion still looks around; Body Language with no Body Animation still breathes. Never add a feature just to make another one work."
                        },
                        new[]
                        {
                            "Convai.DiagnoseEmbodiment", "Convai.ConfigureEmbodiment",
                            "Convai.InspectEmbodimentPresets",
                            "Convai.ConfigureGaze", "Convai.DiagnoseGaze", "Convai.MarkGazeTarget",
                            "Convai.ConfigureLipSync", "Convai.DiagnoseLipSync",
                            "Convai.ConfigureBodyAnimation", "Convai.DiagnoseBodyAnimation",
                            "Convai.InspectBodyAnimationContent", "Convai.TuneBodyAnimationPersonality",
                            "Convai.ConfigureBodyLanguage", "Convai.DiagnoseBodyLanguage",
                            "Convai.InspectBodyLanguagePersonalities",
                            "Convai.ConfigureEmotion", "Convai.DiagnoseEmotion",
                            "Convai.InspectEmotionPersonalities", "Convai.TuneEmotionPersonality",
                            "Convai.InspectScene", "Convai.ValidateSetup"
                        },
                        new[] { "Unity.ManageGameObject", "Unity.ManageAsset" },
                        new[]
                        {
                            docs + "EMBODIMENT.md", docs + "GAZE.md", docs + "EMOTIONS.md",
                            docs + "BODY-ANIMATION.md", docs + "BODY-LANGUAGE.md"
                        });
                case ConvaiGuidanceTopic.Gaze:
                    return new Guidance(
                        "Gaze gives a character eye contact. Adding the Gaze component is the only required step — it resolves the head and eye bones from the character's rig, treats the camera tagged MainCamera as the player, and runs on the SDK defaults with no profile asset.",
                        new[]
                        {
                            "A Convai Character in the open scene",
                            "A rig with a head bone — the one thing that can stop gaze working",
                            "A camera tagged MainCamera, or a Player Anchor Override per character"
                        },
                        new[]
                        {
                            "Run Convai.DiagnoseGaze first. It reports which bones resolved, whether the rig faces the right way, and what the character treats as the player.",
                            "Add and tune gaze with Convai.ConfigureGaze. It previews by default; call it again with dryRun false to apply.",
                            "For \"why isn't it looking at me?\", read the watches block: it names whether Player Anchor Override, the character's Player Anchor, or the main camera decided the answer.",
                            "A character with no Gaze Profile is working, not broken — the profile only tunes personality. Never create one; name the menu path instead.",
                            "Use Convai.MarkGazeTarget to make a prop worth looking at. The player counts as priority 10, so a target above that outranks the player during conversation.",
                            "Everything beyond the component's own settings lives on the Gaze Profile asset, which these tools deliberately never edit."
                        },
                        new[]
                        {
                            "Convai.DiagnoseGaze", "Convai.ConfigureGaze", "Convai.MarkGazeTarget",
                            "Convai.InspectScene", "Convai.ValidateSetup"
                        },
                        new[] { "Unity.ManageGameObject", "Unity.ManageAsset" },
                        new[] { docs + "GAZE.md" });
                case ConvaiGuidanceTopic.BodyAnimation:
                    return new Guidance(
                        "Body Animation makes a character idle, talk, gesture, point and walk. Adding the component and assigning content are the only required steps — there is no Animator Controller. It is content-gated: several behaviours stay inert until the character's animation set carries clips for them, and that is a content gap, not a setup fault.",
                        new[]
                        {
                            "A Convai Character in the open scene",
                            "An Animator with a valid Humanoid avatar — the one thing that can stop it working",
                            "A baked NavMesh, only if the character should walk"
                        },
                        new[]
                        {
                            "Run Convai.DiagnoseBodyAnimation first. Its readiness state separates the three ways a character can be unfinished: NotInstalled, Blocked (the rig), and NeedsContent (no animation set).",
                            "Read the features list before concluding anything is broken. A behaviour marked NeedsContent is set up but has no clips on this character; ContentIdle means the clips exist and the setting that plays them is off; FallbackTier means a documented fallback is running and nothing is wrong.",
                            "Add and set up a character with Convai.ConfigureBodyAnimation. It previews by default; call it again with dryRun false to apply.",
                            "Run Convai.InspectBodyAnimationContent before writing code — it lists every action name and alias PlayAction accepts, plus the locomotion coverage.",
                            "Movement is genuinely optional. A character with no movement component idles, talks, gestures and points perfectly well; never report its absence as a fault.",
                            "Tune personality with Convai.TuneBodyAnimationPersonality. One config can be shared by many characters, so it copies the config for the named character before changing anything, and needs makeConfigUnique consent to do so.",
                            "Never create an animation set, tag a clip, measure clips, or edit an Animator Controller through these tools. Name the Body Animation Editor window or the menu path instead."
                        },
                        new[]
                        {
                            "Convai.DiagnoseBodyAnimation", "Convai.ConfigureBodyAnimation",
                            "Convai.InspectBodyAnimationContent", "Convai.TuneBodyAnimationPersonality",
                            "Convai.InspectScene", "Convai.ValidateSetup"
                        },
                        new[] { "Unity.ManageGameObject", "Unity.ManageAsset" },
                        new[] { docs + "BODY-ANIMATION.md" });
                case ConvaiGuidanceTopic.BodyLanguage:
                    return new Guidance(
                        "Body Animation moves the body; Body Language makes it speak — breathing, posture, weight shifts, sway, co-speech gesturing and embodied listening. Adding the component is the only required step: no clips, no Animator Controller, and no profile asset. A character with no personality assigned is working, not unfinished.",
                        new[]
                        {
                            "A Convai Character in the open scene",
                            "An Animator with a Spine bone mapped — the one thing that can stop it working"
                        },
                        new[]
                        {
                            "Run Convai.DiagnoseBodyLanguage first. Its readiness state separates the two ways a character can be unfinished: NotInstalled, and Blocked (the rig). There is no content state — this module needs none.",
                            "For \"why isn't this character moving?\", read whyItMightNotMove. It orders the real causes cheapest first: a blocked rig, Expressiveness set to Subtle, a master switch such as Sway On The Spot turned off, a rig that simply has no hips, or another module holding the pose.",
                            "Read the coordination block before concluding anything is broken. This module shares the body with Body Animation and Gaze: with Body Animation present, walking and full-body actions deliberately reduce posture and gesticulation while head-beats and breathing stay; with Gaze present, Gaze composes the head gestures; with neither, nothing ever ducks the character and it moves its own head.",
                            "A missing optional bone is a legitimate rig, not a fault. No shoulders means shoulder lift and tension stay off; no hips means no weight shifts. Never report those as faults.",
                            "Add the component with Convai.ConfigureBodyLanguage. It previews by default; call it again with dryRun false to apply. It refuses a rig that cannot drive the module rather than adding something inert.",
                            "Run Convai.InspectBodyLanguagePersonalities to see the personalities the project has before assigning one. A personality shared by several characters tunes all of them.",
                            "Every amplitude, cadence and toggle lives on the Body Language Profile asset, which these tools deliberately never create or edit. Name the menu path or the Inspector field instead."
                        },
                        new[]
                        {
                            "Convai.DiagnoseBodyLanguage", "Convai.ConfigureBodyLanguage",
                            "Convai.InspectBodyLanguagePersonalities",
                            "Convai.InspectScene", "Convai.ValidateSetup"
                        },
                        new[] { "Unity.ManageGameObject", "Unity.ManageAsset" },
                        new[] { docs + "BODY-LANGUAGE.md" });
                case ConvaiGuidanceTopic.Emotion:
                    return new Guidance(
                        "Give a Convai character a face that reacts to what is said, and tune its temperament without restyling every other character that shares its personality.",
                        new[]
                        {
                            "A Convai Character in the open scene",
                            "A skinned mesh with blendshapes under it — the one thing that can stop it working"
                        },
                        new[]
                        {
                            "Run Convai.DiagnoseEmotion first. Its readiness state separates three ways a character can be unfinished: NotInstalled, Blocked (no face to move), and Inert — set up, unblocked, and still never going to change expression because emotion detection is Off.",
                            "Never report a setting from its stored value. Read the behaviour block, which reports what a user would actually observe. Three settings look on and do nothing: a personality that did not come from a character type stores false for the switches the documentation calls on by default; the four conversation-beat reactions only play when 'Never sits perfectly still' is on; and 'Picks up other characters' moods' does nothing in a scene holding one character.",
                            "Emotion detection has three settings and the words matter: Responsive updates while the reply is spoken and is the default, Accurate reads the whole reply once and works in any language, Off means the character never receives anything to feel. Never say NRCLex or LLM to a user.",
                            "For \"what is this character resting at?\", read restingMood.decidedBy. Three things can decide it and they are not interchangeable: the personality's own resting mood, this character's override, or an override deliberately forcing a neutral rest that suppresses the personality. A runtime SetMood call outranks all three, and in Play Mode the live value is reported beside the authored one.",
                            "Add the component with Convai.ConfigureEmotion. It previews by default; call it again with dryRun false to apply. It writes only fields on the character — detection, the personality reference, and this character's own resting mood.",
                            "Run Convai.InspectEmotionPersonalities before assigning one. A personality used by several characters tunes all of them, and the four that ship with the SDK cannot be edited in place at all.",
                            "Change how a character feels with Convai.TuneEmotionPersonality. It previews first and refuses to touch a shared or SDK-shipped personality until makePersonalityUnique is passed, at which point it copies it for that character and writes only the copy. No tool here ever creates a personality from nothing."
                        },
                        new[]
                        {
                            "Convai.DiagnoseEmotion", "Convai.ConfigureEmotion",
                            "Convai.InspectEmotionPersonalities", "Convai.TuneEmotionPersonality",
                            "Convai.InspectScene", "Convai.ValidateSetup"
                        },
                        new[] { "Unity.ManageGameObject", "Unity.ManageAsset" },
                        new[] { docs + "EMOTIONS.md" });
                case ConvaiGuidanceTopic.Events:
                    return TopicGuidance(
                        "Prefer ConvaiManager.Events for typed code and relay components for Inspector-driven UnityEvents.",
                        docs + "WORKING-WITH-EVENTS.md");
                case ConvaiGuidanceTopic.Runtime:
                    return FeatureGuidance(
                        "Use ConvaiManager for session ownership, Audio for room audio, and Transcripts for canonical history.",
                        new[] { "Convai.DiagnoseConversation", "Convai.TraceRuntimeEvents", "Convai.DiagnoseActions", "Convai.DiagnoseTranscripts" },
                        new[] { docs + "API-ENTRYPOINTS.md", docs + "TROUBLESHOOTING.md" });
                default:
                    return new Guidance(
                        "Use Convai tools only for SDK-aware operations; compose official Unity MCP tools for generic project changes.",
                        new[] { "Unity AI Assistant 2.13+", "Unity MCP client approved", "Convai SDK installed" },
                        new[]
                        {
                            "Load topic guidance.",
                            "Inspect before mutation.",
                            "Use exact instance IDs.",
                            "Validate after mutation.",
                            "Never pass API keys through MCP."
                        },
                        new[]
                        {
                            "Convai.GetGuidance",
                            "Convai.GetProjectStatus",
                            "Convai.InspectScene",
                            "Convai.ValidateSetup"
                        },
                        new[]
                        {
                            "Unity.ManageGameObject",
                            "Unity.ManageAsset",
                            "Unity.ManageScript",
                            "Unity.ManageScene"
                        },
                        new[] { docs + "README.md", docs + "API-ENTRYPOINTS.md" });
            }
        }

        private static Guidance TopicGuidance(string summary, params string[] documentation) => new(
            summary,
            new[] { "Inspect relevant Convai components and profile assets before changes." },
            new[]
            {
                "Read the referenced feature documentation.",
                "Use official Unity tools for generic scripts and GameObjects.",
                "Use Convai feature tools when available.",
                "Run Convai.ValidateSetup after configuration."
            },
            new[] { "Convai.GetGuidance", "Convai.InspectScene", "Convai.ValidateSetup" },
            new[] { "Unity.ManageGameObject", "Unity.ManageAsset", "Unity.ManageScript" },
            documentation);

        private static Guidance FeatureGuidance(string summary, string[] featureTools, string[] documentation) => new(
            summary,
            new[] { "Inspect relevant Convai components and profile assets before changes." },
            new[]
            {
                "Read the referenced feature documentation.",
                "Diagnose before repair.",
                "Use official Unity tools for generic scripts and GameObjects.",
                "Preview and apply only missing or broken Convai configuration.",
                "Run feature diagnosis and Convai.ValidateSetup after configuration."
            },
            new[] { "Convai.GetGuidance", "Convai.InspectScene", "Convai.ValidateSetup" }
                .Concat(featureTools ?? System.Array.Empty<string>()).ToArray(),
            new[] { "Unity.ManageGameObject", "Unity.ManageAsset", "Unity.ManageScript" },
            documentation);

        private static readonly string[] AllToolIds = ConvaiMcpToolCatalog.All.ToArray();

        private sealed class Guidance
        {
            public Guidance(
                string summary,
                string[] prerequisites,
                string[] workflow,
                string[] convaiTools,
                string[] unityTools,
                string[] documentation)
            {
                Summary = summary;
                Prerequisites = prerequisites;
                Workflow = workflow;
                ConvaiTools = convaiTools;
                UnityTools = unityTools;
                Documentation = documentation;
            }

            public string Summary { get; }
            public string[] Prerequisites { get; }
            public string[] Workflow { get; }
            public string[] ConvaiTools { get; }
            public string[] UnityTools { get; }
            public string[] Documentation { get; }
        }
    }
}
