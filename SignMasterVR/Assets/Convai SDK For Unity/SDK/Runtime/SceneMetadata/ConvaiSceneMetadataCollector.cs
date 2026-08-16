using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using UnityEngine;

namespace Convai.Runtime.SceneMetadata
{
    /// <summary>
    ///     Collects metadata from all registered ConvaiObjectMetadata components and sends it via RTVI.
    ///     Uses the efficient ConvaiMetadataRegistry instead of expensive FindObjectsOfType calls.
    ///     Uses EventHub subscriptions and supports dependency injection.
    /// </summary>
    /// <remarks>
    ///     Discovered during runtime initialization and configured through dependency injection.
    /// </remarks>
    public class ConvaiSceneMetadataCollector : MonoBehaviour
    {
#pragma warning disable CS0649
        [Tooltip("Whether to automatically collect metadata on Start")] [SerializeField]
        [ConvaiInspectorSection("Collection Settings")]
        private bool _collectOnStart;
#pragma warning restore CS0649

        [Tooltip("Whether to log collection stats")] [SerializeField]
        [ConvaiInspectorSection("Collection Settings")]
        private bool _logStatistics = true;

        [ReadOnly] [SerializeField]
        [Tooltip("Number of metadata objects collected the last time this ran (read-only)")]
        [ConvaiInspectorSection("Status")]
        private int _lastCollectedCount;

        [ReadOnly] [SerializeField]
        [Tooltip("How long the last collection pass took, in seconds (read-only)")]
        [ConvaiInspectorSection("Status")]
        private float _lastCollectionTime;
        private IEventHub _eventHub;
        private bool _isInjected;
        private IConvaiRoomConnectionService _roomConnectionService;

        private SubscriptionToken? _sessionStateToken;

        private void Start()
        {
            TryResolveDependencies();
            if (!_isInjected)
            {
                ConvaiLogger.Error(
                    "Dependencies not injected. Add ConvaiManager to scene.",
                    LogCategory.SDK);
                enabled = false;
                return;
            }

            _sessionStateToken = _eventHub.Subscribe<SessionStateChanged>(OnSessionStateChanged);
        }

        private void OnDestroy()
        {
            if (_sessionStateToken.HasValue && _eventHub != null)
            {
                _eventHub.Unsubscribe(_sessionStateToken.Value);
                _sessionStateToken = null;
            }
        }

        /// <summary>
        ///     Injects dependencies into ConvaiSceneMetadataCollector.
        ///     Called by the ConvaiManager pipeline.
        /// </summary>
        /// <param name="eventHub">Event hub for subscribing to domain events (required)</param>
        /// <param name="roomConnectionService">Room connection service (required)</param>
        public void Inject(IEventHub eventHub, IConvaiRoomConnectionService roomConnectionService)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _roomConnectionService =
                roomConnectionService ?? throw new ArgumentNullException(nameof(roomConnectionService));
            _isInjected = true;
        }

        private void TryResolveDependencies()
        {
            if (_isInjected) return;

            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null) return;

            manager.TryGetEventHub(out IEventHub eventHub);
            manager.TryGetRoomConnectionService(out IConvaiRoomConnectionService roomConnectionService);

            if (eventHub != null && roomConnectionService != null)
                Inject(eventHub, roomConnectionService);
        }

        /// <summary>
        ///     Handles session state changes from EventHub.
        /// </summary>
        private void OnSessionStateChanged(SessionStateChanged e)
        {
            if (e.NewState == SessionState.Connected) OnRoomConnected();
        }

        /// <summary>
        ///     Called when the room connection is successful - sends metadata if configured to collect on start
        /// </summary>
        private void OnRoomConnected()
        {
            if (_collectOnStart) CollectAndSendSceneMetadata();
        }

        /// <summary>
        ///     Collects metadata from all registered ConvaiObjectMetadata components and sends via RTVI
        ///     Can be called manually after room connection or will be called automatically if _collectOnStart is enabled
        /// </summary>
        public void CollectAndSendSceneMetadata()
        {
            if (_roomConnectionService == null)
            {
                ConvaiLogger.Error(
                    "Room connection service not injected. Add ConvaiManager to scene.",
                    LogCategory.SDK);
                return;
            }

            if (!_roomConnectionService.IsConnected)
            {
                ConvaiLogger.Warning(
                    "Room connection is not ready. Ensure the room is connected before sending metadata.",
                    LogCategory.SDK);
                return;
            }

            float startTime = Time.realtimeSinceStartup;

            List<Infrastructure.Protocol.Messages.SceneMetadata> sceneMetadataList =
                ConvaiMetadataRegistry.GetSceneMetadataList();

            _lastCollectedCount = sceneMetadataList.Count;
            _lastCollectionTime = Time.realtimeSinceStartup - startTime;

            if (_logStatistics)
            {
                Dictionary<string, object> stats = ConvaiMetadataRegistry.GetStatistics();
                ConvaiLogger.Debug(
                    $"Collected {_lastCollectedCount} metadata objects in {_lastCollectionTime:F4}s. " +
                    $"Registry stats: {stats["TotalRegistered"]} total, {stats["ValidMetadata"]} valid, {stats["InvalidMetadata"]} invalid",
                    LogCategory.SDK);
            }

            bool sent = _roomConnectionService.UpdateSceneMetadata(sceneMetadataList);
            if (!sent)
            {
                ConvaiLogger.Warning(
                    "Failed to send scene metadata because the room connection service was not ready.",
                    LogCategory.SDK);
                return;
            }

            ConvaiLogger.Debug(
                $"Sent {_lastCollectedCount} metadata objects to RTVI service",
                LogCategory.SDK);
        }

        /// <summary>
        ///     Gets the current metadata count without sending
        /// </summary>
        /// <returns>Number of valid metadata objects currently registered</returns>
        public int GetMetadataCount() => ConvaiMetadataRegistry.GetValidMetadata().Length;

        /// <summary>
        ///     Gets all current metadata without sending
        /// </summary>
        /// <returns>List of current scene metadata</returns>
        public List<Infrastructure.Protocol.Messages.SceneMetadata> GetCurrentMetadata() =>
            ConvaiMetadataRegistry.GetSceneMetadataList();

        /// <summary>
        ///     Validates all registered metadata and logs any issues
        /// </summary>
        public void ValidateAllMetadata()
        {
            ConvaiObjectMetadata[] allMetadata = ConvaiMetadataRegistry.GetAllMetadata();
            List<string> validationIssues = new();

            foreach (ConvaiObjectMetadata metadata in allMetadata)
            {
                if (metadata == null)
                {
                    validationIssues.Add("Found null metadata reference");
                    continue;
                }

                List<string> errors = metadata.GetValidationErrors();
                if (errors.Count > 0) validationIssues.Add($"{metadata.gameObject.name}: {string.Join(", ", errors)}");
            }

            if (validationIssues.Count > 0)
            {
                ConvaiLogger.Warning(
                    $"Found {validationIssues.Count} validation issues:\n" +
                    string.Join("\n", validationIssues), LogCategory.SDK);
            }
            else
            {
                ConvaiLogger.Debug(
                    $"All {allMetadata.Length} metadata objects are valid",
                    LogCategory.SDK);
            }
        }

        /// <summary>
        ///     Checks if the metadata collection system is properly set up and ready to send data
        /// </summary>
        /// <returns>True if ready to send metadata, false otherwise</returns>
        public bool IsReadyToSendMetadata()
        {
            if (_roomConnectionService == null)
            {
                ConvaiLogger.Warning(
                    "Room connection service not injected. Add ConvaiManager to scene.",
                    LogCategory.SDK);
                return false;
            }

            if (!_roomConnectionService.IsConnected)
            {
                ConvaiLogger.Warning(
                    "Room connection service is not connected.",
                    LogCategory.SDK);
                return false;
            }

            return true;
        }
    }
}
