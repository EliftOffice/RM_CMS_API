# RM_CMS — Testing Guide

How to exercise everything that is actually built, as of **2026-08-15** (branch
`feature/auth_security`). Companion document: [SYSTEM_WORKFLOW.md](../architecture/SYSTEM_WORKFLOW.md).

> **Since this was written (as of 2026-09-07):** the scope table in §0 and §11 "What you
> cannot test yet" are now out of date on two points — the Pastor dashboard is built and
> verified (`GET /api/dashboards/pastor`, `wwwroot/pages/dashboard/pastor.html`), and
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
| Frontend pages | ⛔ 16 of 17 dead | Only `admin/accounts.html` and login work |

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

`wwwroot/pages/intake/record-visitor.html` + `assets/js/record-visitor.js`.
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
| I9 | DATA_ENTRY cannot reach Accounts | Navigating to `admin/accounts.html` redirects back to intake | ✅ |
| I10 | Missing first name / mobile | Inline error, field marked `aria-invalid`, focus moved | — |
| I11 | Follow-up unchecked | Person saved, **no** case opened | — |
| I12 | Case fails after person saved | Reported as a **partial success**, not "nothing saved" — otherwise the operator enters the visitor twice | — |

> **Test data left behind:** P0014 "Anitha Reddy" and P0015 "Anitha Sharma" (both
> `9876512345`) plus their cases, created through the UI during verification. Harmless in
> the dev dataset — delete if you want a clean slate.

## 4.5 User management — ✅ verified 2026-08-15

`admin/users.html` (role tabs) and `admin/add-user.html`. Admin only.

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

`admin/settings.html`. Admin only. Replaces the dead `/api/systemconfig` and
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

`care/my-assignments.html`. The screen volunteers use daily.

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

## 4.9 Areas — ✅ verified 2026-09-07

`Modules/Areas`, `admin/areas.html` + `assets/js/areas.js`, and the shared
`assets/js/area-picker.js` used by BOTH the intake and add-user screens.

**What it is.** A controlled locality list, campus-scoped like `team`. It replaces
free-text typing of the same neighbourhood: `person.locality` is still there and is still
written for someone from out of town, but anyone local gets `person.area_id`. The point is
that a volunteer and a visitor two streets away land on the SAME row, which is what makes
"who lives near this person" answerable at all.

**The find-or-create is on the server, not in the browser.** The picker sends the id it
holds AND the text in the box; `AreaService.ResolveAsync` prefers the id, falls back to
the name, and creates the area when nothing matches. Doing it as two calls from the client
would race — two operators typing the same new area at once, one of them getting a
duplicate-key failure instead of a save.

Verified against `cms_api_db` with the real dataset. Every fixture below was removed
afterwards and the row counts returned to baseline (178 people / 49 accounts / 35
volunteers / 1 area).

| # | Test | Expect | Status |
|---|---|---|---|
| A1 | Migration `005_areas.sql` | `area` + `person.area_id` created; one area backfilled per distinct `person.locality` per campus | ✅ 1 area, 2 people filed |
| A2 | Re-run the migration | No error, no duplicate rows, the grant setting not reset | ✅ |
| A3 | `GET /api/areas/options?q=` | Active areas at the caller's campus, id and name only — never the counts | ✅ |
| A4 | Type-ahead is whitespace/case blind | `kurnool road` finds `Kurnool Road` | ✅ |
| A5 | Create from management | `'  Kurnool   Road  '` stored as `Kurnool Road` | ✅ |
| A6 | Duplicate refused | `kurnool road` → "'Kurnool Road' already exists at this campus." | ✅ |
| A7 | Rename onto another name | Refused with a sentence, not a duplicate-key 500 | ✅ |
| A8 | Stale `rowVersion` | "This record was changed by someone else." | ✅ |
| A9 | Retire an area in use | Allowed, and the message names how many people still hold it | ✅ |
| A10 | Retired area leaves the picker | `options` returns it no longer | ✅ |
| A11 | Re-typing a retired name | Reuses that row — does NOT create a second, and does NOT silently reactivate it | ✅ |
| A12 | Campus scoping | The picker at Pernamitta offers none of Ongole's areas; changing the campus clears the field | ✅ |

### 4.9.1 The intake screen's "Where they live"

| # | Test | Expect | Status |
|---|---|---|---|
| A13 | "Lives locally" ticked (the default) | ONLY the Area field is shown; address / locality / postal code are hidden | ✅ |
| A14 | Area is required when local | Browser refuses, and so does `POST /api/people` — the rule is on the intake **controller**, not in `PeopleService`, because creating a user goes through the service with no address at all | ✅ |
| A15 | New area typed | Area created, then the person filed against it, in one request | ✅ |
| A16 | Same area typed differently | Second person gets the SAME `area_id`; no second row | ✅ |
| A17 | "Lives locally" unticked | Address / locality / postal code return, exactly as before areas existed; no area recorded | ✅ |
| A18 | Toggling clears the hidden half | A full address typed before unticking cannot be submitted invisibly, and vice versa | ✅ |
| A19 | Unknown `areaId` | "That area no longer exists. Choose or type it again." | ✅ |

### 4.9.2 The add-user screen

| # | Test | Expect | Status |
|---|---|---|---|
| A20 | Area field shown for VOLUNTEER only | Hidden and **cleared** when the role changes to anything else | ✅ |
| A21 | New volunteer with a new area | Area created; `area_id` lands on the PERSON row, not the volunteer row | ✅ |
| A22 | Existing person given a volunteer role | Their area is recorded; nothing else on their record changes | ✅ |
| A23 | A rejected area does not fail the creation | Reported as a note on `UserChangeResultDto`, surfaced on the screen after the reset | — |

### 4.9.3 The management grant

An administrator always manages areas. A DATA_ENTRY operator does so only when
`area.manage_by_data_entry` is on — the same shape as `team.manage_by_*`.

| # | Test | Expect | Status |
|---|---|---|---|
| A24 | Setting appears in the admin panel | Rendered automatically under a new "Area" group — the settings screen is generic | ✅ |
| A25 | DATA_ENTRY, grant OFF | `/api/areas/access` → `scope: NONE`; list and create refused, naming the setting | ✅ |
| A26 | DATA_ENTRY, grant ON | `scope: OWNCAMPUSONLY`; list and create succeed | ✅ |
| A27 | Reading the picker is never gated | `options` works for DATA_ENTRY with the grant off — typing a new area is part of recording a visitor, not management | ✅ |
| A28 | Granted operator is campus-bound | `canSeeAllCampuses: false`; the list and any create are forced to their own campus whatever they send | ✅ |
| A29 | The nav link explains itself | Shown to ADMIN and DATA_ENTRY; with no grant the page replaces itself with the reason and names the setting | ✅ |

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
`wwwroot/pages/dashboard/team-lead.html`.

**The UI is unchanged on purpose.** Team leads already work in this screen, so only the
data layer moved: `team-lead-dashboard.js` now makes one call to the new endpoint instead of four to
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

**Manual assignment** — `care/assign-cases.html`

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
raw error. The duplicate copy of this handler inside `team-lead-dashboard.js` was removed.

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
and the modal on `dashboard/team-lead.html`.

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

## 8.6 People pipeline — visitors only, and the visitor history — ✅ verified 2026-09-07

`Modules/Pipeline`, `pages/care/pipeline.html` + `assets/js/pipeline.js`, and the new
`pages/care/visitor-journey.html` + `assets/js/visitor-journey.js`.

### The bug that was fixed

The pipeline listed **every `person` row**. Staff are person rows too, so all 54
volunteers, team leads, pastors and intake operators appeared in a funnel about visitors
— 30% of 178 rows — nearly all of them counted under "Not started", which is not a stage
they were ever on.

The exclusion (`VisitorsOnly` in `PipelineRepository`) is a login OR a volunteer record,
the same definition `CampusRepository` already used, so "visitors" now means the same
number on both screens. It is applied to the **list, the count and the summary** — leaving
it off any one of the three would make the funnel disagree with the table under it.

| # | Test | Expect | Status |
|---|---|---|---|
| P1 | Total after the fix | 178 → **124**, matching `SELECT COUNT(*) … NOT EXISTS(user_account) AND NOT EXISTS(volunteer)` | ✅ |
| P2 | No staff in the payload | Every returned `personId` cross-checked against the database: **0 staff** | ✅ |
| P3 | No visitor lost | All 124 real visitors present | ✅ |
| P4 | Funnel agrees with the list | `summary.totalPeople` == `totalCount` == 124 | ✅ |
| P5 | Staff with an open case | Drops off this screen. Their case is still on the dashboard and assignment screens, which read `care_case` — one such row exists in the live data | ✅ |

### The visitor history

`GET /api/pipeline/{personId}` — person-centric on purpose. `GET /api/cases/{id}` answers
one case; somebody who visited, went quiet and came back has two, and reading them
separately loses the shape of the relationship. Returns the profile, every case, per-person
stats, and one merged timeline of contacts, escalations, assignments and notes.

| # | Test | Expect | Status |
|---|---|---|---|
| P6 | Timeline merges every source | Contacts, escalations, assignments, case opened/closed and notes in one list, newest first | ✅ 16 events on a real record |
| P7 | Notes from BOTH sources | Case notes and person-level notes; a person note outlives the case it was never attached to | ✅ fixture |
| P8 | Private notes excluded | `is_private = 1` never appears — surfacing them would change what "private" meant after the fact | ✅ by query |
| P9 | Do-not-contact banner | Red, full width, above everything else | ✅ fixture |
| P10 | Empty record | "No follow-up was ever opened", stats read `—` / `never` rather than `0` | ✅ fixture |
| P11 | Timeline filters | Contacts / Concerns / Notes / Assignments; kinds with no events are not offered | ✅ |
| P12 | Back preserves the list | Stage, search and page ride along in `?back=` and are restored | ✅ |
| P13 | Staff refused | Same "That visitor was not found." as a missing person — no enumeration | ✅ |
| P14 | Unknown ULID | Same message | ✅ |
| P15 | Malformed id | 400 | ✅ |
| P16 | Unauthenticated | 401 | ✅ |
| P17 | Team lead scope | The `EXISTS(care_case … team_id IN @TeamIds)` clause partitions the 124 into 25 in-scope / 99 refused for a real lead | ✅ |

### A data problem this surfaced

**43 `care_interaction` rows have `made_contact` disagreeing with their outcome's
`care_outcome.contact_made`** — e.g. `made_contact = 1` on a `NO_ANSWER`. A migration
artifact. It rendered as "Spoke with them" tagged "No answer" on the same line, and made
one visitor's success rate read 100% when it was 86%.

The timeline and the stats both now derive "did we reach them" from
`COALESCE(o.contact_made, i.made_contact)` — the outcome is the definition of what
happened, the flag is only the fallback. **The underlying rows were not corrected**, and
anything else reading `made_contact` directly still sees the inconsistent value. Worth a
one-off reconciliation.

---

## 8.7 Website enquiries — ✅ verified 2026-09-08

`Modules/WebEnquiries`, migration `006_web_enquiries.sql`, `pages/admin/web-enquiries.html`.

The public website's only way in, and the queue a **Website Coordinator** works. Submissions
land in `web_enquiry` as untrusted input — they are **not** pastoral records until a person
decides they are. See [`../deployment/WEBSITE.md`](../deployment/WEBSITE.md).

### Public submission — `POST /api/public/enquiries` (anonymous)

| # | Test | Expect | Status |
|---|---|---|---|
| E1 | BSG registration with the site's real fields | 200, `responseType 0`, reference `W0001` | ✅ |
| E2 | Prayer request with no name or contact | Accepted — anonymous prayer is allowed on purpose | ✅ |
| E3 | Empty prayer request | Refused, "Please write your request before sending." | ✅ |
| E4 | Registration with no name | Refused | ✅ |
| E5 | Registration with a bad mobile | Refused with the 10-digit rule | ✅ |
| E6 | `+91 98765 43211` | Accepted; stored as typed, normalised to `9876543211` | ✅ |
| E7 | Bad email | Refused | ✅ |
| E8 | Unknown `formType` | Refused **without echoing the valid values** — an unknown type is a bug or a probe, and listing them helps the probe more | ✅ |
| E9 | Honeypot filled | **Accepted** and flagged; hidden from the default queue. Rejecting it would tell the bot which field to leave blank | ✅ |
| E10 | 16 rapid submissions from one IP | 1-10 accepted, 11-12 throttled (200 + `responseType 1`), 13+ `429` from the rate limiter | ✅ |
| E11 | Reference codes | `W0001`…`W0004`, generated inside the insert transaction | ✅ |

### The coordinator queue

| # | Test | Expect | Status |
|---|---|---|---|
| E12 | Unauthenticated `GET /api/web-enquiries` | 401 | ✅ |
| E13 | `WEB_COORDINATOR` reads the queue | 200 | ✅ |
| E14 | Same account on `/api/people`, `/api/pipeline`, `/api/volunteers`, `/api/admin/users`, `/api/admin/settings` | **403 on all five** — the role is confined to this one queue | ✅ |
| E15 | Sign-in lands them on the queue | `/pages/admin/web-enquiries.html`; nav shows only "Website" | ✅ |
| E16 | Spam hidden by default | 3 of 4 listed; the summary still counts all 4 | ✅ |
| E17 | Mobile matching an existing person | `matchingPeople: 1` surfaced before any decision | ✅ |
| E18 | `IN_REVIEW` with no note | Allowed — picking something up needs no explanation | ✅ |
| E19 | `ACTIONED` with no note | Refused, "Add a short note saying what was done." | ✅ |
| E20 | Stale `rowVersion` | Refused as a concurrent edit | ✅ |
| E21 | Back to `NEW` | Refused | ✅ |
| E22 | Unknown status | Refused | ✅ |
| E23 | Role creation through Add a user | Grants `WEB_COORDINATOR`; sits off the pastoral ladder (rank 15, beside DATA_ENTRY) | ✅ |

All fixtures removed afterwards — `web_enquiry` back to 0 rows, `person` back to the 178
baseline, the test coordinator account deleted.

### Bug found and fixed on 2026-09-08 — a role grant could fail in silence

Reported as "undefined role" when creating a Website Coordinator. Three faults in a chain:

1. **`schema.sql` did not seed `WEB_COORDINATOR`.** It was added to migration `006` but not
   to the full schema — and a fresh database is built from `schema.sql`, which is exactly
   what `deployment/COOLIFY.md` tells you to load. So any new deployment had the code and
   the screens but no `app_role` row.
2. **`user_role.role_code` has a foreign key to `app_role.code`**, so the grant violated it.
3. **The insert was `INSERT IGNORE`**, and MySQL downgrades a foreign-key violation under
   `IGNORE` to a warning. The grant was skipped in silence, `ExecuteAsync` returned 0, and
   the caller was told it had worked.

The result was an **active account with no roles at all**, reported as a success — which
then fails at the login screen with "your account has no assigned role" and nothing
anywhere explaining why.

| # | Test | Expect | Status |
|---|---|---|---|
| E24 | Reproduce: no `app_role` row, create the user | Old code: `responseType 0`, "is now Website Coordinator", account active, **0 `user_role` rows** | ✅ reproduced |
| E25 | Same, after changing `INSERT IGNORE` → `ON DUPLICATE KEY UPDATE` | Refused: "Unable to create the account." No account created | ✅ |
| E26 | With the role seeded | Created, `roles: ['WEB_COORDINATOR']`, and the account signs in carrying it | ✅ |
| E27 | Repeated grant still idempotent | `ON DUPLICATE KEY UPDATE user_account_id = user_account_id` — a no-op, which is what `IGNORE` was there for | ✅ |

`INSERT IGNORE` was replaced at **all three** `user_role` insert sites, so this class of
silent failure is closed for every role, not only the new one.

**Left alone:** a failed user creation still leaves the `person` row behind, because the
person is created first through `IPeopleService` and the account failure does not roll it
back. Pre-existing, out of scope here, worth a look later.

Also fixed, found while investigating: `WEB_COORDINATOR` was missing from the label maps in
`users.js` and `accounts.js` (it rendered as the raw code), had no filter tab on the Users
screen, and its insertion into the `add-user.js` list had silently moved the pre-selected
default off Volunteer — that default now matches on the role code rather than an array
index, so it cannot drift again.

### Wired to the React site — ✅ verified 2026-09-08

`C:\RM_Website_React` (React 19 + Vite + TS) submits through `src/lib/forms.ts`. Run
end-to-end against the local CMS with the site on `:5173` and the API on `:5055`.

| # | Test | Expect | Status |
|---|---|---|---|
| E28 | CORS preflight from the site's origin | `204` with `Access-Control-Allow-Origin` for `http://localhost:5173` | ✅ |
| E29 | Prayer form at `/prayer` | Lands as `PRAYER_REQUEST` with name, mobile and `sourcePage: /prayer` | ✅ |
| E30 | BSG form at `/bsg` | `MINISTRY_REGISTRATION` with city, street, landmark, referrer, `extra.ministry` | ✅ |
| E31 | Join-team form at `/ministries/worship-team` | `TEAM_REGISTRATION` with `extra.team` and `extra.teamName` | ✅ |
| E32 | Visit form at `/contact` | `PLAN_VISIT` with `extra.visiting` and `extra.partySize` | ✅ |
| E33 | Non-ASCII survives the round trip | `partySize: "3–4 people"` — en-dash intact through JSON, MySQL and back | ✅ |
| E34 | A refusal is not shown as success | Site was pointed at a server that did not yet know `TEAM_REGISTRATION`; the form surfaced the refusal instead of its success state | ✅ |
| E35 | `npm run lint`, `typecheck`, `build` | All clean | ✅ |

`TEAM_REGISTRATION` was added to `WebFormTypes` for the site's "join a serving team" flow,
which is a different intention from a Bible Study Group registration — one is asking to be
cared for, the other is offering to serve — so they are separate form types rather than one
with a flag.

**E34 is worth keeping.** It happened by accident: the API was running a build that predated
`TEAM_REGISTRATION`, so the submission was refused. The site showed the refusal, which is
exactly what `src/lib/forms.ts` was rewritten to do — the original checked `response.ok`
alone and would have shown "your request has been received" for a rejected prayer request.

All test submissions deleted afterwards; `web_enquiry` back to 0 rows.

### Not built yet

What happens *after* a coordinator reads an enquiry is a workflow still being decided, so
no conversion action exists. `web_enquiry.linked_person_id` is in place, surfaced through
the API and shown on the screen, ready for it.

---

## 8.8 Church events — ✅ verified 2026-09-10

`Modules/Events`, migration `007_events.sql`, `pages/admin/events.html`, and on the
website `src/lib/events.ts` + `src/hooks/useEvents.ts`.

The calendar the public site lists. Events were a hard-coded array in the website's own
source; the CMS owns them now. See [`../deployment/WEBSITE.md`](../deployment/WEBSITE.md) §4a.

### Admin

| # | Test | Expect | Status |
|---|---|---|---|
| V1 | Create with local time 08:00 | Stored `02:30` UTC, read back as `08:00` local, zone `Asia/Kolkata` | ✅ |
| V2 | New event's status | Always `DRAFT`, never published on create | ✅ |
| V3 | Slug from title | `Good Friday Service` → `good-friday-service`, auto-filled in the editor | ✅ |
| V4 | Duplicate slug | Refused, naming the address already in use | ✅ |
| V5 | End before start | Refused, "The event cannot end before it starts." | ✅ |
| V6 | *(retired)* Unknown image slug | The image dropdown is gone — see §8.8a. Superseded by P1–P8 | — |
| V7 | Unparseable date | Refused | ✅ |
| V8 | Title with no Latin characters (Telugu) | Refused, asking for a web address to be typed — an empty slug would collide and break `/events/` | ✅ |
| V9 | Unknown campus | Refused | ✅ |
| V10 | Length rules | Readable messages, not the framework's "The field Venue must be a string with a minimum length of 2" | ✅ |
| V11 | Publish / unpublish / cancel | Each moves status and stamps `published_at`; only the moves that make sense are offered per row | ✅ |
| V12 | Concurrent edit | Refused on a stale `rowVersion` | ✅ |

### Public feed and the website

| # | Test | Expect | Status |
|---|---|---|---|
| V13 | Draft in `GET /api/public/events` | **Absent.** Feed empty while the only event was a draft | ✅ |
| V14 | Published event | `date: "2027-04-02T08:00:00+05:30"` — offset-carrying ISO, the shape the site's `ChurchEvent` documents | ✅ |
| V15 | Description | Blank-line text split into a paragraph array | ✅ |
| V16 | `/events` on the site | Both CMS events render with images, dates and venues | ✅ |
| V17 | `/events/<slug>` | Renders; Schema.org `startDate`/`endDate` carry the right offsets | ✅ |
| V18 | **Unknown slug still redirects** | `/events/no-such-event` → `/events`, *after* loading rather than during it | ✅ |
| V19 | Homepage band | Shows upcoming events; hidden entirely while loading and when empty | ✅ |
| V20 | Cancelled event | Shows `CANCELLED` and a struck-through title on the card; `EventCancelled` in the Schema.org markup | ✅ |
| V21 | `npm run verify` | typecheck, lint, prettier and build clean on every file touched | ✅ |

### The trap this feature nearly walked into

`EventPage` did `if (!event) return <Navigate to="/events" />`. Events arrive over the
network now, so `event` is undefined for the first moment of **every** visit — that
redirect would have bounced anyone opening an event link straight to the index before the
fetch answered, and the link would have looked broken while working perfectly. The loading
check has to come first, and V18 is the test that holds it there.

### Also fixed while here

**`schema.sql` was missing `web_enquiry`.** The table was added in migration `006` but
never folded into the full schema, and a fresh database is built from `schema.sql` — so a
new deployment would have had the website-enquiry code and screens with no table behind
them. The same omission that hid the `WEB_COORDINATOR` role in §8.7. Both `web_enquiry`
and the new `church_event` are in `schema.sql` now, and it was verified by building a
database from it alone: **37 tables, all six roles seeded**.

---

## 8.8a The event poster — ✅ verified 2026-09-10

Migration `008_event_poster.sql`, the poster routes on `Modules/Events`, the upload field
on `pages/admin/events.html`, and on the website `src/lib/events.ts`, `EventCard.tsx`,
`PageHero.tsx` and `EventPage.tsx`.

**What changed.** The picture was an `image_slug` chosen from a dropdown of about twenty
stock plates baked into the website at build time. Staff could not use the poster actually
designed for the event — the one on the flyer and the WhatsApp forward — so they picked the
least wrong stock photograph and two unrelated events routinely showed the same one. The
field is now a file upload, one poster per event, bytes held in `church_event_poster`.

| # | Test | Expect | Status |
|---|---|---|---|
| P1 | Upload a real PNG | Accepted; `hasPoster` true, `posterUrl` set, `rowVersion` moves | ✅ |
| P2 | **A non-image renamed `.png` and sent as `image/jpeg`** | **Refused.** The format is read from the file's own first bytes, so neither the name nor the declared type gets it through | ✅ |
| P3 | `GET /api/public/events/{id}/poster` anonymously | 200, `Content-Type: image/png`, bytes **identical** to what was uploaded | ✅ |
| P4 | Headers on that response | `Cross-Origin-Resource-Policy: cross-origin`, `Cache-Control: public, max-age=86400, immutable`, `Last-Modified` | ✅ |
| P5 | Headers on every OTHER response | `Cross-Origin-Resource-Policy: same-origin` — the opt-out is one endpoint, not a weakening of the default | ✅ |
| P6 | Draft's poster in the public feed | Feed empty while the event was a draft; poster URL appears only once published | ✅ |
| P7 | `DELETE .../poster` | `hasPoster` false, and the serving endpoint then 404s | ✅ |
| P8 | Re-running migration `008` | Clean: "poster columns already present", "image_slug already dropped" | ✅ |

### On the website

| # | Test | Expect | Status |
|---|---|---|---|
| P9 | `/events` against the live API, site on `:4173` and API on `:5055` | The card's `<img>` loads and decodes the cross-origin poster — `complete: true`, natural size read back from the file | ✅ |
| P10 | Browser console on that page | No errors. In particular no CORP or CORS message | ✅ |
| P11 | `tsc --noEmit`, eslint, `npm run build` | All clean | ✅ |

> **Testing note.** The Browser pane reports a zero-height viewport, so `loading="lazy"`
> never fires and the poster reads as `complete: false` no matter how long you wait. That
> is the harness, not the page. Flip the element to `loading="eager"` and re-assign its
> `src` to force the fetch, or load the URL through `new Image()`.

### Why the bytes are in the database

Coolify rebuilds the container from the image on every push, so a file written into
`wwwroot` survives exactly until the next deploy — every uploaded poster would vanish and
the events would quietly go back to having no picture. Avoiding that with files needs a
mounted volume, and forgetting to configure one fails **invisibly** until a visitor looks.
A row is backed up and restored with everything else.

### The trade-off, stated

An uploaded poster skips the website's build-time image pipeline: no AVIF or WebP
variants, no `srcset`, no blurred placeholder, and the original is served at whatever size
it was uploaded. The card and hero render it as a plain `<img>` in a fixed-ratio box so the
layout still does not shift. If posters turn out to be multi-megabyte phone photographs in
practice, the fix is server-side resizing on upload — not going back to a dropdown that
could not show the church's own poster.

### Known trade-off

Individual events are no longer in `sitemap.xml`, because it is generated at build time and
events are published after it. `/events` is still listed and links to every event, so they
remain crawlable. `scripts/generate-seo.mjs` says so where the loop used to be, rather than
silently iterating an empty array.

---

## 8.9 Signing in with a mobile number alone — ✅ verified 2026-09-10

Migration `009_passwordless_login.sql`, `IdentityService.GetLoginMethodAsync` and
`SetPasswordlessLoginAsync`, `POST /api/auth/login-method`,
`PUT /api/admin/accounts/{id}/passwordless`, and the rewritten `login.js` / `accounts.js`.

**Why it exists.** Some of the people who use this system cannot read. A password prompt
does not make their account safer — what actually happens is that somebody literate types
it for them, so the credential ends up shared or written down. An administrator marks
those accounts individually.

**What it costs, and this is not hedged:** for such an account the mobile number IS the
credential, and mobile numbers are on posters and in group chats. Anyone who knows the
number can sign in as that person and read whatever that person can read, which here means
pastoral records and prayer requests. It is off by default (`NOT NULL DEFAULT 0`), granted
one account at a time, and audited both ways.

### The switch

| # | Test | Expect | Status |
|---|---|---|---|
| L1 | Column default | Every existing account came out `allows_passwordless_login = 0` | ✅ |
| L2 | Grant to a team lead | Accepted; message says what it now allows | ✅ |
| L3 | **Grant to an administrator** | **Refused.** "Anyone who knew the number would hold the whole system." | ✅ |
| L4 | Grant clears `must_change_password` | Yes — otherwise the account is sent to a password screen it cannot use and stops dead | ✅ |
| L5 | Sessions on change | Revoked either way, so revoking the grant takes effect immediately rather than whenever the open session ends | ✅ |
| L6 | Audit | `PASSWORDLESS_ENABLED`, `PASSWORDLESS_DISABLED` and `PASSWORDLESS_LOGIN` rows written | ✅ |
| L7 | `canAllowPasswordlessLogin` in the DTO | `false` for an administrator, `true` for a volunteer, so the screen greys the control rather than offering a button that always fails | ✅ |
| L8 | Re-running migration `009` | Clean: "already present" for both column and index | ✅ |

### Signing in

| # | Test | Expect | Status |
|---|---|---|---|
| L9 | Probe before the grant | `requiresPassword: true` | ✅ |
| L10 | **Probe for a number with no account** | `requiresPassword: true` — identical to a real account, so this cannot be used to find out who has one | ✅ |
| L11 | Empty password before the grant | `401`, generic "Invalid username or password." | ✅ |
| L12 | Probe after the grant | `requiresPassword: false` | ✅ |
| L13 | Empty password after the grant | `200`, token issued, `mustChangePassword: false` | ✅ |
| L14 | After revoking | Probe back to `true`; number-only sign-in `401` again | ✅ |

### The screen

| # | Test | Expect | Status |
|---|---|---|---|
| L15 | First paint | Mobile number only. Password section hidden, **and there is no Sign in button anywhere** | ✅ |
| L16 | A number that needs a password | Password section revealed, focus moved into it, "Press Enter to sign in." shown in place of the button | ✅ |
| L17 | Editing the number afterwards | Password section hides again and the typed password is cleared — those digits may be a different account now | ✅ |
| L18 | A number granted "number only" | Signed straight in and landed on the team lead dashboard, nothing else typed | ✅ |

### A defect caught before shipping

The probe was first put on the **login** rate limit, which is 5 requests per 5 minutes.
Every successful sign-in would then have spent **two** of those five, and one mistyped
number would have locked somebody out before their first real attempt. It has its own
bucket now (`rl-login-method`, 20 per 5 minutes); the endpoint carries no credential, so
there is nothing there to brute-force.

### Still open, and pre-dating this work

`LoginPartitionKey` is **IP-only**, and behind Coolify every request arrives from the
proxy — so in production one bucket is shared by everybody signing in. That is deliberate
(`X-Forwarded-For` is caller-supplied, and trusting it would let an attacker mint a fresh
bucket per request), but it means a busy Sunday could hit the ceiling. Worth revisiting
separately; this feature did not create it and does not make it worse now that the probe
has its own bucket.

### Testing note

Both test servers were restarted between runs to clear the in-memory rate limiter — a
fixed-window limiter has no other reset. The account used for L2–L14 belongs to the real
migrated dataset; its username and every column touched were captured beforehand and
restored afterwards, and the browser test at L18 ran against a placeholder number so no
member's real number was ever displayed.

---

## 8.10 Editable Telegram messages — ✅ verified 2026-09-11

Migration `010_message_templates.sql`, `Modules/MessageTemplates`, the rewired
`NotificationComposer` and `TelegramLinkService`, and
`pages/admin/telegram-messages.html`.

**Why it exists.** The wording of every Telegram message was a string literal in C#, so
changing a word meant a code change and a deployment. In practice the wording never
changed, because the people who know how it should read are pastors rather than whoever
can rebuild the application.

**The shape.** A row in `telegram_template` exists only for a scenario somebody has
edited. Every scenario has a default in `TelegramTemplates`, and no row means "use it" —
so a fresh database sends correct messages with the table empty, a scenario added in code
works before anyone opens the screen, and Reset is a `DELETE` rather than a second copy
of the wording to keep in step.

| # | Test | Expect | Status |
|---|---|---|---|
| T1 | The catalogue | 11 scenarios listed, grouped, none customised on a clean database | ✅ |
| T2 | **A placeholder the scenario does not have** | **Refused** — "This message does not have {{Name}}." Caught on save, not discovered as a gap in a volunteer's message | ✅ |
| T3 | A valid edit | Saved, marked customised, and attributed to the administrator who made it | ✅ |
| T4 | Preview | Body rendered with the sample values from the catalogue | ✅ |
| T5 | **End to end through a stub Telegram server** | The customised wording reached the wire with the recipient's own first name and full name substituted — no `{{` left in the sent text | ✅ |
| T6 | Reset | Row deleted, scenario back on the built-in wording | ✅ |
| T7 | Re-running migration `010` | Clean; the table is empty by design, nothing seeded | ✅ |

### Placeholders

`{{RecipientName}}`, `{{RecipientFirstName}}`, `{{ChurchName}}` and `{{SiteUrl}}` are
available in every scenario — these are the recipient's own details, and they are what
make a message specific to the person receiving it rather than a broadcast. Each scenario
adds its own: the escalation templates carry the person, reason, tier and how long it has
waited; the assignment template carries the person and reference.

### The escaping rule, which is the whole point of `TemplateRenderer`

Messages go with `parse_mode=HTML`, so the **template** is markup — a pastor may write
`<b>` and expect bold. The **values** are not: they are names and localities, and a name
containing an ampersand makes Telegram reject the entire message rather than render it
plainly. So the template passes through untouched and every substituted value is escaped.
Getting this backwards either way breaks something — escaping the template shows tags as
literal text, and not escaping the values loses real messages to a person called "A & B".

Three values are genuinely markup the server composes (the escalation heading, the
team-lead line, the safeguarding line). They go through a separate `rawValues` channel
that is deliberately awkward to reach, with the names inside them escaped at the point
they are put in.

### Two things this turned up

**`CASE_ASSIGNED` never fired.** The notification type existed and the composer had no
branch for it, so it would have been closed as "No message template" had anything ever
queued one — and nothing did. Assignment now queues it and the composer renders it, so a
volunteer learns a follow-up is theirs without checking the screen. Queued, never sent
inline: a Telegram outage must not roll back an assignment. Every failure in that path is
swallowed and logged, because the case is already assigned by the time it runs and
throwing would report an error for work that succeeded.

**`{{PersonPhone}}` is offered but is not in the default wording.** Putting somebody's
phone number into a Telegram message is a decision the church should make deliberately —
Telegram history outlives the follow-up — so the placeholder exists and the default does
not use it.

### Testing note

T5 used a local HTTP stub standing in for the Telegram Bot API, with
`Telegram__ApiBaseUrl` pointed at it, which is how the notification sender was proved
originally. The captured message was checked for the substituted name and then masked
before being printed, so no member's real name was displayed. Both templates edited during
the test were reset afterwards and `telegram_template` is empty again.

---

## 8.11 Confirming a sign-in on Telegram — ✅ verified 2026-09-11

Migration `011_login_telegram_verification.sql`, `login_challenge`, the verification
block in `IdentityService`, the two `verify/*` routes, `callback_query` handling in the
Telegram webhook, and the third step on the login screen.

**What it does.** With `telegram.verify_on_login` switched on, a correct credential is no
longer enough: the person also taps a button in Telegram. It applies to password accounts
and to number-only accounts alike, which is the point — a number written on a poster no
longer gets anybody in, because the phone has to be in their hand.

**Two secrets, doing different jobs.** The browser holds a challenge id that identifies
the pending sign-in and can only ask "has it been approved yet?". The token that actually
approves it lives in the Telegram button and never reaches the browser. So the browser can
wait for approval but cannot grant it to itself.

| # | Test | Expect | Status |
|---|---|---|---|
| V1 | Setting off | Sign-in behaves exactly as before | ✅ |
| V2 | **On, but the account has no usable Telegram link** | **Signed in anyway**, and the reason logged. Fails OPEN on purpose — see below | ✅ |
| V3 | On, with a usable link | `requiresTelegramVerification: true`, a challenge id, and **no access token and no cookie** | ✅ |
| V4 | The prompt | Message rendered from the `LOGIN_VERIFICATION` template with two buttons: confirm and refuse | ✅ |
| V5 | Poll before the tap | `WAITING`, no session | ✅ |
| V6 | The tap, delivered as Telegram delivers it | Webhook accepts the `callback_query` and approves | ✅ |
| V7 | Poll after the tap | `APPROVED`, access token issued, refresh token **only** in the `Set-Cookie` header and `null` in the body | ✅ |
| V8 | **Polling again** | `FAILED`, no second session. A challenge is good for exactly one | ✅ |
| V9 | **A tap from a different chat** | Refused; the challenge stays `WAITING`. The chat that presses must be the chat that was asked | ✅ |
| V10 | "This was not me" | Challenge `DECLINED`, poll returns `FAILED`, and `LOGIN_VERIFY_DECLINED` written | ✅ |
| V11 | Audit trail | `LOGIN_VERIFY_REQUIRED` on each attempt, `LOGIN_VERIFIED` on success, `LOGIN_VERIFY_DECLINED` on refusal | ✅ |

### Why it fails open

`StartTelegramVerificationAsync` returns "let them in" when the setting is off, when the
account has no usable Telegram link, **and when anything throws**. The alternative is a
second factor that bricks every account the moment Telegram is misconfigured — including
the administrator who would have to fix it. `telegram.require_linking` is the setting that
makes the second case rare, and the new setting's description says to use the two together.

### A bug the schema caught

The first run failed on `ck_login_challenge_window` — `expires_at > created_at`. The cause:
`created_at` was left to `DEFAULT CURRENT_TIMESTAMP(3)`, which stamps the **MySQL server's
local time**, while the application writes UTC. On a server running in IST that puts
`created_at` five and a half hours ahead of an `expires_at` three minutes in the future, so
every challenge was rejected. `created_at` is now passed explicitly from the application.
The same trap is documented on `church_event.published_at`; the CHECK constraint is what
turned it into a loud failure instead of a feature that silently never triggered.

### A design note on the button

A callback button, not a `t.me` deep link. A deep link only re-sends its start payload for
a **new** chat, so for anybody who has already linked — which is everybody this applies to,
since linking is what gives them a chat id — tapping one just opens the conversation and
nothing reaches the webhook. Callback buttons work in place, every time.

### Testing note

The Telegram Bot API was stubbed locally and the button press was delivered to the webhook
as Telegram delivers it, with the shared secret header. The administrator's own Telegram
contact was temporarily made usable for V3 onwards and put back afterwards; the setting is
off again, `login_challenge` is empty, and the test audit rows were removed. The recipient
name was masked before printing.

---

## 8.12 A global error page — ✅ verified 2026-09-11

`Middleware/ErrorPages.cs`, `wwwroot/pages/error.html`, and the handlers in `Program.cs`
that used to write JSON unconditionally.

**What was there before: nothing.** Verified by running the application with
`ASPNETCORE_ENVIRONMENT=Production` and requesting missing pages. Every one came back
with a status and an **empty body**, so the browser fell back to its own blank "cannot
reach this page" — which reads as the whole site being down rather than one address being
wrong. There was no `UseStatusCodePages`, no `UseExceptionHandler` and no error page
anywhere in `wwwroot`.

**The rule.** Anything under `/api` always gets `application/problem+json`, even from a
browser address bar — a client that parses responses must never be handed markup because
an Accept header was broad. Everything else that explicitly asks for `text/html` gets the
page. `fetch` defaults to a wildcard Accept, so page scripts stay on the JSON path.

| # | Test | Expect | Status |
|---|---|---|---|
| E1 | Browser request to an unknown path | HTML page, ~4.7 KB, showing the status and a heading | ✅ |
| E2 | Browser request to a missing page under `/pages` | Same | ✅ |
| E3 | Browser request to a missing static asset | Same | ✅ |
| E4 | `/api/...` with `Accept: application/json` | `application/problem+json`, unchanged | ✅ |
| E5 | **`/api/...` with `Accept: */*`** (what `fetch` sends) | JSON, not markup | ✅ |
| E6 | **429 keeps `Retry-After`** | Present, `300` | ✅ |

### Two things worth knowing

**Unknown paths answer 401, not 404.** The fallback authorization policy denies anonymous
callers before routing can decide the path does not exist. That is deliberate and was left
alone: answering 401 for everything unknown tells a prober nothing about which paths exist.
The page now says "Please sign in" rather than showing nothing at all.

**`Response.Clear()` drops headers as well as the body.** The rate limiter sets
`Retry-After` before delegating to the renderer, and that value is the only actionable
thing a throttled caller gets. It is captured and re-applied across the reset. The security
headers are unaffected because `SecurityHeadersMiddleware` adds them in an `OnStarting`
callback, which runs later.

The page is deliberately self-contained — no stylesheet, no script, no font. It is what
people see when something is already broken, and every external file it depended on would
be one more thing that could be the reason it fails to render. If `error.html` is missing
entirely, a plain built-in fallback is used rather than reverting to a blank response.

---

## 8.13 Data entry operators can correct a record — ✅ verified 2026-09-11

`GET /api/people/{id}/intake`, the policy on `PUT /api/people/{id}`, and the correction
mode on the intake screen.

**Why.** Data entry operators are the ones who create these records, so they are the ones
who mishear a name or transpose a digit. Editing was `VolunteerOrAbove`, so the person who
made the mistake could not fix it — and in practice the record stayed wrong.

**How the widening was kept narrow.** Rather than opening the full `GET /{id}`, which
carries lifecycle, do-not-contact and pastoral notes, a new endpoint returns exactly the
fields the intake form itself collects. `lookup` already masks contact values for the same
reason, and widening the general read would have quietly undone that.

| # | Test | Expect | Status |
|---|---|---|---|
| D1 | `GET /{id}/intake` as DATA_ENTRY | 200, and the payload carries **only** intake fields — no lifecycle, do-not-contact, reference code or care notes | ✅ |
| D2 | **`GET /{id}` (full record) as DATA_ENTRY** | **403.** The wider read stays closed | ✅ |
| D3 | `PUT /{id}` as DATA_ENTRY | 200, "Person updated", change applied | ✅ |
| D4 | `PUT /{id}/do-not-contact` | 403 | ✅ |
| D5 | `PUT /{id}/lifecycle` | 403 | ✅ |
| D6 | `DELETE /{id}` | 403 | ✅ |
| D7 | `GET /api/people` (list everybody) | 403 | ✅ |

### On the screen

A "Correct a record" button opens a search over the lookup the operator already had. Choosing
a result loads the record into the same form they know, with a warning banner, the title
changed, and Save relabelled. Two things are suppressed in that mode: the follow-up
checkbox is hidden, because a correction must never open a pastoral case off the back of a
spelling fix, and the duplicate override is not sent, because it is meaningless on an
update. The save goes to `PUT` with the row version, so a concurrent edit is refused rather
than silently overwritten.

### Testing note

The test account held both DATA_ENTRY and VOLUNTEER, which would have passed the old policy
anyway, so it was temporarily reduced to DATA_ENTRY only — otherwise D2 to D7 would have
proved nothing. Its password hash, security stamp, token version, row version, roles and
failed-attempt counter were all captured beforehand and restored, along with the one field
changed on the test person. A failed login I made during setup had bumped the row version
and that was put back too.

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
