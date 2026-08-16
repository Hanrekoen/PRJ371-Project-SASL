namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Source of normalized speech energy for embodiment modules.
    /// </summary>
    internal interface ISpeechEnergyProvider
    {
        float Current { get; }
        void Sample(float deltaTime);
    }

    internal interface IConfigurableSpeechEnergyProvider : ISpeechEnergyProvider
    {
        void Configure(float windowSeconds);
    }
}
