using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RM_CMS.Data.Models.Auth;

namespace RM_CMS.Security
{
    public interface IPasswordPolicy
    {
        /// <summary>Returns every rule the candidate password violates. Empty == acceptable.</summary>
        IReadOnlyList<string> Validate(string? password, AuthUser? user = null);
    }

    /// <summary>
    /// OWASP ASVS v4 §2.1 password rules, plus a small contextual check so a user
    /// cannot set their own phone number or display name as their password.
    /// </summary>
    public sealed class PasswordPolicy : IPasswordPolicy
    {
        private readonly AuthOptions _options;

        /// <summary>
        /// Passwords that satisfy the character rules but are still trivially guessable.
        /// A full breached-password check (k-anonymity against HaveIBeenPwned) is the
        /// recommended upgrade — see the security notes in the PR description.
        /// </summary>
        private static readonly HashSet<string> ObviousPasswords = new(StringComparer.OrdinalIgnoreCase)
        {
            "Password@123", "Password@1234", "Passw0rd@123", "Admin@123456",
            "Welcome@1234", "Qwerty@123456", "Church@123456", "Rmoffice@123",
            "Volunteer@123", "TeamLead@123", "P@ssw0rd1234", "Abcd@12345678"
        };

        public PasswordPolicy(IOptions<AuthOptions> options) => _options = options.Value;

        public IReadOnlyList<string> Validate(string? password, AuthUser? user = null)
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

            if (user is not null)
            {
                if (ContainsCaseInsensitive(password, user.Username) ||
                    ContainsCaseInsensitive(password, user.MobileNumber) ||
                    ContainsCaseInsensitive(password, user.Email))
                {
                    errors.Add("Password must not contain your username, mobile number or email address.");
                }

                foreach (var part in user.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.Length >= 4 && ContainsCaseInsensitive(password, part))
                    {
                        errors.Add("Password must not contain your name.");
                        break;
                    }
                }
            }

            return errors;
        }

        private static bool ContainsCaseInsensitive(string haystack, string? needle) =>
            !string.IsNullOrWhiteSpace(needle) &&
            needle.Length >= 4 &&
            haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Thin wrapper over ASP.NET Identity's <see cref="PasswordHasher{TUser}"/>.
    /// That is PBKDF2-HMAC-SHA512, 210,000 iterations, 128-bit salt (IdentityV3 format)
    /// with a constant-time comparison — the .NET 8 default, and OWASP-compliant.
    ///
    /// Using it directly (package Microsoft.Extensions.Identity.Core) means we get the
    /// hardened implementation without pulling in Entity Framework or Identity stores.
    /// </summary>
    public interface IPasswordHashingService
    {
        string Hash(AuthUser user, string password);

        /// <summary>
        /// Verifies a password. <paramref name="needsRehash"/> is true when the stored hash
        /// used an older/weaker format and should be upgraded on next successful login.
        /// </summary>
        bool Verify(AuthUser user, string hashedPassword, string providedPassword, out bool needsRehash);
    }

    public sealed class PasswordHashingService : IPasswordHashingService
    {
        private readonly PasswordHasher<AuthUser> _hasher;

        public PasswordHashingService(IOptions<PasswordHasherOptions> options) =>
            _hasher = new PasswordHasher<AuthUser>(options);

        public string Hash(AuthUser user, string password) =>
            _hasher.HashPassword(user, password);

        public bool Verify(AuthUser user, string hashedPassword, string providedPassword, out bool needsRehash)
        {
            needsRehash = false;

            if (string.IsNullOrEmpty(hashedPassword) || string.IsNullOrEmpty(providedPassword))
                return false;

            PasswordVerificationResult result;
            try
            {
                result = _hasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
            }
            catch (FormatException)
            {
                // Corrupt or non-Identity hash in the column — treat as a failed login,
                // never as a success.
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
    }

    /// <summary>
    /// Generates a policy-compliant random password for admin-issued credentials,
    /// so an administrator never has to invent one (and never reuses one).
    /// </summary>
    public static class PasswordGenerator
    {
        private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // no I/O
        private const string Lower = "abcdefghijkmnopqrstuvwxyz";  // no l
        private const string Digits = "23456789";                  // no 0/1
        private const string Special = "!@#$%^&*()-_=+[]{}?";

        public static string Generate(int length = 16)
        {
            if (length < 12) length = 12;

            var all = Upper + Lower + Digits + Special;
            var chars = new List<char>(length)
            {
                Pick(Upper), Pick(Lower), Pick(Digits), Pick(Special)
            };

            while (chars.Count < length)
                chars.Add(Pick(all));

            // Fisher-Yates with a CSPRNG so the guaranteed characters are not positionally
            // predictable.
            for (var i = chars.Count - 1; i > 0; i--)
            {
                var j = System.Security.Cryptography.RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            return new string(chars.ToArray());
        }

        private static char Pick(string source) =>
            source[System.Security.Cryptography.RandomNumberGenerator.GetInt32(source.Length)];
    }
}
