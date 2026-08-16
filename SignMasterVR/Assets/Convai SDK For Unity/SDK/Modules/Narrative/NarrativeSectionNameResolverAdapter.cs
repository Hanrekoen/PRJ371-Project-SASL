using System.Collections.Generic;
using Convai.Domain.Abstractions;

namespace Convai.Modules.Narrative
{
    /// <summary>
    ///     Runtime registry that aggregates section-name resolvers contributed by the Narrative module.
    /// </summary>
    public class NarrativeSectionNameResolverAdapter : INarrativeSectionNameResolver
    {
        private readonly List<INarrativeSectionNameResolver> _resolvers = new();

        /// <inheritdoc />
        public bool TryGetSectionName(string sectionId, out string sectionName)
        {
            sectionName = null;

            if (string.IsNullOrEmpty(sectionId)) return false;

            Refresh();

            foreach (INarrativeSectionNameResolver resolver in _resolvers)
            {
                if (resolver == null || ReferenceEquals(resolver, this)) continue;
                if (resolver.TryGetSectionName(sectionId, out sectionName) &&
                    !string.IsNullOrEmpty(sectionName))
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Adds a new resolver to the registry.
        /// </summary>
        public void AddResolver(INarrativeSectionNameResolver resolver)
        {
            if (resolver == null || ReferenceEquals(resolver, this) || _resolvers.Contains(resolver)) return;
            _resolvers.Add(resolver);
        }

        /// <summary>
        ///     Removes a resolver from the registry.
        /// </summary>
        public void RemoveResolver(INarrativeSectionNameResolver resolver)
        {
            if (resolver == null) return;
            _resolvers.Remove(resolver);
        }

        /// <summary>
        ///     Prunes destroyed or removed resolver references.
        /// </summary>
        public void Refresh() => _resolvers.RemoveAll(resolver => resolver == null);
    }
}
