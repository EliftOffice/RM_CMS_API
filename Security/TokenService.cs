using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RM_CMS.Data.Models.Auth;

namespace RM_CMS.Security
{
    public readonly record struct AccessToken(string Value, string TokenId, DateTime ExpiresUtc);

    /// <summary>A freshly minted refresh token: the raw value goes to the client, the hash to the DB.</summary>
    public readonly record struct RefreshTokenMaterial(string RawValue, string Hash, DateTime ExpiresUtc);

    public interface ITokenService
    {
        AccessToken CreateAccessToken(AuthUser user);
        RefreshTokenMaterial CreateRefreshToken();

        /// <summary>Deterministic SHA-256 hex hash, used to look a presented token up by hash.</summary>
        string HashRefreshToken(string rawToken);

        /// <summary>Non-reversible, non-identifying fingerprint of a user agent for device correlation.</summary>
        string? HashUserAgent(string? userAgent);

        TokenValidationParameters BuildValidationParameters();
    }

    public sealed class TokenService : ITokenService
    {
        /// <summary>64 bytes = 512 bits of entropy, well above the 64-byte requirement floor.</summary>
        private const int RefreshTokenBytes = 64;

        private readonly JwtOptions _jwt;
        private readonly TimeProvider _clock;

        public TokenService(IOptions<JwtOptions> jwt, TimeProvider clock)
        {
            _jwt = jwt.Value;
            _clock = clock;
        }

        public AccessToken CreateAccessToken(AuthUser user)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

            // Cryptographically random JWT ID — not a Guid, which is only 122 bits and
            // (for v4) not guaranteed to come from a CSPRNG on every platform.
            var jti = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16));

            var claims = new List<Claim>
            {
                new(AppClaimTypes.UserId, user.UserId),
                new(AppClaimTypes.TokenId, jti),
                new(AppClaimTypes.DisplayName, user.DisplayName),
                new(AppClaimTypes.SecurityStamp, user.SecurityStamp),
                new(AppClaimTypes.TokenVersion, user.TokenVersion.ToString()),
                new(JwtRegisteredClaimNames.Iat,
                    new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64)
            };

            foreach (var role in user.Roles.Where(Roles.IsKnown).Distinct(StringComparer.Ordinal))
                claims.Add(new Claim(AppClaimTypes.Role, role));

            if (!string.IsNullOrWhiteSpace(user.VolunteerId))
                claims.Add(new Claim(AppClaimTypes.VolunteerId, user.VolunteerId));

            if (!string.IsNullOrWhiteSpace(user.TeamLeadId))
                claims.Add(new Claim(AppClaimTypes.TeamLeadId, user.TeamLeadId));

            if (user.MustChangePassword)
                claims.Add(new Claim(AppClaimTypes.MustChangePassword, "1"));

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

        public RefreshTokenMaterial CreateRefreshToken()
        {
            var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
            var expires = _clock.GetUtcNow().UtcDateTime.AddDays(_jwt.RefreshTokenDays);
            return new RefreshTokenMaterial(raw, HashRefreshToken(raw), expires);
        }

        public string HashRefreshToken(string rawToken)
        {
            // Plain SHA-256 is correct here (unlike for passwords): the token already has
            // 512 bits of entropy, so it is not brute-forceable and needs no work factor.
            // A fixed hash also lets us look the row up by hash in a single indexed query.
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public string? HashUserAgent(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent)) return null;
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(userAgent));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public TokenValidationParameters BuildValidationParameters()
        {
            // Current key first, then any rotation-window predecessors. Old keys validate
            // but never sign, so a rotation can complete without logging everyone out.
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

                // Pin the algorithm: without this a token could claim "alg": "none"
                // or a different family and be accepted.
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },

                NameClaimType = AppClaimTypes.DisplayName,
                RoleClaimType = AppClaimTypes.Role
            };
        }
    }
}
