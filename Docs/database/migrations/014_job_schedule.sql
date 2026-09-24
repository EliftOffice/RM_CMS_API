-- =============================================================================
-- 014 — Scheduled jobs actually run on a schedule
--
-- Until now nothing ran on its own. JobsController says so in as many words:
-- "There is no in-process scheduler: an external cron calls these endpoints."
-- No external cron was ever set up, so every sweep only happened when an
-- administrator opened Settings and pressed "Run now". Cases sat unassigned,
-- nurture steps never advanced, and escalations were chased only by hand.
--
-- This adds the missing half: one row per job saying when it should run, and
-- the lease the application uses to run it exactly once.
--
-- WHY next_due_at IS STORED RATHER THAN COMPUTED PER TICK
--
-- The scheduler ticks every minute and has to answer "is anything due?" without
-- re-deriving a local-time rule for every job on every tick. Storing the answer
-- in UTC makes the query an index range scan, and — more importantly — makes the
-- CLAIM atomic: the conditional UPDATE below is what stops two application
-- instances running the same sweep twice, which is the exact objection that kept
-- a scheduler out of this application in the first place.
--
-- WHY THE LOCAL RULE IS KEPT ALONGSIDE IT
--
-- next_due_at alone cannot be re-derived after a restart or a settings change.
-- day_of_week, time_of_day and timezone are the rule; next_due_at is only the
-- cached next occurrence of it, recomputed after every run and whenever an
-- administrator edits the schedule.
--
-- TIMEZONE. Stored per row as an IANA id, defaulting to Asia/Kolkata — the same
-- default campus.timezone already uses. An administrator thinks in local time
-- ("Monday at six"), and every instant this application stores is UTC, so the
-- conversion has to live somewhere explicit rather than in a server's TZ.
--
-- WHAT IS ENABLED BY DEFAULT: assign-unassigned, and nothing else.
--
-- Deliberate. The other sweeps queue or send Telegram messages to real people,
-- and a migration must not start messaging a congregation on its own. They are
-- seeded disabled so they appear on the schedule screen ready to be turned on
-- once an administrator has decided the timing. Turning them on is one toggle.
--
-- Safe to re-run: CREATE TABLE IF NOT EXISTS and INSERT IGNORE throughout.
-- =============================================================================

USE cms_api_db;

-- -----------------------------------------------------------------------------
-- 1. The schedule
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS job_schedule (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,

    -- Matches job_run.job_name and JobNames in the application. One schedule per
    -- job, which is why this is unique rather than merely indexed.
    job_name        VARCHAR(80)     NOT NULL,

    is_enabled      TINYINT(1)      NOT NULL DEFAULT 0,

    -- DAILY ignores day_of_week. WEEKLY requires it.
    cadence         VARCHAR(10)     NOT NULL DEFAULT 'WEEKLY',

    -- ISO numbering, 1 = Monday ... 7 = Sunday, matching the numbering
    -- assignment.week_starts_on already uses. NULL only when cadence is DAILY.
    day_of_week     TINYINT         NULL,

    -- Local wall-clock time in `timezone`, to the minute. Seconds are not
    -- offered: a schedule screen that accepts them implies a precision a
    -- once-a-minute tick does not have.
    time_of_day     TIME            NOT NULL DEFAULT '06:00:00',

    timezone        VARCHAR(64)     NOT NULL DEFAULT 'Asia/Kolkata',

    -- The cached next occurrence, in UTC. NULL means "not scheduled" and is the
    -- resting state of a disabled row, so a disabled job can never be claimed by
    -- the due query no matter what its local rule says.
    next_due_at     DATETIME(3)     NULL,

    -- When the scheduler last CLAIMED this row. Not the same as the last entry
    -- in job_run: a run triggered by hand writes job_run and does not touch this.
    last_run_at     DATETIME(3)     NULL,

    -- Status of that last scheduled run, so the screen can show "last run
    -- succeeded" without joining job_run on every page load.
    last_status     VARCHAR(20)     NULL,

    created_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3)
                                    ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by      BIGINT UNSIGNED NULL,

    -- Optimistic concurrency, the same guard app_setting uses: two
    -- administrators editing the schedule must not silently overwrite each other.
    row_version     INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_job_schedule_name (job_name),

    -- The scheduler's only hot query: enabled rows that have come due.
    KEY ix_job_schedule_due (is_enabled, next_due_at),

    CONSTRAINT ck_job_schedule_cadence CHECK (cadence IN ('DAILY','WEEKLY')),

    -- The two halves have to agree, or a WEEKLY row with no day is silently
    -- unschedulable and nobody finds out until the sweep never happens.
    -- day_of_week IS NOT NULL is not redundant beside BETWEEN. A CHECK is satisfied
    -- unless it evaluates to FALSE, and NULL BETWEEN 1 AND 7 is NULL, not FALSE — so
    -- without it a WEEKLY row with no day was accepted, which is precisely the row
    -- this constraint exists to reject. Verified by inserting one.
    CONSTRAINT ck_job_schedule_day CHECK (
        (cadence = 'DAILY'  AND day_of_week IS NULL) OR
        (cadence = 'WEEKLY' AND day_of_week IS NOT NULL AND day_of_week BETWEEN 1 AND 7)
    ),

    CONSTRAINT ck_job_schedule_status CHECK (
        last_status IS NULL OR last_status IN ('RUNNING','SUCCEEDED','FAILED','PARTIAL')
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -----------------------------------------------------------------------------
-- 2. One row per known job
--
-- next_due_at is left NULL even for the enabled row. The application computes it
-- on startup from the local rule, which keeps exactly one implementation of
-- "when does Monday 06:00 Asia/Kolkata fall in UTC" — in C#, where the timezone
-- database is, rather than duplicated here in a SQL expression that would drift.
-- -----------------------------------------------------------------------------
INSERT IGNORE INTO job_schedule
    (job_name, is_enabled, cadence, day_of_week, time_of_day, timezone)
VALUES
    -- Monday morning, before the week's visiting starts. The one job enabled out
    -- of the box: it moves work to volunteers and sends nothing to anybody.
    ('assign-unassigned',  1, 'WEEKLY', 1, '06:00:00', 'Asia/Kolkata'),

    -- Daily by nature — a nurture step falls due on its own date, not on a
    -- weekday — but left off until an administrator chooses the hour.
    ('advance-nurture',    0, 'DAILY',  NULL, '06:15:00', 'Asia/Kolkata'),
    ('mark-overdue',       0, 'DAILY',  NULL, '06:30:00', 'Asia/Kolkata'),
    ('chase-escalations',  0, 'DAILY',  NULL, '07:00:00', 'Asia/Kolkata'),

    -- Harmless on the other six days: the job checks huddle.day_of_week itself
    -- and does nothing when today is not it.
    ('huddle-reminder',    0, 'DAILY',  NULL, '07:30:00', 'Asia/Kolkata'),

    -- Runs last. The sweeps above fill the queue this one drains, so scheduling
    -- it earlier in the morning would send yesterday's alerts and leave today's.
    ('send-notifications', 0, 'DAILY',  NULL, '08:00:00', 'Asia/Kolkata');
