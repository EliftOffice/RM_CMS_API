-- =============================================================================
-- RM_CMS — CANONICAL DATABASE SCHEMA
--
-- This is the single source of truth for the database structure.
-- It replaces and supersedes:
--     db_schema.sql
--     nurture_sequence_schema.sql
--     auth_schema.sql
--     latest_schema.sql            (production dump — contains PII, never commit)
--     Documentation/DB_Schema/schema.sql
--
-- CONTENTS
--     Section 1 .. Structure (tables, keys, indexes, foreign keys)
--     Section 2 .. Reference data (config defaults, capacity bands, roles)
--     Section 3 .. Upgrade path for an EXISTING database
--     Section 4 .. One-time migration: back-fill staff auth accounts
--
-- USAGE
--     Fresh install:      run sections 1 and 2.
--     Existing database:  run sections 1 and 2 (idempotent), then 3, then 4.
--
-- CONVENTIONS (applied consistently — the old files did not)
--     * Engine  : InnoDB
--     * Charset : utf8mb4 / utf8mb4_general_ci everywhere.
--                 Chosen over utf8mb4_0900_ai_ci because that collation does not
--                 exist on MariaDB. Mixing the two caused "Illegal mix of
--                 collations" on any cross-table string join.
--     * Business keys : varchar(20) — 'P001', 'V001', 'TL001'. Longest real value
--                 in production is 8 characters.
--     * Timestamps    : UTC. The application supplies values via TimeProvider.
--     * GUID columns  : varchar(36), NOT char(36). MySqlConnector silently converts
--                 CHAR(36) to System.Guid, which Dapper cannot map onto the string
--                 properties this codebase uses ("Object must implement IConvertible").
--
-- CONTAINS NO PERSONAL DATA AND NO SECRETS. Do not paste production dumps here.
-- =============================================================================

SET FOREIGN_KEY_CHECKS = 0;
SET NAMES utf8mb4;


-- =============================================================================
-- SECTION 1 — STRUCTURE
-- Tables are declared in dependency order so foreign keys can be inline rather
-- than bolted on by trailing ALTER statements.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1.1  Organisation
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `team_leads` (
  `team_lead_id`         varchar(20)  NOT NULL,
  `first_name`           varchar(50)  NOT NULL,
  `last_name`            varchar(50)  NOT NULL,
  `email`                varchar(100) NOT NULL,
  `phone`                varchar(20)  DEFAULT NULL,
  `role_type`            varchar(30)  NOT NULL,          -- full-time | player-coach
  `campus`               varchar(50)  DEFAULT NULL,
  `start_date`           date         NOT NULL,
  `term_end_date`        date         DEFAULT NULL,
  `max_volunteers`       int          NOT NULL,
  `current_volunteers`   int          DEFAULT 0,
  `team_vnps_avg`        decimal(5,2) DEFAULT NULL,
  `team_retention_rate`  decimal(5,2) DEFAULT NULL,
  `team_completion_rate` decimal(5,2) DEFAULT NULL,
  `boundary_incidents`   int          DEFAULT 0,
  `status`               varchar(20)  NOT NULL DEFAULT 'Active',
  `telegram_chat_id`     varchar(45)  DEFAULT NULL,
  `created_at`           timestamp    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`           timestamp    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`team_lead_id`),
  KEY `idx_tl_status` (`status`),
  KEY `idx_tl_campus` (`campus`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `capacity_bands` (
  `band_name`    varchar(20) NOT NULL,                   -- Limited | Balanced | Consistent
  `min_per_week` int         NOT NULL,
  `max_per_week` int         NOT NULL,
  `description`  text,
  PRIMARY KEY (`band_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `volunteers` (
  `volunteer_id`           varchar(20)  NOT NULL,
  `first_name`             varchar(50)  NOT NULL,
  `last_name`              varchar(50)  NOT NULL,
  `email`                  varchar(100) NOT NULL,
  `phone`                  varchar(20)  DEFAULT NULL,
  `status`                 varchar(30)  NOT NULL,        -- Active | Inactive | ...
  `level`                  varchar(20)  NOT NULL,
  `start_date`             date         NOT NULL,
  `end_date`               date         DEFAULT NULL,

  -- Capacity. Mirrors capacity_bands; see the note in section 2.
  `capacity_band`          varchar(20)  NOT NULL,
  `capacity_min`           int          NOT NULL,
  `capacity_max`           int          NOT NULL,
  `current_assignments`    int          DEFAULT 0,
  `total_completed`        int          DEFAULT 0,
  `total_assigned`         int          DEFAULT 0,

  `completion_rate`        decimal(5,2) DEFAULT NULL,
  `avg_response_time`      decimal(5,2) DEFAULT NULL,
  `last_check_in`          date         DEFAULT NULL,
  `next_check_in`          date         DEFAULT NULL,
  `emotional_tone`         varchar(10)  DEFAULT NULL,
  `vnps_score`             int          DEFAULT NULL,
  `burnout_risk`           varchar(20)  DEFAULT NULL,

  `team_lead`              varchar(20)  DEFAULT NULL,
  `campus`                 varchar(50)  DEFAULT 'Ongole',

  -- Safeguarding / onboarding milestones
  `level_0_complete`       date         DEFAULT NULL,
  `crisis_trained`         date         DEFAULT NULL,
  `confidentiality_signed` date         DEFAULT NULL,
  `background_check`       date         DEFAULT NULL,
  `boundary_violations`    int          DEFAULT 0,
  `last_violation_date`    date         DEFAULT NULL,

  `telegram_chat_id`       bigint       DEFAULT NULL,
  `last_assigned_at`       datetime     DEFAULT NULL,

  `created_at`             timestamp    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`             timestamp    NULL     DEFAULT NULL ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`volunteer_id`),
  KEY `idx_vol_team_lead` (`team_lead`),
  KEY `idx_vol_status`    (`status`),
  KEY `idx_vol_campus`    (`campus`),
  -- Supports the assignment picker: active volunteers in a campus with spare capacity.
  KEY `idx_vol_assign`    (`status`, `campus`, `current_assignments`),
  CONSTRAINT `fk_volunteers_team_lead`
    FOREIGN KEY (`team_lead`) REFERENCES `team_leads` (`team_lead_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


-- -----------------------------------------------------------------------------
-- 1.2  People and the follow-up journey
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `people` (
  `id`                 int          NOT NULL AUTO_INCREMENT,   -- surrogate, insertion order
  `person_id`          varchar(20)  NOT NULL,                  -- business key, 'P001'
  `first_name`         varchar(50)  NOT NULL,
  `last_name`          varchar(50)  NOT NULL,
  `email`              varchar(100) DEFAULT NULL,
  `phone`              varchar(20)  DEFAULT NULL,
  `age_range`          varchar(20)  DEFAULT NULL,
  `household_type`     varchar(50)  DEFAULT NULL,
  `zip_code`           varchar(10)  DEFAULT NULL,
  `address`            varchar(100) DEFAULT NULL,
  `location_type`      varchar(45)  DEFAULT 'Local',

  `visit_type`         varchar(30)  NOT NULL,
  `first_visit_date`   date         NOT NULL,
  `last_visit_date`    date         DEFAULT NULL,
  `visit_count`        int          DEFAULT 1,
  `connection_source`  varchar(50)  DEFAULT NULL,
  `campus`             varchar(50)  DEFAULT NULL,

  -- Lifecycle: Pending -> In Progress -> IN_NURTURE -> IN_REVIEW -> PERMANENT | FAILED
  `follow_up_status`   varchar(30)  NOT NULL,
  `follow_up_priority` varchar(20)  DEFAULT NULL,
  `assigned_volunteer` varchar(20)  DEFAULT NULL,
  `assigned_date`      date         DEFAULT NULL,
  `last_contact_date`  date         DEFAULT NULL,
  `next_action_date`   date         DEFAULT NULL,

  `interested_in`      text,
  `prayer_requests`    text,
  `specific_needs`     text,
  `reference_name`     varchar(100) DEFAULT NULL,
  `reference_phone`    varchar(100) DEFAULT NULL,

  `created_at`         timestamp    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`         timestamp    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`         varchar(50)  DEFAULT NULL,

  PRIMARY KEY (`person_id`),
  UNIQUE KEY `uq_people_id` (`id`),
  KEY `idx_people_volunteer`   (`assigned_volunteer`),
  KEY `idx_people_status`      (`follow_up_status`),
  KEY `idx_people_next_action` (`next_action_date`),
  KEY `idx_people_campus`      (`campus`),
  CONSTRAINT `fk_people_volunteer`
    FOREIGN KEY (`assigned_volunteer`) REFERENCES `volunteers` (`volunteer_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `follow_ups` (
  -- NOTE: previously had no PRIMARY KEY at all, only a UNIQUE index. An InnoDB
  -- table without a declared PK falls back to a hidden clustered index, which
  -- hurts secondary-index lookups and breaks row-based replication tooling.
  `follow_up_id`           varchar(20) NOT NULL,
  `person_id`              varchar(20) NOT NULL,
  `volunteer_id`           varchar(20) NOT NULL,
  `team_lead_id`           varchar(20) DEFAULT NULL,

  `attempt_number`         int         NOT NULL,
  `attempt_date`           date        NOT NULL,
  `attempt_time`           time        DEFAULT NULL,
  `contact_method`         varchar(20) DEFAULT NULL,
  `contact_status`         varchar(30) NOT NULL,          -- Contacted | Not Contacted
  `response_type`          varchar(30) DEFAULT NULL,      -- Normal | Needs Follow-Up | Crisis | No Response
  `call_duration_min`      int         DEFAULT NULL,

  `next_action`            varchar(50) DEFAULT NULL,
  `next_action_date`       date        DEFAULT NULL,
  `escalation_tier`        varchar(20) DEFAULT NULL,
  `escalation_appropriate` enum('Correct','Under-Escalation','Over-Escalation','Not-Assessed')
                                       DEFAULT 'Not-Assessed',

  `notes`                  text,
  `tags`                   varchar(200) DEFAULT NULL,

  -- Denormalised reporting buckets, written by the application.
  `week_number`            int         DEFAULT NULL,
  `month_number`           int         DEFAULT NULL,
  `quarter_number`         int         DEFAULT NULL,
  `year`                   int         DEFAULT NULL,

  `created_at`             timestamp   NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`             timestamp   NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`follow_up_id`),
  KEY `idx_fu_person`    (`person_id`),
  KEY `idx_fu_volunteer` (`volunteer_id`),
  KEY `idx_fu_team_lead` (`team_lead_id`),
  -- Supports "latest attempt for this person".
  KEY `idx_fu_person_attempt` (`person_id`, `attempt_number`),
  CONSTRAINT `fk_follow_ups_person`    FOREIGN KEY (`person_id`)    REFERENCES `people`     (`person_id`),
  CONSTRAINT `fk_follow_ups_volunteer` FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers` (`volunteer_id`),
  CONSTRAINT `fk_follow_ups_team_lead` FOREIGN KEY (`team_lead_id`) REFERENCES `team_leads` (`team_lead_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `escalations` (
  `id`                       int         NOT NULL AUTO_INCREMENT,
  `escalation_id`            varchar(20) NOT NULL,
  `follow_up_id`             varchar(20) NOT NULL,
  `person_id`                varchar(20) NOT NULL,
  `volunteer_id`             varchar(20) NOT NULL,
  `team_lead_id`             varchar(20) DEFAULT NULL,

  `escalation_date`          date        NOT NULL,
  `notified_at`              datetime    DEFAULT NULL,
  `acknowledged_at`          datetime    DEFAULT NULL,
  `escalation_tier`          enum('Standard','Urgent','Emergency') NOT NULL,
  `escalation_reason`        enum('Crisis','Needs Follow-Up','Financial Crisis','Health Crisis',
                                  'Marriage Crisis','Spiritual Questions','Grief/Loss',
                                  'Abuse Disclosure','Suicidal Ideation','Other') NOT NULL,
  `description`              text        NOT NULL,
  `status`                   enum('New','In Progress','Resolved','Referred Out','Closed') NOT NULL,

  `assigned_to`              varchar(50) DEFAULT NULL,
  `resolved_date`            date        DEFAULT NULL,
  `resolution_notes`         text,
  `outcome`                  enum('Connected to Resource','Pastoral Care Scheduled','Counseling Referral',
                                  'Benevolence Provided','Emergency Services Called','Other') DEFAULT NULL,
  `resource_connected`       varchar(100) DEFAULT NULL,
  `follow_up_scheduled`      tinyint(1)  DEFAULT 0,
  `crisis_protocol_followed` tinyint(1)  DEFAULT NULL,
  `authorities_contacted`    tinyint(1)  DEFAULT NULL,
  `volunteer_debriefed`      tinyint(1)  DEFAULT NULL,

  `created_at`               timestamp   NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`               timestamp   NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`escalation_id`),
  UNIQUE KEY `uq_esc_id` (`id`),
  KEY `idx_esc_follow_up` (`follow_up_id`),
  KEY `idx_esc_person`    (`person_id`),
  KEY `idx_esc_volunteer` (`volunteer_id`),
  KEY `idx_esc_team_lead` (`team_lead_id`),
  -- Supports the team-lead pending-escalations queue.
  KEY `idx_esc_tl_status` (`team_lead_id`, `status`),
  CONSTRAINT `fk_escalations_follow_up` FOREIGN KEY (`follow_up_id`) REFERENCES `follow_ups` (`follow_up_id`),
  CONSTRAINT `fk_escalations_person`    FOREIGN KEY (`person_id`)    REFERENCES `people`     (`person_id`),
  CONSTRAINT `fk_escalations_volunteer` FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers` (`volunteer_id`),
  CONSTRAINT `fk_escalations_team_lead` FOREIGN KEY (`team_lead_id`) REFERENCES `team_leads` (`team_lead_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


-- -----------------------------------------------------------------------------
-- 1.3  Volunteer care
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `check_ins` (
  `id`                        int         NOT NULL AUTO_INCREMENT,
  `check_in_id`               varchar(20) NOT NULL,
  `volunteer_id`              varchar(20) NOT NULL,
  `team_lead_id`              varchar(20) NOT NULL,
  `check_in_date`             date        NOT NULL DEFAULT (CURDATE()),
  `duration_min`              int         DEFAULT NULL,
  -- Was utf8mb4_0900_ai_ci while its own table was utf8mb4_general_ci.
  `meeting_type`              varchar(30) DEFAULT 'Monthly',
  `emotional_tone`            varchar(10) NOT NULL,
  `capacity_adjustment`       tinyint(1)  DEFAULT 0,
  `new_capacity_band`         varchar(20) DEFAULT NULL,
  `concerns_noted`            text,
  `follow_up_needed`          tinyint(1)  DEFAULT 0,
  `completion_rate_discussed` tinyint(1)  DEFAULT NULL,
  `boundary_issues`           tinyint(1)  DEFAULT 0,
  `training_needs`            text,
  `action_items`              text,
  `next_check_in_date`        date        DEFAULT NULL,
  `created_at`                timestamp   NOT NULL DEFAULT CURRENT_TIMESTAMP,

  PRIMARY KEY (`check_in_id`),
  UNIQUE KEY `uq_ci_id` (`id`),
  KEY `idx_ci_volunteer` (`volunteer_id`),
  KEY `idx_ci_team_lead` (`team_lead_id`),
  CONSTRAINT `fk_check_ins_volunteer` FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers` (`volunteer_id`),
  CONSTRAINT `fk_check_ins_team_lead` FOREIGN KEY (`team_lead_id`) REFERENCES `team_leads` (`team_lead_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `capacity_history` (
  `history_id`        varchar(20) NOT NULL,
  `volunteer_id`      varchar(20) NOT NULL,
  `change_date`       date        NOT NULL,
  `old_capacity_band` varchar(20) DEFAULT NULL,
  `new_capacity_band` varchar(20) NOT NULL,
  `change_reason`     varchar(50) DEFAULT NULL,
  `initiated_by`      varchar(50) NOT NULL,
  `notes`             text,
  `created_at`        timestamp   NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`history_id`),
  KEY `idx_ch_volunteer` (`volunteer_id`),
  CONSTRAINT `fk_capacity_history_volunteer`
    FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers` (`volunteer_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `vnps_surveys` (
  `survey_id`           varchar(20) NOT NULL,
  `volunteer_id`        varchar(20) NOT NULL,
  `survey_date`         date        NOT NULL,
  `quarter`             varchar(10) NOT NULL,
  `year`                int         NOT NULL,
  `vnps_score`          int         NOT NULL,
  `vnps_category`       varchar(20) DEFAULT NULL,
  `what_working_well`   text,
  `what_could_improve`  text,
  `additional_feedback` text,
  `sentiment`           varchar(20) DEFAULT NULL,
  `created_at`          timestamp   NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`survey_id`),
  KEY `idx_vnps_volunteer` (`volunteer_id`),
  CONSTRAINT `fk_vnps_volunteer`
    FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers` (`volunteer_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `notes` (
  -- Polymorphic by design: entity_type/entity_id can point at a person, a
  -- volunteer or a team lead, so no foreign key is possible here.
  `note_id`         varchar(20)  NOT NULL,
  `entity_type`     varchar(20)  NOT NULL,
  `entity_id`       varchar(20)  NOT NULL,
  `note_type`       varchar(30)  DEFAULT NULL,
  `note_text`       text         NOT NULL,
  `tags`            varchar(200) DEFAULT NULL,
  `is_private`      tinyint(1)   DEFAULT 0,
  `visible_to_role` varchar(30)  DEFAULT NULL,
  `created_by`      varchar(50)  NOT NULL,
  `created_at`      timestamp    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`      timestamp    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`note_id`),
  KEY `idx_notes_entity` (`entity_type`, `entity_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


-- -----------------------------------------------------------------------------
-- 1.4  Nurture sequence (7-step Call/Visit journey)
--      These two tables previously had NO foreign keys at all, so orphaned
--      sequences and steps were possible.
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `nurture_sequences` (
  `sequence_id`  varchar(20) NOT NULL,
  `person_id`    varchar(20) NOT NULL,
  `volunteer_id` varchar(20) NOT NULL,                   -- same volunteer as the initial follow-up
  `team_lead_id` varchar(20) DEFAULT NULL,
  `current_step` int         NOT NULL DEFAULT 1,
  `status`       varchar(20) NOT NULL DEFAULT 'Active',  -- Active | InReview | Permanent | Failed
  `started_at`   timestamp   NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `completed_at` timestamp   NULL     DEFAULT NULL,
  `final_notes`  text,
  `created_at`   timestamp   NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`   timestamp   NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`sequence_id`),
  KEY `idx_ns_person`    (`person_id`),
  KEY `idx_ns_volunteer` (`volunteer_id`),
  KEY `idx_ns_status`    (`status`),
  KEY `idx_ns_team_lead` (`team_lead_id`),
  CONSTRAINT `fk_ns_person`    FOREIGN KEY (`person_id`)    REFERENCES `people`     (`person_id`),
  CONSTRAINT `fk_ns_volunteer` FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers` (`volunteer_id`),
  CONSTRAINT `fk_ns_team_lead` FOREIGN KEY (`team_lead_id`) REFERENCES `team_leads` (`team_lead_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `nurture_steps` (
  `step_id`        varchar(20) NOT NULL,
  `sequence_id`    varchar(20) NOT NULL,
  `person_id`      varchar(20) NOT NULL,
  `volunteer_id`   varchar(20) NOT NULL,
  `step_number`    int         NOT NULL,                 -- 1..7
  `method`         varchar(10) NOT NULL,                 -- Call | Visit (alternating)
  `scheduled_date` date        NOT NULL,                 -- start + (step_number - 1) * 7 days
  `status`         varchar(20) NOT NULL DEFAULT 'Pending', -- Pending | Done | Missed
  `contact_status` varchar(30) DEFAULT NULL,
  `response_type`  varchar(30) DEFAULT NULL,
  `notes`          text,
  `completed_at`   timestamp   NULL DEFAULT NULL,
  `created_at`     timestamp   NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`     timestamp   NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`step_id`),
  UNIQUE KEY `uq_nst_sequence_step` (`sequence_id`, `step_number`),
  KEY `idx_nst_person`    (`person_id`),
  KEY `idx_nst_volunteer` (`volunteer_id`),
  KEY `idx_nst_scheduled` (`scheduled_date`),
  KEY `idx_nst_status`    (`status`),
  -- Supports the "steps due for this volunteer" query.
  KEY `idx_nst_due`       (`volunteer_id`, `status`, `scheduled_date`),
  CONSTRAINT `fk_nst_sequence`
    FOREIGN KEY (`sequence_id`) REFERENCES `nurture_sequences` (`sequence_id`) ON DELETE CASCADE,
  CONSTRAINT `fk_nst_person`    FOREIGN KEY (`person_id`)    REFERENCES `people`     (`person_id`),
  CONSTRAINT `fk_nst_volunteer` FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers` (`volunteer_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


-- -----------------------------------------------------------------------------
-- 1.5  Events and attendance
--
--      `app_users` is a separate, simpler identity used only by event check-in.
--      It overlaps with auth_users and should eventually be merged — see the
--      note at the end of this file.
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `app_users` (
  `id`            bigint       NOT NULL AUTO_INCREMENT,
  `name`          varchar(150) NOT NULL,
  `mobile_number` varchar(30)  NOT NULL,
  `created_at`    datetime     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    datetime     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_app_users_mobile` (`mobile_number`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `events` (
  `id`                      bigint        NOT NULL AUTO_INCREMENT,
  `title`                   varchar(200)  NOT NULL,
  `venue_name`              varchar(200)  NOT NULL,
  `address`                 text          NOT NULL,
  `latitude`                decimal(10,7) NOT NULL,
  `longitude`               decimal(10,7) NOT NULL,
  `radius`                  int           NOT NULL,        -- geofence, metres
  `start_time`              datetime      NOT NULL,
  `end_time`                datetime      NOT NULL,
  `is_active`               tinyint(1)    NOT NULL DEFAULT 1,
  `recurrence_type`         varchar(30)   NOT NULL DEFAULT 'once',
  `recurrence_day`          varchar(20)   DEFAULT NULL,
  `repeat_until`            date          DEFAULT NULL,
  `reuse_same_location`     tinyint(1)    NOT NULL DEFAULT 1,
  `auto_activate_recurring` tinyint(1)    NOT NULL DEFAULT 1,
  `created_at`              datetime      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`              datetime      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `idx_events_active`    (`is_active`),
  KEY `idx_events_time`      (`start_time`, `end_time`),
  KEY `idx_events_recurring` (`recurrence_type`, `recurrence_day`, `repeat_until`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `attendances` (
  `id`              bigint        NOT NULL AUTO_INCREMENT,
  `user_id`         bigint        NOT NULL,
  `event_id`        bigint        NOT NULL,
  -- Denormalised copy of events.title, kept so historic attendance still reads
  -- correctly if an event is renamed.
  `event_title`     varchar(200)  NOT NULL,
  `attendance_day`  date          NOT NULL,
  `checkin_time`    datetime      NOT NULL,
  `latitude`        decimal(10,7) NOT NULL,
  `longitude`       decimal(10,7) NOT NULL,
  `device_info`     varchar(255)  DEFAULT NULL,
  `created_at`      datetime      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_attendance_user_event_day` (`user_id`, `event_id`, `attendance_day`),
  KEY `idx_attendance_event`     (`event_id`),
  KEY `idx_attendance_user_time` (`user_id`, `checkin_time`),
  KEY `idx_attendance_day`       (`attendance_day`),
  CONSTRAINT `fk_attendance_user`  FOREIGN KEY (`user_id`)  REFERENCES `app_users` (`id`),
  CONSTRAINT `fk_attendance_event` FOREIGN KEY (`event_id`) REFERENCES `events`    (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


-- -----------------------------------------------------------------------------
-- 1.6  Configuration
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `system_config` (
  `config_key`   varchar(50)  NOT NULL,
  `config_value` varchar(200) NOT NULL,
  `config_type`  varchar(20)  NOT NULL,                   -- bool | integer | string
  `description`  text,
  `updated_at`   timestamp    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `updated_by`   varchar(50)  DEFAULT NULL,
  PRIMARY KEY (`config_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


-- -----------------------------------------------------------------------------
-- 1.7  Authentication and authorization
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `auth_users` (
  `user_id`              varchar(36)  NOT NULL,           -- GUID; see the note on varchar vs char
  `username`             varchar(100) NOT NULL,           -- login handle (mobile number today)
  `normalized_username`  varchar(100) NOT NULL,           -- upper-invariant, used for lookup
  `email`                varchar(150) DEFAULT NULL,
  `mobile_number`        varchar(30)  NOT NULL,
  `display_name`         varchar(150) NOT NULL,

  -- Empty string means "no usable password"; the login path refuses it.
  `password_hash`        varchar(500) NOT NULL DEFAULT '',
  `security_stamp`       varchar(36)  NOT NULL,           -- rotates on any credential/role change
  `token_version`        int          NOT NULL DEFAULT 1, -- coarse kill-switch for access tokens

  `is_active`            tinyint(1)   NOT NULL DEFAULT 1,
  `must_change_password` tinyint(1)   NOT NULL DEFAULT 0,
  `access_failed_count`  int          NOT NULL DEFAULT 0,
  `lockout_end_utc`      datetime     DEFAULT NULL,
  `last_login_utc`       datetime     DEFAULT NULL,
  `password_changed_utc` datetime     DEFAULT NULL,

  `volunteer_id`         varchar(20)  DEFAULT NULL,
  `team_lead_id`         varchar(20)  DEFAULT NULL,
  `person_id`            varchar(20)  DEFAULT NULL,

  `row_version`          bigint       NOT NULL DEFAULT 1, -- optimistic concurrency
  `created_at`           datetime     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`           datetime     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`           varchar(100) DEFAULT NULL,

  PRIMARY KEY (`user_id`),
  UNIQUE KEY `uq_auth_users_username` (`normalized_username`),
  KEY `idx_auth_users_mobile`    (`mobile_number`),
  KEY `idx_auth_users_volunteer` (`volunteer_id`),
  KEY `idx_auth_users_teamlead`  (`team_lead_id`),
  KEY `idx_auth_users_active`    (`is_active`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `auth_roles` (
  `role_name`   varchar(30)  NOT NULL,
  `description` varchar(200) DEFAULT NULL,
  `created_at`  datetime     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`role_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `auth_user_roles` (
  `user_id`     varchar(36)  NOT NULL,
  `role_name`   varchar(30)  NOT NULL,
  `assigned_at` datetime     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `assigned_by` varchar(100) DEFAULT NULL,
  PRIMARY KEY (`user_id`, `role_name`),
  KEY `idx_aur_role` (`role_name`),
  CONSTRAINT `fk_aur_user` FOREIGN KEY (`user_id`)   REFERENCES `auth_users` (`user_id`) ON DELETE CASCADE,
  CONSTRAINT `fk_aur_role` FOREIGN KEY (`role_name`) REFERENCES `auth_roles` (`role_name`) ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `auth_refresh_tokens` (
  -- Only the SHA-256 hash is stored, so a database dump yields no usable tokens.
  -- family_id groups one login's rotation chain, so detecting reuse can revoke it whole.
  `token_id`             varchar(36) NOT NULL,
  `user_id`              varchar(36) NOT NULL,
  `family_id`            varchar(36) NOT NULL,
  `token_hash`           char(64)    NOT NULL,
  `parent_token_id`      varchar(36) DEFAULT NULL,
  `replaced_by_token_id` varchar(36) DEFAULT NULL,

  `expires_utc`          datetime    NOT NULL,
  `created_utc`          datetime    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `revoked_utc`          datetime    DEFAULT NULL,
  `revoked_reason`       varchar(60) DEFAULT NULL,

  `device_label`         varchar(120) DEFAULT NULL,
  `user_agent_hash`      char(64)     DEFAULT NULL,
  `created_ip`           varchar(45)  DEFAULT NULL,

  PRIMARY KEY (`token_id`),
  UNIQUE KEY `uq_art_hash` (`token_hash`),
  KEY `idx_art_user`    (`user_id`),
  KEY `idx_art_family`  (`family_id`),
  KEY `idx_art_expires` (`expires_utc`),
  CONSTRAINT `fk_art_user` FOREIGN KEY (`user_id`) REFERENCES `auth_users` (`user_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `auth_password_history` (
  `history_id`    bigint       NOT NULL AUTO_INCREMENT,
  `user_id`       varchar(36)  NOT NULL,
  `password_hash` varchar(500) NOT NULL,
  `created_utc`   datetime     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`history_id`),
  KEY `idx_aph_user` (`user_id`, `created_utc`),
  CONSTRAINT `fk_aph_user` FOREIGN KEY (`user_id`) REFERENCES `auth_users` (`user_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `auth_login_audit` (
  -- Outcomes only. Never stores a password, token, hash or secret.
  -- Intentionally has NO foreign key on user_id: audit rows must survive the
  -- deletion of the account they describe.
  `audit_id`           bigint       NOT NULL AUTO_INCREMENT,
  `user_id`            varchar(36)  DEFAULT NULL,
  `username_attempted` varchar(100) DEFAULT NULL,
  `event_type`         varchar(40)  NOT NULL,
  `succeeded`          tinyint(1)   NOT NULL DEFAULT 0,
  `detail`             varchar(300) DEFAULT NULL,
  `ip_address`         varchar(45)  DEFAULT NULL,
  `user_agent`         varchar(300) DEFAULT NULL,
  `correlation_id`     varchar(36)  DEFAULT NULL,
  `created_utc`        datetime     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`audit_id`),
  KEY `idx_ala_user`    (`user_id`, `created_utc`),
  KEY `idx_ala_event`   (`event_type`, `created_utc`),
  KEY `idx_ala_created` (`created_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


SET FOREIGN_KEY_CHECKS = 1;


-- =============================================================================
-- SECTION 2 — REFERENCE DATA
--
-- Configuration and lookup values required for the application to run.
-- Deliberately contains NO people, volunteers, team leads or demo records —
-- those are operational data, and the old db_schema.sql shipping them made it
-- unsafe to run against a real database.
-- =============================================================================

INSERT INTO `capacity_bands` (`band_name`, `min_per_week`, `max_per_week`, `description`) VALUES
  ('Limited',    1, 2, 'Reduced load: new, recovering or time-constrained volunteers'),
  ('Balanced',   2, 3, 'Standard sustainable load'),
  ('Consistent', 4, 6, 'Experienced volunteers with proven capacity')
ON DUPLICATE KEY UPDATE
  `min_per_week` = VALUES(`min_per_week`),
  `max_per_week` = VALUES(`max_per_week`),
  `description`  = VALUES(`description`);


INSERT INTO `auth_roles` (`role_name`, `description`) VALUES
  ('Admin',     'Full system administration, configuration and user management'),
  ('Pastor',    'Cross-team oversight dashboards, read-mostly'),
  ('TeamLead',  'Manages a team of volunteers: escalations, check-ins, nurture review'),
  ('Volunteer', 'Handles assigned people: follow-ups and nurture steps'),
  ('Member',    'Authenticated end user: own attendance and events only')
ON DUPLICATE KEY UPDATE `description` = VALUES(`description`);


-- Business rules. Safe to re-run: existing values are preserved, only missing
-- keys are inserted, so an administrator's tuning is never overwritten.
INSERT IGNORE INTO `system_config` (`config_key`, `config_value`, `config_type`, `description`, `updated_by`) VALUES
  ('assign_immediately',          'true', 'bool',    'Assign a new person on creation instead of waiting for the batch job', 'schema'),
  ('limited_min',                 '1',    'integer', 'Minimum follow-ups per week for the Limited band',    'schema'),
  ('limited_max',                 '2',    'integer', 'Maximum follow-ups per week for the Limited band',    'schema'),
  ('balanced_min',                '2',    'integer', 'Minimum follow-ups per week for the Balanced band',   'schema'),
  ('balanced_max',                '3',    'integer', 'Maximum follow-ups per week for the Balanced band',   'schema'),
  ('consistent_min',              '4',    'integer', 'Minimum follow-ups per week for the Consistent band', 'schema'),
  ('consistent_max',              '6',    'integer', 'Maximum follow-ups per week for the Consistent band', 'schema'),
  ('max_retry_attempts',          '3',    'integer', 'Contact attempts before a person is marked unresponsive', 'schema'),
  ('retry_delay_days',            '3',    'integer', 'Days to wait before a retry attempt',                 'schema'),
  ('response_time_target',        '48',   'integer', 'Target hours for the first contact attempt',          'schema'),
  ('check_in_frequency_days',     '30',   'integer', 'Days between team-lead check-ins',                    'schema'),
  ('green_threshold',             '90',   'integer', 'Completion rate % for a green flag',                  'schema'),
  ('yellow_threshold',            '75',   'integer', 'Completion rate % for a yellow flag',                 'schema'),
  ('red_threshold',               '74',   'integer', 'Completion rate % at or below which the flag is red', 'schema'),
  ('team_lead_span_full_time',    '12',   'integer', 'Maximum volunteers for a full-time team lead',        'schema'),
  ('team_lead_span_player_coach', '8',    'integer', 'Maximum volunteers for a player-coach team lead',     'schema'),
  ('vnps_frequency_months',       '3',    'integer', 'Months between volunteer NPS surveys',                'schema');

-- NOTE: `telegram_bot_token` is deliberately NOT seeded here. It is a live secret
-- and should be moved out of this table into an environment variable — see
-- "Tech Debt and Risks" in the project notes.


-- =============================================================================
-- SECTION 3 — UPGRADING AN EXISTING DATABASE
--
-- Section 1 only creates tables that do not exist, so it will not alter a
-- database that predates this file. Run the statements below ONCE to bring an
-- existing database in line. Each is safe and independently revertible.
--
-- TAKE A BACKUP FIRST:
--     mysqldump -u root cms_api_db > backup_before_cleanup.sql
-- =============================================================================

-- 3.1  Normalise collations. `events`, `attendances` and `app_users` were created
--      as utf8mb4_0900_ai_ci while everything else was utf8mb4_general_ci, which
--      made any string join between them fail with "Illegal mix of collations".
--
-- ALTER TABLE `app_users`   CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
-- ALTER TABLE `events`      CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
-- ALTER TABLE `attendances` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
-- ALTER TABLE `check_ins`   MODIFY `meeting_type` varchar(30)
--     CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT 'Monthly';

-- 3.2  Narrow people.person_id from varchar(50) to varchar(20) so it matches every
--      table that references it. Verified safe: the longest live value is 8 chars.
--      Foreign keys must be dropped and re-added around the change.
--
-- ALTER TABLE `people` MODIFY `person_id` varchar(20) NOT NULL;

-- 3.3  Give follow_ups a real primary key. It previously had only a UNIQUE index.
--
-- ALTER TABLE `follow_ups` ADD PRIMARY KEY (`follow_up_id`);

-- 3.4  Add the missing referential integrity on the nurture tables.
--      Verified against live data: zero orphan rows, so these will apply cleanly.
--
-- ALTER TABLE `nurture_sequences`
--   ADD CONSTRAINT `fk_ns_person`    FOREIGN KEY (`person_id`)    REFERENCES `people`     (`person_id`),
--   ADD CONSTRAINT `fk_ns_volunteer` FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers` (`volunteer_id`),
--   ADD CONSTRAINT `fk_ns_team_lead` FOREIGN KEY (`team_lead_id`) REFERENCES `team_leads` (`team_lead_id`);
--
-- ALTER TABLE `nurture_steps`
--   ADD CONSTRAINT `fk_nst_sequence`  FOREIGN KEY (`sequence_id`)  REFERENCES `nurture_sequences` (`sequence_id`) ON DELETE CASCADE,
--   ADD CONSTRAINT `fk_nst_person`    FOREIGN KEY (`person_id`)    REFERENCES `people`            (`person_id`),
--   ADD CONSTRAINT `fk_nst_volunteer` FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers`        (`volunteer_id`);

-- 3.5  Drop the dead `users` table. It is referenced by no C# code and by no
--      other table; `volunteers`, `team_leads` and `auth_users` cover its role.
--      Confirmed 2026-08-08 that this schema is used only by this repository.
--
-- DROP TABLE IF EXISTS `users`;

-- 3.6  Performance indexes added by this file that an older database will lack.
--
-- ALTER TABLE `volunteers`    ADD KEY `idx_vol_assign` (`status`, `campus`, `current_assignments`);
-- ALTER TABLE `follow_ups`    ADD KEY `idx_fu_person_attempt` (`person_id`, `attempt_number`);
-- ALTER TABLE `escalations`   ADD KEY `idx_esc_tl_status` (`team_lead_id`, `status`);
-- ALTER TABLE `nurture_steps` ADD KEY `idx_nst_due` (`volunteer_id`, `status`, `scheduled_date`);
-- ALTER TABLE `notes`         ADD KEY `idx_notes_entity` (`entity_type`, `entity_id`);


-- =============================================================================
-- SECTION 4 — ONE-TIME MIGRATION: staff authentication accounts
--
-- Creates a DISABLED, password-less auth account for every active volunteer and
-- team lead. They CANNOT sign in until an administrator enables them and issues
-- a first password via POST /api/admin/auth/users/{id}/set-password:
--     password_hash = ''  -> rejected by the login path
--     is_active     = 0   -> rejected by the login path
--
-- Idempotent: re-running will not duplicate accounts.
-- =============================================================================

-- 4.1  Team leads
INSERT INTO `auth_users`
  (`user_id`, `username`, `normalized_username`, `email`, `mobile_number`, `display_name`,
   `password_hash`, `security_stamp`, `token_version`, `is_active`, `must_change_password`,
   `team_lead_id`, `created_by`)
SELECT
  UUID(), tl.`phone`, UPPER(tl.`phone`), tl.`email`, tl.`phone`,
  CONCAT(tl.`first_name`, ' ', tl.`last_name`),
  '', UUID(), 1, 0, 1, tl.`team_lead_id`, 'migration:schema.sql'
FROM `team_leads` tl
WHERE tl.`phone` IS NOT NULL AND tl.`phone` <> ''
  AND NOT EXISTS (SELECT 1 FROM `auth_users` au WHERE au.`normalized_username` = UPPER(tl.`phone`));

INSERT IGNORE INTO `auth_user_roles` (`user_id`, `role_name`, `assigned_by`)
SELECT au.`user_id`, 'TeamLead', 'migration:schema.sql'
FROM `auth_users` au WHERE au.`team_lead_id` IS NOT NULL;

-- 4.2  Volunteers
INSERT INTO `auth_users`
  (`user_id`, `username`, `normalized_username`, `email`, `mobile_number`, `display_name`,
   `password_hash`, `security_stamp`, `token_version`, `is_active`, `must_change_password`,
   `volunteer_id`, `created_by`)
SELECT
  UUID(), v.`phone`, UPPER(v.`phone`), v.`email`, v.`phone`,
  CONCAT(v.`first_name`, ' ', v.`last_name`),
  '', UUID(), 1, 0, 1, v.`volunteer_id`, 'migration:schema.sql'
FROM `volunteers` v
WHERE v.`phone` IS NOT NULL AND v.`phone` <> ''
  AND NOT EXISTS (SELECT 1 FROM `auth_users` au WHERE au.`normalized_username` = UPPER(v.`phone`));

INSERT IGNORE INTO `auth_user_roles` (`user_id`, `role_name`, `assigned_by`)
SELECT au.`user_id`, 'Volunteer', 'migration:schema.sql'
FROM `auth_users` au WHERE au.`volunteer_id` IS NOT NULL;


-- =============================================================================
-- HOUSEKEEPING (run periodically, e.g. nightly)
-- =============================================================================
-- DELETE FROM `auth_refresh_tokens` WHERE `expires_utc` < (UTC_TIMESTAMP() - INTERVAL 30 DAY);
-- DELETE FROM `auth_login_audit`    WHERE `created_utc` < (UTC_TIMESTAMP() - INTERVAL 180 DAY);


-- =============================================================================
-- KNOWN REMAINING ISSUES (not fixed here — they need code changes too)
--
--  1. `app_users` duplicates `auth_users`. Event check-in uses app_users.id while
--     everything else uses auth_users.user_id. Merging them means reworking
--     attendances.user_id and the Attendance module.
--  2. Volunteer workload is tracked twice: the `volunteers.current_assignments`
--     counter and the live count of assigned `people`. They can drift.
--  3. Capacity bands exist both in `capacity_bands` and as `*_min`/`*_max` keys in
--     `system_config`. Two sources of truth for the same rule.
--  4. `people`, `escalations` and `check_ins` each carry both a surrogate
--     AUTO_INCREMENT `id` and a business key. Harmless, but inconsistent.
-- =============================================================================
