-- =============================================================================
-- 006 — Website enquiries
--
-- Everything the public website collects, and the role that reviews it.
--
-- WHY A SEPARATE TABLE RATHER THAN WRITING STRAIGHT INTO `person`:
-- this endpoint is open to the internet. Writing anonymous submissions directly
-- into the pastoral records would let anyone on earth create people, and the
-- first spam run would put junk beside real prayer requests and crisis
-- escalations with no way to tell them apart afterwards. A submission lands
-- here as raw, untrusted input; a human with the WEB_COORDINATOR role reads it
-- and decides what it becomes. `linked_person_id` records that decision when it
-- is made.
--
-- The form fields are the ones the site actually has today (the Bible Study
-- Groups registration and the prayer request). `payload` carries anything a
-- future form adds, so a new field on the website does not require a migration
-- before the site can ship.
--
-- Safe to re-run: guarded table creation and INSERT IGNORE for the role.
-- =============================================================================

USE cms_api_db;

-- -----------------------------------------------------------------------------
-- 1. web_enquiry
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS web_enquiry (
    id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    public_id           CHAR(26)        NOT NULL,
    -- Human-readable handle a coordinator can quote ('W0001'). Display only.
    reference_code      VARCHAR(20)     NULL,

    -- Which form it came from. Not a FK to a lookup table: the website owns its
    -- own form list and can add one without a schema change here. Validated
    -- against a known set in the service so an unknown value is refused at the
    -- edge rather than stored and puzzled over later.
    form_type           VARCHAR(40)     NOT NULL,

    -- Where the site says this belongs. Nullable: the public site does not
    -- necessarily know, and guessing would file people at the wrong site.
    campus_id           BIGINT UNSIGNED NULL,

    -- ---- what the visitor typed -------------------------------------------
    -- All nullable. A prayer request has only a message; a registration has
    -- everything but a message. One table, two shapes, and the service enforces
    -- which fields each form_type actually requires.
    full_name           VARCHAR(160)    NULL,
    mobile              VARCHAR(20)     NULL,
    -- Digits only, for matching against person_contact.normalized_value without
    -- re-parsing what was typed.
    mobile_normalized   VARCHAR(20)     NULL,
    email               VARCHAR(255)    NULL,

    city                VARCHAR(100)    NULL,
    street              VARCHAR(200)    NULL,
    landmark            VARCHAR(200)    NULL,

    referred_by_name    VARCHAR(160)    NULL,
    referred_by_mobile  VARCHAR(20)     NULL,

    -- Prayer request text, or any free-text message.
    message             TEXT            NULL,

    -- Anything the form sent that has no column. JSON so a new website field is
    -- captured from day one and can be promoted to a column later if it proves
    -- worth querying.
    payload             JSON            NULL,

    -- ---- provenance --------------------------------------------------------
    -- The page it was submitted from, for telling the BSG form on one ministry
    -- page apart from the same form somewhere else.
    source_page         VARCHAR(255)    NULL,

    -- Truncated. Useful for spotting a bot flood; not treated as identity.
    user_agent          VARCHAR(255)    NULL,

    -- SHA-256 of the caller's IP salted with the submission DATE. Deliberately
    -- not the raw IP: the only question a coordinator needs answered is "did
    -- these forty submissions all come from one place today", and a hash that
    -- rotates daily answers it without keeping a permanent identifier for
    -- somebody who only ever filled in a prayer request. It is a grouping key,
    -- not evidence.
    submitter_hash      CHAR(64)        NULL,

    -- Set when the honeypot field was filled in — only a bot does that. Kept
    -- rather than dropped so the volume of attempts is visible.
    is_suspected_spam   TINYINT(1)      NOT NULL DEFAULT 0,

    -- ---- triage ------------------------------------------------------------
    status              VARCHAR(20)     NOT NULL DEFAULT 'NEW',
    reviewed_by         BIGINT UNSIGNED NULL,
    reviewed_at         DATETIME(3)     NULL,
    review_note         VARCHAR(1000)   NULL,

    -- Set when a coordinator turns this into a real record. Null until then,
    -- which is what makes "what still needs looking at" a plain query.
    linked_person_id    BIGINT UNSIGNED NULL,

    submitted_at        DATETIME(3)     NOT NULL,

    created_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    updated_by          BIGINT UNSIGNED NULL,
    row_version         INT UNSIGNED    NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY ux_web_enquiry_public_id      (public_id),
    UNIQUE KEY ux_web_enquiry_reference_code (reference_code),
    -- The coordinator's list: newest unhandled first.
    KEY ix_web_enquiry_status    (status, submitted_at),
    KEY ix_web_enquiry_form      (form_type, submitted_at),
    KEY ix_web_enquiry_mobile    (mobile_normalized),
    KEY ix_web_enquiry_submitter (submitter_hash, submitted_at),

    CONSTRAINT fk_web_enquiry_campus   FOREIGN KEY (campus_id)        REFERENCES campus (id),
    CONSTRAINT fk_web_enquiry_person   FOREIGN KEY (linked_person_id) REFERENCES person (id),
    CONSTRAINT fk_web_enquiry_reviewer FOREIGN KEY (reviewed_by)      REFERENCES user_account (id),

    CONSTRAINT ck_web_enquiry_status CHECK (
        status IN ('NEW','IN_REVIEW','ACTIONED','SPAM','CLOSED')
    ),
    -- A reviewed row must say who and when, or the audit trail is decorative.
    CONSTRAINT ck_web_enquiry_reviewed CHECK (
        status = 'NEW' OR (reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL)
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- -----------------------------------------------------------------------------
-- 2. The role that reads them
--
-- A side role, like DATA_ENTRY — not a rung on the Volunteer → Team Lead →
-- Pastor ladder. Someone who works the website enquiry list is not partway to
-- being a pastor, and putting them on the ladder would offer promotions that
-- mean nothing.
--
-- hierarchy_level 25 sits just above DATA_ENTRY (20): both are intake roles,
-- and this one reads unfiltered public input, which is the more sensitive of
-- the two.
-- -----------------------------------------------------------------------------
INSERT IGNORE INTO app_role (code, label, description, hierarchy_level, is_assignable)
VALUES ('WEB_COORDINATOR', 'Website Coordinator',
        'Reviews enquiries submitted through the public website and decides what happens to them',
        25, 1);


-- -----------------------------------------------------------------------------
-- Verification
-- -----------------------------------------------------------------------------
SELECT COUNT(*) AS web_enquiry_table_exists
FROM information_schema.tables
WHERE table_schema = DATABASE() AND table_name = 'web_enquiry';

SELECT code, label, hierarchy_level FROM app_role ORDER BY hierarchy_level;
