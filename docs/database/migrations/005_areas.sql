-- =============================================================================
-- 005 — Areas
--
-- Adds a controlled list of localities ("areas") that a person can be filed
-- against, and points `person` at it.
--
-- Why a table rather than the free-text `person.locality` it sits beside:
-- locality was typed fresh every time, so the same neighbourhood arrived as
-- 'Kurnool Rd', 'kurnool road' and 'Kurnool  Road' and could never be grouped,
-- filtered or matched between a visitor and a volunteer who lives near them.
-- The area is picked from what already exists and only becomes a new row when
-- nothing matches.
--
-- `locality` is NOT dropped. It still holds what was typed for everyone
-- recorded before this, and the visitor screen still writes it for someone who
-- does NOT live locally — an out-of-town address is a one-off, not a
-- neighbourhood worth adding to a shared list.
--
-- Areas are scoped to a campus, like teams. An area is a place near one site;
-- offering Ongole's neighbourhoods to an operator at another campus would be a
-- picker whose entries can only ever be wrong.
--
-- Safe to re-run. Every statement is guarded: the table and column creations
-- check information_schema first, the setting uses INSERT IGNORE, and the
-- backfill only touches people who still have no area.
-- =============================================================================

USE cms_api_db;

-- -----------------------------------------------------------------------------
-- 1. area
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS area (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id       CHAR(26)        NOT NULL,
    campus_id       BIGINT UNSIGNED NOT NULL,

    -- As typed by whoever first recorded it, trimmed. This is what is shown.
    name            VARCHAR(100)    NOT NULL,

    -- Lower-cased with runs of whitespace collapsed. Matching and uniqueness
    -- use this and never `name`: the collation already ignores case, but it
    -- does not ignore a double space, and 'Kurnool  Road' would otherwise
    -- become a second area nobody can tell apart from the first.
    normalized_name VARCHAR(100)    NOT NULL,

    is_active       TINYINT(1)      NOT NULL DEFAULT 1,

    created_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    created_by      BIGINT UNSIGNED NULL,
    updated_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by      BIGINT UNSIGNED NULL,
    row_version     INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_area_public_id (public_id),
    -- The find-or-create at intake depends on this: two operators typing the
    -- same new area at the same moment must not produce two rows.
    UNIQUE KEY ux_area_campus_name (campus_id, normalized_name),
    KEY ix_area_campus_active (campus_id, is_active),

    CONSTRAINT fk_area_campus FOREIGN KEY (campus_id) REFERENCES campus (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- -----------------------------------------------------------------------------
-- 2. person.area_id
--
-- Nullable: somebody from out of town has no area, and every person recorded
-- before this migration has none until the backfill below finds one.
-- -----------------------------------------------------------------------------
SET @has_column := (
    SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE() AND table_name = 'person' AND column_name = 'area_id');

SET @sql := IF(@has_column = 0,
    'ALTER TABLE person
        ADD COLUMN area_id BIGINT UNSIGNED NULL AFTER locality,
        ADD KEY ix_person_area (area_id),
        ADD CONSTRAINT fk_person_area FOREIGN KEY (area_id) REFERENCES area (id)',
    'SELECT ''person.area_id already exists''');

PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- -----------------------------------------------------------------------------
-- 3. Who may manage areas
--
-- An administrator always may; that is not a setting and cannot be switched
-- off. This only widens it to the data-entry operators, who are the people
-- actually typing area names all day and therefore the ones who notice a
-- misspelling first. Default OFF — turning it on lets an intake account rename
-- and retire rows the whole organisation reads.
-- -----------------------------------------------------------------------------
INSERT IGNORE INTO app_setting
    (setting_key, setting_value, value_type, category, description, min_value, max_value)
VALUES
    ('area.manage_by_data_entry', 'false', 'BOOLEAN', 'AREA',
     'Data entry operators may open the area management screen', NULL, NULL);


-- -----------------------------------------------------------------------------
-- 4. Backfill from person.locality
--
-- Without this the picker starts empty and every operator retypes what is
-- already on file. One area per distinct locality per campus.
--
-- The whitespace pattern is written as the POSIX class [[:space:]]+ rather than
-- \s+. MySQL string literals swallow an unrecognised backslash escape, so a
-- single-backslash \s reaches the regex engine as a plain s and the pattern
-- silently collapses letters instead of spaces. The class has no backslash to
-- lose. AreaNames.Normalize in the application does the same job for rows
-- created from then on.
--
-- public_id is a readable placeholder in the same style as the seeded campus
-- ('01JCAMPUS0000000000000001') rather than a real ULID — these rows are
-- generated by a migration, not minted by the application, and nothing parses
-- the format. Rows created from here on get a real ULID from the service.
-- -----------------------------------------------------------------------------
INSERT IGNORE INTO area (public_id, campus_id, name, normalized_name)
SELECT
    CONCAT('01JAREA', LPAD(ROW_NUMBER() OVER (ORDER BY g.campus_id, g.normalized_name), 19, '0')),
    g.campus_id,
    g.name,
    g.normalized_name
FROM (
    SELECT
        p.campus_id                                          AS campus_id,
        -- One spelling wins for the display name. MIN is arbitrary but stable,
        -- and an administrator can rename it afterwards.
        MIN(TRIM(p.locality))                                AS name,
        LOWER(REGEXP_REPLACE(TRIM(p.locality), '[[:space:]]+', ' ')) AS normalized_name
    FROM person p
    WHERE p.campus_id IS NOT NULL
      AND p.deleted_at IS NULL
      AND p.locality IS NOT NULL
      AND TRIM(p.locality) <> ''
    -- ROW_NUMBER() sits OUTSIDE this subquery. Inside, its ORDER BY would be an
    -- expression the GROUP BY does not cover, which ONLY_FULL_GROUP_BY rejects.
    GROUP BY p.campus_id, LOWER(REGEXP_REPLACE(TRIM(p.locality), '[[:space:]]+', ' '))
) AS g;

UPDATE person p
JOIN area a
  ON a.campus_id = p.campus_id
 AND a.normalized_name = LOWER(REGEXP_REPLACE(TRIM(p.locality), '[[:space:]]+', ' '))
SET p.area_id = a.id
WHERE p.area_id IS NULL
  AND p.deleted_at IS NULL
  AND p.locality IS NOT NULL
  AND TRIM(p.locality) <> '';


-- -----------------------------------------------------------------------------
-- Verification
-- -----------------------------------------------------------------------------
SELECT COUNT(*) AS areas_created FROM area;

SELECT COUNT(*) AS people_with_an_area FROM person WHERE area_id IS NOT NULL;

SELECT setting_key, setting_value, description
FROM app_setting
WHERE setting_key = 'area.manage_by_data_entry';
