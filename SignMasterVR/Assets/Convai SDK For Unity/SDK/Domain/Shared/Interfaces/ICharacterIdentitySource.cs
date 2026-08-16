namespace Convai.Shared.Interfaces
{
    /// <summary>
    ///     Provides the authoritative character identity for character-scoped components.
    /// </summary>
    public interface ICharacterIdentitySource
    {
        /// <summary>
        ///     Stable character identifier used across runtime events.
        /// </summary>
        public string CharacterId { get; }
    }
}
