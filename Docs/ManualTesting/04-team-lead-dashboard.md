# 4 · Team lead dashboard

**Screen:** `wwwroot/templates/TeamLeads/TeamLeadDashboard.html`
**Scripts:** `wwwroot/templates/assets/js/teamlead.js`, `teamlead-shell.js`
**URL:** `http://localhost:5043/templates/TeamLeads/TeamLeadDashboard.html`

---

## What this screen does

This is the landing page for a team lead, and it exists to answer one question: **is
anybody being dropped?**

Screens 1–3 put work into the system. This one is where the work is watched. Nothing on
it is a vanity number — every figure is something a lead can act on this morning:
an unacknowledged escalation to answer, a case with no volunteer, a volunteer whose
completion rate has fallen two weeks running.

It is also **read-mostly**. The only thing on the page that writes is the team huddle
(`POST /api/huddle/verdicts`). Everything else is a view; the acting happens on the
screens this one links to. That makes it the safest screen in the system to test
repeatedly — you can reload it all day without changing case state.

### Three calls fill the page

The MVP dashboard made four calls (team metrics, huddle, nurture active, nurture
review) and **every one of them carried the team lead id as a parameter**. This one
makes three, and none of them carries an identifier at all.

| Card | Endpoint | Loaded by |
|---|---|---|
| Team Performance, Volunteers, Attention, Nurture, Escalations | `GET /api/dashboards/team-lead` | `loadDashboard()` |
| Check-ins | `GET /api/check-ins/due` | `loadCheckInsDue()` |
| Team Huddle | `GET /api/huddle` | `loadHuddleCard()` |

They are three calls rather than one on purpose: a slow or failing check-in query
should not blank the escalations sitting above it.

### Whose team it shows

**From the signed-in token, via `team.lead_user_id`.** Never from a parameter.

The MVP page read `?teamleadid=` from the query string and trusted it, which meant any
signed-in user — including a volunteer — could read any team lead's queue by editing the
address bar. That queue names real people and the crises disclosed about them. **Step 3
below is the test that this is actually fixed, and it is the most important step in
this document.**

`TLID` still exists in `teamlead.js`, but it is now populated *from the response*
(`teams[0].publicId`) rather than from the URL, so downstream handlers still have a team
id to work with and nothing is trusted on the way in.

### Leading no team is a legitimate state

A newly promoted lead, or one whose team was reassigned, gets a working page that says
*"You do not lead a team yet."* — not an error, and not a blank screen. An **Administrator
or Pastor** signing in also lands here with no team, because they pass the policy but
lead nothing. Every count is zero and every list is empty; the page must not break.

---

## Who can use it

| Endpoint | Policy | Roles |
|---|---|---|
| `GET /api/dashboards/team-lead` | `TeamLeadOrAbove` | TEAM_LEAD, PASTOR, ADMIN |
| `GET /api/check-ins/due` | `TeamLeadOrAbove` | same |
| `GET /api/huddle` | `TeamLeadOrAbove` | same |
| `POST /api/huddle/verdicts` | `TeamLeadOrAbove` | same |

A **VOLUNTEER** gets HTTP 403. The page turns that into
*"You do not have team lead access."* rather than *"Error loading metrics"* — being
signed in without the right role is a different problem from the server being down, and
should read differently.

Scoping is applied *on top of* the policy: a PASTOR passes `TeamLeadOrAbove` but sees
only the teams where `team.lead_user_id` is their own account. The policy decides who
may open the page; `GetLedTeamIdsAsync` decides what is on it.

---

## The six cards, and what each number means

### 📊 Team Performance

Six counts, all scoped to the caller's teams and all excluding closed cases.

| Row | Source | Means |
|---|---|---|
| Awaiting Assignment | `care_case.status = 'AWAITING_ASSIGNMENT'` | needs a volunteer |
| In Progress | `care_case.status = 'IN_PROGRESS'` | being worked |
| Escalated | `care_case.status = 'ESCALATED'` | paused pending the lead |
| Awaiting Review | `care_case.stage = 'REVIEW'` and not closed | needs the lead's sign-off |
| Due Today | `care_interaction.status = 'PENDING'` and `scheduled_on = today` | |
| Overdue | `PENDING` and `scheduled_on < today − grace` | |

> **"Overdue" is not "late by one day."** It uses the same grace period as the
> mark-overdue job — `app_setting.assignment.retry_delay_days`, default **3** — read at
> request time rather than hard-coded, so the page and the job can never disagree about
> which contacts are late. If you change that setting, both move together.

### 👥 Volunteers Metrics

One row per **ACTIVE** volunteer on the team, busiest first. Inactive volunteers are
excluded deliberately, and for two reasons: the card answers *"who can take another
one?"* and somebody on leave cannot; and the member count in the header counts the same
way, so a page reading "6 members" above 7 rows never happens.

| Column | Shows |
|---|---|
| Name | plus a red pill with their count of open escalations, when non-zero |
| Capacity \| Target Range | band label, then `current_case_load / max_per_week` |
| This Week | current case load |
| Trend | ↑ ↓ → or — , with last complete week's completion % |
| Flag | Healthy / Watch / At risk, plus **Full** when there is no spare capacity |

**The trend is three weeks of raw counts, not three percentages.** A week where nothing
was scheduled is not a 0% week — it is a week with no information, and the difference
matters. Treating "no cases given" as total failure would flag a volunteer for their
*team lead's* quiet week.

| `trend` | Meaning | Shown as |
|---|---|---|
| `UP` | last complete week better than the one before | green ↑ |
| `FLAT` | the same | grey → |
| `DOWN` | worse | red ↓ |
| `NONE` | one of the two weeks had **nothing scheduled** | grey — |

`healthFlag` is derived from two consecutive comparisons, not one:

| Flag | Condition | Badge |
|---|---|---|
| `GREEN` | steady or improving | Healthy |
| `AMBER` | one week down | Watch |
| `RED` | **two consecutive weeks down** | At risk |

One bad week is a bad week. Two in a row is the shape that precedes a volunteer quietly
dropping out — which is what the lead needs to catch while there is still time to ask.

> **The flag is health, not capacity.** They are different questions and the card has
> room for both: capacity is already two columns to the left, and *Full* rides along
> beside the health badge rather than replacing it.

"Week 1" is the **last complete week** — the Monday before this one. A part-finished
current week would show the entire team falling every Monday morning.

### ⚠️ Attention Needed

Derived on the client from counts already in the payload. Order is fixed, and it is the
order a lead should work in:

| # | Appears when | Priority shown |
|---|---|---|
| 1 | `escalations.unacknowledged > 0` | High |
| 2 | `cases.awaitingReview > 0` | Medium |
| 3 | `contacts.overdue > 0` | Medium |
| 4 | `cases.awaitingAssignment > 0` | Medium |

The escalation line also carries the age of the oldest unacknowledged one
(`oldest 32h`). A count alone hides the difference between one raised a minute ago and
one raised on Friday.

Nothing outstanding renders *"Nothing needs your attention."*

### 📋 Escalations & Check-ins

**Escalations Pending** lists up to **10**, and the ordering is load-bearing:

```
unacknowledged first  →  then tier EMERGENCY, URGENT, STANDARD  →  then oldest
```

The 🚨 blinking siren is reserved for what earns it: `tier = 'EMERGENCY'`, **or** a
reason whose `escalation_reason.requires_protocol = 1`. Everything else gets ⚠️. A siren
on every row is a siren nobody looks at.

Two routes put an escalation on this list — assigned directly to the lead
(`assigned_to_user_id`), **or** raised on a case belonging to a team they lead. The
second matters because the chase-up job falls back to "every team lead at the campus"
when an escalation has no assignee; without it, exactly the escalations most at risk of
being missed would be invisible on the page that exists to surface them.

**Check-ins** comes from the separate `/api/check-ins/due` call. A volunteer is due when:

- they have **never** had a check-in — these sort first, and read *"never checked in"*
  rather than a very large number of days; or
- `next_check_in_on` has arrived; or
- no next date was set and it has been **35 days** (`DefaultOverdueDays`) since the last.

A RED or AMBER emotional tone at the last check-in is badged on the row.

### 🌱 Nurture Sequences

**Hidden entirely when `nurture.active = 0`** — an empty card teaches the eye to skip
that part of the page.

| Badge | Counts |
|---|---|
| Active | cases with `stage = 'NURTURE'`, not closed |
| Overdue Steps | of those, `next_step_due_on < today` |
| Paused | of those, `status = 'ESCALATED'` |

A case paused by an escalation is **counted and marked, not hidden**. A lead looking at
"9 active" needs to know two of them are stuck behind a concern they have not yet
resolved.

Each row shows a progress bar (`current_step_number / plan step count`), the volunteer,
and a badge — `paused` in red, or `Nd late` in amber. Up to 12 rows, most overdue first.
The button reads **"See all in the pipeline"** and goes to
`Pipeline.html?stage=NURTURE`; the card is a summary, not a second list view.

### 🤝 Team Huddle

The weekly meeting where the lead reviews the team's contacts and records, for each,
whether the volunteer's escalation judgement was right.

**This is the only thing in the system that can catch an *under*-escalation** — a
concern that was heard and never passed on. The chase-up job cannot: it only chases
escalations that were actually raised.

The card is shown when `isHuddleDay`, **or** there is anything to review, **or** there
is a backlog. A huddle that was missed last week is precisely when the card needs to be
visible. Otherwise it stays hidden.

| Setting | Default | Effect |
|---|---|---|
| `huddle.day_of_week` | `6` (Saturday) | 1 = Monday … 7 = Sunday |
| `huddle.lookback_days` | `7` | how far back the agenda reaches |

> The app's convention is 1 = Monday; .NET's `DayOfWeek` is 0 = Sunday. The conversion
> happens in `HuddleService.IsHuddleDay`, so the *setting* stays readable to whoever
> opens the settings screen.

Two things changed from the MVP, both because the MVP version recorded **zero verdicts
in three months of production**:

1. **The window is real.** The old modal was titled "for this Week" but the week filter
   was commented out of the SQL, so it returned every unassessed contact ever — an
   unpaginated wall that only grew.
2. **Verdicts save as a batch.** The old screen wanted a click on Update and a
   confirmation dialog *per row*.

---

## Tables read, and the one thing written

**Read** by `GET /api/dashboards/team-lead`:

`team`, `campus`, `volunteer`, `person`, `capacity_band`, `care_case`,
`care_interaction`, `escalation`, `escalation_reason`, `nurture_plan_step`,
`app_setting`

**Read** by the other two: `volunteer_check_in`, `care_outcome`, `visitor_intent`.

**Written** — only by the huddle:

| Table | Column | When |
|---|---|---|
| `care_interaction` | `escalation_assessment`, `assessment_note`, `assessed_by`, `assessed_at` | Save verdicts |

Nothing else on this screen writes anything. If a test run changes case counts, the
change came from another screen, not this one.

---

## Before you start

**Run the API:**

```bash
cd C:/Users/LENOVO/source/repos/CMS && dotnet run
```

### Getting signed in as a team lead

Every migrated team lead has `must_change_password = 1`. A token issued to such an
account is **rejected by every endpoint except the password-change one** — you get
HTTP 403 `password_change_required`, *not* a working dashboard. Clear the flag first:

```sql
UPDATE user_account SET must_change_password = 0 WHERE username = '<their mobile>';
```

The four migrated leads:

| Username | Name | Team | Leads |
|---|---|---|---|
| `9703006373` | Vijaya O | Vijaya O Team | team 1 |
| `8297277478` | Rancy A | Rancy A Team | team 2 |
| `6301140238` | Lakshmi Naga | Lakshmi Naga Team | team 3 |
| `9859939859` | Prisk G | Prisk G Team | team 4 |

> ⚠️ **Not every migrated account shares the admin password.** `9859939859` has a
> different `password_hash` from the rest and does not accept `Xq7#vTrb92!mKp`. The
> other three do. If you want to sign in as Prisk G — the only lead with an open
> escalation — reset their hash from a known account rather than guessing, and note
> that **five failed logins in five minutes trips the rate limiter** (`PermitLimit = 5`,
> `Window = 5 min`) and you will get HTTP 429 for the rest of the window.

**Put the flag back when you are done:**

```sql
UPDATE user_account SET must_change_password = 1 WHERE username = '<their mobile>';
```

### What the migrated data already gives you

You do not need to invent much — the baseline covers most of the interesting shapes:

| Shape | Where it already exists |
|---|---|
| Team with an unacknowledged escalation | **team 4** (Prisk G) — `ESC0015`, STANDARD, raised 2026-07-24 |
| Cases awaiting review | **team 4** — 2 |
| Live nurture with overdue steps | **team 1** — 11 active, 10 overdue |
| Overdue follow-ups | **team 1** — 23; **team 4** — 20 |
| **Volunteers over capacity** | **team 1** — four at 3/2; **team 4** — Ananthalakshmi at 4/2 and three at 3/2 |
| Huddle backlog with an empty current week | **team 1** — 0 items this week, **90** older unassessed |
| Everyone due a check-in | all teams — last check-in 2026-05-20, 99 days ago |
| Leads no team | the `9999999999` administrator |
| An AMBER volunteer | **team 1** — Sujeetha M and Hepsiba B, both `DOWN` |

The over-capacity rows are the valuable ones and they are **not** hypothetical — see
step 6. What the baseline does **not** contain is a volunteer with `trend = 'NONE'` or a
`RED` health flag; both have to be manufactured (step 7).

---

## Test steps

### Step 1 · Baseline

Pick a lead and record what the database says before opening anything. For Vijaya O
(team 1):

```sql
SET @team := 1, @lead := 2;

SELECT SUM(status='AWAITING_ASSIGNMENT') AwaitingAssignment,
       SUM(status='IN_PROGRESS')         InProgress,
       SUM(status='ESCALATED')           Escalated,
       SUM(stage='REVIEW' AND status<>'CLOSED') AwaitingReview
FROM care_case WHERE team_id = @team AND status <> 'CLOSED';

SELECT SUM(ci.status='PENDING' AND ci.scheduled_on = UTC_DATE()) DueToday,
       SUM(ci.status='PENDING' AND ci.scheduled_on <
           DATE_SUB(UTC_DATE(), INTERVAL 3 DAY))                 Overdue
FROM care_interaction ci
JOIN care_case cc ON cc.id = ci.care_case_id
WHERE cc.team_id = @team;

SELECT COUNT(*) OpenEsc, SUM(e.acknowledged_at IS NULL) Unacknowledged
FROM escalation e JOIN care_case cc ON cc.id = e.care_case_id
WHERE (e.assigned_to_user_id = @lead OR cc.team_id = @team)
  AND e.status NOT IN ('RESOLVED','CLOSED','REFERRED_OUT');

SELECT COUNT(*) NurtureActive,
       SUM(next_step_due_on IS NOT NULL AND next_step_due_on < UTC_DATE()) NurtureOverdue,
       SUM(status = 'ESCALATED') NurturePaused
FROM care_case WHERE team_id = @team AND stage='NURTURE' AND status <> 'CLOSED';
```

Open the dashboard.

✅ **Expect:** every figure on the page matches, the header reads
`TEAM LEAD DASHBOARD - Vijaya O Team`, and the subtitle reads
`<campus> · <n> of 10 members`.

⚠️ **If Overdue disagrees**, check `assignment.retry_delay_days` before calling it a
bug — the SQL above hard-codes 3 days and the page reads the live setting.

### Step 2 · The summary line leads with the worst thing

`res.message` is what a phone notification would show, so it must not just say "OK".
The priority is fixed:

| Condition | Message |
|---|---|
| any unacknowledged escalation | `N escalation(s) need acknowledging.` |
| else any awaiting review | `N case(s) awaiting your review.` |
| else any overdue contact | `N overdue follow-up(s).` |
| else, no team | `You do not lead a team yet.` |
| else | `Nothing needs your attention right now.` |

```bash
curl -s -H "Authorization: Bearer <lead token>" \
  http://localhost:5043/api/dashboards/team-lead | head -c 120
```

✅ **Expect:** the line matching the *highest* condition that is true — not the last one.

### Step 3 · The URL parameter must not change anything ⭐

**This is the step that matters most.** Signed in as one lead, ask for another's:

```bash
curl -s -H "Authorization: Bearer <Vijaya O token>" \
  "http://localhost:5043/api/dashboards/team-lead?teamleadid=4&teamId=4&leadUserId=5"
```

✅ **Expect:** *Vijaya O Team* and its figures, byte-for-byte identical to the call with
no query string. Not team 4's. Not an error — the parameter is simply not read.

Then the same in the browser: append `?teamleadid=4` to the dashboard URL and reload.

✅ **Expect:** the same page as before. If the heading changes to another team's name,
**stop and record it** — that is the MVP vulnerability, and it exposes named individuals
and the crises disclosed about them.

### Step 4 · A volunteer cannot open it

Sign in as a volunteer and load the dashboard URL.

✅ **Expect:** *"You do not have team lead access."* in the alerts strip — not
*"Error loading metrics"*, and not a partly-rendered page.

```bash
curl -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer <volunteer token>" \
  http://localhost:5043/api/dashboards/team-lead
```

✅ **Expect:** `403`.

### Step 5 · A lead with no team

Sign in as `9999999999` (administrator — passes the policy, leads nothing).

✅ **Expect:** the page loads. Heading `TEAM LEAD DASHBOARD` with no team name,
subtitle *"No team assigned"*, every Team Performance figure `0`, Attention Needed
reads *"Nothing needs your attention."*, the volunteers table reads *"No volunteers on
this team."*, and the nurture and huddle cards are **hidden**. Message:
*"You do not lead a team yet."*

❌ **Fail if** anything throws, or a card renders with `undefined` / `NaN` in it.

### Step 6 · An over-capacity volunteer does not take the page down ⭐

You do not have to manufacture this. **Team 4** has Ananthalakshmi at 4/2 and three
others at 3/2; **team 1** has four of its six at 3/2. Whichever lead you signed in as,
you are probably already looking at it.

Both `capacity_band.max_per_week` and `volunteer.current_case_load` are **UNSIGNED**. The
volunteer query orders by their difference, and an unsigned subtraction that goes
negative makes MySQL raise *"BIGINT UNSIGNED value is out of range"* — which 500s the
**whole dashboard**, not just that row. The `CAST(... AS SIGNED)` in the ORDER BY is what
prevents it, and it is load-bearing.

Sign in as the lead of team 4 and load the page. Or manufacture it on any team:

```sql
UPDATE volunteer SET current_case_load = 99 WHERE id = <a volunteer on your team>;
```

✅ **Expect:** HTTP 200, the page renders, and that volunteer sorts **first** (busiest
first) showing `99 / 2` with a **Full** badge.

❌ **Fail if** the page shows *"Error loading metrics"* or the API returns 500. Note that
over capacity is not a hypothetical: a team gets there through manual assignment or
through a band being lowered, and the lead looking at that team is exactly who needs the
page to load.

```sql
-- put it back
UPDATE volunteer SET current_case_load = <original> WHERE id = <id>;
```

### Step 7 · Trend and flag say what they mean

The migrated data has no `NONE` and no `RED`, so this one has to be made. To get a
`NONE`, clear a volunteer's scheduled contacts from one of the two comparison weeks:

```sql
-- week 2 relative to the Monday of this week
UPDATE care_interaction SET scheduled_on = DATE_SUB(scheduled_on, INTERVAL 60 DAY)
WHERE volunteer_id = <id>
  AND scheduled_on >= DATE_SUB(<this Monday>, INTERVAL 14 DAY)
  AND scheduled_on <  DATE_SUB(<this Monday>, INTERVAL  7 DAY);
```

Then find that volunteer in the payload — one of the two comparison weeks now has
nothing scheduled.

✅ **Expect:** a grey dash and **no** percentage, and the flag reads **Healthy**, not
*At risk*. A volunteer must never be flagged for a week in which they were given no work.

To force a RED, give one volunteer two consecutive falling weeks of scheduled contacts.
The three windows, relative to the Monday of the current week:

| Week | Range |
|---|---|
| 1 | Monday−7 … Monday−1 |
| 2 | Monday−14 … Monday−8 |
| 3 | Monday−21 … Monday−15 |

"Done" is `care_interaction.status = 'COMPLETED'` **or** an interaction that raised an
escalation — both are the volunteer doing their job with what they heard. `PENDING` and
`MISSED` sit in the denominator only.

✅ **Expect:** ↓ red arrow and an **At risk** badge only after **two** consecutive falls;
one fall is **Watch**.

### Step 8 · Escalation ordering and the siren

On a team with several open escalations:

```sql
SELECT e.reference_code, e.tier, e.status, e.raised_at, e.acknowledged_at,
       er.requires_protocol
FROM escalation e
JOIN care_case cc ON cc.id = e.care_case_id
JOIN escalation_reason er ON er.code = e.reason_code
WHERE cc.team_id = <team> AND e.status NOT IN ('RESOLVED','CLOSED','REFERRED_OUT')
ORDER BY (e.acknowledged_at IS NULL) DESC,
         FIELD(e.tier,'EMERGENCY','URGENT','STANDARD'), e.raised_at;
```

✅ **Expect:** the card lists them in exactly that order, at most 10, each with its age
(`32h`, or `3d` past 24 hours).

✅ **Expect:** 🚨 only on EMERGENCY tier or `requires_protocol = 1`; ⚠️ on the rest.

### Step 9 · Clicking an escalation navigates — it does not open a modal ⭐

Click any row under Escalations Pending.

✅ **Expect:** the browser **navigates** to `Escalations.html?id=<public id>`.

❌ **Fail if** a modal opens showing *"refused to connect"*. The application sends
`X-Frame-Options: DENY` and CSP `frame-ancestors 'none'`, so the browser refuses any
page loaded into `#escIframe`. Those headers are deliberate clickjacking defence and
stay; the modal was the wrong mechanism. This was true of *every* modal on the old page,
which meant clicking an escalation — the team lead's main job — did nothing but show an
error.

Same check for a check-in row (→ `CheckIns.html?id=…`) and a volunteer name in the
metrics table (→ `../Volunteers/Assignments.html?volunteerid=…`).

### Step 10 · Check-ins due

```sql
SELECT p.full_name, v.last_check_in_on, v.next_check_in_on,
       DATEDIFF(UTC_DATE(), v.last_check_in_on) AS days_since
FROM volunteer v JOIN person p ON p.id = v.person_id
WHERE v.team_id = <team> AND v.status = 'ACTIVE' AND p.deleted_at IS NULL
  AND (v.last_check_in_on IS NULL
       OR (v.next_check_in_on IS NOT NULL AND v.next_check_in_on <= UTC_DATE())
       OR (v.next_check_in_on IS NULL AND DATEDIFF(UTC_DATE(), v.last_check_in_on) >= 35))
ORDER BY (v.last_check_in_on IS NOT NULL), v.last_check_in_on, p.full_name;
```

✅ **Expect:** the same people in the same order, capped at **8** on screen. Anyone with
no check-in at all sorts first and reads *"never checked in"* — never *"12345 days ago"*.

Then break it on purpose — stop the API mid-load, or point the check-in call at a dead
route.

✅ **Expect:** *"Check-ins could not be loaded."* under the Check-ins heading, and the
**escalations above it still rendered**. The two are separate calls precisely so one
cannot blank the other.

### Step 11 · The nurture card hides itself

```sql
SELECT COUNT(*) FROM care_case
WHERE team_id = <team> AND stage = 'NURTURE' AND status <> 'CLOSED';
```

✅ **Expect:** `0` → the card is **not on the page at all**. Non-zero → the card is
visible, badges match, and rows are ordered most-overdue-first with a progress bar per
person.

✅ **Expect:** a case with `status = 'ESCALATED'` appears with a red **paused** badge and
is counted in both *Active* and *Paused* — not hidden from the list.

✅ **Expect:** the button reads **"See all in the pipeline"** and goes to
`Pipeline.html?stage=NURTURE`.

### Step 12 · Huddle card visibility

Today is a huddle day when `today (1=Mon…7=Sun) == huddle.day_of_week` (default 6,
Saturday).

| Situation | Card |
|---|---|
| Huddle day | shown |
| Not huddle day, items waiting | shown |
| Not huddle day, `olderUnassessedCount > 0` | shown |
| Not huddle day, nothing waiting, no backlog | **hidden** |

Force it either way:

```sql
UPDATE app_setting SET setting_value = <today's ISO day> WHERE setting_key = 'huddle.day_of_week';
```

✅ **Expect:** when there is a backlog the button reads
`Team Huddle (N to review)`; otherwise just `Team Huddle`.

Put the setting back to `6` afterwards.

### Step 13 · The huddle window is real

Open the modal.

✅ **Expect:** the title reads `Team Huddle — <from> to <to>`, a window of
`huddle.lookback_days` (default 7), **not** every unassessed contact ever recorded.

✅ **Expect:** when nothing falls in the window but older items exist, the body reads
*"Nothing from this week. N older contact(s) are still unassessed."* and the footer
carries the same backlog count.

✅ **Expect:** a contact that already raised an escalation is badged **escalated** — that
is the question being judged, and the lead should not have to remember.

### Step 14 · Verdicts save as a batch, and a miscalibration needs a note ⚠️ writes

*The only writing step in this document.*

In the modal, set several dropdowns at once.

**a.** Choose **Correct** on one row and **Save verdicts**.

✅ **Expect:** the note box never appears, and a success toast reading `N recorded.`

**b.** Choose **Under-escalated** and save without typing a note.

✅ **Expect:** the note box appears as soon as the dropdown changes, and saving is
refused client-side: *"1 verdict(s) need a note before they can be saved."* — nothing is
sent.

Bypass the client and confirm the server refuses it too:

```bash
curl -s -X POST -H "Authorization: Bearer <lead token>" -H "Content-Type: application/json" \
  -d '{"verdicts":[{"interactionId":"<id>","assessment":"UNDER_ESCALATED","note":null}]}' \
  http://localhost:5043/api/huddle/verdicts
```

✅ **Expect:** a warning — *"1 verdict(s) mark a miscalibration without a note. Say
briefly what should have happened — it is what the volunteer will be shown."*

**c.** Leave a row on *Not assessed* and save.

✅ **Expect:** it is simply not sent — no row, no null verdict.

**d.** Send a nonsense assessment.

✅ **Expect:** *"Unknown assessment 'NONSENSE'."*

**e.** Send an `interactionId` belonging to **another lead's team**.

✅ **Expect:** it is rejected server-side and the message says so —
*"N recorded, M rejected — those contacts are not on your teams."* The count of rejected
rows must be non-zero.

**f.** Save one genuine `UNDER_ESCALATED` with a note.

✅ **Expect:** message *"N recorded, including 1 under-escalation(s) to raise at their
check-in."*, a `LogWarning` line in the API console, and the row persisted:

```sql
SELECT escalation_assessment, assessment_note, assessed_by, assessed_at
FROM care_interaction WHERE public_id = '<interaction>';
```

✅ **Expect:** after saving, the modal reloads **and** the dashboard behind it reloads —
the huddle can change what is on the page under it.

### Step 15 · The shared header

Every team lead screen renders its header from `teamlead-shell.js`.

✅ **Expect:** *My team* (current, not a link), *Assign cases*, *People*, and *Sign out*.

✅ **Expect:** **no "Add volunteer" button.** Enrolling a volunteer creates an account
that can read other people's pastoral records; that is a pastor's or administrator's
decision, not something a team lead grants themselves. Its absence is the feature.

✅ **Expect:** *Sign out* clears the tokens **and the refresh cookie** via
`RmAuth.logout()` — not a plain link to the login page, which left the session live on
the server. Confirm by pressing Back after signing out: the dashboard must not render
its data.

### Step 16 · A stale tab is obvious

`generatedAt` is in the payload for exactly this reason. Leave the tab open, change
something from another screen, and reload.

✅ **Expect:** the numbers move and `generatedAt` advances.

---

## ⚠️ Known — not new bugs

| Thing | Detail |
|---|---|
| **Dead nurture-modal code** | The bottom of `teamlead.js` still carries `loadNurtureData()`, `populateNurtureModal()` and `openCloseSeq()`, which call `/api/nurture/teamlead/{id}/active`, `/review` and `/nurture/sequence/close`. **Those routes do not exist.** `loadNurtureData` only fires on a `teamLeadLoaded` event that nothing dispatches, so it never runs. The live nurture card is `renderNurture()`, fed from the dashboard payload. |
| **`#confirmCloseSeqBtn` is live** | Its click handler is bound unconditionally and does `POST /api/nurture/sequence/close`. The button sits in `#closeSequenceModal`, which is now only reachable through the dead path above — so in practice it cannot be clicked. If you find a way to open that modal, expect a 404. |
| **`#dashboardSubtitle` no longer exists** | `teamlead.js` still writes to it; the header owns the subtitle now and gets it via `TeamLeadShell.setSubtitle()`. The stray write is a no-op. |
| **Bootstrap and jQuery load from CDN** | Offline, the modals will not open. |
| **`9859939859` password** | Different hash from the other migrated accounts; `Xq7#vTrb92!mKp` does not work. |

---

## Cleanup

Every step reads only, except the three that force a case on purpose — **7** (moves
scheduled dates), **12** (changes a setting), **6** (changes a case load) — and **14**,
which is the one real write path on the screen.

**Step 14.** Verdicts are an assessment record, not case state, so they undo cleanly:

```sql
UPDATE care_interaction
SET escalation_assessment = NULL, assessment_note = NULL,
    assessed_by = NULL, assessed_at = NULL
WHERE assessed_at >= '<when you started>';
```

Anything you changed to force a case:

```sql
UPDATE app_setting SET setting_value = '6' WHERE setting_key = 'huddle.day_of_week';
UPDATE volunteer SET current_case_load = <original> WHERE id = <id>;
UPDATE user_account SET must_change_password = 1 WHERE username = '<their mobile>';

-- step 7, if you moved scheduled dates
UPDATE care_interaction SET scheduled_on = DATE_ADD(scheduled_on, INTERVAL 60 DAY)
WHERE volunteer_id = <id> AND scheduled_on < DATE_SUB(UTC_DATE(), INTERVAL 60 DAY);
```

Put the `must_change_password` flag back even if you change nothing else — leaving it
cleared is a live account on the migrated production password.

Or go back to the migrated baseline entirely:

```bash
"/c/Program Files/MySQL/MySQL Server 9.1/bin/mysql.exe" -uroot < Database/Migration/001_mvp_to_v2.sql
"/c/Program Files/MySQL/MySQL Server 9.1/bin/mysql.exe" -uroot < Database/Migration/002_huddle_assessment.sql
```

---

## Verified live while writing this

Against the migrated data on 2026-08-27, signed in as **Vijaya O** (team 1) and as the
administrator. Read-only throughout — the only database change was clearing
`must_change_password` to sign in, which was put back afterwards.

| Step | Check | Result |
|---|---|---|
| 1 | Every Team Performance figure against the SQL | ✅ cases `0 / 14 / 0 / 0`, contacts `2 due · 23 overdue · 0 missed`, nurture `11 active · 10 overdue · 0 paused`, escalations `0 / 0` — all exact |
| 1 | Header and subtitle | ✅ `Vijaya O Team`, 6 of 10 members |
| 2 | Summary line priority | ✅ *"23 overdue follow-up(s)."* — correctly skipped the two higher conditions, both zero |
| 3 | **`?teamleadid=4&teamId=4&leadUserId=5`** | ✅ **byte-for-byte identical** to the clean call (`generatedAt` aside); still `Vijaya O Team` |
| 4 | Volunteer token on all three endpoints | ✅ `403` on `/dashboards/team-lead`, `/check-ins/due` and `/huddle` |
| 5 | ADMIN, who leads no team | ✅ 200, all zeroes, empty lists, *"You do not lead a team yet."* on all three calls |
| 6 | **Over-capacity volunteers** | ✅ 200 — four of team 1's six are at `3 / 2` and the page renders; no unsigned-underflow 500 |
| 7 | Trend and flag | ✅ `FLAT`/`GREEN` and `DOWN`/`AMBER` both present; no `RED`, and no `NONE`, in this data |
| 10 | Check-ins due | ✅ 6 volunteers, *"6 due a check-in."*, 99 days since the last one each |
| 12/13 | Huddle window and backlog | ✅ `isHuddleDay: false`, `huddleDayOfWeek: 6`, window 21–28 Aug, **0 items this week and 90 older unassessed** — so the card shows on a non-huddle day, which is the backlog rule working |
| — | A `must_change_password` token against the dashboard | ✅ `403 password_change_required` — a token *is* issued, and it does not work |
| — | Login rate limit | ✅ HTTP 429 after 5 attempts in 5 minutes |
| — | Migrated leads share the admin password | ❌ **not all of them** — `9859939859` has a different hash |

Steps 8, 9, 11, 14, 15 and 16 need a browser or the write path and are for your pass.
Step 14 is the only one that writes.

---

## What to record

Per step: pass / fail, the on-screen text, and the SQL result beside the expected one.

The three marked ⭐ are the ones to be most careful about:

- **Step 3** — does `?teamleadid=` change what you see? It must not. This is the MVP
  vulnerability, and it exposed named individuals and the crises disclosed about them.
- **Step 6** — does an over-capacity volunteer take the whole page down? Real rows in
  the baseline data already have this shape.
- **Step 9** — does clicking an escalation actually navigate? A team lead who cannot
  open an escalation cannot do the one job this screen exists for.
