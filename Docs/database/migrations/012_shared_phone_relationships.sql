-- =============================================================================
-- 012 — Shared phone numbers and family relationships
--
-- A family shares one phone. The intake screen used to treat a second person on
-- a number as a mistake to be argued past: it refused the save, and the operator
-- either pressed "Save anyway" — recording a person with no hint of who they
-- belong to — or gave up and left the wife off the register entirely.
--
-- Nothing in the DATABASE ever forbade this. `person_contact` is unique on
-- (person_id, contact_type, normalized_value), so the same number on two
-- different people has always been legal. The refusal was in the application
-- alone. What was missing was somewhere to record the ANSWER to the obvious
-- question: if this number is already John's, who is this person to John?
--
-- THE MODEL IS A STAR, NOT A GRAPH. Everyone on a number hangs off the first
-- person registered with it — the base visitor — and the relationship is always
-- stored relative to THEM, never to whoever was added most recently. A chain
-- ("Mary is David's mother, David is John's son") would need walking to answer
-- "who is this household?", and would change meaning if somebody in the middle
-- were deleted. A star answers it with one index lookup and cannot drift.
--
-- Hence exactly two columns rather than a join table: a person has at most one
-- base visitor, and that is the whole shape of the data.
--
-- NO BACKFILL. People who already share a number are left alone. The earliest
-- of them is still treated as the base at runtime — that is derived from the
-- contact rows, not from these columns — but nothing here writes a relationship
-- nobody stated. Some of those existing pairs are genuine families and some are
-- the same person recorded twice; guessing between them in a migration would put
-- invented family structure into a pastoral record.
--
-- Safe to re-run. Every statement is guarded: table and column creation check
-- information_schema first, and the seed uses INSERT IGNORE.
-- =============================================================================

USE cms_api_db;

-- -----------------------------------------------------------------------------
-- 1. relationship_type
--
-- The vocabulary, as data. This file's list is a starting point, not a
-- definition: a church that needs "Grandmother" or "Guardian" adds a row, and
-- the intake screen offers it without a deployment — the same arrangement as
-- care_outcome and capacity_band.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS relationship_type (
    code        VARCHAR(30)     NOT NULL,
    label       VARCHAR(60)     NOT NULL,

    -- Controls the order of the options on screen. Spouse first, then children,
    -- then parents, then siblings, with the two catch-alls last: that is the
    -- order a welcome desk actually needs them in, not alphabetical.
    sort_order  INT             NOT NULL DEFAULT 0,

    -- Retiring a relationship must not rewrite history. Deactivating hides it
    -- from the picker while every person already recorded against it keeps it.
    is_active   TINYINT(1)      NOT NULL DEFAULT 1,

    PRIMARY KEY (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT IGNORE INTO relationship_type (code, label, sort_order) VALUES
    ('WIFE',     'Wife',     10),
    ('HUSBAND',  'Husband',  20),
    ('SON',      'Son',      30),
    ('DAUGHTER', 'Daughter', 40),
    ('FATHER',   'Father',   50),
    ('MOTHER',   'Mother',   60),
    ('BROTHER',  'Brother',  70),
    ('SISTER',   'Sister',   80),
    ('RELATIVE', 'Relative', 90),
    ('OTHER',    'Other',   100);

-- -----------------------------------------------------------------------------
-- 2. person.base_person_id
--
-- The first person registered on this number. Null means this person IS the base
-- visitor, which is also the state of everybody whose number nobody shares.
-- -----------------------------------------------------------------------------
SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_schema = DATABASE()
        AND table_name   = 'person'
        AND column_name  = 'base_person_id') = 0,
    'ALTER TABLE person ADD COLUMN base_person_id BIGINT UNSIGNED NULL AFTER household_type',
    'SELECT ''person.base_person_id already exists''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -----------------------------------------------------------------------------
-- 3. person.relationship_code
--
-- What this person is TO the base visitor: Mary is the WIFE of John. Read the
-- pair in that direction and nothing else — the reverse is not stored, because
-- storing both halves means they can disagree.
-- -----------------------------------------------------------------------------
SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_schema = DATABASE()
        AND table_name   = 'person'
        AND column_name  = 'relationship_code') = 0,
    'ALTER TABLE person ADD COLUMN relationship_code VARCHAR(30) NULL AFTER base_person_id',
    'SELECT ''person.relationship_code already exists''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -----------------------------------------------------------------------------
-- 4. Index and keys
--
-- The index earns its place on the read "everybody who belongs to this base
-- visitor", which is how a household is drawn on a person's record.
-- -----------------------------------------------------------------------------
SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.statistics
      WHERE table_schema = DATABASE()
        AND table_name   = 'person'
        AND index_name   = 'ix_person_base') = 0,
    'CREATE INDEX ix_person_base ON person (base_person_id)',
    'SELECT ''ix_person_base already exists''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- No referential action, so RESTRICT. Deliberately not CASCADE, which would
-- delete a whole family because the first of them was removed, and deliberately
-- not SET NULL either: MySQL refuses a CHECK constraint on a column an action
-- writes to, and the two checks below are worth more than an action that can
-- practically never fire. People are SOFT-deleted here — `deleted_at`, because
-- pastoral history references them — so a hard delete of a base visitor is not
-- part of normal operation. If one is ever attempted, failing loudly is the
-- right answer.
SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.table_constraints
      WHERE table_schema    = DATABASE()
        AND table_name      = 'person'
        AND constraint_name = 'fk_person_base') = 0,
    'ALTER TABLE person ADD CONSTRAINT fk_person_base
        FOREIGN KEY (base_person_id) REFERENCES person (id)',
    'SELECT ''fk_person_base already exists''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.table_constraints
      WHERE table_schema    = DATABASE()
        AND table_name      = 'person'
        AND constraint_name = 'fk_person_relationship') = 0,
    'ALTER TABLE person ADD CONSTRAINT fk_person_relationship
        FOREIGN KEY (relationship_code) REFERENCES relationship_type (code)',
    'SELECT ''fk_person_relationship already exists''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- The two columns are meaningless apart: a relationship to nobody says nothing,
-- and a base visitor with no stated relationship is the "someone shares this
-- number but we never asked" state this migration exists to end. This is why
-- fk_person_base above carries no ON DELETE action — MySQL will not accept a
-- CHECK over a column a referential action assigns to.
SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.table_constraints
      WHERE table_schema    = DATABASE()
        AND table_name      = 'person'
        AND constraint_name = 'ck_person_base_pair') = 0,
    'ALTER TABLE person ADD CONSTRAINT ck_person_base_pair CHECK (
        (base_person_id IS NULL AND relationship_code IS NULL)
     OR (base_person_id IS NOT NULL AND relationship_code IS NOT NULL))',
    'SELECT ''ck_person_base_pair already exists''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- NOBODY IS THEIR OWN RELATIVE, and that one is NOT a constraint here.
--
-- `CHECK (base_person_id <> id)` is the obvious way to say it and MySQL rejects
-- it outright: a check constraint may not refer to an auto-increment column.
-- So the rule lives in PeopleService, which resolves the base visitor from the
-- contact rows and never assigns a person to themselves — the id it would need
-- does not exist yet at the point the base is chosen, because the row is not
-- written until after.
--
-- Recorded here rather than left silent, so the next person to read this file
-- does not add the constraint, watch it fail, and conclude the migration is
-- broken.
