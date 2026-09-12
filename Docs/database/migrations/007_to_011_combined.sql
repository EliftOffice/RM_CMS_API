-- =============================================================================
-- Combined migration: 007 → 011, in one file
--
-- Run this ONCE against cms_api_db to bring a database up from the 006 baseline
-- (website enquiries) to the current schema. It is the five migrations 007–011
-- concatenated in order:
--
--   007  church events
--   008  the event poster (drops church_event.image_slug, adds the poster)
--   009  passwordless (mobile-number-only) login
--   010  editable Telegram message templates
--   011  Telegram sign-in confirmation
--
-- SAFE TO RE-RUN, and safe on a database that already has some or all of this.
-- Every step is guarded: tables use CREATE TABLE IF NOT EXISTS, columns and
-- constraints are added only when information_schema shows them missing, the
-- app_setting rows use INSERT IGNORE, and image_slug is dropped only if present.
-- A freshly reset database that already matches the final schema will run this
-- through and change nothing.
--
-- ORDER MATTERS. 008 depends on 007's church_event, so keep the sections in
-- place if you edit this. The per-migration files remain in this folder as the
-- source of truth; this rollup is a convenience for applying them in one pass.
-- =============================================================================


-- #############################################################################
-- BEGIN 007_events
-- #############################################################################

-- =============================================================================
-- 007 — Church events
--
-- The events the public website lists at /events and /events/<slug>.
--
-- Until now that page read a hard-coded array in the website's own source, so
-- publishing an event meant a code change and a redeploy. This table is the
-- source of truth instead, and the site fetches it.
--
-- WHAT THIS IS NOT: `security_event` is an audit row about the application.
-- This is a gathering people are invited to. Different things, hence
-- `church_event` rather than `event` — and `event` is a reserved word in MySQL
-- (the scheduler), so an unquoted `event` in a query is a syntax error waiting
-- to happen.
--
-- Safe to re-run: guarded table creation.
-- =============================================================================

USE cms_api_db;

CREATE TABLE IF NOT EXISTS church_event (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id       CHAR(26)        NOT NULL,

    -- The URL segment: /events/<slug>. Generated from the title, editable, and
    -- unique. Stable once published — it is what anyone who shared the link is
    -- holding, so renaming the title deliberately does NOT change it.
    slug            VARCHAR(120)    NOT NULL,

    -- Null means every campus. A single-campus church never has to pick one,
    -- and a multi-campus one can run an event at a single site.
    campus_id       BIGINT UNSIGNED NULL,

    title           VARCHAR(160)    NOT NULL,

    -- One or two lines for the card. Always shown; the description is not.
    summary         VARCHAR(400)    NOT NULL,

    -- The long text on the event's own page. Blank-line separated paragraphs,
    -- split into an array by the API because that is the shape the site's
    -- ChurchEvent type expects.
    description     TEXT            NULL,

    venue           VARCHAR(200)    NOT NULL,

    -- UTC, like every other instant in this schema. The API converts to the
    -- campus's zone on the way out, because the website needs an ISO string
    -- carrying a real offset ("2026-04-03T08:00:00+05:30") — a bare UTC stamp
    -- would render 8am as 2:30am to anyone reading it in India.
    starts_at       DATETIME(3)     NOT NULL,
    ends_at         DATETIME(3)     NULL,

    -- A slug from the website's own image manifest, not a URL or an upload.
    -- The site optimises images at build time into a fixed set of widths and
    -- formats; an arbitrary URL would bypass all of that and ship a 4MB JPEG to
    -- a phone. The admin screen offers the known slugs as a list.
    image_slug      VARCHAR(60)     NULL,

    -- DRAFT is invisible to the public. That is the whole point: an event can
    -- be written, checked and dated before anyone outside the church sees it.
    status          VARCHAR(20)     NOT NULL DEFAULT 'DRAFT',
    published_at    DATETIME(3)     NULL,

    created_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by      BIGINT UNSIGNED NULL,
    updated_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by      BIGINT UNSIGNED NULL,
    row_version     INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_church_event_public_id (public_id),
    UNIQUE KEY ux_church_event_slug      (slug),
    -- The public list: published events ordered by when they start.
    KEY ix_church_event_public (status, starts_at),
    KEY ix_church_event_campus (campus_id, starts_at),

    CONSTRAINT fk_church_event_campus FOREIGN KEY (campus_id) REFERENCES campus (id),

    CONSTRAINT ck_church_event_status CHECK (
        status IN ('DRAFT','PUBLISHED','CANCELLED')
    ),
    -- An end before a start is always a typo, and it silently breaks the
    -- upcoming/past split the website does on ends_at.
    CONSTRAINT ck_church_event_dates CHECK (
        ends_at IS NULL OR ends_at >= starts_at
    ),
    -- Published means published at a knowable moment; without the stamp the
    -- audit trail says an event went public but not when.
    CONSTRAINT ck_church_event_published CHECK (
        status <> 'PUBLISHED' OR published_at IS NOT NULL
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- -----------------------------------------------------------------------------
-- Verification
-- -----------------------------------------------------------------------------
SELECT COUNT(*) AS church_event_table_exists
FROM information_schema.tables
WHERE table_schema = DATABASE() AND table_name = 'church_event';

-- ------------------------------- END 007_events -------------------------------


-- #############################################################################
-- BEGIN 008_event_poster
-- #############################################################################

-- =============================================================================
-- 008 — An event carries its own poster
--
-- Replaces `church_event.image_slug` with an uploaded image.
--
-- WHY THE SLUG HAD TO GO:
-- it named a picture from the WEBSITE's build-time manifest — one of about twenty
-- stock plates (`congregation`, `worship`, `auditorium`) baked into the site by
-- `npm run assets`. So the poster actually designed for the event, the one on the
-- flyer and the WhatsApp forward, was never one of the options. Staff picked the
-- least wrong stock photograph instead, and two different events routinely showed
-- the same picture.
--
-- WHY THE BYTES ARE IN THE DATABASE AND NOT A FILE ON DISK:
-- the API is deployed by Coolify from a container image rebuilt on every push.
-- Anything written into the container's filesystem — wwwroot/uploads and the like
-- — is gone the next time the app deploys, so every poster staff had uploaded
-- would silently vanish and the events would go back to rendering without a
-- picture. Surviving that needs a mounted volume nobody has configured, and the
-- failure mode of forgetting is invisible until a visitor looks. Bytes in a row
-- are backed up with everything else and need no infrastructure at all.
--
-- WHY A SECOND TABLE:
-- a MEDIUMBLOB on `church_event` would be dragged along by the admin list, which
-- reads fifty rows to show a table of titles and dates. Split out, that list
-- reads a few hundred bytes per row and the image is fetched only by the one
-- endpoint that serves it.
--
-- Safe to re-run: every step is guarded.
-- =============================================================================

USE cms_api_db;

-- -----------------------------------------------------------------------------
-- 1. Poster metadata on the event itself
--
-- Metadata here, bytes next door. The list screen needs to know only THAT a
-- poster exists, and the serving endpoint can answer a conditional request
-- without touching the blob.
-- -----------------------------------------------------------------------------
SET @sql := (
    SELECT IF(
        EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_schema = DATABASE()
                   AND table_name = 'church_event'
                   AND column_name = 'poster_content_type'),
        'SELECT ''poster columns already present'' AS note',
        'ALTER TABLE church_event
            ADD COLUMN poster_content_type VARCHAR(80)  NULL AFTER venue,
            ADD COLUMN poster_file_name    VARCHAR(200) NULL AFTER poster_content_type,
            ADD COLUMN poster_byte_size    INT UNSIGNED NULL AFTER poster_file_name,
            ADD COLUMN poster_updated_at   DATETIME(3)  NULL AFTER poster_byte_size'
    )
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- -----------------------------------------------------------------------------
-- 2. The bytes
--
-- Keyed by the event rather than by an id of its own: there is never a second
-- poster for one event, and a surrogate key would only make that possible by
-- accident.
--
-- MEDIUMBLOB (16 MB) rather than BLOB (64 KB): a phone photograph of a printed
-- poster is routinely 3-4 MB before anyone resizes it. The application caps what
-- it will accept well below this, so the limit that actually bites is the one
-- with a readable sentence attached rather than a truncated row.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS church_event_poster (
    church_event_id BIGINT UNSIGNED NOT NULL,
    bytes           MEDIUMBLOB      NOT NULL,

    PRIMARY KEY (church_event_id),

    -- Deleting an event takes its poster with it. This is the one place a cascade
    -- is right: the image has no meaning without the event, and leaving it behind
    -- would be megabytes nothing can ever reach.
    CONSTRAINT fk_church_event_poster_event FOREIGN KEY (church_event_id)
        REFERENCES church_event (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- -----------------------------------------------------------------------------
-- 3. Either there is a poster and we know what it is, or there is none
--
-- A content type with no size is a half-written upload. Added separately from the
-- columns so re-running against a database that already has the constraint is not
-- an error.
-- -----------------------------------------------------------------------------
SET @sql := (
    SELECT IF(
        EXISTS (SELECT 1 FROM information_schema.table_constraints
                 WHERE table_schema = DATABASE()
                   AND table_name = 'church_event'
                   AND constraint_name = 'ck_church_event_poster'),
        'SELECT ''poster check already present'' AS note',
        'ALTER TABLE church_event
            ADD CONSTRAINT ck_church_event_poster CHECK (
                (poster_content_type IS NULL AND poster_byte_size IS NULL
                                             AND poster_updated_at IS NULL)
             OR (poster_content_type IS NOT NULL AND poster_byte_size IS NOT NULL
                                                 AND poster_updated_at IS NOT NULL))'
    )
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- -----------------------------------------------------------------------------
-- 4. Drop the slug
--
-- Dropped rather than left in place. A dead column that the API no longer reads
-- but the schema still offers is how the next person writing an event screen ends
-- up populating it and wondering why nothing appears on the site.
--
-- Nothing is migrated across: a slug named a picture belonging to the website, and
-- there is no file here to turn it into. Events that had one keep their text and
-- show no poster until somebody uploads the real one, which is the honest outcome
-- — the stock photograph was never this event's picture in the first place.
-- -----------------------------------------------------------------------------
SET @sql := (
    SELECT IF(
        EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_schema = DATABASE()
                   AND table_name = 'church_event'
                   AND column_name = 'image_slug'),
        'ALTER TABLE church_event DROP COLUMN image_slug',
        'SELECT ''image_slug already dropped'' AS note'
    )
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- -----------------------------------------------------------------------------
-- Verification
-- -----------------------------------------------------------------------------
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema = DATABASE()
  AND table_name = 'church_event'
  AND column_name LIKE 'poster%'
ORDER BY ordinal_position;

SELECT COUNT(*) AS image_slug_columns_remaining
FROM information_schema.columns
WHERE table_schema = DATABASE()
  AND table_name = 'church_event'
  AND column_name = 'image_slug';

-- ------------------------------- END 008_event_poster -------------------------------


-- #############################################################################
-- BEGIN 009_passwordless_login
-- #############################################################################

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

-- ------------------------------- END 009_passwordless_login -------------------------------


-- #############################################################################
-- BEGIN 010_message_templates
-- #############################################################################

-- =============================================================================
-- 010 — Editable Telegram message templates
--
-- Every message this system sends over Telegram was a string literal in C#:
-- the welcome after somebody links their account, the escalation chase-up, the
-- huddle reminder. Changing a word meant a code change and a deployment, so in
-- practice the wording never changed — and the people who know how it should
-- read are the pastors, not whoever can rebuild the application.
--
-- WHY A ROW IS THE EXCEPTION, NOT THE RULE:
-- this table holds only templates somebody has actually edited. Every scenario
-- has a default written in C# (`TelegramTemplates`), and no row means "use it".
-- So a fresh database sends correct messages with this table empty, a new
-- scenario added in code works before anyone touches this screen, and "reset to
-- default" is a DELETE rather than a second copy of the text to keep in step.
--
-- WHY NO MESSAGE BODIES ARE STORED ANYWHERE ELSE:
-- `notification_delivery` deliberately has no body column, because a sent
-- message quotes pastoral detail and names. That is unchanged. What is stored
-- here is the TEMPLATE — the shape with `{{Placeholders}}` in it — which
-- contains no personal data at all. The values are substituted at send time and
-- never persisted.
--
-- Safe to re-run: guarded.
-- =============================================================================

USE cms_api_db;

CREATE TABLE IF NOT EXISTS telegram_template (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id       CHAR(26)        NOT NULL,

    -- The scenario this is the wording for: LINK_WELCOME, CASE_ASSIGNED and so
    -- on. Not a foreign key to a lookup table: the set of scenarios is decided
    -- by the code that sends them, and a row here for a code the application no
    -- longer knows about is simply ignored rather than being a broken reference.
    --
    -- VARCHAR + application validation rather than a CHECK, deliberately. A
    -- CHECK would have to be migrated every time a scenario is added, which is
    -- exactly the deployment this feature exists to avoid.
    code            VARCHAR(60)     NOT NULL,

    -- The wording, with {{Placeholder}} tokens. Telegram HTML, so it may contain
    -- <b> and <i>; the VALUES substituted into it are escaped at render time,
    -- which is what stops a person's name containing an angle bracket from
    -- breaking the whole message.
    --
    -- TEXT rather than a sized VARCHAR: Telegram's own ceiling is 4096
    -- characters for a message, but a template can be longer than the message it
    -- produces is not — the application enforces the real limit with a readable
    -- error attached.
    body            TEXT            NOT NULL,

    created_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3)
                                    ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by      BIGINT UNSIGNED NULL,
    row_version     INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_telegram_template_public_id (public_id),
    -- One wording per scenario. Two would mean the message sent depends on which
    -- row was read first.
    UNIQUE KEY ux_telegram_template_code      (code),

    CONSTRAINT fk_telegram_template_editor FOREIGN KEY (updated_by)
        REFERENCES user_account (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- -----------------------------------------------------------------------------
-- Verification
--
-- An empty table is the correct result of this migration. Every scenario falls
-- back to the default written in C# until somebody edits it, so nothing is
-- seeded here.
-- -----------------------------------------------------------------------------
SELECT COUNT(*) AS customised_templates FROM telegram_template;

SELECT code, CHAR_LENGTH(body) AS body_length, updated_at
FROM telegram_template
ORDER BY code;

-- ------------------------------- END 010_message_templates -------------------------------


-- #############################################################################
-- BEGIN 011_login_telegram_verification
-- #############################################################################

-- =============================================================================
-- 011 — Confirm a sign-in on Telegram
--
-- A second step at login: the password (or the mobile number, for an account that
-- signs in without one) proves the credential, and a tap on Telegram proves the
-- person holds the phone that account is linked to.
--
-- WHY THIS MATTERS MORE HERE THAN IN MOST SYSTEMS: migration 009 allows accounts
-- that sign in with a mobile number alone, for people who cannot read a password
-- prompt. That is a deliberate trade, and this is the thing that buys some of it
-- back — a number written on a poster is no longer enough on its own, because the
-- phone has to be in the person's hand.
--
-- OFF BY DEFAULT, ORGANISATION-WIDE. Turning it on is one setting, and the same
-- setting turns it off again if it goes wrong at 9am on a Sunday.
--
-- Safe to re-run: guarded.
-- =============================================================================

USE cms_api_db;

-- -----------------------------------------------------------------------------
-- 1. login_challenge — one pending sign-in awaiting a tap
--
-- A row exists only between "the credential checked out" and "the session was
-- issued", which is at most a few minutes. It is not an audit table: what
-- happened is written to security_event like every other authentication event.
--
-- WHY THE CREDENTIAL IS ALREADY VERIFIED BEFORE A ROW APPEARS: a challenge
-- created before checking the password would let anyone spray mobile numbers and
-- make the church's phones buzz. The Telegram prompt is only ever sent to
-- somebody who already got the first factor right.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS login_challenge (
    id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,

    -- What the waiting browser holds and quotes back. A ULID: 80 bits of
    -- randomness, so it cannot be guessed, and it never leaves the browser that
    -- passed the first factor.
    public_id         CHAR(26)        NOT NULL,

    user_account_id   BIGINT UNSIGNED NOT NULL,

    -- SHA-256 of the token inside the Telegram button, never the token itself.
    -- Same rule as refresh_token: a database leak must not hand somebody the
    -- means to approve a sign-in.
    token_hash        CHAR(64)        NOT NULL,

    -- The chat the prompt was sent to, captured when the challenge is created.
    -- Held so approval can be checked against the chat that was actually asked,
    -- rather than trusting whichever chat pressed the button.
    chat_id           VARCHAR(32)     NOT NULL,

    -- PENDING -> APPROVED -> CONSUMED is the whole life of a row. DECLINED is
    -- the person saying "this was not me", and EXPIRED is the sweep below.
    status            VARCHAR(20)     NOT NULL DEFAULT 'PENDING',

    -- How many times the prompt has been sent. Re-sending is allowed — a message
    -- can be missed — but not without limit, or this becomes a way to make
    -- somebody's phone buzz all afternoon.
    send_count        SMALLINT UNSIGNED NOT NULL DEFAULT 0,

    -- Where the sign-in was attempted from, shown IN the Telegram message so the
    -- person can tell their own sign-in from somebody else's.
    request_ip        VARCHAR(64)     NULL,
    user_agent        VARCHAR(255)    NULL,

    created_at        DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    -- Minutes, not hours. A confirmation that is still valid tomorrow is a
    -- standing invitation to whoever picks the phone up.
    expires_at        DATETIME(3)     NOT NULL,
    approved_at       DATETIME(3)     NULL,
    consumed_at       DATETIME(3)     NULL,

    PRIMARY KEY (id),
    UNIQUE KEY ux_login_challenge_public_id (public_id),
    -- The lookup the webhook does on every button press.
    UNIQUE KEY ux_login_challenge_token     (token_hash),
    -- Serves the expiry sweep, and "does this account already have one pending".
    KEY ix_login_challenge_pending (status, expires_at),
    KEY ix_login_challenge_account (user_account_id, created_at),

    -- Deleting an account takes its pending sign-ins with it. There is nothing to
    -- keep: a challenge for an account that no longer exists can never be
    -- completed, and leaving it would be a row nothing can reach.
    CONSTRAINT fk_login_challenge_account FOREIGN KEY (user_account_id)
        REFERENCES user_account (id) ON DELETE CASCADE,

    CONSTRAINT ck_login_challenge_status CHECK (
        status IN ('PENDING','APPROVED','CONSUMED','DECLINED','EXPIRED')
    ),
    -- An approved row must say when. Without this the "was it approved before it
    -- expired" question has no answer.
    CONSTRAINT ck_login_challenge_approved CHECK (
        status <> 'APPROVED' OR approved_at IS NOT NULL
    ),
    CONSTRAINT ck_login_challenge_window CHECK (expires_at > created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- -----------------------------------------------------------------------------
-- 2. The switch
--
-- One organisation-wide setting, editable on the Settings screen, default OFF.
--
-- NOT per account, deliberately. A second factor that half the church has is a
-- policy nobody can describe, and the failure it guards against — somebody else
-- knowing a mobile number — applies to everyone equally. It is also the setting
-- somebody will need to turn OFF in a hurry if Telegram is down on a Sunday
-- morning, and one switch is findable under pressure in a way per-user checkboxes
-- are not.
--
-- WHAT HAPPENS TO SOMEBODY WITH NO TELEGRAM LINKED: they sign in as before. The
-- application cannot ask a phone it has no address for, and refusing them instead
-- would lock the church out of its own system the moment this is switched on.
-- `telegram.require_linking` is the setting that makes everyone have a link, and
-- the two are meant to be used together — the Settings screen says so.
-- -----------------------------------------------------------------------------
INSERT IGNORE INTO app_setting
    (setting_key, setting_value, value_type, category, description, is_editable)
VALUES
    ('telegram.verify_on_login', 'false', 'BOOLEAN', 'TELEGRAM',
     'Ask people to confirm each sign-in by tapping a button in Telegram. Only applies to accounts that have Telegram linked — turn on telegram.require_linking as well so that is everybody.',
     1);


-- -----------------------------------------------------------------------------
-- Verification
-- -----------------------------------------------------------------------------
SELECT COUNT(*) AS login_challenge_table
FROM information_schema.tables
WHERE table_schema = DATABASE() AND table_name = 'login_challenge';

SELECT setting_key, setting_value, value_type, category
FROM app_setting
WHERE setting_key = 'telegram.verify_on_login';

SELECT COUNT(*) AS challenges_outstanding FROM login_challenge;

-- ------------------------------- END 011_login_telegram_verification -------------------------------

