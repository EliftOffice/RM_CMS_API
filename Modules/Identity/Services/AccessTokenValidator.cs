using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;

namespace RM_CMS.Modules.Identity.Services
{
    /// <summary>
    /// Re-checks every presented access token against the account's current state.
    ///
    /// A JWT is self-contained, so on its own it stays valid until it expires — even
    /// after a logout, password change, role change or account disable. This closes
    /// that window: each request re-reads the account's security stamp and token
    /// version and rejects the token if either has moved on.
    ///
    /// The lookup is cached for a few seconds, so this costs at most one small query
    /// per account per TTL rather than one per request.
    /// </summary>
    public sealed class AccessTokenValidator
    {
        private readonly IUserAccountRepository _accounts;
        private readonly IAccountStateCache _cache;
        private readonly ILogger<AccessTokenValidator> _logger;

        public AccessTokenValidator(
            IUserAccountRepository accounts,
            IAccountStateCache cache,
            ILogger<AccessTokenValidator> logger)
        {
            _accounts = accounts;
            _cache = cache;
            _logger = logger;
        }

        public async Task ValidateAsync(TokenValidatedContext context)
        {
            var principal = context.Principal;

            var accountId = principal?.FindFirst(ClaimNames.Subject)?.Value;
            var stamp = principal?.FindFirst(ClaimNames.SecurityStamp)?.Value;
            var versionClaim = principal?.FindFirst(ClaimNames.TokenVersion)?.Value;

            if (string.IsNullOrWhiteSpace(accountId) ||
                string.IsNullOrWhiteSpace(stamp) ||
                !int.TryParse(versionClaim, out var tokenVersion))
            {
                context.Fail("Token is missing required identity claims.");
                return;
            }

            var snapshot = await _cache.GetAsync(accountId, async () =>
            {
                var account = await _accounts.GetByPublicIdAsync(accountId);

                return account is null
                    ? null
                    : new AccountState(account.SecurityStamp, account.TokenVersion, account.IsActive);
            });

            if (snapshot is null)
            {
                _logger.LogWarning("Access token presented for unknown account {AccountId}", accountId);
                context.Fail("Account no longer exists.");
                return;
            }

            if (!snapshot.IsActive)
            {
                _logger.LogWarning("Access token presented for disabled account {AccountId}", accountId);
                context.Fail("Account is disabled.");
                return;
            }

            if (!string.Equals(snapshot.SecurityStamp, stamp, StringComparison.Ordinal) ||
                snapshot.TokenVersion != tokenVersion)
            {
                // A password change, role change, disable or forced logout happened
                // after this token was issued.
                _logger.LogInformation(
                    "Rejecting stale access token for {AccountId} (security stamp or token version changed)", accountId);

                context.Fail("Token is no longer valid. Please sign in again.");
            }
        }
    }

    public sealed record AccountState(string SecurityStamp, int TokenVersion, bool IsActive);

    /// <summary>
    /// Very short-lived per-account cache for the revocation check.
    ///
    /// The TTL is the maximum time a revoked token can still be accepted, so it is
    /// kept to seconds. Set it to zero to check the database on every request.
    /// </summary>
    public interface IAccountStateCache
    {
        Task<AccountState?> GetAsync(string accountId, Func<Task<AccountState?>> factory);
        void Invalidate(string accountId);
    }

    public sealed class AccountStateCache : IAccountStateCache
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
        private readonly TimeSpan _ttl;
        private readonly TimeProvider _clock;

        public AccountStateCache(IOptions<AccountStateCacheOptions> options, TimeProvider clock)
        {
            _ttl = TimeSpan.FromSeconds(Math.Clamp(options.Value.TtlSeconds, 0, 60));
            _clock = clock;
        }

        public async Task<AccountState?> GetAsync(string accountId, Func<Task<AccountState?>> factory)
        {
            var now = _clock.GetUtcNow();

            if (_ttl > TimeSpan.Zero &&
                _entries.TryGetValue(accountId, out var cached) &&
                cached.ExpiresAt > now)
            {
                return cached.State;
            }

            var state = await factory();

            if (_ttl > TimeSpan.Zero)
                _entries[accountId] = new CacheEntry(state, now.Add(_ttl));

            // Opportunistic sweep so the dictionary cannot grow without bound.
            if (_entries.Count > 5000)
            {
                foreach (var stale in _entries.Where(e => e.Value.ExpiresAt <= now).Select(e => e.Key).Take(1000))
                    _entries.TryRemove(stale, out _);
            }

            return state;
        }

        public void Invalidate(string accountId) => _entries.TryRemove(accountId, out _);

        private sealed record CacheEntry(AccountState? State, DateTimeOffset ExpiresAt);
    }

    public sealed class AccountStateCacheOptions
    {
        public const string SectionName = "Auth:SecurityStampCache";

        /// <summary>
        /// Seconds to cache an account's revocation state. 0 disables caching
        /// (strictest, one query per request). Default 10 — a revoked token dies
        /// within ten seconds.
        /// </summary>
        public int TtlSeconds { get; set; } = 10;
    }
}
