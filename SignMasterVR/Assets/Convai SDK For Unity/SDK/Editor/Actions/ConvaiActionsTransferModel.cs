using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Convai.Editor.Actions
{
    /// <summary>How an imported action whose name collides with an existing inline action is treated.</summary>
    internal enum ConvaiActionsImportCollisionMode
    {
        /// <summary>The colliding imported action is dropped; the existing one stays untouched.</summary>
        Skip = 0,

        /// <summary>The existing inline action is replaced in place by the imported one.</summary>
        Overwrite = 1,

        /// <summary>The imported action is added under a collision-safe " Copy"-suffixed name.</summary>
        Rename = 2
    }

    /// <summary>
    ///     Editor-internal JSON transfer schema and merge logic for the Actions Editor's
    ///     Export/Import feature . Deliberately <em>not</em> the MCP
    ///     <c>ConfigureActions</c> DTOs (<c>Convai.Editor.AI.ConvaiConfigureActionsRequest</c>):
    ///     those reference scene components via editor-session instance IDs
    ///     (<c>ExecutorInstanceId</c>/<c>GameObjectInstanceId</c>), which are meaningless in a file
    ///     that outlives the session, and they carry no <c>Enabled</c>, behavior type hint, or
    ///     failure policy. Field names align with the MCP shapes wherever the semantics overlap so
    ///     a future MCP parity pass can converge on this schema (the deltas are the three fields
    ///     above plus the hint replacing the instance ID). Pure data + list logic — no
    ///     <c>UnityEditor</c> dependency, so round-trips are unit-testable.
    /// </summary>
    internal static class ConvaiActionsTransferModel
    {
        /// <summary>Current schema version written into every export.</summary>
        internal const int CurrentSchemaVersion = 1;

        #region DTOs

        /// <summary>Root of one exported file.</summary>
        internal sealed class ExportDocument
        {
            [JsonProperty("schemaVersion")]
            public int SchemaVersion { get; set; } = CurrentSchemaVersion;

            // No initializer on purpose: a missing "actions" key must deserialize as null so
            // TryParse can reject files that are not Actions exports.
            [JsonProperty("actions")]
            public List<ActionDto> Actions { get; set; }

            [JsonProperty("objects", NullValueHandling = NullValueHandling.Ignore)]
            public List<KnownObjectDto> Objects { get; set; }

            [JsonProperty("characters", NullValueHandling = NullValueHandling.Ignore)]
            public List<KnownCharacterDto> Characters { get; set; }

            [JsonProperty("initialAttentionObject", NullValueHandling = NullValueHandling.Ignore)]
            public string InitialAttentionObject { get; set; }
        }

        /// <summary>One exported action definition (scene behavior carried as a type hint).</summary>
        internal sealed class ActionDto
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("description")]
            public string Description { get; set; } = string.Empty;

            [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)]
            public List<ParameterDto> Parameters { get; set; }

            [JsonProperty("targetRequirement")]
            [JsonConverter(typeof(StringEnumConverter))]
            public ConvaiActionTargetRequirement TargetRequirement { get; set; }

            [JsonProperty("behaviorTypeHint", NullValueHandling = NullValueHandling.Ignore)]
            public string BehaviorTypeHint { get; set; }

            [JsonProperty("timeoutSeconds")]
            public float TimeoutSeconds { get; set; }

            [JsonProperty("failurePolicy")]
            [JsonConverter(typeof(StringEnumConverter))]
            public ConvaiActionFailurePolicyOverride FailurePolicy { get; set; }

            [JsonProperty("waitForBotSpeech")]
            public bool WaitForBotSpeech { get; set; }

            [JsonProperty("delayAfterBotSpeechSeconds")]
            public float DelayAfterBotSpeechSeconds { get; set; }

            [JsonProperty("enabled")]
            public bool Enabled { get; set; } = true;

            /// <summary>
            ///     Authoring category, omitted from the document when the action is uncategorized so
            ///     a project that never uses categories exports exactly the file it used to.
            /// </summary>
            [JsonProperty("category", NullValueHandling = NullValueHandling.Ignore)]
            public string Category { get; set; }
        }

        /// <summary>One exported action parameter.</summary>
        internal sealed class ParameterDto
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("description")]
            public string Description { get; set; } = string.Empty;

            [JsonProperty("type")]
            [JsonConverter(typeof(StringEnumConverter))]
            public ConvaiActionParameterType Type { get; set; } = ConvaiActionParameterType.Auto;

            [JsonProperty("connector", NullValueHandling = NullValueHandling.Ignore)]
            public string Connector { get; set; }

            [JsonProperty("choices", NullValueHandling = NullValueHandling.Ignore)]
            public List<string> Choices { get; set; }
        }

        /// <summary>One exported Known Object entry (name + description only — no scene references).</summary>
        internal sealed class KnownObjectDto
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("description")]
            public string Description { get; set; } = string.Empty;
        }

        /// <summary>One exported Known Character entry.</summary>
        internal sealed class KnownCharacterDto
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("bio")]
            public string Bio { get; set; } = string.Empty;
        }

        #endregion

        #region Export

        /// <summary>
        ///     Builds the export document from a character's inline definitions and (optionally) its
        ///     authored scene knowledge. Scene behaviors are carried as type hints via
        ///     <see cref="ConvaiActionsProductivityModel.CreateDetachedSnapshot" />, never as scene
        ///     references.
        /// </summary>
        internal static ExportDocument BuildDocument(
            IReadOnlyList<ConvaiActionDefinition> inlineDefinitions,
            bool includeSceneKnowledge,
            IReadOnlyList<ConvaiActionObjectDefinition> objects,
            IReadOnlyList<ConvaiActionCharacterDefinition> characters,
            string initialAttentionObject)
        {
            var document = new ExportDocument { Actions = new List<ActionDto>() };
            if (inlineDefinitions != null)
            {
                for (int i = 0; i < inlineDefinitions.Count; i++)
                {
                    ActionDto dto = ToDto(inlineDefinitions[i]);
                    if (dto != null)
                        document.Actions.Add(dto);
                }
            }

            if (!includeSceneKnowledge)
                return document;

            document.Objects = new List<KnownObjectDto>();
            if (objects != null)
            {
                for (int i = 0; i < objects.Count; i++)
                {
                    ConvaiActionObjectDefinition entry = objects[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                        continue;

                    document.Objects.Add(new KnownObjectDto
                    {
                        Name = entry.Name.Trim(),
                        Description = entry.Description ?? string.Empty
                    });
                }
            }

            document.Characters = new List<KnownCharacterDto>();
            if (characters != null)
            {
                for (int i = 0; i < characters.Count; i++)
                {
                    ConvaiActionCharacterDefinition entry = characters[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                        continue;

                    document.Characters.Add(new KnownCharacterDto
                    {
                        Name = entry.Name.Trim(),
                        Bio = entry.Bio ?? string.Empty
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(initialAttentionObject))
                document.InitialAttentionObject = initialAttentionObject.Trim();

            return document;
        }

        /// <summary>Maps one definition to its DTO (null definition → null).</summary>
        internal static ActionDto ToDto(ConvaiActionDefinition definition)
        {
            ConvaiActionDefinition detached = ConvaiActionsProductivityModel.CreateDetachedSnapshot(definition);
            if (detached == null)
                return null;

            var dto = new ActionDto
            {
                Name = detached.ActionName ?? string.Empty,
                Description = detached.Description ?? string.Empty,
                TargetRequirement = detached.TargetRequirement,
                BehaviorTypeHint = string.IsNullOrWhiteSpace(detached.ExecutorTypeHint) ? null : detached.ExecutorTypeHint,
                TimeoutSeconds = detached.TimeoutSeconds,
                FailurePolicy = detached.FailurePolicyOverride,
                WaitForBotSpeech = detached.WaitForBotSpeech,
                DelayAfterBotSpeechSeconds = detached.DelayAfterBotSpeechSeconds,
                Enabled = detached.Enabled,
                Category = string.IsNullOrEmpty(detached.Category) ? null : detached.Category
            };

            if (detached.Parameters != null && detached.Parameters.Count > 0)
            {
                dto.Parameters = new List<ParameterDto>(detached.Parameters.Count);
                for (int i = 0; i < detached.Parameters.Count; i++)
                {
                    ConvaiActionParameterDefinition parameter = detached.Parameters[i];
                    if (parameter == null)
                        continue;

                    dto.Parameters.Add(new ParameterDto
                    {
                        Name = parameter.Name ?? string.Empty,
                        Description = parameter.Description ?? string.Empty,
                        Type = parameter.Type,
                        Connector = string.IsNullOrWhiteSpace(parameter.Connector) ? null : parameter.Connector,
                        Choices = parameter.Choices != null && parameter.Choices.Count > 0
                            ? new List<string>(parameter.Choices)
                            : null
                    });
                }
            }

            return dto;
        }

        /// <summary>Serializes a document as indented JSON.</summary>
        internal static string ToJson(ExportDocument document) =>
            JsonConvert.SerializeObject(document, Formatting.Indented);

        #endregion

        #region Import

        /// <summary>
        ///     Parses exported JSON. Returns false with a beginner-readable error when the text is
        ///     not a valid export (bad JSON, no action list, or a newer schema version).
        /// </summary>
        internal static bool TryParse(string json, out ExportDocument document, out string error)
        {
            document = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The file is empty.";
                return false;
            }

            try
            {
                document = JsonConvert.DeserializeObject<ExportDocument>(json);
            }
            catch (JsonException exception)
            {
                error = exception.Message;
                return false;
            }

            if (document?.Actions == null)
            {
                document = null;
                error = "The file carries no action list, so it is not an Actions export.";
                return false;
            }

            if (document.SchemaVersion > CurrentSchemaVersion)
            {
                document = null;
                error = "The file was exported by a newer SDK version. Update the Convai SDK to import it.";
                return false;
            }

            return true;
        }

        /// <summary>Maps one DTO back to a definition (scene behavior stays a type hint).</summary>
        internal static ConvaiActionDefinition ToDefinition(ActionDto dto)
        {
            if (dto == null)
                return null;

            var definition = new ConvaiActionDefinition
            {
                ActionName = dto.Name ?? string.Empty,
                Description = dto.Description ?? string.Empty,
                TargetRequirement = dto.TargetRequirement,
                ExecutorTypeHint = dto.BehaviorTypeHint ?? string.Empty,
                TimeoutSeconds = Math.Max(0f, dto.TimeoutSeconds),
                FailurePolicyOverride = dto.FailurePolicy,
                WaitForBotSpeech = dto.WaitForBotSpeech,
                DelayAfterBotSpeechSeconds = Math.Max(0f, dto.DelayAfterBotSpeechSeconds),
                Enabled = dto.Enabled,
                Category = dto.Category ?? string.Empty,
                Parameters = new List<ConvaiActionParameterDefinition>()
            };

            if (dto.Parameters != null)
            {
                for (int i = 0; i < dto.Parameters.Count; i++)
                {
                    ParameterDto parameter = dto.Parameters[i];
                    if (parameter == null)
                        continue;

                    definition.Parameters.Add(new ConvaiActionParameterDefinition
                    {
                        Name = parameter.Name ?? string.Empty,
                        Description = parameter.Description ?? string.Empty,
                        Type = parameter.Type,
                        Connector = parameter.Connector ?? string.Empty,
                        Choices = parameter.Choices != null ? new List<string>(parameter.Choices) : new List<string>()
                    });
                }
            }

            return definition;
        }

        /// <summary>Per-outcome counts of one import merge.</summary>
        internal sealed class ImportResult
        {
            /// <summary>The rebuilt inline definition list (existing instances preserved unless overwritten).</summary>
            internal List<ConvaiActionDefinition> Definitions = new();

            /// <summary>The imported definition instances that ended up in <see cref="Definitions" />.</summary>
            internal List<ConvaiActionDefinition> Imported = new();

            internal int AddedCount;
            internal int OverwrittenCount;
            internal int RenamedCount;
            internal int SkippedCount;
        }

        /// <summary>
        ///     Merges a parsed document's actions into an existing inline definition list under one
        ///     collision mode. Collisions are name matches (case-insensitive) against the existing
        ///     <em>inline</em> list; renaming additionally avoids every name in
        ///     <paramref name="reservedNames" /> (typically the character's full effective name set,
        ///     so a renamed import cannot shadow an Action Set entry). Pure list-in/list-out — the
        ///     caller owns Undo, dirtying, and writing the result back.
        /// </summary>
        internal static ImportResult ApplyImport(
            IReadOnlyList<ConvaiActionDefinition> existingInline,
            IReadOnlyList<string> reservedNames,
            ExportDocument document,
            ConvaiActionsImportCollisionMode collisionMode)
        {
            var result = new ImportResult();
            if (existingInline != null)
                result.Definitions.AddRange(existingInline);

            if (document?.Actions == null)
                return result;

            var takenNames = new List<string>();
            if (reservedNames != null)
                takenNames.AddRange(reservedNames);
            for (int i = 0; i < result.Definitions.Count; i++)
            {
                string existingName = result.Definitions[i]?.ActionName;
                if (!string.IsNullOrWhiteSpace(existingName))
                    takenNames.Add(existingName.Trim());
            }

            for (int i = 0; i < document.Actions.Count; i++)
            {
                ConvaiActionDefinition incoming = ToDefinition(document.Actions[i]);
                if (incoming == null || string.IsNullOrWhiteSpace(incoming.ActionName))
                    continue;

                int collisionIndex = FindByName(result.Definitions, incoming.ActionName);
                if (collisionIndex < 0)
                {
                    result.Definitions.Add(incoming);
                    result.Imported.Add(incoming);
                    result.AddedCount++;
                    takenNames.Add(incoming.ActionName.Trim());
                    continue;
                }

                switch (collisionMode)
                {
                    case ConvaiActionsImportCollisionMode.Overwrite:
                        result.Definitions[collisionIndex] = incoming;
                        result.Imported.Add(incoming);
                        result.OverwrittenCount++;
                        break;

                    case ConvaiActionsImportCollisionMode.Rename:
                        incoming.ActionName =
                            ConvaiActionsProductivityModel.MakeDuplicateActionName(incoming.ActionName, takenNames);
                        result.Definitions.Add(incoming);
                        result.Imported.Add(incoming);
                        result.RenamedCount++;
                        takenNames.Add(incoming.ActionName);
                        break;

                    default:
                        result.SkippedCount++;
                        break;
                }
            }

            return result;
        }

        /// <summary>
        ///     Adds imported Known Object entries whose names are not already present
        ///     (case-insensitive; existing entries are never modified). Returns how many were added.
        /// </summary>
        internal static int MergeKnownObjects(
            List<ConvaiActionObjectDefinition> objects,
            IReadOnlyList<KnownObjectDto> incoming)
        {
            if (objects == null || incoming == null)
                return 0;

            int added = 0;
            for (int i = 0; i < incoming.Count; i++)
            {
                KnownObjectDto dto = incoming[i];
                if (dto == null || string.IsNullOrWhiteSpace(dto.Name) || ContainsObjectName(objects, dto.Name))
                    continue;

                objects.Add(new ConvaiActionObjectDefinition
                {
                    Name = dto.Name.Trim(),
                    Description = dto.Description ?? string.Empty
                });
                added++;
            }

            return added;
        }

        /// <summary>Character-list counterpart of <see cref="MergeKnownObjects" />.</summary>
        internal static int MergeKnownCharacters(
            List<ConvaiActionCharacterDefinition> characters,
            IReadOnlyList<KnownCharacterDto> incoming)
        {
            if (characters == null || incoming == null)
                return 0;

            int added = 0;
            for (int i = 0; i < incoming.Count; i++)
            {
                KnownCharacterDto dto = incoming[i];
                if (dto == null || string.IsNullOrWhiteSpace(dto.Name) || ContainsCharacterName(characters, dto.Name))
                    continue;

                characters.Add(new ConvaiActionCharacterDefinition
                {
                    Name = dto.Name.Trim(),
                    Bio = dto.Bio ?? string.Empty
                });
                added++;
            }

            return added;
        }

        private static int FindByName(List<ConvaiActionDefinition> definitions, string actionName)
        {
            string needle = actionName?.Trim();
            if (string.IsNullOrEmpty(needle))
                return -1;

            for (int i = 0; i < definitions.Count; i++)
            {
                string candidate = definitions[i]?.ActionName;
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    string.Equals(candidate.Trim(), needle, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private static bool ContainsObjectName(List<ConvaiActionObjectDefinition> objects, string name)
        {
            string needle = name.Trim();
            for (int i = 0; i < objects.Count; i++)
            {
                string candidate = objects[i]?.Name;
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    string.Equals(candidate.Trim(), needle, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool ContainsCharacterName(List<ConvaiActionCharacterDefinition> characters, string name)
        {
            string needle = name.Trim();
            for (int i = 0; i < characters.Count; i++)
            {
                string candidate = characters[i]?.Name;
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    string.Equals(candidate.Trim(), needle, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        #endregion
    }
}
