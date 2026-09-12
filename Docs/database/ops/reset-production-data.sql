-- =============================================================================
-- DESTRUCTIVE: full operational data reset + single administrator
-- =============================================================================
--
-- WHAT THIS DOES
--   1. Deletes every row from every operational table: people, contacts, user
--      accounts, roles, tokens, care cases, interactions, escalations,
--      volunteers, teams, notes, web enquiries, church events, notifications,
--      job runs and the security event log.
--   2. Keeps the lookup / configuration tables and clears the audit columns on
--      them that pointed at deleted users:
--        campus, area, app_role, contact_method, connection_source,
--        care_outcome, visitor_intent, nurture_plan, nurture_plan_step,
--        care_progression_rule, escalation_reason, escalation_outcome,
--        capacity_band, note_type, app_setting, telegram_template
--   3. Creates ONE person and ONE account holding the ADMIN role:
--        username  9999999999
--        password  Adminuser@12345
--
--   There is NO undo. Take a backup first:
--     mysqldump -u <user> -p --single-transaction --routines cms_api_db > backup_before_reset.sql
--
-- HOW TO RUN
--     mysql -u <user> -p cms_api_db < reset-production-data.sql
--
--   Stop the API (or put it in maintenance) before running, and restart it
--   afterwards so nothing is holding stale rows or live tokens.
--
-- THE PASSWORD HASH BELOW
--   ASP.NET Core Identity V3: PBKDF2-HMAC-SHA512, 210,000 iterations, 16-byte
--   salt, 32-byte subkey -- the exact PasswordHasherOptions configured in
--   Program.cs. It was generated with Microsoft.AspNetCore.Identity's own
--   PasswordHasher and verified against it. If you change IterationCount in
--   Program.cs, regenerate the hash; an old-format hash still verifies and is
--   silently rehashed on the next successful sign-in.
--
--   Adminuser@12345 satisfies the policy in Auth (min 12 chars, upper, lower,
--   digit, special) and is not on the blocked-common-passwords list, so it can
--   also be re-entered through the application later.
-- =============================================================================


-- -----------------------------------------------------------------------------
-- STEP 0. Safety guards. Nothing below runs unless all three pass.
--
-- A failed guard aborts the script with:
--     ERROR 3141 (22032): Invalid JSON text in argument 1 to function
--     json_extract: "Invalid value." at position 0
-- The SELECT immediately above each guard prints the real reason first.
-- -----------------------------------------------------------------------------

-- Edit these two lines. The script refuses to run until you do.
SET @expected_db := 'cms_api_db';
SET @i_have_taken_a_backup := 0;   -- change to 1 to arm the script


SELECT CONCAT('ABORT: connected to `', DATABASE(), '`, expected `',
              @expected_db, '`. Nothing was changed.') AS abort_reason
WHERE DATABASE() IS NULL OR DATABASE() <> @expected_db;

SET @guard := IF(DATABASE() = @expected_db, 1, JSON_EXTRACT('abort', '$'));


SELECT 'ABORT: set @i_have_taken_a_backup := 1 at the top of this script first.'
       AS abort_reason
WHERE @i_have_taken_a_backup <> 1;

SET @guard := IF(@i_have_taken_a_backup = 1, 1, JSON_EXTRACT('abort', '$'));


-- The new admin's person row needs a campus, and campus is a table this script
-- preserves rather than seeds.
SET @campus_id := (SELECT id FROM campus WHERE is_active = 1 ORDER BY id LIMIT 1);

SELECT 'ABORT: no active campus exists. Apply Docs/architecture/schema.sql, which seeds one.'
       AS abort_reason
WHERE @campus_id IS NULL;

SET @guard := IF(@campus_id IS NOT NULL, 1, JSON_EXTRACT('abort', '$'));


-- -----------------------------------------------------------------------------
-- STEP 1. Delete all operational data. One transaction: all of it or none.
-- -----------------------------------------------------------------------------
SET @OLD_FK_CHECKS := @@FOREIGN_KEY_CHECKS;
SET FOREIGN_KEY_CHECKS = 0;

START TRANSACTION;

-- Website and events
DELETE FROM church_event_poster;
DELETE FROM church_event;
DELETE FROM web_enquiry;

-- Messaging
DELETE FROM telegram_link_token;
DELETE FROM notification_delivery;

-- Pastoral care
DELETE FROM note;
DELETE FROM escalation;
DELETE FROM care_interaction;
DELETE FROM care_case_assignment;
DELETE FROM care_case;

-- Volunteers and teams
DELETE FROM volunteer_survey;
DELETE FROM volunteer_check_in;
DELETE FROM volunteer_capacity_change;
DELETE FROM volunteer;
DELETE FROM team;

-- Identity and audit
DELETE FROM login_challenge;
DELETE FROM security_event;
DELETE FROM password_history;
DELETE FROM refresh_token;
DELETE FROM user_role;
DELETE FROM user_account;

-- People
DELETE FROM person_contact;
DELETE FROM person;

-- Background job history
DELETE FROM job_run;

-- Kept tables: drop the references to users that no longer exist.
UPDATE telegram_template     SET updated_by = NULL;
UPDATE app_setting           SET updated_by = NULL;
UPDATE campus                SET created_by = NULL, updated_by = NULL;
UPDATE area                  SET created_by = NULL, updated_by = NULL;
UPDATE nurture_plan          SET created_by = NULL, updated_by = NULL;
UPDATE care_progression_rule SET created_by = NULL, updated_by = NULL;

COMMIT;

SET FOREIGN_KEY_CHECKS = @OLD_FK_CHECKS;


-- -----------------------------------------------------------------------------
-- STEP 2. Restart the id sequences, so the new admin is person 1 / account 1.
--
-- These are DDL and cannot be rolled back. They are also optional: skip this
-- whole block if you would rather ids kept climbing from where they were.
-- -----------------------------------------------------------------------------
ALTER TABLE person                    AUTO_INCREMENT = 1;
ALTER TABLE person_contact            AUTO_INCREMENT = 1;
ALTER TABLE user_account              AUTO_INCREMENT = 1;
ALTER TABLE refresh_token             AUTO_INCREMENT = 1;
ALTER TABLE password_history          AUTO_INCREMENT = 1;
ALTER TABLE security_event            AUTO_INCREMENT = 1;
ALTER TABLE login_challenge           AUTO_INCREMENT = 1;
ALTER TABLE team                      AUTO_INCREMENT = 1;
ALTER TABLE volunteer                 AUTO_INCREMENT = 1;
ALTER TABLE volunteer_capacity_change AUTO_INCREMENT = 1;
ALTER TABLE volunteer_check_in        AUTO_INCREMENT = 1;
ALTER TABLE volunteer_survey          AUTO_INCREMENT = 1;
ALTER TABLE care_case                 AUTO_INCREMENT = 1;
ALTER TABLE care_case_assignment      AUTO_INCREMENT = 1;
ALTER TABLE care_interaction          AUTO_INCREMENT = 1;
ALTER TABLE escalation                AUTO_INCREMENT = 1;
ALTER TABLE note                      AUTO_INCREMENT = 1;
ALTER TABLE telegram_link_token       AUTO_INCREMENT = 1;
ALTER TABLE notification_delivery     AUTO_INCREMENT = 1;
ALTER TABLE job_run                   AUTO_INCREMENT = 1;
ALTER TABLE web_enquiry               AUTO_INCREMENT = 1;
ALTER TABLE church_event              AUTO_INCREMENT = 1;
ALTER TABLE church_event_poster       AUTO_INCREMENT = 1;


-- -----------------------------------------------------------------------------
-- STEP 3. Create the administrator.
-- -----------------------------------------------------------------------------
SET @campus_id := (SELECT id FROM campus WHERE is_active = 1 ORDER BY id LIMIT 1);

SET @username      := '9999999999';
SET @password_hash := 'AQAAAAIAAzRQAAAAEAzbwqbhPLn59dz1QNQ+Dlu6V0Gc1FesBvjn21yWtREOJ1svH2wJU4awC9ZmlB6s2w==';

-- ULIDs (Crockford base32, 26 chars) generated with the app's own generator.
SET @person_ulid  := '01M27E8WK0T4Z434TFYTGV9TQ9';
SET @account_ulid := '01M27E8WK09BAYEQSHJ0J68N1H';
SET @stamp_ulid   := '01M27E8WK067NMTMNBJZVZVR3J';

START TRANSACTION;

INSERT INTO person (public_id, campus_id, given_name, family_name, created_by)
VALUES (@person_ulid, @campus_id, 'System', 'Administrator', NULL);

SET @person_id := LAST_INSERT_ID();

INSERT INTO user_account
    (public_id, person_id, username, normalized_username,
     password_hash, security_stamp, token_version,
     is_active, must_change_password, allows_passwordless_login,
     failed_access_count)
VALUES
    (@account_ulid, @person_id, @username, UPPER(@username),
     @password_hash, @stamp_ulid, 1,
     1, 0, 0,
     0);

SET @account_id := LAST_INSERT_ID();

INSERT INTO user_role (user_account_id, role_code, campus_id, granted_by)
VALUES (@account_id, 'ADMIN', NULL, NULL);

-- Seeded so the password-reuse check knows about this password. Without it the
-- admin can "change" the password straight back to the same value.
INSERT INTO password_history (user_account_id, password_hash)
VALUES (@account_id, @password_hash);

-- The mobile number as a contact, so person search and the phone-lookup paths
-- find this account. normalized_value is the last 10 digits, which is what
-- ContactNormalizer.NormalizePhone produces and what every lookup reads.
INSERT INTO person_contact
    (person_id, contact_type, value, normalized_value, is_primary, is_verified)
VALUES
    (@person_id, 'MOBILE', @username, @username, 1, 1);

COMMIT;


-- -----------------------------------------------------------------------------
-- STEP 4. Verify. Expect exactly one row, role ADMIN, active, 1 contact.
-- -----------------------------------------------------------------------------
SELECT ua.id           AS account_id,
       ua.username,
       ua.is_active,
       ua.must_change_password,
       ua.allows_passwordless_login,
       ur.role_code,
       p.full_name,
       c.code          AS campus,
       (SELECT COUNT(*) FROM person_contact pc WHERE pc.person_id = p.id) AS contacts
FROM user_account ua
JOIN user_role ur ON ur.user_account_id = ua.id
JOIN person    p  ON p.id  = ua.person_id
LEFT JOIN campus c ON c.id = p.campus_id;

SELECT 'person' AS table_name, COUNT(*) AS rows_left FROM person
UNION ALL SELECT 'user_account',    COUNT(*) FROM user_account
UNION ALL SELECT 'person_contact',  COUNT(*) FROM person_contact
UNION ALL SELECT 'care_case',       COUNT(*) FROM care_case
UNION ALL SELECT 'volunteer',       COUNT(*) FROM volunteer
UNION ALL SELECT 'team',            COUNT(*) FROM team
UNION ALL SELECT 'church_event',    COUNT(*) FROM church_event
UNION ALL SELECT 'web_enquiry',     COUNT(*) FROM web_enquiry
UNION ALL SELECT 'campus (kept)',   COUNT(*) FROM campus
UNION ALL SELECT 'area (kept)',     COUNT(*) FROM area
UNION ALL SELECT 'app_role (kept)', COUNT(*) FROM app_role;
