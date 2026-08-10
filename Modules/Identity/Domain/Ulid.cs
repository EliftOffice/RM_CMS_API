using System.Security.Cryptography;

namespace RM_CMS.Modules.Identity.Domain
{
    /// <summary>
    /// ULID generator — the public identifier for every entity the API exposes.
    ///
    /// 26 characters: a 48-bit millisecond timestamp followed by 80 bits of CSPRNG
    /// randomness, encoded in Crockford base32.
    ///
    /// Why not a GUID:
    ///   * A ULID sorts lexicographically by creation time, so it indexes well and
    ///     an ORDER BY public_id is chronological.
    ///   * It is URL-safe with no hyphens or casing ambiguity.
    ///   * MySqlConnector silently converts CHAR(36) to System.Guid, which Dapper
    ///     then cannot map onto a string property ("Object must implement
    ///     IConvertible"). CHAR(26) has no such behaviour. This bit us on the MVP.
    ///
    /// Why not the database id: internal ids are sequential, so exposing them lets
    /// any authenticated caller walk the entire dataset by incrementing a number.
    /// </summary>
    public static class Ulid
    {
        // Crockford base32: no I, L, O or U, so a ULID cannot be misread aloud
        // or mistyped into an ambiguous value.
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        private const int TimestampChars = 10;
        private const int RandomChars = 16;
        public const int Length = TimestampChars + RandomChars; // 26

        /// <summary>Generates a new ULID for the current instant.</summary>
        public static string NewUlid() => NewUlid(DateTimeOffset.UtcNow);

        /// <summary>Generates a ULID for a specific instant. Exposed for deterministic tests.</summary>
        public static string NewUlid(DateTimeOffset timestamp)
        {
            Span<char> buffer = stackalloc char[Length];

            EncodeTimestamp(timestamp.ToUnixTimeMilliseconds(), buffer[..TimestampChars]);
            EncodeRandom(buffer.Slice(TimestampChars, RandomChars));

            return new string(buffer);
        }

        /// <summary>
        /// True when the value is a syntactically valid ULID. Use before hitting the
        /// database so a malformed route parameter costs nothing.
        /// </summary>
        public static bool IsValid(string? value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != Length)
                return false;

            foreach (var c in value)
            {
                if (Alphabet.IndexOf(char.ToUpperInvariant(c)) < 0)
                    return false;
            }

            return true;
        }

        private static void EncodeTimestamp(long milliseconds, Span<char> destination)
        {
            // 48 bits across 10 base32 characters, most significant first.
            for (var i = destination.Length - 1; i >= 0; i--)
            {
                destination[i] = Alphabet[(int)(milliseconds & 31)];
                milliseconds >>= 5;
            }
        }

        private static void EncodeRandom(Span<char> destination)
        {
            Span<byte> entropy = stackalloc byte[destination.Length];
            RandomNumberGenerator.Fill(entropy);

            for (var i = 0; i < destination.Length; i++)
            {
                // Mask to 5 bits. Taking one byte per character wastes entropy but
                // keeps the mapping uniform — no modulo bias.
                destination[i] = Alphabet[entropy[i] & 31];
            }
        }
    }
}
