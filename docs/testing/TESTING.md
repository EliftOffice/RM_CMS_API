# RM_CMS — Testing Guide

How to exercise everything that is actually built, as of **2026-08-15** (branch
`feature/auth_security`). Companion document: [SYSTEM_WORKFLOW.md](../architecture/SYSTEM_WORKFLOW.md).

> **Since this was written (as of 2026-09-07):** the scope table in §0 and §11 "What you
> cannot test yet" are now out of date on two points — the Pastor dashboard is built and
> verified (`GET /api/dashboards/pastor`, `wwwroot/templates/Pastor/Dashboard.html`), and
> the row listing Followups/Escalations(legacy)/Nurture/TeamLeads/CheckIn/Pastors/Users/
> SystemConfig/CornJobs/Telegram as "⛔ broken" is moot — that entire old layer has since
> been deleted outright rather than left broken-but-registered. "16 of 17 frontend pages
> dead" is also no longer accurate; most pages listed as untestable in §11 now work
> (Escalations, Check-ins, Pipeline, Manual Assignment, Teams admin). Everything else
> below — the auth/authorization matrices, the Care/Jobs/Notifications test procedures —
> is unaffected and still the right reference.

---

## 0. Read this first — scope

The application is **mid-rewrite**. Six modules are on the new schema; the rest of the
old code is still registered and queries tables that no longer exist.

| Module | State | Testable |
|---|---|---|
| **Identity** — `api/auth/*`, `api/admin/accounts/*` | ✅ migrated | Yes |
| **People** — `api/people/*` | ✅ migrated | Yes |
| **Volunteers** — `api/volunteers/*`, `api/teams/*` | ✅ migrated | Yes |
| **Care** — `api/cases/*`, `api/contacts/*`, `api/escalations/*` | ✅ migrated | Yes |
| **Jobs** — `api/jobs/*` | ✅ migrated | Yes — verified end-to-end |
| **Notifications** | ⚠️ queue only | Partly — rows are queued, **nothing sends them** |
| Followups, Escalations (legacy), Nurture, TeamLeads, CheckIn, Pastors, Users, SystemConfig, CornJobs, Telegram | ⛔ broken | **No** — they query dropped tables and fail at runtime |
| Frontend pages | ⛔ 16 of 17 dead | Only `Admin/accounts.html` and login work |

> **A green build proves nothing here.** The project compiles with most of the
> application non-functional. Only a live run against the database tells you anything.

---

## 1. Setup

### Database
`cms_api_db` on `localhost:3306` is already the new 33-table schema with test data.

```bash
"/c/Program Files/MySQL/MySQL Server 9.1/bin/mysql.exe" -u root -D cms_api_db -e "SHOW TABLES;"
```

To rebuild from scratch (destroys data — the script is **not** idempotent and must run
against an empty database):

```bash
mysql -u root -e "DROP DATABASE IF EXISTS cms_api_db; CREATE DATABASE cms_api_db;"
mysql -u root --database=cms_api_db < Database/Schema/schema.sql
```

### Run the API

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5043" dotnet run --no-launch-profile
```

Swagger is at `http://localhost:5043/swagger` (Development only).

**Smoke test — the app is up:**
```bash
curl -s http://localhost:5043/health
```
Expect `{"status":"ok"}`.

### Conventions you need to know before writing assertions

| Thing | Behaviour |
|---|---|
| Response envelope | `{"responseType":N,"message":"...","data":{...}}` |
| `responseType` | `0` Success · `1` Warning · `2` Error — **an integer, not a string** |
| Casing | **camelCase.** Never "fix" this; every page reads `res.data` |
| Business failures | HTTP **200** with `responseType` 1 or 2 — not a 4xx |
| **Authentication failures** | HTTP **401** *and* `responseType: 2` — the exception to the row above. Verified 2026-08-15 |
| Validation failures | HTTP **400** with RFC 7807 ProblemDetails + `correlationId` |
| Ids in URLs | 26-character **ULID** `public_id`. Never the integer key |
| Writes | Require the current `rowVersion`; a stale one is a **conflict**, not a no-op |
| Auth default | **Deny.** Every endpoint needs a token unless explicitly anonymous |

---

## 2. Authentication

Everything except `/health`, `/`, and `api/auth/login|refresh|logout` requires a bearer token.

### 2.1 Log in as the bootstrap administrator

The account is created at startup by `IIdentityBootstrapper`. Local dev credentials come
from `appsettings.Development.json` (throwaway values, localhost only):

> ### ⚠️ The bootstrap password is create-only
> `EnsureAdministratorAsync` **returns immediately if an active ADMIN already exists.**
> It never updates the password of an account it did not just create.
>
> So editing `Auth:Bootstrap:Password` after the first run changes **nothing** — the
> stored hash still matches whatever the password was on the day the account was created,
> and login fails with credentials that look correct in config. This is correct behaviour
> (a config file that silently resets an admin password on every boot would be far worse),
> but it is a guaranteed source of confusion.
>
> **If login fails with the documented credentials**, check whether the config value
> post-dates the account:
> ```sql
> SELECT id, username, is_active, failed_access_count, lockout_ends_at, created_at
> FROM user_account WHERE normalized_username = '9999999999';
> ```
> To recover, reset it through the real code path — sign in as any other admin and call
> `POST /api/admin/accounts/{id}/password`. If no admin is reachable at all, point
> `Auth:Bootstrap:Username` at an unused username, restart to mint a fresh admin, then
> reset the original from there and remove the temporary account.

```bash
curl -s -X POST http://localhost:5043/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"9999999999","password":"Xq7#vTrb92!mKp","deviceLabel":"test-cli"}'
```

**Expect:** `200`, `responseType: 0`, and `data.accessToken`, `data.expiresInSeconds` (900),
`data.account.roles` containing `ADMIN`.
**Also assert:** a `Set-Cookie` header carrying the refresh token with `HttpOnly`, `Secure`,
`SameSite=Strict`. The refresh token must **not** appear in the JSON body for browser clients.

Capture the token for later steps:

```bash
TOKEN=$(curl -s -X POST http://localhost:5043/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"9999999999","password":"Xq7#vTrb92!mKp"}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['data']['accessToken'])")
AUTH="Authorization: Bearer $TOKEN"
```

### 2.2 Auth test matrix

| # | Test | Request | Expect |
|---|---|---|---|
| A1 | No token is denied | `GET /api/people` with no header | `401` + ProblemDetails |
| A2 | Garbage token is denied | `Authorization: Bearer abc` | `401` |
| A3 | Reason is never disclosed | compare A1 and A2 bodies | **Identical**. "expired" vs "invalid signature" must not leak |
| A4 | Valid token works | `GET /api/auth/me` with `$AUTH` | `200`, your account |
| A5 | Wrong password | login with `"password":"wrong"` | `401`, `responseType:2` ✅ verified |
| A5b | **No user enumeration** | login as an unknown username vs. a real one with a bad password | Responses must be **byte-identical**: `{"responseType":2,"message":"Invalid username or password.","data":null}` ✅ verified |
| A6 | Lockout / backoff | 6 wrong logins in a row | `429` from the rate limiter (5 per 5 min per IP) |
| A7 | Sessions listed | `GET /api/auth/sessions` | `200`, one entry per device, `isCurrent` true for yours |
| A8 | Refresh rotates | `POST /api/auth/refresh` (cookie sent) | new `accessToken`; the **old refresh token is now dead** |
| A9 | **Reuse detection** | replay a refresh token you already used | `401` **and the whole token family is revoked** — all sessions die |
| A10 | Logout | `POST /api/auth/logout` | `200`; the refresh cookie is cleared |
| A11 | Logout everywhere | `POST /api/auth/logout-all` | all sessions invalid |
| A12 | Revocation is immediate | disable the account, then call any endpoint | `401` **within `Auth:SecurityStampCache:TtlSeconds`** (dev = `0`, so instantly) |

A9 and A12 are the two that matter most — they are the properties that make short-lived
tokens safe. Test them explicitly.

### 2.3 Password change

```bash
curl -s -X POST http://localhost:5043/api/auth/change-password -H "$AUTH" \
  -H "Content-Type: application/json" \
  -d '{"currentPassword":"OLD","newPassword":"NewPassphrase123!","confirmPassword":"NewPassphrase123!"}'
```

| Test | Expect |
|---|---|
| Mismatched confirmation | `400` — `[Compare]` fails at model binding |
| Shorter than 12 chars | `400` |
| Reuse of a recent password | `200` `responseType:2` — blocked by `password_history` |
| Wrong current password | `200` `responseType:2` |
| Success | `200`; **all other sessions are revoked**; the current one keeps working |
| `mustChangePassword` account | every non-auth endpoint returns a redirect/deny until changed (`PasswordChangeRequiredMiddleware`) |

---

## 3. Authorization

Five roles: `ADMIN`, `PASTOR`, `TEAM_LEAD`, `VOLUNTEER`, `DATA_ENTRY`.

**The single most important test:** create one account per role, then call every endpoint
with each. The expected matrix:

| Endpoint | ADMIN | PASTOR | TEAM_LEAD | VOLUNTEER | DATA_ENTRY |
|---|---|---|---|---|---|
| `GET /api/people` | ✅ | ✅ | ✅ | ✅ | ❌ |
| `POST /api/people` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `DELETE /api/people/{id}` | ✅ | ❌ | ❌ | ❌ | ❌ |
| `PUT /api/people/{id}/lifecycle` | ✅ | ✅ | ✅ | ❌ | ❌ |
| `GET /api/volunteers` | ✅ | ✅ | ✅ | ✅ | ❌ |
| `POST /api/volunteers` | ✅ | ✅ | ✅ | ❌ | ❌ |
| `PUT /api/volunteers/{id}/safeguarding` | ✅ | ❌ | ❌ | ❌ | ❌ |
| `POST /api/cases` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `PUT /api/cases/{id}/assign` | ✅ | ✅ | ✅ | ❌ | ❌ |
| `POST /api/cases/{id}/review` | ✅ | ✅ | ✅ | ❌ | ❌ |
| `GET /api/escalations` | ✅ | ✅ | ✅ | ❌ | ❌ |
| `POST /api/admin/accounts` | ✅ | ❌ | ❌ | ❌ | ❌ |
| `POST /api/jobs/*` | ✅ | ❌ | ❌ | ❌ | ❌ |

❌ must be **`403`** (authenticated but not permitted), not `401`.

### 3.1 Campus scoping — the object-level check

Role checks alone are not enough. `ICurrentIdentity.CanAccessCampus()` is enforced inside
`PeopleService`, `VolunteerService` and `CareService`.

| Test | Setup | Expect |
|---|---|---|
| Cross-campus read is denied | Give a TEAM_LEAD a role grant scoped to campus A, request a case at campus B by id | not found / denied — **never** the record |
| Org-wide grant sees all | Same account with `campusId: null` | both campuses visible |
| Volunteer acting for another | A volunteer logs a contact on an interaction owned by a different volunteer | denied (`CanActForVolunteer`) |

> **Known limitation, by design.** Campus is the enforced boundary; per-volunteer
> ownership is only checked when logging an interaction. Two volunteers at the same campus
> can otherwise see each other's cases. Confirm this is intended before calling it a bug —
> see `Database/Schema/CONVENTIONS.md`.

### 3.2 Default-deny regression guard

```bash
# Every controller action must refuse an anonymous caller.
for p in /api/people /api/volunteers /api/teams /api/cases /api/escalations \
         /api/contacts/mine /api/admin/accounts /api/jobs/history; do
  printf "%-28s %s\n" "$p" "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5043$p)"
done
```
**Every line must print `401`.** This is the guard on `FallbackPolicy` — if someone
removes it, a forgotten `[Authorize]` silently exposes an endpoint and this test is what
catches it.

---

## 4. People

Reference data for payloads: `GET /api/people-reference`.

### 4.1 Create

```bash
curl -s -X POST http://localhost:5043/api/people -H "$AUTH" \
  -H "Content-Type: application/json" -d '{
    "givenName":"Test","familyName":"Visitor",
    "ageBand":"26-35","gender":"M","isLocal":true,
    "addressLine":"1 Test Street","locality":"Hyderabad",
    "contacts":[{"contactType":"MOBILE","value":"9876500001","isPrimary":true}]
  }'
```

| # | Test | Expect |
|---|---|---|
| P1 | Valid create | `200`, `data.id` is a 26-char ULID, `data.referenceCode` allocated |
| P2 | No contacts | `400` — at least one contact is required |
| P3 | Missing `givenName` | `400` |
| P4 | **Duplicate phone** | `200` `responseType:1` warning — not silently created |
| P5 | Duplicate with `"allowDuplicate":true` | `200` created |
| P6 | Contact normalisation | `person_contact.normalized_value` set; dedupe uses it, never `value` |

### 4.2 Read, search, update

| # | Test | Request | Expect |
|---|---|---|---|
| P7 | Get by id | `GET /api/people/{ulid}` | `200`, contacts included |
| P8 | Bad id shape | `GET /api/people/not-a-ulid` | `400` "Invalid person id" |
| P9 | Unknown id | valid ULID that does not exist | `200` `responseType:1/2`, no data |
| P10 | Search + paging | `GET /api/people?page=1&pageSize=5` | `totalCount`, `totalPages` coherent |
| P11 | Lookup | `GET /api/people/lookup?q=9876500001` | finds by normalized phone |
| P12 | Update | `PUT /api/people/{id}` with current `rowVersion` | `200`, `rowVersion` incremented |
| P13 | **Stale `rowVersion`** | resend P12 with the old value | `200` `responseType:2` conflict — **must not** report success |

### 4.3 Do-not-contact and lifecycle

| # | Test | Expect |
|---|---|---|
| P14 | Set do-not-contact | `PUT /api/people/{id}/do-not-contact` `{"doNotContact":true,"note":"asked"}` → `200` |
| P15 | **DNC excludes from work** | that person's open case no longer appears in `FindUnassignedAsync` or nurture sweeps |
| P16 | Lifecycle transition | `PUT /api/people/{id}/lifecycle` `{"lifecycleStatus":"MEMBER","becameMemberOn":"2026-08-15"}` |
| P17 | Delete is soft | `DELETE /api/people/{id}` (ADMIN) → row still present with `deleted_at` set |
| P18 | Soft-deleted still referenceable | escalations/cases pointing at them still read |

P15 is the one worth automating — a do-not-contact request that does not actually stop
contact is the failure with real consequences.

---

## 4.4 Visitor intake screen — ✅ verified 2026-08-15

`wwwroot/templates/Peoples/PeopleEntry.html` + `assets/js/people-entry.js`.
Sign in as a DATA_ENTRY account; login routes there automatically.

The screen is a capture form, not a console, because the role genuinely cannot browse.
Verified boundary:

| As DATA_ENTRY | Result |
|---|---|
| `GET /api/admin/accounts` · `/api/people` · `/api/cases` · `/api/escalations` | **403** ✅ |
| `GET /api/people-reference` · `/api/people/lookup` | **200** ✅ |

| # | Test | Expect | Status |
|---|---|---|---|
| I1 | Page loads for DATA_ENTRY | Shell reads "Intake"; nav shows only "Record a visitor" | ✅ |
| I2 | Age groups load from the API | Options match `AgeBands.All` — they are CHECK-constrained, so hard-coding them would fail on save | ✅ |
| I3 | Record a visitor | Person created with a reference code; appears in "Recorded just now" | ✅ P0014 |
| I4 | Follow-up starts | A `care_case` is opened, auto-assigned, and a PENDING interaction created | ✅ C00006 → Helper3, call due 17 Aug |
| I5 | Pre-emptive duplicate check | On leaving the mobile field, a warning names the match with the number **masked** (`••••••2345`) | ✅ |
| I6 | Server refuses a duplicate | **Nothing is saved**; the button becomes "Save anyway" | ✅ |
| I7 | Explicit override | "Save anyway" resubmits with `allowDuplicate` and records a separate person | ✅ P0015 |
| I8 | Editing clears the warning | Any edit resets the override, so it cannot be carried into a different visitor | ✅ |
| I9 | DATA_ENTRY cannot reach Accounts | Navigating to `Admin/accounts.html` redirects back to intake | ✅ |
| I10 | Missing first name / mobile | Inline error, field marked `aria-invalid`, focus moved | — |
| I11 | Follow-up unchecked | Person saved, **no** case opened | — |
| I12 | Case fails after person saved | Reported as a **partial success**, not "nothing saved" — otherwise the operator enters the visitor twice | — |

> **Test data left behind:** P0014 "Anitha Reddy" and P0015 "Anitha Sharma" (both
> `9876512345`) plus their cases, created through the UI during verification. Harmless in
> the dev dataset — delete if you want a clean slate.

---

## 4.5 User management — ✅ verified 2026-08-15

`Admin/users.html` (role tabs) and `Admin/add-user.html`. Admin only.

**The model.** "User" is not a table. Identity lives on `person`, access on
`user_account` (1:1, `ux_user_account_person`), authority on `user_role` rows, and the
ability to carry cases on `volunteer` (1:1, `ux_volunteer_person`). Promotion grants a
role and adds a volunteer record when the role needs one — **a duplicate person is
structurally impossible**, not merely avoided by care.

Promotion is **additive**: a promoted volunteer keeps their volunteer record, which is
what makes a player-coach team lead possible (`team.max_span_player_coach`).

| # | Test | Expect | Status |
|---|---|---|---|
| U1 | Directory lists people with a login **or** a volunteer record | Plain visitors excluded — they are found via the people picker | ✅ |
| U2 | Role tabs filter and deep-link | `?role=TEAM_LEAD` reloads to the same view | ✅ |
| U3 | Volunteer with no login shows only View/Promote | Account actions hidden when there is no account | ✅ |
| U4 | **Visitor → Volunteer** | Account created (username = mobile), volunteer record created, **no new person** | ✅ Walkin → V005 |
| U5 | **Volunteer → Team Lead** | Role granted, **volunteer record kept** | ✅ Helper2 keeps V002 |
| U6 | **Team Lead → Pastor** | Additive — holds `PASTOR,TEAM_LEAD` | ✅ Ravi Kumar |
| U7 | History survives promotion | Their cases stay attached | ✅ Helper2 still holds 5 cases |
| U8 | Direct creation of a Pastor | Person + account + role in one step, password shown once | ✅ P0018 |
| U9 | Ladder respects existing standing | A volunteer is not offered "promote to Volunteer" | ✅ |
| U10 | Promotion sideways/down refused | "already holds an equal or higher role — use Change role" | — |
| U11 | No mobile on file | Refused with a clear message: the mobile is the username | — |
| U12 | Role change / activate / deactivate / reset password | Reuse the existing account endpoints | — |

**Integrity query** — should return one row per person and no orphans:

```sql
SELECT p.full_name,
       (SELECT COUNT(*) FROM user_account WHERE person_id=p.id) AS accounts,
       (SELECT COUNT(*) FROM volunteer    WHERE person_id=p.id) AS volunteer_recs
FROM person p HAVING accounts > 1 OR volunteer_recs > 1;
```

> **Seed-data note:** one person row shipped with a hand-written `public_id`
> (`01KZTESTPERSON…`) that is not a valid ULID — `O` is not in Crockford base32, so every
> endpoint that validates id shape rejected it with a 400. Corrected to a generated ULID.
> Worth checking before any other environment is seeded by hand.

---

## 4.6 Settings screen — ✅ verified 2026-08-15

`Admin/siteadmin.html`. Admin only. Replaces the dead `/api/systemconfig` and
`/api/cornjobs/*` calls that made the old screen appear broken.

| # | Test | Expect | Status |
|---|---|---|---|
| S1 | `GET /api/admin/settings` | 15 settings in 6 categories with type and bounds | ✅ |
| S2 | Value above `max_value` | Rejected: "Must be at most 168." | ✅ |
| S3 | Value below `min_value` | Rejected: "Must be at least 1." | ✅ |
| S4 | Non-numeric for INTEGER | Rejected: "Must be a whole number." | ✅ |
| S5 | Non-boolean for BOOLEAN | Rejected: "Must be true or false." | ✅ |
| S6 | Unknown key | Rejected: "No such setting." | ✅ |
| S7 | **Batch is all-or-nothing** | One bad value in a batch saves **nothing**, including the valid ones | ✅ |
| S8 | Valid save | Persists, bumps `row_version`, records `updated_by` | ✅ |
| S9 | Rejection lands on the field | Input marked invalid with its own reason beside it | ✅ |
| S10 | Dirty tracking | Changed inputs highlighted; Save disabled until something changes | ✅ |
| S11 | Run a job from the screen | Reports "N processed, N skipped, N failed" plus the explanatory note; writes a `job_run` row with `triggered_by` | ✅ |
| S12 | `is_editable = 0` | Input disabled; server refuses even if forced | — |
| S13 | Concurrent edit | Second save rejected: "Somebody else changed this setting first." | — |

S7 is the one worth keeping: a half-applied rule set is a state nobody chose.

> **Broadcast panel deliberately not reinstated.** The old screen had "Send to all
> volunteers". Notifications are queued but nothing sends them, so that button would
> report success while reaching nobody. It returns when a sender exists.

---

## 4.7 Volunteer assignments screen — ✅ verified 2026-08-15

`Volunteers/Assignments.html`. The screen volunteers use daily.

**The markup is unchanged** — volunteers are trained on this layout, so every element,
label, badge and button caption is exactly as it was. Only the data layer moved:

| Was | Now |
|---|---|
| `/volunteers/{id}/assignments` | `GET /api/contacts/mine` (stage `INITIAL_FOLLOW_UP`) |
| `/nurture/volunteer/{id}/due` | `GET /api/contacts/mine` (stage `NURTURE`) |
| `/followups/log-followup` | `POST /api/contacts/{id}/log` |
| `/nurture/step/log` | `POST /api/contacts/{id}/log` |

Both grids come from one call — a planned contact is a `care_interaction` whichever
stage it belongs to.

The four response options map onto `care_progression_rule`, so the wording volunteers
know now drives the data-driven engine:

| On screen | Outcome sent | Rule action |
|---|---|---|
| Not contacted / No response | `NO_ANSWER` | retry, or next nurture step |
| Normal — Mark complete | `SPOKE` + `WANTS_CONNECTION` | start / continue nurture |
| Needs follow-up — Assign to lead | `NEEDS_SUPPORT` | escalate |
| Crisis — Immediate alert | `CRISIS` | escalate immediately |

| # | Test | Expect | Status |
|---|---|---|---|
| A1 | List loads | "My List (n)", cards with name, phone, due date | ✅ |
| A2 | Both status states | First attempt `PENDING` / "Start Follow-up"; later `RETRY PENDING` / "Update Status" | ✅ |
| A3 | Header | Volunteer name and team lead name | ✅ |
| A4 | Log Normal | Nurture starts; volunteer is told **"Nurture started. Step 1 … due 24 Aug"** | ✅ |
| A5 | Log Crisis | Escalation raised at the outcome's tier, case set `ESCALATED`, item leaves the list | ✅ |
| A6 | Nurture grid | Shows only when a step is due; step and method badges correct | ✅ |
| A7 | Log a nurture step | Advances to step 2 (`VISIT`, due +7d) and says so | ✅ |
| A8 | Work not yet due | A step due next week does **not** appear | ✅ |
| A9 | Paused case hidden | An escalated case leaves the volunteer's list | ✅ |
| A10 | Not contacted | Hides the response section, sends `NO_ANSWER` | — |
| A11 | Stale item | Logging something already handled reports a conflict, not a silent double-log | — |
| A12 | **Step badge follows the plan** | `InteractionDto.nurtureTotalSteps` comes from the case's own plan. Deactivating steps 6–7 made the badge read **"Step 2/5"**, not "2/7" | ✅ |

A12 matters because nurture plans are per-campus and editable. The badge used to be
hardcoded to 7, so the first plan of any other length would have mislabelled every
step — visibly wrong to the volunteer and silently wrong to everyone else. When the
plan size is unknown the badge degrades to plain "Step 2" rather than inventing a
denominator.

### 4.7.1 Contact form brought up to the schema — ✅ verified 2026-08-15

The MVP form asked two questions and threw most of the schema away. What it could not
express, and now can:

| Field | Schema | MVP form | Now |
|---|---|---|---|
| `outcome_code` | 7 outcomes | 4 reachable | **all 7**, served from `care_outcome` |
| `intent_code` | 7 intents | **never captured** | **all 7** — rules key on it |
| `method_code` | 6 methods | hardcoded `CALL` | **all 6**, pre-set from the plan |
| `occurred_at` | any time | always "now" | **date/time picker**, so an evening round can be logged later |
| escalation reason | 9 reasons + tier + protocol flag | always `GENERAL_CONCERN` | **volunteer chooses**, with description |

**Why intent mattered most:** rules exist for `NOT_INTERESTED`, `ALREADY_CHURCHED` and
`DO_NOT_CONTACT` — all `CLOSE_CASE` — but none were reachable, so a case could never be
closed for those reasons and a do-not-contact request could not be honoured from the
volunteer's own screen.

The form drives itself from `contactMade` / `opensEscalation` on each outcome rather
than hardcoding codes, so adding an outcome needs no screen change.

| # | Test | Expect | Status |
|---|---|---|---|
| F1 | Options load from the server | 6 methods, 7 outcomes, 7 intents, 9 reasons | ✅ |
| F2 | Not-reached outcome | Intent, escalation and duration all hidden | ✅ |
| F3 | Spoke | Intent and duration shown; escalation hidden | ✅ |
| F4 | Escalating outcome | Escalation reason and description shown | ✅ |
| F5 | Escalation without a reason | Blocked: "Please choose why this needs a team lead." | ✅ |
| F6 | Escalation without a description | Blocked before the server's 10-char rule is hit | ✅ |
| F7 | Protocol reason selected | Safeguarding note shown for `ABUSE_DISCLOSURE` / `SELF_HARM_RISK` | ✅ |
| F8 | **Reason drives the tier** | `SELF_HARM_RISK` filed at **EMERGENCY** with the volunteer's own description — previously `GENERAL_CONCERN` at the outcome's tier | ✅ |
| F9 | Method recorded honestly | A step planned as a visit is stored `VISIT`, not `CALL` | ✅ |
| F10 | Intent closes a case | `SPOKE` + `NOT_INTERESTED` closes with reason `DECLINED` | — |
| F11 | Do-not-contact | `SPOKE` + `DO_NOT_CONTACT` sets `person.do_not_contact` | — |

F8 is the one that matters: the tier is the **more severe** of the reason's and the
outcome's, so a reason carrying `EMERGENCY` can never be quietly downgraded because the
outcome only suggested `STANDARD`.

> ### 🔒 Security fix (invisible to the volunteer)
> The list was selected by the **`?volunteerid=` query parameter** — editing the URL
> showed another volunteer's people and their phone numbers. It now comes from the
> signed-in token via `/api/contacts/mine`. The parameter is still read for the
> on-screen id but never decides what is fetched.
>
> **Consequence to know about:** a team lead opening `Assignments.html?volunteerid=X`
> now sees *their own* list, not X's. Viewing another volunteer's work needs an
> endpoint that does not exist yet.

---

## 4.8 Telegram linking — ✅ verified 2026-08-22

Sits in the post-sign-in flow beside the change-password screen: after signing in, a
person with no verified TELEGRAM contact sees **Connect Telegram**. Whether it can be
skipped is the admin setting `telegram.require_linking` (default **false** — turning it
on before the bot is configured would strand everyone).

Replaces the MVP's `getUpdates` + "Get Chat ID" flow, which took whichever chat had
messaged the bot most recently and so could silently attach the wrong person's chat.

| # | Test | Expect | Status |
|---|---|---|---|
| G1 | Webhook without the secret | **404**, not 401 — a prober cannot confirm the route | ✅ |
| G2 | Webhook with a wrong secret | 404 | ✅ |
| G3 | Malformed updates (`{}`, `null`, missing chat, non-numeric id) | **200** every time — a non-2xx makes Telegram retry forever | ✅ |
| G4 | Bot unconfigured | Status reports `isConfigured: false`; invitations refused with a clear message | ✅ |
| G5 | **Rule 1** — existing person + deep link | Verified TELEGRAM contact created, token marked used | ✅ |
| G6 | **Rule 5** — token replayed from another chat | Rejected; no second contact created | ✅ |
| G7 | **Rule 2** — known chat, no token | Recognised, no duplicate person | ✅ |
| G8 | **Rule 3** — unknown chat, no token | **No person auto-created** (deliberate scope choice) | ✅ |
| G9 | **Rule 4** — chat already owned by another person | Refused and logged as a conflict; **never reassigned** | ✅ |
| G10 | Disconnect | Soft: row kept, `opted_out_at` set, `is_verified` cleared, chat id still reserved | ✅ |
| G11 | Reconnect | Same row reactivated — no duplicate | ✅ |
| G12 | `telegram.require_linking` | Flips `isRequired`, hides Skip | ✅ |
| G13 | Sign-in gate | Diverts to the screen and stashes the role's real landing page | ✅ |
| G14 | Live detection | Page notices the link and forwards on its own, no refresh | ✅ |
| G15 | Already linked | Next sign-in goes straight through, no re-prompt | ✅ |

G9 is the one that matters most: one chat id delivering another person's pastoral
alerts is the worst failure this feature can have, so it is refused rather than
resolved by guessing.

### 4.8.1 Telegram setup screen — ✅ verified 2026-08-22

`Admin/telegram.html`. Diagnostics and the one-time webhook registration.

**Deliberately not a form for the bot token.** Typing it into a web page would put the
credential in a request body, the server log and browser history. The screen reports
whether each secret is PRESENT and never what it is.

| # | Test | Expect | Status |
|---|---|---|---|
| T1 | Status checks | Token / username / webhook secret each shown present or missing | ✅ |
| T2 | Connection test | Calls Telegram getMe and reports the real answer — a bad token shows `Unauthorized` | ✅ |
| T3 | **Token never in the response** | `/api/telegram/setup` returns booleans; grep for the token value finds nothing | ✅ |
| T4 | **Token never in the page** | Rendered HTML contains no token | ✅ |
| T5 | **Token never in the logs** | See the note below — this failed first time | ✅ after fix |
| T6 | Register disabled without prerequisites | Needs both a token and a webhook secret | ✅ |
| T7 | Adoption count | Number of people with an active link | ✅ |

> ### 🔒 Token leaked to the logs — found and fixed
> `HttpClient`'s built-in request logging writes the full request URI, and Telegram
> carries the bot token **in the URI path** (`api.telegram.org/bot<TOKEN>/getMe`). So
> every call published the credential to the log pipeline — in production, to Coolify.
> The application's own logging was already clean; this was the framework's.
>
> Fixed with `.RemoveAllLoggers()` on the named client. **Re-test this after any change
> to the HTTP client registration** — it is invisible in the UI and only shows up by
> grepping the log for the token.

> **Not verified locally:** Telegram cannot call `localhost`, so a real bot delivering
> a real update needs a public HTTPS tunnel or a deployed environment. Everything above
> was proved by posting the exact payload shape Telegram sends to the real endpoint,
> with the real secret, against the real database. The one untested link is Telegram's
> own delivery.

---

## 5. Volunteers and teams

Capacity bands: `LIMITED` (1–2), `BALANCED` (2–3), `CONSISTENT` (4–6).

| # | Test | Request | Expect |
|---|---|---|---|
| V1 | Enrol | `POST /api/volunteers` `{"personId":"<ulid>","capacityBandCode":"BALANCED"}` | `200`, status `ACTIVE` |
| V2 | Enrol a non-existent person | bad `personId` | `200` `responseType:2` — volunteers attach to existing people |
| V3 | Duplicate enrol | same person twice | rejected (`ux` on person) |
| V4 | List + filter | `GET /api/volunteers?hasCapacity=true` | only those under their band max |
| V5 | Eligible picker | `GET /api/volunteers/eligible?campusId=<id>` | **least-loaded first** — the same order the assignment job uses |
| V6 | Change capacity | `PUT /api/volunteers/{id}/capacity` `{"capacityBandCode":"LIMITED","rowVersion":N}` | `200` + a `volunteer_capacity_change` history row **in the same transaction** |
| V7 | Capacity history | `GET /api/volunteers/{id}/capacity-history` | shows from/to band |
| V8 | Safeguarding (ADMIN only) | `PUT /api/volunteers/{id}/safeguarding` | `200`; other roles `403` |
| V9 | Deactivate | `PUT /api/volunteers/{id}` `{"status":"EXITED","rowVersion":N}` | excluded from `eligible` |
| V10 | Stale `rowVersion` | any PUT with an old version | conflict, not success |
| T1 | Create team | `POST /api/teams` (ADMIN) | `200` |
| T2 | Team span limit | add past `maxMembers` | rejected |

**Counter integrity (V11) — worth a dedicated check.** `volunteer.current_case_load` is
*maintained*, not derived. After assigning and closing cases, assert:

```sql
SELECT v.id, v.current_case_load,
       (SELECT COUNT(*) FROM care_case c
         WHERE c.assigned_volunteer_id = v.id AND c.status <> 'CLOSED') AS derived
FROM volunteer v
HAVING current_case_load <> derived;
```
**Zero rows expected.** The MVP kept a counter *and* a derived figure and they drifted;
this query is the regression guard.

---

## 6. Care — the core funnel

Reference codes: `GET /api/contacts/reference`.
Outcomes: `SPOKE`, `NO_ANSWER`, `LEFT_MESSAGE`, `NEEDS_SUPPORT`, `CRISIS`, `UNREACHABLE`, `WRONG_NUMBER`.
Intents: `WANTS_CONNECTION`, `NEEDS_TIME`, `UNDECIDED`, `NOT_INTERESTED`, `ALREADY_CHURCHED`, `DO_NOT_CONTACT`, `UNKNOWN`.

### 6.1 Open and assign

| # | Test | Expect |
|---|---|---|
| C1 | Open a case | `POST /api/cases` `{"personId":"<ulid>","autoAssign":true}` → `200`, stage `INTAKE`, a volunteer attached |
| C2 | Open with `autoAssign:false` | status `AWAITING_ASSIGNMENT`, no volunteer |
| C3 | **Duplicate open case** | opening a second case for the same person → refused (`FindOpenCaseForPersonAsync`) |
| C4 | Open for a do-not-contact person | refused |
| C5 | Manual assign | `PUT /api/cases/{id}/assign` `{"volunteerId":"<ulid>","rowVersion":N}` → `200`, both volunteers' counters adjusted, `care_case_assignment` history row written |
| C6 | Assign over capacity | volunteer at band max → warning or refusal |

### 6.2 Logging a contact — the progression engine

This is the heart of the system. `POST /api/contacts/{interactionId}/log`.

```bash
curl -s -X POST http://localhost:5043/api/contacts/{interactionId}/log -H "$AUTH" \
  -H "Content-Type: application/json" -d '{
    "outcomeCode":"SPOKE","intentCode":"WANTS_CONNECTION",
    "methodCode":"CALL","durationMinutes":10,
    "notes":"Warm conversation.","rowVersion":1
  }'
```

Each row below is a separate test — the routing decision comes from `care_progression_rule`,
so these assert the **data**, not hard-coded C#:

| # | Outcome + intent | Expected action | Assert on the case |
|---|---|---|---|
| C7 | `SPOKE` + `WANTS_CONNECTION` | `START_NURTURE` | stage `NURTURE`, `nurture_plan_id` set, `current_step_number` 0, a **PENDING step-1 interaction created** |
| C8 | `SPOKE` + `NOT_INTERESTED` | `CLOSE_CASE` | status `CLOSED`, `close_reason` set, `closed_at` set, volunteer capacity released |
| C9 | `SPOKE` + `DO_NOT_CONTACT` | close **and** `person.do_not_contact = 1` | the block follows the **person**, surviving a future re-entry |
| C10 | `NO_ANSWER` | `SCHEDULE_RETRY` | `consecutive_no_contact` incremented, a retry interaction created |
| C11 | `NO_ANSWER` × `max_retry_attempts` (3) | case marked unreachable / sent to review | no infinite retry loop |
| C12 | `CRISIS` | `ESCALATE` | an `escalation` row **and** the case paused at status `ESCALATED` — in one transaction |
| C13 | `NEEDS_SUPPORT` | escalation at the outcome's default tier | tier matches `care_outcome.default_tier` |
| C14 | Unmatched combination | `MANUAL_REVIEW` | `awaiting_review_since` set — **never** a guess |
| C15 | Nurture step completed, more remain | `CONTINUE_NURTURE` | `current_step_number` += 1, next step created, `next_step_due_on` = today + gap |
| C16 | Final nurture step completed | `SEND_TO_REVIEW` | stage `REVIEW`, `next_step_due_on` null |
| C17 | Stale `rowVersion` | conflict, not double-logging |

**C14 is the important one.** The MVP guessed at unknown response types and silently did
the wrong thing. Unmatched input must reach a human.

### 6.3 Escalations — pause and resume

| # | Test | Expect |
|---|---|---|
| E1 | Raise manually | `POST /api/cases/{id}/escalations` `{"reasonCode":"HEALTH_CRISIS","description":"...","tier":"URGENT","caseRowVersion":N}` → `200` |
| E2 | **Case is paused** | the case's status becomes `ESCALATED`; it disappears from nurture sweeps |
| E3 | Description too short | under 10 chars → `400` |
| E4 | Acknowledge | `POST /api/escalations/{id}/acknowledge` (TEAM_LEAD+) → `acknowledged_at` set, status `ACKNOWLEDGED` |
| E5 | Resolve without an outcome | rejected by `ck_escalation_resolved` — a resolved escalation must record when and how |
| E6 | Resolve | `POST /api/escalations/{id}/resolve` `{"outcomeCode":"PASTORAL_CARE","rowVersion":N,"resumeInDays":7}` |
| E7 | **Case resumes** | the case returns to its previous stage with `next_step_due_on` recomputed — the sequence continues where it stopped, it does not restart |
| E8 | Safeguarding reasons | `ABUSE_DISCLOSURE` / `SELF_HARM_RISK` require `protocolFollowed` etc. on resolve |
| E9 | Volunteer cannot resolve | `403` |

E2 and E7 are a pair: test them together. A case that pauses but never resumes is stalled
forever, and one that resumes by restarting re-contacts someone mid-crisis.

### 6.4 Work list and notes

| # | Test | Expect |
|---|---|---|
| C18 | `GET /api/contacts/mine` | only **your** due contacts, oldest first |
| C19 | Another volunteer's work is invisible | not present in the list |
| C20 | Add a note | `POST /api/cases/{id}/notes` |
| C21 | Confidential note | `isPrivate:true` requires `visibleToRole` (`ck_note_confidential`) |
| C22 | Private note hidden | a volunteer does not see a TEAM_LEAD-only note |

---

## 7. Jobs — ✅ verified end-to-end 2026-08-15

Authenticated by the `JobRunner` policy: an **Admin token** *or* the header
`X-Service-Key`. No user identity is required — the machine caller has none.

```bash
KEY="dev-only-service-key-not-for-production"
curl -s -X POST -H "X-Service-Key: $KEY" http://localhost:5043/api/jobs/run-all
```

| # | Test | Expect | Status |
|---|---|---|---|
| J1 | No key | `401` | ✅ verified |
| J2 | Wrong key | `401` | ✅ verified |
| J3 | Valid key | `200` + a `JobReport` | ✅ verified |
| J4 | Every run is audited | a `job_run` row per call, `finished_at` set, status `SUCCEEDED`/`PARTIAL`/`FAILED` | ✅ verified |
| J5 | `GET /api/jobs/history` | recent runs, newest first | ✅ verified |
| J6 | Unknown `jobName` filter | `responseType:1` | — |
| J7 | Overlap guard | a second call while one is `RUNNING` is refused | — |

### 7.1 Assignment — `POST /api/jobs/assign-unassigned`

| Test | Setup | Expect |
|---|---|---|
| J8 | Empty queue | no unassigned cases | `0 processed`, note "Nothing waiting for assignment." ✅ verified |
| J9 | Drains the queue | 3 unassigned cases + volunteers with capacity | 3 processed, each to the **least-loaded** volunteer |
| J10 | No capacity | all volunteers at band max | skipped, noted **once per campus**, not once per case |
| J11 | Do-not-contact excluded | person flagged DNC | never assigned |
| J12 | Idempotent | run twice | second run processes 0 |

### 7.2 Nurture — `POST /api/jobs/advance-nurture`

| Test | Expect |
|---|---|
| J13 | Case due for its next step | a PENDING interaction created at `current_step_number + 1` |
| J14 | Escalated case | **skipped** — an open escalation pauses the case |
| J15 | Case with a pending interaction already | skipped (no duplicate step) |
| J16 | **Plan exhausted** | sent to `REVIEW` rather than sitting due forever — the resumed-at-final-step edge case |
| J17 | Reassignment | if the plan is `SAME_IF_AVAILABLE` and the volunteer is inactive/full, a new one is picked |
| J18 | Lookahead honoured | `nurture.lookahead_days` (1) controls how far ahead steps are created |

### 7.3 Overdue — `POST /api/jobs/mark-overdue`

| Test | Expect |
|---|---|
| J19 | Pending contact past its date + grace | status `MISSED` |
| J20 | Within the grace period | untouched (`assignment.retry_delay_days`, default 3) |
| J21 | Logged mid-sweep | counted as **skipped**, not failed — someone logging the contact is the good outcome |
| J22 | Case is **not** advanced | by design: a miss has no outcome for the engine to route on |

### 7.4 Escalation chase-up — `POST /api/jobs/chase-escalations` ✅ verified

Thresholds from `app_setting`: `escalation.ack_target_hours` (4),
`escalation.reminder_every_hours` (4), `escalation.pastor_alert_hours` (12).

| Test | Expect | Status |
|---|---|---|
| J23 | Nothing unacknowledged | `0 processed`, note "No unacknowledged escalations." | ✅ verified |
| J24 | Unacknowledged > 4h | 1 processed; `reminder_count` = 1, `last_reminder_at` set | ✅ verified |
| J25 | Unacknowledged > 12h | `pastor_alerted_at` set; a second delivery queued to `PASTOR` | ✅ verified |
| J26 | **Reminder interval** | an immediate second run **skips** — no double-pinging | ✅ verified |
| J27 | **Reaches nobody** | job reports **`FAILED`**; deliveries recorded `SKIPPED` with a reason | ✅ verified |
| J28 | Unassigned escalation | falls back to **every** team lead at the campus | — |
| J29 | `EMERGENCY` tier | skips the 12h pastor wait entirely | — |
| J30 | Acknowledged escalation | never chased again | — |

**Fixture for J24–J27** (create, test, then delete and re-check baseline counts):

```sql
INSERT INTO escalation (public_id, care_case_id, campus_id, assigned_to_user_id,
                        reason_code, tier, status, description, raised_at)
VALUES ('01TESTCHASE000000000000001', 2, 1, 2, 'GENERAL_CONCERN', 'STANDARD', 'NEW',
        'TEMP verification fixture', NOW(3) - INTERVAL 20 HOUR);

-- Make a recipient reachable.
-- normalized_value MUST be the numeric chat id: that is the column the queue
-- reads and the only thing the Bot API can send to. is_verified=1 and a null
-- opted_out_at are required too, or the person resolves as unreachable.
INSERT INTO person_contact (person_id, contact_type, value, normalized_value,
                            is_primary, is_verified, verified_at)
VALUES ((SELECT person_id FROM user_account WHERE id=2),
        'TELEGRAM','@tg-test','900000002',0,1,UTC_TIMESTAMP(3));
```

Cleanup:
```sql
DELETE FROM notification_delivery;
DELETE FROM escalation WHERE public_id='01TESTCHASE000000000000001';
DELETE FROM person_contact WHERE contact_type='TELEGRAM';
DELETE FROM job_run;
```

> ### ⚠️ Data gap in the dev database
> **No `TELEGRAM` rows in `person_contact` at all** (only MOBILE and EMAIL), so out of
> the box the chase-up reaches nobody and correctly reports `FAILED`. **Check production
> for the same gap** — the sender cannot fix an empty recipient list.
>
> The earlier "no account holds `PASTOR`" gap is **closed**: users 2 (Ravi Kumar) and
> 5 (Grace Mathew) now hold it, confirmed 2026-08-22.

---

## 8. Notifications — ✅ verified end-to-end 2026-08-22

### 8.1 Queueing

| # | Test | Expect | Status |
|---|---|---|---|
| N1 | Delivery queued | a `notification_delivery` row per recipient with the right `notification_type` and `related_entity_id` | ✅ verified |
| N2 | Unreachable recipient | status `SKIPPED` + `failure_reason` — recorded, never dropped silently | ✅ verified |
| N3 | **No message body stored** | the table has no body column; assert none is added. Alerts quote pastoral detail and names | ✅ verified |
| N4 | **`recipient_address` is the chat id** | the queue reads `person_contact.normalized_value`, never `value` (which holds the `@username`) | ✅ verified |
| N5 | Opted-out / unverified contact | resolves as **unreachable**; the row is `SKIPPED`, not queued for a send that could never land | ✅ verified |

### 8.2 The sender — `POST /api/jobs/send-notifications`

The fifth job. Drains `notification_delivery` over Telegram and is the step that makes
every alert above actually reach somebody. Runs last in `run-all`, so alerts queued by
that same sweep go out in it.

**Three terminal states, and the difference is the point:**

| State | Meaning | Retried? |
|---|---|---|
| `SENT` | Telegram accepted it | no |
| `SKIPPED` | undeliverable by nature — no chat id, entity gone, alert no longer applies | **never** — retrying cannot change it |
| `FAILED` | attempted and refused, out of attempts | until `Notifications:MaxAttempts` (3) |

| # | Test | Expect | Status |
|---|---|---|---|
| N6 | Queue drains | 8 pending → `8 processed`, all rows `SENT` with `attempt_count=1` and `sent_at` set | ✅ verified |
| N7 | Message body | Telugu + HTML, `parse_mode=HTML`, tier-specific title, link to the assignments page | ✅ verified |
| N8 | `requires_protocol` reason | the safeguarding line appears **only** for those reasons (e.g. `SELF_HARM_RISK`) | ✅ verified |
| N9 | `escalation.notified_at` | stamped on the **first** delivery only — the column answers "when was anybody told?" | ✅ verified |
| N10 | Idempotent | a second sweep sends nothing; note "Nothing waiting to be sent." | ✅ verified |
| N11 | Bad address (`@username`) | `SKIPPED`, "Recipient address is not a Telegram chat id." Never retried | ✅ verified |
| N12 | Acknowledged in the meantime | `SKIPPED`, "Acknowledged before the alert was sent." — no nagging about handled work | ✅ verified |
| N13 | Related entity gone | `SKIPPED`, "That escalation no longer exists." | ✅ verified |
| N14 | Type with no template | `SKIPPED`, "No message template for 'CASE_ASSIGNED'." | ✅ verified |
| N15 | **Retry ladder** | Telegram refusing → `PENDING`/1, `PENDING`/2, `FAILED`/3, then never picked again | ✅ verified |
| N16 | **Bot not configured** | job reports `FAILED`; the queue is left **completely untouched** (`attempt_count` still 0) | ✅ verified |
| N17 | Backlog survives an outage | the row from N16 delivers on the next sweep once the bot is configured | ✅ verified |
| N18 | Failed send is not success | any failure puts the run at `PARTIAL`/`FAILED`, never `SUCCEEDED` | ✅ verified |

**N16 is the one worth keeping.** Marking the queue `FAILED` when the bot is merely
unconfigured would burn the attempt budget on a misconfiguration, so that by the time
somebody supplied the token the backlog would be dead rather than deliverable.

#### Testing the send path without a real bot

Point the client at a local stub rather than at Telegram — this exercises the real HTTP
path, the real payload and the real failure handling, with no token and no live chat:

```bash
Telegram__BotToken=stub-token-not-real \
Telegram__BotUsername=stubbot \
Telegram__ApiBaseUrl=http://localhost:5099 \
dotnet run --no-launch-profile
```

Any HTTP server on 5099 that answers `{"ok":true}` to `POST /bot<token>/sendMessage`
will do; answering `400` instead exercises N15. **Log only the method segment of the
path, never the whole URI** — the token is in the path.

#### Configuration

`Notifications` in appsettings. These are deployment facts, not business rules, which is
why they are not in `app_setting`:

| Key | Dev | Prod | Why |
|---|---|---|---|
| `PublicBaseUrl` | `http://localhost:5043` | `https://rmoffice.online` | the link inside every alert; a localhost link in production is worse than none |
| `MaxAttempts` | 3 | 3 | capped by `attempt_count` being a TINYINT |
| `BatchSize` | 100 | 100 | Telegram throttles bots at roughly 30 messages/second |

---

## 8.5 Team lead dashboard — ✅ verified 2026-08-24

`GET /api/dashboards/team-lead`, rendered into the **existing**
`wwwroot/templates/TeamLeads/TeamLeadDashboard.html`.

**The UI is unchanged on purpose.** Team leads already work in this screen, so only the
data layer moved: `teamlead.js` now makes one call to the new endpoint instead of four to
routes that no longer exist. Same Bootstrap 5.3 + inline `<style>`, same `#085c40` green,
same `.page-header`, same six cards, same header buttons. Verified in the browser: the
page loads **no** `admin.css`.

**The security property this rewrite exists for:** the endpoint takes **no team
parameter**. Teams are resolved from `team.lead_user_id` against the signed-in account.
The old page read `?teamleadid=` from the query string and asked the server for that
lead's data, so any authenticated user could read any team lead's queue — including
safeguarding escalations naming real people — by editing the URL.

| # | Test | Expect | Status |
|---|---|---|---|
| D1 | Signed-in team lead | all six cards populate from one call | ✅ verified |
| D2 | **Query string ignored** | `?teamleadid=…&teamLeadId=999` → identical page, same team | ✅ verified |
| D3 | `#lnk_manual` | no longer carries `?teamleadid=` | ✅ verified |
| D4 | Unauthenticated | `401`; page shows the error banner in `#alerts` | ✅ verified |
| D5 | Signed in, not a team lead | `403` → "You do not have team lead access." | ✅ verified |
| D6 | Leads no team | empty dashboard, "No team assigned" — **not** everyone's data | ✅ verified |
| D7 | Escalation assigned personally, leads no team | still shown | ✅ verified |
| D8 | Terminal escalations excluded | RESOLVED / CLOSED / REFERRED_OUT never counted | ✅ verified |
| D9 | Unassigned team escalations included | reached via `care_case.team_id` | ✅ verified |
| D10 | Member count vs volunteer list | both ACTIVE-only; "2 of 3 members" never sits above 3 rows | ✅ verified |
| D11 | Overdue definition | same `assignment.retry_delay_days` grace the mark-overdue job uses | ✅ verified |
| D12 | Ordering | unacknowledged first, then EMERGENCY → URGENT → STANDARD, then oldest | ✅ verified |
| D13 | Siren icon | 🚨 only for EMERGENCY or `requiresProtocol`; ⚠️ otherwise | ✅ verified |
| D14 | Names escaped | volunteer/person names go through `escapeHtml` before reaching HTML | ✅ verified |

Verified against `cms_api_db` with team 1 temporarily led by user 2, then restored
byte-identically. Rendered values matched direct SQL: 2 in-progress + 2 escalated cases,
1 overdue contact, 2 unacknowledged EMERGENCY escalations (oldest 168h), 2 active
volunteers at 2/3 capacity, red badge of 2 on the volunteer who raised both.

### 8.5.1 Related screens — ✅ verified 2026-08-25

The dashboard is only as useful as the screens it links to. All three links were
broken; two are fixed and one has no backend to fix it against.

> **Every iframe modal on the dashboard was dead.** `openInModal` loaded detail pages
> into `#escIframe`, but the app sends `X-Frame-Options: DENY` and
> `frame-ancestors 'none'`, so the browser refused every one — clicking an escalation,
> the team lead's main job, produced "refused to connect". Those headers are deliberate
> clickjacking defence and stay; the modal was the wrong mechanism, and all three links
> now navigate (the Add Volunteer button had already been moved off it for this reason).

**Escalation detail** — `TeamLeads/Escalations.html`

| # | Test | Expect | Status |
|---|---|---|---|
| D15 | `GET /api/escalations/{id}` | **new endpoint**; the screen had nothing to load from | ✅ verified |
| D16 | Campus scoping | another campus's escalation reads as not found | ✅ verified |
| D17 | Acknowledge | status → ACKNOWLEDGED, row version bumps | ✅ verified |
| D18 | Resolve | RESOLVED + outcome + resolvedAt set | ✅ verified |
| D19 | **Paused case resumes** | case ESCALATED → IN_PROGRESS, next contact dated | ✅ verified |
| D20 | **Stale row version refused** | "changed by someone else"; nothing overwritten | ✅ verified |
| D21 | Refusal is not reported as success | a `responseType` 1 inside HTTP 200 shows as an error | ✅ verified |
| D22 | Outcome codes | v2 codes (`PASTORAL_CARE`), not MVP labels | ✅ verified |
| D23 | Safeguarding block hidden | absent for `GENERAL_CONCERN` | ✅ verified |
| D24 | Safeguarding block shown | present for `SELF_HARM_RISK`, and resolve is refused until confirmed | ✅ verified |

**Manual assignment** — `Peoples/ManualAssignments.html`

| # | Test | Expect | Status |
|---|---|---|---|
| D25 | Queue lists **cases**, not people | 17 unassigned, with person name and phone | ✅ verified |
| D26 | Volunteer list | every ACTIVE volunteer with load, least-loaded first | ✅ verified |
| D27 | Over-capacity is allowed but visible | "already at capacity" rather than an empty dropdown | ✅ verified |
| D28 | Assign | case → IN_PROGRESS, queue decrements, `care_case_assignment` row written `MANUAL` | ✅ verified |
| D29 | No `?teamleadid=` | identity comes from the token | ✅ verified |

D27 is deliberate: manual assignment exists for exactly the times the automatic picker
finds nobody, so the cost has to be shown rather than the option removed.

**Check-ins** — `TeamLeads/CheckIns.html` — ✅ built 2026-08-25 (`Modules/CheckIns`)

`volunteer_check_in` had no code at all; it now has a module. This is the only part of
the system that looks after the people doing the work rather than the people being
cared for, which is why tone, concerns and boundary issues are columns and not free text.

| # | Test | Expect | Status |
|---|---|---|---|
| D30 | `GET /api/check-ins/due` | who on the caller's teams is due, longest-waiting first | ✅ verified |
| D31 | Never checked in sorts first | worse than being a fortnight late, so it leads | ✅ verified |
| D32 | Record a check-in | row written; conductor taken from the **token**, not the payload | ✅ verified |
| D33 | `last_check_in_on` moves | check-in row and volunteer date update in one transaction | ✅ verified |
| D34 | **Another team's volunteer** | refused — "only for volunteers on your own team" | ✅ verified |
| D35 | Future date | refused | ✅ verified |
| D36 | `needsAttention` | true for RED, a boundary issue, or an explicit follow-up | ✅ verified |
| D37 | Capacity change during a check-in | goes through `IVolunteerService`; `volunteer_capacity_change` row written with reason `CHECK_IN` | ✅ verified |
| D38 | Bad band code | check-in still saved, and the message says the band was **not** changed | ✅ verified |
| D39 | Dashboard check-ins card | shows who is due, with the last tone badged | ✅ verified |

D38 is the one to keep: the conversation is the record that matters, so a wrong band
code must not lose it — but a lead who thinks they reduced somebody's load and has not
is worse off than one who is told it did not take.

> **Route collision found:** the legacy `Controllers/Teamleads/CheckInController` also
> claimed `POST /api/check-ins`, so the first call returned a 500
> `AmbiguousMatchException`. That slice (controller + `CheckInBLL` + `CheckInDAL`) is
> superseded and was deleted.

**Creating a team lead** — `TeamLeads/TeamLeads.html` — ✅ rewired 2026-08-25

Posted to the removed `/TeamLeadDashBoards/save-team-lead`. Now two calls:
`POST /api/admin/users` (person + login + TEAM_LEAD role), then `POST /api/teams`
carrying **Max Volunteers** — which belongs to the team in v2 (`team.max_members`),
not to the person. Both need ADMIN, and a 403 says so plainly rather than showing a
raw error. The duplicate copy of this handler inside `teamlead.js` was removed.

### Cards with no data behind them

**Team Huddle** and **Nurture Sequences** were already `display:none` in this page and
stay hidden — there is no huddle table on the new schema and the nurture roll-up endpoints
are gone. **Check-ins** renders its original "No upcoming check-ins" empty state, because
`volunteer_check_in` still has no code. All three degrade exactly as the old page did on a
quiet day rather than being deleted, so nothing a team lead recognises has disappeared.

Two columns have no equivalent on the new schema and show what they honestly can:
**Trend** renders `—` (no week-on-week history is kept yet), and **Capacity | Target
Range** now reads `<band> | <load> / <max>`.

---

## 8.6 Team Huddle — ✅ verified 2026-08-25

`GET /api/huddle`, `POST /api/huddle/verdicts`, `POST /api/jobs/huddle-reminder`,
and the modal on `TeamLeadDashboard.html`.

**What it is:** the weekly meeting (Saturday — `huddle.day_of_week` = 6, carried over
from the MVP's `system_config.team_hurdle`) where the lead reviews the week's contacts
and records whether each volunteer's escalation judgement was right: `CORRECT`,
`UNDER_ESCALATED`, `OVER_ESCALATED`.

> **This is the only mechanism that can catch an under-escalation** — a concern that was
> heard and never passed on. The chase-up job cannot: it only chases escalations that
> were actually raised.

**Schema change** (`Database/Migration/002_huddle_assessment.sql`, also in `schema.sql`):
`care_interaction` gains `escalation_assessment`, `assessment_note`, `assessed_by`,
`assessed_at`. The MVP kept the verdict on `follow_ups.escalation_appropriate`; that
column had no home after follow-ups and nurture steps collapsed into `care_interaction`.

| # | Test | Expect | Status |
|---|---|---|---|
| H1 | Agenda window | a real 7-day window, not "everything ever" | ✅ verified |
| H2 | Backlog is a **count** | older unassessed shown as a number, never loaded into the table | ✅ verified |
| H3 | Team scoping | only contacts on the caller's teams; no team parameter exists | ✅ verified |
| H4 | Completed only | pending/missed contacts carry no judgement to assess | ✅ verified |
| H5 | Batch save | the whole sitting in one call | ✅ verified |
| H6 | Unknown verdict | refused | ✅ verified |
| H7 | **Miscalibration needs a note** | UNDER/OVER without a reason is refused | ✅ verified |
| H8 | Cross-team verdict | **rejected and not written**; the count is reported back | ✅ verified |
| H9 | Assessor recorded | `assessed_by` comes from the token | ✅ verified |
| H10 | Under-escalation logged | a warning line of its own — it is the failure the meeting exists to find | ✅ verified |

### 8.6.1 The huddle reminder

`POST /api/jobs/huddle-reminder` — the sixth job, in `run-all` before the sender.

| # | Test | Expect | Status |
|---|---|---|---|
| H11 | Wrong day | does nothing; the note says which day it is and which it wants | ✅ verified |
| H12 | Huddle day | every active team with a lead is reminded — 4 teams, 31 people | ✅ verified |
| H13 | **Lead AND volunteers** | `huddle.remind_volunteers` (default true); a huddle only the lead remembers is not a huddle | ✅ verified |
| H14 | Twice in one day | second run skips every team; the queue stays at 31 | ✅ verified |
| H15 | Delivery | all 31 sent via the notification sender, Telugu copy renders | ✅ verified |
| H16 | **No pastoral detail** | the message carries no names or circumstances — it goes to a whole team; the agenda is behind a login | ✅ verified |

H16 is deliberate. The huddle discusses named people and their crises; a reminder that
quoted any of it would broadcast exactly what should not leave the room.

**Settings** (`app_setting`, category `HUDDLE`): `day_of_week` (6), `lookback_days` (7),
`reminder_hour` (8), `remind_volunteers` (true).

> ### ⚠️ The MVP version was never used
> All 229 follow-ups in the production backup are `Not-Assessed` — not one verdict in
> three months. The idea was not the problem; the mechanics were. The old modal was
> titled "for this Week" while the week filter sat commented out of the SQL, so it
> returned every unassessed contact ever, unpaginated; and each row needed its own
> Update click and confirmation dialog. Both are fixed above. **If it goes unused again,
> that is a signal about the practice, not the screen.**

---

## 9. Cross-cutting

| # | Area | Test | Expect |
|---|---|---|---|
| X1 | Security headers | any response | HSTS (non-dev), `X-Content-Type-Options`, frame options, CSP present |
| X2 | Correlation id | any response | `X-Correlation-Id` header; the same value inside error bodies |
| X3 | Server header | any response | **no** `Server: Kestrel` |
| X4 | Rate limit — login | 6 logins in 5 min | `429` + `Retry-After` |
| X5 | Rate limit — global | > 240 requests/min | `429` |
| X6 | Body size | POST > 2 MB | `413` |
| X7 | Unknown static type | request an unmapped extension in `wwwroot` | **not served** |
| X8 | HTML not cached | fetch any `.html` | `Cache-Control: no-store` |
| X9 | Unhandled exception | force one | ProblemDetails, **no stack trace**, correlation id present |
| X10 | CORS | request with a foreign `Origin` | rejected (allow-list is empty = same-origin only) |
| X11 | Startup fails closed | run with a weak `Jwt__SigningKey` | the app **refuses to boot** |
| X12 | Swagger | `GET /swagger` in Production | not available |
| X13 | Admin pages at 375px | no horizontal page scroll; wide tables scroll inside `.table-wrap` | ✅ verified |
| X15 | **UNSIGNED arithmetic** | `max_per_week - current_case_load` must be CAST to SIGNED; over-capacity volunteers otherwise 500 the whole dashboard | ✅ verified |
| X14 | Admin pages on desktop | unchanged — `.shell` still 56px, `flex-wrap: nowrap` | ✅ verified |

**X13 was a pre-existing bug**, found while testing on a phone: `admin.css` had **no media
queries at all**, and `.shell` is a fixed-height flex row that cannot wrap, so the nav plus
the signed-in badge overflowed and every page using the admin shell scrolled sideways
(`AddVolunteer.html` measured 511px in a 375px viewport). The fix is one additive
`@media (max-width: 640px)` block at the end of `admin.css`, so it can only apply where the
layout was already broken. **This does not affect the team lead dashboard**, which has its
own theme and never loaded `admin.css`.

---

## 10. End-to-end golden path

The scenario worth running after any significant change. Each step's output feeds the next.

1. Log in as admin → token
2. `POST /api/people` → a visitor
3. `POST /api/volunteers` → enrol someone with `BALANCED` capacity
4. `POST /api/cases` `{autoAssign:false}` → case `AWAITING_ASSIGNMENT`
5. `POST /api/jobs/assign-unassigned` → **1 processed**; the case now has the volunteer
6. `GET /api/contacts/mine` (as that volunteer) → the initial follow-up is listed
7. `POST /api/contacts/{id}/log` `SPOKE` + `WANTS_CONNECTION` → nurture starts, step 1 created
8. `POST /api/contacts/{step1}/log` `CRISIS` → escalation raised, **case pauses**
9. `POST /api/jobs/advance-nurture` → the case is **skipped** (correctly paused)
10. Wait past the threshold (or backdate `raised_at`) → `POST /api/jobs/chase-escalations` → team lead reminded
11. `POST /api/jobs/send-notifications` → the reminder is **actually delivered**; the row goes `SENT` and `escalation.notified_at` is stamped
12. Backdate 12h → chase again, send again → **pastor alerted and told**
13. `POST /api/escalations/{id}/acknowledge` then `/resolve` → **case resumes**
14. `POST /api/jobs/advance-nurture` → the next step is created, continuing where it stopped
15. Log the remaining steps → `SEND_TO_REVIEW`
16. `POST /api/cases/{id}/review` → closed; **volunteer capacity released**
17. Assert the counter query in §5 returns zero rows

If all seventeen pass, the migrated half of the system works.

---

## 11. What you cannot test yet

| Area | Why |
|---|---|
| Telegram delivery **against the real Bot API** | The sender is verified against a stub (§8.2); no live bot token has been exercised end to end |
| Admin settings UI/API | `app_setting` is read-only from code; nothing edits it |
| Volunteer surveys | `volunteer_survey` has no code (check-ins are built — see §8.5.1) |
| Pastor dashboard | Not rewritten (the team lead one is done — see §8.5) |
| 15 of 17 frontend pages | They call dropped tables |
| Data migration from the MVP | No migration path or `schema_migration` ledger exists |
| Regression suite | **There are no automated tests at all** — everything here is manual |

The last row is the real gap. Nothing on this page runs by itself, so any of it can
silently regress. **Converting §10 into an automated integration test is now the
highest-value next investment** — the sender that used to hold that place is done.
