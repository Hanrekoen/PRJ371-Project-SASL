namespace Convai.Shared.Types
{
    /// <summary>
    ///     Microphone device descriptor.
    /// </summary>
    public readonly struct ConvaiMicrophoneDevice
    {
        /// <summary>Gets the unique device identifier.</summary>
        public string Id { get; }

        /// <summary>Gets the human-readable device name.</summary>
        public string Name { get; }

        /// <summary>Gets the zero-based device index.</summary>
        public int Index { get; }

        /// <summary>Gets whether this device descriptor refers to a valid device.</summary>
        public bool IsValid => !string.IsNullOrEmpty(Id) && Index >= 0;

        /// <summary>Creates a new microphone device descriptor.</summary>
        public ConvaiMicrophoneDevice(string id, string name, int index)
        {
            Id = id;
            Name = name ?? string.Empty;
            Index = index;
        }

        /// <summary>Gets a sentinel value representing no microphone device.</summary>
        public static ConvaiMicrophoneDevice None => new(string.Empty, string.Empty, -1);
    }
}
