using System;
using System.Collections.Generic;

namespace Convai.Editor.AI
{
    internal static class ConvaiMcpResponses
    {
        internal static object Success(string message, object data) => Envelope(true, message, data);

        internal static object Failure(string code, string message) =>
            Envelope(false, message, new { code });

        internal static object Failure(string code, string message, object details) =>
            Envelope(false, message, new { code, details });

        internal static object Envelope(bool success, string message, object data) => new
        {
            success,
            message,
            data
        };

        internal static object Issue(
            string code,
            string severity,
            string message,
            string evidence,
            long affectedInstanceId,
            bool autoFixable,
            string suggestedTool,
            object suggestedArguments) => new
        {
            code,
            severity,
            message,
            evidence,
            affectedInstanceId,
            autoFixable,
            suggestedTool,
            suggestedArguments
        };

        internal static object StandardResponseSchema(bool moduleCompatibilityStyle = false)
        {
            if (moduleCompatibilityStyle)
                return new
                {
                    type = "object",
                    properties = new
                    {
                        success = new { type = "boolean" },
                        message = new { type = "string" },
                        data = new { type = "object" }
                    },
                    required = new[] { "success", "message", "data" },
                    additionalProperties = true
                };

            return new
            {
                type = "object",
                properties = new
                {
                    success = new { type = "boolean" },
                    message = new { type = "string" },
                    data = new { type = "object", additionalProperties = true }
                },
                required = new[] { "success", "message", "data" }
            };
        }

        internal static object ObjectSchema(
            Dictionary<string, object> properties,
            params string[] required) => new
        {
            type = "object",
            properties,
            required,
            additionalProperties = false
        };

        internal static object ClosedObjectSchemaWithoutRequired(
            Dictionary<string, object> properties) => new
        {
            type = "object",
            properties,
            additionalProperties = false
        };

        internal static object NestedObjectSchema(
            Dictionary<string, object> properties,
            params string[] required) => new
        {
            type = "object",
            properties,
            required
        };

        internal static object IntegerProperty(string description, long? defaultValue = null) => defaultValue.HasValue
            ? new { type = "integer", format = "int64", description, @default = defaultValue.Value }
            : (object)new { type = "integer", format = "int64", description };

        internal static object StringProperty(string description, string defaultValue = null) => defaultValue != null
            ? new { type = "string", description, @default = defaultValue }
            : (object)new { type = "string", description };

        internal static object BooleanProperty(string description, bool defaultValue) => new
        {
            type = "boolean",
            description,
            @default = defaultValue
        };

        internal static object EnumProperty<T>(string description, T defaultValue) where T : struct, Enum => new
        {
            type = "string",
            description,
            @enum = Enum.GetNames(typeof(T)),
            @default = defaultValue.ToString()
        };

        internal static object IntegerSchema(long? defaultValue = null) => defaultValue.HasValue
            ? new { type = "integer", @default = defaultValue.Value }
            : (object)new { type = "integer" };

        internal static object StringSchema(string defaultValue = null) => defaultValue != null
            ? new { type = "string", @default = defaultValue }
            : (object)new { type = "string" };

        internal static object BooleanSchema(bool defaultValue) => new
        {
            type = "boolean",
            @default = defaultValue
        };

        internal static object NumberSchema(object defaultValue) => new
        {
            type = "number",
            @default = defaultValue
        };

        internal static object StringEnumSchema(string[] values, string defaultValue = null) => defaultValue != null
            ? new { type = "string", @enum = values, @default = defaultValue }
            : (object)new { type = "string", @enum = values };

        internal static object ArraySchema(object items) => new
        {
            type = "array",
            items
        };

        // Optional-property builders. A tuning tool must be able to say "omit this and I will leave
        // it alone", which a declared default cannot express — an assistant reading `default: false`
        // reasonably concludes that omitting the field asks for false, and a call meant to change
        // one setting silently resets the rest.

        internal static object OptionalBooleanProperty(string description) => new
        {
            type = "boolean",
            description
        };

        internal static object OptionalIntegerProperty(string description) => new
        {
            type = "integer",
            format = "int64",
            description
        };

        internal static object OptionalNumberProperty(string description) => new
        {
            type = "number",
            description
        };

        internal static object OptionalStringEnumProperty(string description, string[] values) => new
        {
            type = "string",
            description,
            @enum = values
        };

        internal static object ArrayProperty(string description, object items) => new
        {
            type = "array",
            description,
            items
        };
    }
}
