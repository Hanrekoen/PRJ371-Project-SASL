using UnityEngine;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Centralized execution order contract for every Convai embodiment component. Orders are
    ///     grouped into three bands so the Animator pass always sees embodiment state in the right
    ///     order: composition root and lifecycle helpers run first (negative band), per-frame
    ///     cognition runs during <c>Update</c> (positive band), and procedural pose drivers plus the
    ///     facial compositor run after the humanoid pass (high positive band).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Which loop each band runs in.</b> The negative and positive bands are reached from
    ///         <c>Update</c>: the tick scheduler at <see cref="TickScheduler" /> drives every
    ///         <c>IEmbodimentTickable</c> through cognition → expression → finalize there. The high
    ///         band actuates from <c>LateUpdate</c>, which is the only place a write can land after
    ///         the Animator has posed the skeleton — <see cref="BodyPose" /> layers posture and
    ///         breath onto the spine, <see cref="Gaze" /> aims the head and eyes on top of it, and
    ///         <see cref="FacialCompositor" /> flushes the composed blendshape weights last.
    ///     </para>
    ///     <para>
    ///         This note used to claim the opposite — that no band was <c>LateUpdate</c> — while
    ///         Gaze, Body Language and the compositor all actuated from it. The ordering constants
    ///         were right; only the sentence describing them was wrong.
    ///     </para>
    /// </remarks>
    /// <remarks>
    ///     Adding a new embodiment component? Place its execution order constant here and
    ///     reference it via <c>[DefaultExecutionOrder(EmbodimentExecutionOrders.X)]</c> rather
    ///     than a magic number so the cross-module ordering stays auditable in one file.
    /// </remarks>
    internal static class EmbodimentExecutionOrders
    {
        // Composition root + lifecycle band ----------------------------------
        /// <summary>Embodiment context — runs first so modules can resolve it during Awake.</summary>
        public const int Context = -1000;

        /// <summary>Character embodiment binding — applies presets before any module reads its profile.</summary>
        public const int Binding = -500;

        // Cognition band (Update phase) --------------------------------------
        /// <summary>Conversation flow — folds upstream signals into the dialogue state machine.</summary>
        public const int ConversationFlow = 100;

        /// <summary>Emotion — folds reactions into the smoothed emotion reading.</summary>
        public const int Emotion = 300;

        /// <summary>Body animation — layers, variant selection, and locomotion sync for the next pose tick.</summary>
        public const int BodyAnimation = 400;

        // Late pose + compositor band ----------------------------------------
        // Reached from Update(), not LateUpdate(): the scheduler ticks from Update and these
        // orders simply place the pose actuators and the compositor after every module's Update.
        /// <summary>Embodiment tick scheduler — flushes deterministic per-frame ticks before pose actuators.</summary>
        public const int TickScheduler = 18000;

        /// <summary>Animator conductor — single-writer pass over animator layer weights.</summary>
        public const int AnimatorConductor = 19000;

        /// <summary>Procedural body pose adjustments.</summary>
        public const int BodyPose = 19300;

        /// <summary>Gaze solver chain — runs after the Animator pose, before the facial compositor flushes.</summary>
        public const int Gaze = 19400;

        /// <summary>Facial blendshape compositor host — flushes composed weights to the meshes.</summary>
        public const int FacialCompositor = 20000;
    }
}
