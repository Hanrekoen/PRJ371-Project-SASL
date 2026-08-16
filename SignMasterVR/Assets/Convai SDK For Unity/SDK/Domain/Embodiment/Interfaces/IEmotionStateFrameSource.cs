using Convai.Domain.Embodiment.Readings;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    /// Optional high-performance companion to <see cref="IEmotionStateSource"/>. SDK embodiment
    /// consumers prefer this borrowed frame when available and fall back to the retained
    /// snapshot for third-party sources that do not implement this interface.
    /// </summary>
    internal interface IEmotionStateFrameSource
    {
        EmotionStateFrame CurrentFrame { get; }
    }
}
