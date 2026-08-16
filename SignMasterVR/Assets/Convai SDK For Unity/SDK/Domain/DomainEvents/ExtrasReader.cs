using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Convai.Domain.DomainEvents
{
    /// <summary>
    ///     Shared, lenient readers for backend "extras" JObject payloads. Backends are loosely typed
    ///     (a field may arrive as a string, number, or bool), so these coerce permissively and never throw.
    /// </summary>
    internal static class ExtrasReader
    {
        public static string ReadString(JObject obj, string key)
        {
            JToken token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return string.Empty;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? "true" : "false";
            if (token.Type == JTokenType.String) return token.Value<string>() ?? string.Empty;

            return token.ToString(Formatting.None);
        }

        public static int ReadInt(JObject obj, params string[] keys)
        {
            foreach (string key in keys)
            {
                JToken token = obj[key];
                if (token == null || token.Type == JTokenType.Null) continue;
                if (token.Type == JTokenType.Integer) return token.Value<int>();
                if (int.TryParse(token.ToString(), out int value)) return value;
            }

            return 0;
        }

        public static int? ReadNullableInt(JObject obj, params string[] keys)
        {
            foreach (string key in keys)
            {
                JToken token = obj[key];
                if (token == null || token.Type == JTokenType.Null) continue;
                if (token.Type == JTokenType.Integer) return token.Value<int>();
                if (int.TryParse(token.ToString(), out int value)) return value;
            }

            return null;
        }

        public static bool ReadBool(JObject obj, string key)
        {
            JToken token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return false;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>();
            if (token.Type == JTokenType.Integer) return token.Value<int>() != 0;
            return bool.TryParse(token.ToString(), out bool value) && value;
        }

        public static bool? ReadNullableBool(JObject obj, string key)
        {
            JToken token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>();
            if (token.Type == JTokenType.Integer) return token.Value<int>() != 0;
            return bool.TryParse(token.ToString(), out bool value) ? value : null;
        }

        public static string ReadOptionalString(JObject obj, string key)
        {
            JToken token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? "true" : "false";
            if (token.Type == JTokenType.String) return token.Value<string>();

            return token.ToString(Formatting.None);
        }

        public static IReadOnlyList<long> ReadLongArray(JObject obj, string key)
        {
            if (obj[key] is not JArray array || array.Count == 0)
                return Array.Empty<long>();

            var values = new List<long>(array.Count);
            foreach (JToken token in array)
            {
                if (token == null || token.Type == JTokenType.Null)
                    continue;

                if (token.Type == JTokenType.Integer)
                {
                    values.Add(token.Value<long>());
                    continue;
                }

                if (long.TryParse(token.ToString(), out long value))
                    values.Add(value);
            }

            return values;
        }
    }
}
