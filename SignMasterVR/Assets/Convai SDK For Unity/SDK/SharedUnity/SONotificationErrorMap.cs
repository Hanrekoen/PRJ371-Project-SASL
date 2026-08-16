using System;
using System.Collections.Generic;
using Convai.Runtime.Presentation.Views.Notifications;
using UnityEngine;

namespace Convai.Shared.Types
{
    public enum SessionErrorMatchType
    {
        Exact,
        Prefix
    }

    [Serializable]
    public sealed class SessionErrorNotificationRule
    {
        [Tooltip("Exact session error code or prefix to match.")]
        public string ErrorPattern;

        [Tooltip("Whether ErrorPattern is matched exactly or as a prefix.")]
        public SessionErrorMatchType MatchType = SessionErrorMatchType.Exact;

        [Tooltip("Notification to request when this rule matches.")]
        public SONotification Notification;

        public bool Matches(string errorCode)
        {
            if (Notification == null || string.IsNullOrWhiteSpace(ErrorPattern) || string.IsNullOrWhiteSpace(errorCode))
                return false;

            return MatchType switch
            {
                SessionErrorMatchType.Exact => string.Equals(errorCode, ErrorPattern,
                    StringComparison.OrdinalIgnoreCase),
                SessionErrorMatchType.Prefix => errorCode.StartsWith(ErrorPattern, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }
    }

    /// <summary>
    ///     Maps session error codes to <see cref="SONotification" /> assets using ordered, data-driven rules.
    ///     Place a default instance at Resources/SONotificationErrorMap for runtime resolution.
    /// </summary>
    [CreateAssetMenu(menuName = "Convai/Notification System/Session Error Map", fileName = "SONotificationErrorMap")]
    public sealed class SONotificationErrorMap : ScriptableObject
    {
        [Header("Ordered rules used for session error mapping")]
        [Tooltip("Rules are evaluated top-to-bottom. The first matching rule wins.")]
        public List<SessionErrorNotificationRule> Rules = new();

        /// <summary>
        ///     Loads the package default from Resources when present.
        /// </summary>
        public static SONotificationErrorMap LoadDefault() =>
            Resources.Load<SONotificationErrorMap>(nameof(SONotificationErrorMap));

        public bool TryResolve(string errorCode, out SONotification notification)
        {
            notification = null;
            if (string.IsNullOrWhiteSpace(errorCode) || Rules == null) return false;

            foreach (SessionErrorNotificationRule rule in Rules)
            {
                if (rule == null || !rule.Matches(errorCode)) continue;

                notification = rule.Notification;
                return true;
            }

            return false;
        }
    }
}
