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
