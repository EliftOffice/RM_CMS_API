-- phpMyAdmin SQL Dump
-- version 5.2.1
-- https://www.phpmyadmin.net/
--
-- Host: 127.0.0.1
-- Generation Time: Mar 31, 2026 at 12:06 PM
-- Server version: 10.4.32-MariaDB
-- PHP Version: 8.2.12

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
-- Table structure for table `capacity_bands`
--

CREATE TABLE `capacity_bands` (
  `band_name` varchar(20) NOT NULL,
  `min_per_week` int(11) NOT NULL,
  `max_per_week` int(11) NOT NULL,
  `description` text DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `capacity_history`
--

CREATE TABLE `capacity_history` (
  `history_id` varchar(20) NOT NULL,
  `volunteer_id` varchar(20) NOT NULL,
  `change_date` date NOT NULL,
  `old_capacity_band` varchar(20) DEFAULT NULL,
  `new_capacity_band` varchar(20) NOT NULL,
  `change_reason` varchar(50) DEFAULT NULL,
  `initiated_by` varchar(50) NOT NULL,
  `notes` text DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `check_ins`
--

CREATE TABLE `check_ins` (
  `check_in_id` varchar(20) NOT NULL,
  `volunteer_id` varchar(20) NOT NULL,
  `team_lead_id` varchar(20) NOT NULL,
  `check_in_date` date NOT NULL,
  `duration_min` int(11) DEFAULT NULL,
  `meeting_type` varchar(30) DEFAULT NULL,
  `emotional_tone` varchar(10) NOT NULL,
  `capacity_adjustment` tinyint(1) DEFAULT 0,
  `new_capacity_band` varchar(20) DEFAULT NULL,
  `concerns_noted` text DEFAULT NULL,
  `follow_up_needed` tinyint(1) DEFAULT 0,
  `completion_rate_discussed` tinyint(1) DEFAULT NULL,
  `boundary_issues` tinyint(1) DEFAULT 0,
  `training_needs` text DEFAULT NULL,
  `action_items` text DEFAULT NULL,
  `next_check_in_date` date DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `escalations`
--

CREATE TABLE `escalations` (
  `escalation_id` varchar(20) NOT NULL,
  `follow_up_id` varchar(20) NOT NULL,
  `person_id` varchar(20) NOT NULL,
  `volunteer_id` varchar(20) NOT NULL,
  `team_lead_id` varchar(20) DEFAULT NULL,
  `escalation_date` date NOT NULL,
  `escalation_tier` varchar(20) NOT NULL,
  `escalation_reason` varchar(50) NOT NULL,
  `description` text NOT NULL,
  `status` varchar(30) NOT NULL,
  `assigned_to` varchar(50) DEFAULT NULL,
  `resolved_date` date DEFAULT NULL,
  `resolution_notes` text DEFAULT NULL,
  `outcome` varchar(50) DEFAULT NULL,
  `resource_connected` varchar(100) DEFAULT NULL,
  `follow_up_scheduled` tinyint(1) DEFAULT 0,
  `crisis_protocol_followed` tinyint(1) DEFAULT NULL,
  `authorities_contacted` tinyint(1) DEFAULT NULL,
  `volunteer_debriefed` tinyint(1) DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `follow_ups`
--

CREATE TABLE `follow_ups` (
  `follow_up_id` varchar(20) NOT NULL,
  `person_id` varchar(20) NOT NULL,
  `volunteer_id` varchar(20) NOT NULL,
  `team_lead_id` varchar(20) DEFAULT NULL,
  `attempt_number` int(11) NOT NULL,
  `attempt_date` date NOT NULL,
  `attempt_time` time DEFAULT NULL,
  `contact_method` varchar(20) DEFAULT NULL,
  `contact_status` varchar(30) NOT NULL,
  `response_type` varchar(30) DEFAULT NULL,
  `call_duration_min` int(11) DEFAULT NULL,
  `next_action` varchar(50) DEFAULT NULL,
  `next_action_date` date DEFAULT NULL,
  `escalation_tier` varchar(20) DEFAULT NULL,
  `notes` text DEFAULT NULL,
  `tags` varchar(200) DEFAULT NULL,
  `week_number` int(11) DEFAULT NULL,
  `month_number` int(11) DEFAULT NULL,
  `quarter_number` int(11) DEFAULT NULL,
  `year` int(11) DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `notes`
--

CREATE TABLE `notes` (
  `note_id` varchar(20) NOT NULL,
  `entity_type` varchar(20) NOT NULL,
  `entity_id` varchar(20) NOT NULL,
  `note_type` varchar(30) DEFAULT NULL,
  `note_text` text NOT NULL,
  `tags` varchar(200) DEFAULT NULL,
  `is_private` tinyint(1) DEFAULT 0,
  `visible_to_role` varchar(30) DEFAULT NULL,
  `created_by` varchar(50) NOT NULL,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `people`
--

CREATE TABLE `people` (
  `person_id` varchar(20) NOT NULL,
  `first_name` varchar(50) NOT NULL,
  `last_name` varchar(50) NOT NULL,
  `email` varchar(100) DEFAULT NULL,
  `phone` varchar(20) DEFAULT NULL,
  `age_range` varchar(20) DEFAULT NULL,
  `household_type` varchar(50) DEFAULT NULL,
  `zip_code` varchar(10) DEFAULT NULL,
  `visit_type` varchar(30) NOT NULL,
  `first_visit_date` date NOT NULL,
  `last_visit_date` date DEFAULT NULL,
  `visit_count` int(11) DEFAULT 1,
  `connection_source` varchar(50) DEFAULT NULL,
  `campus` varchar(50) DEFAULT NULL,
  `follow_up_status` varchar(30) NOT NULL,
  `follow_up_priority` varchar(20) DEFAULT NULL,
  `assigned_volunteer` varchar(20) DEFAULT NULL,
  `assigned_date` date DEFAULT NULL,
  `last_contact_date` date DEFAULT NULL,
  `next_action_date` date DEFAULT NULL,
  `interested_in` text DEFAULT NULL,
  `prayer_requests` text DEFAULT NULL,
  `specific_needs` text DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp(),
  `created_by` varchar(50) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `people`
--

INSERT INTO `people` (`person_id`, `first_name`, `last_name`, `email`, `phone`, `age_range`, `household_type`, `zip_code`, `visit_type`, `first_visit_date`, `last_visit_date`, `visit_count`, `connection_source`, `campus`, `follow_up_status`, `follow_up_priority`, `assigned_volunteer`, `assigned_date`, `last_contact_date`, `next_action_date`, `interested_in`, `prayer_requests`, `specific_needs`, `created_at`, `updated_at`, `created_by`) VALUES
('P001', 'John', 'Doe', 'john@example.com', '9876543210', '25-34', 'Single', '500001', 'First Time', '2026-03-01', '2026-03-01', 1, 'Online', 'Ongole', 'Pending', 'High', NULL, NULL, NULL, NULL, 'Youth Ministry', 'Pray for job', 'Looking for community', '2026-03-30 08:16:59', '2026-03-30 08:16:59', 'Admin'),
('P002', 'Kiran', 'Kumar', 'kiran.kumar@example.com', '+91-9001111112', '18-24', 'Single', '520001', 'Returning', '2026-02-15', '2026-03-01', 3, 'Friend Invite', 'Ongole', 'In Progress', 'Medium', 'V003', '2026-03-02', '2026-03-06', '2026-03-12', 'Youth Group', 'Health', NULL, '2026-03-31 07:47:31', '2026-03-31 07:47:31', 'Admin'),
('P003', 'Lakshmi', 'Devi', 'lakshmi.devi@example.com', '+91-9001111113', '35-44', 'Family', '500002', 'First Time', '2026-03-10', NULL, 1, 'Walk-in', 'Ongole', 'Pending', 'Low', 'V002', '2026-03-11', NULL, '2026-03-15', 'Prayer Meetings', 'Family peace', 'Financial guidance', '2026-03-31 07:47:31', '2026-03-31 07:47:31', 'Admin'),
('P004', 'Suresh', 'Naidu', 'suresh.naidu@example.com', '+91-9001111111', '25-34', 'Family', '500001', 'First Time', '2026-03-01', NULL, 1, 'Website', 'Ongole', 'Pending', 'High', 'V001', '2026-03-02', '2026-03-05', '2026-03-10', 'Bible Study', 'Job stability', 'Counseling', '2026-03-31 07:47:31', '2026-03-31 07:47:31', 'Admin');

-- --------------------------------------------------------

--
-- Table structure for table `system_config`
--

CREATE TABLE `system_config` (
  `config_key` varchar(50) NOT NULL,
  `config_value` varchar(200) NOT NULL,
  `config_type` varchar(20) NOT NULL,
  `description` text DEFAULT NULL,
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp(),
  `updated_by` varchar(50) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `system_config`
--

INSERT INTO `system_config` (`config_key`, `config_value`, `config_type`, `description`, `updated_at`, `updated_by`) VALUES
('assign_immediately', 'true', 'bool', 'Assigne the new people ', '2026-03-31 08:35:41', 'Admin'),
('balanced_max', '3', 'integer', 'Maximum follow-ups per week for Balanced band', '2026-03-26 15:00:20', 'Admin'),
('balanced_min', '2', 'integer', 'Minimum follow-ups per week for Balanced band', '2026-03-26 15:00:20', 'Admin'),
('check_in_frequency_days', '30', 'integer', 'Days between monthly check-ins', '2026-03-26 15:00:20', 'Admin'),
('consistent_max', '6', 'integer', 'Maximum follow-ups per week for Consistent band', '2026-03-26 15:00:20', 'Admin'),
('consistent_min', '4', 'integer', 'Minimum follow-ups per week for Consistent band', '2026-03-26 15:00:20', 'Admin'),
('green_threshold', '90', 'integer', 'Completion rate % for green flag', '2026-03-26 15:00:20', 'Admin'),
('limited_max', '2', 'integer', 'Maximum follow-ups per week for Limited band', '2026-03-26 15:00:20', 'Admin'),
('limited_min', '1', 'integer', 'Minimum follow-ups per week for Limited band', '2026-03-26 15:00:20', 'Admin'),
('max_retry_attempts', '3', 'integer', 'Maximum contact attempts before marking unresponsive', '2026-03-26 15:00:20', 'Admin'),
('red_threshold', '74', 'integer', 'Completion rate % below which red flag', '2026-03-26 15:00:20', 'Admin'),
('response_time_target', '48', 'integer', 'Target hours for first contact attempt', '2026-03-26 15:00:20', 'Admin'),
('retry_delay_days', '3', 'integer', 'Days to wait before retry attempt', '2026-03-26 15:00:20', 'Admin'),
('team_lead_span_full_time', '12', 'integer', 'Max volunteers for full-time Team Lead', '2026-03-26 15:00:20', 'Admin'),
('team_lead_span_player_coach', '8', 'integer', 'Max volunteers for player-coach Team Lead', '2026-03-26 15:00:20', 'Admin'),
('vnps_frequency_months', '3', 'integer', 'Months between vNPS surveys', '2026-03-26 15:00:20', 'Admin'),
('yellow_threshold', '75', 'integer', 'Completion rate % for yellow flag', '2026-03-26 15:00:20', 'Admin');

-- --------------------------------------------------------

--
-- Table structure for table `team_leads`
--

CREATE TABLE `team_leads` (
  `team_lead_id` varchar(20) NOT NULL,
  `first_name` varchar(50) NOT NULL,
  `last_name` varchar(50) NOT NULL,
  `email` varchar(100) NOT NULL,
  `phone` varchar(20) DEFAULT NULL,
  `role_type` varchar(30) NOT NULL,
  `campus` varchar(50) DEFAULT NULL,
  `start_date` date NOT NULL,
  `term_end_date` date DEFAULT NULL,
  `max_volunteers` int(11) NOT NULL,
  `current_volunteers` int(11) DEFAULT 0,
  `team_vnps_avg` decimal(5,2) DEFAULT NULL,
  `team_retention_rate` decimal(5,2) DEFAULT NULL,
  `team_completion_rate` decimal(5,2) DEFAULT NULL,
  `boundary_incidents` int(11) DEFAULT 0,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `team_leads`
--

INSERT INTO `team_leads` (`team_lead_id`, `first_name`, `last_name`, `email`, `phone`, `role_type`, `campus`, `start_date`, `term_end_date`, `max_volunteers`, `current_volunteers`, `team_vnps_avg`, `team_retention_rate`, `team_completion_rate`, `boundary_incidents`, `created_at`, `updated_at`) VALUES
('TL001', 'David', 'Raj', 'david.raj@example.com', '+91-9000000001', 'Senior Lead', 'Hyderabad', '2023-01-01', NULL, 10, 3, 85.50, 90.00, 88.00, 0, '2026-03-31 07:46:10', '2026-03-31 07:46:10'),
('TL002', 'Samuel', 'Reddy', 'samuel.reddy@example.com', '+91-9000000002', 'Campus Lead', 'Vijayawada', '2023-03-01', NULL, 8, 2, 80.00, 85.00, 82.00, 1, '2026-03-31 07:46:10', '2026-03-31 07:46:10');

-- --------------------------------------------------------

--
-- Table structure for table `vnps_surveys`
--

CREATE TABLE `vnps_surveys` (
  `survey_id` varchar(20) NOT NULL,
  `volunteer_id` varchar(20) NOT NULL,
  `survey_date` date NOT NULL,
  `quarter` varchar(10) NOT NULL,
  `year` int(11) NOT NULL,
  `vnps_score` int(11) NOT NULL CHECK (`vnps_score` between 0 and 10),
  `vnps_category` varchar(20) DEFAULT NULL,
  `what_working_well` text DEFAULT NULL,
  `what_could_improve` text DEFAULT NULL,
  `additional_feedback` text DEFAULT NULL,
  `sentiment` varchar(20) DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `volunteers`
--

CREATE TABLE `volunteers` (
  `volunteer_id` varchar(20) NOT NULL,
  `first_name` varchar(50) NOT NULL,
  `last_name` varchar(50) NOT NULL,
  `email` varchar(100) NOT NULL,
  `phone` varchar(20) DEFAULT NULL,
  `status` varchar(30) NOT NULL,
  `level` varchar(20) NOT NULL,
  `start_date` date NOT NULL,
  `end_date` date DEFAULT NULL,
  `capacity_band` varchar(20) NOT NULL,
  `capacity_min` int(11) NOT NULL,
  `capacity_max` int(11) NOT NULL,
  `current_assignments` int(11) DEFAULT 0,
  `total_completed` int(11) DEFAULT 0,
  `total_assigned` int(11) DEFAULT 0,
  `completion_rate` decimal(5,2) DEFAULT NULL,
  `avg_response_time` decimal(5,2) DEFAULT NULL,
  `last_check_in` date DEFAULT NULL,
  `next_check_in` date DEFAULT NULL,
  `emotional_tone` varchar(10) DEFAULT NULL,
  `vnps_score` int(11) DEFAULT NULL,
  `burnout_risk` varchar(20) DEFAULT NULL,
  `team_lead` varchar(20) DEFAULT NULL,
  `campus` varchar(50) DEFAULT NULL,
  `level_0_complete` date DEFAULT NULL,
  `crisis_trained` date DEFAULT NULL,
  `confidentiality_signed` date DEFAULT NULL,
  `background_check` date DEFAULT NULL,
  `boundary_violations` int(11) DEFAULT 0,
  `last_violation_date` date DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `volunteers`
--

INSERT INTO `volunteers` (`volunteer_id`, `first_name`, `last_name`, `email`, `phone`, `status`, `level`, `start_date`, `end_date`, `capacity_band`, `capacity_min`, `capacity_max`, `current_assignments`, `total_completed`, `total_assigned`, `completion_rate`, `avg_response_time`, `last_check_in`, `next_check_in`, `emotional_tone`, `vnps_score`, `burnout_risk`, `team_lead`, `campus`, `level_0_complete`, `crisis_trained`, `confidentiality_signed`, `background_check`, `boundary_violations`, `last_violation_date`, `created_at`, `updated_at`) VALUES
('V001', 'Ravi', 'Kumar', 'ravi.kumar@example.com', '+91-9876543210', 'Active', 'Level 2', '2024-06-15', NULL, 'Medium', 3, 8, 2, 20, 25, 80.00, 2.50, '2026-03-01', '2026-04-01', 'Positive', 9, 'Low', 'TL001', 'Ongole', '2024-05-20', '2024-07-10', '2024-05-18', '2024-05-25', 0, NULL, '2026-03-31 07:46:25', '2026-03-31 07:46:25'),
('V002', 'Anil', 'Reddy', 'anil.reddy@example.com', '+91-9876543211', 'Active', 'Level 1', '2025-01-10', NULL, 'Low', 1, 5, 1, 10, 12, 83.00, 3.00, '2026-03-10', '2026-04-10', 'Neutral', 8, 'Medium', 'TL001', 'Ongole', '2025-01-01', NULL, '2025-01-05', '2025-01-08', 0, NULL, '2026-03-31 07:46:25', '2026-03-31 07:46:25'),
('V003', 'John', 'Paul', 'john.paul@example.com', '+91-9876543212', 'Active', 'Level 2', '2024-08-20', NULL, 'High', 5, 10, 4, 30, 35, 85.00, 2.00, '2026-03-05', '2026-04-05', 'Positive', 9, 'Low', 'TL002', 'Ongole', '2024-08-01', '2024-09-01', '2024-08-05', '2024-08-10', 0, NULL, '2026-03-31 07:46:25', '2026-03-31 07:46:25');

--
-- Indexes for dumped tables
--

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
  ADD KEY `volunteer_id` (`volunteer_id`),
  ADD KEY `team_lead_id` (`team_lead_id`);

--
-- Indexes for table `escalations`
--
ALTER TABLE `escalations`
  ADD PRIMARY KEY (`escalation_id`),
  ADD KEY `follow_up_id` (`follow_up_id`),
  ADD KEY `person_id` (`person_id`),
  ADD KEY `volunteer_id` (`volunteer_id`),
  ADD KEY `team_lead_id` (`team_lead_id`);

--
-- Indexes for table `follow_ups`
--
ALTER TABLE `follow_ups`
  ADD PRIMARY KEY (`follow_up_id`),
  ADD KEY `person_id` (`person_id`),
  ADD KEY `volunteer_id` (`volunteer_id`),
  ADD KEY `team_lead_id` (`team_lead_id`);

--
-- Indexes for table `notes`
--
ALTER TABLE `notes`
  ADD PRIMARY KEY (`note_id`);

--
-- Indexes for table `people`
--
ALTER TABLE `people`
  ADD PRIMARY KEY (`person_id`),
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
-- Constraints for dumped tables
--

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
