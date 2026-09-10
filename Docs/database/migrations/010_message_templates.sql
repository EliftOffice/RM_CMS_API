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
