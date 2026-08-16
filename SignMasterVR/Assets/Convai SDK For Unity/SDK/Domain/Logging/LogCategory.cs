namespace Convai.Domain.Logging
{
    /// <summary>
    ///     Categories for filtering logs by subsystem.
    ///     Each category can have its own minimum log level.
    /// </summary>
    public enum LogCategory
    {
        /// <summary>General SDK operations.</summary>
        SDK = 0,

        /// <summary>Character/NPC related logs.</summary>
        Character,

        /// <summary>Audio system logs.</summary>
        Audio,

        /// <summary>UI component logs.</summary>
        UI,

        /// <summary>REST API communication logs.</summary>
        REST,

        /// <summary>Transport/connection logs.</summary>
        Transport,

        /// <summary>Event system logs.</summary>
        Events,

        /// <summary>Player/user related logs.</summary>
        Player,

        /// <summary>Editor-only logs.</summary>
        Editor,

        /// <summary>Vision capture and video publishing logs.</summary>
        Vision,

        /// <summary>Bootstrap and initialization logs.</summary>
        Bootstrap,

        /// <summary>Transcript processing and routing logs.</summary>
        Transcript,

        /// <summary>Narrative design and story system logs.</summary>
        Narrative,

        /// <summary>Lip sync processing and blendshape playback logs.</summary>
        LipSync,

        /// <summary>Body animation system logs (layers, transitions, locomotion).</summary>
        Animation,

        /// <summary>Gaze system logs (targeting, policy, eye/head/body solvers).</summary>
        Gaze,

        /// <summary>Body language system logs (gesticulation, posture, breathing, fidgets).</summary>
        BodyLanguage,

        /// <summary>
        ///     Action system logs: which commands arrived, which were dropped and why, and how
        ///     targets resolved.
        /// </summary>
        /// <remarks>
        ///     Separate from <see cref="Character" /> because it answers a different question and is
        ///     tuned to a different volume. "Why did nothing happen when I asked her to walk to the
        ///     gallery" is diagnosed by turning this category up, and a shipping build turns it down
        ///     without silencing everything else a character says about itself. Appended last on
        ///     purpose: <c>ConvaiSettings.CategoryOverrides</c> stores the enum value, so inserting
        ///     anywhere else would re-point every override a project has already saved.
        /// </remarks>
        Actions
    }
}
