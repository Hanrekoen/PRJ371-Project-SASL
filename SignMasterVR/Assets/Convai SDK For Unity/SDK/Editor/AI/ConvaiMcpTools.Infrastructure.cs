using System;
using System.Collections.Generic;
using Convai.Runtime.Components;
using Convai.Shared.Compatibility;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Convai.Editor.AI
{
    public static partial class ConvaiMcpTools
    {
        private static object Success(string message, object data) => ConvaiMcpResponses.Success(message, data);

        private static object Failure(string code, string message, object data) =>
            ConvaiMcpResponses.Failure(code, message, data);

        private static object Result(bool success, string message, object data) =>
            ConvaiMcpResponses.Envelope(success, message, data);

        private static object StandardResponseSchema() => ConvaiMcpResponses.StandardResponseSchema();

        private static object EmptyInputSchema() => new
        {
            type = "object",
            properties = new { },
            additionalProperties = false
        };

        private static object BooleanInputSchema(string propertyName, string description, bool defaultValue) => new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                [propertyName] = new
                {
                    type = "boolean",
                    description,
                    @default = defaultValue
                }
            },
            additionalProperties = false
        };

        private static object EnumInputSchema<T>(string propertyName, string description, T defaultValue)
            where T : struct, Enum => new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                [propertyName] = new
                {
                    type = "string",
                    description,
                    @enum = Enum.GetNames(typeof(T)),
                    @default = defaultValue.ToString()
                }
            },
            additionalProperties = false
        };

        private static T Parse<T>(JObject parameters) where T : class, new() =>
            parameters?.ToObject<T>() ?? new T();

        private static T FindFirst<T>() where T : UnityEngine.Object
        {
            T[] values = ConvaiObjectFind.All<T>(FindObjectsInactive.Include);
            return values.Length > 0 ? values[0] : null;
        }

        private static object[] DescribeComponents<T>(T[] components) where T : Component
        {
            var descriptions = new object[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                T component = components[i];
                descriptions[i] = new
                {
                    instanceId = ConvaiMcpEntityRef.ToToolId(component.gameObject),
                    componentInstanceId = ConvaiMcpEntityRef.ToToolId(component),
                    name = component.gameObject.name,
                    activeInHierarchy = component.gameObject.activeInHierarchy,
                    scene = component.gameObject.scene.name
                };
            }

            return descriptions;
        }

        private static object[] DescribePlayers(ConvaiPlayer[] players)
        {
            var descriptions = new object[players.Length];
            for (int i = 0; i < players.Length; i++)
            {
                ConvaiPlayer player = players[i];
                descriptions[i] = new
                {
                    instanceId = ConvaiMcpEntityRef.ToToolId(player.gameObject),
                    componentInstanceId = ConvaiMcpEntityRef.ToToolId(player),
                    name = player.gameObject.name,
                    playerName = player.PlayerName,
                    activeInHierarchy = player.gameObject.activeInHierarchy,
                    scene = player.gameObject.scene.name
                };
            }

            return descriptions;
        }

        private static object[] DescribeCharacters(ConvaiCharacter[] characters)
        {
            var descriptions = new object[characters.Length];
            for (int i = 0; i < characters.Length; i++)
            {
                ConvaiCharacter character = characters[i];
                descriptions[i] = new
                {
                    instanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                    componentInstanceId = ConvaiMcpEntityRef.ToToolId(character),
                    name = character.gameObject.name,
                    characterId = character.CharacterId,
                    activeInHierarchy = character.gameObject.activeInHierarchy,
                    scene = character.gameObject.scene.name,
                    modules = DescribeModules(character.gameObject)
                };
            }

            return descriptions;
        }

        /// <summary>
        ///     What each Convai module makes of this character, so an assistant inspecting a scene
        ///     can see a capability exists at all before it knows which tool to reach for. Empty
        ///     when no module has registered a surveyor.
        /// </summary>
        private static object[] DescribeModules(GameObject characterRoot)
        {
            IReadOnlyList<ConvaiModuleSurveyResult> surveys =
                ConvaiModuleSurveyRegistry.SurveyAll(characterRoot);
            var descriptions = new object[surveys.Count];
            for (int i = 0; i < surveys.Count; i++)
            {
                ConvaiModuleSurveyResult survey = surveys[i];
                descriptions[i] = new
                {
                    moduleId = survey.ModuleId,
                    name = survey.DisplayName,
                    readiness = survey.Readiness.ToString(),
                    present = survey.IsPresent,
                    working = survey.IsFunctional,
                    summary = survey.Summary,
                    blocker = survey.Blocker,
                    findingCount = survey.Findings.Count
                };
            }

            return descriptions;
        }

        /// <summary>
        ///     Folds every registered module's view of every character in the active scene into the
        ///     validation report, so a blocked capability is visible from the one tool an assistant
        ///     already calls before and after any change. Blocking findings are errors; everything
        ///     else worth acting on is a warning with the module's own next step.
        /// </summary>
        private static void AddModuleFindings(
            List<string> errors, List<string> warnings, List<string> nextSteps)
        {
            ConvaiCharacter[] characters = ConvaiObjectFind.All<ConvaiCharacter>(FindObjectsInactive.Include);
            for (int i = 0; i < characters.Length; i++)
            {
                ConvaiCharacter character = characters[i];
                if (character.gameObject.scene != SceneManager.GetActiveScene()) continue;

                IReadOnlyList<ConvaiModuleSurveyResult> surveys =
                    ConvaiModuleSurveyRegistry.SurveyAll(character.gameObject);
                for (int s = 0; s < surveys.Count; s++)
                {
                    ConvaiModuleSurveyResult survey = surveys[s];
                    if (!survey.IsPresent) continue;

                    IReadOnlyList<ConvaiModuleSurveyFinding> findings = survey.Findings;
                    for (int f = 0; f < findings.Count; f++)
                    {
                        ConvaiModuleSurveyFinding finding = findings[f];
                        if (finding.Severity < ConvaiModuleFindingSeverity.Warning) continue;

                        string line = $"{survey.DisplayName} on '{character.gameObject.name}': {finding.Message}";
                        if (finding.Severity == ConvaiModuleFindingSeverity.Error)
                        {
                            AddUnique(errors, new[] { line });
                            AddUnique(nextSteps, new[] { line });
                        }
                        else
                        {
                            AddUnique(warnings, new[] { line });
                        }
                    }
                }
            }
        }

        private static void AddUnique(List<string> destination, IEnumerable<string> source)
        {
            foreach (string value in source)
            {
                if (!destination.Contains(value)) destination.Add(value);
            }
        }

        private static string TryGetPackageRoot() =>
            ConvaiSceneSetupApi.TryGetConvaiSdkPackageRoot(out string packageRoot)
                ? packageRoot
                : string.Empty;
    }
}
