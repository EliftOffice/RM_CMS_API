-- ============================================================
-- Nurture Sequence Schema
-- Adds 7-step Call/Visit follow-up journey per person
-- Triggered when initial follow-up is closed as "Normal"
-- ============================================================

-- TABLE 1: nurture_sequences
-- One row per person's nurture journey
CREATE TABLE `nurture_sequences` (
  `sequence_id`          varchar(20)  NOT NULL,
  `person_id`            varchar(20)  NOT NULL,
  `volunteer_id`         varchar(20)  NOT NULL,         -- same volunteer as initial follow-up
  `team_lead_id`         varchar(20)  DEFAULT NULL,
  `current_step`         int(11)      NOT NULL DEFAULT 1,
  `status`               varchar(20)  NOT NULL DEFAULT 'Active',
  -- status values: Active | InReview | Permanent | Failed
  `started_at`           timestamp    NOT NULL DEFAULT current_timestamp(),
  `completed_at`         timestamp    NULL     DEFAULT NULL,
  `final_notes`          text         DEFAULT NULL,     -- TL notes when closing
  `created_at`           timestamp    NOT NULL DEFAULT current_timestamp(),
  `updated_at`           timestamp    NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`sequence_id`),
  KEY `idx_ns_person`   (`person_id`),
  KEY `idx_ns_volunteer`(`volunteer_id`),
  KEY `idx_ns_status`   (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- TABLE 2: nurture_steps
-- 7 rows created upfront when the sequence starts
-- Alternates Call / Visit / Call / Visit / Call / Visit / Call
CREATE TABLE `nurture_steps` (
  `step_id`              varchar(20)  NOT NULL,
  `sequence_id`          varchar(20)  NOT NULL,
  `person_id`            varchar(20)  NOT NULL,
  `volunteer_id`         varchar(20)  NOT NULL,
  `step_number`          int(11)      NOT NULL,         -- 1 to 7
  `method`               varchar(10)  NOT NULL,         -- Call | Visit
  `scheduled_date`       date         NOT NULL,         -- start_date + (step_number - 1) * 7 days
  `status`               varchar(20)  NOT NULL DEFAULT 'Pending',
  -- status values: Pending | Done | Missed
  `contact_status`       varchar(30)  DEFAULT NULL,     -- Contacted | Not Contacted
  `response_type`        varchar(30)  DEFAULT NULL,     -- Normal | No Response | Needs Follow-Up | Crisis
  `notes`                text         DEFAULT NULL,
  `completed_at`         timestamp    NULL DEFAULT NULL,
  `created_at`           timestamp    NOT NULL DEFAULT current_timestamp(),
  `updated_at`           timestamp    NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`step_id`),
  KEY `idx_nst_sequence`      (`sequence_id`),
  KEY `idx_nst_person`        (`person_id`),
  KEY `idx_nst_volunteer`     (`volunteer_id`),
  KEY `idx_nst_scheduled`     (`scheduled_date`),
  KEY `idx_nst_status`        (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- people.follow_up_status — new values introduced:
--   'IN_NURTURE'  : active in the 7-step sequence
--   'IN_REVIEW'   : step 7 done, awaiting TL final decision
--   'PERMANENT'   : marked as permanent member
--   'FAILED'      : dropped off after sequence
