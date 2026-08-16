using System;

namespace Convai.Domain.EventSystem
{
    /// <summary>
    ///     Opaque handle for an event subscription. Used to unsubscribe from events.
    /// </summary>
    public readonly struct SubscriptionToken : IEquatable<SubscriptionToken>
    {
        private readonly Guid _id;

        /// <summary>
        ///     Creates a subscription token with the specified ID.
        /// </summary>
        /// <param name="id">Unique identifier for this subscription</param>
        public SubscriptionToken(Guid id)
        {
            _id = id;
        }

        /// <summary>
        ///     Creates a new subscription token with a unique ID.
        /// </summary>
        /// <returns>New subscription token</returns>
        public static SubscriptionToken Create() => new(Guid.NewGuid());

        /// <summary>
        ///     Checks if this token equals another token.
        /// </summary>
        public bool Equals(SubscriptionToken other) => _id.Equals(other._id);

        /// <summary>
        ///     Checks if this token equals another object.
        /// </summary>
        public override bool Equals(object obj) => obj is SubscriptionToken other && Equals(other);

        /// <summary>
        ///     Gets the hash code for this token.
        /// </summary>
        public override int GetHashCode() => _id.GetHashCode();

        /// <summary>
        ///     Checks if two tokens are equal.
        /// </summary>
        public static bool operator ==(SubscriptionToken left, SubscriptionToken right) => left.Equals(right);

        /// <summary>
        ///     Checks if two tokens are not equal.
        /// </summary>
        public static bool operator !=(SubscriptionToken left, SubscriptionToken right) => !left.Equals(right);

        /// <summary>
        ///     Returns a string representation of this token.
        /// </summary>
        public override string ToString() => $"SubscriptionToken({_id})";
    }
}
