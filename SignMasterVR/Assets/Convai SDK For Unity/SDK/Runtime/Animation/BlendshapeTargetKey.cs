using System;
using UnityEngine;
using Convai.Shared.Compatibility;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Stable runtime key for a specific blendshape target on a specific mesh.
    /// </summary>
    public readonly struct BlendshapeTargetKey : IEquatable<BlendshapeTargetKey>
    {
        public BlendshapeTargetKey(SkinnedMeshRenderer mesh, int blendshapeIndex)
        {
            Mesh = mesh;
            BlendshapeIndex = blendshapeIndex;
        }

        public SkinnedMeshRenderer Mesh { get; }
        public int BlendshapeIndex { get; }

        public bool IsValid =>
            Mesh != null &&
            Mesh.sharedMesh != null &&
            BlendshapeIndex >= 0 &&
            BlendshapeIndex < Mesh.sharedMesh.blendShapeCount;

        public bool Equals(BlendshapeTargetKey other) =>
            ReferenceEquals(Mesh, other.Mesh) && BlendshapeIndex == other.BlendshapeIndex;

        public override bool Equals(object obj) => obj is BlendshapeTargetKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int meshHash = ConvaiObjectId.Of(Mesh).GetHashCode();
                return (meshHash * 397) ^ BlendshapeIndex;
            }
        }
    }
}
