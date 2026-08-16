namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Null-object speech energy source used when no live provider exists.
    /// </summary>
    public sealed class NullSpeechEnergyProvider : ISpeechEnergyProvider
    {
        public static readonly NullSpeechEnergyProvider Instance = new();

        private NullSpeechEnergyProvider() { }

        public float Current => 0f;
        public void Sample(float deltaTime) { }
    }
}
