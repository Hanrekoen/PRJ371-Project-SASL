using System;
using Convai.Domain.Logging;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Shared helper for bridging remote audio track notifications to room-controller listeners.
    ///     Keeps participant resolution, mute policy, room-facade control, and platform-specific track handling local.
    /// </summary>
    internal static class RemoteTrackSessionNotificationSupport
    {
        internal static void NotifyAudioTrackSubscribed(
            Action<IRemoteAudioTrack, string, string> notification,
            IRemoteAudioTrack audioTrack,
            string participantSid,
            string characterId,
            ILogger logger,
            string publisherName,
            Func<Action, bool> notificationBridge = null,
            LogCategory logCategory = LogCategory.Audio)
        {
            logger = logger.WithTag(ResolveSource(publisherName));
            if (audioTrack == null)
            {
                logger?.Debug(
                    $"Remote audio track unavailable; skipping OnRemoteAudioTrackSubscribed notification for participant: {participantSid}.",
                    logCategory);
                return;
            }

            Notify(notification, () => notification?.Invoke(audioTrack, participantSid, characterId),
                "OnRemoteAudioTrackSubscribed",
                $"Notified OnRemoteAudioTrackSubscribed: {participantSid} ({characterId})",
                logger, publisherName, notificationBridge, logCategory);
        }

        internal static void NotifyAudioTrackUnsubscribed(
            Action<string, string> notification,
            string participantSid,
            string characterId,
            ILogger logger,
            string publisherName,
            Func<Action, bool> notificationBridge = null,
            LogCategory logCategory = LogCategory.Audio) =>
            Notify(notification, () => notification?.Invoke(participantSid, characterId),
                "OnRemoteAudioTrackUnsubscribed",
                $"Notified OnRemoteAudioTrackUnsubscribed: {participantSid} ({characterId})",
                logger, publisherName, notificationBridge, logCategory);

        private static void Notify(
            Delegate notification,
            Action invoke,
            string notificationName,
            string successMessage,
            ILogger logger,
            string publisherName,
            Func<Action, bool> notificationBridge,
            LogCategory logCategory)
        {
            string source = ResolveSource(publisherName);
            logger = logger.WithTag(source);

            if (notification == null)
            {
                logger?.Debug($"No listeners registered; skipping {notificationName} notification.",
                    logCategory);
                return;
            }

            try
            {
                if (!(notificationBridge ?? DispatchInline)(invoke))
                {
                    logger?.Debug($"Failed to dispatch {notificationName} notification.", logCategory);
                    return;
                }

                logger?.Debug(successMessage, logCategory);
            }
            catch (Exception ex)
            {
                logger?.Debug($"Failed to notify {notificationName}: {ex.Message}", logCategory);
            }
        }

        private static string ResolveSource(string publisherName) =>
            string.IsNullOrWhiteSpace(publisherName)
                ? nameof(RemoteTrackSessionNotificationSupport)
                : publisherName;

        private static bool DispatchInline(Action action)
        {
            action?.Invoke();
            return true;
        }
    }
}
