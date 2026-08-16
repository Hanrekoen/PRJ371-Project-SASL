using System.Collections.Generic;
using UnityEngine;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Single authoritative writer for <see cref="Animator" /> parameters driven by
    ///     embodiment modules.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Behavior modules submit named parameter writes through this conductor instead
    ///         of calling <see cref="Animator.SetFloat(int,float)" /> and friends directly.
    ///         The conductor records the caller that registered each parameter and refuses
    ///         conflicting registrations, which makes animator parameter collisions impossible
    ///         to produce accidentally.
    ///     </para>
    ///     <para>
    ///         Registration validates the animator controller when it is already assigned:
    ///         parameters absent from the controller are not registered, so modules that
        ///         optionally drive animator floats (emotion, posture) stay quiet on
    ///         minimal rigs. Writes to a registered parameter that later disappears fail
    ///         silently without per-frame logging.
    ///     </para>
    ///     <para>
    ///         This component is a hidden infrastructure MonoBehaviour and is auto-provisioned
    ///         by EmbodimentContext on the character root.
    ///     </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(Convai.Runtime.Embodiment.EmbodimentExecutionOrders.AnimatorConductor)]
    [AddComponentMenu("")]
    internal sealed class AnimatorConductor : MonoBehaviour
    {
        private readonly Dictionary<string, OwnedParameter> _ownedParameters = new(32);
        private readonly Dictionary<int, bool> _parameterExistenceCache = new(32);
        private readonly Dictionary<int, OwnedLayer> _ownedLayers = new(4);
        private readonly HashSet<string> _warnedUnregisteredParameters = new();

        // Ownership conflicts are reported once per parameter and once per layer. Both checks sit
        // on write paths that modules call every frame, so an unguarded report would spam the
        // console forever and build a string on every frame it spammed.
        private readonly HashSet<string> _warnedForeignParameterWrites = new();
        private readonly HashSet<int> _warnedForeignLayerWrites = new();

        private Animator _animator;
        private bool _animatorCached;

        /// <summary>Animator being written to. Resolves lazily from the owning hierarchy.</summary>
        public Animator Animator
        {
            get
            {
                if (!_animatorCached)
                {
                    _animator = GetComponentInChildren<Animator>(true);
                    _animatorCached = true;
                }
                return _animator;
            }
        }

        /// <summary>Forces the conductor to re-resolve the animator from the character hierarchy.</summary>
        public void RefreshAnimator()
        {
            _animator = GetComponentInChildren<Animator>(true);
            _animatorCached = true;
            _parameterExistenceCache.Clear();
        }

        /// <summary>Locates or creates a conductor on the supplied component's character root.</summary>
        public static AnimatorConductor GetOrCreate(Component context)
        {
            if (context == null) return null;

            AnimatorConductor existing = context.GetComponentInParent<AnimatorConductor>(true);
            if (existing != null) return existing;

            GameObject owner = context.gameObject;
            if (!UnityEngine.Application.isPlaying)
                return null;

            AnimatorConductor created = owner.AddComponent<AnimatorConductor>();
            created.hideFlags = Convai.Runtime.Embodiment.EmbodimentContext.RuntimeInfrastructureHideFlags();
            return created;
        }

        private void Awake()
        {
            hideFlags = Convai.Runtime.Embodiment.EmbodimentContext.RuntimeInfrastructureHideFlags();
        }

        /// <summary>
        ///     Registers that <paramref name="owner" /> will drive <paramref name="parameterName" />
        ///     of the specified <paramref name="type" />.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Call during module <c>Initialize</c>. A second registration for the same
        ///         parameter from a different owner is rejected and logged ; both modules must
        ///         agree on ownership (typically enforced by the behavior preset).
        ///     </para>
        /// </remarks>
        /// <returns>
        ///     <c>true</c> when registration succeeded and the owner may submit writes.
        ///     <c>false</c> when the animator already has a controller loaded and does not
        ///     define this parameter (optional outputs are skipped without error).
        /// </returns>
        public bool RegisterParameter(Object owner, string parameterName, AnimatorParameterType type)
        {
            if (owner == null || string.IsNullOrEmpty(parameterName)) return false;

            if (_ownedParameters.TryGetValue(parameterName, out OwnedParameter existing))
            {
                if (existing.Owner == owner && existing.Type == type)
                    return true;

                ConvaiLogger.Warning(
                    $"[AnimatorConductor] Parameter '{parameterName}' is already owned by " +
                    $"'{existing.OwnerName}' ({existing.Type}); rejected registration from " +
                    $"'{owner.name}' ({type}). Remove the duplicate component or give it a different parameter.",
                    LogCategory.Character);
                return false;
            }

            Animator animator = Animator;
            if (animator != null
                && animator.runtimeAnimatorController != null
                && !AnimatorHasParameter(animator, parameterName, type))
            {
                return false;
            }

            _ownedParameters[parameterName] = new OwnedParameter(owner, owner.name, type);

            // Ownership changed, so a conflict reported against the previous owner is spent. Clear
            // it, or the once-per-parameter guard would silence a genuine new conflict forever.
            _warnedForeignParameterWrites.Remove(parameterName);
            _warnedUnregisteredParameters.Remove(parameterName);
            return true;
        }

        /// <summary>
        ///     Releases a previously registered parameter. Safe to call from <c>OnDisable</c>
        ///     or <c>OnDestroy</c>.
        /// </summary>
        public void UnregisterParameter(Object owner, string parameterName)
        {
            if (owner == null || string.IsNullOrEmpty(parameterName)) return;

            if (_ownedParameters.TryGetValue(parameterName, out OwnedParameter existing) &&
                existing.Owner == owner)
            {
                _ownedParameters.Remove(parameterName);
            }
        }

        /// <summary>Writes a float value. The caller must own the parameter.</summary>
        public bool WriteFloat(Object owner, string parameterName, float value)
        {
            if (!TryResolveOwnedHash(owner, parameterName, AnimatorParameterType.Float, out int hash))
                return false;

            Animator.SetFloat(hash, value);
            return true;
        }

        /// <summary>Writes an int value. The caller must own the parameter.</summary>
        public bool WriteInt(Object owner, string parameterName, int value)
        {
            if (!TryResolveOwnedHash(owner, parameterName, AnimatorParameterType.Int, out int hash))
                return false;

            Animator.SetInteger(hash, value);
            return true;
        }

        /// <summary>Writes a bool value. The caller must own the parameter.</summary>
        public bool WriteBool(Object owner, string parameterName, bool value)
        {
            if (!TryResolveOwnedHash(owner, parameterName, AnimatorParameterType.Bool, out int hash))
                return false;

            Animator.SetBool(hash, value);
            return true;
        }

        /// <summary>Fires a trigger. The caller must own the parameter.</summary>
        public bool FireTrigger(Object owner, string parameterName)
        {
            if (!TryResolveOwnedHash(owner, parameterName, AnimatorParameterType.Trigger, out int hash))
                return false;

            Animator.SetTrigger(hash);
            return true;
        }

        /// <summary>
        ///     Registers that <paramref name="owner" /> will drive the weight of layer
        ///     <paramref name="layerIndex" /> on the animator controller.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Layer-weight registration uses the same single-writer discipline as
        ///         parameter registration: a second owner requesting the same layer is
        ///         rejected so additive-layer policy is explicit in the behavior preset.
        ///     </para>
        /// </remarks>
        public bool RegisterLayer(Object owner, int layerIndex)
        {
            if (owner == null || layerIndex < 0) return false;

            if (_ownedLayers.TryGetValue(layerIndex, out OwnedLayer existing))
            {
                if (existing.Owner == owner) return true;

                ConvaiLogger.Warning(
                    $"[AnimatorConductor] Layer {layerIndex} is already owned by " +
                    $"'{existing.OwnerName}'; rejected registration from '{owner.name}'. " +
                    $"Remove the duplicate component or give it a different parameter.",
                    LogCategory.Character);
                return false;
            }

            _ownedLayers[layerIndex] = new OwnedLayer(owner, owner.name);
            _warnedForeignLayerWrites.Remove(layerIndex);
            return true;
        }

        /// <summary>Releases a previously registered layer owner.</summary>
        public void UnregisterLayer(Object owner, int layerIndex)
        {
            if (owner == null || layerIndex < 0) return;

            if (_ownedLayers.TryGetValue(layerIndex, out OwnedLayer existing) &&
                existing.Owner == owner)
            {
                _ownedLayers.Remove(layerIndex);
            }
        }

        /// <summary>Writes a layer weight. The caller must own the layer.</summary>
        public bool WriteLayerWeight(Object owner, int layerIndex, float weight)
        {
            if (owner == null || layerIndex < 0) return false;

            if (!_ownedLayers.TryGetValue(layerIndex, out OwnedLayer owned) || owned.Owner != owner)
            {
                if (_warnedForeignLayerWrites.Add(layerIndex))
                {
                    ConvaiLogger.Warning(
                        $"[AnimatorConductor] '{owner.name}' tried to write layer {layerIndex} " +
                        $"weight but the layer is not registered to this owner.",
                        LogCategory.Character);
                }
                return false;
            }

            Animator animator = Animator;
            if (animator == null || layerIndex >= animator.layerCount)
                return false;

            animator.SetLayerWeight(layerIndex, weight);
            return true;
        }

        /// <summary>
        ///     Enumerates registered ownership entries for editor tooling (validator, inspector
        ///     overlay). Values are copied so callers may not mutate the internal map.
        ///     Intended for <c>UNITY_EDITOR</c> diagnostics; avoid calling every frame in player builds.
        /// </summary>
        public IReadOnlyCollection<AnimatorParameterOwnership> EnumerateRegistrations()
        {
            var snapshot = new List<AnimatorParameterOwnership>(_ownedParameters.Count);
            foreach (KeyValuePair<string, OwnedParameter> kvp in _ownedParameters)
            {
                snapshot.Add(new AnimatorParameterOwnership(
                    kvp.Key,
                    kvp.Value.Type,
                    kvp.Value.OwnerName));
            }
            return snapshot;
        }

        private bool TryResolveOwnedHash(
            Object owner,
            string parameterName,
            AnimatorParameterType type,
            out int hash)
        {
            hash = 0;

            if (owner == null || string.IsNullOrEmpty(parameterName))
                return false;

            if (!_ownedParameters.TryGetValue(parameterName, out OwnedParameter owned))
            {
                if (_warnedUnregisteredParameters.Add(parameterName))
                {
                    ConvaiLogger.Warning(
                        $"[AnimatorConductor] Write to unregistered parameter '{parameterName}' " +
                        $"rejected. Register during Initialize before writing.",
                        LogCategory.Character);
                }
                return false;
            }

            if (owned.Owner != owner || owned.Type != type)
            {
                if (_warnedForeignParameterWrites.Add(parameterName))
                {
                    ConvaiLogger.Warning(
                        $"[AnimatorConductor] '{owner?.name ?? "?"}' tried to write '{parameterName}' " +
                        $"but it is owned by '{owned.OwnerName}' as {owned.Type}.",
                        LogCategory.Character);
                }
                return false;
            }

            Animator animator = Animator;
            if (animator == null)
                return false;

            hash = Animator.StringToHash(parameterName);

            if (!_parameterExistenceCache.TryGetValue(hash, out bool exists))
            {
                exists = AnimatorHasParameter(animator, parameterName, type);
                _parameterExistenceCache[hash] = exists;
            }

            return exists;
        }

        private static bool AnimatorHasParameter(
            Animator animator,
            string parameterName,
            AnimatorParameterType type)
        {
            AnimatorControllerParameter[] parameters = animator.parameters;
            AnimatorControllerParameterType required = type switch
            {
                AnimatorParameterType.Float => AnimatorControllerParameterType.Float,
                AnimatorParameterType.Int => AnimatorControllerParameterType.Int,
                AnimatorParameterType.Bool => AnimatorControllerParameterType.Bool,
                AnimatorParameterType.Trigger => AnimatorControllerParameterType.Trigger,
                _ => AnimatorControllerParameterType.Float
            };

            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter param = parameters[i];
                if (param.type == required && string.Equals(param.name, parameterName))
                    return true;
            }

            return false;
        }

        private readonly struct OwnedParameter
        {
            public OwnedParameter(Object owner, string ownerName, AnimatorParameterType type)
            {
                Owner = owner;
                OwnerName = ownerName;
                Type = type;
            }

            public Object Owner { get; }
            public string OwnerName { get; }
            public AnimatorParameterType Type { get; }
        }

        private readonly struct OwnedLayer
        {
            public OwnedLayer(Object owner, string ownerName)
            {
                Owner = owner;
                OwnerName = ownerName;
            }

            public Object Owner { get; }
            public string OwnerName { get; }
        }
    }

    /// <summary>
    ///     Diagnostic snapshot of a single registered animator parameter. Returned by
    ///     <see cref="AnimatorConductor.EnumerateRegistrations" />.
    /// </summary>
    public readonly struct AnimatorParameterOwnership
    {
        public AnimatorParameterOwnership(
            string parameterName,
            AnimatorParameterType type,
            string ownerName)
        {
            ParameterName = parameterName;
            Type = type;
            OwnerName = ownerName;
        }

        public string ParameterName { get; }
        public AnimatorParameterType Type { get; }
        public string OwnerName { get; }
    }
}
