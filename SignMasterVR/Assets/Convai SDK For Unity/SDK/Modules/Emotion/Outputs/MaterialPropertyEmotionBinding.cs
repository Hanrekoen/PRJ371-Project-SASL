using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Taxonomy;
using Convai.Domain.Logging;
using Convai.Runtime.Embodiment;
using Convai.Runtime.Logging;
using Convai.Runtime.Utilities;
using UnityEngine;

namespace Convai.Modules.Emotion.Outputs
{
    /// <summary>
    ///     Authoring entry that maps a taxonomy emotion label to a shader float property driven
    ///     by <see cref="MaterialPropertyEmotionBinding" />.
    /// </summary>
    /// <remarks>
    ///     Intended for BYO-shader effects with zero built-in shader knowledge in the SDK —
    ///     blush, tear glisten, sweat sheen, etc. Author the shader's exposed float property name
    ///     directly (e.g. <c>_EmotionBlush</c>); the binding writes it via a
    ///     <see cref="MaterialPropertyBlock" /> so the shared material asset is never mutated.
    /// </remarks>
    [Serializable]
    public sealed class MaterialPropertyEmotionSlot
    {
        [SerializeField, ConvaiEmotionLabel]
        [Tooltip("The emotion that drives this effect.")]
        private string emotionLabel = string.Empty;

        [SerializeField, Tooltip("Shader float property name to drive (e.g. \"_EmotionBlush\"). Leave empty to skip this slot.")]
        private string propertyName = string.Empty;

        [SerializeField, Tooltip("Property value written at zero emotion intensity (rest).")]
        private float minValue;

        [SerializeField, Tooltip("Property value written at full (1.0) emotion intensity.")]
        private float maxValue = 1f;

        public string EmotionLabel => emotionLabel;
        public string PropertyName => propertyName;
        public float MinValue => minValue;
        public float MaxValue => maxValue;

        public MaterialPropertyEmotionSlot()
        {
        }

        /// <summary>
        ///     Construct a slot programmatically. Intended for editor tooling and preset
        ///     factories; runtime authoring should still prefer the inspector.
        /// </summary>
        public MaterialPropertyEmotionSlot(string emotionLabel, string propertyName, float minValue = 0f, float maxValue = 1f)
        {
            this.emotionLabel = emotionLabel ?? string.Empty;
            this.propertyName = propertyName ?? string.Empty;
            this.minValue = minValue;
            this.maxValue = maxValue;
        }

        /// <summary>Creates a value-copy so runtime bindings never mutate shared authoring state.</summary>
        public MaterialPropertyEmotionSlot Clone() => new(emotionLabel, propertyName, minValue, maxValue);
    }

    /// <summary>
    ///     Drives arbitrary shader float properties (blush, tear glisten, sweat sheen, etc.) from
    ///     composed emotion scores via a per-renderer <see cref="MaterialPropertyBlock" />. Zero
    ///     built-in shader knowledge — authors wire their own shader's exposed float property
    ///     names through the profile inspector.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Target renderers are resolved the same way the facial expression output does
    ///         (the rig's facial meshes, falling back to a <see cref="SkinnedMeshRenderer" /> scan
    ///         under the character root). A resolved slot writes to EVERY resolved facial mesh
    ///         renderer regardless of whether that particular renderer's material declares the
    ///         property — an unsupported <see cref="MaterialPropertyBlock" /> float write is inert,
    ///         never an error, and shader variance across meshes is normal. Separately,
    ///         <see cref="Material.HasProperty(int)" /> across all target materials feeds a single
    ///         actionable warning per bind for the ALL-miss case (no authored property found on ANY
    ///         target material at all).
    ///     </para>
    ///     <para>
    ///         When multiple slots target the SAME property on the SAME renderer, their composed
    ///         intensities MAX-COMBINE before the lerp — the strongest (highest composed intensity)
    ///         slot's <c>[MinValue, MaxValue]</c> range is used for that frame's write, independent
    ///         of authoring order. Writes use get-modify-set
    ///         (<see cref="Renderer.GetPropertyBlock(MaterialPropertyBlock)" />/
    ///         <see cref="Renderer.SetPropertyBlock(MaterialPropertyBlock)" />) through a single
    ///         reused <see cref="MaterialPropertyBlock" /> instance so any other system's MPB writes
    ///         on the same renderer are preserved. A renderer is skipped entirely for a frame when
    ///         none of its properties moved by more than <see cref="PushEpsilon" /> since the last
    ///         push.
    ///     </para>
    /// </remarks>
    [Serializable]
    public sealed class MaterialPropertyEmotionBinding : IEmotionOutputBinding
    {
        /// <summary>Minimum change (in property units) required to re-push a property's value.</summary>
        private const float PushEpsilon = 0.0005f;

        [SerializeField, Tooltip("Which emotion drives which shader property. A row with no property name is ignored.")]
        private List<MaterialPropertyEmotionSlot> slots = new();

        private readonly List<ResolvedSlot> _resolvedSlots = new();
        private readonly List<RendererGroup> _rendererGroups = new();
        private MaterialPropertyBlock _scratchBlock;
        private string _sourceName;
        private bool _bound;

        public IReadOnlyList<MaterialPropertyEmotionSlot> Slots => slots;

        /// <summary>
        ///     Replaces the authored slot list. Intended for preset factories and editor
        ///     tooling; prefer the inspector for one-off authoring.
        /// </summary>
        public void SetSlots(IReadOnlyList<MaterialPropertyEmotionSlot> newSlots)
        {
            slots.Clear();
            if (newSlots == null) return;
            for (int i = 0; i < newSlots.Count; i++)
            {
                MaterialPropertyEmotionSlot slot = newSlots[i];
                if (slot != null) slots.Add(slot);
            }
        }

        /// <summary>
        ///     Creates an ephemeral runtime copy that reuses authored slot data but owns its own
        ///     bound-state caches. Shared profile assets must never bind themselves directly
        ///     because multiple characters may use the same authoring asset.
        /// </summary>
        public MaterialPropertyEmotionBinding CreateRuntimeCopy()
        {
            var copy = new MaterialPropertyEmotionBinding();
            if (slots == null) return copy;

            var clonedSlots = new List<MaterialPropertyEmotionSlot>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                MaterialPropertyEmotionSlot slot = slots[i];
                if (slot != null) clonedSlots.Add(slot.Clone());
            }

            copy.SetSlots(clonedSlots);
            return copy;
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Implemented explicitly because the contract and the rig binding it takes are
        ///     internal to the SDK — infrastructure a customer never wires up by hand. The class
        ///     stays public, since its settings are authored on the emotion profile, but this
        ///     entry point is reachable only through the interface.
        /// </remarks>
        void IEmotionOutputBinding.Bind(
            UnityEngine.Object owner,
            IEmotionTaxonomy taxonomy,
            IStandardRigBinding rig)
        {
            Unbind(owner);

            _sourceName = owner != null ? owner.name : "MaterialPropertyEmotionBinding";
            _scratchBlock ??= new MaterialPropertyBlock();

            if (taxonomy == null || slots == null) return;

            IReadOnlyList<SkinnedMeshRenderer> facialMeshes = ResolveFacialMeshes(rig, owner as Component);
            if (facialMeshes.Count == 0) return;

            bool hasAuthoredSlot = false;
            bool anyPropertyFoundOnAnyMaterial = false;
            List<string> unmatchedPropertyNames = null;

            for (int i = 0; i < slots.Count; i++)
            {
                MaterialPropertyEmotionSlot slot = slots[i];
                if (slot == null) continue;
                if (string.IsNullOrWhiteSpace(slot.PropertyName)) continue;
                if (string.IsNullOrWhiteSpace(slot.EmotionLabel)) continue;

                hasAuthoredSlot = true;
                string propertyName = slot.PropertyName.Trim();
                int propertyId = Shader.PropertyToID(propertyName);

                // The diagnostic HasProperty check is independent of writing: shader variance
                // across meshes is normal, so every resolved facial mesh is a write target
                // regardless of whether ITS material happens to declare the property (a
                // MaterialPropertyBlock write for an unsupported property is inert, not an
                // error). HasProperty only feeds the ALL-miss warn-once diagnostic below.
                bool foundOnAnyMesh = false;
                for (int m = 0; m < facialMeshes.Count; m++)
                {
                    if (!AnyMaterialHasProperty(facialMeshes[m], propertyId)) continue;
                    foundOnAnyMesh = true;
                    break;
                }

                if (foundOnAnyMesh)
                {
                    anyPropertyFoundOnAnyMaterial = true;
                }
                else
                {
                    unmatchedPropertyNames ??= new List<string>(2);
                    if (!unmatchedPropertyNames.Contains(propertyName)) unmatchedPropertyNames.Add(propertyName);
                }

                if (!taxonomy.TryResolve(slot.EmotionLabel, out EmotionDescriptor descriptor)) continue;

                int resolvedIndex = _resolvedSlots.Count;
                _resolvedSlots.Add(new ResolvedSlot(descriptor.Label, propertyId, slot.MinValue, slot.MaxValue));

                for (int m = 0; m < facialMeshes.Count; m++)
                {
                    RendererGroup group = FindOrCreateGroup(facialMeshes[m]);
                    int propertyIndex = group.PropertyIds.IndexOf(propertyId);
                    if (propertyIndex < 0)
                    {
                        group.PropertyIds.Add(propertyId);
                        group.Contributors.Add(new List<int>(1));
                        group.LastPushedValues.Add(float.NaN);
                        group.ComputedValues.Add(0f);
                        propertyIndex = group.PropertyIds.Count - 1;
                    }

                    group.Contributors[propertyIndex].Add(resolvedIndex);
                }
            }

            _bound = _resolvedSlots.Count > 0;

            // Warn ONCE per bind only in the all-miss case — at least one authored slot
            // exists but none of the authored properties were found on any target material.
            // Per-slot misses (this property absent on SOME meshes, present on others) stay
            // silent since shader variance across meshes is normal.
            if (hasAuthoredSlot && !anyPropertyFoundOnAnyMaterial)
            {
                string names = unmatchedPropertyNames != null ? string.Join(", ", unmatchedPropertyNames) : "(none)";
                ConvaiLogger.Warning(
                    $"[MaterialPropertyEmotionBinding] '{_sourceName}' has authored material-property slot(s) but " +
                    $"none of the authored shader properties ({names}) were found on any target material. Verify " +
                    "the property name(s) (e.g. \"_EmotionBlush\") match a property exposed by the character's " +
                    "assigned material(s).",
                    LogCategory.SDK);
            }
        }

        /// <inheritdoc />
        public void Apply(IReadOnlyDictionary<string, float> scores, float intensityGain)
        {
            if (!_bound || scores == null) return;

            float intensityAttenuation = intensityGain;

            for (int g = 0; g < _rendererGroups.Count; g++)
            {
                RendererGroup group = _rendererGroups[g];
                Renderer renderer = group.Renderer;
                if (renderer == null) continue; // fake-null guard (Unity's == null), never throws.

                bool anyChanged = false;
                int propertyCount = group.PropertyIds.Count;
                for (int p = 0; p < propertyCount; p++)
                {
                    List<int> contributors = group.Contributors[p];
                    float bestIntensity = 0f;
                    float bestMin = 0f;
                    float bestMax = 1f;
                    bool first = true;

                    for (int c = 0; c < contributors.Count; c++)
                    {
                        ResolvedSlot slot = _resolvedSlots[contributors[c]];
                        scores.TryGetValue(slot.Label, out float score);
                        float intensity = Mathf.Clamp01(score * intensityAttenuation);

                        // Max-combine — the strongest slot's [min, max] range wins, order-independent.
                        if (first || intensity > bestIntensity)
                        {
                            bestIntensity = intensity;
                            bestMin = slot.MinValue;
                            bestMax = slot.MaxValue;
                            first = false;
                        }
                    }

                    float value = Mathf.Lerp(bestMin, bestMax, bestIntensity);
                    group.ComputedValues[p] = value;

                    float lastValue = group.LastPushedValues[p];
                    if (float.IsNaN(lastValue) || Mathf.Abs(value - lastValue) > PushEpsilon)
                        anyChanged = true;
                }

                if (!anyChanged) continue; // Skip this renderer entirely — nothing moved enough to push.

                renderer.GetPropertyBlock(_scratchBlock);
                for (int p = 0; p < propertyCount; p++)
                {
                    _scratchBlock.SetFloat(group.PropertyIds[p], group.ComputedValues[p]);
                    group.LastPushedValues[p] = group.ComputedValues[p];
                }
                renderer.SetPropertyBlock(_scratchBlock);
            }
        }

        /// <inheritdoc />
        public void Unbind(UnityEngine.Object owner)
        {
            if (_bound && _scratchBlock != null)
            {
                for (int g = 0; g < _rendererGroups.Count; g++)
                {
                    RendererGroup group = _rendererGroups[g];
                    Renderer renderer = group.Renderer;
                    if (renderer == null) continue; // fake-null guard, never throws.

                    renderer.GetPropertyBlock(_scratchBlock);
                    int propertyCount = group.PropertyIds.Count;
                    for (int p = 0; p < propertyCount; p++)
                    {
                        // Rest value: the first contributing slot's MinValue. At rest every
                        // contributor's composed intensity is 0, so which slot "wins" the
                        // max-combine tie is not observable from the written value in the
                        // single-slot case, and is a deliberate, documented simplification here.
                        List<int> contributors = group.Contributors[p];
                        float restValue = contributors.Count > 0 ? _resolvedSlots[contributors[0]].MinValue : 0f;
                        _scratchBlock.SetFloat(group.PropertyIds[p], restValue);
                    }
                    renderer.SetPropertyBlock(_scratchBlock);
                }
            }

            _resolvedSlots.Clear();
            _rendererGroups.Clear();
            _bound = false;
        }

        private RendererGroup FindOrCreateGroup(Renderer renderer)
        {
            for (int i = 0; i < _rendererGroups.Count; i++)
            {
                if (ReferenceEquals(_rendererGroups[i].Renderer, renderer)) return _rendererGroups[i];
            }

            var group = new RendererGroup(renderer);
            _rendererGroups.Add(group);
            return group;
        }

        private static bool AnyMaterialHasProperty(Renderer renderer, int propertyId)
        {
            if (renderer == null) return false;
            Material[] materials = renderer.sharedMaterials;
            if (materials == null) return false;

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material != null && material.HasProperty(propertyId)) return true;
            }
            return false;
        }

        private static IReadOnlyList<SkinnedMeshRenderer> ResolveFacialMeshes(
            IStandardRigBinding rig,
            Component fallbackContext)
        {
            if (rig != null && rig.FacialMeshes != null && rig.FacialMeshes.Count > 0)
                return rig.FacialMeshes;

            if (fallbackContext == null) return Array.Empty<SkinnedMeshRenderer>();

            Transform root = rig?.Root;
            if (root == null)
            {
                EmbodimentContext context = fallbackContext.GetComponentInParent<EmbodimentContext>(true);
                root = context != null ? context.CharacterRoot : fallbackContext.transform;
            }

            SkinnedMeshRenderer[] discovered = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            var filtered = new List<SkinnedMeshRenderer>(discovered.Length);
            for (int i = 0; i < discovered.Length; i++)
            {
                SkinnedMeshRenderer smr = discovered[i];
                if (smr == null || smr.sharedMesh == null) continue;
                if (smr.sharedMesh.blendShapeCount == 0) continue;
                filtered.Add(smr);
            }
            return filtered;
        }

        private readonly struct ResolvedSlot
        {
            public ResolvedSlot(string label, int propertyId, float minValue, float maxValue)
            {
                Label = label;
                PropertyId = propertyId;
                MinValue = minValue;
                MaxValue = maxValue;
            }

            public string Label { get; }
            public int PropertyId { get; }
            public float MinValue { get; }
            public float MaxValue { get; }
        }

        /// <summary>
        ///     Per-renderer state: which shader properties this renderer needs written, which
        ///     resolved slots contribute to each (for the max-combine), and the last value
        ///     pushed to each so <see cref="Apply" /> can skip the renderer entirely when nothing
        ///     moved.
        /// </summary>
        private sealed class RendererGroup
        {
            public RendererGroup(Renderer renderer)
            {
                Renderer = renderer;
            }

            public Renderer Renderer { get; }
            public readonly List<int> PropertyIds = new(2);
            public readonly List<List<int>> Contributors = new(2);
            public readonly List<float> LastPushedValues = new(2);
            public readonly List<float> ComputedValues = new(2);
        }
    }
}
