using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Security;

namespace RM_CMS.Modules.Identity.Services
{
    public readonly record struct AccessToken(string Value, string TokenId, DateTime ExpiresAt);

    /// <summary>A new refresh token: the raw value goes to the client, the hash to the database.</summary>
    public readonly record struct RefreshTokenMaterial(string RawValue, string Hash, DateTime ExpiresAt);

    public interface ITokenIssuer
    {
        AccessToken CreateAccessToken(UserAccount account);
        RefreshTokenMaterial CreateRefreshToken();

        /// <summary>Deterministic SHA-256 hex, used to look a presented token up by hash.</summary>
        string HashRefreshToken(string rawToken);

        /// <summary>Non-identifying fingerprint of a user agent, for device correlation.</summary>
        string? HashUserAgent(string? userAgent);

        TokenValidationParameters BuildValidationParameters();
    }

    public sealed class TokenIssuer : ITokenIssuer
    {
        /// <summary>64 bytes = 512 bits of entropy.</summary>
        private const int RefreshTokenBytes = 64;

        private readonly JwtOptions _jwt;
        private readonly TimeProvider _clock;

        public TokenIssuer(IOptions<JwtOptions> jwt, TimeProvider clock)
        {
            _jwt = jwt.Value;
            _clock = clock;
        }

        public AccessToken CreateAccessToken(UserAccount account)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

            var jti = Ulid.NewUlid();

            // NOTE: every identifier here is a PUBLIC id. A token never carries an
            // internal database key, so a leaked token exposes no enumerable range.
            var claims = new List<Claim>
            {
                new(ClaimNames.Subject,       account.PublicId),
                new(ClaimNames.TokenId,       jti),
                new(ClaimNames.DisplayName,   account.FullName),
                new(ClaimNames.PersonId,      account.PersonPublicId),
                new(ClaimNames.SecurityStamp, account.SecurityStamp),
                new(ClaimNames.TokenVersion,  account.TokenVersion.ToString()),
                new(JwtRegisteredClaimNames.Iat,
                    new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64)
            };

            foreach (var role in account.RoleCodes.Where(RoleCodes.IsKnown).Distinct(StringComparer.Ordinal))
                claims.Add(new Claim(ClaimNames.Role, role));

            if (!string.IsNullOrWhiteSpace(account.VolunteerPublicId))
                claims.Add(new Claim(ClaimNames.VolunteerId, account.VolunteerPublicId));

            if (!string.IsNullOrWhiteSpace(account.TeamPublicId))
                claims.Add(new Claim(ClaimNames.TeamId, account.TeamPublicId));

            // The tenancy boundary. Absent means organisation-wide.
            //
            // This used to be the person's campus, unconditionally. That made an
            // administrator scoped to whichever site their own person record happened
            // to sit at — invisible while one campus existed, and the moment a second
            // was created they could create it and then not file anything against it.
            //
            // user_role.campus_id IS NULL already means "organisation-wide grant", so
            // the fix is to honour it. Deliberately limited to ADMIN and PASTOR: a
            // null campus on a volunteer or data-entry grant is far more likely to be
            // an oversight than an intention, and the cost of reading it as intent is
            // someone seeing another site's pastoral records.
            if (!IsOrganisationWide(account) && !string.IsNullOrWhiteSpace(account.CampusPublicId))
                claims.Add(new Claim(ClaimNames.CampusId, account.CampusPublicId));

            // Always present, even for an organisation-wide account. This is not a
            // permission — it is the campus their records default to when they do not
            // name one, which an org-wide caller still needs or every person they
            // record lands with no campus at all.
            if (!string.IsNullOrWhiteSpace(account.CampusPublicId))
                claims.Add(new Claim(ClaimNames.HomeCampusId, account.CampusPublicId));

            if (account.MustChangePassword)
                claims.Add(new Claim(ClaimNames.MustChangePassword, "1"));

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _jwt.Issuer,
                audience: _jwt.Audience,
                claims: claims,
                notBefore: now,
                expires: expires,
                signingCredentials: credentials);

            return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), jti, expires);
        }

        /// <summary>
        /// True when the account holds an ADMIN or PASTOR role granted with no campus
        /// — the shape <c>user_role.campus_id IS NULL</c> was designed to express.
        ///
        /// Only those two roles. Widening it to every role would turn a null campus on
        /// a volunteer grant, which is almost certainly a data oversight, into
        /// organisation-wide read access over other sites' pastoral records.
        /// </summary>
        private static bool IsOrganisationWide(UserAccount account) =>
            account.Roles.Any(r =>
                string.IsNullOrWhiteSpace(r.CampusPublicId) &&
                (string.Equals(r.RoleCode, RoleCodes.Admin, StringComparison.Ordinal) ||
                 string.Equals(r.RoleCode, RoleCodes.Pastor, StringComparison.Ordinal)));

        public RefreshTokenMaterial CreateRefreshToken()
        {
            var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
            var expires = _clock.GetUtcNow().UtcDateTime.AddDays(_jwt.RefreshTokenDays);
            return new RefreshTokenMaterial(raw, HashRefreshToken(raw), expires);
        }

        public string HashRefreshToken(string rawToken)
        {
            // Plain SHA-256 is correct here, unlike for passwords: the token already
            // carries 512 bits of entropy so it is not brute-forceable, and a fixed
            // hash lets us find the row with one indexed lookup.
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
        }

        public string? HashUserAgent(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent)) return null;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userAgent))).ToLowerInvariant();
        }

        public TokenValidationParameters BuildValidationParameters()
        {
            // Current key first, then rotation-window predecessors. Old keys validate
            // but never sign, so a key rotation does not log everyone out.
            var keys = new List<SecurityKey>
            {
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey))
            };

            keys.AddRange(_jwt.PreviousSigningKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(k))));

            return new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _jwt.Issuer,

                ValidateAudience = true,
                ValidAudience = _jwt.Audience,

                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = keys,

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(_jwt.ClockSkewSeconds),

                RequireExpirationTime = true,
                RequireSignedTokens = true,

                // Pin the algorithm. Without this a token could claim "alg":"none"
                // or a different family and be accepted.
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },

                NameClaimType = ClaimNames.DisplayName,
                RoleClaimType = ClaimNames.Role
            };
        }
    }
}
