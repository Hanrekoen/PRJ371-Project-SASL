using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using UnityEngine;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     User-authored mapping from Convai semantic blendshape ids to a custom rig's
    ///     concrete blendshape names.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CustomRigConventionMap",
        menuName = "Convai/Embodiment/Custom Rig Convention Map",
        order = 161)]
    public sealed class CustomRigConventionMap : ScriptableObject
    {
        [Tooltip("Maps each Convai semantic blendshape id to the matching blendshape name on this rig. " +
                 "Leave a mapping's name empty to skip that semantic id on this rig.")]
        [SerializeField]
        private List<BlendshapeMapping> blendshapes = new();

        public IReadOnlyList<BlendshapeMapping> Blendshapes => blendshapes;

        public bool TryGetBlendshapeName(StandardBlendshape semantic, out string blendshapeName)
        {
            blendshapeName = null;
            if (blendshapes == null) return false;

            for (int i = 0; i < blendshapes.Count; i++)
            {
                BlendshapeMapping mapping = blendshapes[i];
                if (mapping.Semantic != semantic) continue;
                if (string.IsNullOrWhiteSpace(mapping.BlendshapeName)) return false;

                blendshapeName = mapping.BlendshapeName;
                return true;
            }

            return false;
        }

        [Serializable]
        public struct BlendshapeMapping
        {
            [SerializeField] private StandardBlendshape semantic;
            [SerializeField] private string blendshapeName;

            public BlendshapeMapping(StandardBlendshape semantic, string blendshapeName)
            {
                this.semantic = semantic;
                this.blendshapeName = blendshapeName;
            }

            public StandardBlendshape Semantic => semantic;
            public string BlendshapeName => blendshapeName;
        }
    }
}
