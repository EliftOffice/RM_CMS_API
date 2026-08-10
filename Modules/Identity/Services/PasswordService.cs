using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Security;

namespace RM_CMS.Modules.Identity.Services
{
    /// <summary>
    /// Password policy, hashing and verification.
    ///
    /// Hashing uses ASP.NET Identity's <see cref="PasswordHasher{TUser}"/> directly —
    /// PBKDF2-HMAC-SHA512 with a 128-bit salt and a constant-time comparison. Taking
    /// it from <c>Microsoft.Extensions.Identity.Core</c> gives us the hardened
    /// implementation without pulling in Entity Framework or Identity's stores.
    /// </summary>
    public interface IPasswordService
    {
        /// <summary>Returns every rule the candidate violates. Empty means acceptable.</summary>
        IReadOnlyList<string> Validate(string? password, UserAccount? account = null);

        string Hash(UserAccount account, string password);

        /// <summary>
        /// Verifies a password. <paramref name="needsRehash"/> is true when the stored
        /// hash used an older format and should be upgraded on next successful login.
        /// </summary>
        bool Verify(UserAccount account, string hashedPassword, string providedPassword, out bool needsRehash);

        /// <summary>
        /// Burns the same CPU time as a real verification. Called when the account does
        /// not exist or has no usable password, so response timing cannot be used to
        /// discover which usernames are real.
        /// </summary>
        void BurnVerificationTime(string providedPassword);

        /// <summary>Generates a policy-compliant random password for admin-issued credentials.</summary>
        string Generate(int length = 16);
    }

    public sealed class PasswordService : IPasswordService
    {
        private readonly PasswordHasher<UserAccount> _hasher;
        private readonly AuthOptions _options;

        /// <summary>
        /// A well-formed PBKDF2 hash of a random value, used only for timing equalisation.
        /// </summary>
        private const string DummyHash =
            "AQAAAAIAAYagAAAAEJ8Zx0Qw3nQm4kZq8yQx1cVQY2bK5xW9d0mJ3sF7hT2nRp6vL8cA1eG4dH5jK9wS0g==";

        /// <summary>
        /// Passwords that pass the character rules but are still trivially guessable.
        /// A breached-password check against HaveIBeenPwned's k-anonymity range API is
        /// the proper upgrade; this is the floor, not the ceiling.
        /// </summary>
        private static readonly HashSet<string> ObviousPasswords = new(StringComparer.OrdinalIgnoreCase)
        {
            "Password@123", "Password@1234", "Passw0rd@123", "Admin@123456",
            "Welcome@1234", "Qwerty@123456", "Church@123456", "Rmoffice@123",
            "Volunteer@123", "TeamLead@123", "P@ssw0rd1234", "Abcd@12345678"
        };

        public PasswordService(IOptions<PasswordHasherOptions> hasherOptions, IOptions<AuthOptions> options)
        {
            _hasher = new PasswordHasher<UserAccount>(hasherOptions);
            _options = options.Value;
        }

        public IReadOnlyList<string> Validate(string? password, UserAccount? account = null)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(password))
            {
                errors.Add("Password is required.");
                return errors;
            }

            if (password.Length < _options.PasswordMinLength)
                errors.Add($"Password must be at least {_options.PasswordMinLength} characters long.");

            if (password.Length > _options.PasswordMaxLength)
                errors.Add($"Password must be at most {_options.PasswordMaxLength} characters long.");

            if (_options.RequireUppercase && !password.Any(char.IsUpper))
                errors.Add("Password must contain at least one uppercase letter.");

            if (_options.RequireLowercase && !password.Any(char.IsLower))
                errors.Add("Password must contain at least one lowercase letter.");

            if (_options.RequireDigit && !password.Any(char.IsDigit))
                errors.Add("Password must contain at least one number.");

            if (_options.RequireNonAlphanumeric && password.All(char.IsLetterOrDigit))
                errors.Add("Password must contain at least one special character.");

            if (password.Any(char.IsControl))
                errors.Add("Password must not contain control characters.");

            if (ObviousPasswords.Contains(password))
                errors.Add("This password is too common. Choose something less predictable.");

            if (account is not null)
            {
                // A password containing the account's own identifiers is guessable by
                // anyone who knows the person.
                if (Contains(password, account.Username) ||
                    Contains(password, account.GivenName) ||
                    Contains(password, account.FamilyName))
                {
                    errors.Add("Password must not contain your username or name.");
                }
            }

            return errors;
        }

        public string Hash(UserAccount account, string password) =>
            _hasher.HashPassword(account, password);

        public bool Verify(UserAccount account, string hashedPassword, string providedPassword, out bool needsRehash)
        {
            needsRehash = false;

            if (string.IsNullOrEmpty(hashedPassword) || string.IsNullOrEmpty(providedPassword))
                return false;

            PasswordVerificationResult result;
            try
            {
                result = _hasher.VerifyHashedPassword(account, hashedPassword, providedPassword);
            }
            catch (FormatException)
            {
                // A corrupt or non-Identity hash in the column is a failed login,
                // never a success.
                return false;
            }

            switch (result)
            {
                case PasswordVerificationResult.Success:
                    return true;
                case PasswordVerificationResult.SuccessRehashNeeded:
                    needsRehash = true;
                    return true;
                default:
                    return false;
            }
        }

        public void BurnVerificationTime(string providedPassword)
        {
            if (string.IsNullOrEmpty(providedPassword)) return;

            try
            {
                _hasher.VerifyHashedPassword(new UserAccount(), DummyHash, providedPassword);
            }
            catch (FormatException)
            {
                // Irrelevant — the point is the elapsed time, not the result.
            }
        }

        public string Generate(int length = 16)
        {
            // Ambiguous glyphs removed so a generated password can be read aloud or
            // copied from a screen without transcription errors.
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";  // no I, O
            const string lower = "abcdefghijkmnopqrstuvwxyz"; // no l
            const string digits = "23456789";                 // no 0, 1
            const string special = "!@#$%^&*()-_=+?";

            if (length < _options.PasswordMinLength)
                length = _options.PasswordMinLength;

            var all = upper + lower + digits + special;

            var chars = new List<char>(length)
            {
                Pick(upper), Pick(lower), Pick(digits), Pick(special)
            };

            while (chars.Count < length)
                chars.Add(Pick(all));

            // Fisher-Yates with a CSPRNG, so the guaranteed characters are not
            // positionally predictable.
            for (var i = chars.Count - 1; i > 0; i--)
            {
                var j = RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            return new string(chars.ToArray());
        }

        private static char Pick(string source) =>
            source[RandomNumberGenerator.GetInt32(source.Length)];

        private static bool Contains(string haystack, string? needle) =>
            !string.IsNullOrWhiteSpace(needle) &&
            needle.Length >= 4 &&
            haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
