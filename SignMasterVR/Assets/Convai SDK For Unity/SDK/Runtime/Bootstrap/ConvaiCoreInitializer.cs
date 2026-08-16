using Convai.Domain.Logging;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Runtime.Bootstrap
{
    /// <summary>
    ///     Initializes the Convai SDK at runtime.
    ///     Settings are loaded from <see cref="ConvaiSettings" /> (Project Settings > Convai SDK).
    ///     Session data is managed by <see cref="ConvaiSessionData" />.
    /// </summary>
    public static class ConvaiCoreInitializer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            ConvaiLogger.Initialize();
            ConvaiLogger.Info("Convai Bootstrapper: Initializing...", LogCategory.Bootstrap);

            ValidateSettings();

            _ = ConvaiSessionData.Instance;

            ConvaiLogger.Info("Convai Bootstrapper: Initialization complete.", LogCategory.Bootstrap);
        }

        private static void ValidateSettings()
        {
            var settings = ConvaiSettings.Instance;

            if (settings == null)
            {
                ConvaiLogger.Error(
                    "Convai Bootstrapper: ConvaiSettings not found! Please configure settings via Edit > Project Settings > Convai SDK.",
                    LogCategory.Bootstrap);
                return;
            }

            if (settings.AuthMode == ConvaiAuthMode.ApiKey && !settings.HasApiKey)
            {
                ConvaiLogger.Warning(
                    "Convai Bootstrapper: API key not configured. Please set your API key in Edit > Project Settings > Convai SDK.",
                    LogCategory.Bootstrap);
                return;
            }

            if (settings.AuthMode != ConvaiAuthMode.AuthToken || settings.HasValidAuthConfig)
                return;

            // A code-based provider is commonly registered later from a scene Awake callback. Keep this diagnostic
            // informational here; the room startup gate performs the authoritative validation after all Awakes run.
            ConvaiLogger.Info(
                "Convai Bootstrapper: Auth Token mode is awaiting a registered provider or a valid endpoint before the first connection.",
                LogCategory.Bootstrap);
        }
    }
}
