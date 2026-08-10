# Database conventions

Rules for `Database/Schema/schema.sql`. Phase 3 (the application rewrite) must follow
these — most of them exist because the MVP broke them and it cost something.

---

## Naming

| Thing | Rule | Example |
|---|---|---|
| Table | `snake_case`, **singular** | `care_case`, not `care_cases` |
| Column | `snake_case` | `first_contact_at` |
| Foreign key column | `<referenced_table>_id` | `care_case_id` |
| Primary key | `fk_<table>_<target>` | `fk_volunteer_campus` |
| Index | `ix_<table>_<purpose>` | `ix_volunteer_assignment` |
| Unique | `ux_<table>_<purpose>` | `ux_user_account_username` |
| Check | `ck_<table>_<rule>` | `ck_volunteer_status` |
| Instant column | ends `_at` (DATETIME) | `closed_at` |
| Date column | ends `_on` (DATE) | `started_on` |
| Boolean | `is_` / `has_` / verb-ed | `is_active`, `location_verified` |
| Count | ends `_count` | `contact_attempt_count` |

**Never use a reserved word as an identifier.** `rank` cost a syntax error during
validation; it is a MySQL 8 window function. When in doubt, pick a more specific name
rather than quoting.

---

## Keys and identifiers

Every table has **three** kinds of identity, and they are not interchangeable:

```
id            BIGINT UNSIGNED AUTO_INCREMENT   internal only, never leaves the server
public_id     CHAR(26)  ULID                   what the API and URLs expose
reference_code VARCHAR(20)                     what humans say out loud ('V001')
```

- **`id`** is the clustered primary key. Compact, sequential, ideal for InnoDB. Used for
  every join and foreign key. It must never appear in a URL, an API payload or a log.
- **`public_id`** is a ULID — 26 chars, URL-safe, lexicographically sortable by creation
  time. This is what `GET /api/cases/{id}` accepts. Sequential ids in URLs let any
  authenticated caller enumerate the whole dataset; that was a live hole in the MVP.
- **`reference_code`** is display data. Staff say "V001" to each other. It is `UNIQUE`
  but is not a key — never join on it.

> **ULID, not UUID/`CHAR(36)`.** MySqlConnector silently converts `CHAR(36)` to
> `System.Guid`, which Dapper then cannot map onto a string property — it fails with
> *"Object must implement IConvertible"*. This cost real debugging time on the MVP.
> `CHAR(26)` has no such behaviour.

---

## Data types

| Need | Use | Not |
|---|---|---|
| Instant | `DATETIME(3)`, UTC | `TIMESTAMP` — 2038 limit, silent timezone conversion |
| Calendar date | `DATE` | |
| Money | `DECIMAL(p,s)` | `FLOAT`/`DOUBLE` |
| Coordinates | `DECIMAL(9,6)` | ~11 cm precision, ample |
| Boolean | `TINYINT(1)` | |
| IP address | `VARBINARY(16)` + `INET6_ATON()` | `VARCHAR(45)` — 16 bytes vs 45, IPv6-correct |
| Short set of values | `VARCHAR` + `CHECK`, or a lookup table | **`ENUM`** |
| Free text | `TEXT` | oversized `VARCHAR` |

**All instants are UTC.** The application supplies them via `TimeProvider`; the database
never applies a timezone.

---

## Enums: the deciding rule

`ENUM` is not used anywhere. Adding a value rewrites the table and cannot be done by an
administrator. The replacement depends on *who owns the vocabulary*:

| The values are… | Use | Because |
|---|---|---|
| A business vocabulary an admin may extend (`escalation_reason`, `contact_method`) | **Lookup table + FK** | Adding a reason is a Tuesday, not a deployment |
| An internal state machine the code branches on (`care_case.status`, `care_interaction.status`) | **`VARCHAR` + `CHECK`** | If an admin renamed `OPEN`, every `if (status == "OPEN")` would silently stop matching |

Getting this backwards is how you end up with either a migration for every new dropdown
value, or an admin screen that can break the application.

### Lookup codes are immutable

You change a lookup's `label`; you never change its `code`. Consequently lookup foreign
keys carry **no `ON UPDATE CASCADE`**.

This is also a hard MySQL constraint: a column in a foreign key *with a referential
action* cannot appear in a `CHECK`. Several invariants here (`ck_care_interaction_completed`,
`ck_escalation_resolved`) depend on checking those exact columns.

### Behaviour belongs in the lookup

`care_outcome` carries `opens_escalation`, `starts_nurture`, `schedules_retry`,
`closes_case`. Routing reads these flags. In the MVP the same logic was a hard-coded C#
`switch` over lower-cased strings (`"needs follow-up"` *and* `"needs followup"`), so a
wording change in the UI produced *"Unknown response type"* at runtime.

---

## Constraints

Put an invariant in the database when it must hold **regardless of which code path
writes the row**. Business rules that need context stay in the service layer.

In the schema:
- `ck_care_case_closed_consistency` — a closed case must have `closed_at` and `close_reason`
- `ck_care_interaction_completed` — a completed interaction must have `occurred_at` and an outcome
- `ck_escalation_resolved` — a resolved escalation must have `resolved_at` and an outcome
- `ck_note_confidential` — a confidential note must name the role that may read it
- `ck_refresh_token_expiry` — a token cannot expire before it was issued

**Every one of these was verified by attempting the invalid insert**, not by reading the DDL.

Rules deliberately left to the service layer, because they need data the row doesn't have:
- A volunteer's `current_case_load` must not exceed their band's `max_per_week`
- A crisis case requires a crisis-trained volunteer with a current background check
- Exactly one `care_outcome` behaviour flag should normally be set

---

## Audit and concurrency

Mutable tables carry `created_at` / `created_by` / `updated_at` / `updated_by`.
Append-only logs (`security_event`, `job_run`, `volunteer_capacity_change`,
`password_history`) carry only creation columns — there is nothing to update.

`row_version` exists on rows two users can edit at once. Updates must be written:

```sql
UPDATE care_case
   SET status = @Status, row_version = row_version + 1
 WHERE id = @Id AND row_version = @RowVersion;
```

**Zero affected rows means a conflict, not a no-op.** Re-read and retry or surface it —
never treat it as success.

---

## Deletion

Hard delete by default. Soft delete (`deleted_at`) only where a row must stay
referenceable after removal:

- **`person`** — pastoral history, escalations and attendance reference them and must
  stay readable
- **`event_series`** — past occurrences and attendance reference them

Everywhere else, use a status column. A "deleted" volunteer is `status = 'EXITED'`.

`user_account` deliberately has **no** soft delete: a disabled account keeps its unique
username, which a soft delete would leave permanently claimed but unusable.

> Soft delete and `UNIQUE` interact badly. A soft-deleted `person` still occupies its
> `reference_code`. Accept it or scope the uniqueness — do not add a soft-delete flag to
> a table with a natural key without deciding which.

---

## Indexing

Index for the queries that actually run, not speculatively. Every non-obvious index in
the schema carries a comment naming its query. Examples:

| Index | Serves |
|---|---|
| `ix_volunteer_assignment (campus_id, status, current_case_load)` | the assignment picker: active volunteers at a campus, least loaded first |
| `ix_care_case_queue (campus_id, status, assigned_volunteer_id)` | the unassigned-intake queue the batch job drains |
| `ix_care_interaction_due (volunteer_id, status, scheduled_on)` | a volunteer's daily work list |
| `ix_escalation_queue (assigned_to_user_id, status, tier)` | the team lead's pending queue |
| `ix_security_event_attempt (username_attempted, occurred_at)` | brute-force detection across source IPs |

Column order follows the equality-then-range rule: equality predicates first, the ranged
or sorted column last.

---

## Security

- **No plaintext credentials, ever.** Passwords are PBKDF2-HMAC-SHA512; refresh tokens
  are stored as SHA-256 hashes; the MFA secret is encrypted by the application before it
  reaches the database.
- **Third-party secrets do not belong in `app_setting`.** The MVP kept the live Telegram
  bot token there in plaintext, reachable through a generic settings API. Bot tokens,
  signing keys and connection strings are environment variables.
- **`campus_id` is the authorization boundary.** It is denormalised onto `volunteer`,
  `care_case`, `escalation` and `event_series` so a scoped query never needs a join to
  enforce access. The application must keep it consistent when something moves campus.
- **`security_event` has no foreign key on `user_account_id`** — on purpose. Audit rows
  must survive deletion of the account they describe, which is exactly when they matter.
- **Notification bodies are not stored.** `notification_delivery` records the outcome
  only; the message text quotes pastoral detail and names.

---

## Counters

`volunteer.current_case_load` and `care_case.contact_attempt_count` are **maintained**,
not derived. The MVP kept the same figure in a counter *and* derived it live from another
table, and the two drifted.

The rule: a counter is authoritative, and it is updated **in the same transaction** as
the row that causes the change. A nightly reconciliation job should assert
counter == derived and report drift rather than silently correcting it.

---

## Applying the schema

```bash
mysql -u root -e "CREATE DATABASE rmcms;"
mysql -u root --database=rmcms < Database/Schema/schema.sql
```

This file is **not** idempotent — it uses bare `CREATE TABLE` so a mistake fails loudly
rather than silently skipping. Run it against an empty database.

Once the application is live, schema changes stop being edits to this file and become
numbered, forward-only migrations with a `schema_migration` ledger. Until then, this file
is the whole truth.
