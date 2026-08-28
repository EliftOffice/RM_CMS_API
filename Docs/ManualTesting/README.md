# Manual testing — RM_CMS

One document per screen, in the order a real person moves through the system. Each
document says what the screen does, exactly which tables it writes, sample data you can
paste in, and a checklist with the SQL to prove each step actually landed.

`TESTING.md` in the repo root is the API-level record — endpoints, policies, edge cases.
These documents are the **screen-level** counterpart: what a person clicks, and what
should happen in the database when they do.

## The order to test in

The system is a funnel. Testing it out of order means inventing state that the previous
screen should have produced, which is how a bug in step 2 gets found in step 5.

| # | Screen | Document | Role |
|---|---|---|---|
| 1 | Visitor entry | [01-visitor-entry.md](01-visitor-entry.md) | Data entry and above |
| 2 | Volunteer assignments | [02-volunteer-assignments.md](02-volunteer-assignments.md) | Volunteer |
| 3 | Log a contact | *(to be written)* | Volunteer |
| 4 | Team lead dashboard | [04-team-lead-dashboard.md](04-team-lead-dashboard.md) | Team lead |
| 5 | Escalation detail | *(to be written)* | Team lead |
| 6 | Check-in | *(to be written)* | Team lead |
| 7 | People pipeline | *(to be written)* | Team lead / pastor |
| 8 | Team huddle | *(to be written)* | Team lead |

## Before you start

**Run the API:**

```bash
cd C:/Users/LENOVO/source/repos/CMS && dotnet run
```

Then open `http://localhost:5043/templates/Volunteers/Login.html`.

**The local sign-in you already have:**

| Username | Password | Role |
|---|---|---|
| `9999999999` | `Xq7#vTrb92!mKp` | Administrator |

Every account migrated from the production backup shares that password but is forced to
change it at first sign-in. To sign in as one of them without the change, clear the flag
first:

```sql
UPDATE user_account SET must_change_password = 0 WHERE username = '<their mobile>';
```

Clearing it is not optional: a token issued to an account that still has the flag is
refused by every endpoint except the password-change one, with HTTP 403
`password_change_required`. **Put the flag back when you are done.**

> Not *quite* every account: `9859939859` (Prisk G) carries a different `password_hash`
> and does not accept that password. Guessing at it costs you the login rate limiter —
> five attempts per five minutes, then HTTP 429 for the rest of the window.

> **The dev database holds real production data** — real names, real phone numbers,
> real pastoral detail. Treat it accordingly: it is not sample data, and it should not
> be copied anywhere, pasted into an issue, or shared outside this machine. The sample
> rows in these documents are invented for exactly that reason.

## Resetting between runs

Every document ends with cleanup SQL that removes only what that test created. To go
back to the migrated baseline entirely:

```bash
"/c/Program Files/MySQL/MySQL Server 9.1/bin/mysql.exe" -uroot < Database/Migration/001_mvp_to_v2.sql
"/c/Program Files/MySQL/MySQL Server 9.1/bin/mysql.exe" -uroot < Database/Migration/002_huddle_assessment.sql
```

That is destructive to test data and safe for the source: it reads `rm_cms_10_aug_2026`
and rebuilds `cms_api_db` from it.

## Conventions used in these documents

- **`✅ Expect`** — what should happen. If it does not, stop and record it.
- **`⚠️ Known`** — a gap already understood; not a new bug.
- SQL blocks are runnable as-is against `cms_api_db`.
