-- =============================================================================
-- RM_CMS — PRODUCTION DATABASE SCHEMA
--
-- Authoritative source of truth. Target: MySQL 8.0.16+ (CHECK constraints are
-- enforced from 8.0.16; developed and validated against MySQL 9.1).
--
-- This is a ground-up redesign. The MVP schema at
-- Database/SQL_Scripts/schema.sql is retained as a historical reference only —
-- this file is NOT backward compatible with it and is not a migration.
--
-- -----------------------------------------------------------------------------
-- CONVENTIONS (full rationale in Database/Schema/CONVENTIONS.md)
-- -----------------------------------------------------------------------------
--  Naming      snake_case; SINGULAR table names; a table is a set of one entity.
--              Foreign keys are <referenced_table>_id.
--              Indexes ix_*, unique ux_*, foreign keys fk_*, checks ck_*.
--
--  Keys        Every table has `id BIGINT UNSIGNED AUTO_INCREMENT` as its internal
--              primary key — compact, sequential, ideal for InnoDB's clustered index.
--
--              Every table the API exposes also has `public_id CHAR(26)`: a ULID,
--              which is what URLs and API payloads use. Internal ids are NEVER
--              exposed. This closes the enumeration hole in the MVP, where
--              sequential 'V001', 'P002' ids in URLs let any caller walk the
--              entire dataset.
--
--              Human-facing codes ('V001') survive as `reference_code` where staff
--              genuinely use them in conversation — display data, never a key.
--
--  Time        All instants are UTC in DATETIME(3). TIMESTAMP is avoided: it is
--              limited to 2038 and silently converts using the session timezone,
--              which produces wrong values when the app and DB disagree.
--              Column names ending _at are instants; _on are calendar dates.
--
--  Deletion    Hard delete by default. Soft delete (`deleted_at`) only where a row
--              must remain referenceable after removal — see each table's note.
--              State is modelled with explicit status columns, not by deleting.
--
--  Audit       created_at/created_by and updated_at/updated_by on every mutable
--              table. Append-only logs carry created_at only.
--
--  Concurrency `row_version` on rows that concurrent users can edit. Callers must
--              include it in the WHERE clause of UPDATEs and treat 0 affected rows
--              as a conflict.
--
--  Lookups     A lookup `code` is an IMMUTABLE identifier: you change its `label`,
--              never its `code`. Lookup foreign keys therefore carry no
--              ON UPDATE CASCADE. This is also a hard MySQL requirement — a column
--              used in a foreign key with a referential action cannot appear in a
--              CHECK constraint, and several business invariants here depend on
--              checking those columns.
--
--  Enums       MySQL ENUM is not used anywhere. Adding a value to an ENUM rewrites
--              the table and cannot be done by an administrator.
--                * Business classifications an admin may extend  -> lookup table + FK
--                * Internal state machines the code branches on  -> VARCHAR + CHECK
--              The distinction matters: renaming a state an admin can edit would
--              silently break code that compares against it.
--
--  Secrets     No credential, token or API key is ever stored in plaintext.
--              Passwords are PBKDF2 hashes; refresh tokens are SHA-256 hashes.
--              Third-party secrets (Telegram bot token) live in environment
--              variables, NOT in app_setting.
--
--  Scoping     campus_id is the security/tenancy boundary and is denormalised onto
--              the tables that are filtered by it, so scoped queries never need a
--              join to enforce access.
--
-- CONTAINS NO PERSONAL DATA AND NO SECRETS.
-- =============================================================================

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;


-- #############################################################################
-- 1. REFERENCE DATA
--
--    Small, slow-changing, administrator-maintainable vocabularies.
--    Each has a stable `code` used by the application and a `label` shown to
--    users, so renaming a label never breaks logic.
-- #############################################################################

-- The tenancy / security boundary. Volunteers serve one campus; assignment,
-- reporting and access scoping all key off this.
CREATE TABLE campus (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id       CHAR(26)        NOT NULL,
    code            VARCHAR(20)     NOT NULL,
    name            VARCHAR(100)    NOT NULL,
    timezone        VARCHAR(64)     NOT NULL DEFAULT 'Asia/Kolkata',
    is_active       TINYINT(1)      NOT NULL DEFAULT 1,

    created_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by      BIGINT UNSIGNED NULL,
    updated_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by      BIGINT UNSIGNED NULL,
    row_version     INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_campus_public_id (public_id),
    UNIQUE KEY ux_campus_code      (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- How a volunteer reached (or tried to reach) someone.
CREATE TABLE contact_method (
    code            VARCHAR(30)  NOT NULL,
    label           VARCHAR(80)  NOT NULL,
    sort_order      SMALLINT     NOT NULL DEFAULT 0,
    is_active       TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Where a visitor came from.
CREATE TABLE connection_source (
    code            VARCHAR(30)  NOT NULL,
    label           VARCHAR(80)  NOT NULL,
    sort_order      SMALLINT     NOT NULL DEFAULT 0,
    is_active       TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- The outcome of a contact attempt.
--
-- The behaviour flags make routing DATA-DRIVEN. In the MVP this logic was a
-- hard-coded C# switch on lower-cased strings ("needs follow-up" / "needs
-- followup" / "crisis" ...), so a wording change in the UI silently produced
-- "Unknown response type". Here the effect of an outcome is a property of the
-- outcome itself.
CREATE TABLE care_outcome (
    code               VARCHAR(30)  NOT NULL,
    label              VARCHAR(80)  NOT NULL,

    -- Did we actually speak to them? Drives the retry counter.
    contact_made       TINYINT(1)   NOT NULL DEFAULT 1,

    -- These two depend ONLY on how the contact went, so they belong here.
    opens_escalation   TINYINT(1)   NOT NULL DEFAULT 0,
    schedules_retry    TINYINT(1)   NOT NULL DEFAULT 0,
    default_tier       VARCHAR(20)  NULL,

    sort_order         SMALLINT     NOT NULL DEFAULT 0,
    is_active          TINYINT(1)   NOT NULL DEFAULT 1,

    PRIMARY KEY (code),
    CONSTRAINT ck_care_outcome_tier
        CHECK (default_tier IS NULL OR default_tier IN ('STANDARD','URGENT','EMERGENCY'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
-- NOTE: whether an outcome leads to nurture is deliberately NOT a column here.
-- That decision also depends on what the visitor WANTS, which is a separate axis —
-- see visitor_intent and nurture_entry_rule below.


-- What the visitor wants to happen next.
--
-- This axis is the fix for the real-world case the MVP could not express: a call
-- that goes perfectly well ("Normal") where the visitor says "I already belong to
-- another church, please don't call again". Outcome and intent are orthogonal, and
-- collapsing them into one `response_type` forced that person into a 7-step nurture
-- sequence they had explicitly declined.
CREATE TABLE visitor_intent (
    code                VARCHAR(30)  NOT NULL,
    label               VARCHAR(120) NOT NULL,

    -- TRUE for intents that are an explicit request to stop. The service layer sets
    -- person.do_not_contact when an interaction records one of these, so the block
    -- follows the PERSON and survives being re-recorded as a new visitor later.
    implies_do_not_contact TINYINT(1) NOT NULL DEFAULT 0,

    sort_order          SMALLINT     NOT NULL DEFAULT 0,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- =====================================================================
-- NURTURE PLAN — the admin-defined shape of a nurture sequence.
--
-- The MVP hard-coded "7 steps, Call/Visit alternating, 7 days apart" and created
-- every step upfront the moment a sequence started. That committed to a schedule
-- three weeks in advance and could not react to what actually happened.
--
-- Here the plan is DATA, and steps are created ONE AT A TIME by the scheduled job
-- after the previous one completes and its outcome is evaluated.
-- =====================================================================
CREATE TABLE nurture_plan (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id           CHAR(26)        NOT NULL,
    name                VARCHAR(120)    NOT NULL,
    -- NULL applies to every campus.
    campus_id           BIGINT UNSIGNED NULL,

    -- Whether progression consults the previous outcome at all. When 0, the job
    -- advances to the next step regardless of how the last contact went.
    evaluate_outcome    TINYINT(1)      NOT NULL DEFAULT 1,

    -- Who runs the next step:
    --   SAME_VOLUNTEER       always the volunteer already on the case
    --   SAME_IF_AVAILABLE    keep them if active and under capacity, else reassign
    --   REASSIGN_LEAST_LOADED always pick the least-loaded eligible volunteer
    assignment_mode     VARCHAR(30)     NOT NULL DEFAULT 'SAME_IF_AVAILABLE',

    -- Fallback gap when a step does not define its own.
    default_gap_days    SMALLINT UNSIGNED NOT NULL DEFAULT 7,
    -- Grace period after which an untouched step is marked MISSED.
    missed_after_days   SMALLINT UNSIGNED NOT NULL DEFAULT 3,

    is_default          TINYINT(1)      NOT NULL DEFAULT 0,
    is_active           TINYINT(1)      NOT NULL DEFAULT 1,

    created_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by          BIGINT UNSIGNED NULL,
    row_version         INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_nurture_plan_public_id (public_id),
    UNIQUE KEY ux_nurture_plan_name      (name),
    KEY ix_nurture_plan_active (is_active, is_default),

    CONSTRAINT fk_nurture_plan_campus FOREIGN KEY (campus_id) REFERENCES campus (id),
    CONSTRAINT ck_nurture_plan_mode CHECK (
        assignment_mode IN ('SAME_VOLUNTEER','SAME_IF_AVAILABLE','REASSIGN_LEAST_LOADED')
    ),
    CONSTRAINT ck_nurture_plan_gap CHECK (default_gap_days BETWEEN 1 AND 180)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- One step in a plan. `gap_days` is the wait AFTER the previous step completes
-- before this one becomes due — the admin-controlled interval.
CREATE TABLE nurture_plan_step (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    nurture_plan_id     BIGINT UNSIGNED NOT NULL,
    step_number         SMALLINT UNSIGNED NOT NULL,
    method_code         VARCHAR(30)     NULL,       -- NULL lets the volunteer choose
    gap_days            SMALLINT UNSIGNED NULL,     -- NULL uses the plan default
    label               VARCHAR(120)    NULL,
    guidance            TEXT            NULL,       -- what to cover on this contact
    is_active           TINYINT(1)      NOT NULL DEFAULT 1,

    created_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),

    PRIMARY KEY (id),
    UNIQUE KEY ux_nurture_plan_step (nurture_plan_id, step_number),

    CONSTRAINT fk_nurture_step_plan   FOREIGN KEY (nurture_plan_id) REFERENCES nurture_plan (id) ON DELETE CASCADE,
    CONSTRAINT fk_nurture_step_method FOREIGN KEY (method_code)     REFERENCES contact_method (code),
    CONSTRAINT ck_nurture_step_number CHECK (step_number BETWEEN 1 AND 100),
    CONSTRAINT ck_nurture_step_gap    CHECK (gap_days IS NULL OR gap_days BETWEEN 0 AND 180)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- =====================================================================
-- CARE PROGRESSION RULES — the admin control surface for "what happens next".
--
-- Evaluated by the scheduled job every time an interaction completes, for BOTH
-- the initial follow-up and each nurture step. Each row matches on any subset of
-- (stage, step number, outcome, intent); NULL means "any".
--
-- RESOLUTION: rules are evaluated in `priority` order, lowest first, and the FIRST
-- match wins. Specificity is only a tie-breaker between equal priorities.
--
--   ORDER BY priority ASC,
--            (from_stage IS NOT NULL) + (from_step_number IS NOT NULL)
--          + (outcome_code IS NOT NULL) + (intent_code IS NOT NULL) DESC
--   LIMIT 1
--
-- Priority-first is deliberate. Ranking by specificity instead meant a narrow rule
-- like (NURTURE, SPOKE, any) outranked (any, any, NOT_INTERESTED), so a visitor who
-- said "not interested" mid-sequence kept receiving nurture calls. An explicit
-- refusal has to win no matter how precisely another rule matches.
--
-- For an administrator this is also the simpler mental model: the list is ordered,
-- and the first rule that fits is the one that applies.
--
-- Anything unmatched falls through to MANUAL_REVIEW, so an unforeseen combination is
-- queued for a human rather than silently mis-routed.
--
-- This is what makes the sequence dynamic: "not interested" stops it immediately,
-- a crisis escalates, and everything else continues on the admin-defined cadence —
-- all without a code change.
-- =====================================================================
CREATE TABLE care_progression_rule (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id           CHAR(26)        NOT NULL,

    -- Matching criteria. NULL = any.
    from_stage          VARCHAR(20)     NULL,       -- INITIAL_FOLLOW_UP | NURTURE
    from_step_number    SMALLINT UNSIGNED NULL,     -- which nurture step just finished
    outcome_code        VARCHAR(30)     NULL,
    intent_code         VARCHAR(30)     NULL,

    action              VARCHAR(30)     NOT NULL,
    -- For JUMP_TO_STEP: which step to go to instead of the next one.
    jump_to_step        SMALLINT UNSIGNED NULL,
    -- For CLOSE_CASE.
    close_reason        VARCHAR(30)     NULL,
    -- Optional override of the plan gap for the step this rule schedules.
    override_gap_days   SMALLINT UNSIGNED NULL,

    priority            SMALLINT        NOT NULL DEFAULT 100,
    is_active           TINYINT(1)      NOT NULL DEFAULT 1,
    description         VARCHAR(255)    NULL,

    created_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by          BIGINT UNSIGNED NULL,
    row_version         INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_progression_rule_public_id (public_id),
    -- NULL is distinct in a plain UNIQUE index, so the criteria are coalesced to
    -- sentinels. Without this, two contradictory catch-all rules could coexist.
    UNIQUE KEY ux_progression_rule_combo (
        (COALESCE(from_stage, '*')),
        (COALESCE(from_step_number, 0)),
        (COALESCE(outcome_code, '*')),
        (COALESCE(intent_code, '*'))
    ),
    KEY ix_progression_rule_lookup (is_active, priority),

    CONSTRAINT fk_progression_rule_outcome FOREIGN KEY (outcome_code) REFERENCES care_outcome (code),
    CONSTRAINT fk_progression_rule_intent  FOREIGN KEY (intent_code)  REFERENCES visitor_intent (code),

    CONSTRAINT ck_progression_rule_stage CHECK (
        from_stage IS NULL OR from_stage IN ('INITIAL_FOLLOW_UP','NURTURE')
    ),
    CONSTRAINT ck_progression_rule_action CHECK (
        action IN ('START_NURTURE','CONTINUE_NURTURE','JUMP_TO_STEP','SCHEDULE_RETRY',
                   'ESCALATE','SEND_TO_REVIEW','CLOSE_CASE','MANUAL_REVIEW')
    ),
    CONSTRAINT ck_progression_rule_close CHECK (
        action <> 'CLOSE_CASE' OR close_reason IS NOT NULL
    ),
    CONSTRAINT ck_progression_rule_jump CHECK (
        action <> 'JUMP_TO_STEP' OR jump_to_step IS NOT NULL
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Why an escalation was raised. Pastoral vocabulary — expected to evolve, hence
-- a table rather than the MVP's ENUM.
CREATE TABLE escalation_reason (
    code               VARCHAR(40)  NOT NULL,
    label              VARCHAR(120) NOT NULL,
    default_tier       VARCHAR(20)  NOT NULL DEFAULT 'STANDARD',
    -- Safeguarding cases requiring a documented protocol and, potentially,
    -- statutory reporting.
    requires_protocol  TINYINT(1)   NOT NULL DEFAULT 0,
    sort_order         SMALLINT     NOT NULL DEFAULT 0,
    is_active          TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (code),
    CONSTRAINT ck_escalation_reason_tier
        CHECK (default_tier IN ('STANDARD','URGENT','EMERGENCY'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- How an escalation was ultimately resolved.
CREATE TABLE escalation_outcome (
    code            VARCHAR(40)  NOT NULL,
    label           VARCHAR(120) NOT NULL,
    sort_order      SMALLINT     NOT NULL DEFAULT 0,
    is_active       TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Volunteer workload tiers.
--
-- In the MVP these existed BOTH as a `capacity_bands` table and as
-- limited_min / balanced_max / ... keys in system_config — two sources of truth
-- that could silently disagree. This table is now the only one.
CREATE TABLE capacity_band (
    code            VARCHAR(20)  NOT NULL,
    label           VARCHAR(80)  NOT NULL,
    min_per_week    SMALLINT UNSIGNED NOT NULL,
    max_per_week    SMALLINT UNSIGNED NOT NULL,
    description     VARCHAR(255) NULL,
    sort_order      SMALLINT     NOT NULL DEFAULT 0,
    is_active       TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (code),
    CONSTRAINT ck_capacity_band_range CHECK (max_per_week >= min_per_week)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- #############################################################################
-- 2. IDENTITY AND ACCESS
--
--    The MVP stored a human's name, email and phone in FIVE places: people,
--    volunteers, team_leads, app_users and users. A volunteer who was also a
--    visitor existed twice with no link, and updating a phone number in one
--    place left the others stale.
--
--    Here `person` is the single canonical record for a human being. Everything
--    else — being a visitor, being a volunteer, having a login — is a ROLE that
--    person holds, modelled as a separate row that references them.
-- #############################################################################

CREATE TABLE person (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id           CHAR(26)        NOT NULL,
    -- Human-readable handle used verbally by staff ('P0001'). Display only.
    reference_code      VARCHAR(20)     NULL,

    campus_id           BIGINT UNSIGNED NULL,

    given_name          VARCHAR(80)     NOT NULL,
    family_name         VARCHAR(80)     NULL,
    -- Generated so searching and sorting by full name needs no expression index.
    full_name           VARCHAR(161)    AS (
                            TRIM(CONCAT(given_name, ' ', COALESCE(family_name, '')))
                        ) STORED,

    date_of_birth       DATE            NULL,
    -- Coarse demographic band, used when an exact DOB is not collected.
    age_band            VARCHAR(20)     NULL,
    gender              VARCHAR(20)     NULL,
    household_type      VARCHAR(40)     NULL,

    address_line        VARCHAR(200)    NULL,
    locality            VARCHAR(100)    NULL,
    postal_code         VARCHAR(20)     NULL,
    -- Whether this person lives near enough for in-person visits. Drives whether
    -- a nurture step can be a Visit or must be a Call.
    is_local            TINYINT(1)      NOT NULL DEFAULT 1,

    -- Where this person stands with the church. A visitor becomes a MEMBER when
    -- their care case closes as BECAME_MEMBER. The members list is a query on this.
    lifecycle_status    VARCHAR(20)     NOT NULL DEFAULT 'VISITOR',
    became_member_on    DATE            NULL,

    -- CONSENT. Set when a person asks not to be contacted again. This is a
    -- property of the PERSON, not of one case: if they are recorded again later
    -- as a new visitor, the block must still apply. No care case may be opened
    -- and no notification sent while this is set.
    do_not_contact      TINYINT(1)      NOT NULL DEFAULT 0,
    do_not_contact_at   DATETIME(3)     NULL,
    do_not_contact_note VARCHAR(255)    NULL,

    notes               TEXT            NULL,

    -- Soft delete: person rows are referenced by care history, escalations and
    -- attendance that must remain readable after a person is removed from active
    -- use. Hard-deleting would either orphan or cascade-destroy pastoral records.
    deleted_at          DATETIME(3)     NULL,
    deleted_by          BIGINT UNSIGNED NULL,

    created_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by          BIGINT UNSIGNED NULL,
    row_version         INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_person_public_id      (public_id),
    UNIQUE KEY ux_person_reference_code (reference_code),
    KEY ix_person_campus     (campus_id),
    KEY ix_person_full_name  (full_name),
    KEY ix_person_deleted    (deleted_at),
    -- The members list, and the do-not-contact screen.
    KEY ix_person_lifecycle  (lifecycle_status, campus_id),
    KEY ix_person_dnc        (do_not_contact),

    CONSTRAINT fk_person_campus FOREIGN KEY (campus_id) REFERENCES campus (id),
    CONSTRAINT ck_person_age_band CHECK (
        age_band IS NULL OR age_band IN ('UNDER_18','18_25','26_35','36_45','46_60','OVER_60')
    ),
    CONSTRAINT ck_person_lifecycle CHECK (
        lifecycle_status IN ('VISITOR','MEMBER','LAPSED','MOVED_AWAY','DECEASED')
    ),
    CONSTRAINT ck_person_dnc CHECK (
        do_not_contact = 0 OR do_not_contact_at IS NOT NULL
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Contact points, normalised out of `person`.
--
-- The MVP had a single phone and email column per table, so a second number or a
-- spouse's contact had nowhere to go and ended up in free-text notes. Telegram
-- chat ids were a third variant, stored as bigint on volunteers and varchar on
-- team_leads — the same value with two different types.
CREATE TABLE person_contact (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    person_id       BIGINT UNSIGNED NOT NULL,

    contact_type    VARCHAR(20)     NOT NULL,  -- MOBILE | EMAIL | TELEGRAM | WHATSAPP | LANDLINE
    -- Raw value as entered/displayed.
    value           VARCHAR(255)    NOT NULL,
    -- Canonical form for matching: E.164 for phones, lower-cased for email.
    -- Deduplication and lookup use this, never `value`.
    normalized_value VARCHAR(255)   NOT NULL,

    is_primary      TINYINT(1)      NOT NULL DEFAULT 0,
    is_verified     TINYINT(1)      NOT NULL DEFAULT 0,
    verified_at     DATETIME(3)     NULL,
    -- Set when the person asks not to be contacted this way.
    opted_out_at    DATETIME(3)     NULL,

    created_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by      BIGINT UNSIGNED NULL,
    updated_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by      BIGINT UNSIGNED NULL,

    PRIMARY KEY (id),
    -- The same number cannot be recorded twice for one person.
    UNIQUE KEY ux_person_contact_value (person_id, contact_type, normalized_value),
    KEY ix_person_contact_lookup (contact_type, normalized_value),
    KEY ix_person_contact_person (person_id),

    CONSTRAINT fk_person_contact_person
        FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
    CONSTRAINT ck_person_contact_type CHECK (
        contact_type IN ('MOBILE','EMAIL','TELEGRAM','WHATSAPP','LANDLINE')
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Login credentials. Separate from `person` because not every person has a login
-- (most visitors never will) and because credential columns carry different
-- security and audit requirements from demographic data.
CREATE TABLE user_account (
    id                      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id               CHAR(26)        NOT NULL,
    person_id               BIGINT UNSIGNED NOT NULL,

    username                VARCHAR(100)    NOT NULL,
    -- Upper-invariant form; all lookups use this so casing can never split accounts.
    normalized_username     VARCHAR(100)    NOT NULL,

    -- PBKDF2-HMAC-SHA512 (ASP.NET Core Identity V3 format). Empty string means
    -- "no usable password" — an account provisioned but not yet activated.
    password_hash           VARCHAR(500)    NOT NULL DEFAULT '',
    -- Rotated on any credential or role change; checked on every request so a
    -- logout, password change or role change invalidates live access tokens
    -- immediately instead of waiting for them to expire.
    -- ULID, not a GUID in CHAR(36): MySqlConnector auto-converts CHAR(36) to
    -- System.Guid, which Dapper cannot map onto a string property.
    security_stamp          CHAR(26)        NOT NULL,
    token_version           INT UNSIGNED    NOT NULL DEFAULT 1,

    is_active               TINYINT(1)      NOT NULL DEFAULT 1,
    must_change_password    TINYINT(1)      NOT NULL DEFAULT 0,
    failed_access_count     SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    lockout_ends_at         DATETIME(3)     NULL,
    last_login_at           DATETIME(3)     NULL,
    password_changed_at     DATETIME(3)     NULL,

    -- Second factor, reserved. Secret is stored encrypted by the application.
    mfa_enabled             TINYINT(1)      NOT NULL DEFAULT 0,
    mfa_secret_encrypted    VARBINARY(512)  NULL,

    created_at              DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by              BIGINT UNSIGNED NULL,
    updated_at              DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by              BIGINT UNSIGNED NULL,
    row_version             INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_user_account_public_id (public_id),
    UNIQUE KEY ux_user_account_username  (normalized_username),
    -- One login per person. Shared logins destroy accountability in an audit trail.
    UNIQUE KEY ux_user_account_person    (person_id),
    KEY ix_user_account_active (is_active),

    CONSTRAINT fk_user_account_person FOREIGN KEY (person_id) REFERENCES person (id),
    CONSTRAINT ck_user_account_username_len CHECK (CHAR_LENGTH(username) >= 3)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
-- NOTE: no soft delete here. A disabled account (is_active = 0) keeps its unique
-- username, which soft delete would leave dangling and unusable for re-issue.


CREATE TABLE app_role (
    code            VARCHAR(30)  NOT NULL,
    label           VARCHAR(80)  NOT NULL,
    description     VARCHAR(255) NULL,
    -- Higher wins when resolving a user's landing page or effective privilege.
    -- Named hierarchy_level, not `rank`: RANK is a reserved word in MySQL 8+.
    hierarchy_level SMALLINT     NOT NULL DEFAULT 0,
    is_assignable   TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Role assignment, optionally scoped to a campus so a team lead can administer
-- one campus without gaining rights over another.
CREATE TABLE user_role (
    user_account_id BIGINT UNSIGNED NOT NULL,
    role_code       VARCHAR(30)     NOT NULL,
    campus_id       BIGINT UNSIGNED NULL,  -- NULL = organisation-wide

    granted_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    granted_by      BIGINT UNSIGNED NULL,

    PRIMARY KEY (user_account_id, role_code),
    KEY ix_user_role_role   (role_code),
    KEY ix_user_role_campus (campus_id),

    CONSTRAINT fk_user_role_user   FOREIGN KEY (user_account_id) REFERENCES user_account (id) ON DELETE CASCADE,
    CONSTRAINT fk_user_role_role   FOREIGN KEY (role_code)       REFERENCES app_role (code),
    CONSTRAINT fk_user_role_campus FOREIGN KEY (campus_id)       REFERENCES campus (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Refresh tokens. Only the SHA-256 hash is stored, so a database compromise
-- yields no usable tokens. `family_id` groups one login's rotation chain: if a
-- spent token is replayed, the whole family is revoked.
CREATE TABLE refresh_token (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id           CHAR(26)        NOT NULL,
    user_account_id     BIGINT UNSIGNED NOT NULL,
    family_id           CHAR(26)        NOT NULL,

    token_hash          CHAR(64)        NOT NULL,   -- SHA-256 hex of a 64-byte token
    parent_id           BIGINT UNSIGNED NULL,
    replaced_by_id      BIGINT UNSIGNED NULL,

    issued_at           DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    expires_at          DATETIME(3)     NOT NULL,
    revoked_at          DATETIME(3)     NULL,
    revoked_reason      VARCHAR(40)     NULL,

    device_label        VARCHAR(120)    NULL,
    user_agent_hash     CHAR(64)        NULL,
    issued_ip           VARBINARY(16)   NULL,       -- INET6_ATON; IPv4 and IPv6

    PRIMARY KEY (id),
    UNIQUE KEY ux_refresh_token_public_id (public_id),
    UNIQUE KEY ux_refresh_token_hash      (token_hash),
    KEY ix_refresh_token_user    (user_account_id),
    KEY ix_refresh_token_family  (family_id),
    KEY ix_refresh_token_expires (expires_at),

    CONSTRAINT fk_refresh_token_user FOREIGN KEY (user_account_id) REFERENCES user_account (id) ON DELETE CASCADE,
    CONSTRAINT ck_refresh_token_expiry CHECK (expires_at > issued_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


CREATE TABLE password_history (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    user_account_id BIGINT UNSIGNED NOT NULL,
    password_hash   VARCHAR(500)    NOT NULL,
    created_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (id),
    KEY ix_password_history_user (user_account_id, created_at DESC),
    CONSTRAINT fk_password_history_user
        FOREIGN KEY (user_account_id) REFERENCES user_account (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Security audit trail. Append-only; outcomes only, never a password or token.
--
-- Deliberately has NO foreign key on user_account_id: audit rows must survive the
-- deletion of the account they describe, which is precisely when they matter most.
CREATE TABLE security_event (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    occurred_at         DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),

    user_account_id     BIGINT UNSIGNED NULL,
    username_attempted  VARCHAR(100)    NULL,

    event_type          VARCHAR(40)     NOT NULL,
    succeeded           TINYINT(1)      NOT NULL DEFAULT 0,
    detail              VARCHAR(500)    NULL,

    ip_address          VARBINARY(16)   NULL,
    user_agent          VARCHAR(300)    NULL,
    -- VARCHAR, not CHAR(36): a caller may supply a GUID-shaped value in the
    -- X-Correlation-Id header, and CHAR(36) would be read back as System.Guid.
    correlation_id      VARCHAR(36)     NULL,

    PRIMARY KEY (id),
    KEY ix_security_event_user    (user_account_id, occurred_at),
    KEY ix_security_event_type    (event_type, occurred_at),
    KEY ix_security_event_time    (occurred_at),
    -- Supports brute-force detection across a username regardless of source IP.
    KEY ix_security_event_attempt (username_attempted, occurred_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- #############################################################################
-- 3. ORGANISATION
--
--    The MVP had no team entity: `volunteers.team_lead` pointed at a team_leads
--    row, so a team existed only implicitly and could not be renamed, retired or
--    handed to a new leader without rewriting every volunteer row.
-- #############################################################################

CREATE TABLE team (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id           CHAR(26)        NOT NULL,
    campus_id           BIGINT UNSIGNED NOT NULL,

    name                VARCHAR(120)    NOT NULL,
    -- The team lead, as a user account. Nullable so a team can outlive a
    -- departure without being deleted.
    lead_user_id        BIGINT UNSIGNED NULL,

    -- Span-of-control ceiling; differs for full-time vs player-coach leads.
    max_members         SMALLINT UNSIGNED NOT NULL DEFAULT 12,
    is_active           TINYINT(1)      NOT NULL DEFAULT 1,

    created_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by          BIGINT UNSIGNED NULL,
    row_version         INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_team_public_id   (public_id),
    UNIQUE KEY ux_team_campus_name (campus_id, name),
    KEY ix_team_lead   (lead_user_id),
    KEY ix_team_active (is_active),

    CONSTRAINT fk_team_campus FOREIGN KEY (campus_id)    REFERENCES campus (id),
    CONSTRAINT fk_team_lead   FOREIGN KEY (lead_user_id) REFERENCES user_account (id),
    CONSTRAINT ck_team_max_members CHECK (max_members BETWEEN 1 AND 100)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- The volunteer-specific facts about a person. One row per person who serves.
--
-- `current_case_load` is a maintained counter. The MVP kept the same figure in
-- volunteers.current_assignments AND derived it live from the people table, and
-- the two drifted. Here the counter is the single authority, maintained inside
-- the same transaction that opens or closes a case.
CREATE TABLE volunteer (
    id                      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id               CHAR(26)        NOT NULL,
    reference_code          VARCHAR(20)     NULL,       -- 'V001', display only

    person_id               BIGINT UNSIGNED NOT NULL,
    campus_id               BIGINT UNSIGNED NOT NULL,
    team_id                 BIGINT UNSIGNED NULL,

    status                  VARCHAR(20)     NOT NULL DEFAULT 'ACTIVE',
    service_level           VARCHAR(20)     NOT NULL DEFAULT 'LEVEL_0',
    capacity_band_code      VARCHAR(20)     NOT NULL,

    started_on              DATE            NOT NULL,
    ended_on                DATE            NULL,

    -- Live workload. Never exceeds the band's max_per_week; enforced by the
    -- assignment service inside a transaction.
    current_case_load       SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    lifetime_cases_assigned INT UNSIGNED    NOT NULL DEFAULT 0,
    lifetime_cases_closed   INT UNSIGNED    NOT NULL DEFAULT 0,
    last_assigned_at        DATETIME(3)     NULL,

    -- Wellbeing signals reviewed at check-ins.
    burnout_risk            VARCHAR(20)     NULL,
    last_check_in_on        DATE            NULL,
    next_check_in_on        DATE            NULL,

    -- Safeguarding gates. A volunteer must not receive a crisis case without
    -- crisis training and a current background check; enforced in the service layer.
    background_checked_on   DATE            NULL,
    confidentiality_signed_on DATE          NULL,
    crisis_trained_on       DATE            NULL,
    boundary_violation_count SMALLINT UNSIGNED NOT NULL DEFAULT 0,

    created_at              DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by              BIGINT UNSIGNED NULL,
    updated_at              DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by              BIGINT UNSIGNED NULL,
    row_version             INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_volunteer_public_id      (public_id),
    UNIQUE KEY ux_volunteer_reference_code (reference_code),
    UNIQUE KEY ux_volunteer_person         (person_id),
    KEY ix_volunteer_team   (team_id),
    KEY ix_volunteer_status (status),
    -- Covers the assignment picker: active volunteers at a campus, least loaded first.
    KEY ix_volunteer_assignment (campus_id, status, current_case_load),

    CONSTRAINT fk_volunteer_person   FOREIGN KEY (person_id)          REFERENCES person (id),
    CONSTRAINT fk_volunteer_campus   FOREIGN KEY (campus_id)          REFERENCES campus (id),
    CONSTRAINT fk_volunteer_team     FOREIGN KEY (team_id)            REFERENCES team (id),
    CONSTRAINT fk_volunteer_capacity FOREIGN KEY (capacity_band_code) REFERENCES capacity_band (code),

    CONSTRAINT ck_volunteer_status CHECK (
        status IN ('ACTIVE','PAUSED','ON_LEAVE','INACTIVE','EXITED')
    ),
    CONSTRAINT ck_volunteer_burnout CHECK (
        burnout_risk IS NULL OR burnout_risk IN ('LOW','MEDIUM','HIGH')
    ),
    CONSTRAINT ck_volunteer_dates CHECK (ended_on IS NULL OR ended_on >= started_on)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Audit of capacity band changes. Append-only.
CREATE TABLE volunteer_capacity_change (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    volunteer_id        BIGINT UNSIGNED NOT NULL,
    from_band_code      VARCHAR(20)     NULL,
    to_band_code        VARCHAR(20)     NOT NULL,
    reason              VARCHAR(120)    NULL,
    notes               TEXT            NULL,
    changed_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    changed_by          BIGINT UNSIGNED NULL,

    PRIMARY KEY (id),
    KEY ix_capacity_change_volunteer (volunteer_id, changed_at),
    CONSTRAINT fk_capacity_change_volunteer FOREIGN KEY (volunteer_id)   REFERENCES volunteer (id) ON DELETE CASCADE,
    CONSTRAINT fk_capacity_change_to_band   FOREIGN KEY (to_band_code)   REFERENCES capacity_band (code),
    CONSTRAINT fk_capacity_change_from_band FOREIGN KEY (from_band_code) REFERENCES capacity_band (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Recurring one-to-one between a team lead and a volunteer.
CREATE TABLE volunteer_check_in (
    id                      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id               CHAR(26)        NOT NULL,
    volunteer_id            BIGINT UNSIGNED NOT NULL,
    conducted_by            BIGINT UNSIGNED NOT NULL,   -- user_account of the team lead

    held_on                 DATE            NOT NULL,
    duration_minutes        SMALLINT UNSIGNED NULL,
    meeting_type            VARCHAR(20)     NOT NULL DEFAULT 'MONTHLY',

    emotional_tone          VARCHAR(20)     NULL,
    concerns                TEXT            NULL,
    training_needs          TEXT            NULL,
    action_items            TEXT            NULL,

    capacity_reviewed       TINYINT(1)      NOT NULL DEFAULT 0,
    boundary_issues_raised  TINYINT(1)      NOT NULL DEFAULT 0,
    follow_up_required      TINYINT(1)      NOT NULL DEFAULT 0,
    next_check_in_on        DATE            NULL,

    created_at              DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by              BIGINT UNSIGNED NULL,
    updated_at              DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by              BIGINT UNSIGNED NULL,

    PRIMARY KEY (id),
    UNIQUE KEY ux_check_in_public_id (public_id),
    KEY ix_check_in_volunteer (volunteer_id, held_on),
    KEY ix_check_in_conductor (conducted_by, held_on),

    CONSTRAINT fk_check_in_volunteer FOREIGN KEY (volunteer_id) REFERENCES volunteer (id),
    CONSTRAINT fk_check_in_conductor FOREIGN KEY (conducted_by) REFERENCES user_account (id),
    CONSTRAINT ck_check_in_tone CHECK (
        emotional_tone IS NULL OR emotional_tone IN ('GREEN','AMBER','RED')
    ),
    CONSTRAINT ck_check_in_type CHECK (
        meeting_type IN ('MONTHLY','AD_HOC','ONBOARDING','EXIT','WELFARE')
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Volunteer Net Promoter Score.
CREATE TABLE volunteer_survey (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id           CHAR(26)        NOT NULL,
    volunteer_id        BIGINT UNSIGNED NOT NULL,

    surveyed_on         DATE            NOT NULL,
    period_year         SMALLINT UNSIGNED NOT NULL,
    period_quarter      TINYINT UNSIGNED NOT NULL,
    score               TINYINT UNSIGNED NOT NULL,
    -- Derived from score; stored so historical categorisation survives a change
    -- in the banding rules.
    category            VARCHAR(20)     NULL,

    working_well        TEXT            NULL,
    could_improve       TEXT            NULL,
    additional_feedback TEXT            NULL,

    created_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by          BIGINT UNSIGNED NULL,

    PRIMARY KEY (id),
    UNIQUE KEY ux_survey_public_id (public_id),
    -- One response per volunteer per quarter.
    UNIQUE KEY ux_survey_period    (volunteer_id, period_year, period_quarter),
    KEY ix_survey_volunteer (volunteer_id, surveyed_on),

    CONSTRAINT fk_survey_volunteer FOREIGN KEY (volunteer_id) REFERENCES volunteer (id) ON DELETE CASCADE,
    CONSTRAINT ck_survey_score    CHECK (score BETWEEN 0 AND 10),
    CONSTRAINT ck_survey_quarter  CHECK (period_quarter BETWEEN 1 AND 4),
    CONSTRAINT ck_survey_category CHECK (
        category IS NULL OR category IN ('DETRACTOR','PASSIVE','PROMOTER')
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- #############################################################################
-- 4. CARE — the follow-up and nurture journey
--
--    The MVP put follow-up state directly on the person (people.follow_up_status,
--    assigned_volunteer, next_action_date). That makes a person's journey a
--    single, overwritable slot: someone who visits, goes quiet, and returns two
--    years later overwrites their own history, and there is no way to report on
--    "how many journeys did we run last year".
--
--    A `care_case` is one engagement journey. A person may have several over time.
--
--    LIFECYCLE
--      Intake -> the first follow-up is assigned to a volunteer.
--      Every subsequent nurture step is mechanically IDENTICAL to that first
--      follow-up; the only difference is that the scheduler assigns it, one at a
--      time, after the previous one completes.
--
--      If an outcome raises an escalation the case PAUSES: the scheduler creates
--      no further steps while an escalation is open. The team lead is alerted, the
--      pastor too if it goes unacknowledged past the configured threshold. Closing
--      the escalation resumes the sequence from where it stopped.
--
--      When the plan runs out of steps the case moves to REVIEW, where the team
--      lead reads the whole conversation history and its notes and decides the
--      outcome. That review is why `note` matters: a volunteer flags interest in
--      ministry for the team lead to act on, and a team lead explains a
--      reassignment to the incoming volunteer.
-- #############################################################################

CREATE TABLE care_case (
    id                      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id               CHAR(26)        NOT NULL,
    reference_code          VARCHAR(20)     NULL,

    person_id               BIGINT UNSIGNED NOT NULL,
    campus_id               BIGINT UNSIGNED NOT NULL,
    assigned_volunteer_id   BIGINT UNSIGNED NULL,
    team_id                 BIGINT UNSIGNED NULL,

    -- Where the journey is. Code branches on these, so they are a CHECK-constrained
    -- vocabulary rather than an admin-editable lookup.
    stage                   VARCHAR(20)     NOT NULL DEFAULT 'INTAKE',
    status                  VARCHAR(20)     NOT NULL DEFAULT 'OPEN',
    priority                VARCHAR(10)     NOT NULL DEFAULT 'NORMAL',

    -- Why this journey began.
    visit_type              VARCHAR(30)     NULL,
    connection_source_code  VARCHAR(30)     NULL,
    first_visit_on          DATE            NULL,

    opened_at               DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    assigned_at             DATETIME(3)     NULL,
    first_contact_at        DATETIME(3)     NULL,
    last_contact_at         DATETIME(3)     NULL,
    next_action_on          DATE            NULL,
    closed_at               DATETIME(3)     NULL,
    close_reason            VARCHAR(30)     NULL,
    close_notes             TEXT            NULL,

    -- Nurture position. Steps are created one at a time, so the case records
    -- where it is rather than holding a pre-built schedule.
    nurture_plan_id         BIGINT UNSIGNED NULL,
    current_step_number     SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    -- When the next step becomes due. The scheduled job picks cases up on this.
    next_step_due_on        DATE            NULL,
    -- Set when a progression rule resolved to MANUAL_REVIEW; a human must decide
    -- before the case can move again.
    awaiting_review_since   DATETIME(3)     NULL,

    -- Running counters, maintained with the interactions that cause them.
    contact_attempt_count   SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    consecutive_no_contact  SMALLINT UNSIGNED NOT NULL DEFAULT 0,

    created_at              DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by              BIGINT UNSIGNED NULL,
    updated_at              DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by              BIGINT UNSIGNED NULL,
    row_version             INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_care_case_public_id      (public_id),
    UNIQUE KEY ux_care_case_reference_code (reference_code),
    KEY ix_care_case_person    (person_id),
    KEY ix_care_case_volunteer (assigned_volunteer_id, status),
    KEY ix_care_case_team      (team_id, status),
    -- Covers the unassigned-intake queue the assignment job drains.
    KEY ix_care_case_queue     (campus_id, status, assigned_volunteer_id),
    -- Covers the overdue-action sweep.
    KEY ix_care_case_due       (status, next_action_on),
    -- Covers the scheduled job that creates the next nurture step: cases whose
    -- next step has come due.
    KEY ix_care_case_next_step (stage, next_step_due_on),
    KEY ix_care_case_review    (awaiting_review_since),

    CONSTRAINT fk_care_case_person    FOREIGN KEY (person_id)             REFERENCES person (id),
    CONSTRAINT fk_care_case_campus    FOREIGN KEY (campus_id)             REFERENCES campus (id),
    CONSTRAINT fk_care_case_volunteer FOREIGN KEY (assigned_volunteer_id) REFERENCES volunteer (id),
    CONSTRAINT fk_care_case_team      FOREIGN KEY (team_id)               REFERENCES team (id),
    CONSTRAINT fk_care_case_source    FOREIGN KEY (connection_source_code) REFERENCES connection_source (code),
    CONSTRAINT fk_care_case_plan      FOREIGN KEY (nurture_plan_id)        REFERENCES nurture_plan (id),

    CONSTRAINT ck_care_case_stage CHECK (
        stage IN ('INTAKE','INITIAL_FOLLOW_UP','NURTURE','REVIEW','CLOSED')
    ),
    CONSTRAINT ck_care_case_status CHECK (
        status IN ('OPEN','AWAITING_ASSIGNMENT','IN_PROGRESS','ESCALATED','ON_HOLD','CLOSED')
    ),
    CONSTRAINT ck_care_case_priority CHECK (priority IN ('LOW','NORMAL','HIGH','URGENT')),
    CONSTRAINT ck_care_case_close_reason CHECK (
        close_reason IS NULL OR close_reason IN
            ('BECAME_MEMBER','DECLINED','UNREACHABLE','MOVED_AWAY','DUPLICATE','REFERRED_OUT',
             'ALREADY_CHURCHED','DO_NOT_CONTACT','OTHER')
    ),
    -- A closed case must record when and why.
    CONSTRAINT ck_care_case_closed_consistency CHECK (
        (status <> 'CLOSED') OR (closed_at IS NOT NULL AND close_reason IS NOT NULL)
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Who has held this case, and when.
--
-- The assigned volunteer changes over the life of a case: the scheduler may hand
-- the next step to someone else under the plan's assignment_mode, and a team lead
-- reassigns manually when a volunteer goes inactive. care_case.assigned_volunteer_id
-- only holds the current holder, so the history lives here.
CREATE TABLE care_case_assignment (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    care_case_id    BIGINT UNSIGNED NOT NULL,
    volunteer_id    BIGINT UNSIGNED NOT NULL,

    assigned_at     DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    -- NULL means this is the current holder.
    unassigned_at   DATETIME(3)     NULL,

    -- Why the case moved. MANUAL and VOLUNTEER_INACTIVE are team-lead actions and
    -- should be accompanied by a note explaining it to the incoming volunteer.
    reason          VARCHAR(30)     NOT NULL DEFAULT 'AUTO',
    assigned_by     BIGINT UNSIGNED NULL,

    PRIMARY KEY (id),
    KEY ix_case_assignment_case      (care_case_id, assigned_at),
    KEY ix_case_assignment_volunteer (volunteer_id, unassigned_at),

    CONSTRAINT fk_case_assignment_case      FOREIGN KEY (care_case_id) REFERENCES care_case (id) ON DELETE CASCADE,
    CONSTRAINT fk_case_assignment_volunteer FOREIGN KEY (volunteer_id) REFERENCES volunteer (id),
    CONSTRAINT fk_case_assignment_by        FOREIGN KEY (assigned_by)  REFERENCES user_account (id),
    CONSTRAINT ck_case_assignment_reason CHECK (
        reason IN ('AUTO','MANUAL','REASSIGNED','VOLUNTEER_INACTIVE','CAPACITY','ESCALATION')
    ),
    CONSTRAINT ck_case_assignment_dates CHECK (
        unassigned_at IS NULL OR unassigned_at >= assigned_at
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Every planned or completed contact within a case.
--
-- This single table replaces the MVP's `follow_ups` AND `nurture_steps`, which
-- were near-duplicates: both recorded a method, a date, a contact status, a
-- response type and notes, and each needed its own DAL, DTOs and endpoints.
-- The only real difference — that nurture steps are scheduled in advance — is
-- expressed by `scheduled_on` being populated ahead of time.
CREATE TABLE care_interaction (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id           CHAR(26)        NOT NULL,

    care_case_id        BIGINT UNSIGNED NOT NULL,
    volunteer_id        BIGINT UNSIGNED NULL,       -- NULL if generated before assignment
    logged_by           BIGINT UNSIGNED NULL,       -- user_account that recorded it

    -- INITIAL_FOLLOW_UP interactions are created as they happen;
    -- NURTURE interactions are created up-front as a schedule.
    stage               VARCHAR(20)     NOT NULL,
    -- 1..n within the stage. Unique per case+stage.
    sequence_number     SMALLINT UNSIGNED NOT NULL,

    method_code         VARCHAR(30)     NULL,
    scheduled_on        DATE            NULL,
    occurred_at         DATETIME(3)     NULL,

    status              VARCHAR(20)     NOT NULL DEFAULT 'PENDING',
    -- Did we actually reach them?
    made_contact        TINYINT(1)      NULL,
    outcome_code        VARCHAR(30)     NULL,
    -- What the visitor wants next. Captured alongside the outcome; together they
    -- decide the next stage via nurture_entry_rule.
    intent_code         VARCHAR(30)     NULL,

    -- Team huddle: the lead's weekly verdict on the volunteer's escalation
    -- judgement. NULL means not yet assessed, which is what the huddle queue
    -- selects on. The note is kept because a verdict with no reasoning teaches
    -- nobody and cannot be referred back to at the volunteer's check-in.
    escalation_assessment VARCHAR(20)   NULL,
    assessment_note     VARCHAR(500)    NULL,
    assessed_by         BIGINT UNSIGNED NULL,
    assessed_at         DATETIME(3)     NULL,

    duration_minutes    SMALLINT UNSIGNED NULL,
    notes               TEXT            NULL,

    created_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by          BIGINT UNSIGNED NULL,
    row_version         INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    KEY ix_interaction_assessment (escalation_assessment, occurred_at),
    CONSTRAINT fk_interaction_assessor FOREIGN KEY (assessed_by) REFERENCES user_account (id),
    CONSTRAINT ck_interaction_assessment CHECK (
        escalation_assessment IS NULL OR
        escalation_assessment IN ('CORRECT','UNDER_ESCALATED','OVER_ESCALATED')
    ),
    UNIQUE KEY ux_care_interaction_public_id (public_id),
    UNIQUE KEY ux_care_interaction_sequence  (care_case_id, stage, sequence_number),
    KEY ix_care_interaction_case      (care_case_id, occurred_at),
    -- Covers "what is due for this volunteer" — the volunteer's daily work list.
    KEY ix_care_interaction_due       (volunteer_id, status, scheduled_on),
    KEY ix_care_interaction_outcome   (outcome_code),

    CONSTRAINT fk_care_interaction_case      FOREIGN KEY (care_case_id) REFERENCES care_case (id) ON DELETE CASCADE,
    CONSTRAINT fk_care_interaction_volunteer FOREIGN KEY (volunteer_id) REFERENCES volunteer (id),
    CONSTRAINT fk_care_interaction_logged_by FOREIGN KEY (logged_by)    REFERENCES user_account (id),
    CONSTRAINT fk_care_interaction_method    FOREIGN KEY (method_code)  REFERENCES contact_method (code),
    CONSTRAINT fk_care_interaction_outcome   FOREIGN KEY (outcome_code) REFERENCES care_outcome (code),
    CONSTRAINT fk_care_interaction_intent    FOREIGN KEY (intent_code)  REFERENCES visitor_intent (code),

    CONSTRAINT ck_care_interaction_stage CHECK (stage IN ('INITIAL_FOLLOW_UP','NURTURE','AD_HOC')),
    CONSTRAINT ck_care_interaction_status CHECK (
        status IN ('PENDING','COMPLETED','MISSED','CANCELLED')
    ),
    -- A completed interaction must say when it happened and how it went.
    CONSTRAINT ck_care_interaction_completed CHECK (
        (status <> 'COMPLETED') OR (occurred_at IS NOT NULL AND outcome_code IS NOT NULL)
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- A concern raised out of an interaction and handed to a team lead.
CREATE TABLE escalation (
    id                      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id               CHAR(26)        NOT NULL,
    reference_code          VARCHAR(20)     NULL,

    care_case_id            BIGINT UNSIGNED NOT NULL,
    care_interaction_id     BIGINT UNSIGNED NULL,
    raised_by_volunteer_id  BIGINT UNSIGNED NULL,
    assigned_to_user_id     BIGINT UNSIGNED NULL,
    campus_id               BIGINT UNSIGNED NOT NULL,

    reason_code             VARCHAR(40)     NOT NULL,
    tier                    VARCHAR(20)     NOT NULL DEFAULT 'STANDARD',
    status                  VARCHAR(20)     NOT NULL DEFAULT 'NEW',
    description             TEXT            NOT NULL,

    raised_at               DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    notified_at             DATETIME(3)     NULL,
    acknowledged_at         DATETIME(3)     NULL,
    resolved_at             DATETIME(3)     NULL,

    outcome_code            VARCHAR(40)     NULL,
    resolution_notes        TEXT            NULL,
    resource_connected      VARCHAR(150)    NULL,

    -- ESCALATION PAUSES THE CASE.
    -- While this row is open the scheduler will not create the next follow-up for
    -- care_case_id. Closing it sets the case back to its previous stage and
    -- recomputes next_step_due_on, so the sequence resumes exactly where it stopped.
    resume_case_on_close    TINYINT(1)      NOT NULL DEFAULT 1,

    -- Chase-up trail. An escalation nobody acknowledges is the failure mode that
    -- matters most here, so the reminder job records what it has already done:
    -- team lead first, then the pastor once the threshold passes.
    reminder_count          SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    last_reminder_at        DATETIME(3)     NULL,
    pastor_alerted_at       DATETIME(3)     NULL,

    -- Safeguarding record for reasons flagged requires_protocol.
    protocol_followed       TINYINT(1)      NULL,
    authorities_contacted   TINYINT(1)      NULL,
    volunteer_debriefed     TINYINT(1)      NULL,

    created_at              DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by              BIGINT UNSIGNED NULL,
    updated_at              DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by              BIGINT UNSIGNED NULL,
    row_version             INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_escalation_public_id      (public_id),
    UNIQUE KEY ux_escalation_reference_code (reference_code),
    KEY ix_escalation_case        (care_case_id),
    KEY ix_escalation_interaction (care_interaction_id),
    -- Covers the team lead's pending queue, most urgent first.
    KEY ix_escalation_queue       (assigned_to_user_id, status, tier),
    KEY ix_escalation_campus      (campus_id, status),
    KEY ix_escalation_raised      (raised_at),
    -- Covers the chase-up sweep: open escalations ordered by how long they have
    -- been waiting, so the oldest unacknowledged one is found first.
    KEY ix_escalation_pending     (status, acknowledged_at, raised_at),

    CONSTRAINT fk_escalation_case        FOREIGN KEY (care_case_id)           REFERENCES care_case (id),
    CONSTRAINT fk_escalation_interaction FOREIGN KEY (care_interaction_id)    REFERENCES care_interaction (id),
    CONSTRAINT fk_escalation_volunteer   FOREIGN KEY (raised_by_volunteer_id) REFERENCES volunteer (id),
    CONSTRAINT fk_escalation_assignee    FOREIGN KEY (assigned_to_user_id)    REFERENCES user_account (id),
    CONSTRAINT fk_escalation_campus      FOREIGN KEY (campus_id)              REFERENCES campus (id),
    CONSTRAINT fk_escalation_reason      FOREIGN KEY (reason_code)            REFERENCES escalation_reason (code),
    CONSTRAINT fk_escalation_outcome     FOREIGN KEY (outcome_code)           REFERENCES escalation_outcome (code),

    CONSTRAINT ck_escalation_tier   CHECK (tier IN ('STANDARD','URGENT','EMERGENCY')),
    CONSTRAINT ck_escalation_status CHECK (
        status IN ('NEW','ACKNOWLEDGED','IN_PROGRESS','RESOLVED','REFERRED_OUT','CLOSED')
    ),
    -- A resolved escalation must record when and how.
    CONSTRAINT ck_escalation_resolved CHECK (
        (status NOT IN ('RESOLVED','CLOSED')) OR (resolved_at IS NOT NULL AND outcome_code IS NOT NULL)
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Free-text notes attached to any entity.
--
-- Kept polymorphic (as in the MVP) because notes genuinely apply to people,
-- volunteers and cases alike. The trade-off is explicit: no foreign key is
-- possible, so the application must validate entity_type/entity_id.
-- Categories of note. A lookup so new categories are an admin action.
CREATE TABLE note_type (
    code        VARCHAR(30)  NOT NULL,
    label       VARCHAR(80)  NOT NULL,
    sort_order  SMALLINT     NOT NULL DEFAULT 0,
    is_active   TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Flexible documentation attached to any entity.
--
-- Polymorphic by design (entity_type + entity_id), because a note applies equally
-- to a person, a volunteer, a case, an interaction or a check-in. The trade-off is
-- explicit: no foreign key on entity_id is possible, so the service layer must
-- verify the target exists before inserting.
CREATE TABLE note (
    id                   BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id            CHAR(26)        NOT NULL,

    entity_type          VARCHAR(30)     NOT NULL,
    entity_id            BIGINT UNSIGNED NOT NULL,

    note_type_code       VARCHAR(30)     NULL,
    body                 TEXT            NOT NULL,
    -- Free-form comma-separated labels. Searchable, but not a controlled
    -- vocabulary; promote to a table if tags ever start driving logic.
    tags                 VARCHAR(255)    NULL,

    -- Restricted visibility. A private note is readable only by accounts holding
    -- visible_to_role_code, or a role with a higher hierarchy_level.
    is_private           TINYINT(1)      NOT NULL DEFAULT 0,
    visible_to_role_code VARCHAR(30)     NULL,

    created_at           DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by           BIGINT UNSIGNED NULL,
    updated_at           DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by           BIGINT UNSIGNED NULL,

    PRIMARY KEY (id),
    UNIQUE KEY ux_note_public_id (public_id),
    KEY ix_note_entity  (entity_type, entity_id, created_at),
    KEY ix_note_private (is_private, visible_to_role_code),

    CONSTRAINT fk_note_type FOREIGN KEY (note_type_code)       REFERENCES note_type (code),
    CONSTRAINT fk_note_role FOREIGN KEY (visible_to_role_code) REFERENCES app_role (code),
    CONSTRAINT ck_note_entity_type CHECK (
        entity_type IN ('PERSON','VOLUNTEER','CARE_CASE','CARE_INTERACTION','CHECK_IN','ESCALATION','TEAM')
    ),
    -- A private note naming no role would be invisible to everyone.
    CONSTRAINT ck_note_private CHECK (
        is_private = 0 OR visible_to_role_code IS NOT NULL
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- #############################################################################
-- 5. OPERATIONS
-- #############################################################################

-- Tunable business rules.
--
-- Contains NO secrets. In the MVP the live Telegram bot token sat in this table
-- in plaintext and was reachable through the generic settings API. Third-party
-- credentials belong in environment variables; `is_secret` exists only to mark
-- values the API must redact, not to make the table a safe place for them.
CREATE TABLE app_setting (
    setting_key     VARCHAR(80)     NOT NULL,
    setting_value   VARCHAR(500)    NOT NULL,
    value_type      VARCHAR(20)     NOT NULL DEFAULT 'STRING',
    category        VARCHAR(40)     NOT NULL DEFAULT 'GENERAL',
    description     VARCHAR(255)    NULL,

    -- Bounds enforced by the settings service so an administrator cannot set a
    -- value that breaks the application.
    min_value       INT             NULL,
    max_value       INT             NULL,

    is_secret       TINYINT(1)      NOT NULL DEFAULT 0,
    is_editable     TINYINT(1)      NOT NULL DEFAULT 1,

    updated_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by      BIGINT UNSIGNED NULL,
    row_version     INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (setting_key),
    KEY ix_app_setting_category (category),
    CONSTRAINT ck_app_setting_type CHECK (
        value_type IN ('STRING','INTEGER','DECIMAL','BOOLEAN','JSON')
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- One-time tokens that connect a Telegram account to a person.
--
-- The MVP linked Telegram by calling getUpdates and taking whichever chat had
-- messaged the bot most recently. With two people connecting at once that
-- silently attached the wrong chat id, and from then on one person's crisis
-- alerts went to a stranger. The token removes the race: it names exactly one
-- person before Telegram is ever opened.
--
-- Only the SHA-256 of the token is stored, as with refresh_token: a database
-- leak must not yield tokens that can still be redeemed. The token itself never
-- contains the person id, so a link cannot be reversed into a database key.
CREATE TABLE telegram_link_token (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    person_id       BIGINT UNSIGNED NOT NULL,
    token_hash      CHAR(64)        NOT NULL,   -- SHA-256 hex of a 32-byte token
    issued_at       DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    expires_at      DATETIME(3)     NOT NULL,
    used_at         DATETIME(3)     NULL,
    issued_by       BIGINT UNSIGNED NULL,

    PRIMARY KEY (id),
    UNIQUE KEY ux_telegram_link_token_hash (token_hash),
    -- Covers "the live token for this person", so issuing a new one can retire
    -- any earlier unused token rather than leaving several valid at once.
    KEY ix_telegram_link_token_person (person_id, used_at, expires_at),

    CONSTRAINT fk_telegram_link_token_person
        FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
    CONSTRAINT fk_telegram_link_token_issuer
        FOREIGN KEY (issued_by) REFERENCES user_account (id),
    CONSTRAINT ck_telegram_link_token_expiry CHECK (expires_at > issued_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Outbound notification log.
--
-- New in this design. The MVP sent Telegram alerts and discarded the result, so
-- a volunteer with no linked chat id silently received nothing and no one could
-- tell. Delivery is now a recorded fact.
CREATE TABLE notification_delivery (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id           CHAR(26)        NOT NULL,

    channel             VARCHAR(20)     NOT NULL DEFAULT 'TELEGRAM',
    recipient_person_id BIGINT UNSIGNED NULL,
    -- Opaque channel address (Telegram chat id, email). Not a secret.
    recipient_address   VARCHAR(255)    NULL,

    notification_type   VARCHAR(40)     NOT NULL,
    -- What this alert was about, for tracing back from a delivery failure.
    related_entity_type VARCHAR(30)     NULL,
    related_entity_id   BIGINT UNSIGNED NULL,

    status              VARCHAR(20)     NOT NULL DEFAULT 'PENDING',
    attempt_count       TINYINT UNSIGNED NOT NULL DEFAULT 0,
    -- Message bodies are NOT stored: alerts quote pastoral detail and names.
    -- Only the outcome is retained.
    failure_reason      VARCHAR(255)    NULL,

    queued_at           DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    sent_at             DATETIME(3)     NULL,

    PRIMARY KEY (id),
    UNIQUE KEY ux_notification_public_id (public_id),
    KEY ix_notification_recipient (recipient_person_id, queued_at),
    KEY ix_notification_status    (status, queued_at),
    KEY ix_notification_related   (related_entity_type, related_entity_id),

    CONSTRAINT fk_notification_person FOREIGN KEY (recipient_person_id) REFERENCES person (id),
    CONSTRAINT ck_notification_channel CHECK (channel IN ('TELEGRAM','SMS','EMAIL','WHATSAPP','PUSH')),
    CONSTRAINT ck_notification_status  CHECK (status IN ('PENDING','SENT','FAILED','SKIPPED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- Record of scheduled job runs, so a silently dead cron is visible.
CREATE TABLE job_run (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    job_name        VARCHAR(80)     NOT NULL,
    started_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    finished_at     DATETIME(3)     NULL,
    status          VARCHAR(20)     NOT NULL DEFAULT 'RUNNING',
    items_processed INT UNSIGNED    NOT NULL DEFAULT 0,
    items_failed    INT UNSIGNED    NOT NULL DEFAULT 0,
    detail          VARCHAR(500)    NULL,
    triggered_by    BIGINT UNSIGNED NULL,

    PRIMARY KEY (id),
    KEY ix_job_run_name (job_name, started_at),
    CONSTRAINT ck_job_run_status CHECK (status IN ('RUNNING','SUCCEEDED','FAILED','PARTIAL'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


SET FOREIGN_KEY_CHECKS = 1;


-- #############################################################################
-- 7. REFERENCE DATA
--
--    Vocabulary and configuration required for the application to function.
--    No people, no volunteers, no demo records, no secrets.
-- #############################################################################

INSERT INTO campus (public_id, code, name, timezone) VALUES
    ('01JCAMPUS0000000000000001', 'ONGOLE', 'Ongole', 'Asia/Kolkata');

-- Every account is STAFF. A visitor/member is a `person`, never a user_account.
-- More roles are expected; this table is the extension point.
INSERT INTO app_role (code, label, description, hierarchy_level) VALUES
    ('ADMIN',      'Administrator', 'Full system administration, configuration and user management', 100),
    ('PASTOR',     'Pastor',        'Cross-team oversight and reporting',                             80),
    ('TEAM_LEAD',  'Team Lead',     'Leads a team: escalations, check-ins, nurture review',           60),
    ('VOLUNTEER',  'Volunteer',     'Handles assigned care cases',                                    40),
    ('DATA_ENTRY', 'Data Entry',    'Records visitors at intake; no case or volunteer access',        20);

INSERT INTO capacity_band (code, label, min_per_week, max_per_week, description, sort_order) VALUES
    ('LIMITED',    'Limited',    1, 2, 'Reduced load: new, recovering or time-constrained volunteers', 10),
    ('BALANCED',   'Balanced',   2, 3, 'Standard sustainable load',                                    20),
    ('CONSISTENT', 'Consistent', 4, 6, 'Experienced volunteers with proven capacity',                  30);

INSERT INTO contact_method (code, label, sort_order) VALUES
    ('CALL',     'Phone call',    10),
    ('VISIT',    'In-person visit', 20),
    ('TELEGRAM', 'Telegram',      30),
    ('WHATSAPP', 'WhatsApp',      40),
    ('SMS',      'SMS',           50),
    ('EMAIL',    'Email',         60);

INSERT INTO connection_source (code, label, sort_order) VALUES
    ('WALK_IN',       'Walk-in',            10),
    ('FRIEND_INVITE', 'Invited by a friend', 20),
    ('SOCIAL_MEDIA',  'Social media',       30),
    ('WEBSITE',       'Website',            40),
    ('EVENT',         'Outreach event',     50),
    ('OTHER',         'Other',              60);

-- HOW the contact went.
INSERT INTO care_outcome
    (code, label, contact_made, opens_escalation, schedules_retry, default_tier, sort_order) VALUES
    ('SPOKE',         'Spoke with them',        1, 0, 0, NULL,        10),
    ('NEEDS_SUPPORT', 'Needs pastoral support', 1, 1, 0, 'STANDARD',  20),
    ('CRISIS',        'Crisis disclosed',       1, 1, 0, 'EMERGENCY', 30),
    ('NO_ANSWER',     'No answer',              0, 0, 1, NULL,        40),
    ('LEFT_MESSAGE',  'Left a message',         0, 0, 1, NULL,        50),
    ('WRONG_NUMBER',  'Wrong number',           0, 0, 0, NULL,        60),
    ('UNREACHABLE',   'Unreachable',            0, 0, 0, NULL,        70);

-- WHAT the visitor wants next. The second axis.
INSERT INTO visitor_intent (code, label, implies_do_not_contact, sort_order) VALUES
    ('WANTS_CONNECTION', 'Wants to connect / keep in touch',        0, 10),
    ('UNDECIDED',        'Open but undecided',                      0, 20),
    ('NEEDS_TIME',       'Interested, asked to be contacted later', 0, 30),
    ('ALREADY_CHURCHED', 'Already belongs to another church',       0, 40),
    ('NOT_INTERESTED',   'Not interested',                          0, 50),
    ('DO_NOT_CONTACT',   'Asked not to be contacted again',         1, 60),
    ('UNKNOWN',          'Not established',                         0, 70);

-- The default nurture plan. Admin-editable: change the gaps, the methods, the
-- number of steps, or whether outcomes are evaluated at all.
INSERT INTO nurture_plan
    (public_id, name, evaluate_outcome, assignment_mode, default_gap_days, missed_after_days, is_default) VALUES
    ('01JPLAN00000000000000001', 'Standard nurture', 1, 'SAME_IF_AVAILABLE', 7, 3, 1);

-- Seven contacts, alternating call and visit. Each gap is the wait after the
-- PREVIOUS step completes, so a slow response naturally stretches the sequence
-- instead of stacking up overdue steps.
INSERT INTO nurture_plan_step (nurture_plan_id, step_number, method_code, gap_days, label) VALUES
    (1, 1, 'CALL',  7,  'First nurture call'),
    (1, 2, 'VISIT', 7,  'Home visit'),
    (1, 3, 'CALL',  7,  'Check-in call'),
    (1, 4, 'VISIT', 7,  'Second visit'),
    (1, 5, 'CALL',  7,  'Progress call'),
    (1, 6, 'VISIT', 7,  'Third visit'),
    (1, 7, 'CALL',  7,  'Final call before review');

-- PROGRESSION RULES, in evaluation order (lowest priority number first).
-- The first matching rule wins.
INSERT INTO care_progression_rule
    (public_id, from_stage, from_step_number, outcome_code, intent_code, action, close_reason, priority, description) VALUES

    -- ---- Escalation pauses the case at any stage ----
    -- A crisis disclosure means the visitor engaged and shared a problem, so it is
    -- an opening, not a refusal. ESCALATE raises the escalation and pauses the
    -- sequence; when the team lead closes it the next follow-up resumes from where
    -- it stopped. It does not end the journey.
    ('01JPROG0000000000000001', NULL, NULL, 'CRISIS',        NULL,               'ESCALATE',      NULL,               1,  'Crisis disclosed - escalate and pause; resume when closed'),

    -- ---- Explicit refusals stop the sequence immediately ----
    ('01JPROG0000000000000002', NULL, NULL, NULL,            'DO_NOT_CONTACT',   'CLOSE_CASE',    'DO_NOT_CONTACT',   10, 'Asked not to be contacted - stop and flag the person'),
    ('01JPROG0000000000000003', NULL, NULL, NULL,            'NOT_INTERESTED',   'CLOSE_CASE',    'DECLINED',         20, 'Not interested - stop nurturing immediately'),
    ('01JPROG0000000000000004', NULL, NULL, NULL,            'ALREADY_CHURCHED', 'CLOSE_CASE',    'ALREADY_CHURCHED', 30, 'Belongs to another church - stop nurturing'),

    -- ---- Needs pastoral support ----
    ('01JPROG0000000000000005', NULL, NULL, 'NEEDS_SUPPORT', NULL,               'ESCALATE',      NULL,               40, 'Needs pastoral support - escalate and pause; resume when closed'),

    -- ---- Initial follow-up: does this person enter nurture at all? ----
    ('01JPROG0000000000000006', 'INITIAL_FOLLOW_UP', NULL, 'SPOKE',       'WANTS_CONNECTION', 'START_NURTURE', NULL,  60, 'Positive and open - begin nurturing'),
    ('01JPROG0000000000000007', 'INITIAL_FOLLOW_UP', NULL, 'SPOKE',       'UNDECIDED',        'START_NURTURE', NULL,  61, 'Open but undecided - begin nurturing'),
    ('01JPROG0000000000000008', 'INITIAL_FOLLOW_UP', NULL, 'SPOKE',       'NEEDS_TIME',       'SCHEDULE_RETRY',NULL,  62, 'Asked to be contacted later - retry, do not nurture yet'),
    ('01JPROG0000000000000009', 'INITIAL_FOLLOW_UP', NULL, 'NO_ANSWER',   NULL,               'SCHEDULE_RETRY',NULL,  70, 'No answer - retry until the attempt limit'),
    ('01JPROG0000000000000010', 'INITIAL_FOLLOW_UP', NULL, 'LEFT_MESSAGE',NULL,               'SCHEDULE_RETRY',NULL,  71, 'Message left - retry until the attempt limit'),
    ('01JPROG0000000000000011', 'INITIAL_FOLLOW_UP', NULL, 'WRONG_NUMBER',NULL,               'CLOSE_CASE',    'UNREACHABLE', 80, 'Wrong number - close'),
    ('01JPROG0000000000000012', 'INITIAL_FOLLOW_UP', NULL, 'UNREACHABLE', NULL,               'CLOSE_CASE',    'UNREACHABLE', 81, 'Exhausted contact routes - close'),

    -- ---- Nurture: does the sequence continue? ----
    ('01JPROG0000000000000013', 'NURTURE', NULL, 'SPOKE',        NULL,           'CONTINUE_NURTURE', NULL,           60, 'Contact made - schedule the next step'),
    ('01JPROG0000000000000014', 'NURTURE', NULL, 'NO_ANSWER',    NULL,           'CONTINUE_NURTURE', NULL,           70, 'No answer - keep going through the sequence'),
    ('01JPROG0000000000000015', 'NURTURE', NULL, 'LEFT_MESSAGE', NULL,           'CONTINUE_NURTURE', NULL,           71, 'Message left - keep going'),
    ('01JPROG0000000000000016', 'NURTURE', NULL, 'WRONG_NUMBER', NULL,           'CLOSE_CASE',       'UNREACHABLE',  80, 'Wrong number - close'),
    ('01JPROG0000000000000017', 'NURTURE', NULL, 'UNREACHABLE',  NULL,           'CLOSE_CASE',       'UNREACHABLE',  81, 'Exhausted contact routes - close'),

    -- ---- Fallback ----
    -- Nothing matched. Queue for a human rather than guess.
    ('01JPROG0000000000000018', NULL, NULL, NULL, NULL, 'MANUAL_REVIEW', NULL, 999, 'Fallback - no rule matched, a team lead decides');

INSERT INTO note_type (code, label, sort_order) VALUES
    ('GENERAL',        'General',        10),
    ('PRAYER_REQUEST', 'Prayer request', 20),
    ('FOLLOW_UP',      'Follow-up',      30),
    ('ALERT',          'Alert',          40),
    ('SAFEGUARDING',   'Safeguarding',   50);

INSERT INTO escalation_reason (code, label, default_tier, requires_protocol, sort_order) VALUES
    ('GENERAL_CONCERN',    'General concern',        'STANDARD',  0, 10),
    ('SPIRITUAL_QUESTION', 'Spiritual questions',    'STANDARD',  0, 20),
    ('FINANCIAL_CRISIS',   'Financial crisis',       'URGENT',    0, 30),
    ('HEALTH_CRISIS',      'Health crisis',          'URGENT',    0, 40),
    ('MARRIAGE_CRISIS',    'Marriage or family crisis','URGENT',  0, 50),
    ('GRIEF_LOSS',         'Grief or bereavement',   'STANDARD',  0, 60),
    ('ABUSE_DISCLOSURE',   'Disclosure of abuse',    'EMERGENCY', 1, 70),
    ('SELF_HARM_RISK',     'Risk of self-harm',      'EMERGENCY', 1, 80),
    ('OTHER',              'Other',                  'STANDARD',  0, 90);

INSERT INTO escalation_outcome (code, label, sort_order) VALUES
    ('RESOURCE_CONNECTED',  'Connected to a resource',    10),
    ('PASTORAL_CARE',       'Pastoral care scheduled',    20),
    ('COUNSELLING_REFERRAL','Referred to counselling',    30),
    ('BENEVOLENCE',         'Benevolence provided',       40),
    ('EMERGENCY_SERVICES',  'Emergency services involved',50),
    ('NO_ACTION_NEEDED',    'No further action needed',   60),
    ('OTHER',               'Other',                      70);

INSERT INTO app_setting (setting_key, setting_value, value_type, category, description, min_value, max_value) VALUES
    ('assignment.auto_assign_on_intake', 'true', 'BOOLEAN', 'ASSIGNMENT', 'Assign a case at intake rather than waiting for the batch job', NULL, NULL),
    ('assignment.max_retry_attempts',    '3',    'INTEGER', 'ASSIGNMENT', 'Contact attempts before a case is marked unreachable',           1,   10),
    ('assignment.retry_delay_days',      '3',    'INTEGER', 'ASSIGNMENT', 'Days to wait before a retry attempt',                             1,   30),
    ('assignment.response_target_hours', '48',   'INTEGER', 'ASSIGNMENT', 'Target hours for first contact',                                  1,  168),
    -- Step count, per-step gaps, outcome evaluation and reassignment behaviour all
    -- live on nurture_plan / nurture_plan_step, not here.
    ('nurture.lookahead_days',           '1',    'INTEGER', 'NURTURE',    'How many days ahead the scheduler creates a due nurture step',    0,   14),
    ('escalation.ack_target_hours',      '4',    'INTEGER', 'ESCALATION', 'Hours a team lead has to acknowledge before reminders begin',     1,   72),
    ('escalation.reminder_every_hours',  '4',    'INTEGER', 'ESCALATION', 'Hours between reminders to the team lead',                        1,   72),
    ('escalation.pastor_alert_hours',    '12',   'INTEGER', 'ESCALATION', 'Hours unacknowledged before the pastor is alerted as well',       1,  168),
    ('nurture.max_manual_review_days',   '7',    'INTEGER', 'NURTURE',    'Days a case may sit in manual review before it is flagged',       1,   90),
    ('care.check_in_frequency_days',     '30',   'INTEGER', 'CARE',       'Days between volunteer check-ins',                                7,  180),
    ('care.survey_frequency_months',     '3',    'INTEGER', 'CARE',       'Months between volunteer surveys',                                1,   12),
    ('health.green_threshold',           '90',   'INTEGER', 'HEALTH',     'Completion rate %% for a green flag',                             0,  100),
    ('health.amber_threshold',           '75',   'INTEGER', 'HEALTH',     'Completion rate %% for an amber flag',                            0,  100),
    ('team.max_span_full_time',          '12',   'INTEGER', 'TEAM',       'Maximum volunteers for a full-time team lead',                    1,   50),
    ('team.max_span_player_coach',       '8',    'INTEGER', 'TEAM',       'Maximum volunteers for a player-coach team lead',                 1,   50),
    -- Off by default: turning this on before the bot is configured would lock
    -- every unlinked user out of the application.
    ('huddle.day_of_week',               '6',    'INTEGER', 'HUDDLE',     'Team huddle day (1=Mon ... 7=Sun)',                               1,    7),
    ('huddle.lookback_days',             '7',    'INTEGER', 'HUDDLE',     'How many days of contacts the huddle reviews',                    1,   60),
    ('huddle.reminder_hour',             '8',    'INTEGER', 'HUDDLE',     'Hour (UTC) the huddle reminder is sent on the day',               0,   23),
    ('huddle.remind_volunteers',         'true', 'BOOLEAN', 'HUDDLE',     'Also remind the volunteers, not just the lead',                   NULL, NULL),
    ('telegram.require_linking',         'false','BOOLEAN', 'TELEGRAM',   'Require users to connect Telegram before using the application',  NULL, NULL),
    ('telegram.link_token_minutes',      '30',   'INTEGER', 'TELEGRAM',   'How long a Telegram linking link stays valid',                    5,  1440);

-- NOTE: no telegram bot token, no signing key, no connection string. Those are
-- supplied as environment variables and must never be inserted here.


-- #############################################################################
-- DESIGN DECISIONS AND KNOWN TRADE-OFFS
--
-- 1. Counters (volunteer.current_case_load, care_case.contact_attempt_count) are
--    maintained rather than derived. Derivation is always correct but costs a
--    COUNT on every assignment decision. The counter is authoritative and must be
--    updated in the SAME transaction as the row that changes it. A nightly
--    reconciliation job should assert counter == derived and report drift.
--
-- 2. `note` is polymorphic and therefore has no foreign key on entity_id. This is
--    a deliberate trade of referential integrity for a single notes surface. The
--    application must validate the target exists before inserting.
--
-- 3. Nurture entry is decided by nurture_entry_rule over (outcome, intent), not
--    by a flag on the outcome. Rules are admin-editable and fall through to
--    MANUAL_REVIEW, so an unforeseen combination is queued for a human rather
--    than silently routed the wrong way.
--
-- 4. person.full_name is a STORED generated column. It costs a little disk and
--    write time in exchange for indexed name search without an expression index.
--
-- 4. Soft delete exists ONLY on person and event_series, where rows must remain
--    referenceable after removal. Everywhere else, state is a status column.
--    Note that a soft-deleted person still occupies their unique reference_code.
--
-- 5. Contact details live in person_contact, so "the person's phone number" is a
--    query, not a column. Reads that need one primary number should use a view or
--    a join on is_primary = 1. This is the cost of supporting multiple contacts.
--
-- 6. campus_id is denormalised onto volunteer, care_case, escalation and
--    event_series so authorisation filters never require a join. The application
--    must keep it consistent when a person or case moves campus.
--
-- 7. IP addresses are VARBINARY(16) via INET6_ATON/INET6_NTOA — 16 bytes instead
--    of 45, and correct for IPv6.
--
-- 8. No table stores a plaintext credential. Refresh tokens are SHA-256 hashes,
--    passwords are PBKDF2, and the MFA secret column is encrypted by the
--    application before it reaches the database.
-- #############################################################################
