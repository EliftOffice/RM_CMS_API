-- =============================================================================
-- 013 — Capacity is a weekly INTAKE limit
--
-- "Limited (1–2/week)" has to mean at most two NEW visitors in a week. It did
-- not. Eligibility compared `volunteer.current_case_load` against
-- `capacity_band.max_per_week`, and that counter is decremented every time a
-- case closes — so a volunteer on Limited who finished two follow-ups on Tuesday
-- was handed two more on Wednesday, and could take a dozen in a week while the
-- screen still said "1–2/week". The band read as a ceiling and behaved as a
-- queue depth.
--
-- The fix needs no new counter. `care_case_assignment` already records every
-- assignment with its timestamp and is never decremented — closing a case does
-- not touch it. Counting the distinct cases a volunteer was given since the
-- start of the current week is therefore the honest answer to "how many new
-- visitors have they taken this week", and it cannot be refunded by finishing
-- the work. Nothing here adds a column that could drift from that ledger.
--
-- So this migration adds only what makes that count cheap and correct:
-- an index to support it, and a setting for where a week begins.
--
-- Safe to re-run. The index creation checks information_schema and the setting
-- uses INSERT IGNORE.
-- =============================================================================

USE cms_api_db;

-- -----------------------------------------------------------------------------
-- 1. The index the weekly count reads
--
-- The existing ix_case_assignment_volunteer is (volunteer_id, unassigned_at),
-- which answers "what does this volunteer currently hold" and is no use at all
-- for "what were they given since Monday" — that scans every assignment they
-- have ever had. This one is read on EVERY assignment decision, for every
-- candidate volunteer, so it is worth its write cost.
-- -----------------------------------------------------------------------------
SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.statistics
      WHERE table_schema = DATABASE()
        AND table_name   = 'care_case_assignment'
        AND index_name   = 'ix_case_assignment_volunteer_week') = 0,
    'CREATE INDEX ix_case_assignment_volunteer_week
        ON care_case_assignment (volunteer_id, assigned_at)',
    'SELECT ''ix_case_assignment_volunteer_week already exists''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -----------------------------------------------------------------------------
-- 2. Where a week begins
--
-- 1 = Monday ... 7 = Sunday, the same convention as huddle.day_of_week and the
-- one the church says aloud.
--
-- It defaults to Monday because that is what the dashboards already assumed, so
-- applying this migration changes nobody's week silently. It is a setting rather
-- than a constant because the boundary is the whole rule: a church whose week
-- runs Sunday to Saturday would otherwise have every volunteer's allowance reset
-- in the middle of the Sunday service, which is precisely when the visitors
-- being assigned walked in.
-- -----------------------------------------------------------------------------
INSERT IGNORE INTO app_setting
    (setting_key, setting_value, value_type, category, description, min_value, max_value)
VALUES
    ('assignment.week_starts_on', '1', 'INTEGER', 'ASSIGNMENT',
     'Which day a capacity week begins on (1 = Monday ... 7 = Sunday). A volunteer''s weekly assignment allowance resets on this day.',
     1, 7);
