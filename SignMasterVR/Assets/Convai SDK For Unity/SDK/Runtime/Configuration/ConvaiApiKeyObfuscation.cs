using System;
using System.Text;

namespace Convai.Runtime
{
    /// <summary>
    ///     Obfuscates the API key stored in the ConvaiSettings asset.
    /// </summary>
    /// <remarks>
    ///     This is obfuscation, not encryption. It keeps the key from being trivially
    ///     greppable in the serialized asset (and in version control), but anyone with
    ///     the SDK source can reverse it. Any key shipped inside a client build is
    ///     ultimately extractable; use a runtime <c>ICredentialProvider</c> with
    ///     server-issued tokens when that matters.
    /// </remarks>
    internal static class ConvaiApiKeyObfuscation
    {
        internal const string Prefix = "cnv1:";

        private static readonly byte[] Key = Encoding.UTF8.GetBytes("Convai.Settings.ApiKey.Obfuscation.v1");

        /// <summary>Obfuscates a plaintext key. Returns an empty string for null/empty input.</summary>
        public static string Obfuscate(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;

            byte[] bytes = Encoding.UTF8.GetBytes(plain);
            Xor(bytes);
            return Prefix + Convert.ToBase64String(bytes);
        }

        /// <summary>
        ///     Attempts to deobfuscate a payload produced by <see cref="Obfuscate" />.
        ///     Returns false when the payload is empty, unprefixed, or malformed.
        /// </summary>
        public static bool TryDeobfuscate(string payload, out string plain)
        {
            plain = string.Empty;
            if (string.IsNullOrEmpty(payload) || !payload.StartsWith(Prefix, StringComparison.Ordinal))
                return false;

            try
            {
                byte[] bytes = Convert.FromBase64String(payload.Substring(Prefix.Length));
                Xor(bytes);
                plain = Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static void Xor(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] ^= Key[i % Key.Length];
        }
    }
}
