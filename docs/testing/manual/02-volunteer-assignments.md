# 2 · Volunteer assignments

**Screen:** `wwwroot/pages/care/my-assignments.html`
**Script:** `wwwroot/assets/js/my-assignments.js`
**URL:** `http://localhost:5043/pages/care/my-assignments.html`

---

## What this screen does

This is where the work actually happens. A volunteer opens it, sees the people they are
meant to contact, rings them, and writes down what happened.

**It is also the most consequential screen in the system.** Logging a contact does not
just record a fact — it runs the progression engine, and the engine decides what happens
to that person next. One dropdown choice can start a nurture journey, raise a crisis to
a team lead, or close the case permanently.

### Two lists, one underlying thing

| On screen | Really |
|---|---|
| **My List** | `care_interaction` rows with `stage = 'INITIAL_FOLLOW_UP'` |
| **Nurture Steps Due** | the same table with `stage = 'NURTURE'` |

Both come from **one call** — `GET /api/contacts/mine`. In the new model a planned
contact is a `care_interaction` whichever stage it belongs to, so the two grids are two
views of one list split on `stage`. The MVP used four separate endpoints across two
tables.

### Whose work it shows

From the **signed-in token**, via `volunteer_id` on the account. The MVP took
`?volunteerid=` from the query string, so editing the URL showed another volunteer's
people — including the pastoral notes on them. The parameter is still read for the
on-screen id but never decides what is fetched.

> A volunteer whose account is not linked to a `volunteer` record gets
> *"Your account is not linked to a volunteer record."* rather than an empty list —
> those are different problems and should read differently.

### The list shows what is DUE, not everything pending

`/api/contacts/mine` returns contacts whose `scheduled_on` has arrived. A volunteer with
13 pending steps may see 7 — the other 6 are scheduled for future dates and appear when
they fall due.

That is deliberate: a work list showing next month's calls is a work list nobody reads to
the bottom of. **When counting rows to verify a test, filter the same way**, or the
screen and your SQL will disagree for a reason that is not a bug:

```sql
SELECT SUM(scheduled_on <= UTC_DATE()) AS due_now,
       SUM(scheduled_on >  UTC_DATE()) AS scheduled_later
FROM care_interaction ci
JOIN volunteer v ON v.id = ci.volunteer_id
JOIN person vp ON vp.id = v.person_id
JOIN user_account ua ON ua.person_id = vp.id
WHERE ci.status = 'PENDING' AND ua.username = '<login>';
```

---

## Who can use it

| Endpoint | Policy | Roles |
|---|---|---|
| `GET /api/contacts/mine` | `VolunteerOrAbove` | VOLUNTEER, TEAM_LEAD, PASTOR, ADMIN |
| `POST /api/contacts/{id}/log` | `VolunteerOrAbove` | same |

Object-level check on top of the policy: `CanActForVolunteer` refuses a contact assigned
to somebody else — *"This contact is assigned to another volunteer."* A team lead can act
for their own volunteers; a volunteer cannot act for a peer.

---

## The four response options, and what they mean

The wording on screen is unchanged from the MVP. What changed is that the meaning is now
**data** in `care_progression_rule` rather than a hard-coded branch.

| On screen | Sends | Engine action |
|---|---|---|
| Not contacted / No response | `NO_ANSWER` | retry, or next nurture step |
| Normal | `SPOKE` + an intent | start or continue nurture |
| Needs follow-up | `NEEDS_SUPPORT` | **escalate** to the team lead |
| Crisis | `CRISIS` | **escalate immediately**, EMERGENCY tier |

### The full outcome vocabulary

| Code | Label | Contact made | Escalates |
|---|---|---|---|
| `SPOKE` | Spoke with them | yes | no |
| `NEEDS_SUPPORT` | Needs Followup | yes | **yes** (STANDARD) |
| `CRISIS` | Crisis disclosed | yes | **yes** (EMERGENCY) |
| `NO_ANSWER` | No answer | no | no — schedules a retry |
| `LEFT_MESSAGE` | Left a message | no | no — schedules a retry |
| `WRONG_NUMBER` | Wrong number | no | no — closes the case |
| `UNREACHABLE` | Unreachable | no | no — closes the case |

### The intent vocabulary

Asked when contact was made. `DO_NOT_CONTACT` is the one with teeth.

| Code | Label | Blocks future contact |
|---|---|---|
| `WANTS_CONNECTION` | Wants to connect / keep in touch | |
| `UNDECIDED` | Open but undecided | |
| `NEEDS_TIME` | Interested, asked to be contacted later | |
| `ALREADY_CHURCHED` | Already belongs to another church | |
| `NOT_INTERESTED` | Not interested | |
| `DO_NOT_CONTACT` | Asked not to be contacted again | **YES — flags the PERSON** |
| `UNKNOWN` | Not established | |

---

## The progression rules — what actually decides

Read top to bottom; **the first match wins.** This is the whole engine.

| Priority | Stage | Outcome | Intent | Action |
|---|---|---|---|---|
| 1 | any | `CRISIS` | * | **ESCALATE** |
| 10 | any | * | `DO_NOT_CONTACT` | **CLOSE_CASE** |
| 20 | any | * | `NOT_INTERESTED` | CLOSE_CASE |
| 30 | any | * | `ALREADY_CHURCHED` | CLOSE_CASE |
| 40 | any | `NEEDS_SUPPORT` | * | **ESCALATE** |
| 60 | INITIAL_FOLLOW_UP | `SPOKE` | `WANTS_CONNECTION` | START_NURTURE |
| 60 | NURTURE | `SPOKE` | * | CONTINUE_NURTURE |
| 61 | INITIAL_FOLLOW_UP | `SPOKE` | `UNDECIDED` | START_NURTURE |
| 62 | INITIAL_FOLLOW_UP | `SPOKE` | `NEEDS_TIME` | SCHEDULE_RETRY |
| 70 | INITIAL_FOLLOW_UP | `NO_ANSWER` | * | SCHEDULE_RETRY |
| 70 | NURTURE | `NO_ANSWER` | * | CONTINUE_NURTURE |
| 71 | INITIAL_FOLLOW_UP | `LEFT_MESSAGE` | * | SCHEDULE_RETRY |
| 71 | NURTURE | `LEFT_MESSAGE` | * | CONTINUE_NURTURE |
| 80 | either | `WRONG_NUMBER` | * | CLOSE_CASE |
| 81 | either | `UNREACHABLE` | * | CLOSE_CASE |
| 999 | any | * | * | MANUAL_REVIEW |

**Priority 1 and 10 are the two that matter most.** `CRISIS` outranks everything —
somebody disclosing a crisis is escalated regardless of what else was ticked. And
`DO_NOT_CONTACT` outranks every positive outcome, so a person who asks to be left alone
is left alone even if the volunteer also ticked "wants to connect".

---

## Tables written

### Always — the contact itself

| Table | What changes |
|---|---|
| `care_interaction` | The row is **completed**: `status='COMPLETED'`, `outcome_code`, `intent_code`, `made_contact`, `occurred_at`, `duration_minutes`, `notes`, `row_version` +1 |

### Then, depending on what the engine decided

| Action | Also writes |
|---|---|
| `START_NURTURE` | `care_case` → `stage='NURTURE'`, `nurture_plan_id`, `next_step_due_on`; **a new `care_interaction`** for step 1 |
| `CONTINUE_NURTURE` | `care_case` → `current_step_number` +1, `next_step_due_on`; **a new `care_interaction`** for the next step |
| `SCHEDULE_RETRY` | `care_case` → `next_action_on`, `contact_attempt_count` +1; **a new `care_interaction`** |
| `ESCALATE` | **`escalation`** (new row) + `care_case` → `status='ESCALATED'` — **the case pauses** |
| `CLOSE_CASE` | `care_case` → `status='CLOSED'`, `closed_at`, `close_reason`; `volunteer.current_case_load` −1, `lifetime_cases_closed` +1 |
| intent = `DO_NOT_CONTACT` | **`person.do_not_contact`** set — outlives this case, blocks every future one |

> **Escalation pauses the case.** Once `status='ESCALATED'`, the nurture scheduler skips
> it entirely. It stays frozen until a team lead resolves the escalation (screen 5),
> which resumes it where it stopped. If nurture appears stuck, check for an open
> escalation before anything else.

---

## Sample test data

These use people already in the migrated data, so no setup is needed. Pick a volunteer
with pending work:

```sql
-- Volunteers with something in their list, and what stage it is
SELECT vp.full_name AS volunteer, ua.username AS login,
       SUM(ci.stage = 'INITIAL_FOLLOW_UP') AS my_list,
       SUM(ci.stage = 'NURTURE')           AS nurture_due
FROM care_interaction ci
JOIN volunteer v     ON v.id = ci.volunteer_id
JOIN person vp       ON vp.id = v.person_id
JOIN user_account ua ON ua.person_id = vp.id
WHERE ci.status = 'PENDING'
GROUP BY v.id, vp.full_name, ua.username
HAVING my_list > 0 OR nurture_due > 0
ORDER BY (my_list + nurture_due) DESC
LIMIT 5;
```

Sign in with that `login` and `Xq7#vTrb92!mKp`. Clear the password-change flag first:

```sql
UPDATE user_account SET must_change_password = 0 WHERE username = '<login>';
```

### ⚠️ Read this before planning your run

The migrated data is **lopsided**, and it decides which tests you can do where:

| Stage | Pending contacts | What that means |
|---|---|---|
| `NURTURE` | **183** | Plenty. Every volunteer has a full "Nurture Steps Due" grid |
| `INITIAL_FOLLOW_UP` | **1** | "My List" is empty for all but one volunteer |

The production snapshot was taken mid-flight, so almost every initial follow-up had
already been completed. **The rules differ by stage** — compare priorities 60–71 in the
table above — so testing only nurture steps leaves half the engine unexercised.

**To get initial follow-ups to test with, use screen 1.** Recording a visitor with
"Start follow-up" ticked auto-assigns them and creates exactly one
`INITIAL_FOLLOW_UP` contact. Create three or four visitors that way first, note which
volunteer they landed on, and sign in as that volunteer:

```sql
-- who did the visitors you just created land on?
SELECT p.full_name AS visitor, vp.full_name AS volunteer, ua.username AS login,
       ci.public_id AS interaction_id
FROM care_interaction ci
JOIN care_case cc    ON cc.id = ci.care_case_id
JOIN person p        ON p.id = cc.person_id
JOIN volunteer v     ON v.id = ci.volunteer_id
JOIN person vp       ON vp.id = v.person_id
JOIN user_account ua ON ua.person_id = vp.id
WHERE ci.stage = 'INITIAL_FOLLOW_UP' AND ci.status = 'PENDING'
ORDER BY ci.id DESC;
```

> **Also worth knowing:** 19 cases sit in `INITIAL_FOLLOW_UP` with **no pending contact
> at all** — the snapshot caught them between steps. They are not in anybody's list and
> will not move on their own. Whether that needs a sweep to recover them is a real
> question, not a testing artefact.

### The five cases to exercise

| # | Response | Intent | Proves |
|---|---|---|---|
| **A1** | Normal | `WANTS_CONNECTION` | nurture starts, step 1 created |
| **A2** | Not contacted | — | retry scheduled, attempt count rises |
| **A3** | Needs follow-up | — | escalation raised, **case pauses** |
| **A4** | Crisis | — | EMERGENCY escalation, outranks everything |
| **A5** | Normal | `DO_NOT_CONTACT` | case closed **and the person flagged** |

Escalation cases (A3, A4) need a reason and a description of **at least 10 characters** —
the client says so before submitting rather than letting the server refuse.

---

## Test steps

### Step 1 · Baseline

```sql
SELECT 'pending contacts' k, COUNT(*) n FROM care_interaction WHERE status='PENDING'
UNION ALL SELECT 'completed', COUNT(*) FROM care_interaction WHERE status='COMPLETED'
UNION ALL SELECT 'open escalations', COUNT(*) FROM escalation WHERE status NOT IN ('RESOLVED','CLOSED','REFERRED_OUT')
UNION ALL SELECT 'cases in nurture', COUNT(*) FROM care_case WHERE stage='NURTURE'
UNION ALL SELECT 'closed cases', COUNT(*) FROM care_case WHERE status='CLOSED'
UNION ALL SELECT 'do-not-contact people', COUNT(*) FROM person WHERE do_not_contact=1;
```

### Step 2 · The work list is yours, and only yours

Sign in as the volunteer and open the screen.

✅ **Expect:** their name in the header, their people in the grids, counts matching the
query above for that volunteer.

Now try to see somebody else's:

```
Assignments.html?volunteerid=<another volunteer's public_id>
```

✅ **Expect:** **the same list as before.** The parameter changes the id shown on screen
and nothing else. If the list changes, stop — that is the MVP hole reopened.

### Step 3 · A1 — Normal + wants to connect

**Needs an initial follow-up** — create one via screen 1 if My List is empty (see the
warning above).

Open a person from **My List**, choose **Normal**, intent **Wants to connect**, add a
note, submit.

✅ **Expect on screen:** a message saying the nurture journey has started.

```sql
-- the contact just logged
SELECT ci.status, ci.outcome_code, ci.intent_code, ci.made_contact, ci.occurred_at, ci.notes
FROM care_interaction ci WHERE ci.public_id = '<from the URL or the list>';

-- the case moved, and a new step exists
SELECT cc.reference_code, cc.stage, cc.status, cc.current_step_number, cc.next_step_due_on,
       (SELECT COUNT(*) FROM care_interaction i
         WHERE i.care_case_id = cc.id AND i.status='PENDING') pending_now
FROM care_case cc WHERE cc.id = (SELECT care_case_id FROM care_interaction WHERE public_id='<id>');
```

| Should be | |
|---|---|
| logged contact `status` | `COMPLETED` |
| case `stage` | `NURTURE` |
| case `next_step_due_on` | a date roughly 7 days out |
| `pending_now` | **1** — the next step was created |

### Step 4 · A2 — Not contacted

Pick another from My List, choose **Not contacted**, submit. Note the stage matters here:
from `INITIAL_FOLLOW_UP` this schedules a **retry**, but the same choice on a nurture
step **continues the plan** instead (priority 70, two different rules). Worth doing both.

✅ **Expect:** a retry is scheduled, not a closure.

```sql
SELECT cc.reference_code, cc.stage, cc.status, cc.contact_attempt_count, cc.next_action_on
FROM care_case cc WHERE cc.id = <case id>;
```

`contact_attempt_count` should have risen and the case should still be open.

### Step 5 · A3 — Needs follow-up raises an escalation

Choose **Needs follow-up**, pick a reason, write ≥10 characters, submit.

✅ **Expect on screen:** confirmation that a team lead has been notified.

```sql
SELECT e.reference_code, e.tier, e.status, e.reason_code, e.description,
       cc.status AS case_status, cc.stage
FROM escalation e
JOIN care_case cc ON cc.id = e.care_case_id
WHERE e.care_interaction_id = (SELECT id FROM care_interaction WHERE public_id='<id>');
```

| Should be | |
|---|---|
| `escalation.tier` | `STANDARD` |
| `escalation.status` | `NEW` |
| **`case_status`** | **`ESCALATED`** — the case is paused |

⚠️ Try submitting with a description under 10 characters first — the client should refuse
before any request is sent.

### Step 6 · A4 — Crisis outranks everything

Choose **Crisis**, reason, description, submit.

✅ **Expect:** `escalation.tier = 'EMERGENCY'`, case paused.

This is priority 1 in the rules — it fires regardless of what intent was chosen, which is
the point. A crisis disclosed alongside "wants to connect" is still a crisis.

### Step 7 · A5 — Do not contact

Choose **Normal**, intent **Asked not to be contacted again**, submit.

✅ **Expect:** the case closes **and the person is flagged permanently.**

```sql
SELECT p.full_name, p.do_not_contact, cc.status, cc.close_reason
FROM person p JOIN care_case cc ON cc.person_id = p.id
WHERE cc.id = <case id>;
```

| Should be | |
|---|---|
| `person.do_not_contact` | **1** |
| `case.status` | `CLOSED` |

The flag is on the **person**, not the case. Opening a new case for them later must not
undo it — that is the whole reason it lives where it does.

### Step 8 · Concurrency and double-submit

Open the same contact in two tabs. Log it in the first, then submit the second.

✅ **Expect:** *"This contact has already been logged."* — not a second row, and not a
silent overwrite.

### Step 9 · Somebody else's contact

```bash
curl -X POST -H "Authorization: Bearer <volunteer token>" \
  -H "Content-Type: application/json" -d '{"outcomeCode":"SPOKE","rowVersion":1}' \
  http://localhost:5043/api/contacts/<another volunteer's interaction id>/log
```

✅ **Expect:** *"This contact is assigned to another volunteer."*

### Step 10 · Does it show up downstream?

Open the **team lead dashboard** (screen 4) as the lead of that volunteer's team.

✅ **Expect:** the A3 and A4 escalations in "Attention Needed", the case counts moved,
and the volunteer's load reflecting any closure.

---

## Cleanup

There is no clean automated undo — logging a contact deliberately changes case state,
raises escalations and moves counters, and unpicking that by hand risks leaving the data
half-consistent. **Re-run the migration instead:**

```bash
"/c/Program Files/MySQL/MySQL Server 9.1/bin/mysql.exe" -uroot < Database/Migration/001_mvp_to_v2.sql
"/c/Program Files/MySQL/MySQL Server 9.1/bin/mysql.exe" -uroot < Database/Migration/002_huddle_assessment.sql
```

That rebuilds `cms_api_db` from the read-only production backup and returns every count
to the step 1 baseline.

---

## Verified live while writing this

Against the migrated data on 2026-08-27, without mutating anything:

| Check | Result |
|---|---|
| `GET /api/contacts/mine` scoped by token | ✅ 7 due contacts returned for the signed-in volunteer |
| Stale `rowVersion` refused | ✅ *"This contact was already logged, or changed by someone else."* |
| Unknown outcome refused | ✅ *"Unknown outcome 'NONSENSE'."* |
| Due-vs-pending split | ✅ 7 due, 6 scheduled later, 13 total — the screen shows 7 |

The write paths (steps 3–7) were **not** run, because each one permanently changes case
state. Those are for your pass.

---

## What to record

Per step: pass / fail, the on-screen message, and the SQL result beside the expected one.

The two worth being most careful about are **step 2** (does the URL parameter change the
list — it must not) and **step 7** (is the do-not-contact flag on the person). Both are
places where the MVP got it wrong in ways that were invisible until they mattered.
