using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using UnityEngine;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Collects every mesh/index pair that implements a semantic blendshape on the bound
    ///     facial meshes, not just the first matching mesh.
    /// </summary>
    public static class BlendshapeTargetCollector
    {
        public static int CollectAllMatches(
            IStandardRigBinding rigBinding,
            StandardBlendshape semantic,
            List<BlendshapeTargetKey> destination)
        {
            if (destination == null) return 0;
            if (rigBinding == null) return 0;
            if (!rigBinding.TryGetBlendshape(semantic, out SkinnedMeshRenderer mesh, out int blendshapeIndex))
                return 0;
            if (mesh == null || mesh.sharedMesh == null || blendshapeIndex < 0) return 0;

            string blendshapeName = mesh.sharedMesh.GetBlendShapeName(blendshapeIndex);
            if (string.IsNullOrWhiteSpace(blendshapeName)) return 0;

            return CollectAllMatches(rigBinding.FacialMeshes, blendshapeName, destination);
        }

        public static int CollectAllMatches(
            IReadOnlyList<SkinnedMeshRenderer> meshes,
            string blendshapeName,
            List<BlendshapeTargetKey> destination)
        {
            if (destination == null) return 0;
            if (meshes == null || string.IsNullOrWhiteSpace(blendshapeName)) return 0;

            HashSet<BlendshapeTargetKey> seen = new(destination);
            int added = 0;

            for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                SkinnedMeshRenderer renderer = meshes[meshIndex];
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                if (mesh == null) continue;

                int index = mesh.GetBlendShapeIndex(blendshapeName);
                if (index < 0) continue;

                BlendshapeTargetKey key = new(renderer, index);
                if (!key.IsValid || !seen.Add(key)) continue;

                destination.Add(key);
                added++;
            }

            return added;
        }
    }
}
