using System.Data;
using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.People.Domain;

namespace RM_CMS.Modules.People.Data
{
    /// <summary>
    /// Data access for <c>person</c> and <c>person_contact</c>.
    ///
    /// Every statement is parameterised. Mutating statements guard on
    /// <c>row_version</c> and return the affected row count, so a lost update is
    /// detected rather than silently applied.
    ///
    /// Soft-deleted people are excluded everywhere except <see cref="GetByIdAsync"/>,
    /// which an administrator uses to inspect or restore one.
    /// </summary>
    public interface IPersonRepository
    {
        Task<Person?> GetByPublicIdAsync(string publicId, bool includeDeleted = false);
        Task<Person?> GetByIdAsync(long id);

        Task<IReadOnlyList<Person>> SearchAsync(PersonQuery query);
        Task<int> CountAsync(PersonQuery query);

        /// <summary>
        /// People whose contact points match any of the supplied normalized values.
        /// Backs duplicate detection at intake.
        /// </summary>
        Task<IReadOnlyList<Person>> FindByContactAsync(IEnumerable<string> normalizedValues);

        /// <summary>Free-text match on name, for the person picker.</summary>
        Task<IReadOnlyList<Person>> FindByNameAsync(string term, int limit);

        Task<long> CreateAsync(Person person, IEnumerable<PersonContact> contacts, long? actingUserId);

        Task<bool> UpdateAsync(Person person, long? actingUserId);
        Task<bool> SetDoNotContactAsync(long personId, int rowVersion, bool doNotContact, string? note, DateTime nowUtc, long? actingUserId);
        Task<bool> SetLifecycleAsync(long personId, int rowVersion, string lifecycleStatus, DateTime? becameMemberOn, long? actingUserId);
        Task<bool> SoftDeleteAsync(long personId, int rowVersion, DateTime nowUtc, long? actingUserId);

        Task<long> AddContactAsync(long personId, PersonContact contact, long? actingUserId);
        Task<bool> RemoveContactAsync(long personId, long contactId);
        Task<bool> SetPrimaryContactAsync(long personId, long contactId, string contactType);

        /// <summary>Resolves a campus public id to its internal key. Null when unknown.</summary>
        Task<long?> ResolveCampusIdAsync(string? campusPublicId);

        /// <summary>Next reference code in the P0001 series. Generated inside the insert transaction.</summary>
        Task<string> NextReferenceCodeAsync(IDbConnection connection, IDbTransaction transaction);
    }

    /// <summary>Filters for a people search. Null means "no filter".</summary>
    public sealed class PersonQuery
    {
        public string? Search { get; init; }
        public string? LifecycleStatus { get; init; }
        public long? CampusId { get; init; }
        public bool? DoNotContact { get; init; }
        public int Skip { get; init; }
        public int Take { get; init; } = 25;
    }

    public sealed class PersonRepository : IPersonRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public PersonRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        private const string SelectPerson = @"
            SELECT
                p.id                  AS Id,
                p.public_id           AS PublicId,
                p.reference_code      AS ReferenceCode,
                p.campus_id           AS CampusId,
                c.public_id           AS CampusPublicId,
                c.name                AS CampusName,
                p.given_name          AS GivenName,
                p.family_name         AS FamilyName,
                p.full_name           AS FullName,
                p.date_of_birth       AS DateOfBirth,
                p.age_band            AS AgeBand,
                p.gender              AS Gender,
                p.household_type      AS HouseholdType,
                p.address_line        AS AddressLine,
                p.locality            AS Locality,
                p.area_id             AS AreaId,
                ar.public_id          AS AreaPublicId,
                ar.name               AS AreaName,
                p.postal_code         AS PostalCode,
                p.is_local            AS IsLocal,
                p.lifecycle_status    AS LifecycleStatus,
                p.became_member_on    AS BecameMemberOn,
                p.do_not_contact      AS DoNotContact,
                p.do_not_contact_at   AS DoNotContactAt,
                p.do_not_contact_note AS DoNotContactNote,
                p.notes               AS Notes,
                p.deleted_at          AS DeletedAt,
                p.created_at          AS CreatedAt,
                p.updated_at          AS UpdatedAt,
                p.row_version         AS RowVersion
            FROM person p
            LEFT JOIN campus c ON c.id = p.campus_id
            LEFT JOIN area   ar ON ar.id = p.area_id";

        private const string SelectContacts = @"
            SELECT  id               AS Id,
                    person_id        AS PersonId,
                    contact_type     AS ContactType,
                    value            AS Value,
                    normalized_value AS NormalizedValue,
                    is_primary       AS IsPrimary,
                    is_verified      AS IsVerified,
                    verified_at      AS VerifiedAt,
                    opted_out_at     AS OptedOutAt,
                    created_at       AS CreatedAt
            FROM person_contact
            WHERE person_id IN @PersonIds
            ORDER BY is_primary DESC, id;";

        // ------------------------------------------------------------------
        // Reads
        // ------------------------------------------------------------------

        public async Task<Person?> GetByPublicIdAsync(string publicId, bool includeDeleted = false)
        {
            var sql = SelectPerson + @"
            WHERE p.public_id = @PublicId"
            + (includeDeleted ? "" : " AND p.deleted_at IS NULL")
            + " LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            var person = await connection.QueryFirstOrDefaultAsync<Person>(sql, new { PublicId = publicId });

            if (person is not null)
                await AttachContactsAsync(connection, new[] { person });

            return person;
        }

        public async Task<Person?> GetByIdAsync(long id)
        {
            const string sql = SelectPerson + @" WHERE p.id = @Id LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            var person = await connection.QueryFirstOrDefaultAsync<Person>(sql, new { Id = id });

            if (person is not null)
                await AttachContactsAsync(connection, new[] { person });

            return person;
        }

        public async Task<IReadOnlyList<Person>> SearchAsync(PersonQuery query)
        {
            var sql = SelectPerson + WhereClause() + @"
            ORDER BY p.created_at DESC
            LIMIT @Take OFFSET @Skip;";

            using var connection = _dbFactory.GetConnection();
            var people = (await connection.QueryAsync<Person>(sql, Parameters(query))).ToList();

            if (people.Count > 0)
                await AttachContactsAsync(connection, people);

            return people;
        }

        public async Task<int> CountAsync(PersonQuery query)
        {
            var sql = @"
                SELECT COUNT(1)
                FROM person p
                LEFT JOIN campus c ON c.id = p.campus_id" + WhereClause() + ";";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql, Parameters(query));
        }

        /// <summary>
        /// Shared predicate. The search term is matched against the generated
        /// full_name column and against contact values, so staff can find someone by
        /// either a name or the number they were given.
        /// </summary>
        private static string WhereClause() => @"
            WHERE p.deleted_at IS NULL
              AND (@Search IS NULL
                   OR p.full_name      LIKE CONCAT('%', @Search, '%')
                   OR p.reference_code LIKE CONCAT('%', @Search, '%')
                   OR EXISTS (SELECT 1 FROM person_contact pc
                               WHERE pc.person_id = p.id
                                 AND (pc.value LIKE CONCAT('%', @Search, '%')
                                   -- Guarded on NOT NULL: a non-numeric term normalises to
                                   -- an empty string, and LIKE '%%' would match every
                                   -- contact row, so searching a name returned everyone.
                                   OR (@SearchDigits IS NOT NULL
                                       AND pc.normalized_value LIKE CONCAT('%', @SearchDigits, '%')))))
              AND (@LifecycleStatus IS NULL OR p.lifecycle_status = @LifecycleStatus)
              AND (@CampusId IS NULL OR p.campus_id = @CampusId)
              AND (@DoNotContact IS NULL OR p.do_not_contact = @DoNotContact)";

        private static object Parameters(PersonQuery query)
        {
            var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

            return new
            {
                Search = search,
                // Lets "98765 43210" match a stored "9876543210". Null when the term
                // holds no digits, so the digit clause is skipped entirely.
                SearchDigits = NullIfEmpty(search is null ? null : ContactNormalizer.NormalizePhone(search)),
                query.LifecycleStatus,
                query.CampusId,
                query.DoNotContact,
                query.Skip,
                query.Take
            };
        }

        private static string? NullIfEmpty(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;

        public async Task<IReadOnlyList<Person>> FindByContactAsync(IEnumerable<string> normalizedValues)
        {
            var values = normalizedValues
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (values.Length == 0) return Array.Empty<Person>();

            const string sql = SelectPerson + @"
            WHERE p.deleted_at IS NULL
              AND EXISTS (SELECT 1 FROM person_contact pc
                           WHERE pc.person_id = p.id
                             AND pc.normalized_value IN @Values)
            LIMIT 25;";

            using var connection = _dbFactory.GetConnection();
            var people = (await connection.QueryAsync<Person>(sql, new { Values = values })).ToList();

            if (people.Count > 0)
                await AttachContactsAsync(connection, people);

            return people;
        }

        public async Task<IReadOnlyList<Person>> FindByNameAsync(string term, int limit)
        {
            const string sql = SelectPerson + @"
            WHERE p.deleted_at IS NULL
              AND p.full_name LIKE CONCAT('%', @Term, '%')
            ORDER BY p.full_name
            LIMIT @Limit;";

            using var connection = _dbFactory.GetConnection();
            var people = (await connection.QueryAsync<Person>(sql, new { Term = term.Trim(), Limit = limit })).ToList();

            if (people.Count > 0)
                await AttachContactsAsync(connection, people);

            return people;
        }

        // ------------------------------------------------------------------
        // Writes
        // ------------------------------------------------------------------

        public async Task<long> CreateAsync(Person person, IEnumerable<PersonContact> contacts, long? actingUserId)
        {
            const string insertPerson = @"
                INSERT INTO person
                    (public_id, reference_code, campus_id, given_name, family_name,
                     date_of_birth, age_band, gender, household_type,
                     address_line, locality, area_id, postal_code, is_local,
                     lifecycle_status, notes, created_by, updated_by)
                VALUES
                    (@PublicId, @ReferenceCode, @CampusId, @GivenName, @FamilyName,
                     @DateOfBirth, @AgeBand, @Gender, @HouseholdType,
                     @AddressLine, @Locality, @AreaId, @PostalCode, @IsLocal,
                     @LifecycleStatus, @Notes, @ActingUserId, @ActingUserId);
                SELECT LAST_INSERT_ID();";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Generated inside the transaction so two concurrent intakes cannot
                // be handed the same code.
                person.ReferenceCode = await NextReferenceCodeAsync(connection, transaction);

                var personId = await connection.ExecuteScalarAsync<long>(insertPerson, new
                {
                    person.PublicId,
                    person.ReferenceCode,
                    person.CampusId,
                    person.GivenName,
                    person.FamilyName,
                    person.DateOfBirth,
                    person.AgeBand,
                    person.Gender,
                    person.HouseholdType,
                    person.AddressLine,
                    person.Locality,
                    person.AreaId,
                    person.PostalCode,
                    person.IsLocal,
                    person.LifecycleStatus,
                    person.Notes,
                    ActingUserId = actingUserId
                }, transaction);

                foreach (var contact in contacts)
                    await InsertContactAsync(connection, transaction, personId, contact, actingUserId);

                transaction.Commit();
                return personId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<bool> UpdateAsync(Person person, long? actingUserId)
        {
            const string sql = @"
                UPDATE person
                SET given_name     = @GivenName,
                    family_name    = @FamilyName,
                    campus_id      = @CampusId,
                    date_of_birth  = @DateOfBirth,
                    age_band       = @AgeBand,
                    gender         = @Gender,
                    household_type = @HouseholdType,
                    address_line   = @AddressLine,
                    locality       = @Locality,
                    area_id        = @AreaId,
                    postal_code    = @PostalCode,
                    is_local       = @IsLocal,
                    notes          = @Notes,
                    updated_by     = @ActingUserId,
                    row_version    = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion AND deleted_at IS NULL;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                person.Id,
                person.RowVersion,
                person.GivenName,
                person.FamilyName,
                person.CampusId,
                person.DateOfBirth,
                person.AgeBand,
                person.Gender,
                person.HouseholdType,
                person.AddressLine,
                person.Locality,
                person.AreaId,
                person.PostalCode,
                person.IsLocal,
                person.Notes,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task<bool> SetDoNotContactAsync(
            long personId, int rowVersion, bool doNotContact, string? note, DateTime nowUtc, long? actingUserId)
        {
            // do_not_contact_at is cleared when the flag is lifted, so the column
            // always answers "when was this set" for the CURRENT state.
            const string sql = @"
                UPDATE person
                SET do_not_contact      = @DoNotContact,
                    do_not_contact_at   = CASE WHEN @DoNotContact = 1 THEN @NowUtc ELSE NULL END,
                    do_not_contact_note = CASE WHEN @DoNotContact = 1 THEN @Note ELSE NULL END,
                    updated_by          = @ActingUserId,
                    row_version         = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion AND deleted_at IS NULL;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                Id = personId,
                RowVersion = rowVersion,
                DoNotContact = doNotContact,
                Note = note,
                NowUtc = nowUtc,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task<bool> SetLifecycleAsync(
            long personId, int rowVersion, string lifecycleStatus, DateTime? becameMemberOn, long? actingUserId)
        {
            const string sql = @"
                UPDATE person
                SET lifecycle_status = @LifecycleStatus,
                    became_member_on = @BecameMemberOn,
                    updated_by       = @ActingUserId,
                    row_version      = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion AND deleted_at IS NULL;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                Id = personId,
                RowVersion = rowVersion,
                LifecycleStatus = lifecycleStatus,
                BecameMemberOn = becameMemberOn,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task<bool> SoftDeleteAsync(long personId, int rowVersion, DateTime nowUtc, long? actingUserId)
        {
            // Soft delete, because care history, escalations and notes reference this
            // row and must stay readable.
            const string sql = @"
                UPDATE person
                SET deleted_at  = @NowUtc,
                    deleted_by  = @ActingUserId,
                    row_version = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion AND deleted_at IS NULL;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                Id = personId,
                RowVersion = rowVersion,
                NowUtc = nowUtc,
                ActingUserId = actingUserId
            }) == 1;
        }

        // ------------------------------------------------------------------
        // Contacts
        // ------------------------------------------------------------------

        public async Task<long> AddContactAsync(long personId, PersonContact contact, long? actingUserId)
        {
            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var id = await InsertContactAsync(connection, transaction, personId, contact, actingUserId);
                transaction.Commit();
                return id;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<bool> RemoveContactAsync(long personId, long contactId)
        {
            const string sql = @"DELETE FROM person_contact WHERE id = @Id AND person_id = @PersonId;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { Id = contactId, PersonId = personId }) == 1;
        }

        public async Task<bool> SetPrimaryContactAsync(long personId, long contactId, string contactType)
        {
            // Only one primary per type, so demote the others in the same transaction.
            const string demote = @"
                UPDATE person_contact SET is_primary = 0
                WHERE person_id = @PersonId AND contact_type = @ContactType;";

            const string promote = @"
                UPDATE person_contact SET is_primary = 1
                WHERE id = @Id AND person_id = @PersonId;";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                await connection.ExecuteAsync(demote, new { PersonId = personId, ContactType = contactType }, transaction);
                var updated = await connection.ExecuteAsync(promote, new { Id = contactId, PersonId = personId }, transaction);

                transaction.Commit();
                return updated == 1;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        public async Task<long?> ResolveCampusIdAsync(string? campusPublicId)
        {
            if (string.IsNullOrWhiteSpace(campusPublicId)) return null;

            const string sql = @"SELECT id FROM campus WHERE public_id = @PublicId AND is_active = 1 LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long?>(sql, new { PublicId = campusPublicId });
        }

        public async Task<string> NextReferenceCodeAsync(IDbConnection connection, IDbTransaction transaction)
        {
            // Sequential P#### for staff to quote. Derived from the existing maximum
            // rather than a counter table, and generated inside the caller's
            // transaction so concurrent intakes cannot collide.
            const string sql = @"
                -- LPAD TRUNCATES when the value is longer than the width, so a
                -- plain LPAD(n, 4) silently returns the first 4 characters once the
                -- sequence outgrows it. The MVP's codes carried the year
                -- (P2026149), which is seven digits, so every generated code came
                -- back as 'P2026' and the second intake collided on the unique
                -- key — no visitor could be recorded at all. GREATEST keeps the
                -- padding for small numbers and gets out of the way for large ones.
                SELECT CONCAT('P', LPAD(
                    IFNULL(MAX(CAST(SUBSTRING(reference_code, 2) AS UNSIGNED)), 0) + 1,
                    GREATEST(4, CHAR_LENGTH(
                        IFNULL(MAX(CAST(SUBSTRING(reference_code, 2) AS UNSIGNED)), 0) + 1)),
                    '0'))
                FROM person
                WHERE reference_code REGEXP '^P[0-9]+$'
                FOR UPDATE;";

            return await connection.ExecuteScalarAsync<string>(sql, transaction: transaction) ?? "P0001";
        }

        private static async Task<long> InsertContactAsync(
            IDbConnection connection, IDbTransaction transaction,
            long personId, PersonContact contact, long? actingUserId)
        {
            const string sql = @"
                INSERT INTO person_contact
                    (person_id, contact_type, value, normalized_value, is_primary, created_by, updated_by)
                VALUES
                    (@PersonId, @ContactType, @Value, @NormalizedValue, @IsPrimary, @ActingUserId, @ActingUserId);
                SELECT LAST_INSERT_ID();";

            return await connection.ExecuteScalarAsync<long>(sql, new
            {
                PersonId = personId,
                contact.ContactType,
                contact.Value,
                contact.NormalizedValue,
                contact.IsPrimary,
                ActingUserId = actingUserId
            }, transaction);
        }

        /// <summary>Loads contacts for a page of people in one round trip rather than N.</summary>
        private static async Task AttachContactsAsync(IDbConnection connection, IReadOnlyCollection<Person> people)
        {
            var contacts = await connection.QueryAsync<PersonContact>(
                SelectContacts, new { PersonIds = people.Select(p => p.Id).ToArray() });

            var byPerson = contacts.GroupBy(c => c.PersonId)
                                   .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var person in people)
                person.Contacts = byPerson.TryGetValue(person.Id, out var list) ? list : new List<PersonContact>();
        }
    }
}
