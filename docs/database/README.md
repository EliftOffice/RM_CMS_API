# Database

Moved here from the old top-level `Database/` folder as part of the documentation
consolidation — paths below are relative to this file's new location, `docs/database/`.

## Which file is authoritative

| Path | Status | Use |
|---|---|---|
| **[`../architecture/schema.sql`](../architecture/schema.sql)** | ✅ **Source of truth** | The live production schema (34 tables). All new work targets this. |
| [`../architecture/CONVENTIONS.md`](../architecture/CONVENTIONS.md) | ✅ Active | The rules `schema.sql` follows, and the rules the application rewrite must follow. |
| [`archive/mvp_schema_pre_rewrite.sql`](archive/mvp_schema_pre_rewrite.sql) | 🗄️ **Reference only — do not modify** | The consolidated pre-rewrite MVP schema (22 tables, plural names, varchar business keys). Retained to understand the old system and to map data across when repopulating. Not a migration source in its own right — `migrations/001_mvp_to_v2.sql` is. |

The live schema is a ground-up redesign and is **deliberately not backward compatible**
with the archived MVP one — different table names, different identity strategy (`id` /
`public_id` ULID / `reference_code` instead of varchar business keys). There is no
schema-level migration between them; `migrations/001_mvp_to_v2.sql` moves *data* across
by mapping old business keys to new generated ids, not by altering the old schema in place.

## Applying it fresh

```bash
mysql -u root -e "CREATE DATABASE cms_api_db;"
mysql -u root --database=cms_api_db < docs/architecture/schema.sql
```

Not idempotent by design — it uses bare `CREATE TABLE` so re-running against a populated
database fails loudly instead of silently half-applying. Run it against an empty database.

## Bringing up real data (the current dev workflow)

The dev database (`cms_api_db`) is not populated from `schema.sql` alone — it's migrated
from a real production MVP backup, then brought forward with two additive migrations. In
order, all in [`migrations/`](migrations/):

| Script | What it does |
|---|---|
| `001_mvp_to_v2.sql` | The real data migration — moves production data from the old MVP database into the new schema. Re-runnable; truncates transactional tables and preserves the local bootstrap admin. Every migrated account gets the same known dev password with `must_change_password=1` — explicitly **not for production as-is**. |
| `002_huddle_assessment.sql` | Additive, idempotent — adds the Team Huddle escalation-assessment columns to `care_interaction` plus its `app_setting` rows. |
| `003_team_management_access.sql` | Additive, idempotent — adds the `team.manage_by_pastor` / `team.manage_by_team_lead` grant settings (both default `false`). |
| `004_clear_placeholder_telegram.sql` | Not a schema migration — a data-hygiene script. Run once after `001` against real migrated data: disconnects any `TELEGRAM` contact rows that share a single chat id (a leftover test chat from migration), which otherwise routes every affected person's alerts to one chat and falsely satisfies the volunteer-reachability check. |

See [`../testing/manual/README.md`](../testing/manual/README.md) for the exact reset-to-baseline commands.

## What it contains

Structure, plus the reference vocabulary the application needs to function (campus, roles,
capacity bands, contact methods, care outcomes, escalation reasons and outcomes, default
settings).

`schema.sql` itself contains **no people, no volunteers, no demo records, no secrets** —
those only enter via the migration scripts above, against a local database.

## What must never go in this folder (or anywhere in the repo)

Production dumps. The MVP export used as input to `001_mvp_to_v2.sql` holds real names,
phone numbers, prayer requests and crisis-escalation records. It must stay out of version
control: git history is permanent, and this repository auto-deploys to a public host.

If you need production data to work with, restore it into a local database directly. Do
not stage it through the repo.
