-- =============================================================================
-- 009 — Sign in with a mobile number alone
--
-- Some of the people who use this system cannot read. Asking them for a password
-- does not make their account safer; it makes the system unusable to them, and
-- what actually happens is that somebody literate types the password for them —
-- so the credential ends up shared, written down, or both. A per-account switch
-- that an administrator turns on deliberately is more honest than a password
-- three people know.
--
-- WHAT THIS COSTS, STATED PLAINLY:
-- for an account with this set, the mobile number IS the credential. Anyone who
-- knows the number can sign in as that person and see whatever that person can
-- see. Mobile numbers are not secret — they are on posters, in group chats, and
-- known to every member of a family. This is a deliberate trade of security for
-- access, made one account at a time, and it must never be the default.
--
-- Hence: NOT NULL DEFAULT 0. Every existing account keeps needing a password,
-- and every new one does too, until somebody decides otherwise for that person.
--
-- Safe to re-run: guarded.
-- =============================================================================

USE cms_api_db;

-- -----------------------------------------------------------------------------
-- 1. The switch
--
-- On user_account rather than person, because it is a property of SIGNING IN,
-- not of the human being. A person with two accounts could reasonably have one
-- of each.
-- -----------------------------------------------------------------------------
SET @sql := (
    SELECT IF(
        EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_schema = DATABASE()
                   AND table_name = 'user_account'
                   AND column_name = 'allows_passwordless_login'),
        'SELECT ''allows_passwordless_login already present'' AS note',
        'ALTER TABLE user_account
            ADD COLUMN allows_passwordless_login TINYINT(1) NOT NULL DEFAULT 0
            AFTER must_change_password'
    )
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- -----------------------------------------------------------------------------
-- 2. An index for the one question worth asking of it
--
-- "Which accounts can be signed into with a number alone?" is the audit an
-- administrator should be able to run, and the answer should stay cheap as the
-- table grows. Without this it is a full scan of every account in the church.
-- -----------------------------------------------------------------------------
SET @sql := (
    SELECT IF(
        EXISTS (SELECT 1 FROM information_schema.statistics
                 WHERE table_schema = DATABASE()
                   AND table_name = 'user_account'
                   AND index_name = 'ix_user_account_passwordless'),
        'SELECT ''index already present'' AS note',
        'CREATE INDEX ix_user_account_passwordless
             ON user_account (allows_passwordless_login, is_active)'
    )
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- -----------------------------------------------------------------------------
-- Verification
--
-- The second query is the audit itself. It should return zero rows on a database
-- where nobody has been granted this yet, and it is the query to run whenever
-- somebody asks "who can get in without a password?"
-- -----------------------------------------------------------------------------
SELECT column_name, column_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_schema = DATABASE()
  AND table_name = 'user_account'
  AND column_name = 'allows_passwordless_login';

SELECT ua.username, p.full_name, ua.is_active
FROM user_account ua
JOIN person p ON p.id = ua.person_id
WHERE ua.allows_passwordless_login = 1
ORDER BY p.full_name;
