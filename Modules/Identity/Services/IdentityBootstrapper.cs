using Dapper;
using Microsoft.Extensions.Options;
using RM_CMS.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Security;

namespace RM_CMS.Modules.Identity.Services
{
    /// <summary>
    /// Creates the first administrator on a fresh database, so that someone can sign
    /// in and create everyone else.
    ///
    /// Deliberately separate from <see cref="IdentityService"/>: this runs once at
    /// startup and is the only place allowed to create a <c>person</c> as a side
    /// effect of making an account. Every other path attaches an account to a person
    /// that already exists.
    ///
    /// Idempotent — it does nothing once an active administrator exists.
    /// </summary>
    public interface IIdentityBootstrapper
    {
        Task EnsureAdministratorAsync(CancellationToken cancellationToken = default);
    }

    public sealed class IdentityBootstrapper : IIdentityBootstrapper
    {
        private readonly IDbConnectionFactory _dbFactory;
        private readonly IPasswordService _passwords;
        private readonly AuthOptions _options;
        private readonly ILogger<IdentityBootstrapper> _logger;

        public IdentityBootstrapper(
            IDbConnectionFactory dbFactory,
            IPasswordService passwords,
            IOptions<AuthOptions> options,
            ILogger<IdentityBootstrapper> logger)
        {
            _dbFactory = dbFactory;
            _passwords = passwords;
            _options = options.Value;
            _logger = logger;
        }

        public async Task EnsureAdministratorAsync(CancellationToken cancellationToken = default)
        {
            if (!_options.Bootstrap.Enabled) return;

            try
            {
                using var connection = _dbFactory.GetConnection();

                var adminExists = await connection.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1)
                    FROM user_role ur
                    JOIN user_account ua ON ua.id = ur.user_account_id
                    WHERE ur.role_code = 'ADMIN' AND ua.is_active = 1;");

                if (adminExists > 0)
                {
                    _logger.LogInformation("Bootstrap skipped: an active administrator already exists.");
                    return;
                }

                var username = _options.Bootstrap.Username.Trim();
                var normalized = username.ToUpperInvariant();

                var taken = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM user_account WHERE normalized_username = @Normalized;",
                    new { Normalized = normalized });

                if (taken > 0)
                {
                    _logger.LogWarning(
                        "Bootstrap skipped: username already exists but holds no active Admin role. " +
                        "Grant the ADMIN role manually.");
                    return;
                }

                // Validate the password BEFORE writing anything. A bootstrap admin with
                // a weak password is worse than no bootstrap admin.
                var account = new UserAccount { Username = username, GivenName = "System" };
                var violations = _passwords.Validate(_options.Bootstrap.Password, account);

                if (violations.Count > 0)
                {
                    _logger.LogError(
                        "Bootstrap administrator NOT created: the supplied password fails policy. {Violations}",
                        string.Join(" ", violations));
                    return;
                }

                var campusId = await connection.ExecuteScalarAsync<long?>(
                    "SELECT id FROM campus WHERE is_active = 1 ORDER BY id LIMIT 1;");

                if (campusId is null)
                {
                    _logger.LogError(
                        "Bootstrap administrator NOT created: no active campus exists. " +
                        "Apply Database/Schema/schema.sql, which seeds one.");
                    return;
                }

                var displayName = string.IsNullOrWhiteSpace(_options.Bootstrap.DisplayName)
                    ? "System Administrator"
                    : _options.Bootstrap.DisplayName.Trim();

                var nameParts = displayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

                connection.Open();
                using var transaction = connection.BeginTransaction();

                try
                {
                    var personId = await connection.ExecuteScalarAsync<long>(@"
                        INSERT INTO person (public_id, campus_id, given_name, family_name, created_by)
                        VALUES (@PublicId, @CampusId, @GivenName, @FamilyName, NULL);
                        SELECT LAST_INSERT_ID();",
                        new
                        {
                            PublicId = Ulid.NewUlid(),
                            CampusId = campusId,
                            GivenName = nameParts.Length > 0 ? nameParts[0] : "System",
                            FamilyName = nameParts.Length > 1 ? nameParts[1] : null
                        }, transaction);

                    account.PersonId = personId;
                    var passwordHash = _passwords.Hash(account, _options.Bootstrap.Password);

                    var accountId = await connection.ExecuteScalarAsync<long>(@"
                        INSERT INTO user_account
                            (public_id, person_id, username, normalized_username,
                             password_hash, security_stamp, token_version,
                             is_active, must_change_password, failed_access_count)
                        VALUES
                            (@PublicId, @PersonId, @Username, @Normalized,
                             @PasswordHash, @SecurityStamp, 1,
                             1, 1, 0);
                        SELECT LAST_INSERT_ID();",
                        new
                        {
                            PublicId = Ulid.NewUlid(),
                            PersonId = personId,
                            Username = username,
                            Normalized = normalized,
                            PasswordHash = passwordHash,
                            SecurityStamp = Ulid.NewUlid()
                        }, transaction);

                    await connection.ExecuteAsync(@"
                        INSERT INTO user_role (user_account_id, role_code, campus_id, granted_by)
                        VALUES (@AccountId, 'ADMIN', NULL, NULL);",
                        new { AccountId = accountId }, transaction);

                    // Seed password history, otherwise the bootstrap password sits outside
                    // the reuse check and the administrator can cycle straight back to it.
                    await connection.ExecuteAsync(@"
                        INSERT INTO password_history (user_account_id, password_hash)
                        VALUES (@AccountId, @PasswordHash);",
                        new { AccountId = accountId, PasswordHash = passwordHash }, transaction);

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }

                _logger.LogWarning(
                    "Bootstrap administrator '{Username}' created and must change password at first sign-in. " +
                    "Disable Auth:Bootstrap:Enabled and clear the password environment variable now.",
                    username);
            }
            catch (Exception ex)
            {
                // Never prevent the application from starting.
                _logger.LogError(ex, "Bootstrap administrator creation failed.");
            }
        }
    }
}
