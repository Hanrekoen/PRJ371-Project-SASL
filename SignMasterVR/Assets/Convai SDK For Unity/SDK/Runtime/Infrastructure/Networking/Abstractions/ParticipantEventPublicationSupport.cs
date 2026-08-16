using System;
using Convai.Domain.DomainEvents.Participant;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Shared helper for publishing participant lifecycle events through the EventHub.
    ///     Keeps participant resolution, transport wiring, and platform-specific side effects local to controllers.
    /// </summary>
    internal static class ParticipantEventPublicationSupport
    {
        internal static void PublishConnected(
            IEventHub eventHub,
            ILogger logger,
            ParticipantInfo participant,
            string publisherName,
            LogCategory logCategory = LogCategory.Transport) =>
            Publish(eventHub, logger, ParticipantConnected.Create(participant), nameof(ParticipantConnected),
                participant,
                publisherName, logCategory);

        internal static void PublishDisconnected(
            IEventHub eventHub,
            ILogger logger,
            ParticipantInfo participant,
            string publisherName,
            LogCategory logCategory = LogCategory.Transport) =>
            Publish(eventHub, logger, ParticipantDisconnected.Create(participant), nameof(ParticipantDisconnected),
                participant, publisherName, logCategory);

        private static void Publish<TEvent>(
            IEventHub eventHub,
            ILogger logger,
            TEvent @event,
            string eventName,
            ParticipantInfo participant,
            string publisherName,
            LogCategory logCategory) where TEvent : notnull
        {
            string source = string.IsNullOrWhiteSpace(publisherName)
                ? nameof(ParticipantEventPublicationSupport)
                : publisherName;
            logger = logger.WithTag(source);

            if (eventHub == null)
            {
                logger?.Debug($"EventHub unavailable; skipping {eventName} event.", logCategory);
                return;
            }

            try
            {
                eventHub.Publish(@event);
                logger?.Debug(
                    $"Published {eventName} via EventHub: {participant.DisplayName} ({participant.ParticipantId})",
                    logCategory);
            }
            catch (Exception ex)
            {
                logger?.Debug($"Failed to publish {eventName} event: {ex.Message}", logCategory);
            }
        }
    }
}
