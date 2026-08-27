# 1 · Visitor entry

**Screen:** `wwwroot/templates/Peoples/PeopleEntry.html`
**Script:** `wwwroot/templates/assets/js/people-entry.js`
**URL:** `http://localhost:5043/templates/Peoples/PeopleEntry.html`

---

## What this screen does

It is the front door. Somebody visits the church, a desk operator writes them down, and
this is where the whole funnel starts. Everything downstream — assignment, follow-up,
nurture, escalation — exists because a row was created here.

It does **two separate things** in one submit, and the distinction matters when reading
the results:

1. **Records the PERSON** — the human being, permanently. `POST /api/people`
2. **Optionally opens a CARE CASE** — one journey of engagement with them, which is what
   volunteers actually work. `POST /api/cases`

The second only happens if **"Start follow-up"** is ticked. A person recorded without a
case is on file and in nobody's queue — that is the "Not started" column on the pipeline
screen, and there are 46 such people in the migrated data.

### Why they are separate

In the MVP one row was both. That meant a returning visitor either overwrote their own
history or became a second person. Here the person is permanent and cases come and go,
so somebody who visits, lapses and returns keeps one record with two journeys.

---

## Who can use it

Both endpoints sit on the same policy, so a data-entry operator can complete the whole
screen including starting follow-up:

| Policy | Roles |
|---|---|
| `CanRecordVisitors` (POST `/api/people`) | DATA_ENTRY, VOLUNTEER, TEAM_LEAD, PASTOR, ADMIN |
| `CanRecordVisitors` (POST `/api/cases`) | DATA_ENTRY, VOLUNTEER, TEAM_LEAD, PASTOR, ADMIN |

Verified 2026-08-26: signed in as a DATA_ENTRY account, both calls succeed.

---

## Tables written

### Always — recording the person

| Table | What lands there |
|---|---|
| `person` | One row. `public_id` (ULID), `full_name`, `given_name`, `family_name`, `gender`, `age_band`, `address_line`, `locality`, `postal_code`, `is_local`, `campus_id`, `lifecycle_status`, `notes` |
| `person_contact` | **One row per contact.** Mobile always; email only if given. Each has `contact_type`, `value` (as typed), `normalized_value` (for matching), `is_primary` |

Contacts are rows, not columns. A person with two numbers gets two rows — which is why
duplicate detection works on `normalized_value` and not on a single `phone` field.

### Only when "Start follow-up" is ticked

| Table | What lands there |
|---|---|
| `care_case` | One row. `reference_code` (`C2026xxx`), `stage='INTAKE'`, `status='AWAITING_ASSIGNMENT'`, `priority`, `connection_source_code`, `first_visit_on`, `person_id`, `campus_id` |

### Only when auto-assignment finds somebody

The case request sends `autoAssign: true`. If a volunteer on that campus is **active,
reachable and under capacity**, three more tables move:

| Table | What lands there |
|---|---|
| `care_case` | Updated — `assigned_volunteer_id`, `team_id`, `assigned_at`, `status='IN_PROGRESS'` |
| `care_case_assignment` | One history row, `reason='AUTO'` |
| `volunteer` | `current_case_load` +1, `lifetime_cases_assigned` +1, `last_assigned_at` set |
| `care_interaction` | **The first follow-up**, `stage='INITIAL_FOLLOW_UP'`, `method_code='CALL'`, `status='PENDING'`, scheduled `assignment.response_target_hours` (48h) out |

If nobody has capacity the case stays `AWAITING_ASSIGNMENT` and waits for the
`assign-unassigned` job or a manual assignment. The screen says so.

> **Reachability gate:** a volunteer needs an active login **and** a verified Telegram
> contact to be eligible. The migrated data has **27 volunteers who meet both**, so
> auto-assignment does find somebody and new cases are assigned immediately. If you
> want to see the queued path instead, temporarily opt a volunteer out — see step 5.

---

## The form

| Field | Required | Notes |
|---|---|---|
| First name | **Yes** | |
| Last name | no | |
| Mobile | **Yes** | 6–15 chars, digits and `+ - ( )` only |
| Email | no | must contain `@` if given |
| Gender | no | `MALE` `FEMALE` `OTHER` `PREFER_NOT_TO_SAY` |
| Age band | no | `UNDER_18` `18_25` `26_35` `36_45` `46_60` `OVER_60` |
| Address / locality / postal code | no | |
| Local resident | no | checkbox, defaults on |
| First visit date | no | |
| Connection source | no | `WALK_IN` `FRIEND_INVITE` `EVENT` `SOCIAL_MEDIA` `WEBSITE` `OTHER` |
| Priority | no | `LOW` `NORMAL` `HIGH` `URGENT` — only shown when starting follow-up |
| Notes | no | |
| **Start follow-up** | no | **the switch that decides whether a case is created** |

### Duplicate detection

As the mobile is typed, the screen calls `GET /api/people/lookup?q=` and warns if the
number is already on file. Submitting anyway is refused once, with the matching names,
and the operator must confirm — the second submit sends `allowDuplicate: true`.

The names come back **masked** from the lookup, so the pre-check cannot be used to
enumerate people by trying numbers.

---

## Sample test data

Invented, not from the production set. The mobile numbers use the `9000 0009xx` range so
they are easy to find and delete afterwards.

### V1 — Full record, follow-up started

| Field | Value |
|---|---|
| First name | `Anjali` |
| Last name | `Testkumar` |
| Mobile | `9000000901` |
| Email | `anjali.test@example.com` |
| Gender | `FEMALE` |
| Age band | `26_35` |
| Address | `12 Test Street` |
| Locality | `Ongole` |
| Postal code | `523001` |
| Local resident | ticked |
| First visit | today |
| Connection source | `WALK_IN` |
| Priority | `NORMAL` |
| Notes | `Manual test V1 — full record` |
| **Start follow-up** | **ticked** |

### V2 — Minimum fields, no follow-up

| Field | Value |
|---|---|
| First name | `Ravi` |
| Mobile | `9000000902` |
| **Start follow-up** | **unticked** |

Everything else blank. This is the "recorded but not started" case.

### V3 — Duplicate of V1

Same mobile as V1 (`9000000901`), first name `Anjali Duplicate`. Used to prove the
duplicate guard fires.

### V4 — High priority with a referral

| Field | Value |
|---|---|
| First name | `Suresh` |
| Last name | `Testrao` |
| Mobile | `9000000904` |
| Age band | `OVER_60` |
| Connection source | `FRIEND_INVITE` |
| Priority | `HIGH` |
| **Start follow-up** | **ticked** |

### V5 — Invalid input

Used to prove validation. Try each on its own:

| Try | Expect |
|---|---|
| Empty first name | "Enter the visitor's first name." |
| Mobile `abc` | "That does not look like a valid mobile number." |
| Mobile `123` | too short — same message |
| Email `notanemail` | "That does not look like a valid email address." |

---

## Test steps

### Step 1 · Baseline

Run this first and keep the numbers.

```sql
SELECT 'person' t, COUNT(*) n FROM person
UNION ALL SELECT 'person_contact', COUNT(*) FROM person_contact
UNION ALL SELECT 'care_case', COUNT(*) FROM care_case
UNION ALL SELECT 'care_interaction', COUNT(*) FROM care_interaction
UNION ALL SELECT 'care_case_assignment', COUNT(*) FROM care_case_assignment;
```

### Step 2 · V1 — full record with follow-up

Sign in as `9999999999`, open the screen, enter V1, save.

✅ **Expect on screen:** a success message naming the person, and either "Assigned to
&lt;volunteer&gt;" or "No volunteer has spare capacity, so it is queued for assignment."

✅ **Expect in the database:**

```sql
SELECT p.public_id, p.full_name, p.gender, p.age_band, p.is_local, p.locality
FROM person p WHERE p.full_name LIKE 'Anjali%';

-- two contact rows: mobile + email
SELECT contact_type, value, normalized_value, is_primary
FROM person_contact WHERE person_id = (SELECT id FROM person WHERE full_name LIKE 'Anjali Test%');

-- the case
SELECT reference_code, stage, status, priority, connection_source_code, first_visit_on
FROM care_case WHERE person_id = (SELECT id FROM person WHERE full_name LIKE 'Anjali Test%');
```

| Column | Should be |
|---|---|
| `person.full_name` | `Anjali Testkumar` |
| `person_contact` rows | **2** — one MOBILE (`is_primary=1`), one EMAIL |
| `care_case.stage` | `INTAKE` |
| `care_case.status` | `AWAITING_ASSIGNMENT`, or `IN_PROGRESS` if a volunteer was found |
| `care_case.reference_code` | `C2026___` — generated, not blank |

### Step 3 · V2 — person only

Enter V2, leave "Start follow-up" **unticked**, save.

✅ **Expect:** one `person` row, **one** `person_contact` row (mobile only), and **no**
`care_case` row.

```sql
SELECT p.full_name,
       (SELECT COUNT(*) FROM person_contact c WHERE c.person_id = p.id) contacts,
       (SELECT COUNT(*) FROM care_case cc WHERE cc.person_id = p.id) cases
FROM person p WHERE p.full_name = 'Ravi';
```

Should read `contacts = 1`, `cases = 0`.

### Step 4 · V3 — the duplicate guard

Enter V3 (same mobile as V1).

✅ **Expect while typing:** a notice appears under the mobile field once the number
matches.
✅ **Expect on first save:** refused, naming Anjali Testkumar, with a confirm prompt.
✅ **Expect on confirm:** saved as a separate person.

```sql
SELECT COUNT(*) should_be_2 FROM person_contact WHERE normalized_value LIKE '%9000000901';
```

⚠️ If you do **not** confirm, nothing should be written — check the count is still 1.

### Step 5 · Auto-assignment

27 volunteers in the migrated data are reachable, so this happens without a fixture.
Enter V4 with follow-up ticked.

✅ **Expect on screen:** "Assigned to &lt;name&gt;."
✅ **Expect in the database — all four tables move:**

```sql
SELECT cc.reference_code, cc.status, cc.assigned_at,
       vp.full_name AS volunteer, t.name AS team,
       (SELECT COUNT(*) FROM care_case_assignment a WHERE a.care_case_id = cc.id) history_rows,
       (SELECT COUNT(*) FROM care_interaction i WHERE i.care_case_id = cc.id) interactions
FROM care_case cc
JOIN person p       ON p.id = cc.person_id
LEFT JOIN volunteer v ON v.id = cc.assigned_volunteer_id
LEFT JOIN person vp ON vp.id = v.person_id
LEFT JOIN team t    ON t.id = cc.team_id
WHERE p.full_name LIKE 'Suresh Test%';
```

| Column | Should be |
|---|---|
| `status` | `IN_PROGRESS` |
| `volunteer` | a real name, not null |
| `history_rows` | **1** (`reason='AUTO'`) |
| `interactions` | **1** — the first follow-up, `PENDING`, dated ~2 days out |

And the volunteer's counter went up:

```sql
SELECT p.full_name, v.current_case_load, v.lifetime_cases_assigned, v.last_assigned_at
FROM volunteer v JOIN person p ON p.id = v.person_id
WHERE v.person_id = (SELECT person_id FROM person_contact WHERE normalized_value = '900000901');
```

### Step 6 · One open case per person

Try to start follow-up for V1 a second time — reopen the screen, enter the same mobile,
confirm past the duplicate warning, and tick "Start follow-up".

✅ **Expect:** the person is recorded (you confirmed the duplicate), but the case is
refused: *"&lt;name&gt; already has an open case (C2026xxx). Close it before opening
another."*

That guard is what stops two volunteers working the same person without knowing.

### Step 7 · Validation

Work through V5. Each should be refused **on the client**, with no network request and
nothing written.

### Step 8 · Does it show up downstream?

This is the point of the whole screen — the funnel should now know about these people.

```sql
-- Should include V1, V2 (as "no case") and V4
SELECT p.full_name, IFNULL(cc.stage,'(no case)') stage, IFNULL(cc.status,'—') status
FROM person p
LEFT JOIN care_case cc ON cc.person_id = p.id
WHERE p.full_name LIKE '%Test%' OR p.full_name = 'Ravi'
ORDER BY p.id DESC;
```

Then open **People → pipeline** (`/templates/Peoples/Pipeline.html`) as the administrator.

✅ **Expect:** V1 and V4 appear under Intake or First contact, V2 appears in the
**Not started** count, and searching `9000000` finds them.

---

## Cleanup

Removes only what this document creates.

```sql
-- restore the volunteer counter that auto-assignment moved
UPDATE volunteer v
SET current_case_load = GREATEST(0, CAST(current_case_load AS SIGNED) - 1),
    lifetime_cases_assigned = GREATEST(0, CAST(lifetime_cases_assigned AS SIGNED) - 1)
WHERE v.id IN (
    SELECT cc.assigned_volunteer_id FROM care_case cc
    JOIN person p ON p.id = cc.person_id
    WHERE p.full_name LIKE '%Test%' AND cc.assigned_volunteer_id IS NOT NULL);

-- the test people and everything hanging off them
DELETE ci FROM care_interaction ci
  JOIN care_case cc ON cc.id = ci.care_case_id
  JOIN person p ON p.id = cc.person_id
  WHERE p.full_name LIKE '%Test%' OR p.full_name = 'Ravi';

DELETE a FROM care_case_assignment a
  JOIN care_case cc ON cc.id = a.care_case_id
  JOIN person p ON p.id = cc.person_id
  WHERE p.full_name LIKE '%Test%' OR p.full_name = 'Ravi';

DELETE cc FROM care_case cc
  JOIN person p ON p.id = cc.person_id
  WHERE p.full_name LIKE '%Test%' OR p.full_name = 'Ravi';

-- person_contact cascades on person delete
DELETE FROM person WHERE full_name LIKE '%Test%' OR full_name = 'Ravi';
```

Then re-run the step 1 baseline query. Every count should match what you started with.

> The `LIKE '%Test%'` filters are why the sample names all carry "Test". Check the
> production data has no real person whose name contains it before running these —
> in the migrated set, it does not.

---

## Fixed while writing this document

**Reference code generation was broken — no visitor could be recorded at all.**

`LPAD(n, 4, '0')` in MySQL *truncates* when `n` is longer than the width. The MVP's codes
carry the year (`P2026149`, seven digits), so the generator returned the first four
characters — `P2026` — for every new person. The first intake took that code and every
one after it collided on the unique key with
`Duplicate entry 'P2026' for key 'person.ux_person_reference_code'`.

`care_case` had the identical bug (`C20261`). Both are on this screen's path, which is
why nothing could be saved. `volunteer` (width 3, max 59) and `escalation` were not
affected but shared the pattern, so all four were fixed the same way:

```sql
LPAD(n, GREATEST(<width>, CHAR_LENGTH(n)), '0')
```

Padding still applies to small numbers (`0007`), and large ones pass through intact.
Verified: two consecutive intakes now produce `P2026150` and `P2026151`.

---

## What to record

For each step: pass / fail, and if it failed, the screen message and the actual SQL
result beside the expected one. A failure here matters more than anywhere else in the
system: if the front door records people wrongly, every screen after it is working from
bad data.
