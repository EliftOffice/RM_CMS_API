using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using RM_CMS.DAL.Auth;

namespace RM_CMS.Security
{
    /// <summary>
    /// Re-checks each presented access token against the account's current state.
    ///
    /// A JWT is self-contained, so on its own it stays valid until it expires — even after
    /// a logout, password change, role change or account disable. This closes that window:
    /// every request re-reads the user's security stamp and token version and rejects the
    /// token if either has moved on.
    ///
    /// The lookup is cached briefly so this costs at most one small query per user per
    /// few seconds, not one per request.
    /// </summary>
    public sealed class AccessTokenStampValidator
    {
        private readonly IAuthUsersDAL _users;
        private readonly IMemoryStampCache _cache;
        private readonly ILogger<AccessTokenStampValidator> _logger;

        public AccessTokenStampValidator(
            IAuthUsersDAL users,
            IMemoryStampCache cache,
            ILogger<AccessTokenStampValidator> logger)
        {
            _users = users;
            _cache = cache;
            _logger = logger;
        }

        public async Task ValidateAsync(TokenValidatedContext context)
        {
            var principal = context.Principal;

            var userId = principal?.FindFirst(AppClaimTypes.UserId)?.Value;
            var stamp = principal?.FindFirst(AppClaimTypes.SecurityStamp)?.Value;
            var tokenVersionClaim = principal?.FindFirst(AppClaimTypes.TokenVersion)?.Value;

            if (string.IsNullOrWhiteSpace(userId) ||
                string.IsNullOrWhiteSpace(stamp) ||
                !int.TryParse(tokenVersionClaim, out var tokenVersion))
            {
                context.Fail("Token is missing required identity claims.");
                return;
            }

            var snapshot = await _cache.GetAsync(userId, async () =>
            {
                var user = await _users.GetByIdAsync(userId);

                return user is null
                    ? null
                    : new UserSecuritySnapshot(user.SecurityStamp, user.TokenVersion, user.IsActive);
            });

            if (snapshot is null)
            {
                _logger.LogWarning("Access token presented for unknown user {UserId}", userId);
                context.Fail("Account no longer exists.");
                return;
            }

            if (!snapshot.IsActive)
            {
                _logger.LogWarning("Access token presented for disabled account {UserId}", userId);
                context.Fail("Account is disabled.");
                return;
            }

            if (!string.Equals(snapshot.SecurityStamp, stamp, StringComparison.Ordinal) ||
                snapshot.TokenVersion != tokenVersion)
            {
                // Password change, role change, disable or forced logout happened after this
                // token was issued.
                _logger.LogInformation(
                    "Rejecting stale access token for {UserId} (security stamp or token version changed)", userId);

                context.Fail("Token is no longer valid. Please sign in again.");
            }
        }
    }

    public sealed record UserSecuritySnapshot(string SecurityStamp, int TokenVersion, bool IsActive);

    /// <summary>
    /// Very short-lived per-user cache for the security stamp check.
    ///
    /// The TTL is the maximum time a revoked token can still be accepted, so it is kept
    /// small (seconds). Set it to zero to disable caching and hit the database every request.
    /// </summary>
    public interface IMemoryStampCache
    {
        Task<UserSecuritySnapshot?> GetAsync(string userId, Func<Task<UserSecuritySnapshot?>> factory);
        void Invalidate(string userId);
    }

    public sealed class MemoryStampCache : IMemoryStampCache
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
        private readonly TimeSpan _ttl;
        private readonly TimeProvider _clock;

        public MemoryStampCache(IOptions<SecurityStampCacheOptions> options, TimeProvider clock)
        {
            _ttl = TimeSpan.FromSeconds(Math.Clamp(options.Value.TtlSeconds, 0, 60));
            _clock = clock;
        }

        public async Task<UserSecuritySnapshot?> GetAsync(string userId, Func<Task<UserSecuritySnapshot?>> factory)
        {
            var now = _clock.GetUtcNow();

            if (_ttl > TimeSpan.Zero &&
                _entries.TryGetValue(userId, out var cached) &&
                cached.ExpiresAt > now)
            {
                return cached.Snapshot;
            }

            var snapshot = await factory();

            if (_ttl > TimeSpan.Zero)
                _entries[userId] = new CacheEntry(snapshot, now.Add(_ttl));

            // Opportunistic sweep so the dictionary cannot grow without bound.
            if (_entries.Count > 5000)
            {
                foreach (var stale in _entries.Where(e => e.Value.ExpiresAt <= now).Select(e => e.Key).Take(1000))
                    _entries.TryRemove(stale, out _);
            }

            return snapshot;
        }

        public void Invalidate(string userId) => _entries.TryRemove(userId, out _);

        private sealed record CacheEntry(UserSecuritySnapshot? Snapshot, DateTimeOffset ExpiresAt);
    }

    public sealed class SecurityStampCacheOptions
    {
        public const string SectionName = "Auth:SecurityStampCache";

        /// <summary>
        /// Seconds to cache a user's security stamp. 0 disables caching (strictest, one
        /// query per request). Default 10 — a revoked token dies within ten seconds.
        /// </summary>
        public int TtlSeconds { get; set; } = 10;
    }
}
