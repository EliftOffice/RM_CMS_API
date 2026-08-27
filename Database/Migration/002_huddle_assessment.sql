-- ============================================================================
-- 002 — Team Huddle: escalation assessment on care_interaction
--
-- WHAT THE HUDDLE IS
--   A weekly team meeting (Saturday in production: system_config.team_hurdle = 6)
--   where the team lead reviews the week's contacts and records, for each one,
--   whether the volunteer's escalation judgement was right:
--
--     CORRECT           they judged it well
--     UNDER_ESCALATED   they should have escalated and did not — a concern was
--                       heard and not passed on. This is the one that matters
--     OVER_ESCALATED    they escalated something routine, which costs the lead
--                       time and usually signals anxiety or a training gap
--
--   It is a coaching instrument, not a status meeting. It pairs with
--   volunteer_check_in: the huddle produces the evidence about a volunteer's
--   judgement, the check-in is where it is discussed with them.
--
-- WHY THIS IS A SCHEMA CHANGE
--   The MVP kept the verdict on `follow_ups.escalation_appropriate`. v2 collapsed
--   follow_ups and nurture_steps into `care_interaction`, and no equivalent column
--   was carried across — so the huddle had nowhere to write. This adds it.
--
-- WHAT CHANGED FROM THE MVP DESIGN
--   The MVP stored a verdict and nothing else. A coaching record with no reasoning
--   teaches nobody and cannot be referred back to at the check-in, so this also
--   keeps a note, who assessed it, and when.
--
-- SAFE TO RE-RUN.
-- ============================================================================

USE cms_api_db;

-- ---------------------------------------------------------------------------
-- 1 · The assessment columns
-- ---------------------------------------------------------------------------
SET @sql = (
    SELECT IF(
        EXISTS (SELECT 1 FROM information_schema.COLUMNS
                 WHERE TABLE_SCHEMA = 'cms_api_db'
                   AND TABLE_NAME = 'care_interaction'
                   AND COLUMN_NAME = 'escalation_assessment'),
        'SELECT ''escalation_assessment already present'' AS note',
        'ALTER TABLE care_interaction
            ADD COLUMN escalation_assessment VARCHAR(20)     NULL AFTER intent_code,
            ADD COLUMN assessment_note       VARCHAR(500)    NULL AFTER escalation_assessment,
            ADD COLUMN assessed_by           BIGINT UNSIGNED NULL AFTER assessment_note,
            ADD COLUMN assessed_at           DATETIME(3)     NULL AFTER assessed_by'
    )
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------------
-- 2 · Constraint and index
--
-- NULL means "not yet assessed", which is the overwhelming majority of rows and
-- is why it is a nullable column rather than a NOT NULL with a NOT_ASSESSED
-- default: the huddle queue is "WHERE escalation_assessment IS NULL", and an
-- index on a mostly-NULL column is exactly the cheap case.
-- ---------------------------------------------------------------------------
SET @sql = (
    SELECT IF(
        EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS
                 WHERE TABLE_SCHEMA = 'cms_api_db'
                   AND TABLE_NAME = 'care_interaction'
                   AND CONSTRAINT_NAME = 'ck_interaction_assessment'),
        'SELECT ''constraint already present'' AS note',
        'ALTER TABLE care_interaction
            ADD CONSTRAINT ck_interaction_assessment CHECK (
                escalation_assessment IS NULL OR
                escalation_assessment IN (''CORRECT'',''UNDER_ESCALATED'',''OVER_ESCALATED'')
            )'
    )
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = (
    SELECT IF(
        EXISTS (SELECT 1 FROM information_schema.STATISTICS
                 WHERE TABLE_SCHEMA = 'cms_api_db'
                   AND TABLE_NAME = 'care_interaction'
                   AND INDEX_NAME = 'ix_interaction_assessment'),
        'SELECT ''index already present'' AS note',
        -- Covers the huddle queue: unassessed contacts for a week, newest first.
        'ALTER TABLE care_interaction
            ADD KEY ix_interaction_assessment (escalation_assessment, occurred_at)'
    )
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = (
    SELECT IF(
        EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS
                 WHERE TABLE_SCHEMA = 'cms_api_db'
                   AND TABLE_NAME = 'care_interaction'
                   AND CONSTRAINT_NAME = 'fk_interaction_assessor'),
        'SELECT ''fk already present'' AS note',
        'ALTER TABLE care_interaction
            ADD CONSTRAINT fk_interaction_assessor
                FOREIGN KEY (assessed_by) REFERENCES user_account (id)'
    )
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------------
-- 3 · Settings
--
-- The huddle day carries over from the MVP's system_config.team_hurdle = 6.
-- MySQL DAYOFWEEK() is 1=Sunday..7=Saturday, but the MVP used 1=Monday..7=Sunday
-- and that is what the church says out loud, so the app's convention is kept and
-- converted in code rather than silently renumbered here.
-- ---------------------------------------------------------------------------
INSERT INTO app_setting (setting_key, setting_value, value_type, category, description, min_value, max_value)
VALUES
    ('huddle.day_of_week',   '6',    'INTEGER', 'HUDDLE', 'Team huddle day (1=Mon ... 7=Sun)',                    1, 7),
    ('huddle.lookback_days', '7',    'INTEGER', 'HUDDLE', 'How many days of contacts the huddle reviews',         1, 60),
    ('huddle.reminder_hour', '8',    'INTEGER', 'HUDDLE', 'Hour (UTC) the huddle reminder is sent on the day',    0, 23),
    ('huddle.remind_volunteers', 'true', 'BOOLEAN', 'HUDDLE', 'Also remind the volunteers, not just the lead', NULL, NULL)
ON DUPLICATE KEY UPDATE
    description = VALUES(description);

SELECT 'Huddle assessment columns and settings are in place.' AS status;
