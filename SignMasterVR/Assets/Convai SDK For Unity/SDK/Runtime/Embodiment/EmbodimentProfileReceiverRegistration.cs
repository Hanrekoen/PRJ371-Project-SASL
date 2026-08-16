using UnityEngine;

namespace Convai.Runtime.Embodiment
{
    internal readonly struct EmbodimentProfileReceiverRegistration
    {
        public EmbodimentProfileReceiverRegistration(IEmbodimentProfileReceiver receiver, Component owner)
        {
            Receiver = receiver;
            Owner = owner;
        }

        public IEmbodimentProfileReceiver Receiver { get; }
        public Component Owner { get; }
        public string ModuleId => Receiver != null ? Receiver.ModuleId : string.Empty;
        public bool IsValid => Receiver != null;

        public bool Matches(IEmbodimentProfileReceiver receiver) =>
            ReferenceEquals(Receiver, receiver);
    }
}
