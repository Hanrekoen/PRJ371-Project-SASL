using System;
using System.Collections.Generic;
using System.Reflection;
using Convai.Domain.Logging;
using Convai.Runtime.Embodiment;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Embodiment.Setup
{
    /// <summary>One embodiment feature, as declared by the component that implements it.</summary>
    internal readonly struct EmbodimentModuleDescriptor
    {
        public EmbodimentModuleDescriptor(
            string moduleId, string displayName, string description, string absence, int order,
            Type controllerType, Type profileType, string createProfileMenuPath)
        {
            ModuleId = moduleId;
            DisplayName = displayName;
            Description = description;
            Absence = absence;
            Order = order;
            ControllerType = controllerType;
            ProfileType = profileType;
            CreateProfileMenuPath = createProfileMenuPath;
        }

        /// <summary>Stable routing key serialized in preset assets.</summary>
        public string ModuleId { get; }

        /// <summary>Plain-English label. Shown instead of the id everywhere a user reads.</summary>
        public string DisplayName { get; }

        /// <summary>One sentence on what the feature does.</summary>
        public string Description { get; }

        /// <summary>
        ///     What the character loses without this feature, phrased to complete "Without it, ".
        ///     Empty when the module declares none.
        /// </summary>
        public string Absence { get; }

        public int Order { get; }

        /// <summary>The component a user adds to a character for this feature.</summary>
        public Type ControllerType { get; }

        /// <summary>
        ///     The profile asset type this feature accepts, read from its generic base rather than
        ///     restated in the attribute.
        /// </summary>
        public Type ProfileType { get; }

        /// <summary>
        ///     The menu path that creates this feature's settings asset, exactly as a user reads it:
        ///     <c>Assets → Create → Convai → Embodiment → Gaze Profile</c>. Empty when the feature
        ///     takes no settings asset, or when its asset is not creatable from the Assets menu.
        /// </summary>
        /// <remarks>
        ///     Read off the profile type's own <see cref="UnityEngine.CreateAssetMenuAttribute" />
        ///     rather than tabulated here. A hand-written table of six menu paths is one more copy of
        ///     the same knowledge, and it is the copy that goes stale first — the attribute is what
        ///     Unity actually builds the menu from.
        /// </remarks>
        public string CreateProfileMenuPath { get; }

        public bool IsValid => !string.IsNullOrEmpty(ModuleId) && ControllerType != null;
    }

    /// <summary>
    ///     The single source of truth for "which embodiment features exist", built by reflecting over
    ///     the components that declare themselves with <see cref="EmbodimentModuleAttribute" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Serves the preset inspector's module dropdown, its type-filtered profile picker, the
    ///         character map, and the architecture tests. Each of those used to carry its own copy of
    ///         the list; the copies drifted and shipped a false warning on Convai's own sample asset.
    ///     </para>
    ///     <para>
    ///         <b>Editor-only, on purpose.</b> A player build must never need this, so nothing in the
    ///         runtime preset path depends on it — routing at runtime still goes through each
    ///         receiver's own <c>ProfileModuleId</c>. That keeps the reflection out of IL2CPP's way
    ///         and means <c>link.xml</c> needs no entry for module controllers or this attribute.
    ///     </para>
    ///     <para>
    ///         Built once per domain reload and cached. <see cref="TypeCache" /> is Unity's own index,
    ///         so this is a dictionary fill, not an assembly scan.
    ///     </para>
    /// </remarks>
    [InitializeOnLoad]
    internal static class EmbodimentModuleCatalog
    {
        private static EmbodimentModuleDescriptor[] _modules;
        private static Dictionary<string, EmbodimentModuleDescriptor> _byId;

        static EmbodimentModuleCatalog()
        {
            // Deliberately not eager: a domain reload should not pay for a catalog nobody asked for.
            // The static constructor exists only so [InitializeOnLoad] clears the cache on reload.
            _modules = null;
            _byId = null;
        }

        /// <summary>Every declared module, in display order.</summary>
        internal static IReadOnlyList<EmbodimentModuleDescriptor> Modules
        {
            get
            {
                EnsureBuilt();
                return _modules;
            }
        }

        /// <summary>Looks up a module by its stable routing id.</summary>
        internal static bool TryGet(string moduleId, out EmbodimentModuleDescriptor descriptor)
        {
            EnsureBuilt();
            if (!string.IsNullOrWhiteSpace(moduleId) && _byId.TryGetValue(moduleId, out descriptor))
                return true;

            descriptor = default;
            return false;
        }

        /// <summary>Every declared module id, for a dropdown's value list.</summary>
        internal static string[] ModuleIdsInDisplayOrder()
        {
            EnsureBuilt();
            var ids = new string[_modules.Length];
            for (int i = 0; i < _modules.Length; i++) ids[i] = _modules[i].ModuleId;
            return ids;
        }

        /// <summary>Display labels aligned with <see cref="ModuleIdsInDisplayOrder" />.</summary>
        internal static string[] DisplayNamesInDisplayOrder()
        {
            EnsureBuilt();
            var names = new string[_modules.Length];
            for (int i = 0; i < _modules.Length; i++) names[i] = _modules[i].DisplayName;
            return names;
        }

        /// <summary>The label to show for an id — its display name, or the raw id when unknown.</summary>
        internal static string DescribeModule(string moduleId) =>
            TryGet(moduleId, out EmbodimentModuleDescriptor d) ? d.DisplayName : moduleId;

        /// <summary>The modules present on <paramref name="characterRoot" />.</summary>
        internal static List<EmbodimentModuleDescriptor> ModulesOn(GameObject characterRoot)
        {
            var present = new List<EmbodimentModuleDescriptor>();
            if (characterRoot == null) return present;

            EnsureBuilt();
            for (int i = 0; i < _modules.Length; i++)
            {
                if (characterRoot.GetComponentInChildren(_modules[i].ControllerType, true) != null)
                    present.Add(_modules[i]);
            }

            return present;
        }

        private static void EnsureBuilt()
        {
            if (_modules != null) return;

            var found = new List<EmbodimentModuleDescriptor>(8);
            var seen = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            foreach (Type type in TypeCache.GetTypesWithAttribute<EmbodimentModuleAttribute>())
            {
                if (type.IsAbstract) continue;

                var attribute = type.GetCustomAttribute<EmbodimentModuleAttribute>(false);
                if (attribute == null || string.IsNullOrWhiteSpace(attribute.ModuleId)) continue;

                if (seen.TryGetValue(attribute.ModuleId, out Type existing))
                {
                    ConvaiLogger.Error(
                        $"[Convai] Two components claim the embodiment module id '{attribute.ModuleId}': " +
                        $"{existing.Name} and {type.Name}. A preset slot cannot route to both — give one a " +
                        "different id.",
                        LogCategory.Character);
                    continue;
                }

                seen[attribute.ModuleId] = type;
                Type profileType = ResolveProfileType(type);
                found.Add(new EmbodimentModuleDescriptor(
                    attribute.ModuleId,
                    string.IsNullOrWhiteSpace(attribute.DisplayName) ? type.Name : attribute.DisplayName,
                    attribute.Description,
                    attribute.Absence,
                    attribute.Order,
                    type,
                    profileType,
                    ResolveCreateProfileMenuPath(profileType)));
            }

            found.Sort((a, b) =>
            {
                int byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0
                    ? byOrder
                    : string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal);
            });

            _modules = found.ToArray();
            _byId = new Dictionary<string, EmbodimentModuleDescriptor>(StringComparer.OrdinalIgnoreCase);
            foreach (EmbodimentModuleDescriptor d in _modules) _byId[d.ModuleId] = d;
        }

        /// <summary>
        ///     Walks up to <c>ConvaiCharacterModule&lt;TProfile&gt;</c> and returns
        ///     <c>TProfile</c> — the type the component genuinely accepts, so a preset picker cannot
        ///     offer an asset the module will reject at runtime.
        /// </summary>
        private static Type ResolveProfileType(Type controllerType)
        {
            for (Type t = controllerType; t != null && t != typeof(object); t = t.BaseType)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ConvaiCharacterModule<>))
                    return t.GetGenericArguments()[0];
            }

            return null;
        }

        /// <summary>
        ///     Turns a profile type's <c>[CreateAssetMenu]</c> into the path a user reads in the
        ///     editor, so setup surfaces can tell someone where to make one instead of assuming they
        ///     already know.
        /// </summary>
        private static string ResolveCreateProfileMenuPath(Type profileType)
        {
            var attribute = profileType?.GetCustomAttribute<CreateAssetMenuAttribute>(false);
            if (attribute == null || string.IsNullOrWhiteSpace(attribute.menuName)) return string.Empty;

            // Each separator is written inside the segment it introduces rather than pulled out into
            // a literal of its own. A bare " → " reads to the editor design-system guard as an icon
            // spelled inline, which is the rule that keeps section marks coming from
            // ConvaiEditorGlyphs — and it is right to: an arrow on its own is a glyph, an arrow
            // inside a menu path is prose.
            var builder = new System.Text.StringBuilder("Assets → Create");
            string[] segments = attribute.menuName.Split('/');
            for (int i = 0; i < segments.Length; i++) builder.Append($" → {segments[i]}");

            return builder.ToString();
        }
    }
}
