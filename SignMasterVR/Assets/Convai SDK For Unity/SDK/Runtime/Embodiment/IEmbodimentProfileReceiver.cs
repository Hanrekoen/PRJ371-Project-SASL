using UnityEngine;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Implemented by embodiment modules that can receive an optional authoring profile
    ///     from a character-level preset.
    /// </summary>
    public interface IEmbodimentProfileReceiver
    {
        /// <summary>Stable module identifier used by ConvaiEmbodimentPreset entries.</summary>
        string ModuleId { get; }

        /// <summary>Returns true when this module can consume the supplied profile asset.</summary>
        bool CanApplyProfile(ScriptableObject candidate);

        /// <summary>Applies the supplied profile asset. Passing null clears the authored override.</summary>
        bool ApplyProfile(ScriptableObject candidate);
    }
}
