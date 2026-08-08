-- ============================================================
-- RM_CMS — Authentication & Authorization schema
-- Branch: feature/auth_security
--
-- Idempotent: safe to re-run. Contains NO seed users and NO secrets.
-- The first administrator is created by the application at startup
-- from environment variables (see appsettings.json -> Auth:Bootstrap).
--
-- NOTE: GUID columns are varchar(36), deliberately NOT char(36).
-- MySqlConnector auto-converts CHAR(36) to System.Guid, which Dapper then cannot
-- map onto the string properties used throughout this codebase ("Object must
-- implement IConvertible"). varchar(36) is returned as a plain string.
-- ============================================================

-- ------------------------------------------------------------
-- 1. Users
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `auth_users` (
  `user_id`               varchar(36)   NOT NULL,              -- GUID
  `username`              varchar(100)  NOT NULL,              -- login handle (mobile number today)
  `normalized_username`   varchar(100)  NOT NULL,              -- upper-invariant, used for lookup
  `email`                 varchar(150)  DEFAULT NULL,
  `mobile_number`         varchar(30)   NOT NULL,
  `display_name`          varchar(150)  NOT NULL,

  -- Credentials. `password_hash` empty string == no usable password (must be set by an admin).
  `password_hash`         varchar(500)  NOT NULL DEFAULT '',
  `security_stamp`        varchar(36)   NOT NULL,              -- rotates on any credential/role change
  `token_version`         int(11)       NOT NULL DEFAULT 1,    -- bumped to hard-invalidate all access tokens

  -- State
  `is_active`             tinyint(1)    NOT NULL DEFAULT 1,
  `must_change_password`  tinyint(1)    NOT NULL DEFAULT 0,
  `access_failed_count`   int(11)       NOT NULL DEFAULT 0,
  `lockout_end_utc`       datetime      DEFAULT NULL,
  `last_login_utc`        datetime      DEFAULT NULL,
  `password_changed_utc`  datetime      DEFAULT NULL,

  -- Links into the existing domain (nullable: an admin need not be a volunteer)
  `volunteer_id`          varchar(20)   DEFAULT NULL,
  `team_lead_id`          varchar(20)   DEFAULT NULL,
  `person_id`             varchar(20)   DEFAULT NULL,

  -- Optimistic concurrency
  `row_version`           bigint(20)    NOT NULL DEFAULT 1,

  `created_at`            datetime      NOT NULL DEFAULT current_timestamp(),
  `updated_at`            datetime      NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `created_by`            varchar(100)  DEFAULT NULL,

  PRIMARY KEY (`user_id`),
  UNIQUE KEY `ux_auth_users_username` (`normalized_username`),
  KEY `idx_auth_users_mobile`    (`mobile_number`),
  KEY `idx_auth_users_volunteer` (`volunteer_id`),
  KEY `idx_auth_users_teamlead`  (`team_lead_id`),
  KEY `idx_auth_users_active`    (`is_active`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- ------------------------------------------------------------
-- 2. Roles  (fixed set — see Security/Roles.cs)
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `auth_roles` (
  `role_name`   varchar(30)  NOT NULL,
  `description` varchar(200) DEFAULT NULL,
  `created_at`  datetime     NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`role_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

INSERT INTO `auth_roles` (`role_name`, `description`) VALUES
  ('Admin',     'Full system administration, configuration and user management'),
  ('Pastor',    'Cross-team oversight dashboards, read-mostly'),
  ('TeamLead',  'Manages a team of volunteers: escalations, check-ins, nurture review'),
  ('Volunteer', 'Handles assigned people: follow-ups and nurture steps'),
  ('Member',    'Authenticated end user: own attendance and events only')
ON DUPLICATE KEY UPDATE `description` = VALUES(`description`);

-- ------------------------------------------------------------
-- 3. User <-> Role
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `auth_user_roles` (
  `user_id`     varchar(36)  NOT NULL,
  `role_name`   varchar(30)  NOT NULL,
  `assigned_at` datetime     NOT NULL DEFAULT current_timestamp(),
  `assigned_by` varchar(100) DEFAULT NULL,
  PRIMARY KEY (`user_id`, `role_name`),
  KEY `idx_aur_role` (`role_name`),
  CONSTRAINT `fk_aur_user` FOREIGN KEY (`user_id`)   REFERENCES `auth_users` (`user_id`) ON DELETE CASCADE,
  CONSTRAINT `fk_aur_role` FOREIGN KEY (`role_name`) REFERENCES `auth_roles` (`role_name`) ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- ------------------------------------------------------------
-- 4. Refresh tokens
--    Only the SHA-256 hash is stored — the raw token never touches the DB.
--    `family_id` groups a rotation chain so reuse can revoke the whole family.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `auth_refresh_tokens` (
  `token_id`             varchar(36)   NOT NULL,
  `user_id`              varchar(36)   NOT NULL,
  `family_id`            varchar(36)   NOT NULL,           -- constant across one login's rotation chain
  `token_hash`           char(64)      NOT NULL,           -- SHA-256 hex of the 64-byte token
  `parent_token_id`      varchar(36)   DEFAULT NULL,
  `replaced_by_token_id` varchar(36)   DEFAULT NULL,

  `expires_utc`          datetime      NOT NULL,
  `created_utc`          datetime      NOT NULL DEFAULT current_timestamp(),
  `revoked_utc`          datetime      DEFAULT NULL,
  `revoked_reason`       varchar(60)   DEFAULT NULL,       -- Rotated | Logout | LogoutAll | ReuseDetected | PasswordChanged | Disabled | RoleChanged

  -- Device binding (supports multiple concurrent devices: one family per device)
  `device_label`         varchar(120)  DEFAULT NULL,
  `user_agent_hash`      char(64)      DEFAULT NULL,
  `created_ip`           varchar(45)   DEFAULT NULL,

  PRIMARY KEY (`token_id`),
  UNIQUE KEY `ux_art_hash`   (`token_hash`),
  KEY `idx_art_user`         (`user_id`),
  KEY `idx_art_family`       (`family_id`),
  KEY `idx_art_expires`      (`expires_utc`),
  CONSTRAINT `fk_art_user` FOREIGN KEY (`user_id`) REFERENCES `auth_users` (`user_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- ------------------------------------------------------------
-- 5. Password history (prevents reuse of the last N passwords)
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `auth_password_history` (
  `history_id`    bigint(20)   NOT NULL AUTO_INCREMENT,
  `user_id`       varchar(36)  NOT NULL,
  `password_hash` varchar(500) NOT NULL,
  `created_utc`   datetime     NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`history_id`),
  KEY `idx_aph_user` (`user_id`, `created_utc`),
  CONSTRAINT `fk_aph_user` FOREIGN KEY (`user_id`) REFERENCES `auth_users` (`user_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- ------------------------------------------------------------
-- 6. Security audit log
--    Never stores passwords, tokens or secrets — only outcomes.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `auth_login_audit` (
  `audit_id`           bigint(20)   NOT NULL AUTO_INCREMENT,
  `user_id`            varchar(36)  DEFAULT NULL,           -- null when the username did not resolve
  `username_attempted` varchar(100) DEFAULT NULL,
  `event_type`         varchar(40)  NOT NULL,               -- LoginSucceeded | LoginFailed | LoginLockedOut | TokenRefreshed
                                                            -- | RefreshReuseDetected | Logout | LogoutAll | PasswordChanged
                                                            -- | PasswordSetByAdmin | RolesChanged | AccountDisabled
                                                            -- | AccountEnabled | AuthorizationDenied
  `succeeded`          tinyint(1)   NOT NULL DEFAULT 0,
  `detail`             varchar(300) DEFAULT NULL,
  `ip_address`         varchar(45)  DEFAULT NULL,
  `user_agent`         varchar(300) DEFAULT NULL,
  `correlation_id`     varchar(36)  DEFAULT NULL,
  `created_utc`        datetime     NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`audit_id`),
  KEY `idx_ala_user`    (`user_id`, `created_utc`),
  KEY `idx_ala_event`   (`event_type`, `created_utc`),
  KEY `idx_ala_created` (`created_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


-- ============================================================
-- MIGRATION — back-fill accounts for existing staff
--
-- Creates a DISABLED, password-less account for every active volunteer and
-- team lead so an administrator can enable them and set a first password via
--   POST /api/admin/auth/users/{id}/set-password
--
-- These accounts CANNOT be logged into until that happens:
--   password_hash = ''  -> rejected by the login path
--   is_active     = 0   -> rejected by the login path
--
-- Run this once, after the tables above.
-- ============================================================

-- 7a. Team leads
INSERT INTO `auth_users`
  (`user_id`, `username`, `normalized_username`, `email`, `mobile_number`, `display_name`,
   `password_hash`, `security_stamp`, `token_version`, `is_active`, `must_change_password`,
   `team_lead_id`, `created_by`)
SELECT
  UUID(),
  tl.`phone`,
  UPPER(tl.`phone`),
  tl.`email`,
  tl.`phone`,
  CONCAT(tl.`first_name`, ' ', tl.`last_name`),
  '',            -- no usable password
  UUID(),
  1,
  0,             -- disabled until an admin activates
  1,             -- force password change on first login
  tl.`team_lead_id`,
  'migration:auth_schema.sql'
FROM `team_leads` tl
WHERE tl.`phone` IS NOT NULL
  AND tl.`phone` <> ''
  AND NOT EXISTS (SELECT 1 FROM `auth_users` au WHERE au.`normalized_username` = UPPER(tl.`phone`));

INSERT IGNORE INTO `auth_user_roles` (`user_id`, `role_name`, `assigned_by`)
SELECT au.`user_id`, 'TeamLead', 'migration:auth_schema.sql'
FROM `auth_users` au
WHERE au.`team_lead_id` IS NOT NULL;

-- 7b. Volunteers
INSERT INTO `auth_users`
  (`user_id`, `username`, `normalized_username`, `email`, `mobile_number`, `display_name`,
   `password_hash`, `security_stamp`, `token_version`, `is_active`, `must_change_password`,
   `volunteer_id`, `created_by`)
SELECT
  UUID(),
  v.`phone`,
  UPPER(v.`phone`),
  v.`email`,
  v.`phone`,
  CONCAT(v.`first_name`, ' ', v.`last_name`),
  '',
  UUID(),
  1,
  0,
  1,
  v.`volunteer_id`,
  'migration:auth_schema.sql'
FROM `volunteers` v
WHERE v.`phone` IS NOT NULL
  AND v.`phone` <> ''
  AND NOT EXISTS (SELECT 1 FROM `auth_users` au WHERE au.`normalized_username` = UPPER(v.`phone`));

INSERT IGNORE INTO `auth_user_roles` (`user_id`, `role_name`, `assigned_by`)
SELECT au.`user_id`, 'Volunteer', 'migration:auth_schema.sql'
FROM `auth_users` au
WHERE au.`volunteer_id` IS NOT NULL;


-- ============================================================
-- HOUSEKEEPING — run periodically (e.g. nightly)
-- Expired/revoked refresh tokens are not needed once well past expiry.
-- ============================================================
-- DELETE FROM `auth_refresh_tokens`
--  WHERE `expires_utc` < (UTC_TIMESTAMP() - INTERVAL 30 DAY);
