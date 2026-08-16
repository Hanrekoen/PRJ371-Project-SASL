using System.Collections;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;

namespace Convai.Runtime.Adapters.Networking
{
    public partial class ConvaiRoomManager
    {
        private void Awake()
        {
            if (transform.parent != null) transform.SetParent(null);

            bool shouldPersistAcrossScenes = ConvaiManager.ActiveManager != null &&
                                             ConvaiManager.ActiveManager.ShouldPersistAcrossScenes;

            if (!shouldPersistAcrossScenes)
            {
                _instance = this;
                return;
            }

            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this) Destroy(gameObject);
        }

        private IEnumerator Start()
        {
            ConvaiLogger.Debug("*** Initializing room connection... ***", LogCategory.SDK);

            RoomCompositionStartupResult startupResult =
                _roomCompositionService.ValidateAndComposeStartup(CreateCompositionContext(), CreateCompositionState());

            if (!startupResult.IsValid)
            {
                if (!string.IsNullOrEmpty(startupResult.ErrorMessage))
                    ConvaiLogger.Error(startupResult.ErrorMessage, LogCategory.SDK);

                if (!string.IsNullOrEmpty(startupResult.FailureErrorCode))
                {
                    RecordConnectionFailure(ConnectionFailure.Create(
                        startupResult.FailureErrorCode,
                        startupResult.FailureRecordMessage,
                        SessionErrorStage.Configuration,
                        SessionErrorCodes.IsRecoverable(startupResult.FailureErrorCode)));
                }

                if (startupResult.DisableManager)
                    enabled = false;

                yield break;
            }

            ApplyCompositionArtifacts(startupResult.Artifacts, true);
            _roomConnectionRuntimeAdapter?.SignalStartCompleted();

            if (!startupResult.ShouldAutoConnect)
            {
                string message = startupResult.FailureReason == RoomStartupFailureReason.MissingCharacters
                    ? "No owned characters found at startup - waiting for runtime character registration"
                    : "Lazy connection mode - waiting for manual ConnectAsync() call";
                ConvaiLogger.Debug(message, LogCategory.SDK);
                yield break;
            }

            ConvaiLogger.Debug("Initialization complete. Auto-connecting...", LogCategory.SDK);
            _ = ConnectAsync().AsTask().ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    ConvaiLogger.Error(
                        $"Auto-connect failed: {task.Exception?.GetBaseException()?.Message}",
                        LogCategory.SDK);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            DetachRuntimeEventBridges();

            if (_sessionStateToken != default)
                _eventHub.Unsubscribe(_sessionStateToken);

            DisposeCurrentComposition();
        }
    }
}
