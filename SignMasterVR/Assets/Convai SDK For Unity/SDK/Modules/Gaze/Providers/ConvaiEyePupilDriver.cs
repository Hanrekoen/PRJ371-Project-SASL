using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Logging;
using Convai.Runtime.Embodiment;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Modules.Gaze.Providers
{
    /// <summary>
    ///     Opt-in material driver that dilates a character's pupils with the Gaze module's
    ///     smoothed arousal signal ("pupil response via material seam" —
    ///     <see cref="IEyeAppearanceDriver" />).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Confirmed working on the shipped Camila character (LipSync Sample): the
    ///         Reallusion RL5 cornea shader
    ///         (<c>Plugins/Reallusion/CCiC/URP/Shaders/SG/HQ/RL5_CorneaShaderParallax_URP</c>)
    ///         exposes a <c>_PupilScale</c> float property ("Pupil Scale", authored slider range
    ///         0.1-2.0, default 0.8) that multiplies into the shader's pupil-size computation. A
    ///         LARGER value enlarges the pupil — this matches the property's own name/range and
    ///         Reallusion Character Creator's "Pupil Scale" UI convention, so the default sign
    ///         here is non-inverted. If a different rig's pupil-scale property runs the opposite
    ///         direction, enable <see cref="invertSign" />.
    ///     </para>
    ///     <para>
    ///         Writes go through a single reused <see cref="MaterialPropertyBlock" /> per
    ///         renderer (get-modify-set), so the shared material asset is never mutated and any
    ///         other system's property-block writes on the same renderer are preserved. Each
    ///         renderer's base property value is read once from its
    ///         <see cref="Renderer.sharedMaterial" /> when the driver binds, and restored on
    ///         disable.
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Gaze/Advanced/Eye Pupil Driver")]
    [DisallowMultipleComponent]
    public sealed class ConvaiEyePupilDriver : MonoBehaviour, IEyeAppearanceDriver
    {
        [SerializeField]
        [Tooltip("Explicit renderer targets (cornea/eye meshes). Leave empty to auto-discover every " +
                 "SkinnedMeshRenderer under the character whose material exposes Shader Property Name.")]
        private Renderer[] renderers;

        [SerializeField]
        [Tooltip("Shader float property name to drive. Defaults to the Reallusion RL5 cornea " +
                 "shader's pupil-scale property, confirmed on the LipSync Sample's Camila character.")]
        private string shaderPropertyName = "_PupilScale";

        [SerializeField, Range(5f, 20f)]
        [Tooltip("Maximum pupil dilation at full arousal, as a percentage of each renderer's base " +
                 "property value (e.g. 12 = +12% at maximum dilation).")]
        private float maxDilationPercent = 12f;

        [SerializeField]
        [Tooltip("Flips the dilation direction. Default (off) matches the RL5 cornea shader, where " +
                 "a larger _PupilScale enlarges the pupil.")]
        private bool invertSign;

        private readonly List<RendererTarget> _targets = new();
        private MaterialPropertyBlock _scratchBlock;
        private EmbodimentContext _context;
        private CharacterServiceRegistry.ServiceToken _token;
        private int _propertyId;
        private bool _bound;
        private bool _loggedInert;
        private float _lastNormalized = -1f;

        private void OnEnable()
        {
            if (!EmbodimentContext.TryResolveFor(this, out _context))
            {
                LogInertOnce("No EmbodimentContext found in the parent hierarchy; component is inert.");
                enabled = false;
                return;
            }

            Bind();
            _token = _context.Provide<IEyeAppearanceDriver>(this);
        }

        private void OnDisable()
        {
            _token.Release();
            _token = default;
            RestoreBaseValues();
            _targets.Clear();
            _bound = false;
            _lastNormalized = -1f;
        }

        /// <inheritdoc />
        public void SetPupilDilation(float normalized01)
        {
            if (!_bound || _targets.Count == 0) return;

            float clamped = Mathf.Clamp01(normalized01);
            if (Mathf.Approximately(clamped, _lastNormalized)) return;
            _lastNormalized = clamped;

            float sign = invertSign ? -1f : 1f;
            float scale = 1f + sign * clamped * (maxDilationPercent / 100f);

            _scratchBlock ??= new MaterialPropertyBlock();
            for (int i = 0; i < _targets.Count; i++)
            {
                RendererTarget target = _targets[i];
                Renderer renderer = target.Renderer;
                if (renderer == null) continue; // fake-null guard (Unity's == null), never throws.

                renderer.GetPropertyBlock(_scratchBlock);
                _scratchBlock.SetFloat(_propertyId, target.BaseValue * scale);
                renderer.SetPropertyBlock(_scratchBlock);
            }
        }

        private void Bind()
        {
            _targets.Clear();
            _bound = false;

            if (string.IsNullOrWhiteSpace(shaderPropertyName))
            {
                LogInertOnce("Shader Property Name is empty; component is inert.");
                return;
            }

            _propertyId = Shader.PropertyToID(shaderPropertyName);

            IReadOnlyList<Renderer> candidates = ResolveCandidates();
            for (int i = 0; i < candidates.Count; i++)
            {
                Renderer renderer = candidates[i];
                if (renderer == null) continue;

                Material material = renderer.sharedMaterial;
                if (material == null || !material.HasProperty(_propertyId)) continue;

                _targets.Add(new RendererTarget(renderer, material.GetFloat(_propertyId)));
            }

            _bound = _targets.Count > 0;
            if (_bound) return;

            LogInertOnce(
                $"No renderer with a '{shaderPropertyName}' material property was found under " +
                $"'{name}'. The driver is inert until a matching renderer/property is available.");
        }

        private IReadOnlyList<Renderer> ResolveCandidates()
        {
            if (renderers != null && renderers.Length > 0) return renderers;

            Transform root = _context != null && _context.CharacterRoot != null ? _context.CharacterRoot : transform;
            return root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }

        private void RestoreBaseValues()
        {
            if (!_bound || _scratchBlock == null) return;

            for (int i = 0; i < _targets.Count; i++)
            {
                RendererTarget target = _targets[i];
                Renderer renderer = target.Renderer;
                if (renderer == null) continue; // fake-null guard, never throws.

                renderer.GetPropertyBlock(_scratchBlock);
                _scratchBlock.SetFloat(_propertyId, target.BaseValue);
                renderer.SetPropertyBlock(_scratchBlock);
            }
        }

        private void LogInertOnce(string message)
        {
            if (_loggedInert) return;
            _loggedInert = true;
            ConvaiLogger.Warning($"[ConvaiEyePupilDriver] {message}", LogCategory.Gaze);
        }

        private readonly struct RendererTarget
        {
            public RendererTarget(Renderer renderer, float baseValue)
            {
                Renderer = renderer;
                BaseValue = baseValue;
            }

            public Renderer Renderer { get; }
            public float BaseValue { get; }
        }
    }
}
