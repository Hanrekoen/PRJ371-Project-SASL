using System;
using UnityEngine;

namespace Convai.Runtime.Core.Configuration
{
    /// <summary>Authentication strategy used for runtime Convai room connections.</summary>
    public enum ConvaiAuthMode
    {
        /// <summary>Read the account API key from <see cref="ConvaiSettings" />.</summary>
        ApiKey = 0,

        /// <summary>Resolve a short-lived auth token from a developer-controlled provider or endpoint.</summary>
        AuthToken = 1
    }

    /// <summary>HTTP methods supported by the configured auth-token endpoint.</summary>
    public enum ConvaiAuthTokenHttpMethod
    {
        /// <summary>Send an HTTP GET request without a body.</summary>
        Get = 0,

        /// <summary>Send an HTTP POST request with an empty JSON object body.</summary>
        Post = 1
    }

    /// <summary>A Unity-serializable header name/value pair for auth-token endpoint requests.</summary>
    [Serializable]
    public struct ConvaiAuthTokenHeader
    {
        [SerializeField] [Tooltip("HTTP request header name.")]
        private string _name;

        [SerializeField] [Tooltip("HTTP request header value.")]
        private string _value;

        /// <summary>Creates an auth-token endpoint header.</summary>
        /// <param name="name">HTTP header name.</param>
        /// <param name="value">HTTP header value.</param>
        public ConvaiAuthTokenHeader(string name, string value)
        {
            _name = name ?? string.Empty;
            _value = value ?? string.Empty;
        }

        /// <summary>HTTP header name.</summary>
        public string Name => _name ?? string.Empty;

        /// <summary>HTTP header value.</summary>
        public string Value => _value ?? string.Empty;
    }
}
