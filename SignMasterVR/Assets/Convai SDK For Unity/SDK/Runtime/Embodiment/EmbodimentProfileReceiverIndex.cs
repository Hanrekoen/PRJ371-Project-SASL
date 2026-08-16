using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Per-character roster of <see cref="IEmbodimentProfileReceiver" /> registrations used by
    ///     <see cref="EmbodimentContext" /> to dispatch live preset application. Owns the
    ///     deduplication, removal, and exception-safe registration notification.
    /// </summary>
    internal sealed class EmbodimentProfileReceiverIndex
    {
        private readonly List<EmbodimentProfileReceiverRegistration> _registrations = new();
        private EmbodimentContext _ownerContext;

        public EmbodimentProfileReceiverIndex()
        {
        }

        internal void EnsureAttached(EmbodimentContext owner)
        {
            if (owner != null && _ownerContext == null)
                _ownerContext = owner;
        }

        public event Action<EmbodimentProfileReceiverRegistration> Registered;

        public void Register(IEmbodimentProfileReceiver receiver, Component owner)
        {
            if (receiver == null) return;

            for (int i = 0; i < _registrations.Count; i++)
            {
                if (_registrations[i].Matches(receiver))
                    return;
            }

            Component resolvedOwner = owner != null ? owner : receiver as Component;
            var registration = new EmbodimentProfileReceiverRegistration(receiver, resolvedOwner);
            _registrations.Add(registration);
            Raise(registration);
        }

        public void Unregister(IEmbodimentProfileReceiver receiver)
        {
            if (receiver == null) return;

            for (int i = _registrations.Count - 1; i >= 0; i--)
            {
                if (_registrations[i].Matches(receiver))
                    _registrations.RemoveAt(i);
            }
        }

        public void CopyTo(List<EmbodimentProfileReceiverRegistration> destination)
        {
            if (destination == null) return;
            destination.AddRange(_registrations);
        }

        private void Raise(EmbodimentProfileReceiverRegistration registration)
        {
            Action<EmbodimentProfileReceiverRegistration> handler = Registered;
            if (handler == null) return;

            try
            {
                handler.Invoke(registration);
            }
            catch (Exception ex)
            {
                const string message =
                    "[EmbodimentContext] A subscriber threw while handling ProfileReceiverRegistered.";
                ILogger logger = _ownerContext?.Logger;
                if (logger != null)
                    logger.Error(ex, message, LogCategory.Character);
                else
                    ConvaiLogger.Exception(ex, LogCategory.Character);
            }
        }
    }
}
