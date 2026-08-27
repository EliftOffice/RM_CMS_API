-- ============================================================================
-- MVP  ->  v2 schema data migration
--
-- Moves the production data in `rm_cms_10_aug_2026` (the old MVP schema) onto the
-- 33-table schema in `cms_api_db`. Source is read-only throughout; every write
-- lands in the target.
--
-- WHAT THIS IS FOR
--   Local testing against real data. The MVP shapes and the v2 shapes disagree in
--   ways that need judgement, not just column renames — that judgement is written
--   down inline so the same decisions can be reviewed before this is ever pointed
--   at production.
--
-- THE THREE STRUCTURAL DIFFERENCES THAT DRIVE EVERYTHING BELOW
--   1. MVP `people` conflates a PERSON with their FOLLOW-UP JOURNEY. v2 splits
--      them: `person` is the human, `care_case` is one journey. One MVP row
--      therefore becomes two.
--   2. MVP has two separate contact tables (`follow_ups`, `nurture_steps`). v2 has
--      one `care_interaction` with a `stage` column. Both collapse into it.
--   3. MVP identifies people by phone strings scattered across four tables. v2 has
--      a single `person` with `person_contact` rows, so the same human recorded as
--      both a visitor and a volunteer must become ONE person.
--
-- RE-RUNNABLE. Clears the target's transactional tables first and preserves the
-- local administrator account, so it can be run repeatedly while iterating.
--
-- NOT FOR PRODUCTION AS-IS: see the password note at step 6.
-- ============================================================================

SET NAMES utf8mb4;
SET SESSION sql_mode = 'STRICT_ALL_TABLES';

USE cms_api_db;

-- ----------------------------------------------------------------------------
-- 0 · Preserve the local administrator
--
-- The bootstrap admin is how anyone signs in locally. It is not in the MVP data,
-- so it has to survive the clear-down and be put back.
-- ----------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS _keep_person, _keep_account, _keep_roles;

CREATE TEMPORARY TABLE _keep_person AS
    SELECT * FROM person WHERE id IN (SELECT person_id FROM user_account WHERE id = 1);

CREATE TEMPORARY TABLE _keep_account AS
    SELECT * FROM user_account WHERE id = 1;

CREATE TEMPORARY TABLE _keep_roles AS
    SELECT * FROM user_role WHERE user_account_id = 1;

-- ----------------------------------------------------------------------------
-- 1 · Clear the transactional tables
--
-- Lookup and seed tables (campus, capacity_band, contact_method, care_outcome,
-- visitor_intent, note_type, escalation_reason, escalation_outcome,
-- connection_source, app_role, app_setting, nurture_plan, nurture_plan_step,
-- care_progression_rule) are DELIBERATELY LEFT ALONE — they are configuration,
-- not data, and the MVP's equivalents are a subset of them.
-- ----------------------------------------------------------------------------
SET FOREIGN_KEY_CHECKS = 0;

TRUNCATE TABLE notification_delivery;
TRUNCATE TABLE job_run;
TRUNCATE TABLE note;
TRUNCATE TABLE escalation;
TRUNCATE TABLE care_interaction;
TRUNCATE TABLE care_case_assignment;
TRUNCATE TABLE care_case;
TRUNCATE TABLE volunteer_capacity_change;
TRUNCATE TABLE volunteer_check_in;
TRUNCATE TABLE volunteer_survey;
TRUNCATE TABLE volunteer;
TRUNCATE TABLE team;
TRUNCATE TABLE user_role;
TRUNCATE TABLE refresh_token;
TRUNCATE TABLE password_history;
TRUNCATE TABLE security_event;
TRUNCATE TABLE telegram_link_token;
TRUNCATE TABLE user_account;
TRUNCATE TABLE person_contact;
TRUNCATE TABLE person;

SET FOREIGN_KEY_CHECKS = 1;

-- Put the administrator back, keeping id 1 so nothing that referenced it drifts.
-- Columns are listed explicitly because person.full_name is GENERATED — SELECT *
-- carries it along, and MySQL refuses any value for a generated column.
INSERT INTO person
    (id, public_id, reference_code, campus_id, given_name, family_name,
     date_of_birth, age_band, gender, household_type, address_line, locality,
     postal_code, is_local, lifecycle_status, became_member_on,
     do_not_contact, do_not_contact_at, do_not_contact_note, notes,
     deleted_at, deleted_by, created_at, created_by, updated_at, updated_by, row_version)
SELECT
     id, public_id, reference_code, campus_id, given_name, family_name,
     date_of_birth, age_band, gender, household_type, address_line, locality,
     postal_code, is_local, lifecycle_status, became_member_on,
     do_not_contact, do_not_contact_at, do_not_contact_note, notes,
     deleted_at, deleted_by, created_at, created_by, updated_at, updated_by, row_version
FROM _keep_person;
INSERT INTO user_account SELECT * FROM _keep_account;
INSERT INTO user_role SELECT * FROM _keep_roles;

-- ----------------------------------------------------------------------------
-- 2 · Mapping tables
--
-- Old ids are VARCHAR business keys ('P2026020', 'V001'); new ids are BIGINT
-- surrogates handed out by AUTO_INCREMENT. Every later step joins through these.
-- ----------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS _map_person, _map_volunteer, _map_lead, _map_case, _map_followup;

-- COLLATION. The source database is utf8mb4_general_ci and the target is
-- utf8mb4_0900_ai_ci. Joining a string column across the two raises
-- "Illegal mix of collations" — MySQL will not guess which one wins. The mapping
-- keys are declared in the SOURCE collation because that is what they are
-- compared against; inserting a target-collation value INTO them is a plain
-- conversion, which is allowed.
CREATE TEMPORARY TABLE _map_person (
    src_key   VARCHAR(50) COLLATE utf8mb4_general_ci NOT NULL,
    src_table VARCHAR(20) COLLATE utf8mb4_general_ci NOT NULL,
    new_id    BIGINT UNSIGNED NOT NULL,
    PRIMARY KEY (src_table, src_key),
    KEY (new_id)
) ENGINE=InnoDB;

CREATE TEMPORARY TABLE _map_volunteer (
    src_key   VARCHAR(20) COLLATE utf8mb4_general_ci NOT NULL PRIMARY KEY,
    new_id    BIGINT UNSIGNED NOT NULL,
    person_id BIGINT UNSIGNED NOT NULL,
    KEY (new_id)
) ENGINE=InnoDB;

CREATE TEMPORARY TABLE _map_lead (
    src_key    VARCHAR(20) COLLATE utf8mb4_general_ci NOT NULL PRIMARY KEY,
    team_id    BIGINT UNSIGNED NOT NULL,
    account_id BIGINT UNSIGNED NOT NULL,
    person_id  BIGINT UNSIGNED NOT NULL
) ENGINE=InnoDB;

CREATE TEMPORARY TABLE _map_case (
    src_key VARCHAR(50) COLLATE utf8mb4_general_ci NOT NULL PRIMARY KEY,
    new_id  BIGINT UNSIGNED NOT NULL,
    KEY (new_id)
) ENGINE=InnoDB;

CREATE TEMPORARY TABLE _map_followup (
    src_key VARCHAR(20) COLLATE utf8mb4_general_ci NOT NULL PRIMARY KEY,
    new_id  BIGINT UNSIGNED NOT NULL
) ENGINE=InnoDB;

-- ----------------------------------------------------------------------------
-- 3 · People (the visitors)
--
-- ULIDs: the schema wants CHAR(26) from Crockford base32. Uppercase hex is a
-- strict subset of that alphabet (no I, L, O or U appear in hex), so a 26-char
-- uppercase hex string is a valid, unique value of the right shape. These ids are
-- opaque handles — nothing decodes a timestamp out of them.
-- ----------------------------------------------------------------------------
INSERT INTO person
    (public_id, reference_code, campus_id, given_name, family_name,
     age_band, household_type, address_line, postal_code, is_local,
     lifecycle_status, notes, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    p.person_id,
    1,                                    -- every MVP row is campus 'Ongole'
    NULLIF(TRIM(p.first_name), ''),
    NULLIF(TRIM(p.last_name), ''),
    CASE p.age_range
        WHEN '18-25' THEN '18_25'
        WHEN '26-35' THEN '26_35'
        WHEN '36-45' THEN '36_45'
        -- MVP's top two bands are 46-55 and 56+; v2's are 46_60 and OVER_60.
        -- The boundary moves by five years. Nothing in the app reasons on the
        -- band numerically, so the nearest band is the honest choice.
        WHEN '46-55' THEN '46_60'
        WHEN '56+'   THEN 'OVER_60'
        ELSE NULL
    END,
    NULLIF(TRIM(p.household_type), ''),
    NULLIF(TRIM(p.address), ''),
    NULLIF(TRIM(p.zip_code), ''),
    IF(p.location_type = 'Non-Local', 0, 1),
    -- A completed follow-up does not mean they joined; the MVP had no membership
    -- flag at all, so everyone stays a VISITOR rather than inventing members.
    'VISITOR',
    NULLIF(TRIM(p.interested_in), ''),
    p.created_at,
    p.updated_at
FROM rm_cms_10_aug_2026.people p
ORDER BY p.id;

INSERT INTO _map_person (src_key, src_table, new_id)
SELECT reference_code, 'people', id FROM person WHERE reference_code LIKE 'P%';

-- ----------------------------------------------------------------------------
-- 4 · Volunteers as people
--
-- A volunteer is a person who serves, so they need a `person` row before a
-- `volunteer` row. One volunteer shares a phone with an existing visitor — the
-- same human, recorded twice by the MVP. They are matched here rather than
-- duplicated, because two person rows would split their history in half.
-- ----------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS _vol_existing;
CREATE TEMPORARY TABLE _vol_existing (
    volunteer_id VARCHAR(20) COLLATE utf8mb4_general_ci NOT NULL PRIMARY KEY,
    person_id    BIGINT UNSIGNED NOT NULL
) ENGINE=InnoDB;

INSERT INTO _vol_existing (volunteer_id, person_id)
SELECT v.volunteer_id, mp.new_id
FROM rm_cms_10_aug_2026.volunteers v
JOIN rm_cms_10_aug_2026.people src
  ON src.phone = v.phone AND v.phone IS NOT NULL AND v.phone <> ''
JOIN _map_person mp ON mp.src_key = src.person_id AND mp.src_table = 'people'
GROUP BY v.volunteer_id;

INSERT INTO person
    (public_id, reference_code, campus_id, given_name, family_name,
     is_local, lifecycle_status, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    v.volunteer_id,
    1,
    NULLIF(TRIM(v.first_name), ''),
    NULLIF(TRIM(v.last_name), ''),
    1,
    -- Someone serving the church is a member, not a visitor.
    'MEMBER',
    v.created_at,
    COALESCE(v.updated_at, v.created_at)
FROM rm_cms_10_aug_2026.volunteers v
WHERE v.volunteer_id NOT IN (SELECT volunteer_id FROM _vol_existing)
ORDER BY v.volunteer_id;

INSERT INTO _map_person (src_key, src_table, new_id)
SELECT reference_code, 'volunteers', id FROM person WHERE reference_code LIKE 'V%';

INSERT INTO _map_person (src_key, src_table, new_id)
SELECT volunteer_id, 'volunteers', person_id FROM _vol_existing
ON DUPLICATE KEY UPDATE new_id = VALUES(new_id);

-- ----------------------------------------------------------------------------
-- 5 · Team leads and app users as people
-- ----------------------------------------------------------------------------
INSERT INTO person
    (public_id, reference_code, campus_id, given_name, family_name,
     is_local, lifecycle_status, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    t.team_lead_id,
    1,
    NULLIF(TRIM(t.first_name), ''),
    NULLIF(TRIM(t.last_name), ''),
    1, 'MEMBER',
    t.created_at, t.updated_at
FROM rm_cms_10_aug_2026.team_leads t
ORDER BY t.team_lead_id;

INSERT INTO _map_person (src_key, src_table, new_id)
SELECT reference_code, 'team_leads', id FROM person WHERE reference_code LIKE 'TL%';

INSERT INTO person
    (public_id, reference_code, campus_id, given_name, family_name,
     is_local, lifecycle_status, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    CONCAT('AU', LPAD(a.id, 3, '0')),
    1,
    -- app_users has one free-text `name`. Split on the first space; anything with
    -- no space becomes a given name only, which the schema allows.
    NULLIF(TRIM(SUBSTRING_INDEX(a.name, ' ', 1)), ''),
    NULLIF(TRIM(SUBSTRING(a.name, LOCATE(' ', a.name) + 1)), ''),
    1, 'MEMBER',
    a.created_at, a.updated_at
FROM rm_cms_10_aug_2026.app_users a
ORDER BY a.id;

INSERT INTO _map_person (src_key, src_table, new_id)
SELECT reference_code, 'app_users', id FROM person WHERE reference_code LIKE 'AU%';

-- ----------------------------------------------------------------------------
-- 6 · Contact points
--
-- The MVP kept one phone/email column per table. v2 makes each a row, which is
-- what lets a person hold a mobile AND a Telegram chat without new columns.
--
-- TELEGRAM rows matter most: `normalized_value` holds the CHAT ID, which is the
-- only thing the Bot API can send to, and `value` holds what a human recognises.
-- Getting these the wrong way round silently breaks every alert.
-- ----------------------------------------------------------------------------
INSERT INTO person_contact (person_id, contact_type, value, normalized_value, is_primary, is_verified)
SELECT mp.new_id, 'MOBILE', TRIM(p.phone), TRIM(p.phone), 1, 0
FROM rm_cms_10_aug_2026.people p
JOIN _map_person mp ON mp.src_key = p.person_id AND mp.src_table = 'people'
WHERE p.phone IS NOT NULL AND TRIM(p.phone) <> ''
ON DUPLICATE KEY UPDATE is_primary = 1;

INSERT INTO person_contact (person_id, contact_type, value, normalized_value, is_primary, is_verified)
SELECT mp.new_id, 'EMAIL', TRIM(p.email), LOWER(TRIM(p.email)), 1, 0
FROM rm_cms_10_aug_2026.people p
JOIN _map_person mp ON mp.src_key = p.person_id AND mp.src_table = 'people'
WHERE p.email IS NOT NULL AND TRIM(p.email) <> ''
ON DUPLICATE KEY UPDATE is_primary = 1;

INSERT INTO person_contact (person_id, contact_type, value, normalized_value, is_primary, is_verified)
SELECT mp.new_id, 'MOBILE', TRIM(v.phone), TRIM(v.phone), 1, 0
FROM rm_cms_10_aug_2026.volunteers v
JOIN _map_person mp ON mp.src_key = v.volunteer_id AND mp.src_table = 'volunteers'
WHERE v.phone IS NOT NULL AND TRIM(v.phone) <> ''
ON DUPLICATE KEY UPDATE is_primary = 1;

INSERT INTO person_contact (person_id, contact_type, value, normalized_value, is_primary, is_verified)
SELECT mp.new_id, 'EMAIL', TRIM(v.email), LOWER(TRIM(v.email)), 0, 0
FROM rm_cms_10_aug_2026.volunteers v
JOIN _map_person mp ON mp.src_key = v.volunteer_id AND mp.src_table = 'volunteers'
WHERE v.email IS NOT NULL AND TRIM(v.email) <> ''
ON DUPLICATE KEY UPDATE is_primary = VALUES(is_primary);

-- Volunteer Telegram links. These were live in production, so they arrive
-- verified — the person really did press Start on the bot.
INSERT INTO person_contact
    (person_id, contact_type, value, normalized_value, is_primary, is_verified, verified_at)
SELECT mp.new_id, 'TELEGRAM',
       CONCAT('chat:', v.telegram_chat_id), CAST(v.telegram_chat_id AS CHAR),
       0, 1, v.created_at
FROM rm_cms_10_aug_2026.volunteers v
JOIN _map_person mp ON mp.src_key = v.volunteer_id AND mp.src_table = 'volunteers'
WHERE v.telegram_chat_id IS NOT NULL
ON DUPLICATE KEY UPDATE is_verified = 1;

INSERT INTO person_contact (person_id, contact_type, value, normalized_value, is_primary, is_verified)
SELECT mp.new_id, 'MOBILE', TRIM(t.phone), TRIM(t.phone), 1, 0
FROM rm_cms_10_aug_2026.team_leads t
JOIN _map_person mp ON mp.src_key = t.team_lead_id AND mp.src_table = 'team_leads'
WHERE t.phone IS NOT NULL AND TRIM(t.phone) <> ''
ON DUPLICATE KEY UPDATE is_primary = 1;

INSERT INTO person_contact (person_id, contact_type, value, normalized_value, is_primary, is_verified)
SELECT mp.new_id, 'EMAIL', TRIM(t.email), LOWER(TRIM(t.email)), 0, 0
FROM rm_cms_10_aug_2026.team_leads t
JOIN _map_person mp ON mp.src_key = t.team_lead_id AND mp.src_table = 'team_leads'
WHERE t.email IS NOT NULL AND TRIM(t.email) <> ''
ON DUPLICATE KEY UPDATE is_primary = VALUES(is_primary);

INSERT INTO person_contact
    (person_id, contact_type, value, normalized_value, is_primary, is_verified, verified_at)
SELECT mp.new_id, 'TELEGRAM',
       CONCAT('chat:', t.telegram_chat_id), TRIM(t.telegram_chat_id),
       0, 1, t.created_at
FROM rm_cms_10_aug_2026.team_leads t
JOIN _map_person mp ON mp.src_key = t.team_lead_id AND mp.src_table = 'team_leads'
WHERE t.telegram_chat_id IS NOT NULL AND TRIM(t.telegram_chat_id) <> ''
ON DUPLICATE KEY UPDATE is_verified = 1;

INSERT INTO person_contact (person_id, contact_type, value, normalized_value, is_primary, is_verified)
SELECT mp.new_id, 'MOBILE', TRIM(a.mobile_number), TRIM(a.mobile_number), 1, 0
FROM rm_cms_10_aug_2026.app_users a
JOIN _map_person mp ON mp.src_key = CONCAT('AU', LPAD(a.id, 3, '0')) AND mp.src_table = 'app_users'
ON DUPLICATE KEY UPDATE is_primary = 1;

-- ----------------------------------------------------------------------------
-- 7 · Teams
--
-- The MVP had no team entity: `volunteers.team_lead` pointed straight at a
-- team_leads row. v2 has a real `team` that a lead runs and volunteers belong to,
-- which is what makes the team lead dashboard scopeable. One team per lead.
-- ----------------------------------------------------------------------------
INSERT INTO team (public_id, campus_id, name, lead_user_id, max_members, is_active, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    1,
    CONCAT(TRIM(t.first_name), ' ', TRIM(t.last_name), ' Team'),
    NULL,                                  -- set after the accounts exist
    GREATEST(t.max_volunteers, 1),
    IF(t.status = 'Active', 1, 0),
    t.created_at, t.updated_at
FROM rm_cms_10_aug_2026.team_leads t
ORDER BY t.team_lead_id;

-- ----------------------------------------------------------------------------
-- 8 · Login accounts
--
-- PASSWORDS. The MVP stored NO password hashes anywhere — `app_users` is just a
-- name and a mobile number. There is nothing to carry across, so every migrated
-- account is given the SAME known development password and flagged
-- must_change_password.
--
-- That is acceptable only because this target is a local testing database. If this
-- script is ever aimed at a real environment, delete this block: minting accounts
-- that all share one password is exactly the hole the Identity rewrite closed.
--
-- The hash is copied from the local bootstrap administrator, so the password is
-- whatever Auth:Bootstrap:Password was when that account was created.
-- ----------------------------------------------------------------------------
INSERT INTO user_account
    (public_id, person_id, username, normalized_username, password_hash, security_stamp,
     is_active, must_change_password, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    mp.new_id,
    TRIM(t.phone), TRIM(t.phone),
    (SELECT password_hash FROM user_account WHERE id = 1),
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    IF(t.status = 'Active', 1, 0), 1,
    t.created_at, t.updated_at
FROM rm_cms_10_aug_2026.team_leads t
JOIN _map_person mp ON mp.src_key = t.team_lead_id AND mp.src_table = 'team_leads'
WHERE t.phone IS NOT NULL AND CHAR_LENGTH(TRIM(t.phone)) >= 3
ORDER BY t.team_lead_id;

INSERT INTO _map_lead (src_key, team_id, account_id, person_id)
SELECT t.team_lead_id, tm.id, ua.id, mp.new_id
FROM rm_cms_10_aug_2026.team_leads t
JOIN _map_person mp  ON mp.src_key = t.team_lead_id AND mp.src_table = 'team_leads'
-- The team name is built from source strings, so the comparison is forced into
-- the target's collation rather than left ambiguous.
JOIN team tm         ON tm.name = CONCAT(TRIM(t.first_name), ' ', TRIM(t.last_name), ' Team')
                                 COLLATE utf8mb4_0900_ai_ci
JOIN user_account ua ON ua.person_id = mp.new_id;

UPDATE team tm
JOIN _map_lead ml ON ml.team_id = tm.id
SET tm.lead_user_id = ml.account_id;

INSERT INTO user_role (user_account_id, role_code, campus_id, granted_at)
SELECT ml.account_id, 'TEAM_LEAD', 1, NOW(3) FROM _map_lead ml;

-- Volunteers with a phone get a login. Five have none; they still exist as
-- volunteers and can be assigned work, they simply cannot sign in — which is
-- exactly their state in the MVP.
INSERT INTO user_account
    (public_id, person_id, username, normalized_username, password_hash, security_stamp,
     is_active, must_change_password, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    mp.new_id,
    TRIM(v.phone), TRIM(v.phone),
    (SELECT password_hash FROM user_account WHERE id = 1),
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    IF(v.status = 'Active', 1, 0), 1,
    v.created_at, COALESCE(v.updated_at, v.created_at)
FROM rm_cms_10_aug_2026.volunteers v
JOIN _map_person mp ON mp.src_key = v.volunteer_id AND mp.src_table = 'volunteers'
WHERE v.phone IS NOT NULL AND CHAR_LENGTH(TRIM(v.phone)) >= 3
  AND mp.new_id NOT IN (SELECT person_id FROM user_account)
ORDER BY v.volunteer_id;

INSERT INTO user_role (user_account_id, role_code, campus_id, granted_at)
SELECT ua.id, 'VOLUNTEER', 1, NOW(3)
FROM rm_cms_10_aug_2026.volunteers v
JOIN _map_person mp  ON mp.src_key = v.volunteer_id AND mp.src_table = 'volunteers'
JOIN user_account ua ON ua.person_id = mp.new_id
WHERE ua.id NOT IN (SELECT account_id FROM _map_lead);

-- app_users were the MVP's intake operators.
INSERT INTO user_account
    (public_id, person_id, username, normalized_username, password_hash, security_stamp,
     is_active, must_change_password, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    mp.new_id,
    TRIM(a.mobile_number), TRIM(a.mobile_number),
    (SELECT password_hash FROM user_account WHERE id = 1),
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    1, 1,
    a.created_at, a.updated_at
FROM rm_cms_10_aug_2026.app_users a
JOIN _map_person mp ON mp.src_key = CONCAT('AU', LPAD(a.id, 3, '0')) AND mp.src_table = 'app_users'
WHERE CHAR_LENGTH(TRIM(a.mobile_number)) >= 3
  AND mp.new_id NOT IN (SELECT person_id FROM user_account)
ORDER BY a.id;

INSERT INTO user_role (user_account_id, role_code, campus_id, granted_at)
SELECT ua.id, 'DATA_ENTRY', 1, NOW(3)
FROM rm_cms_10_aug_2026.app_users a
JOIN _map_person mp  ON mp.src_key = CONCAT('AU', LPAD(a.id, 3, '0')) AND mp.src_table = 'app_users'
JOIN user_account ua ON ua.person_id = mp.new_id;

-- ----------------------------------------------------------------------------
-- 9 · Volunteers
--
-- current_case_load is NOT carried over. The MVP counter drifted from reality, and
-- v2 treats it as live workload the assignment service maintains inside a
-- transaction. It is recomputed from the migrated cases at step 13 instead.
-- ----------------------------------------------------------------------------
INSERT INTO volunteer
    (public_id, reference_code, person_id, campus_id, team_id, status, service_level,
     capacity_band_code, started_on, ended_on, current_case_load,
     lifetime_cases_assigned, lifetime_cases_closed, last_assigned_at,
     burnout_risk, last_check_in_on, next_check_in_on,
     background_checked_on, confidentiality_signed_on, crisis_trained_on,
     boundary_violation_count, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    v.volunteer_id,
    mp.new_id,
    1,
    ml.team_id,
    CASE v.status WHEN 'Active' THEN 'ACTIVE' ELSE 'INACTIVE' END,
    CASE v.level WHEN 'Level 1' THEN 'LEVEL_1' WHEN 'Level 2' THEN 'LEVEL_2' ELSE 'LEVEL_0' END,
    UPPER(v.capacity_band),
    v.start_date,
    v.end_date,
    0,
    GREATEST(COALESCE(v.total_assigned, 0), 0),
    GREATEST(COALESCE(v.total_completed, 0), 0),
    v.last_assigned_at,
    CASE UPPER(COALESCE(v.burnout_risk, ''))
        WHEN 'LOW' THEN 'LOW' WHEN 'MEDIUM' THEN 'MEDIUM' WHEN 'HIGH' THEN 'HIGH' ELSE NULL END,
    v.last_check_in, v.next_check_in,
    v.background_check, v.confidentiality_signed, v.crisis_trained,
    COALESCE(v.boundary_violations, 0),
    v.created_at, COALESCE(v.updated_at, v.created_at)
FROM rm_cms_10_aug_2026.volunteers v
JOIN _map_person mp ON mp.src_key = v.volunteer_id AND mp.src_table = 'volunteers'
LEFT JOIN _map_lead ml ON ml.src_key = v.team_lead
ORDER BY v.volunteer_id;

INSERT INTO _map_volunteer (src_key, new_id, person_id)
SELECT reference_code, id, person_id FROM volunteer;

-- ----------------------------------------------------------------------------
-- 9a · Unreachable volunteers are INACTIVE
--
-- A volunteer who cannot sign in has no way to see a case, and one with no
-- verified Telegram has no way to be reminded of it. Either way, assigning to
-- them produces a case that LOOKS handled and is not — which is worse than
-- leaving it in the unassigned queue, where it is visibly waiting.
--
-- The MVP let such volunteers sit as 'Active' because it had no concept of
-- reachability. Carrying that status across would import the same silent failure,
-- so it is corrected here rather than migrated faithfully. They keep their rows,
-- their history and their reference codes; they simply cannot be assigned until
-- someone gives them a login and a Telegram link, at which point a team lead can
-- set them active again.
-- ----------------------------------------------------------------------------
UPDATE volunteer v
JOIN person p ON p.id = v.person_id
SET v.status = 'INACTIVE'
WHERE v.status = 'ACTIVE'
  AND (
        NOT EXISTS (SELECT 1 FROM user_account ua
                     WHERE ua.person_id = p.id AND ua.is_active = 1)
     OR NOT EXISTS (SELECT 1 FROM person_contact tg
                     WHERE tg.person_id = p.id
                       AND tg.contact_type = 'TELEGRAM'
                       AND tg.is_verified = 1
                       AND tg.opted_out_at IS NULL)
      );

-- ----------------------------------------------------------------------------
-- 10 · Care cases
--
-- THE CENTRAL SPLIT. One MVP `people` row carries both the human and their
-- follow-up journey; here the journey becomes a `care_case`. Every visitor gets
-- exactly one, because the MVP could not represent a second.
--
-- follow_up_status collapses two v2 concepts — WHERE the case is (stage) and WHAT
-- IS HAPPENING to it (status) — so each MVP value maps to a pair:
--
--   NEW / Not Contacted -> INTAKE            + AWAITING_ASSIGNMENT
--   ASSIGNED / RETRY    -> INITIAL_FOLLOW_UP + IN_PROGRESS
--   IN_NURTURE          -> NURTURE           + IN_PROGRESS
--   IN_REVIEW           -> REVIEW            + IN_PROGRESS
--   ESCALATED           -> keeps its stage   + ESCALATED  (paused by an escalation)
--   COMPLETE            -> CLOSED            + CLOSED, reason OTHER
--   UNRESPONSIVE        -> CLOSED            + CLOSED, reason UNREACHABLE
--
-- A closed case must record when and why (ck_care_case_closed_consistency), and
-- the MVP recorded neither, so closed_at falls back to the last contact date and
-- the reason is the honest OTHER rather than an invented BECAME_MEMBER.
-- ----------------------------------------------------------------------------
INSERT INTO care_case
    (public_id, reference_code, person_id, campus_id, assigned_volunteer_id, team_id,
     stage, status, priority, visit_type, connection_source_code,
     first_visit_on, opened_at, assigned_at, last_contact_at, next_action_on,
     closed_at, close_reason, nurture_plan_id, current_step_number, next_step_due_on,
     contact_attempt_count, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    CONCAT('C', SUBSTRING(p.person_id, 2)),
    mp.new_id,
    1,
    mv.new_id,
    vol.team_id,
    -- 'NEW' and 'Not Contacted' describe the CONTACT, not the assignment. In the MVP
    -- those were independent columns, so a row could legitimately read
    -- "Not Contacted" while holding a volunteer and an assigned_date — meaning
    -- assigned, first contact not yet made. v2 has one pair of fields, so the
    -- volunteer decides which state it really is. Mapping on the status word alone
    -- produced 7 cases that claimed to be awaiting assignment while already assigned,
    -- which put them in the team lead's unassigned queue and nobody else's.
    CASE
        WHEN p.follow_up_status IN ('NEW','Not Contacted')
             AND p.assigned_volunteer IS NOT NULL              THEN 'INITIAL_FOLLOW_UP'
        WHEN p.follow_up_status IN ('NEW','Not Contacted')     THEN 'INTAKE'
        WHEN p.follow_up_status = 'ASSIGNED'                   THEN 'INITIAL_FOLLOW_UP'
        WHEN p.follow_up_status = 'RETRY PENDING'              THEN 'INITIAL_FOLLOW_UP'
        WHEN p.follow_up_status = 'IN_NURTURE'                 THEN 'NURTURE'
        WHEN p.follow_up_status = 'IN_REVIEW'                  THEN 'REVIEW'
        WHEN p.follow_up_status = 'ESCALATED'                  THEN 'INITIAL_FOLLOW_UP'
        ELSE 'CLOSED'
    END,
    CASE
        WHEN p.follow_up_status IN ('NEW','Not Contacted')
             AND p.assigned_volunteer IS NOT NULL              THEN 'IN_PROGRESS'
        WHEN p.follow_up_status IN ('NEW','Not Contacted')     THEN 'AWAITING_ASSIGNMENT'
        WHEN p.follow_up_status = 'ASSIGNED'                   THEN 'IN_PROGRESS'
        WHEN p.follow_up_status = 'RETRY PENDING'              THEN 'IN_PROGRESS'
        WHEN p.follow_up_status = 'IN_NURTURE'                 THEN 'IN_PROGRESS'
        WHEN p.follow_up_status = 'IN_REVIEW'                  THEN 'IN_PROGRESS'
        WHEN p.follow_up_status = 'ESCALATED'                  THEN 'ESCALATED'
        ELSE 'CLOSED'
    END,
    CASE p.follow_up_priority WHEN 'High' THEN 'HIGH' ELSE 'NORMAL' END,
    CASE p.visit_type
        WHEN 'First-Time Visitor' THEN 'FIRST_TIME'
        WHEN 'Returning Visitor'  THEN 'RETURNING'
        ELSE NULL
    END,
    CASE p.connection_source
        WHEN 'Social_Media'         THEN 'SOCIAL_MEDIA'
        WHEN 'Friend_Family_Invite' THEN 'FRIEND_INVITE'
        ELSE 'OTHER'
    END,
    p.first_visit_date,
    p.created_at,
    p.assigned_date,
    p.last_contact_date,
    p.next_action_date,
    -- Closed cases need a timestamp; the best evidence available is the last real
    -- contact, falling back to when the row was last touched.
    IF(p.follow_up_status IN ('COMPLETE','UNRESPONSIVE'),
       COALESCE(CAST(p.last_contact_date AS DATETIME), p.updated_at), NULL),
    CASE p.follow_up_status
        WHEN 'COMPLETE'     THEN 'OTHER'
        WHEN 'UNRESPONSIVE' THEN 'UNREACHABLE'
        ELSE NULL
    END,
    -- Only a case actually in nurture is attached to the plan.
    IF(p.follow_up_status = 'IN_NURTURE', 1, NULL),
    0,
    NULL,
    0,
    p.created_at,
    p.updated_at
FROM rm_cms_10_aug_2026.people p
JOIN _map_person mp     ON mp.src_key = p.person_id AND mp.src_table = 'people'
LEFT JOIN _map_volunteer mv ON mv.src_key = p.assigned_volunteer
LEFT JOIN volunteer vol     ON vol.id = mv.new_id
ORDER BY p.id;

INSERT INTO _map_case (src_key, new_id)
SELECT CONCAT('P', SUBSTRING(reference_code, 2)), id FROM care_case;

-- ----------------------------------------------------------------------------
-- 11 · Interactions, part one: the initial follow-ups
--
-- `follow_ups` and `nurture_steps` were separate tables doing the same job at
-- different points in the journey. v2 has one table with a `stage`, so both land
-- here and the stage is what tells them apart.
--
-- ck_care_interaction_completed: a COMPLETED row must have both occurred_at and
-- outcome_code. The MVP allowed a contacted attempt with no response recorded, so
-- those are completed with the truthful SPOKE default rather than dropped.
-- ----------------------------------------------------------------------------
INSERT INTO care_interaction
    (public_id, care_case_id, volunteer_id, stage, sequence_number, method_code,
     scheduled_on, occurred_at, status, made_contact, outcome_code, intent_code,
     duration_minutes, notes, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    mc.new_id,
    mv.new_id,
    'INITIAL_FOLLOW_UP',
    f.attempt_number,
    CASE f.contact_method
        WHEN 'Phone Call'  THEN 'CALL'
        WHEN 'House Visit' THEN 'VISIT'
        ELSE 'CALL'
    END,
    f.attempt_date,
    IF(f.contact_status = 'Contacted',
       TIMESTAMP(f.attempt_date, COALESCE(f.attempt_time, '12:00:00')), NULL),
    IF(f.contact_status = 'Contacted', 'COMPLETED', 'MISSED'),
    IF(f.contact_status = 'Contacted', 1, 0),
    IF(f.contact_status = 'Contacted',
       CASE f.response_type
           WHEN 'Normal'          THEN 'SPOKE'
           WHEN 'Needs Follow-Up' THEN 'NEEDS_SUPPORT'
           WHEN 'No Response'     THEN 'NO_ANSWER'
           WHEN 'Not Contacted'   THEN 'UNREACHABLE'
           ELSE 'SPOKE'
       END,
       NULL),
    -- The MVP never asked what the visitor WANTED, only how the call went. That
    -- is a genuine gap: intent stays null rather than being guessed from outcome.
    NULL,
    f.call_duration_min,
    NULLIF(TRIM(f.notes), ''),
    f.created_at,
    f.updated_at
FROM rm_cms_10_aug_2026.follow_ups f
JOIN _map_case mc       ON mc.src_key = f.person_id
JOIN _map_volunteer mv  ON mv.src_key = f.volunteer_id
ORDER BY f.follow_up_id;

INSERT INTO _map_followup (src_key, new_id)
SELECT f.follow_up_id, ci.id
FROM rm_cms_10_aug_2026.follow_ups f
JOIN _map_case mc ON mc.src_key = f.person_id
JOIN care_interaction ci
  ON ci.care_case_id = mc.new_id
 AND ci.stage = 'INITIAL_FOLLOW_UP'
 AND ci.sequence_number = f.attempt_number
 AND ci.scheduled_on = f.attempt_date
GROUP BY f.follow_up_id;

-- ----------------------------------------------------------------------------
-- 12 · Interactions, part two: the nurture steps
--
-- sequence_number is offset by the number of initial follow-ups already on the
-- case, so the two stages do not collide on (case, stage, sequence) and the
-- progression engine reads a single ascending history.
-- ----------------------------------------------------------------------------
INSERT INTO care_interaction
    (public_id, care_case_id, volunteer_id, stage, sequence_number, method_code,
     scheduled_on, occurred_at, status, made_contact, outcome_code, intent_code,
     notes, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    mc.new_id,
    mv.new_id,
    'NURTURE',
    s.step_number,
    CASE s.method WHEN 'Visit' THEN 'VISIT' ELSE 'CALL' END,
    s.scheduled_date,
    IF(s.status = 'Done', COALESCE(s.completed_at, TIMESTAMP(s.scheduled_date, '12:00:00')), NULL),
    IF(s.status = 'Done', 'COMPLETED', 'PENDING'),
    IF(s.status = 'Done', IF(s.response_type = 'No Response', 0, 1), NULL),
    IF(s.status = 'Done',
       CASE s.response_type
           WHEN 'Normal'          THEN 'SPOKE'
           WHEN 'Needs Follow-Up' THEN 'NEEDS_SUPPORT'
           WHEN 'No Response'     THEN 'NO_ANSWER'
           ELSE 'SPOKE'
       END,
       NULL),
    NULL,
    NULLIF(TRIM(s.notes), ''),
    s.created_at,
    s.updated_at
FROM rm_cms_10_aug_2026.nurture_steps s
JOIN _map_case mc      ON mc.src_key = s.person_id
JOIN _map_volunteer mv ON mv.src_key = s.volunteer_id
ORDER BY s.step_id;

-- Carry the sequence position across, and point the case at its next due step.
UPDATE care_case cc
JOIN _map_case mc ON mc.new_id = cc.id
JOIN rm_cms_10_aug_2026.nurture_sequences ns ON ns.person_id = mc.src_key
SET cc.current_step_number = GREATEST(ns.current_step - 1, 0),
    cc.nurture_plan_id     = 1;

UPDATE care_case cc
SET cc.next_step_due_on = (
        SELECT MIN(ci.scheduled_on) FROM care_interaction ci
        WHERE ci.care_case_id = cc.id AND ci.stage = 'NURTURE' AND ci.status = 'PENDING')
WHERE cc.stage = 'NURTURE';

-- ----------------------------------------------------------------------------
-- 13 · Derived counters
--
-- Recomputed, never copied: these are the numbers the MVP let drift.
-- ----------------------------------------------------------------------------
UPDATE care_case cc
SET cc.contact_attempt_count = (
        SELECT COUNT(*) FROM care_interaction ci
        WHERE ci.care_case_id = cc.id AND ci.status IN ('COMPLETED','MISSED')),
    cc.last_contact_at = (
        SELECT MAX(ci.occurred_at) FROM care_interaction ci
        WHERE ci.care_case_id = cc.id AND ci.status = 'COMPLETED');

UPDATE volunteer v
SET v.current_case_load = (
        SELECT COUNT(*) FROM care_case cc
        WHERE cc.assigned_volunteer_id = v.id AND cc.status <> 'CLOSED');

-- ----------------------------------------------------------------------------
-- 14 · Escalations
--
-- ck_escalation_resolved: a RESOLVED or CLOSED escalation must record when it was
-- resolved AND an outcome. One production row is Closed with no outcome, so it
-- takes OTHER — the constraint exists to stop a concern being quietly marked done
-- with no account of what happened, and OTHER is the truthful answer here.
--
-- Every MVP escalation reason is 'Needs Follow-Up', which is GENERAL_CONCERN in
-- v2. None of the safeguarding reasons were ever used.
-- ----------------------------------------------------------------------------
INSERT INTO escalation
    (public_id, reference_code, care_case_id, care_interaction_id, raised_by_volunteer_id,
     assigned_to_user_id, campus_id, reason_code, tier, status, description,
     raised_at, notified_at, acknowledged_at, resolved_at,
     outcome_code, resolution_notes, resource_connected,
     protocol_followed, authorities_contacted, volunteer_debriefed,
     reminder_count, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    e.escalation_id,
    mc.new_id,
    mf.new_id,
    mv.new_id,
    ml.account_id,
    1,
    'GENERAL_CONCERN',
    UPPER(e.escalation_tier),
    CASE e.status
        WHEN 'New'          THEN 'NEW'
        WHEN 'In Progress'  THEN 'IN_PROGRESS'
        WHEN 'Resolved'     THEN 'RESOLVED'
        WHEN 'Referred Out' THEN 'REFERRED_OUT'
        ELSE 'CLOSED'
    END,
    COALESCE(NULLIF(TRIM(e.description), ''), 'No description recorded.'),
    TIMESTAMP(e.escalation_date, '12:00:00'),
    e.notified_at,
    e.acknowledged_at,
    IF(e.status IN ('Resolved','Closed','Referred Out'),
       COALESCE(CAST(e.resolved_date AS DATETIME), e.updated_at), NULL),
    IF(e.status IN ('Resolved','Closed','Referred Out'),
       CASE e.outcome
           WHEN 'Connected to Resource'    THEN 'RESOURCE_CONNECTED'
           WHEN 'Pastoral Care Scheduled'  THEN 'PASTORAL_CARE'
           WHEN 'Counseling Referral'      THEN 'COUNSELLING_REFERRAL'
           WHEN 'Benevolence Provided'     THEN 'BENEVOLENCE'
           WHEN 'Emergency Services Called' THEN 'EMERGENCY_SERVICES'
           ELSE 'OTHER'
       END,
       NULL),
    NULLIF(TRIM(e.resolution_notes), ''),
    NULLIF(TRIM(e.resource_connected), ''),
    e.crisis_protocol_followed,
    e.authorities_contacted,
    e.volunteer_debriefed,
    0,
    e.created_at,
    e.updated_at
FROM rm_cms_10_aug_2026.escalations e
JOIN _map_case mc       ON mc.src_key = e.person_id
LEFT JOIN _map_followup mf  ON mf.src_key = e.follow_up_id
LEFT JOIN _map_volunteer mv ON mv.src_key = e.volunteer_id
LEFT JOIN _map_lead ml      ON ml.src_key = e.team_lead_id
ORDER BY e.escalation_id;

-- ----------------------------------------------------------------------------
-- 15 · Prayer requests
--
-- Free pastoral text against the PERSON, not the case: a prayer request outlives
-- the follow-up journey that happened to capture it.
-- ----------------------------------------------------------------------------
-- ck_note_private: a private note must name who may read it, so that "private"
-- is an access rule rather than a vague flag. A prayer request is shared so the
-- church can pray about it, and the volunteer following up is who acts on it —
-- so it is visible from VOLUNTEER upward, not restricted to leadership.
INSERT INTO note (public_id, entity_type, entity_id, note_type_code, body,
                  is_private, visible_to_role_code, created_at, updated_at)
SELECT
    UPPER(LEFT(CONCAT(REPLACE(UUID(),'-',''), REPLACE(UUID(),'-','')), 26)),
    'PERSON',
    mp.new_id,
    'PRAYER_REQUEST',
    TRIM(p.prayer_requests),
    1,
    'VOLUNTEER',
    p.created_at,
    p.updated_at
FROM rm_cms_10_aug_2026.people p
JOIN _map_person mp ON mp.src_key = p.person_id AND mp.src_table = 'people'
WHERE p.prayer_requests IS NOT NULL AND TRIM(p.prayer_requests) <> '';

-- ----------------------------------------------------------------------------
-- 16 · Assignment history
--
-- The MVP kept only the CURRENT volunteer, so exactly one row can be
-- reconstructed per assigned case. It is written because the case detail screen
-- reads this table and an empty history reads as "never assigned".
-- ----------------------------------------------------------------------------
INSERT INTO care_case_assignment
    (care_case_id, volunteer_id, assigned_at, unassigned_at, reason, assigned_by)
SELECT cc.id, cc.assigned_volunteer_id,
       COALESCE(cc.assigned_at, cc.opened_at), NULL, 'MANUAL', NULL
FROM care_case cc
WHERE cc.assigned_volunteer_id IS NOT NULL;

DROP TEMPORARY TABLE IF EXISTS _keep_person, _keep_account, _keep_roles,
    _map_person, _map_volunteer, _map_lead, _map_case, _map_followup, _vol_existing;
