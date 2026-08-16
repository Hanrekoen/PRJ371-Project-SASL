using System;
using System.Collections.Generic;
using System.Reflection;
using Convai.Modules.Gaze.Data;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Gaze.Editor
{
    /// <summary>
    ///     Resolves a Gaze Profile setting's serialized property path from its plain field name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The profile's settings live in nested blocks, so <c>playerMaxDistance</c> is really
    ///         <c>targeting.playerMaxDistance</c> on the serialized object. Every editor surface that
    ///         reaches a setting by name — the profile inspector, the personality dials, the
    ///         archetype presets — would otherwise have to hard-code which block each of 111 settings
    ///         belongs to, and would break the moment one moved between blocks.
    ///     </para>
    ///     <para>
    ///         The map is built by reflection from the profile type itself, so it cannot disagree
    ///         with the asset. A name that is still a top-level field resolves to itself, which keeps
    ///         this usable for the profile's non-block fields too.
    ///     </para>
    /// </remarks>
    internal static class GazeProfileSerializedPaths
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly Dictionary<string, string> PathByName = BuildPaths();

        private static Dictionary<string, string> BuildPaths()
        {
            var paths = new Dictionary<string, string>();
            Type profile = typeof(ConvaiGazeProfile);

            foreach (FieldInfo field in profile.GetFields(Flags))
            {
                if (!IsSerialized(field)) continue;

                // A settings block is any serialized field whose own type carries serialized fields
                // and is declared inside the profile — no marker list to keep in step with the asset.
                if (field.FieldType.DeclaringType == profile && !field.FieldType.IsEnum)
                {
                    foreach (FieldInfo nested in field.FieldType.GetFields(Flags))
                        if (IsSerialized(nested))
                            paths[nested.Name] = $"{field.Name}.{nested.Name}";

                    continue;
                }

                paths[field.Name] = field.Name;
            }

            return paths;
        }

        private static bool IsSerialized(FieldInfo field)
        {
            if (field.IsNotSerialized || field.IsStatic) return false;
            return field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
        }

        /// <summary>
        ///     The serialized path for <paramref name="fieldName" />, or the name unchanged when the
        ///     profile has no such field — callers already handle a null property by reporting it,
        ///     and that reads better than an exception from here.
        /// </summary>
        internal static string Of(string fieldName) =>
            PathByName.TryGetValue(fieldName, out string path) ? path : fieldName;

        /// <summary>Finds a profile setting by plain field name, wherever its block put it.</summary>
        internal static SerializedProperty Find(SerializedObject serializedProfile, string fieldName) =>
            serializedProfile?.FindProperty(Of(fieldName));

        /// <summary>
        ///     Every setting name the profile serializes, with the nested blocks flattened — the
        ///     enumeration a guard test or a tool needs to check that every setting is reachable.
        /// </summary>
        internal static IEnumerable<string> SettingNames => PathByName.Keys;
    }
}
