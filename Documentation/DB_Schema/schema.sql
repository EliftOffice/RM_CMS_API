-- phpMyAdmin SQL Dump
-- version 5.2.3
-- https://www.phpmyadmin.net/
--
-- Host: unntyizfev069tijgce0conw
-- Generation Time: Jun 24, 2026 at 01:51 PM
-- Server version: 8.4.8
-- PHP Version: 8.5.4

SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
START TRANSACTION;
SET time_zone = "+00:00";


/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;

--
-- Database: `rm_cms_api`
--

-- --------------------------------------------------------

--
-- Table structure for table `app_users`
--

CREATE TABLE `app_users` (
  `id` bigint NOT NULL,
  `name` varchar(150) NOT NULL,
  `mobile_number` varchar(30) NOT NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

--
-- Dumping data for table `app_users`
--



-- --------------------------------------------------------

--
-- Table structure for table `attendances`
--

CREATE TABLE `attendances` (
  `id` bigint NOT NULL,
  `user_id` bigint NOT NULL,
  `event_id` bigint NOT NULL,
  `event_title` varchar(200) NOT NULL,
  `attendance_day` date NOT NULL,
  `checkin_time` datetime NOT NULL,
  `latitude` decimal(10,7) NOT NULL,
  `longitude` decimal(10,7) NOT NULL,
  `device_info` varchar(255) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

--
-- Dumping data for table `attendances`
--


-- --------------------------------------------------------

--
-- Table structure for table `capacity_bands`
--

CREATE TABLE `capacity_bands` (
  `band_name` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `min_per_week` int NOT NULL,
  `max_per_week` int NOT NULL,
  `description` text COLLATE utf8mb4_general_ci
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `capacity_bands`
--


-- --------------------------------------------------------

--
-- Table structure for table `capacity_history`
--

CREATE TABLE `capacity_history` (
  `history_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `volunteer_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `change_date` date NOT NULL,
  `old_capacity_band` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `new_capacity_band` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `change_reason` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `initiated_by` varchar(50) COLLATE utf8mb4_general_ci NOT NULL,
  `notes` text COLLATE utf8mb4_general_ci,
  `created_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `check_ins`
--

CREATE TABLE `check_ins` (
  `id` int NOT NULL,
  `check_in_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `volunteer_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `team_lead_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `check_in_date` date NOT NULL DEFAULT (curdate()),
  `duration_min` int DEFAULT NULL,
  `meeting_type` varchar(30) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT 'Monthly',
  `emotional_tone` varchar(10) COLLATE utf8mb4_general_ci NOT NULL,
  `capacity_adjustment` tinyint(1) DEFAULT '0',
  `new_capacity_band` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `concerns_noted` text COLLATE utf8mb4_general_ci,
  `follow_up_needed` tinyint(1) DEFAULT '0',
  `completion_rate_discussed` tinyint(1) DEFAULT NULL,
  `boundary_issues` tinyint(1) DEFAULT '0',
  `training_needs` text COLLATE utf8mb4_general_ci,
  `action_items` text COLLATE utf8mb4_general_ci,
  `next_check_in_date` date DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `escalations`
--

CREATE TABLE `escalations` (
  `id` int NOT NULL,
  `escalation_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `follow_up_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `person_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `volunteer_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `team_lead_id` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `escalation_date` date NOT NULL,
  `notified_at` datetime DEFAULT NULL,
  `escalation_tier` enum('Standard','Urgent','Emergency') COLLATE utf8mb4_general_ci NOT NULL,
  `escalation_reason` enum('Crisis','Needs Follow-Up','Financial Crisis','Health Crisis','Marriage Crisis','Spiritual Questions','Grief/Loss','Abuse Disclosure','Suicidal Ideation','Other') COLLATE utf8mb4_general_ci NOT NULL,
  `description` text COLLATE utf8mb4_general_ci NOT NULL,
  `status` enum('New','In Progress','Resolved','Referred Out','Closed') COLLATE utf8mb4_general_ci NOT NULL,
  `acknowledged_at` datetime DEFAULT NULL,
  `assigned_to` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `resolved_date` date DEFAULT NULL,
  `resolution_notes` text COLLATE utf8mb4_general_ci,
  `outcome` enum('Connected to Resource','Pastoral Care Scheduled','Counseling Referral','Benevolence Provided','Emergency Services Called','Other') COLLATE utf8mb4_general_ci DEFAULT NULL,
  `resource_connected` varchar(100) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `follow_up_scheduled` tinyint(1) DEFAULT '0',
  `crisis_protocol_followed` tinyint(1) DEFAULT NULL,
  `authorities_contacted` tinyint(1) DEFAULT NULL,
  `volunteer_debriefed` tinyint(1) DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `escalations`
--


-- --------------------------------------------------------

--
-- Table structure for table `events`
--

CREATE TABLE `events` (
  `id` bigint NOT NULL,
  `title` varchar(200) NOT NULL,
  `venue_name` varchar(200) NOT NULL,
  `address` text NOT NULL,
  `latitude` decimal(10,7) NOT NULL,
  `longitude` decimal(10,7) NOT NULL,
  `radius` int NOT NULL,
  `start_time` datetime NOT NULL,
  `end_time` datetime NOT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT '1',
  `recurrence_type` varchar(30) NOT NULL DEFAULT 'once',
  `recurrence_day` varchar(20) DEFAULT NULL,
  `repeat_until` date DEFAULT NULL,
  `reuse_same_location` tinyint(1) NOT NULL DEFAULT '1',
  `auto_activate_recurring` tinyint(1) NOT NULL DEFAULT '1',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

--
-- Dumping data for table `events`
--


-- --------------------------------------------------------

--
-- Table structure for table `follow_ups`
--

CREATE TABLE `follow_ups` (
  `follow_up_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `person_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `volunteer_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `team_lead_id` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `attempt_number` int NOT NULL,
  `attempt_date` date NOT NULL,
  `attempt_time` time DEFAULT NULL,
  `contact_method` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `contact_status` varchar(30) COLLATE utf8mb4_general_ci NOT NULL,
  `response_type` varchar(30) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `call_duration_min` int DEFAULT NULL,
  `next_action` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `next_action_date` date DEFAULT NULL,
  `escalation_tier` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `notes` text COLLATE utf8mb4_general_ci,
  `tags` varchar(200) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `week_number` int DEFAULT NULL,
  `month_number` int DEFAULT NULL,
  `quarter_number` int DEFAULT NULL,
  `year` int DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `escalation_appropriate` enum('Correct','Under-Escalation','Over-Escalation','Not-Assessed') COLLATE utf8mb4_general_ci DEFAULT 'Not-Assessed'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `follow_ups`
--


-- --------------------------------------------------------

--
-- Table structure for table `notes`
--

CREATE TABLE `notes` (
  `note_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `entity_type` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `entity_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `note_type` varchar(30) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `note_text` text COLLATE utf8mb4_general_ci NOT NULL,
  `tags` varchar(200) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `is_private` tinyint(1) DEFAULT '0',
  `visible_to_role` varchar(30) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `created_by` varchar(50) COLLATE utf8mb4_general_ci NOT NULL,
  `created_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `nurture_sequences`
--

CREATE TABLE `nurture_sequences` (
  `sequence_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `person_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `volunteer_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `team_lead_id` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `current_step` int NOT NULL DEFAULT '1',
  `status` varchar(20) COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'Active',
  `started_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `completed_at` timestamp NULL DEFAULT NULL,
  `final_notes` text COLLATE utf8mb4_general_ci,
  `created_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `nurture_sequences`
--

-- --------------------------------------------------------

--
-- Table structure for table `nurture_steps`
--

CREATE TABLE `nurture_steps` (
  `step_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `sequence_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `person_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `volunteer_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `step_number` int NOT NULL,
  `method` varchar(10) COLLATE utf8mb4_general_ci NOT NULL,
  `scheduled_date` date NOT NULL,
  `status` varchar(20) COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'Pending',
  `contact_status` varchar(30) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `response_type` varchar(30) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `notes` text COLLATE utf8mb4_general_ci,
  `completed_at` timestamp NULL DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `nurture_steps`
--


-- --------------------------------------------------------

--
-- Table structure for table `people`
--

CREATE TABLE `people` (
  `id` int NOT NULL,
  `person_id` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `first_name` varchar(50) COLLATE utf8mb4_general_ci NOT NULL,
  `last_name` varchar(50) COLLATE utf8mb4_general_ci NOT NULL,
  `email` varchar(100) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `phone` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `age_range` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `household_type` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `zip_code` varchar(10) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `visit_type` varchar(30) COLLATE utf8mb4_general_ci NOT NULL,
  `first_visit_date` date NOT NULL,
  `last_visit_date` date DEFAULT NULL,
  `visit_count` int DEFAULT '1',
  `connection_source` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `campus` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `follow_up_status` varchar(30) COLLATE utf8mb4_general_ci NOT NULL,
  `follow_up_priority` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `assigned_volunteer` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `assigned_date` date DEFAULT NULL,
  `last_contact_date` date DEFAULT NULL,
  `next_action_date` date DEFAULT NULL,
  `interested_in` text COLLATE utf8mb4_general_ci,
  `prayer_requests` text COLLATE utf8mb4_general_ci,
  `specific_needs` text COLLATE utf8mb4_general_ci,
  `created_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `created_by` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `reference_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL,
  `reference_phone` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL,
  `address` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL,
  `location_type` varchar(45) COLLATE utf8mb4_general_ci DEFAULT 'Local'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `people`
--

-- --------------------------------------------------------

--
-- Table structure for table `system_config`
--

CREATE TABLE `system_config` (
  `config_key` varchar(50) COLLATE utf8mb4_general_ci NOT NULL,
  `config_value` varchar(200) COLLATE utf8mb4_general_ci NOT NULL,
  `config_type` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `description` text COLLATE utf8mb4_general_ci,
  `updated_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_by` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `system_config`
--


-- --------------------------------------------------------

--
-- Table structure for table `team_leads`
--

CREATE TABLE `team_leads` (
  `team_lead_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `first_name` varchar(50) COLLATE utf8mb4_general_ci NOT NULL,
  `last_name` varchar(50) COLLATE utf8mb4_general_ci NOT NULL,
  `email` varchar(100) COLLATE utf8mb4_general_ci NOT NULL,
  `phone` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `role_type` varchar(30) COLLATE utf8mb4_general_ci NOT NULL,
  `campus` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `start_date` date NOT NULL,
  `term_end_date` date DEFAULT NULL,
  `max_volunteers` int NOT NULL,
  `current_volunteers` int DEFAULT '0',
  `team_vnps_avg` decimal(5,2) DEFAULT NULL,
  `team_retention_rate` decimal(5,2) DEFAULT NULL,
  `team_completion_rate` decimal(5,2) DEFAULT NULL,
  `boundary_incidents` int DEFAULT '0',
  `created_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `status` varchar(20) COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'Active',
  `telegram_chat_id` varchar(45) COLLATE utf8mb4_general_ci DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `team_leads`
--


-- --------------------------------------------------------

--
-- Table structure for table `users`
--

CREATE TABLE `users` (
  `user_id` int NOT NULL,
  `first_name` varchar(50) COLLATE utf8mb4_general_ci NOT NULL,
  `last_name` varchar(50) COLLATE utf8mb4_general_ci NOT NULL,
  `email` varchar(100) COLLATE utf8mb4_general_ci NOT NULL,
  `phone` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `role_type` varchar(30) COLLATE utf8mb4_general_ci NOT NULL,
  `campus` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `status` varchar(20) COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'Active',
  `telegram_chat_id` varchar(45) COLLATE utf8mb4_general_ci DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `users`
--


-- --------------------------------------------------------

--
-- Table structure for table `vnps_surveys`
--

CREATE TABLE `vnps_surveys` (
  `survey_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `volunteer_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `survey_date` date NOT NULL,
  `quarter` varchar(10) COLLATE utf8mb4_general_ci NOT NULL,
  `year` int NOT NULL,
  `vnps_score` int NOT NULL,
  `vnps_category` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `what_working_well` text COLLATE utf8mb4_general_ci,
  `what_could_improve` text COLLATE utf8mb4_general_ci,
  `additional_feedback` text COLLATE utf8mb4_general_ci,
  `sentiment` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
) ;

-- --------------------------------------------------------

--
-- Table structure for table `volunteers`
--

CREATE TABLE `volunteers` (
  `volunteer_id` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `first_name` varchar(50) COLLATE utf8mb4_general_ci NOT NULL,
  `last_name` varchar(50) COLLATE utf8mb4_general_ci NOT NULL,
  `email` varchar(100) COLLATE utf8mb4_general_ci NOT NULL,
  `phone` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `status` varchar(30) COLLATE utf8mb4_general_ci NOT NULL,
  `level` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `start_date` date NOT NULL,
  `end_date` date DEFAULT NULL,
  `capacity_band` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `capacity_min` int NOT NULL,
  `capacity_max` int NOT NULL,
  `current_assignments` int DEFAULT '0',
  `total_completed` int DEFAULT '0',
  `total_assigned` int DEFAULT '0',
  `completion_rate` decimal(5,2) DEFAULT NULL,
  `avg_response_time` decimal(5,2) DEFAULT NULL,
  `last_check_in` date DEFAULT NULL,
  `next_check_in` date DEFAULT NULL,
  `emotional_tone` varchar(10) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `vnps_score` int DEFAULT NULL,
  `burnout_risk` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `team_lead` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `campus` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT 'Ongole',
  `level_0_complete` date DEFAULT NULL,
  `crisis_trained` date DEFAULT NULL,
  `confidentiality_signed` date DEFAULT NULL,
  `background_check` date DEFAULT NULL,
  `boundary_violations` int DEFAULT '0',
  `last_violation_date` date DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT NULL ON UPDATE CURRENT_TIMESTAMP,
  `telegram_chat_id` bigint DEFAULT NULL,
  `last_assigned_at` datetime DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `volunteers`
--


--
-- Indexes for dumped tables
--

--
-- Indexes for table `app_users`
--
ALTER TABLE `app_users`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_users_mobile_number` (`mobile_number`);

--
-- Indexes for table `attendances`
--
ALTER TABLE `attendances`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_attendance_user_event_day` (`user_id`,`event_id`,`attendance_day`),
  ADD KEY `fk_attendance_event` (`event_id`),
  ADD KEY `idx_attendance_user_time` (`user_id`,`checkin_time`),
  ADD KEY `idx_attendance_day` (`attendance_day`);

--
-- Indexes for table `capacity_bands`
--
ALTER TABLE `capacity_bands`
  ADD PRIMARY KEY (`band_name`);

--
-- Indexes for table `capacity_history`
--
ALTER TABLE `capacity_history`
  ADD PRIMARY KEY (`history_id`),
  ADD KEY `volunteer_id` (`volunteer_id`);

--
-- Indexes for table `check_ins`
--
ALTER TABLE `check_ins`
  ADD PRIMARY KEY (`check_in_id`),
  ADD UNIQUE KEY `id` (`id`),
  ADD KEY `volunteer_id` (`volunteer_id`),
  ADD KEY `team_lead_id` (`team_lead_id`);

--
-- Indexes for table `escalations`
--
ALTER TABLE `escalations`
  ADD PRIMARY KEY (`escalation_id`),
  ADD UNIQUE KEY `id` (`id`),
  ADD KEY `follow_up_id` (`follow_up_id`),
  ADD KEY `person_id` (`person_id`),
  ADD KEY `volunteer_id` (`volunteer_id`),
  ADD KEY `team_lead_id` (`team_lead_id`);

--
-- Indexes for table `events`
--
ALTER TABLE `events`
  ADD PRIMARY KEY (`id`),
  ADD KEY `idx_events_active` (`is_active`),
  ADD KEY `idx_events_time` (`start_time`,`end_time`),
  ADD KEY `idx_events_recurring` (`recurrence_type`,`recurrence_day`,`repeat_until`);

--
-- Indexes for table `follow_ups`
--
ALTER TABLE `follow_ups`
  ADD UNIQUE KEY `follow_up_id` (`follow_up_id`) USING BTREE,
  ADD KEY `person_id` (`person_id`),
  ADD KEY `volunteer_id` (`volunteer_id`),
  ADD KEY `team_lead_id` (`team_lead_id`);

--
-- Indexes for table `notes`
--
ALTER TABLE `notes`
  ADD PRIMARY KEY (`note_id`);

--
-- Indexes for table `nurture_sequences`
--
ALTER TABLE `nurture_sequences`
  ADD PRIMARY KEY (`sequence_id`),
  ADD KEY `idx_ns_person` (`person_id`),
  ADD KEY `idx_ns_volunteer` (`volunteer_id`),
  ADD KEY `idx_ns_status` (`status`);

--
-- Indexes for table `nurture_steps`
--
ALTER TABLE `nurture_steps`
  ADD PRIMARY KEY (`step_id`),
  ADD KEY `idx_nst_sequence` (`sequence_id`),
  ADD KEY `idx_nst_person` (`person_id`),
  ADD KEY `idx_nst_volunteer` (`volunteer_id`),
  ADD KEY `idx_nst_scheduled` (`scheduled_date`),
  ADD KEY `idx_nst_status` (`status`);

--
-- Indexes for table `people`
--
ALTER TABLE `people`
  ADD PRIMARY KEY (`person_id`),
  ADD UNIQUE KEY `id` (`id`),
  ADD KEY `assigned_volunteer` (`assigned_volunteer`);

--
-- Indexes for table `system_config`
--
ALTER TABLE `system_config`
  ADD PRIMARY KEY (`config_key`);

--
-- Indexes for table `team_leads`
--
ALTER TABLE `team_leads`
  ADD PRIMARY KEY (`team_lead_id`);

--
-- Indexes for table `users`
--
ALTER TABLE `users`
  ADD PRIMARY KEY (`user_id`);

--
-- Indexes for table `vnps_surveys`
--
ALTER TABLE `vnps_surveys`
  ADD PRIMARY KEY (`survey_id`),
  ADD KEY `volunteer_id` (`volunteer_id`);

--
-- Indexes for table `volunteers`
--
ALTER TABLE `volunteers`
  ADD PRIMARY KEY (`volunteer_id`),
  ADD KEY `team_lead` (`team_lead`);

--
-- AUTO_INCREMENT for dumped tables
--

--
-- AUTO_INCREMENT for table `app_users`
--
ALTER TABLE `app_users`
  MODIFY `id` bigint NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=11;

--
-- AUTO_INCREMENT for table `attendances`
--
ALTER TABLE `attendances`
  MODIFY `id` bigint NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=22;

--
-- AUTO_INCREMENT for table `check_ins`
--
ALTER TABLE `check_ins`
  MODIFY `id` int NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `escalations`
--
ALTER TABLE `escalations`
  MODIFY `id` int NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=9;

--
-- AUTO_INCREMENT for table `events`
--
ALTER TABLE `events`
  MODIFY `id` bigint NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=19;

--
-- AUTO_INCREMENT for table `people`
--
ALTER TABLE `people`
  MODIFY `id` int NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=77;

--
-- AUTO_INCREMENT for table `users`
--
ALTER TABLE `users`
  MODIFY `user_id` int NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=3;

--
-- Constraints for dumped tables
--

--
-- Constraints for table `attendances`
--
ALTER TABLE `attendances`
  ADD CONSTRAINT `fk_attendance_event` FOREIGN KEY (`event_id`) REFERENCES `events` (`id`),
  ADD CONSTRAINT `fk_attendance_user` FOREIGN KEY (`user_id`) REFERENCES `app_users` (`id`);

--
-- Constraints for table `capacity_history`
--
ALTER TABLE `capacity_history`
  ADD CONSTRAINT `capacity_history_ibfk_1` FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers` (`volunteer_id`);

--
-- Constraints for table `check_ins`
--
ALTER TABLE `check_ins`
  ADD CONSTRAINT `check_ins_ibfk_1` FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers` (`volunteer_id`),
  ADD CONSTRAINT `check_ins_ibfk_2` FOREIGN KEY (`team_lead_id`) REFERENCES `team_leads` (`team_lead_id`);

--
-- Constraints for table `escalations`
--
ALTER TABLE `escalations`
  ADD CONSTRAINT `escalations_ibfk_1` FOREIGN KEY (`follow_up_id`) REFERENCES `follow_ups` (`follow_up_id`),
  ADD CONSTRAINT `escalations_ibfk_2` FOREIGN KEY (`person_id`) REFERENCES `people` (`person_id`),
  ADD CONSTRAINT `escalations_ibfk_3` FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers` (`volunteer_id`),
  ADD CONSTRAINT `escalations_ibfk_4` FOREIGN KEY (`team_lead_id`) REFERENCES `team_leads` (`team_lead_id`);

--
-- Constraints for table `follow_ups`
--
ALTER TABLE `follow_ups`
  ADD CONSTRAINT `follow_ups_ibfk_1` FOREIGN KEY (`person_id`) REFERENCES `people` (`person_id`),
  ADD CONSTRAINT `follow_ups_ibfk_2` FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers` (`volunteer_id`),
  ADD CONSTRAINT `follow_ups_ibfk_3` FOREIGN KEY (`team_lead_id`) REFERENCES `team_leads` (`team_lead_id`);

--
-- Constraints for table `people`
--
ALTER TABLE `people`
  ADD CONSTRAINT `people_ibfk_1` FOREIGN KEY (`assigned_volunteer`) REFERENCES `volunteers` (`volunteer_id`);

--
-- Constraints for table `vnps_surveys`
--
ALTER TABLE `vnps_surveys`
  ADD CONSTRAINT `vnps_surveys_ibfk_1` FOREIGN KEY (`volunteer_id`) REFERENCES `volunteers` (`volunteer_id`);

--
-- Constraints for table `volunteers`
--
ALTER TABLE `volunteers`
  ADD CONSTRAINT `volunteers_ibfk_1` FOREIGN KEY (`team_lead`) REFERENCES `team_leads` (`team_lead_id`);
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
