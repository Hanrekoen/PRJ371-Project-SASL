#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Convai.Modules.LipSync.Editor
{
    /// <summary>Strict reader for versioned lip-sync mapping JSON exported by the inspector.</summary>
    internal static class ConvaiLipSyncMapImportParser
    {
        internal const int CurrentVersion = 1;

        private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal)
        {
            "version", "targetProfileId", "description", "globalMultiplier", "globalOffset",
            "allowUnmappedPassthrough", "mappings"
        };

        private static readonly HashSet<string> EntryKeys = new(StringComparer.Ordinal)
        {
            "sourceBlendshape", "targetNames", "multiplier", "offset", "curveExponent", "enabled",
            "useOverrideValue", "overrideValue", "ignoreGlobalModifiers", "clampMinValue", "clampMaxValue"
        };

        internal static bool TryParse(string rawText, out MappingImportData data, out string error)
        {
            data = null;
            error = null;
            if (string.IsNullOrWhiteSpace(rawText))
            {
                error = "Import text is empty.";
                return false;
            }

            try
            {
                if (JToken.Parse(rawText) is not JObject root)
                {
                    error = "Mapping import must be one canonical JSON object.";
                    return false;
                }

                if (!ValidateKeys(root, RootKeys, "root", out error)) return false;
                if (root["version"]?.Type != JTokenType.Integer || root.Value<int>("version") != CurrentVersion)
                {
                    error = $"Mapping JSON version must be {CurrentVersion}.";
                    return false;
                }

                if (root["mappings"] is not JArray mappings)
                {
                    error = "Mapping JSON requires a 'mappings' array.";
                    return false;
                }

                var parsedData = new MappingImportData();
                ReadOptionalString(root, "targetProfileId", value => parsedData.TargetProfileId = value);
                if (root.TryGetValue("description", out JToken description))
                {
                    if (description.Type != JTokenType.String && description.Type != JTokenType.Null)
                        return Fail("'description' must be a string.", out error);
                    parsedData.HasDescription = true;
                    parsedData.Description = description.Type == JTokenType.Null
                        ? string.Empty
                        : description.Value<string>();
                }

                if (!ReadOptionalFloat(root, "globalMultiplier", value => parsedData.GlobalMultiplier = value,
                        out error) ||
                    !ReadOptionalFloat(root, "globalOffset", value => parsedData.GlobalOffset = value, out error) ||
                    !ReadOptionalBool(root, "allowUnmappedPassthrough",
                        value => parsedData.AllowUnmappedPassthrough = value, out error))
                    return false;

                for (int i = 0; i < mappings.Count; i++)
                {
                    if (mappings[i] is not JObject rawEntry)
                        return Fail($"Mapping entry {i} must be an object.", out error);
                    if (!ValidateKeys(rawEntry, EntryKeys, $"mapping entry {i}", out error)) return false;

                    string source = rawEntry.Value<string>("sourceBlendshape")?.Trim();
                    if (string.IsNullOrEmpty(source))
                        return Fail($"Mapping entry {i} requires a non-empty 'sourceBlendshape'.", out error);

                    var entry = new ImportedEntry { SourceBlendshape = source };
                    if (!ReadTargets(rawEntry, entry.TargetNames, i, out error) ||
                        !ReadOptionalFloat(rawEntry, "multiplier", value => entry.Multiplier = value, out error) ||
                        !ReadOptionalFloat(rawEntry, "offset", value => entry.Offset = value, out error) ||
                        !ReadOptionalFloat(rawEntry, "curveExponent", value => entry.CurveExponent = value,
                            out error) ||
                        !ReadOptionalBool(rawEntry, "enabled", value => entry.Enabled = value, out error) ||
                        !ReadOptionalBool(rawEntry, "useOverrideValue", value => entry.UseOverrideValue = value,
                            out error) ||
                        !ReadOptionalFloat(rawEntry, "overrideValue", value => entry.OverrideValue = value,
                            out error) ||
                        !ReadOptionalBool(rawEntry, "ignoreGlobalModifiers",
                            value => entry.IgnoreGlobalModifiers = value, out error) ||
                        !ReadOptionalFloat(rawEntry, "clampMinValue", value => entry.ClampMinValue = value,
                            out error) ||
                        !ReadOptionalFloat(rawEntry, "clampMaxValue", value => entry.ClampMaxValue = value,
                            out error))
                        return false;

                    parsedData.Entries.Add(entry);
                }

                if (parsedData.Entries.Count == 0)
                    return Fail("Mapping JSON contains no entries.", out error);

                data = parsedData;
                return true;
            }
            catch (JsonException exception)
            {
                error = $"Invalid mapping JSON: {exception.Message}";
                return false;
            }
        }

        private static bool ValidateKeys(JObject obj, HashSet<string> allowed, string context, out string error)
        {
            foreach (JProperty property in obj.Properties())
            {
                if (allowed.Contains(property.Name)) continue;
                error = $"Unknown {context} field '{property.Name}'.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool ReadTargets(JObject obj, List<string> targets, int index, out string error)
        {
            error = null;
            if (!obj.TryGetValue("targetNames", out JToken token)) return true;
            if (token is not JArray array)
            {
                error = $"Mapping entry {index} 'targetNames' must be an array of strings.";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i].Type != JTokenType.String)
                {
                    error = $"Mapping entry {index} target {i} must be a string.";
                    return false;
                }

                string value = array[i].Value<string>()?.Trim();
                if (!string.IsNullOrEmpty(value) && seen.Add(value)) targets.Add(value);
            }

            return true;
        }

        private static bool ReadOptionalFloat(JObject obj, string key, Action<float> apply, out string error)
        {
            error = null;
            if (!obj.TryGetValue(key, out JToken token)) return true;
            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
            {
                error = $"'{key}' must be a number.";
                return false;
            }

            apply(token.Value<float>());
            return true;
        }

        private static bool ReadOptionalBool(JObject obj, string key, Action<bool> apply, out string error)
        {
            error = null;
            if (!obj.TryGetValue(key, out JToken token)) return true;
            if (token.Type != JTokenType.Boolean)
            {
                error = $"'{key}' must be true or false.";
                return false;
            }

            apply(token.Value<bool>());
            return true;
        }

        private static void ReadOptionalString(JObject obj, string key, Action<string> apply)
        {
            if (obj.TryGetValue(key, out JToken token) && token.Type == JTokenType.String)
                apply(token.Value<string>()?.Trim());
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        internal sealed class MappingImportData
        {
            public readonly List<ImportedEntry> Entries = new();
            public readonly List<string> Warnings = new();
            public bool? AllowUnmappedPassthrough;
            public string Description;
            public float? GlobalMultiplier;
            public float? GlobalOffset;
            public bool HasDescription;
            public string TargetProfileId;
        }

        internal sealed class ImportedEntry
        {
            public float ClampMaxValue = 1f;
            public float ClampMinValue;
            public float CurveExponent = 1f;
            public bool Enabled = true;
            public bool IgnoreGlobalModifiers;
            public float Multiplier = 1f;
            public float Offset;
            public float OverrideValue;
            public string SourceBlendshape;
            public List<string> TargetNames = new();
            public bool UseOverrideValue;
        }
    }
}
#endif
