-- =============================================================================
-- Clear placeholder Telegram links
--
-- The migrated data has every TELEGRAM contact pointing at ONE chat id, which is
-- almost certainly a developer's own chat captured during testing. Two things
-- follow from that, and both are worse than having no links at all:
--
--   1. Every alert for all of those people is delivered to that one chat.
--   2. All of them satisfy the reachability half of FindEligibleAsync, so
--      auto-assignment treats them as contactable when only one person is.
--
-- This disconnects them rather than deleting the rows: person_contact history is
-- referenced by delivery records, and a soft disconnect is what the application's
-- own disconnect path does.
--
-- Run the SELECT first. Only run the UPDATE if the count is what you expect.
-- =============================================================================

-- ---- 1. What is about to change -------------------------------------------
SELECT
    pc.normalized_value           AS chat_id,
    COUNT(*)                      AS people_sharing_it,
    GROUP_CONCAT(p.full_name ORDER BY p.full_name SEPARATOR ', ') AS who
FROM person_contact pc
JOIN person p ON p.id = pc.person_id
WHERE pc.contact_type = 'TELEGRAM'
  AND pc.opted_out_at IS NULL
GROUP BY pc.normalized_value
HAVING COUNT(*) > 1;

-- ---- 2. Disconnect every SHARED chat id ------------------------------------
-- Deliberately scoped to chat ids held by more than one person. A chat id held by
-- exactly one person may well be genuine, and clearing those would throw away
-- real links along with the placeholders.
UPDATE person_contact pc
JOIN (
    SELECT normalized_value
    FROM person_contact
    WHERE contact_type = 'TELEGRAM' AND opted_out_at IS NULL
    GROUP BY normalized_value
    HAVING COUNT(*) > 1
) shared ON shared.normalized_value = pc.normalized_value
SET pc.opted_out_at = UTC_TIMESTAMP(3),
    pc.is_verified  = 0,
    pc.verified_at  = NULL
WHERE pc.contact_type = 'TELEGRAM'
  AND pc.opted_out_at IS NULL;

-- ---- 3. Confirm ------------------------------------------------------------
SELECT
    SUM(opted_out_at IS NULL) AS still_linked,
    SUM(opted_out_at IS NOT NULL) AS disconnected
FROM person_contact
WHERE contact_type = 'TELEGRAM';
