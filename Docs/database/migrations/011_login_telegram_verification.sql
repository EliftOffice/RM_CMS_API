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
