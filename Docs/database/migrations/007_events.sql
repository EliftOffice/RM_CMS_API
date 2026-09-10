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
