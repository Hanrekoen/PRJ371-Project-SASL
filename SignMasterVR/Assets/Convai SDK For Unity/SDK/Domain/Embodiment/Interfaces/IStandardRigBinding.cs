using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using UnityEngine;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Abstraction over a character skeleton and skinned meshes that lets behavior
    ///     modules reference rig elements via semantic identifiers rather than bone names or
    ///     blendshape indices.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Implemented once per character at setup time by
    ///         <c>Convai.Runtime.Animation.StandardRigBinding</c>. Resolution tables are
    ///         built from the detected <see cref="RigConvention" /> and any user-authored
    ///         overrides.
    ///     </para>
    ///     <para>
    ///         Modules MUST NOT cache the returned <see cref="Transform" /> or
    ///         <c>BlendshapeTargetKey</c> references beyond the current setup cycle because
    ///         the user may rebind the character at runtime (e.g. hot-swapping outfits).
    ///         Instead, resolve during <c>Initialize</c> and again after a rebind event.
    ///     </para>
    /// </remarks>
    public interface IStandardRigBinding
    {
        /// <summary>
        ///     Root transform of the bound rig. When the visual rig lives under a wrapper
        ///     object, this returns the rig/animator root rather than the wrapper.
        /// </summary>
        Transform Root { get; }

        /// <summary>All skinned meshes that carry facial blendshapes for this character.</summary>
        IReadOnlyList<SkinnedMeshRenderer> FacialMeshes { get; }

        /// <summary>Rig convention detected at setup; <see cref="RigConvention.Unknown" /> when not resolved.</summary>
        RigConvention DetectedConvention { get; }

        /// <summary>
        ///     Resolves a semantic bone identifier to a concrete transform.
        /// </summary>
        /// <returns><c>true</c> when the bone is available on this rig.</returns>
        bool TryGetBone(StandardBone semantic, out Transform bone);

        /// <summary>
        ///     Resolves a semantic blendshape identifier to a concrete mesh + index.
        /// </summary>
        /// <returns><c>true</c> when the blendshape is available on this rig.</returns>
        bool TryGetBlendshape(
            StandardBlendshape semantic,
            out SkinnedMeshRenderer mesh,
            out int blendshapeIndex);
    }
}
