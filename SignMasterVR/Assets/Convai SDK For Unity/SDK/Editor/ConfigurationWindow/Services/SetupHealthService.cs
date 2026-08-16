using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Editor.Settings.Services;
using Convai.Runtime;
using Convai.Runtime.Components;
using Convai.Runtime.Core.Configuration;
using Convai.Shared.Compatibility;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Editor.ConfigurationWindow.Services
{
    /// <summary>
    ///     Evaluates setup-health state for the configuration dashboard.
    /// </summary>
    public static class SetupHealthService
    {
        /// <summary>
        ///     Build a setup-health report for the active scene and project configuration.
        /// </summary>
        public static SetupHealthReport BuildReport()
        {
            List<SetupHealthCheckResult> checks = new()
            {
                CheckApiKey(), CheckRequiredSceneComponents(), CheckCharacterSetup(), CheckPlayerSetup()
            };

            return new SetupHealthReport(checks);
        }

        internal static SetupHealthCheckResult CheckApiKey()
        {
            var settings = ConvaiSettings.Instance;
            if (settings == null)
                return new SetupHealthCheckResult(
                    "api-key",
                    "API Key",
                    SetupHealthStatus.Blocked,
                    "Convai settings could not be loaded.");

            if (settings.AuthMode == ConvaiAuthMode.AuthToken)
                return CheckAuthTokenConfiguration(settings);

            if (!settings.HasApiKey)
                return new SetupHealthCheckResult(
                    "api-key",
                    "API Key",
                    SetupHealthStatus.Blocked,
                    "Missing. Set it in Edit > Project Settings > Convai SDK.");

            if (ApiKeyValidationService.TryGetCachedResult(
                    settings.ApiKey, settings.ApiEnvironment, settings.RestBaseUrlOverride,
                    out ApiKeyValidationResult cached))
            {
                return cached.IsValid
                    ? new SetupHealthCheckResult(
                        "api-key", "API Key", SetupHealthStatus.Healthy, "Configured and validated.")
                    : new SetupHealthCheckResult(
                        "api-key", "API Key", SetupHealthStatus.Blocked,
                        $"Configured but failed validation: {cached.Message}");
            }

            return new SetupHealthCheckResult(
                "api-key", "API Key", SetupHealthStatus.Warning,
                "Configured but not yet validated. Use Validate in SDK Settings > Credentials.");
        }

        private static SetupHealthCheckResult CheckAuthTokenConfiguration(ConvaiSettings settings)
        {
            if (settings.HasValidAuthConfig)
            {
                return new SetupHealthCheckResult(
                    "api-key",
                    "Auth Token",
                    SetupHealthStatus.Healthy,
                    "A runtime auth-token source is configured. The token will be resolved when a room connection starts.");
            }

            if (!string.IsNullOrWhiteSpace(settings.AuthTokenEndpointUrl))
            {
                return new SetupHealthCheckResult(
                    "api-key",
                    "Auth Token",
                    SetupHealthStatus.Blocked,
                    "The token endpoint URL is invalid. Use HTTPS, or HTTP only for loopback development, or register an IConvaiAuthTokenProvider before connecting.");
            }

            return new SetupHealthCheckResult(
                "api-key",
                "Auth Token",
                SetupHealthStatus.Blocked,
                "No token endpoint is configured. Set one in SDK Settings > Credentials or register an IConvaiAuthTokenProvider before connecting.");
        }

        private static SetupHealthCheckResult CheckRequiredSceneComponents()
        {
            bool hasManager = Object.FindAnyObjectByType<ConvaiManager>() != null;

            if (hasManager)
            {
                return new SetupHealthCheckResult(
                    "required-scene-components",
                    "Required Scene Components",
                    SetupHealthStatus.Healthy,
                    "ConvaiManager is present and will bootstrap required Convai scene components.");
            }

            return new SetupHealthCheckResult(
                "required-scene-components",
                "Required Scene Components",
                SetupHealthStatus.Blocked,
                "Missing ConvaiManager. Add it via GameObject > Convai > Setup Required Components.");
        }

        private static SetupHealthCheckResult CheckCharacterSetup()
        {
            ConvaiCharacter[] characters = ConvaiObjectFind.All<ConvaiCharacter>(FindObjectsInactive.Exclude);
            if (characters.Length == 0)
            {
                return new SetupHealthCheckResult(
                    "characters",
                    "Character Setup",
                    SetupHealthStatus.Blocked,
                    "No ConvaiCharacter components found. Add at least one ConvaiCharacter to run conversations.");
            }

            int missingCharacterIdCount =
                characters.Count(character => string.IsNullOrWhiteSpace(character.CharacterId));
            if (missingCharacterIdCount > 0)
            {
                return new SetupHealthCheckResult(
                    "characters",
                    "Character Setup",
                    SetupHealthStatus.Blocked,
                    $"{missingCharacterIdCount} character(s) are missing Character ID.");
            }

            return new SetupHealthCheckResult(
                "characters",
                "Character Setup",
                SetupHealthStatus.Healthy,
                $"{characters.Length} character(s) ready.");
        }

        private static SetupHealthCheckResult CheckPlayerSetup()
        {
            ConvaiPlayer[] players = ConvaiObjectFind.All<ConvaiPlayer>(FindObjectsInactive.Exclude);
            if (players.Length == 0)
            {
                return new SetupHealthCheckResult(
                    "players",
                    "Player Setup",
                    SetupHealthStatus.Blocked,
                    "No ConvaiPlayer component found. Add one ConvaiPlayer to run conversations.");
            }

            return new SetupHealthCheckResult(
                "players",
                "Player Setup",
                SetupHealthStatus.Healthy,
                $"{players.Length} player component(s) found.");
        }
    }

    /// <summary>
    ///     Setup-health report model.
    /// </summary>
    public sealed class SetupHealthReport
    {
        public SetupHealthReport(IReadOnlyList<SetupHealthCheckResult> results)
        {
            Results = results ?? Array.Empty<SetupHealthCheckResult>();
        }

        /// <summary>Ordered setup-health results.</summary>
        public IReadOnlyList<SetupHealthCheckResult> Results { get; }

        /// <summary>True when at least one blocking issue exists.</summary>
        public bool HasBlockingIssues => Results.Any(result => result.Status == SetupHealthStatus.Blocked);

        /// <summary>True when at least one warning exists.</summary>
        public bool HasWarnings => Results.Any(result => result.Status == SetupHealthStatus.Warning);
    }

    /// <summary>
    ///     One setup-health check result.
    /// </summary>
    public sealed class SetupHealthCheckResult
    {
        public SetupHealthCheckResult(string id, string title, SetupHealthStatus status, string message)
        {
            Id = id;
            Title = title;
            Status = status;
            Message = message;
        }

        /// <summary>Unique check identifier.</summary>
        public string Id { get; }

        /// <summary>Human-readable title.</summary>
        public string Title { get; }

        /// <summary>Current setup-health status.</summary>
        public SetupHealthStatus Status { get; }

        /// <summary>Description of current result state.</summary>
        public string Message { get; }
    }

    /// <summary>
    ///     Setup-health status levels.
    /// </summary>
    public enum SetupHealthStatus
    {
        /// <summary>Everything is healthy.</summary>
        Healthy,

        /// <summary>Non-blocking issue that should be addressed.</summary>
        Warning,

        /// <summary>Blocking issue that prevents successful setup.</summary>
        Blocked
    }
}
