using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Convai.Modules.LipSync.Profiles
{
    /// <summary>
    ///     Registry asset that groups profile assets and defines merge precedence for catalog loading.
    /// </summary>
    [CreateAssetMenu(fileName = "ConvaiLipSyncProfileRegistry", menuName = "Convai/Lip Sync/Profile Registry")]
    public class ConvaiLipSyncProfileRegistry : ScriptableObject
    {
        private static readonly List<ConvaiLipSyncProfile> EmptyProfiles = new();

        [Tooltip("Merge order against other registries. Lower values are applied first, so a higher-priority " +
                 "registry's profiles can override the ones a lower-priority registry contributes.")]
        [SerializeField] private int _priority;

        [Tooltip("The lip sync profile assets this registry contributes to the catalog.")]
        [SerializeField] private List<ConvaiLipSyncProfile> _profiles = new();
        private ReadOnlyCollection<ConvaiLipSyncProfile> _readOnlyProfiles;

        /// <summary>
        ///     Merge priority used by the catalog. Lower values are applied first.
        /// </summary>
        public int Priority => _priority;

        /// <summary>
        ///     Ordered list of profile assets contributed by this registry.
        /// </summary>
        public IReadOnlyList<ConvaiLipSyncProfile> Profiles =>
            _profiles != null ? _readOnlyProfiles ??= _profiles.AsReadOnly() : EmptyProfiles;
    }
}
