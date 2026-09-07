# RM_CMS documentation

Everything that used to be scattered across `Docs/`, `Database/`, `Documentation/` and
`Features/` at the repo root, consolidated here on **2026-09-07**. Nothing was rewritten —
original files were moved as-is (paths inside them updated where they pointed at each
other) — except the two indexes below and small "since this was written" currency notes
added to a few dated documents.

Three tiers, in order of how much you should trust them right now:

| Tier | Meaning |
|---|---|
| **Current reference** | Describes the live system accurately, as of this consolidation. |
| **Testing** | Procedures and verification logs for the live system. Dated, but the procedures still apply even where the verified-on date has passed. |
| **[`archive/`](archive/)** | Describes something that no longer exists, was never actually built, or has been fully superseded. Kept for history, not for building against. See [`archive/README.md`](archive/README.md) before reading anything in it. |

---

## Current reference

| Doc | What it's for |
|---|---|
| [`architecture/SYSTEM_WORKFLOW.md`](architecture/SYSTEM_WORKFLOW.md) | **Start here.** How the whole system actually works end to end — the care-case journey, the progression engine, escalation pause/resume, scheduled jobs, notifications, security model. |
| [`architecture/CONVENTIONS.md`](architecture/CONVENTIONS.md) | The schema's design rulebook — naming, the three-tier identity system (`id`/`public_id`/`reference_code`), why there are no `ENUM`s, `row_version` optimistic locking, deletion policy. The rules the whole application rewrite follows. |
| [`architecture/schema.sql`](architecture/schema.sql) | The live 34-table schema. Source of truth — apply fresh with `docs/database/README.md`'s instructions. |
| [`database/README.md`](database/README.md) | How to bring up the database: fresh schema load, or the real migrated-data workflow (`database/migrations/`). |
| [`database/migrations/`](database/migrations/) | The four scripts, in order, that take a real MVP data export to the current schema plus everything added since (huddle assessment, team-management grants, a Telegram data-hygiene cleanup). |
| [`database/archive/mvp_schema_pre_rewrite.sql`](database/archive/mvp_schema_pre_rewrite.sql) | The old MVP schema `001_mvp_to_v2.sql` migrates *from*. Reference for understanding old data shapes only — not a current or applicable schema. |

## Testing

| Doc | What it's for |
|---|---|
| [`testing/TESTING.md`](testing/TESTING.md) | The big one — module-by-module test matrices (auth, authorization, people, volunteers, care/escalations, jobs, notifications, dashboards, huddle), a 17-step end-to-end golden path, and what still has no automated coverage. |
| [`testing/manual/README.md`](testing/manual/README.md) | Index for the hand-run manual test scripts below — setup, shared credentials, reset-to-baseline. |
| [`testing/manual/01-visitor-entry.md`](testing/manual/01-visitor-entry.md) | Visitor intake screen. |
| [`testing/manual/02-volunteer-assignments.md`](testing/manual/02-volunteer-assignments.md) | Volunteer work-list / contact-logging screen, including the full progression-rule priority table. |
| [`testing/manual/04-team-lead-dashboard.md`](testing/manual/04-team-lead-dashboard.md) | Team lead dashboard, all six cards, plus a "known — not new bugs" list (kept up to date — see the entries marked fixed). |

`03`, `05`–`08` in the original numbering were never written (escalation detail, check-in,
pipeline, huddle) — worth writing now, since all four now correspond to live, working
screens rather than blocked-on-backend placeholders.

---

## Everything else: `archive/`

Every file under `Documentation/` and `Features/` turned out to describe either an early
Node.js-style design that was superseded before the current C# rewrite began, or a real
feature (Attendance) that was built, then explicitly deferred and never revived. None of
it should be read as describing the current system. Full breakdown, including exactly
what superseded each piece, is in [`archive/README.md`](archive/README.md).

---

## What changed in this consolidation, if you're looking for something that used to be
elsewhere

| Old location | New location |
|---|---|
| `SYSTEM_WORKFLOW.md` (repo root) | `docs/architecture/SYSTEM_WORKFLOW.md` |
| `TESTING.md` (repo root) | `docs/testing/TESTING.md` |
| `Docs/ManualTesting/*` | `docs/testing/manual/` |
| `Database/Schema/schema.sql` | `docs/architecture/schema.sql` |
| `Database/Schema/CONVENTIONS.md` | `docs/architecture/CONVENTIONS.md` |
| `Database/README.md` | `docs/database/README.md` (content corrected — see below) |
| `Database/Migration/00{1,2,3}_*.sql` | `docs/database/migrations/` |
| `Database/Scripts/clear_placeholder_telegram.sql` | `docs/database/migrations/004_clear_placeholder_telegram.sql` (renamed into the sequence — it's a real follow-on step, not a one-off) |
| `Database/SQL_Scripts/schema.sql` | `docs/database/archive/mvp_schema_pre_rewrite.sql` (renamed for clarity — it's not "the SQL scripts", it's specifically the old MVP schema) |
| `Documentation/*`, `Features/*` | `docs/archive/*` — see the table there for exactly where each file landed |

**`docs/database/README.md` correction:** the original said to create a database named
`rmcms`; the real, current dev database is `cms_api_db` (confirmed against
`testing/manual/README.md`'s reset commands and the live `appsettings.Development.json`
convention). It also never mentioned the `migrations/` folder at all — fixed, since that's
the actual current workflow for standing up a dev database with real test data rather than
an empty schema.
