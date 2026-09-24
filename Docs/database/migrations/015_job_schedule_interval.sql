-- =============================================================================
-- 015 — A volunteer hears about their case within minutes, not the next morning
--
-- Everything needed to tell a volunteer already existed. All four routes that can
-- place a case with somebody — a team lead assigning by hand, the intake desk
-- recording a visitor, the assign-unassigned sweep, the advance-nurture sweep —
-- call AssignmentNotifier, which queues a CASE_ASSIGNED alert. The template
-- exists, the composer renders it, and NotificationSender delivers it to that
-- volunteer's Telegram chat.
--
-- The alert was never delivered because nothing drained the queue. Migration 014
-- gave send-notifications a schedule but left it switched off, and the coarsest
-- cadence it offered was DAILY: a volunteer assigned a case at ten in the morning
-- would have heard about it at eight the next day. For an alert whose whole
-- purpose is "this is yours now", tomorrow is the same as never.
--
-- So this adds a third cadence — EVERY n minutes — and switches the sender on at
-- five. The queue is kept rather than bypassed on purpose: sending inside the
-- assignment itself would put a Telegram round trip in the path of placing a
-- case, and would lose the retry budget that makes a minute of Telegram being
-- unreachable survivable.
--
-- WHY NOT SIMPLY SEND AT ASSIGNMENT TIME
--
-- AssignmentNotifier's own comment says the alert must never undo the placement.
-- A queue drained often keeps that promise AND is prompt; a direct send gives up
-- the first to buy the second.
--
-- Safe to re-run: every step checks information_schema first.
-- =============================================================================

USE cms_api_db;

-- -----------------------------------------------------------------------------
-- 1. How often, for the cadences that are an interval rather than a clock time
--
-- NULL for DAILY and WEEKLY, where time_of_day is the rule instead. Capped at a
-- day by the constraint below: anything longer is a DAILY schedule written
-- badly, and would drift by a few minutes every run.
-- -----------------------------------------------------------------------------
SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_schema = DATABASE()
        AND table_name   = 'job_schedule'
        AND column_name  = 'interval_minutes') = 0,
    'ALTER TABLE job_schedule
        ADD COLUMN interval_minutes SMALLINT UNSIGNED NULL AFTER time_of_day',
    'SELECT ''job_schedule.interval_minutes already exists''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -----------------------------------------------------------------------------
-- 2. Let the cadence be EVERY
-- -----------------------------------------------------------------------------
SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.table_constraints
      WHERE table_schema     = DATABASE()
        AND table_name       = 'job_schedule'
        AND constraint_name  = 'ck_job_schedule_cadence') > 0,
    'ALTER TABLE job_schedule DROP CHECK ck_job_schedule_cadence',
    'SELECT ''ck_job_schedule_cadence not present''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

ALTER TABLE job_schedule
    ADD CONSTRAINT ck_job_schedule_cadence
    CHECK (cadence IN ('DAILY','WEEKLY','EVERY'));

-- -----------------------------------------------------------------------------
-- 3. One constraint for the whole shape, replacing the day-only one
--
-- Each cadence now decides TWO nullable columns, and checking them separately
-- would let an EVERY row keep a stale day, or a WEEKLY row carry an interval
-- that nothing reads.
--
-- NOTE THE `IS NOT NULL` BESIDE EVERY `BETWEEN`. A CHECK is satisfied unless it
-- evaluates to FALSE, and `NULL BETWEEN 1 AND 7` is NULL — not FALSE. The first
-- draft of the day constraint in 014 omitted exactly that and silently accepted
-- the malformed row it existed to reject.
-- -----------------------------------------------------------------------------
SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.table_constraints
      WHERE table_schema    = DATABASE()
        AND table_name      = 'job_schedule'
        AND constraint_name = 'ck_job_schedule_day') > 0,
    'ALTER TABLE job_schedule DROP CHECK ck_job_schedule_day',
    'SELECT ''ck_job_schedule_day not present''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.table_constraints
      WHERE table_schema    = DATABASE()
        AND table_name      = 'job_schedule'
        AND constraint_name = 'ck_job_schedule_shape') > 0,
    'ALTER TABLE job_schedule DROP CHECK ck_job_schedule_shape',
    'SELECT ''ck_job_schedule_shape not present''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

ALTER TABLE job_schedule
    ADD CONSTRAINT ck_job_schedule_shape CHECK (
        (cadence = 'DAILY'
            AND day_of_week IS NULL
            AND interval_minutes IS NULL) OR
        (cadence = 'WEEKLY'
            AND day_of_week IS NOT NULL AND day_of_week BETWEEN 1 AND 7
            AND interval_minutes IS NULL) OR
        (cadence = 'EVERY'
            AND day_of_week IS NULL
            AND interval_minutes IS NOT NULL AND interval_minutes BETWEEN 1 AND 1440)
    );

-- -----------------------------------------------------------------------------
-- 4. Switch the sender on
--
-- Five minutes. Short enough that a volunteer assigned a case has heard about it
-- before they would have noticed on the screen, long enough that the queue is
-- read 288 times a day rather than 1,440 — and an empty queue costs one indexed
-- SELECT that returns nothing.
--
-- next_due_at is left alone: the application recomputes it on its next tick, the
-- same as any other schedule change. Turning this on delivers the backlog that
-- has been accumulating, which is the point.
--
-- This is the one job whose enabling is NOT left to an administrator, because
-- without it every other alert in the system is composed, recorded as sent, and
-- read by nobody.
-- -----------------------------------------------------------------------------
UPDATE job_schedule
SET cadence          = 'EVERY',
    interval_minutes = 5,
    day_of_week      = NULL,
    is_enabled       = 1,
    next_due_at      = NULL
WHERE job_name = 'send-notifications';
