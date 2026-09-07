# Archive

Everything in this folder describes something that is **not** true of the current
codebase — either it was superseded before the current rewrite, or it was built and then
deliberately deferred and never revived. Kept for history and for anyone tracing why a
decision was made; none of it should be used as a reference for building anything today.

None of the code these documents describe exists any more. The old pre-rewrite layer
(`Controllers/`, `BLL/`, `DAL/`, `Data/DTO/`, `Data/Models/`) that most of it was written
against has been fully deleted from the repository.

---

## `assignment-api/` — an early ADO/BLL-DAL design, superseded before the C# rewrite

`AssignmentAPI.md`, `QUICK_REFERENCE.md`, `USAGE_EXAMPLES.md`, `IMPLEMENTATION_SUMMARY.md`

Documented a `POST /api/peoples/{personId}/assign` endpoint (`PeoplesController` →
`PeopleService` → `PeopleRepository`/`VolunteerRepository`) against a `people`/
`volunteers` schema with a single `assigned_volunteer` column.

**Superseded by:** `Modules/People` + `Modules/Care` + `Modules/Pipeline`. Assignment is
now a `care_case`/`care_case_assignment` relationship, not a column on a person, and picks
the least-loaded eligible volunteer via `Modules/Volunteers`' capacity-band logic. No
`api/peoples/*/assign` route exists.

## `early-design/` — the pre-rewrite design documents

`COMPLETE_DATABASE_SCHEMA.md`, `COMPLETE_PROGRAMMING_LOGIC.md`

A from-scratch schema (`people`, `volunteers`, `team_leads`, `follow_ups`, `check_ins`,
`escalations`, `notes`, `vnps_surveys`, `capacity_history`, `system_config`) and a
Node.js/Express pseudocode walkthrough of the same "Level 1 Follow-Up System" — visitor
entry, capacity-aware assignment, a 4-branch auto-routing tree, cron jobs.

**Superseded by:** the live `docs/architecture/schema.sql`, whose own header comments
explicitly describe replacing this exact design (it names the specific problems: state
living directly on the person record, capacity bands duplicated between a table and
`system_config`). The state-machine and auto-routing *ideas* clearly influenced the real
`Modules/Care`'s `ProgressionEngine`, but no code here is live — this was Node.js, the app
is C#, and it predates the actual implementation entirely. Some individual concepts did
carry forward in redesigned form: `vnps_surveys` → `volunteer_survey`, `escalations` →
`escalation` (same STANDARD/URGENT/EMERGENCY tiers), `capacity_bands`/`system_config` →
`capacity_band`.

## `escalation-appropriateness/` — the concept lives on, the mechanism doesn't

`WHAT_IS_ESCALATION_APPROPRIATENESS.md`

Defines Correct / Under-Escalation / Over-Escalation, a monthly Team-Lead review, and a
weighted formula (40% completion + 30% trend + 15% escalation-appropriateness + 15%
emotional health) feeding a volunteer's health flag.

**Partially superseded, not fully dead — read with care:** the taxonomy itself is real
and live — `care_interaction.escalation_assessment` in the current schema, with exactly
these three values (`CORRECT`, `UNDER_ESCALATED`, `OVER_ESCALATED`), judged through the
**Team Huddle** (`Modules/Huddle`), not through a standalone review screen with this
document's field names. What's **not** real: the `follow_ups.escalation_appropriate`
column name, the specific "Team Lead Review Form", and — the part most likely to mislead
someone — the weighted 40/30/15/15 formula. The actual `HealthFlag` in
`Modules/Dashboards/Domain/DashboardModels.cs` is much simpler (two consecutive falling
weeks = red, one = amber, else green) and does not fold in the Huddle verdict at all.

## `system-health/` — built, then deleted this session, name reused for something unrelated

`SYSTEM_HEALTH_COMPLETE_GUIDE.md`, `SYSTEM_HEALTH_IMPLEMENTATION.md`,
`SYSTEM_HEALTH_QUICK_REFERENCE.md`, `SYSTEM_HEALTH_TESTING.md`

Documented `GET /api/pastors/system-health` (`PastorsController` → `PastorService` →
`PastorDashboard` DAL) — organisation-wide vNPS, retention, completion-rate aggregates
with a 🟢/🟡/🔴 `overallHealthStatus`.

**Superseded by:** `Modules/Dashboards`'s `GET /api/dashboards/pastor`
(`IPastorDashboardService`/`PastorDashboardService`) — built and verified 2026-09-07. Note
the naming collision: the old classes were also literally named `PastorDashboard*`, for an
entirely different feature (org-wide vNPS/retention aggregates vs. the new one's
escalations-owed / team case-load / per-volunteer red-amber-green flags). If you find a
reference to "PastorDashboard" anywhere old, don't assume continuity with the current
pastor dashboard — check which one.

## `nurture-sequences-admin/` — real feature, deleted this session, confirmed no live route

`NURTURE_SEQUENCES_MANAGEMENT_GUIDE.md`, `NURTURE_SEQUENCES_MANAGEMENT_QUICK_REFERENCE.md`,
`NURTURE_SEQUENCE_HISTORY_GUIDE.md`, `NURTURE_SEQUENCE_HISTORY_QUICK_REFERENCE.md`,
`nurture_sequence_flow.svg`

Documented two admin pages (`nurture-sequences-management.html`,
`nurture-sequence-history.html`) against a `Controllers/Nurture/NurtureController.cs` that
did once exist (added 2026-06-01, removed 2026-08-15 when the Care module's progression
engine replaced it). Both HTML pages and their JS were confirmed calling routes that no
longer existed, and were deleted this session (2026-09-07) along with the rest of the old
layer.

**Superseded by:** `Modules/Care`'s internal `nurture_plan`/`nurture_plan_step`-driven
`ProgressionEngine`, which schedules nurture steps automatically but exposes no
standalone "view all sequences" admin page or API — current Care routes are only
`api/cases`, `api/contacts`, `api/escalations`. If that oversight view is wanted again,
it needs building fresh against the current schema, not resurrecting this code.

## `attendance-feature/` — built, shipped briefly, explicitly deferred, never revived

18 files: `00_START_HERE.md`, `ATTENDANCE_API_INTEGRATION.md`, `COMPLETION_CHECKLIST.md`,
`DOCUMENTATION_INDEX.md`, `FINAL_SUMMARY.md`, `FINAL_VERIFICATION_REPORT.md`,
`FRONTEND_API_REFERENCE.txt`, `INTEGRATION_COMPLETE.md`, `INTEGRATION_STATUS.md`,
`PROJECT_COMPLETION_REPORT.md`, `QUICK_START.md`, `README.md`, `README_INTEGRATION.md`,
`TEST_API_CURL.sh`, `TEST_API_PowerShell.ps1`, `WEB_INTERFACE_GUIDE.md`,
`WEB_INTERFACE_SETUP.md`, `YOUR_NEXT_STEPS.md`, `backend_integration_spec.md`.

This one is different from the others above: it wasn't just planning docs for something
never built. GPS check-in / geofencing / recurring-event attendance tracking was **fully
implemented** (`BLL/Attendance/AttendanceBLL.cs`, `Controllers/Attendance/AttendanceController.cs`,
`DAL/Attendance/AttendanceDAL.cs`, models and DTOs, `app_users`/`events`/`attendances`
tables), documented here on 2026-06-12, then **deliberately removed** a month later
(2026-08-13, commit `1ed33b7`) with the explicit message *"Events and attendance in full
... deferred to a later feature."* It has not been rebuilt since — no `Modules/Attendance`,
no `api/attendance/*` route, no attendance table in the live schema, no attendance page
anywhere in `wwwroot/`.

**Status: retired by deliberate decision, not forgotten and not never-built.** If
attendance tracking is wanted again, these docs are a genuine, complete spec of a working
implementation to build from — just written against the pre-rewrite schema and layer, so
treat it as a design reference, not code to restore as-is.

**One filing note:** `README.md` in this folder is mismatched — it's titled "Project
Overview - RM_CMS API" and actually describes the unrelated Visitors/Peoples module, not
Attendance. Looks like a copy-paste leftover. `DOCUMENTATION_INDEX.md` and
`00_START_HERE.md` are the ones that actually index this feature.
