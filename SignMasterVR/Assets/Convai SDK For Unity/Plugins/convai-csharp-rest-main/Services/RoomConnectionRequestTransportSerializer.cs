#nullable enable
using System;
using System.Collections.Generic;
using Convai.RestAPI;
using Convai.Shared.Actions;
using Newtonsoft.Json;

namespace Convai.RestAPI.Services
{
    /// <summary>
    /// Prepares and serializes room-connect requests so every transport sends the same wire contract.
    /// </summary>
    internal static class RoomConnectionRequestTransportSerializer
    {
        private static readonly JsonSerializerSettings ConnectRequestJsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        internal static void PrepareForTransport(
            RoomConnectionRequest request,
            ConvaiRestClientOptions options)
        {
            ValidateRequest(request);
            NormalizeIdentityFields(request);
            PopulateInvocationMetadata(request, options);
        }

        internal static string SerializeForTransport(
            RoomConnectionRequest request,
            ConvaiRestClientOptions options)
        {
            PrepareForTransport(request, options);
            return JsonConvert.SerializeObject(request, ConnectRequestJsonSettings);
        }

        internal static void PopulateInvocationMetadata(
            RoomConnectionRequest request,
            ConvaiRestClientOptions options)
        {
            request.InvocationMetadata ??= new RoomInvocationMetadata();

            if (string.IsNullOrWhiteSpace(request.InvocationMetadata.Source))
            {
                request.InvocationMetadata.Source = options.InvocationSource;
            }

            if (string.IsNullOrWhiteSpace(request.InvocationMetadata.ClientVersion))
            {
                request.InvocationMetadata.ClientVersion = options.ClientVersion;
            }
        }

        internal static void ValidateRequest(RoomConnectionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CharacterId))
            {
                throw new ConvaiRestException(
                    "Character ID is required",
                    ConvaiRestErrorCategory.BadRequest);
            }

            if (string.IsNullOrWhiteSpace(request.CoreServiceUrl))
            {
                throw new ConvaiRestException(
                    "Core service URL is required",
                    ConvaiRestErrorCategory.BadRequest);
            }

            if (string.IsNullOrWhiteSpace(request.Transport))
            {
                throw new ConvaiRestException(
                    "Transport type is required",
                    ConvaiRestErrorCategory.BadRequest);
            }
        }

        private static void NormalizeIdentityFields(RoomConnectionRequest request)
        {
            request.EndUserId = string.IsNullOrWhiteSpace(request.EndUserId)
                ? null
                : request.EndUserId.Trim();
            request.EndUserMetadata = NormalizeEndUserMetadata(request.EndUserMetadata);
            request.ActionConfig = NormalizeActionConfig(request.ActionConfig);
        }

        private static IReadOnlyDictionary<string, object>? NormalizeEndUserMetadata(
            IReadOnlyDictionary<string, object>? metadata)
        {
            if (metadata == null || metadata.Count == 0)
                return null;

            var normalized = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> entry in metadata)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                    continue;

                normalized[entry.Key.Trim()] = entry.Value;
            }

            return normalized.Count == 0 ? null : normalized;
        }

        private static ConvaiActionConfig? NormalizeActionConfig(ConvaiActionConfig? actionConfig)
        {
            if (actionConfig == null)
                return null;

            actionConfig.Actions = NormalizeActions(actionConfig.Actions);
            actionConfig.Objects = NormalizeObjects(actionConfig.Objects);
            actionConfig.Characters = NormalizeCharacters(actionConfig.Characters);
            actionConfig.CurrentAttentionObject = NormalizeName(actionConfig.CurrentAttentionObject);

            if (actionConfig.Actions.Count == 0)
                return null;

            ValidateUniqueNames(actionConfig.Objects, static entry => entry.Name, "object");
            ValidateUniqueNames(actionConfig.Characters, static entry => entry.Name, "character");

            if (!string.IsNullOrWhiteSpace(actionConfig.CurrentAttentionObject) &&
                !ContainsName(actionConfig.Objects, actionConfig.CurrentAttentionObject))
            {
                throw new ConvaiRestException(
                    "action_config.current_attention_object must match one of action_config.objects[].name",
                    ConvaiRestErrorCategory.BadRequest);
            }

            return actionConfig;
        }

        private static List<string> NormalizeActions(IReadOnlyList<string>? actions)
        {
            var normalized = new List<string>();
            if (actions == null)
                return normalized;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string action in actions)
            {
                string? trimmed = NormalizeName(action);
                if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed))
                    continue;

                normalized.Add(trimmed);
            }

            return normalized;
        }

        private static List<ConvaiActionObjectDefinition> NormalizeObjects(
            IReadOnlyList<ConvaiActionObjectDefinition>? objects)
        {
            var normalized = new List<ConvaiActionObjectDefinition>();
            if (objects == null)
                return normalized;

            foreach (ConvaiActionObjectDefinition actionObject in objects)
            {
                if (actionObject == null)
                    continue;

                string? name = NormalizeName(actionObject.Name);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                normalized.Add(new ConvaiActionObjectDefinition
                {
                    Name = name,
                    Description = NormalizeText(actionObject.Description),
                    GameObjectReference = actionObject.GameObjectReference
                });
            }

            return normalized;
        }

        private static List<ConvaiActionCharacterDefinition> NormalizeCharacters(
            IReadOnlyList<ConvaiActionCharacterDefinition>? characters)
        {
            var normalized = new List<ConvaiActionCharacterDefinition>();
            if (characters == null)
                return normalized;

            foreach (ConvaiActionCharacterDefinition character in characters)
            {
                if (character == null)
                    continue;

                string? name = NormalizeName(character.Name);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                normalized.Add(new ConvaiActionCharacterDefinition
                {
                    Name = name,
                    Bio = NormalizeText(character.Bio),
                    GameObjectReference = character.GameObjectReference
                });
            }

            return normalized;
        }

        private static void ValidateUniqueNames<T>(
            IReadOnlyList<T> entries,
            Func<T, string> nameSelector,
            string label)
        {
            if (entries == null || entries.Count == 0)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (T entry in entries)
            {
                string? name = NormalizeName(nameSelector(entry));
                if (string.IsNullOrWhiteSpace(name) || seen.Add(name))
                    continue;

                throw new ConvaiRestException(
                    $"Duplicate action_config {label} name '{name}'. Names must be unique.",
                    ConvaiRestErrorCategory.BadRequest);
            }
        }

        private static bool ContainsName(
            IReadOnlyList<ConvaiActionObjectDefinition> objects,
            string targetName)
        {
            if (objects == null || objects.Count == 0)
                return false;

            foreach (ConvaiActionObjectDefinition actionObject in objects)
            {
                if (string.Equals(actionObject?.Name, targetName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string? NormalizeName(string value) => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

        private static string NormalizeText(string value) => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}
