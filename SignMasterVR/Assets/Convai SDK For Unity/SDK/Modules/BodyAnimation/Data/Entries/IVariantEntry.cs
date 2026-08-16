using System.Collections.Generic;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Data
{
    /// <summary>
    ///     Common shape of a selectable clip variant (idle, talk, …) so one scheduler can
    ///     drive weighted, emotion-aware, no-immediate-repeat selection for every pool.
    /// </summary>
    public interface IVariantEntry
    {
        AnimationClip Clip { get; }
        float Weight { get; }
        IReadOnlyList<EmotionAffinity> Affinities { get; }
        bool IsValid { get; }
    }
}
