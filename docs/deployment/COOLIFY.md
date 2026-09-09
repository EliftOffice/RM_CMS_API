# Deploying to Coolify

How to stand up **`feature/auth_security`** as a **test** instance on Coolify, alongside
the existing production deployment.

Written 2026-09-07, against the branch as it stands (14 modules, 35-table schema,
migrations `001`–`005`).

---

## 0. Read this before you touch anything

Three facts about this branch that decide how the whole deployment has to be shaped.

**This branch is a ground-up rewrite with an incompatible schema.** It is not an
increment on what is live. Table names, identity strategy and the whole data model
differ — see [`../database/README.md`](../database/README.md). It **cannot** share a
database with production. Point it at a new, empty one.

**Production auto-deploys on push.** The GitHub repo is wired to Coolify with a webhook,
so a push to a watched branch ships. Make sure the *existing* production application in
Coolify is pinned to its own branch before you push this one — if it is set to build
whatever arrives, pushing `feature/auth_security` replaces the live church system.

**The Telegram bot reaches real people.** Volunteers and team leads receive alerts on
their real phones. If the test instance is given the production `Telegram__BotToken`, a
test escalation sends a real Telegram message to a real volunteer at whatever hour the
job runs. Leave `Telegram__BotToken` **empty** on the test instance, or register a
separate test bot with BotFather.

---

## 1. What you are deploying

| | |
|---|---|
| Repo | `EliftOffice/RM_CMS_API` |
| Branch | `feature/auth_security` |
| Build | The root [`Dockerfile`](../../Dockerfile) — SDK 8.0 build stage, ASP.NET 8.0 runtime stage |
| Listens on | **8080** (the .NET 8 runtime image default; `EXPOSE 8080` in the Dockerfile) |
| Health check | **`GET /health`** — anonymous, rate-limit exempt, returns `{"status":"ok"}` |
| Landing page | `GET /` serves the sign-in shell (`pages/auth/login.html`) |
| Database | MySQL 8+ (developed against 9.1). Not created or migrated by the app — you load the schema yourself, see §3 |
| Scheduler | **None in-process.** Jobs run only when something calls them — see §6 |

### Commit first

Coolify builds from **git**, not from your working tree. Anything uncommitted is not in
the image. At the time of writing this branch has uncommitted work:

```bash
git status --short
```

Commit and push it before deploying, or the instance will be missing whatever is still
local — including migration `005_areas.sql` and the Areas module if those are still
unstaged.

---

## 2. Create the application in Coolify

1. **New Resource → Application → Public/Private Repository**, pointing at
   `EliftOffice/RM_CMS_API`.
2. **Branch:** `feature/auth_security`.
3. **Build Pack:** `Dockerfile`. Leave the Dockerfile location as the repo root.
4. **Port:** `8080`.
5. **Domain:** give it its own hostname — e.g. `test.rmoffice.online`. Do **not** reuse
   `rmoffice.online`.
6. **Health check path:** `/health`.
7. Let Coolify issue the TLS certificate. HTTPS is not optional here — see §5.

> The repo has **no `.dockerignore`**, so `COPY . .` sends `bin/`, `obj/` and the whole
> working directory to the build context. It builds correctly (the SDK stage restores and
> publishes fresh), just slower than it needs to be. Adding one is a worthwhile follow-up,
> not a blocker.

---

## 3. The database

### 3.1 Create an empty one

Add a **MySQL** resource in Coolify (or use an existing server), and create a database
that is **not** the production one:

```sql
CREATE DATABASE rm_cms_test CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
```

### 3.2 Load the schema

The application does **not** create or migrate its schema on startup — there is no EF
migration step, and nothing in `BootstrapAsync` touches DDL. A fresh database must be
loaded by hand, once, before the first request:

```bash
mysql -u <user> -p rm_cms_test < docs/architecture/schema.sql
```

`schema.sql` is the full 35-table schema **including** everything migrations `002`–`005`
added, plus the reference vocabulary the app needs to function (campus, roles, capacity
bands, contact methods, care outcomes, escalation reasons, default `app_setting` rows).
It creates no people, no volunteers and no secrets.

It uses bare `CREATE TABLE` and is deliberately **not** idempotent — run it against an
empty database, where it will fail loudly rather than half-apply.

### 3.3 Do not copy production data in

The MVP export holds real names, phone numbers, prayer requests and crisis-escalation
records. `migrations/001_mvp_to_v2.sql` exists for the local dev database and should stay
there. A test instance on a public hostname with real pastoral data on it is a data
breach waiting for someone to guess a password.

If you need realistic volume, create fixtures through the UI or the API.

---

## 4. Environment variables

Set these in **Coolify → your application → Environment Variables**. Nested config keys
use a **double underscore**.

### Required — the app will not start without them

| Variable | Value | Notes |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | `Server=<host>;Port=3306;Database=rm_cms_test;User=<user>;Password=<pw>;` | The **test** database from §3 |
| `Jwt__SigningKey` | 32+ bytes of random | `openssl rand -base64 48`. **Generate a new one** — do not reuse production's, or a token from one instance is valid on the other |
| `ASPNETCORE_ENVIRONMENT` | `Production` | See the warning below before considering `Development` |

Startup validation is fail-fast: a missing or too-short signing key stops the process
rather than degrading to something insecure. If the container restart-loops, read the
logs — the reason will be spelled out.

### Required for this to work on a test hostname

| Variable | Value | Why |
|---|---|---|
| `AllowedHosts` | `test.rmoffice.online` | **The single most likely thing to break.** `appsettings.json` ships `rmoffice.online;www.rmoffice.online`, and host filtering is on by default. On any other hostname **every request returns 400** before it reaches a controller — including `/health`, so Coolify will report the deployment unhealthy and you will be looking at the app when the problem is one line of config. Semicolon-separated for several hosts |
| `Jwt__Issuer` | `https://test.rmoffice.online` | Issuer and audience are validated against the same config value used to sign, so any consistent value works — but changing it means a token minted by production is rejected here, which is what you want |
| `Notifications__PublicBaseUrl` | `https://test.rmoffice.online` | Embedded in the links inside Telegram messages. Left at the default, a test alert sends people to **production** |

### First admin — needed once, on an empty database

`schema.sql` seeds no accounts, so without this nobody can sign in.

| Variable | Value |
|---|---|
| `Auth__Bootstrap__Enabled` | `true` |
| `Auth__Bootstrap__Username` | A **10-digit mobile number** — the sign-in screen enforces that shape, so anything else creates an account the login form will not accept |
| `Auth__Bootstrap__Password` | A strong password: 12+ chars, upper, lower, digit, non-alphanumeric |
| `Auth__Bootstrap__DisplayName` | e.g. `Test Administrator` |

> **The bootstrap password is create-only.** `EnsureAdministratorAsync` returns
> immediately once an active ADMIN exists, so editing `Auth__Bootstrap__Password` after
> the first successful boot changes nothing — the stored hash keeps whatever it was on
> the day the account was created, and login fails with credentials that look correct in
> Coolify. If you lock yourself out, either reset it as another admin via
> `POST /api/admin/accounts/{id}/password`, or point `Auth__Bootstrap__Username` at an
> unused number to mint a fresh administrator.
>
> Set `Auth__Bootstrap__Enabled=false` once you have signed in.

### Optional

| Variable | Default | Notes |
|---|---|---|
| `Auth__ServiceApiKey` | empty | Lets an external cron call the job endpoints with `X-Service-Key` instead of an admin JWT. **Required if you want the scheduled work to run** — see §6. Generate a fresh random value |
| `Telegram__BotToken` | empty | **Leave empty on a test instance** unless you have a separate test bot. Empty disables Telegram entirely; nothing else breaks, alerts simply queue in `notification_delivery` and are never sent |
| `Telegram__BotUsername` | empty | The bot's `@name`, **without** the `@` |
| `Auth__TelegramWebhookSecret` | empty | The `secret_token` Telegram echoes on each webhook delivery. **The webhook fails closed when this is empty**, which is the right default here |
| `Auth__AllowedOrigins__0` | — | Only needed if a browser on a *different* origin calls this API. The frontend is served by this same application, so same-origin is correct and the array should stay empty. Entries must be `https` outside Development, and `*` is rejected outright (credentials are enabled on the CORS policy) |

> **Do not set `ASPNETCORE_ENVIRONMENT=Development` on a public hostname.** It turns on
> Swagger at `/swagger`, relaxes the CSP, and skips HSTS. If you want the API surface
> documented for testers, put the instance behind Coolify's basic auth first.

### What never goes in `appsettings.json`

Bot tokens, signing keys and connection strings. `appsettings.json` is committed and this
repo auto-deploys; `appsettings.Development.json` is gitignored and holds throwaway
localhost values only. Everything secret is a Coolify environment variable.

---

## 5. HTTPS is required, and why

Coolify's proxy terminates TLS and forwards plain HTTP to the container. Two consequences
worth knowing before you debug something that is not a bug:

**The refresh-token cookie is always `Secure`.** `AuthController` sets
`__Host-rmcms-rt` with `Secure = true` and `SameSite = Strict` unconditionally — not
conditionally on `Request.IsHttps`. That is deliberate and it is why this works behind a
proxy that the app cannot see through. But it also means **a browser on plain `http://`
silently discards the cookie**: sign-in appears to succeed, then the session evaporates
on the first refresh. Always reach the test instance over `https://`.

**`UseHttpsRedirection()` is a no-op here.** With no forwarded-headers middleware
configured, the app cannot determine an HTTPS port and logs
`Failed to determine the https port for redirect` on every request, then does nothing.
Harmless — Coolify's proxy already redirects http→https — but the log line is noise, not
a fault.

**Rate limiting partitions by connection IP for anonymous callers.** Behind the proxy that
is the proxy's address, so all signed-out traffic shares one bucket: a global ceiling of
240 requests/minute and 30 refreshes per 5 minutes. Fine for a handful of testers; if a
larger group hits 429s at the sign-in screen, that is why, and the fix is forwarded
headers rather than raising the limit.

The CSP allows `code.jquery.com`, `cdn.jsdelivr.net` and `cdnjs.cloudflare.com`, which is
what the admin screens load jQuery from — no action needed, but if the proxy or a network
policy blocks those CDNs the admin pages will fail to boot while the API stays fine.

---

## 6. Scheduled jobs — nothing runs on its own

There is **no in-process scheduler**, on purpose: it keeps every sweep runnable on demand
and stops a second instance silently double-processing. Deployed and left alone, the
system will assign nothing, advance no nurture steps, chase no escalations and send no
alerts.

Two ways to drive it on a test instance:

**By hand** — sign in as the administrator and use **Settings → Run now** on a job card.
It shows the run report immediately, which makes it the better option while you are
actually watching something.

Note the screen offers only **four** cards — *assign-unassigned*, *advance-nurture*,
*mark-overdue*, *chase-escalations*. **`huddle-reminder` and `send-notifications` have no
button.** That second gap matters: escalation alerts are *queued* by `chase-escalations`
and only *delivered* by `send-notifications`, so a tester who raises an escalation, presses
"Run now" on the chase-up card and waits for a Telegram message will never get one. Either
schedule `run-all` as below, or call the sender directly:

```bash
curl -fsS -X POST https://test.rmoffice.online/api/jobs/send-notifications \
  -H "X-Service-Key: $SERVICE_KEY"
```

**On a schedule** — set `Auth__ServiceApiKey`, then add a **Scheduled Task** in Coolify
(or any external cron) calling:

```bash
curl -fsS -X POST https://test.rmoffice.online/api/jobs/run-all \
  -H "X-Service-Key: $SERVICE_KEY"
```

`run-all` runs the six jobs in dependency order — assign, nurture, overdue, chase-ups,
huddle reminder, send notifications — so a new case can get its first contact before the
overdue sweep looks at it. Every job is idempotent and takes a bounded batch, so a
15-minute cadence is safe; the huddle reminder does nothing unless today is the configured
huddle day.

Individual routes, if you want separate schedules:
`assign-unassigned`, `advance-nurture`, `mark-overdue`, `chase-escalations`,
`huddle-reminder`, `send-notifications` — all `POST /api/jobs/{name}`.

`GET /api/jobs/history` reads the `job_run` ledger. **A run with no finish time crashed**
— those are not filtered out, which is the point.

---

## 7. First deploy — the order that works

```
1.  Pin the production Coolify app to its own branch          (so this push cannot ship it)
2.  Commit and push feature/auth_security
3.  Create the empty test database
4.  mysql < docs/architecture/schema.sql
5.  Create the Coolify application: repo, branch, Dockerfile, port 8080, /health
6.  Set the environment variables from §4  — AllowedHosts included
7.  Set the domain and let the certificate issue
8.  Deploy
9.  curl https://test.rmoffice.online/health          -> {"status":"ok"}
10. Sign in at https://test.rmoffice.online with the bootstrap credentials
11. Change the admin password
12. Set Auth__Bootstrap__Enabled=false and redeploy
```

### Smoke test after it comes up

| Check | Expect |
|---|---|
| `GET /health` | `{"status":"ok"}` — a **400** here means `AllowedHosts` |
| `GET /` | The sign-in screen |
| Sign in as the bootstrap admin | Lands on the admin shell; the nav shows Users, Teams, Campuses, Areas, Accounts, Settings, Telegram |
| Reload the page after signing in | Still signed in — proves the `__Host-` refresh cookie survived, i.e. you are on HTTPS |
| **Settings** | Business rules load, grouped by category |
| **Areas** | Lists areas; empty on a fresh database until someone records a visitor |
| **Record a visitor** | With "Lives locally" ticked, the Area field appears and is required; typing a new area creates it on save |
| **Settings → Run now** on *Assign new people* | A run report, and a row in `job_run` |

If sign-in reports success and then bounces you back to the login screen, check the
scheme in the address bar before anything else — §5.

---

## 8. Subsequent deploys

A push to `feature/auth_security` triggers a rebuild via the webhook. No manual step.

**Migrations are not automatic.** When a new script lands in
[`../database/migrations/`](../database/migrations/), apply it to the test database
yourself before or immediately after the deploy that needs it. They are additive and
idempotent (`005_areas.sql` guards every statement and is safe to re-run), but nothing
runs them for you:

```bash
mysql -u <user> -p rm_cms_test < docs/database/migrations/005_areas.sql
```

Static assets are served with `no-cache, must-revalidate` for `.js`/`.css` and `no-store`
for `.html`, so testers do not need to hard-refresh after a deploy — a stale script
running against a new API is a class of bug that looks like an application fault, and the
cache headers exist to prevent it.

---

## 9. Tearing it down

Delete the Coolify application and drop the test database. Nothing else holds state.

If you gave the instance a real Telegram bot token, also delete the webhook registration
so Telegram stops delivering to a host that no longer exists.

---

## See also

- [`../architecture/SYSTEM_WORKFLOW.md`](../architecture/SYSTEM_WORKFLOW.md) — how the system works, §8 for the jobs
- [`../database/README.md`](../database/README.md) — schema and migration workflow
- [`../testing/TESTING.md`](../testing/TESTING.md) — what has been verified, and how
- [`../testing/manual/README.md`](../testing/manual/README.md) — hand-run test scripts to run against the instance
