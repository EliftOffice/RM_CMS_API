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

INSERT INTO `app_users` (`id`, `name`, `mobile_number`, `created_at`, `updated_at`) VALUES
(2, 'Prasanth kumar Guttula', '7893843224', '2026-05-26 11:40:31', '2026-05-26 11:40:31'),
(3, 'SRINU', '6303967300', '2026-05-26 14:23:46', '2026-05-26 14:23:46'),
(4, 'Manikanta Kandepu', '9160369700', '2026-05-27 14:06:05', '2026-05-27 14:06:05'),
(5, 'Guttla Manohar', '9989671389', '2026-05-29 13:45:41', '2026-05-29 13:45:41'),
(6, 'GUTTULA SONIYA', '8121214817', '2026-05-29 14:53:52', '2026-05-29 14:53:52'),
(7, 'Raviteja', '9849317673', '2026-05-31 07:49:38', '2026-05-31 07:49:38'),
(8, 'Vicky Joel', '8500820484', '2026-05-31 07:57:10', '2026-05-31 07:57:10'),
(9, 'Vicky Joel', '8712377714', '2026-05-31 08:07:58', '2026-05-31 08:07:58'),
(10, 'Raviteja Sai', '9491957203', '2026-05-31 08:18:05', '2026-05-31 08:18:05');

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

INSERT INTO `attendances` (`id`, `user_id`, `event_id`, `event_title`, `attendance_day`, `checkin_time`, `latitude`, `longitude`, `device_info`, `created_at`) VALUES
(14, 10, 17, 'Sunday Service', '2026-05-31', '2026-05-31 13:48:08', 15.5012929, 80.0502239, 'Android 16 - SM-A366E', '2026-05-31 08:18:08'),
(15, 10, 18, 'Sunday service', '2026-05-31', '2026-05-31 13:48:08', 15.5012929, 80.0502239, 'Android 16 - SM-A366E', '2026-05-31 08:18:08'),
(16, 9, 17, 'Sunday Service', '2026-06-07', '2026-06-07 08:56:25', 15.5014471, 80.0502646, 'Android 15 - CPH2505', '2026-06-14 16:00:18'),
(17, 9, 18, 'Sunday service', '2026-06-07', '2026-06-07 08:56:25', 15.5014471, 80.0502646, 'Android 15 - CPH2505', '2026-06-14 16:00:18'),
(18, 9, 17, 'Sunday Service', '2026-06-14', '2026-06-14 08:45:16', 15.5012893, 80.0501618, 'Android 15 - CPH2505', '2026-06-14 16:00:19'),
(19, 9, 18, 'Sunday service', '2026-06-14', '2026-06-14 08:45:16', 15.5012893, 80.0501618, 'Android 15 - CPH2505', '2026-06-14 16:00:19'),
(20, 3, 17, 'Sunday Service', '2026-06-21', '2026-06-21 15:57:11', 15.5032282, 80.0501742, 'Android 16 - RMX5030', '2026-06-21 10:27:13'),
(21, 3, 18, 'Sunday service', '2026-06-21', '2026-06-21 17:02:36', 15.5024726, 80.0494756, 'Android 16 - RMX5030', '2026-06-21 11:50:45');

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

INSERT INTO `capacity_bands` (`band_name`, `min_per_week`, `max_per_week`, `description`) VALUES
('Balanced', 2, 3, 'Moderate capacity - 2-3 follow-ups per week'),
('Consistent', 4, 6, 'High capacity - 4-6 follow-ups per week'),
('Limited', 1, 2, 'Low capacity - 1-2 follow-ups per week');

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

INSERT INTO `escalations` (`id`, `escalation_id`, `follow_up_id`, `person_id`, `volunteer_id`, `team_lead_id`, `escalation_date`, `notified_at`, `escalation_tier`, `escalation_reason`, `description`, `status`, `acknowledged_at`, `assigned_to`, `resolved_date`, `resolution_notes`, `outcome`, `resource_connected`, `follow_up_scheduled`, `crisis_protocol_followed`, `authorities_contacted`, `volunteer_debriefed`, `created_at`, `updated_at`) VALUES
(1, 'ESC0001', 'F0002', 'P2026020', 'V052', 'TL002', '2026-05-21', NULL, 'Standard', 'Needs Follow-Up', 'Marriage and Job ,she is confused whether she should get married right now or get a job, cause her family facing finance trouble.', 'Closed', NULL, NULL, '2026-05-21', 'Invited to Church.. Regular ga vastundandi.. smiles anna tho matladi guidance teskondi ani chepanu.. Prayer team  meekosam prayer chestharu ani chepanu', 'Other', '', 0, NULL, NULL, NULL, '2026-05-21 09:16:59', '2026-05-21 09:16:59'),
(2, 'ESC0002', 'F0003', 'P2026022', 'V041', 'TL002', '2026-05-21', NULL, 'Standard', 'Needs Follow-Up', 'Pregnancy ( married since 2003), and spiritual ga disturbance ga vundi ani chepparu.', 'Closed', NULL, NULL, '2026-05-21', 'Connected to BSG', 'Connected to Resource', '', 0, NULL, NULL, NULL, '2026-05-21 16:45:43', '2026-05-21 16:45:43'),
(3, 'ESC0003', 'F0012', 'P2026046', 'V007', 'TL001', '2026-05-27', NULL, 'Standard', 'Needs Follow-Up', 'Prayer request:\nHealth issues(irregular periods)\nBusiness  growth,family- spiritual growth', 'Closed', NULL, NULL, '2026-05-28', '', 'Connected to Resource', '', 0, NULL, NULL, NULL, '2026-05-27 04:50:33', '2026-05-27 04:50:33'),
(4, 'ESC0004', 'F0016', 'P2026041', 'V041', 'TL002', '2026-05-28', NULL, 'Standard', 'Needs Follow-Up', '2006 lo jarigina bomb blast lo husband(Ezra) kuda vunnaru, husband ki debbalu tagilayi. Job, Property, kavali ani petukkunnaru avi ravali ani prayer cheyamannaru. Mary garu Job(Pvt. teacher) salary paina illu nadustundi financial ga stable avadaniki and kids education gurinchi prayer cheyamani adigaru.', 'Resolved', NULL, NULL, '2026-05-28', 'Connected to Sujeetha BSG.', 'Other', '', 0, NULL, NULL, NULL, '2026-05-28 03:34:06', '2026-05-28 03:34:06'),
(5, 'ESC0005', 'F0025', 'P2026055', 'V044', 'TL001', '2026-06-01', NULL, 'Standard', 'Needs Follow-Up', 'Polamma sister health and daughter born handicapped ,\nNeed prayer request ', 'Closed', NULL, NULL, '2026-06-01', 'Visit chesanu, Church ki follow up chesanu', 'Other', '', 0, NULL, NULL, NULL, '2026-06-01 10:17:27', '2026-06-01 10:17:27'),
(6, 'ESC0006', 'F0029', 'P2026050', 'V007', 'TL001', '2026-06-01', NULL, 'Standard', 'Needs Follow-Up', 'Lungs problem,\nSister valla bike miss ayyindhi one and half year back but ipati varaku dhorakaledhu\n', 'Closed', NULL, NULL, '2026-06-02', 'Sister,thana Husband parichaya chestunnaru, dani nadipimpukosam prayer cheyinchukovali anukunnaru , me daggara confuse ayyarani chepparu, Health bagane vunnadani cheparu ayya', 'Other', '', 0, NULL, NULL, NULL, '2026-06-01 14:25:09', '2026-06-01 14:25:09'),
(7, 'ESC0007', 'F0037', 'P2026064', 'V047', 'TL001', '2026-06-04', NULL, 'Standard', 'Needs Follow-Up', '1. 70 lakhs debit \n2. Job\n3. Health ', 'Closed', NULL, NULL, '2026-06-05', 'Thanu Chennai untaru, Blood thakkuvaga vundi , Knee pains gurinchi prayer cheyamani adigaru', 'Other', '', 0, NULL, NULL, NULL, '2026-06-04 15:54:55', '2026-06-04 15:54:55'),
(8, 'ESC0008', 'F0040', 'P2026071', 'V035', 'TL002', '2026-06-15', NULL, 'Standard', 'Needs Follow-Up', 'Finashiyal  issue ', 'Referred Out', NULL, NULL, '2026-06-16', 'Connected to BSG', 'Other', '', 0, NULL, NULL, NULL, '2026-06-15 06:08:46', '2026-06-15 06:08:46');

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

INSERT INTO `events` (`id`, `title`, `venue_name`, `address`, `latitude`, `longitude`, `radius`, `start_time`, `end_time`, `is_active`, `recurrence_type`, `recurrence_day`, `repeat_until`, `reuse_same_location`, `auto_activate_recurring`, `created_at`, `updated_at`) VALUES
(9, 'FAMILY MEETING', 'hcm', 'ONGOLE', 16.4929583, 80.6725373, 1750, '2026-05-26 17:07:00', '2026-05-26 18:07:00', 0, 'once', 'Sunday', '2026-05-26', 1, 1, '2026-05-26 11:37:58', '2026-05-26 11:57:21'),
(10, 'Bsg', 'Hcm', 'HCM', 15.5014210, 80.0498030, 1450, '2026-05-26 17:00:00', '2026-05-26 22:26:00', 1, 'once', 'Sunday', '2026-05-26', 1, 1, '2026-05-26 12:00:15', '2026-05-26 12:00:15'),
(11, 'Mng room', 'Deepika residents', 'Mangalagiri', 16.4248556, 80.5764623, 200, '2026-05-27 17:40:00', '2026-05-27 23:59:00', 1, 'weekly', 'Wednesday', '2026-06-04', 1, 1, '2026-05-27 12:12:12', '2026-05-27 12:12:12'),
(12, 'Testing', 'Deepika residents', 'Depika', 16.4248556, 80.5764623, 200, '2026-05-27 17:53:00', '2026-05-27 23:59:00', 1, 'weekly', 'Thursday', '2026-06-24', 1, 1, '2026-05-27 12:24:14', '2026-05-27 12:25:08'),
(13, 'Deepika 2', 'Deepika residents', 'Mangalagiri', 16.4248556, 80.5764623, 200, '2026-05-27 17:57:00', '2026-05-27 23:57:00', 1, 'weekly', 'Thursday', '2026-06-26', 1, 1, '2026-05-27 12:28:17', '2026-05-27 12:28:17'),
(14, 'Reliance mart', 'Smart Bazzar', 'testing', 17.2798000, 82.4104000, 750, '2026-05-29 19:04:00', '2026-05-29 20:04:00', 0, 'once', 'Sunday', '2026-05-29', 1, 1, '2026-05-29 13:34:26', '2026-05-29 13:46:42'),
(15, 'church', 'test', 'tesst', 17.2798000, 82.4104000, 100, '2026-05-29 19:18:00', '2026-05-29 20:20:00', 0, 'once', 'Sunday', '2026-05-29', 1, 1, '2026-05-29 13:48:28', '2026-05-29 14:38:09'),
(16, 'Night Meetings', 'Annavaram', 'test', 17.2798000, 82.4104000, 100, '2026-05-29 20:09:00', '2026-05-29 21:10:00', 1, 'once', 'Sunday', '2026-05-29', 1, 1, '2026-05-29 14:39:24', '2026-05-29 14:39:24'),
(17, 'Sunday Service', 'HCM Jr College', 'ONGOLE', 15.5014210, 80.0498030, 1500, '2026-05-31 12:13:00', '2026-05-31 22:14:00', 0, 'every_sunday', 'Sunday', '2027-10-31', 1, 1, '2026-05-31 07:44:26', '2026-05-31 08:02:41'),
(18, 'Sunday service', 'HCM Jr College', 'Ongole', 15.5012674, 80.0502580, 200, '2026-05-31 13:33:00', '2026-05-31 23:33:00', 1, 'every_sunday', 'Sunday', '2028-06-25', 1, 1, '2026-05-31 08:04:20', '2026-05-31 08:10:50');

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

INSERT INTO `follow_ups` (`follow_up_id`, `person_id`, `volunteer_id`, `team_lead_id`, `attempt_number`, `attempt_date`, `attempt_time`, `contact_method`, `contact_status`, `response_type`, `call_duration_min`, `next_action`, `next_action_date`, `escalation_tier`, `notes`, `tags`, `week_number`, `month_number`, `quarter_number`, `year`, `created_at`, `updated_at`, `escalation_appropriate`) VALUES
('F0001', 'P2026025', 'V034', 'TL001', 1, '2026-05-21', '05:13:13', 'Phone Call', 'Contacted', 'Normal', 2, 'Mark Complete', NULL, NULL, 'Husband salvation koraku prayer request adigaru.', '', 21, 5, 2, 2026, '2026-05-21 05:13:13', '2026-05-21 05:13:13', 'Not-Assessed'),
('F0002', 'P2026020', 'V052', 'TL002', 1, '2026-05-21', '09:16:59', 'Phone Call', 'Contacted', 'Needs Follow-Up', 2, 'Escalate to Team Lead', NULL, 'Standard', 'Marriage and Job ,she is confused whether she should get married right now or get a job, cause her family facing finance trouble.', '', 21, 5, 2, 2026, '2026-05-21 09:16:59', '2026-05-21 09:16:59', 'Not-Assessed'),
('F0003', 'P2026022', 'V041', 'TL002', 1, '2026-05-21', '16:45:43', 'Phone Call', 'Contacted', 'Needs Follow-Up', 2, 'Escalate to Team Lead', NULL, 'Standard', 'Pregnancy ( married since 2003), and spiritual ga disturbance ga vundi ani chepparu.', '', 21, 5, 2, 2026, '2026-05-21 16:45:43', '2026-05-21 16:45:43', 'Not-Assessed'),
('F0004', 'P2026021', 'V042', 'TL003', 1, '2026-05-22', '09:47:40', 'Phone Call', 'Contacted', 'Normal', 4, 'Mark Complete', NULL, NULL, 'Pregnancy kosam\nOwn house kosam \nHealth issues unnai ', '', 21, 5, 2, 2026, '2026-05-22 09:47:40', '2026-05-22 09:47:40', 'Not-Assessed'),
('F0005', 'P2026023', 'V056', 'TL004', 1, '2026-05-22', '09:57:21', 'Phone Call', 'Contacted', 'Normal', 3, 'Mark Complete', NULL, NULL, 'Prayer request: lost her SR(service record) works in women and child welfare office', '', 21, 5, 2, 2026, '2026-05-22 09:57:21', '2026-05-22 09:57:21', 'Not-Assessed'),
('F0006', 'P2026024', 'V032', 'TL003', 1, '2026-05-23', '07:06:30', 'Phone Call', 'Contacted', 'No Response', 0, 'Retry in 2 Days', '2026-05-25', NULL, '', '', 21, 5, 2, 2026, '2026-05-23 07:06:30', '2026-05-23 07:06:30', 'Not-Assessed'),
('F0007', 'P2026024', 'V032', 'TL003', 2, '2026-05-23', '17:12:29', 'Phone Call', 'Contacted', 'No Response', 0, 'Mark Unresponsive', NULL, NULL, '', '', 21, 5, 2, 2026, '2026-05-23 17:12:29', '2026-05-23 17:12:29', 'Not-Assessed'),
('F0008', 'P2026042', 'V040', 'TL001', 1, '2026-05-26', '06:13:14', 'Phone Call', 'Contacted', 'Normal', 8, 'Mark Complete', NULL, NULL, 'Prayer Request : for Government Job, Own House (in native place Karumanchi), Spiritual Growth of entire family, Children education, Good Health & Family welfare. ', '', 22, 5, 2, 2026, '2026-05-26 06:13:14', '2026-05-26 06:13:14', 'Not-Assessed'),
('F0009', 'P2026043', 'V031', 'TL002', 1, '2026-05-26', '06:37:46', 'Phone Call', 'Contacted', 'Normal', 5, 'Mark Complete', NULL, NULL, 'Kiran from Hyderabad attends online church every Sunday and visited our church in person, as he felt it was good to fellowship on Pentecost Sunday. He is the Founder & CEO of FTIH Film School (Film and Television Institute of Hyderabad), which also has a branch in Vijayawada. He shared that everything is going well by God\'s grace and requested us to pray for his business.', '', 22, 5, 2, 2026, '2026-05-26 06:37:46', '2026-05-26 06:37:46', 'Not-Assessed'),
('F0010', 'P2026045', 'V044', 'TL001', 1, '2026-05-26', '11:33:31', 'Phone Call', 'Contacted', 'Normal', 1, 'Mark Complete', NULL, NULL, 'PRAISE THE LORD ANNA \nBrother valla son education loo seat kosam ani  prayer cheyamannaru anna.', '', 22, 5, 2, 2026, '2026-05-26 11:33:31', '2026-05-26 11:33:31', 'Not-Assessed'),
('F0011', 'P2026040', 'V054', 'TL002', 1, '2026-05-26', '11:57:38', 'Phone Call', 'Contacted', 'Normal', 10, 'Mark Complete', NULL, NULL, '', '', 22, 5, 2, 2026, '2026-05-26 11:57:38', '2026-05-26 11:57:38', 'Not-Assessed'),
('F0012', 'P2026046', 'V007', 'TL001', 1, '2026-05-27', '04:50:32', 'Phone Call', 'Contacted', 'Needs Follow-Up', 1, 'Escalate to Team Lead', NULL, 'Standard', 'Prayer request:\nHealth issues(irregular periods)\nBusiness  growth,family- spiritual growth', '', 22, 5, 2, 2026, '2026-05-27 04:50:32', '2026-05-27 04:50:32', 'Not-Assessed'),
('F0013', 'P2026048', 'V047', 'TL001', 1, '2026-05-27', '05:52:25', 'Phone Call', 'Contacted', 'Normal', 3, 'Mark Complete', NULL, NULL, '1.ఇంజనీరింగ్ కోర్స్ లో డీల్ అనే couse తీసుకున్నాను దానిగురించి ప్రార్ధన చేయండి \n2.అమ్మ నాన్న camp అనే ఎనర్జీ డ్రింక్ డిస్టబుటర్ గా చేస్తున్నారు దాని విషయం ప్రధాన చేయండి ', '', 22, 5, 2, 2026, '2026-05-27 05:52:25', '2026-05-27 05:52:25', 'Not-Assessed'),
('F0014', 'P2026044', 'V001', 'TL003', 1, '2026-05-27', '06:46:35', 'Phone Call', 'Contacted', 'Normal', 1, 'Mark Complete', NULL, NULL, 'He has heart problem and wanted to grow spiritually.\n', '', 22, 5, 2, 2026, '2026-05-27 06:46:35', '2026-05-27 06:46:35', 'Not-Assessed'),
('F0015', 'P2026041', 'V041', 'TL002', 1, '2026-05-27', '11:31:59', 'Phone Call', 'Contacted', 'No Response', 0, 'Retry in 2 Days', '2026-05-29', NULL, 'Mary garu call answer cheyaledu.', '', 22, 5, 2, 2026, '2026-05-27 11:31:59', '2026-05-27 11:31:59', 'Not-Assessed'),
('F0016', 'P2026041', 'V041', 'TL002', 2, '2026-05-28', '03:34:06', 'Phone Call', 'Contacted', 'Needs Follow-Up', 0, 'Escalate to Team Lead', NULL, 'Standard', '2006 lo jarigina bomb blast lo husband(Ezra) kuda vunnaru, husband ki debbalu tagilayi. Job, Property, kavali ani petukkunnaru avi ravali ani prayer cheyamannaru. Mary garu Job(Pvt. teacher) salary paina illu nadustundi financial ga stable avadaniki and kids education gurinchi prayer cheyamani adigaru.', '', 22, 5, 2, 2026, '2026-05-28 03:34:06', '2026-05-28 03:34:06', 'Not-Assessed'),
('F0017', 'P2026047', 'V047', 'TL001', 1, '2026-05-28', '07:50:49', 'Phone Call', 'Contacted', 'Normal', 3, 'Mark Complete', NULL, NULL, '1.Husband ki job\n2.vere cast epudu epude devuni dagara ki vastunaru ayanaki maru manasu ', '', 22, 5, 2, 2026, '2026-05-28 07:50:49', '2026-05-28 07:50:49', 'Not-Assessed'),
('F0018', 'P2026063', 'V001', 'TL003', 1, '2026-06-01', '06:10:03', 'Phone Call', 'Contacted', 'No Response', 0, 'Retry in 2 Days', '2026-06-03', NULL, 'Not Answering ', '', 23, 6, 2, 2026, '2026-06-01 06:10:03', '2026-06-01 06:10:03', 'Not-Assessed'),
('F0019', 'P2026059', 'V030', 'TL004', 1, '2026-06-01', '07:20:11', 'Phone Call', 'Contacted', 'Normal', 10, 'Mark Complete', NULL, NULL, 'హైదరాబాద్ నుంచి యూట్యూబ్ లో ఫాలో అవుతూ వచ్చారు. 20 లక్ష అప్పు,వాళ్ళ నాన్నగారికీ హెల్త్త కోసం , తనకి వాళ్ళ బ్రదర్ కి జాబ్ ఇంకా మ్యారేజ్ కోసం ప్రేయర్ చేయమని అడిగారు ', '', 23, 6, 2, 2026, '2026-06-01 07:20:11', '2026-06-01 07:20:11', 'Not-Assessed'),
('F0020', 'P2026049', 'V053', 'TL003', 1, '2026-06-01', '07:47:58', 'Phone Call', 'Contacted', 'Normal', 5, 'Mark Complete', NULL, NULL, 'I called and spoke with Sathish Chandu Y. He said that his wife was not at home. He is currently staying in Hyderabad with his children.\nHe mentioned that both of his daughters are studying Intermediate and requested prayer for their studies.\nHe also asked for prayer regarding his stomach-related health issues.\nHe requested prayers that he may grow spiritually and remain strong in the Lord.\nHe said that he regularly follows the RM Ministry live programs.', '', 23, 6, 2, 2026, '2026-06-01 07:47:58', '2026-06-01 07:47:58', 'Not-Assessed'),
('F0021', 'P2026062', 'V034', 'TL001', 1, '2026-06-01', '07:51:04', 'Phone Call', 'Contacted', 'Normal', 3, 'Mark Complete', NULL, NULL, 'Sunday Anna ni kalisi prayer chepinchukunnamu.ani chepparu.', '', 23, 6, 2, 2026, '2026-06-01 07:51:04', '2026-06-01 07:51:04', 'Not-Assessed'),
('F0022', 'P2026068', 'V057', 'TL002', 1, '2026-06-01', '08:50:42', 'Phone Call', 'Contacted', 'Normal', 3, 'Mark Complete', NULL, NULL, 'Sesponded. No problem for child.', '', 23, 6, 2, 2026, '2026-06-01 08:50:42', '2026-06-01 08:50:42', 'Not-Assessed'),
('F0023', 'P2026066', 'V031', 'TL002', 1, '2026-06-01', '09:22:52', 'Phone Call', 'Contacted', 'Normal', 4, 'Mark Complete', NULL, NULL, 'Praise the LORD.\n\nCall Summary:\nMurali was previously running a rented generic medicine shop, which was removed due to road widening. Two months ago, they rented a new shop and are planning to start a Generic Medicine and Jan Aushadhi Medical Store. Last Sunday, they received prayer from Smiles Brother and sought guidance on whether to start a clothing business instead. Smiles Brother advised them to continue with their plan for the medical shop. Murali\'s wife is currently studying Pharmacy and is expected to receive her pharmacy license certificate by January next year. Until then, they are seeking a pharmacy certificate/license arrangement for their medical shop.\n\nPrayer Request: Pray for pharmacy certificate for their medical shop.', '', 23, 6, 2, 2026, '2026-06-01 09:22:52', '2026-06-01 09:22:52', 'Not-Assessed'),
('F0024', 'P2026054', 'V035', 'TL002', 1, '2026-06-01', '09:41:07', 'Phone Call', 'Contacted', 'Normal', 2, 'Mark Complete', NULL, NULL, 'Financial issues gurinchi  prayer ', '', 23, 6, 2, 2026, '2026-06-01 09:41:07', '2026-06-01 09:41:07', 'Not-Assessed'),
('F0025', 'P2026055', 'V044', 'TL001', 1, '2026-06-01', '10:17:27', 'Phone Call', 'Contacted', 'Needs Follow-Up', 4, 'Escalate to Team Lead', NULL, 'Standard', 'Polamma sister health and daughter born handicapped ,\nNeed prayer request ', '', 23, 6, 2, 2026, '2026-06-01 10:17:27', '2026-06-01 10:17:27', 'Not-Assessed'),
('F0026', 'P2026053', 'V054', 'TL002', 1, '2026-06-01', '10:26:38', 'Phone Call', 'Contacted', 'Normal', 15, 'Mark Complete', NULL, NULL, 'ఆల్రెడీ జాబ్ చేస్తున్నారు \n జాబ్లో ప్రెజర్ వల్ల న్యూ జాబ్ కి ట్రై చేస్తున్నారు \n ', '', 23, 6, 2, 2026, '2026-06-01 10:26:38', '2026-06-01 10:26:38', 'Not-Assessed'),
('F0027', 'P2026067', 'V005', 'TL003', 1, '2026-06-01', '10:32:20', 'Phone Call', 'Not Contacted', 'Not Contacted', 0, 'Not Contacted', NULL, NULL, '', '', 23, 6, 2, 2026, '2026-06-01 10:32:20', '2026-06-01 10:32:20', 'Not-Assessed'),
('F0028', 'P2026057', 'V028', 'TL003', 1, '2026-06-01', '10:35:04', 'Phone Call', 'Contacted', 'Normal', 0, 'Mark Complete', NULL, NULL, '', '', 23, 6, 2, 2026, '2026-06-01 10:35:04', '2026-06-01 10:35:04', 'Not-Assessed'),
('F0029', 'P2026050', 'V007', 'TL001', 1, '2026-06-01', '14:25:09', 'Phone Call', 'Contacted', 'Needs Follow-Up', 2, 'Escalate to Team Lead', NULL, 'Standard', 'Lungs problem,\nSister valla bike miss ayyindhi one and half year back but ipati varaku dhorakaledhu\n', '', 23, 6, 2, 2026, '2026-06-01 14:25:09', '2026-06-01 14:25:09', 'Not-Assessed'),
('F0030', 'P2026058', 'V029', 'TL004', 1, '2026-06-02', '02:18:00', 'Phone Call', 'Contacted', 'Normal', 2, 'Mark Complete', NULL, NULL, 'Family gurinchi prayer cheyandi', '', 23, 6, 2, 2026, '2026-06-02 02:18:00', '2026-06-02 02:18:00', 'Not-Assessed'),
('F0031', 'P2026051', 'V022', 'TL004', 1, '2026-06-02', '05:05:03', 'Phone Call', 'Contacted', 'Normal', 2, 'Mark Complete', NULL, NULL, 'SMILES anna tho matladaranta  Sunday ...\nJust prayer pettamannaru ...', '', 23, 6, 2, 2026, '2026-06-02 05:05:03', '2026-06-02 05:05:03', 'Not-Assessed'),
('F0032', 'P2026056', 'V042', 'TL003', 1, '2026-06-02', '05:24:32', 'Phone Call', 'Contacted', 'No Response', 0, 'Retry in 2 Days', '2026-06-04', NULL, '', '', 23, 6, 2, 2026, '2026-06-02 05:24:32', '2026-06-02 05:24:32', 'Not-Assessed'),
('F0033', 'P2026063', 'V001', 'TL003', 2, '2026-06-02', '08:45:14', 'Phone Call', 'Contacted', 'No Response', 0, 'Mark Unresponsive', NULL, NULL, 'Not Answering ', '', 23, 6, 2, 2026, '2026-06-02 08:45:14', '2026-06-02 08:45:14', 'Not-Assessed'),
('F0034', 'P2026052', 'V032', 'TL003', 1, '2026-06-02', '14:06:12', 'Phone Call', 'Contacted', 'Normal', 0, 'Mark Complete', NULL, NULL, '', '', 23, 6, 2, 2026, '2026-06-02 14:06:12', '2026-06-02 14:06:12', 'Not-Assessed'),
('F0035', 'P2026056', 'V042', 'TL003', 2, '2026-06-03', '03:12:23', 'Phone Call', 'Contacted', 'Normal', 6, 'Mark Complete', NULL, NULL, 'Financial problems \nHusband solvation\nHusband ki sugar undi \nPrayer cheyandi annaru ', '', 23, 6, 2, 2026, '2026-06-03 03:12:23', '2026-06-03 03:12:23', 'Not-Assessed'),
('F0036', 'P2026061', 'V003', 'TL001', 1, '2026-06-04', '01:36:21', 'Phone Call', 'Contacted', 'Normal', 5, 'Mark Complete', NULL, NULL, 'Kumari said all is good. Last week only she came to church', '', 23, 6, 2, 2026, '2026-06-04 01:36:21', '2026-06-04 01:36:21', 'Not-Assessed'),
('F0037', 'P2026064', 'V047', 'TL001', 1, '2026-06-04', '15:54:55', 'Phone Call', 'Contacted', 'Needs Follow-Up', 6, 'Escalate to Team Lead', NULL, 'Standard', '1. 70 lakhs debit \n2. Job\n3. Health ', '', 23, 6, 2, 2026, '2026-06-04 15:54:55', '2026-06-04 15:54:55', 'Not-Assessed'),
('F0038', 'P2026069', 'V044', 'TL001', 1, '2026-06-09', '13:32:39', 'Phone Call', 'Contacted', 'No Response', 0, 'Retry in 2 Days', '2026-06-11', NULL, 'Call ring but not answering ', '', 24, 6, 2, 2026, '2026-06-09 13:32:39', '2026-06-09 13:32:39', 'Not-Assessed'),
('F0039', 'P2026069', 'V044', 'TL001', 2, '2026-06-13', '08:54:51', 'Phone Call', 'Contacted', 'No Response', 0, 'Mark Unresponsive', NULL, NULL, 'Call connected but Call not responding ', '', 24, 6, 2, 2026, '2026-06-13 08:54:51', '2026-06-13 08:54:51', 'Not-Assessed'),
('F0040', 'P2026071', 'V035', 'TL002', 1, '2026-06-15', '06:08:46', 'Phone Call', 'Contacted', 'Needs Follow-Up', 1, 'Escalate to Team Lead', NULL, 'Standard', 'Finashiyal  issue ', '', 25, 6, 2, 2026, '2026-06-15 06:08:46', '2026-06-15 06:08:46', 'Not-Assessed'),
('F0041', 'P2026070', 'V030', 'TL004', 1, '2026-06-15', '06:45:20', 'Phone Call', 'Contacted', 'Normal', 4, 'Nurture Sequence Started', '2026-06-15', NULL, 'Depts 2lakhs', '', 25, 6, 2, 2026, '2026-06-15 06:45:20', '2026-06-15 06:45:20', 'Not-Assessed'),
('F0042', 'P2026072', 'V029', 'TL004', 1, '2026-06-16', '07:51:20', 'Phone Call', 'Contacted', 'Normal', 2, 'Nurture Sequence Started', '2026-06-16', NULL, 'ఫ్యామిలీ గురించి ప్రార్థన చెయ్యండి pellala చదువలగురించి', '', 25, 6, 2, 2026, '2026-06-16 07:51:20', '2026-06-16 07:51:20', 'Not-Assessed'),
('F0043', 'P2026065', 'V056', 'TL004', 1, '2026-06-17', '03:58:20', 'Phone Call', 'Contacted', 'Normal', 5, 'Nurture Sequence Started', '2026-06-17', NULL, 'Prayer request: Knee pains, Typhoid \nTyphoid valla last week church ki ralekapoyaru\nRuth Jayamani BSG ki attach ayyaru', '', 25, 6, 2, 2026, '2026-06-17 03:58:20', '2026-06-17 03:58:20', 'Not-Assessed'),
('F0044', 'P2026076', 'V007', 'TL001', 1, '2026-06-24', '03:14:18', 'Phone Call', 'Contacted', 'Normal', 1, 'Nurture Sequence Started', '2026-07-01', NULL, 'Prayer request:\nMarriage, business', '', 26, 6, 2, 2026, '2026-06-24 03:14:18', '2026-06-24 03:14:18', 'Not-Assessed');

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

INSERT INTO `nurture_sequences` (`sequence_id`, `person_id`, `volunteer_id`, `team_lead_id`, `current_step`, `status`, `started_at`, `completed_at`, `final_notes`, `created_at`, `updated_at`) VALUES
('NS0001', 'P2026070', 'V030', 'TL004', 2, 'Active', '2026-06-15 06:45:20', NULL, NULL, '2026-06-15 06:45:20', '2026-06-15 06:45:20'),
('NS0002', 'P2026072', 'V029', 'TL004', 2, 'Active', '2026-06-16 07:51:20', NULL, NULL, '2026-06-16 07:51:20', '2026-06-16 07:51:21'),
('NS0003', 'P2026065', 'V056', 'TL004', 2, 'Active', '2026-06-17 03:58:20', NULL, NULL, '2026-06-17 03:58:20', '2026-06-17 03:58:20'),
('NS0004', 'P2026076', 'V007', 'TL001', 1, 'Active', '2026-06-24 03:14:18', NULL, NULL, '2026-06-24 03:14:18', '2026-06-24 03:14:18');

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

INSERT INTO `nurture_steps` (`step_id`, `sequence_id`, `person_id`, `volunteer_id`, `step_number`, `method`, `scheduled_date`, `status`, `contact_status`, `response_type`, `notes`, `completed_at`, `created_at`, `updated_at`) VALUES
('NST0001', 'NS0001', 'P2026070', 'V030', 1, 'Call', '2026-06-15', 'Done', NULL, 'Normal', 'Depts 2lakhs', NULL, '2026-06-15 06:45:20', '2026-06-23 04:31:46'),
('NST0002', 'NS0001', 'P2026070', 'V030', 2, 'Visit', '2026-06-22', 'Pending', NULL, NULL, NULL, NULL, '2026-06-15 06:45:20', '2026-06-15 06:45:20'),
('NST0003', 'NS0001', 'P2026070', 'V030', 3, 'Call', '2026-06-29', 'Pending', NULL, NULL, NULL, NULL, '2026-06-15 06:45:20', '2026-06-15 06:45:20'),
('NST0004', 'NS0001', 'P2026070', 'V030', 4, 'Visit', '2026-07-06', 'Pending', NULL, NULL, NULL, NULL, '2026-06-15 06:45:20', '2026-06-15 06:45:20'),
('NST0005', 'NS0001', 'P2026070', 'V030', 5, 'Call', '2026-07-13', 'Pending', NULL, NULL, NULL, NULL, '2026-06-15 06:45:20', '2026-06-15 06:45:20'),
('NST0006', 'NS0001', 'P2026070', 'V030', 6, 'Visit', '2026-07-20', 'Pending', NULL, NULL, NULL, NULL, '2026-06-15 06:45:20', '2026-06-15 06:45:20'),
('NST0007', 'NS0001', 'P2026070', 'V030', 7, 'Call', '2026-07-27', 'Pending', NULL, NULL, NULL, NULL, '2026-06-15 06:45:20', '2026-06-15 06:45:20'),
('NST0008', 'NS0002', 'P2026072', 'V029', 1, 'Call', '2026-06-16', 'Done', NULL, 'Normal', 'ఫ్యామిలీ గురించి ప్రార్థన చెయ్యండి pellala చదువలగురించి', NULL, '2026-06-16 07:51:20', '2026-06-23 04:31:46'),
('NST0009', 'NS0002', 'P2026072', 'V029', 2, 'Visit', '2026-06-23', 'Pending', NULL, NULL, NULL, NULL, '2026-06-16 07:51:20', '2026-06-16 07:51:20'),
('NST0010', 'NS0002', 'P2026072', 'V029', 3, 'Call', '2026-06-30', 'Pending', NULL, NULL, NULL, NULL, '2026-06-16 07:51:20', '2026-06-16 07:51:20'),
('NST0011', 'NS0002', 'P2026072', 'V029', 4, 'Visit', '2026-07-07', 'Pending', NULL, NULL, NULL, NULL, '2026-06-16 07:51:20', '2026-06-16 07:51:20'),
('NST0012', 'NS0002', 'P2026072', 'V029', 5, 'Call', '2026-07-14', 'Pending', NULL, NULL, NULL, NULL, '2026-06-16 07:51:20', '2026-06-16 07:51:20'),
('NST0013', 'NS0002', 'P2026072', 'V029', 6, 'Visit', '2026-07-21', 'Pending', NULL, NULL, NULL, NULL, '2026-06-16 07:51:20', '2026-06-16 07:51:20'),
('NST0014', 'NS0002', 'P2026072', 'V029', 7, 'Call', '2026-07-28', 'Pending', NULL, NULL, NULL, NULL, '2026-06-16 07:51:20', '2026-06-16 07:51:20'),
('NST0015', 'NS0003', 'P2026065', 'V056', 1, 'Call', '2026-06-17', 'Done', NULL, 'Normal', 'Prayer request: Knee pains, Typhoid \nTyphoid valla last week church ki ralekapoyaru\nRuth Jayamani BSG ki attach ayyaru', NULL, '2026-06-17 03:58:20', '2026-06-23 04:31:46'),
('NST0016', 'NS0003', 'P2026065', 'V056', 2, 'Visit', '2026-06-24', 'Pending', NULL, NULL, NULL, NULL, '2026-06-17 03:58:20', '2026-06-17 03:58:20'),
('NST0017', 'NS0003', 'P2026065', 'V056', 3, 'Call', '2026-07-01', 'Pending', NULL, NULL, NULL, NULL, '2026-06-17 03:58:20', '2026-06-17 03:58:20'),
('NST0018', 'NS0003', 'P2026065', 'V056', 4, 'Visit', '2026-07-08', 'Pending', NULL, NULL, NULL, NULL, '2026-06-17 03:58:20', '2026-06-17 03:58:20'),
('NST0019', 'NS0003', 'P2026065', 'V056', 5, 'Call', '2026-07-15', 'Pending', NULL, NULL, NULL, NULL, '2026-06-17 03:58:20', '2026-06-17 03:58:20'),
('NST0020', 'NS0003', 'P2026065', 'V056', 6, 'Visit', '2026-07-22', 'Pending', NULL, NULL, NULL, NULL, '2026-06-17 03:58:20', '2026-06-17 03:58:20'),
('NST0021', 'NS0003', 'P2026065', 'V056', 7, 'Call', '2026-07-29', 'Pending', NULL, NULL, NULL, NULL, '2026-06-17 03:58:20', '2026-06-17 03:58:20'),
('NST0022', 'NS0004', 'P2026076', 'V007', 1, 'Call', '2026-07-01', 'Pending', NULL, NULL, NULL, NULL, '2026-06-24 03:14:18', '2026-06-24 03:14:18'),
('NST0023', 'NS0004', 'P2026076', 'V007', 2, 'Visit', '2026-07-08', 'Pending', NULL, NULL, NULL, NULL, '2026-06-24 03:14:18', '2026-06-24 03:14:18'),
('NST0024', 'NS0004', 'P2026076', 'V007', 3, 'Call', '2026-07-15', 'Pending', NULL, NULL, NULL, NULL, '2026-06-24 03:14:18', '2026-06-24 03:14:18'),
('NST0025', 'NS0004', 'P2026076', 'V007', 4, 'Visit', '2026-07-22', 'Pending', NULL, NULL, NULL, NULL, '2026-06-24 03:14:18', '2026-06-24 03:14:18'),
('NST0026', 'NS0004', 'P2026076', 'V007', 5, 'Call', '2026-07-29', 'Pending', NULL, NULL, NULL, NULL, '2026-06-24 03:14:18', '2026-06-24 03:14:18'),
('NST0027', 'NS0004', 'P2026076', 'V007', 6, 'Visit', '2026-08-05', 'Pending', NULL, NULL, NULL, NULL, '2026-06-24 03:14:18', '2026-06-24 03:14:18'),
('NST0028', 'NS0004', 'P2026076', 'V007', 7, 'Call', '2026-08-12', 'Pending', NULL, NULL, NULL, NULL, '2026-06-24 03:14:18', '2026-06-24 03:14:18');

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

INSERT INTO `people` (`id`, `person_id`, `first_name`, `last_name`, `email`, `phone`, `age_range`, `household_type`, `zip_code`, `visit_type`, `first_visit_date`, `last_visit_date`, `visit_count`, `connection_source`, `campus`, `follow_up_status`, `follow_up_priority`, `assigned_volunteer`, `assigned_date`, `last_contact_date`, `next_action_date`, `interested_in`, `prayer_requests`, `specific_needs`, `created_at`, `updated_at`, `created_by`, `reference_name`, `reference_phone`, `address`, `location_type`) VALUES
(20, 'P2026020', 'Sriveni', 'Yadigiri', '', '8106704958', '26-35', '', NULL, 'First-Time Visitor', '2026-05-18', '2026-05-18', 2, 'Social_Media', 'Ongole', 'COMPLETE', 'High', 'V052', '2026-05-21', '2026-05-21', '2026-05-23', NULL, '', NULL, '2026-05-18 11:33:29', '2026-05-21 03:59:41', NULL, '', '', 'Alakurapadu', 'Local'),
(21, 'P2026021', 'Aruna', 'Ganduri', '', '9705078345', '18-25', '', NULL, 'First-Time Visitor', '2026-05-18', '2026-05-18', 2, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V042', '2026-05-21', '2026-05-22', NULL, NULL, '', NULL, '2026-05-18 11:36:19', '2026-05-21 04:05:58', NULL, 'Lakshmi Koppolu', '9391498510', 'Bhagya Nagar', 'Local'),
(22, 'P2026022', 'Ramaiah', 'D', '', '9849808409', '36-45', '', NULL, 'First-Time Visitor', '2026-05-18', '2026-05-18', 2, 'Unknown', 'Ongole', 'COMPLETE', 'High', 'V041', '2026-05-21', '2026-05-21', '2026-05-23', NULL, '', NULL, '2026-05-18 11:37:18', '2026-05-21 04:01:18', NULL, '', '', 'Railway Colony ', 'Local'),
(23, 'P2026023', 'Pramila ', 'Devara ', '', '8897667155', '56+', 'Single', NULL, 'First-Time Visitor', '2026-05-18', '2026-05-18', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V056', '2026-05-18', '2026-05-22', NULL, NULL, '', NULL, '2026-05-18 11:37:19', '2026-05-18 11:37:19', NULL, 'Koppolu.Lakshmi ', '9391498510', 'Annavarappadu 4th line ', 'Local'),
(37, 'P2026024', 'Anusha', 'Kaki', '', '9492789260', '18-25', 'Single', NULL, 'First-Time Visitor', '2026-05-18', '2026-05-18', 2, 'Friend_Family_Invite', 'Ongole', 'UNRESPONSIVE', 'Normal', 'V032', '2026-05-21', '2026-05-23', NULL, NULL, '', NULL, '2026-05-18 11:37:19', '2026-05-21 04:04:59', NULL, 'Koppolu. Lakshmi ', '9391498510', 'Bhagya Nagar', 'Local'),
(38, 'P2026025', 'Raja kumari', 'Nune', '', '9618191803', '', '', NULL, 'First-Time Visitor', '2026-05-18', '2026-05-18', 2, 'Other', 'Ongole', 'COMPLETE', 'Normal', 'V034', '2026-05-21', '2026-05-21', NULL, NULL, '', NULL, '2026-05-18 11:39:05', '2026-05-21 04:00:24', NULL, '', '', 'Samatha Nagar extension ', 'Local'),
(40, 'P2026040', 'Mounika', 'Pandalapati', '', '7989905778', '18-25', '', NULL, 'First-Time Visitor', '2026-05-24', '2026-05-24', 1, 'Other', 'Ongole', 'COMPLETE', 'Normal', 'V054', '2026-05-25', '2026-05-26', NULL, NULL, '', NULL, '2026-05-24 11:13:16', '2026-05-24 11:13:16', NULL, '', '', 'Santhapeta ', 'Local'),
(41, 'P2026041', 'Mary', 'Chatla', '', '7799624866', '26-35', 'Married', NULL, 'First-Time Visitor', '2026-05-24', '2026-05-24', 1, 'Other', 'Ongole', 'COMPLETE', 'High', 'V041', '2026-05-27', '2026-05-28', '2026-05-29', NULL, '', NULL, '2026-05-24 11:15:35', '2026-05-24 11:15:35', NULL, '', '', 'Ram nagar eleventh line', 'Local'),
(42, 'P2026042', 'Ezra', 'Chatla', '', '9966853855', '36-45', 'Married', NULL, 'First-Time Visitor', '2026-05-24', '2026-05-24', 1, 'Other', 'Ongole', 'COMPLETE', 'Normal', 'V040', '2026-05-25', '2026-05-26', NULL, NULL, '', NULL, '2026-05-24 11:16:20', '2026-05-24 11:16:20', NULL, '', '', 'Ram nagar eleventh line ', 'Local'),
(43, 'P2026043', 'Kiran', 'Kiran ', '', '9966099589', '36-45', 'Married', NULL, 'First-Time Visitor', '2026-05-24', '2026-05-24', 1, 'Social_Media', 'Ongole', 'COMPLETE', 'Normal', 'V031', '2026-05-25', '2026-05-26', NULL, NULL, '', NULL, '2026-05-24 11:17:42', '2026-05-24 11:17:42', NULL, '', '', 'Hyderabad ', 'Local'),
(44, 'P2026044', 'Vivekanand ', 'S', '', '8106188303', '46-55', 'Married', NULL, 'First-Time Visitor', '2026-05-24', '2026-05-24', 1, 'Social_Media', 'Ongole', 'COMPLETE', 'Normal', 'V001', '2026-05-27', '2026-05-27', NULL, NULL, 'Heart issue ( since 4 years)', NULL, '2026-05-24 11:19:29', '2026-05-24 11:19:29', NULL, '', '', 'Khammam', 'Local'),
(45, 'P2026045', 'Nageswararao ', 'P.', '', '9206340193', '46-55', 'Family_with_Kids', NULL, 'First-Time Visitor', '2026-05-24', '2026-05-24', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V044', '2026-05-25', '2026-05-26', NULL, NULL, '', NULL, '2026-05-24 11:21:16', '2026-05-24 11:21:16', NULL, 'Lakshmi Prasanna ', '6309565470', 'Bangalore ', 'Local'),
(46, 'P2026046', 'Sailu', 'Garnapudi ', '', '7207595905', '18-25', 'Single', NULL, 'First-Time Visitor', '2026-05-24', '2026-05-24', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'High', 'V007', '2026-05-25', '2026-05-27', '2026-05-27', NULL, '', NULL, '2026-05-24 11:22:28', '2026-05-24 11:22:28', NULL, 'Mounika ', '7989905778', 'Mangamuru road, Ongole ', 'Local'),
(47, 'P2026047', 'Joshna', 'Chunduru ', '', '9063540289', '26-35', 'Single', NULL, 'First-Time Visitor', '2026-05-24', '2026-05-24', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V047', '2026-05-27', '2026-05-28', NULL, NULL, '', NULL, '2026-05-24 11:24:11', '2026-05-24 11:24:11', NULL, 'Raja rao ', '8978141307', 'Elchur, Sangthamagullru ', 'Local'),
(48, 'P2026048', 'Prabhu kumar ', 'P.', '', '9620190660', '18-25', 'Single', NULL, 'First-Time Visitor', '2026-05-24', '2026-05-24', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V047', '2026-05-25', '2026-05-27', NULL, NULL, '', NULL, '2026-05-24 12:51:54', '2026-05-24 12:51:54', NULL, 'Lakshmi Prasanna ', '6309565470', 'Bangalore ', 'Local'),
(49, 'P2026049', 'Sathish chandu', 'Y.', '', '8106827744', '46-55', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V053', '2026-06-01', '2026-06-01', NULL, NULL, '', NULL, '2026-05-31 15:01:17', '2026-05-31 15:01:17', NULL, 'Rakesh', '9052058274', 'Hyderabad', 'Local'),
(50, 'P2026050', 'PrabhaJyothi', 'Vippala', '', '8309093817', '26-35', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Social_Media', 'Ongole', 'COMPLETE', 'High', 'V007', '2026-06-01', '2026-06-01', '2026-06-03', NULL, '', NULL, '2026-05-31 15:16:11', '2026-05-31 15:16:11', NULL, '', '', 'Satenapalli, Palnadu', 'Local'),
(51, 'P2026051', 'Ramji Chaitanya', 'Thalluri', '', '9676669606', '26-35', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V022', '2026-06-01', '2026-06-02', NULL, NULL, '', NULL, '2026-05-31 15:20:43', '2026-05-31 15:20:43', NULL, 'T.Srilatha', '9100645728', 'Santhapeta', 'Local'),
(52, 'P2026052', 'Naveen', 'Chinige', '', '7386974101', '', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V032', '2026-06-01', '2026-06-02', NULL, NULL, '', NULL, '2026-05-31 15:25:21', '2026-05-31 15:25:21', NULL, 'Anand kandi', '9032903303', 'Yedugundlapadu', 'Local'),
(53, 'P2026053', 'Uday Kumar', 'Pasumarthi', '', '9010950877', '', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V054', '2026-06-01', '2026-06-01', NULL, NULL, '', NULL, '2026-05-31 15:39:17', '2026-05-31 15:39:17', NULL, 'T.Srilatha', '9100645728', 'Mallavarapadu', 'Local'),
(54, 'P2026054', 'Sunny Paul', 'B.', '', '7730995841', '26-35', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Social_Media', 'Ongole', 'COMPLETE', 'Normal', 'V035', '2026-06-01', '2026-06-01', NULL, NULL, '', NULL, '2026-05-31 15:42:57', '2026-05-31 15:42:57', NULL, '', '', 'Palvancha, Bhadadri Kothagudem', 'Local'),
(55, 'P2026055', 'Polamma', 'Imola', '', '9701544929', '46-55', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'High', 'V044', '2026-06-01', '2026-06-01', '2026-06-03', NULL, 'Allergy (for 6 months)', NULL, '2026-05-31 15:48:51', '2026-05-31 15:48:51', NULL, 'Anjaneyulu', '9000109851', '60 Feet road, R.T.C.Bus stand', 'Local'),
(56, 'P2026056', 'Jyothi', 'Pavuluri', '', '8978403699', '46-55', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V042', '2026-06-01', '2026-06-03', NULL, NULL, 'Debts(10lakhs)', NULL, '2026-05-31 15:52:38', '2026-05-31 15:52:38', NULL, 'Kasukurthi Kalyan', '9398592635', 'Housing board', 'Local'),
(57, 'P2026057', 'Saramma', 'Ganugupati', '', '9003099206', '36-45', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V028', '2026-06-01', '2026-06-01', NULL, NULL, '', NULL, '2026-05-31 15:56:26', '2026-05-31 15:56:26', NULL, 'Ashwini', '7660057403', 'Mamidipalem', 'Local'),
(58, 'P2026058', 'Subramanyam', 'Siddareddy', '', '9666588160', '56+', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Social_Media', 'Ongole', 'COMPLETE', 'Normal', 'V029', '2026-06-01', '2026-06-02', NULL, NULL, 'Debts(20Lakhs)', NULL, '2026-05-31 16:00:16', '2026-05-31 16:00:16', NULL, '', '', 'Gudur, Nellore', 'Local'),
(59, 'P2026059', 'Divya', 'Siddapureddy', '', '9700737366', '26-35', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Social_Media', 'Ongole', 'COMPLETE', 'Normal', 'V030', '2026-06-01', '2026-06-01', NULL, NULL, 'Debts(20Lakhs)', NULL, '2026-05-31 16:01:26', '2026-05-31 16:01:26', NULL, '', '', 'Gudur, Nellore', 'Local'),
(60, 'P2026060', 'Usharani', 'Pasumarthi', '', '9121119114', '46-55', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'ASSIGNED', 'Normal', 'V033', '2026-06-01', NULL, '2026-06-03', NULL, 'Debts(10Lakhs), Thyroid(30 Years)', NULL, '2026-05-31 16:09:57', '2026-05-31 16:09:57', NULL, 'Srilatha', '9100645728', 'Mallavarapadu', 'Local'),
(61, 'P2026061', 'Kumari', 'Inola', '', '7036791996', '46-55', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V003', '2026-06-01', '2026-06-04', NULL, NULL, '', NULL, '2026-05-31 16:16:28', '2026-05-31 16:16:28', NULL, 'Anjaneyulu', '9000109851', '60 Ft road, RTC colony', 'Local'),
(62, 'P2026062', 'Prasanna Kumari', 'Valeru', '', '9492318815', '56+', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V034', '2026-06-01', '2026-06-01', NULL, NULL, 'Thyroid(30 Yrs), Son and Daughter-in-law relation gurinchi prayer cheyamani adigaru.', NULL, '2026-05-31 16:22:49', '2026-05-31 16:22:49', NULL, 'Sri', '9100645728', 'Chirala', 'Local'),
(63, 'P2026063', 'Marthamma', 'Ponugupati', '', '9848213412', '46-55', 'Married', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'UNRESPONSIVE', 'Normal', 'V001', '2026-06-01', '2026-06-02', NULL, NULL, '', NULL, '2026-05-31 16:26:39', '2026-05-31 16:26:39', NULL, 'Lakshmi Prasanna', '6309565470', 'Railpet second line', 'Local'),
(64, 'P2026064', 'Rupa', 'Maddala', '', '8056276001', '36-45', 'Family_with_Kids', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'High', 'V047', '2026-06-01', '2026-06-04', '2026-06-03', NULL, 'Low Hemoglobin(5 Years), Debts(70Lakhs)', NULL, '2026-05-31 16:33:37', '2026-05-31 16:33:37', NULL, 'M.Kranthi', '9003230344', 'Chennai', 'Local'),
(65, 'P2026065', 'Geetha Priya', 'Ankam', '', '9441442227', '56+', 'Married', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'IN_NURTURE', 'Normal', 'V056', '2026-06-01', '2026-06-17', '2026-06-17', NULL, 'Arthiritis(5Months), Debts(80 Lakhs)', NULL, '2026-05-31 16:36:45', '2026-05-31 16:36:45', NULL, 'Kollam Kalpana', '7093933220', 'Kabadipalem', 'Local'),
(66, 'P2026066', 'Murali', 'Varikuti', '', '9912417577', '36-45', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V031', '2026-06-01', '2026-06-01', NULL, NULL, '', NULL, '2026-05-31 16:40:53', '2026-05-31 16:40:53', NULL, 'Swarna latha', '8897031513', 'Rajeev colony (Near koppolu)', 'Local'),
(67, 'P2026067', 'Kamala', 'Varikuti', '', '8500789879', '36-45', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'Not Contacted', 'Normal', 'V005', '2026-06-01', '2026-06-01', NULL, NULL, '', NULL, '2026-05-31 16:43:43', '2026-05-31 16:43:43', NULL, 'Swarna latha', '8897031513', 'Rajeev colony', 'Local'),
(68, 'P2026068', 'Renuka', 'Ch.', '', '9391260266', '26-35', '', NULL, 'First-Time Visitor', '2026-05-31', '2026-05-31', 1, 'Friend_Family_Invite', 'Ongole', 'COMPLETE', 'Normal', 'V057', '2026-06-01', '2026-06-01', NULL, NULL, 'Son Health: Stomach lo Billalu', NULL, '2026-05-31 16:46:19', '2026-05-31 16:46:19', NULL, 'Srilekha', '6300158717', 'Nellore', 'Local'),
(69, 'P2026069', 'Bhavani', 'Phrudvi', '', '8142815205', '36-45', '', NULL, 'First-Time Visitor', '2026-06-07', '2026-06-07', 1, 'Friend_Family_Invite', 'Ongole', 'IN_NURTURE', 'Normal', 'V044', '2026-06-08', '2026-06-13', NULL, NULL, '', NULL, '2026-06-07 09:21:15', '2026-06-07 09:21:15', NULL, 'Roja', '9182797275', 'Zakraiah Nagar ', 'Local'),
(70, 'P2026070', 'Haseena ', 'Sk.', '', '7093683382', '26-35', '', NULL, 'First-Time Visitor', '2026-06-14', '2026-06-14', 1, 'Friend_Family_Invite', 'Ongole', 'IN_NURTURE', 'Normal', 'V030', '2026-06-15', '2026-06-15', '2026-06-15', NULL, '', NULL, '2026-06-14 05:34:33', '2026-06-14 05:34:33', NULL, 'Mangamma', '9666775088', 'Balaram Colony ', 'Local'),
(71, 'P2026071', 'Peruri', 'Sreenu', '', '9550993411', '46-55', '', NULL, 'First-Time Visitor', '2026-06-14', '2026-06-14', 1, 'Other', 'Ongole', 'COMPLETE', 'High', 'V035', '2026-06-15', '2026-06-15', '2026-06-17', NULL, '', NULL, '2026-06-14 15:02:33', '2026-06-14 15:02:33', NULL, '', '', 'Old Market', 'Local'),
(72, 'P2026072', 'Anjali', 'Nimmagadda', '', '6300819495', '36-45', '', NULL, 'First-Time Visitor', '2026-06-14', '2026-06-14', 1, 'Friend_Family_Invite', 'Ongole', 'IN_NURTURE', 'Normal', 'V029', '2026-06-15', '2026-06-16', '2026-06-16', NULL, 'Debts.', NULL, '2026-06-14 15:04:23', '2026-06-14 15:04:23', NULL, 'Padma', '9573762819', 'Koppolu', 'Local'),
(73, 'P2026073', 'Johni', 'Sk. ', '', '9030425261', '56+', 'Family_with_Kids', NULL, 'First-Time Visitor', '2026-06-21', '2026-06-21', 1, 'Friend_Family_Invite', 'Ongole', 'ASSIGNED', 'Normal', 'V029', '2026-06-22', NULL, '2026-06-24', NULL, '', NULL, '2026-06-21 14:59:50', '2026-06-21 14:59:50', NULL, 'Lakshmi Prasanna', '6309565470', 'Kammapalem', 'Local'),
(74, 'P2026074', 'Gidyonu', 'Parri', '', '9581650487', '18-25', 'Single', NULL, 'First-Time Visitor', '2026-06-21', '2026-06-21', 1, 'Friend_Family_Invite', 'Ongole', 'ASSIGNED', 'Normal', 'V003', '2026-06-22', NULL, '2026-06-24', NULL, '', NULL, '2026-06-21 15:05:12', '2026-06-21 15:05:12', NULL, 'R. Madhu Babu', '9182380067', 'Ongole Hostel', 'Local'),
(75, 'P2026075', 'Daniel', 'Thambur', '', '7386314139', '18-25', '', NULL, 'First-Time Visitor', '2026-06-21', '2026-06-21', 1, 'Friend_Family_Invite', 'Ongole', 'ASSIGNED', 'Normal', 'V035', '2026-06-22', NULL, '2026-06-24', NULL, '', NULL, '2026-06-21 15:08:39', '2026-06-21 15:08:39', NULL, 'Abhishek Madda', '8142149365', 'Jaya Prakash Colony', 'Local'),
(76, 'P2026076', 'Luke', 'Musidipalli', '', '9494768987', '26-35', 'Single', NULL, 'First-Time Visitor', '2026-06-21', '2026-06-21', 1, 'Friend_Family_Invite', 'Ongole', 'IN_NURTURE', 'Normal', 'V007', '2026-06-22', '2026-06-24', '2026-07-01', NULL, '', NULL, '2026-06-21 15:14:00', '2026-06-21 15:14:00', NULL, 'Anand kandi', '9032903303', 'Lawyerpet', 'Local');

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

INSERT INTO `system_config` (`config_key`, `config_value`, `config_type`, `description`, `updated_at`, `updated_by`) VALUES
('assign_immediately', 'true', 'bool', 'Assigne the new people ', '2026-05-22 02:23:21', 'Admin'),
('balanced_max', '3', 'integer', 'Maximum follow-ups per week for Balanced band', '2026-05-22 02:23:21', 'Admin'),
('balanced_min', '2', 'integer', 'Minimum follow-ups per week for Balanced band', '2026-05-22 02:23:21', 'Admin'),
('check_in_frequency_days', '30', 'integer', 'Days between monthly check-ins', '2026-05-22 02:23:21', 'Admin'),
('consistent_max', '6', 'integer', 'Maximum follow-ups per week for Consistent band', '2026-05-22 02:23:21', 'Admin'),
('consistent_min', '4', 'integer', 'Minimum follow-ups per week for Consistent band', '2026-05-22 02:23:21', 'Admin'),
('green_threshold', '95', 'integer', 'Completion rate % for green flag', '2026-05-22 02:23:21', 'Admin'),
('limited_max', '2', 'integer', 'Maximum follow-ups per week for Limited band', '2026-05-22 02:23:21', 'Admin'),
('limited_min', '1', 'integer', 'Minimum follow-ups per week for Limited band', '2026-05-22 02:23:21', 'Admin'),
('max_retry_attempts', '2', 'integer', 'Maximum contact attempts before marking unresponsive', '2026-05-22 02:23:21', 'Admin'),
('red_threshold', '72', 'integer', 'Completion rate % below which red flag', '2026-05-22 02:23:21', 'Admin'),
('response_time_target', '48', 'integer', 'Target hours for first contact attempt', '2026-05-22 02:23:21', 'Admin'),
('retry_delay_days', '2', 'integer', 'Days to wait before retry attempt', '2026-05-22 02:23:21', 'Admin'),
('team_hurdle', '6', 'integer', 'Team Hurdle day of Week (1=Mon ... 7=Sun)', '2026-05-22 02:23:21', 'Admin'),
('team_lead_span_full_time', '12', 'integer', 'Max volunteers for full-time Team Lead', '2026-05-22 02:23:21', 'Admin'),
('team_lead_span_player_coach', '8', 'integer', 'Max volunteers for player-coach Team Lead', '2026-05-22 02:23:21', 'Admin'),
('telegram_api_hash', 'f232b0ea0230fa287cbb19a56ac9eaf5', 'string', NULL, '2026-05-22 02:23:21', 'Admin'),
('telegram_api_id', '34628394', 'integer', NULL, '2026-05-22 02:23:21', 'Admin'),
('telegram_bot_Token', '8617796194:AAHnKvsPZEX2qYt2jRGfEAKkfO3TRun2lrE', 'string', 'Telegram Bot Token', '2026-05-22 02:23:21', NULL),
('telegram_bot_url', 'https://t.me/FollowupRMbot', 'string', 'Telegram Boot Url', '2026-05-22 02:23:21', NULL),
('telegram_phone_number', '9491957203', 'integer', NULL, '2026-05-22 02:23:21', 'Admin'),
('vnps_frequency_months', '3', 'integer', 'Months between vNPS surveys', '2026-05-22 02:23:21', 'Admin'),
('yellow_threshold', '73', 'integer', 'Completion rate % for yellow flag', '2026-05-22 02:23:21', 'Admin');

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

INSERT INTO `team_leads` (`team_lead_id`, `first_name`, `last_name`, `email`, `phone`, `role_type`, `campus`, `start_date`, `term_end_date`, `max_volunteers`, `current_volunteers`, `team_vnps_avg`, `team_retention_rate`, `team_completion_rate`, `boundary_incidents`, `created_at`, `updated_at`, `status`, `telegram_chat_id`) VALUES
('TL001', 'Vijaya', 'O', '', '9703006373', 'TeamLead', 'Ongole', '2026-05-12', NULL, 10, 7, NULL, NULL, NULL, 0, '2026-05-12 03:38:02', '2026-05-12 03:38:02', 'Active', '1828564030'),
('TL002', 'Rancy', 'A', '', '8297277478', 'TeamLead', 'Ongole', '2026-05-12', NULL, 10, 9, NULL, NULL, NULL, 0, '2026-05-12 03:40:54', '2026-05-12 03:40:54', 'Active', '1033708869'),
('TL003', 'Naga', 'Lakshmi', '', '6301140238', 'TeamLead', 'Ongole', '2026-05-12', NULL, 10, 6, NULL, NULL, NULL, 0, '2026-05-12 03:41:23', '2026-05-12 03:41:23', 'Active', '1469144400'),
('TL004', 'Prisk', 'G', '', '9859939859', 'TeamLead', 'Ongole', '2026-05-12', NULL, 10, 10, NULL, NULL, NULL, 0, '2026-05-12 03:59:24', '2026-05-12 03:59:24', 'Active', '6953881385');

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

INSERT INTO `users` (`user_id`, `first_name`, `last_name`, `email`, `phone`, `role_type`, `campus`, `status`, `telegram_chat_id`) VALUES
(1, 'Siva', 'Krishna', 'dayakar@example.com', '9999999999', 'Pastor', 'Ongole', 'Active', '1033708869'),
(2, 'Sai', 'krishna', 'dayakar@example.com', '9491957203', 'Admin', 'Ongole', 'Active', '1671347213');

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

INSERT INTO `volunteers` (`volunteer_id`, `first_name`, `last_name`, `email`, `phone`, `status`, `level`, `start_date`, `end_date`, `capacity_band`, `capacity_min`, `capacity_max`, `current_assignments`, `total_completed`, `total_assigned`, `completion_rate`, `avg_response_time`, `last_check_in`, `next_check_in`, `emotional_tone`, `vnps_score`, `burnout_risk`, `team_lead`, `campus`, `level_0_complete`, `crisis_trained`, `confidentiality_signed`, `background_check`, `boundary_violations`, `last_violation_date`, `created_at`, `updated_at`, `telegram_chat_id`, `last_assigned_at`) VALUES
('V001', 'Madhuri', 'p', 'testmadhuri@gmail.com', '9963053094', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 2, 2, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL003', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 13:55:15', '2026-06-02 08:45:14', 8166435332, NULL),
('V003', 'Anuraga', 'M', 'testanuraga@gmail.com', '8328465720', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 1, 1, 2, 50.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL001', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 13:59:03', '2026-06-22 08:04:10', 8202552095, NULL),
('V005', 'Deepa', 'Mata', 'deepa@gmail.com', '9492439695', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 1, 1, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL003', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:01:33', '2026-06-01 10:32:20', 1613107927, NULL),
('V006', 'Vijaya Lakshmi', 'Boddu', 'vijayalakshmi@gmail.com', '9014736978', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 0, 0, 0.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL004', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:02:20', '2026-05-20 15:12:33', 8423066940, NULL),
('V007', 'Sujitha', 'Athota', 'sujithaathota11@gmail.com', '7989695921', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 3, 3, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL001', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:03:34', '2026-06-24 03:14:18', 643057912, NULL),
('V011', 'Jagannadham Alekhya', 'J Alekhya', 'jagannadham.alekhya@example.com', NULL, 'Inactive', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 0, 0, 0.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL002', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-05-27 05:59:00', 5718361848, NULL),
('V022', 'Anúradhà', 'Thathí', 'thathi.anuradha@example.com', '9110558618', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 1, 1, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL004', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-06-02 05:05:04', 1299556324, NULL),
('V028', 'Jyothi', 'Bhaskar', 'jyothi@gmail.com', '9502033233', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 1, 1, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL003', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:05:23', '2026-06-01 10:35:04', 7853887180, NULL),
('V029', 'Hanna', 'kandi', 'hanna@gmail.com', '9392007564', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 1, 2, 3, 66.67, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL004', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:06:24', '2026-06-22 08:05:50', 6269766967, NULL),
('V030', 'Kranthi', 'Maddala', 'kranthi@gmail.com', '9003230344', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 2, 2, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL004', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:07:25', '2026-06-15 06:45:20', 5254883669, NULL),
('V031', 'Vidya', 'Sagar', 'vidya.pamula@gmail.com', '9110778525', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 2, 2, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL002', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:08:12', '2026-06-01 09:22:52', 1939558966, NULL),
('V032', 'Jyothi', 'Vankayalapati', 'jyothiv@gmail.com', '7032792613', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 2, 2, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL003', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:10:35', '2026-06-02 14:06:12', 7632019869, NULL),
('V033', 'Ananthalakshmi', 'Parre', 'Ananthalakshmi.mota@example.com', '8328172114', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 1, 0, 1, 0.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL004', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:10:48', '2026-06-01 02:25:15', 7375269624, NULL),
('V034', 'Hepsiba', 'B', 'hepsiba@gmail.com', '7396477929', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 2, 2, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL001', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:22:15', '2026-06-01 07:51:04', 5825055847, NULL),
('V035', 'John', 'katta', 'katta@gmail.com', '9948665540', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 1, 2, 3, 66.67, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL002', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:24:32', '2026-06-22 08:02:30', 1847620351, NULL),
('V036', 'Srinu', 'Mota', 'srinu.mota@example.com', NULL, 'Inactive', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 0, 0, 0.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL001', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-05-28 01:56:03', 1154445840, NULL),
('V037', 'Akhil', 'Rathna', 'akhil.rathna@example.com', NULL, 'Inactive', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 0, 0, 0.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL002', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-05-28 01:56:42', 8647834230, NULL),
('V040', 'Sujeetha', 'M', 'sujeetha.m@example.com', '9381499905', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 1, 1, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL001', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-05-26 06:13:14', 1139585227, NULL),
('V041', 'Lakshmi Prasanna', '', 'lakshmi.prasanna@example.com', '6309565470', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 2, 2, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL002', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-05-28 03:34:06', 1672379183, NULL),
('V042', 'Suneetha', 'S', 'suneetha.s@example.com', '7207870194', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 2, 2, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL003', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-06-03 03:12:23', 6727482918, NULL),
('V043', 'Anand Mathew', 'Kandi', 'anand.kandi@example.com', '9032903303', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 0, 0, 0.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL004', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-05-21 03:14:14', 1007318672, NULL),
('V044', 'Srikanth', 'Venny', 'srikanth.venny@example.com', '9573617176', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 3, 3, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL001', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-06-13 08:54:51', 7216879177, NULL),
('V045', 'Rakesh', '', 'rakesh@example.com', '9052058274', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 0, 0, 0.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL002', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-05-21 03:14:14', 850832427, NULL),
('V046', 'Ragalatha', 'Attanti', 'ragalatha.attanti@example.com', NULL, 'Inactive', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 0, 0, 0.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL004', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-05-28 01:57:48', 5652254177, NULL),
('V047', 'Mahesh', 'Galeti', 'mahesh.galeti@example.com', '9959957544', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 3, 3, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL001', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-06-04 15:54:55', 765520638, NULL),
('V050', 'spandana', '', 'spandana@example.com', NULL, 'Inactive', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 0, 0, 0.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL004', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-05-27 05:59:00', 1063620300, NULL),
('V052', 'Joy', 'Zenith', 'joy.zenith@example.com', '6300534359', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 1, 1, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL002', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-05-21 09:16:59', 1464729923, NULL),
('V053', 'Vijetha', 'Katikala', 'vijetha.katikala@example.com', '9182659030', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 1, 1, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL003', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-06-01 07:47:58', 1688552750, NULL),
('V054', 'Ramesh', '', 'ramesh@example.com', '9666585534', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 2, 2, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL002', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-06-01 10:26:38', 1542293225, NULL),
('V056', 'Rekha', '', 'rekha@example.com', '9963082033', 'Active', 'Level 1', '2026-05-19', NULL, 'Limited', 1, 2, 0, 2, 2, 100.00, NULL, '2026-05-20', '2026-06-19', NULL, NULL, NULL, 'TL004', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-19 14:04:27', '2026-06-17 03:58:20', 6537080358, NULL),
('V057', 'Prasanthi', 'Katta', 'testprasanthi@gmail.com', '9121919038', 'Active', 'Level 1', '2026-05-24', NULL, 'Limited', 1, 2, 0, 1, 1, 100.00, NULL, '2026-05-24', '2026-06-24', NULL, NULL, NULL, 'TL002', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-24 06:52:23', '2026-06-01 08:50:43', 6125973496, NULL),
('V058', 'Hanna', 'Kandi', 'hannaak143@gmail.com', '9063433403', 'Active', 'Level 1', '2026-05-31', NULL, 'Limited', 1, 2, 0, 0, 0, NULL, NULL, '2026-05-31', '2026-05-31', NULL, NULL, NULL, 'TL002', 'Ongole', NULL, NULL, NULL, NULL, 0, NULL, '2026-05-31 06:26:23', '2026-05-31 06:26:24', 1759657235, NULL);

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
