-- =============================================================================
-- 003 — Team management access
--
-- Adds the two grants that decide who, besides an administrator, may reach the
-- team management screen. Both default OFF.
--
-- An administrator always has access; it is not a setting and cannot be turned
-- off. These two only widen it.
--
-- Safe to re-run: INSERT IGNORE leaves an existing value alone, so turning a
-- grant on and re-applying the migration does not silently revoke it.
-- =============================================================================

USE cms_api_db;

INSERT IGNORE INTO app_setting
    (setting_key, setting_value, value_type, category, description, min_value, max_value)
VALUES
    ('team.manage_by_pastor',    'false', 'BOOLEAN', 'TEAM',
     'Pastors may manage teams at their own campus',        NULL, NULL),

    ('team.manage_by_team_lead', 'false', 'BOOLEAN', 'TEAM',
     'Team leads may rename and resize the team they lead', NULL, NULL);

SELECT setting_key, setting_value, description
FROM app_setting
WHERE setting_key IN ('team.manage_by_pastor', 'team.manage_by_team_lead');
