# Database

## Which file is authoritative

| Path | Status | Use |
|---|---|---|
| **`Schema/schema.sql`** | ✅ **Source of truth** | The production schema. All new work targets this. |
| `Schema/CONVENTIONS.md` | ✅ Active | The rules `schema.sql` follows, and the rules the application rewrite must follow. |
| `SQL_Scripts/schema.sql` | 🗄️ **Reference only — do not modify** | The consolidated MVP schema. Retained to understand the old system and to map data across when repopulating. Not a migration source. |

`Schema/schema.sql` is a ground-up redesign and is **deliberately not backward
compatible** with `SQL_Scripts/schema.sql`. There is no migration between them; the new
database is populated fresh.

## Applying it

```bash
mysql -u root -e "CREATE DATABASE rmcms;"
mysql -u root --database=rmcms < Database/Schema/schema.sql
```

Not idempotent by design — it uses bare `CREATE TABLE` so re-running against a populated
database fails loudly instead of silently half-applying. Run it against an empty database.

## What it contains

Structure, plus the reference vocabulary the application needs to function (campus, roles,
capacity bands, contact methods, care outcomes, escalation reasons and outcomes, default
settings).

It contains **no people, no volunteers, no demo records, no secrets**.

## What must never go in this folder

Production dumps. `latest_schema.sql` — the export used as input to this redesign — holds
real names, phone numbers, prayer requests and crisis-escalation records. It is excluded
by `.gitignore` and must stay out of version control: git history is permanent, and this
repository auto-deploys to a public host.

If you need production data to work with, restore it into a local database directly. Do
not stage it through the repo.
