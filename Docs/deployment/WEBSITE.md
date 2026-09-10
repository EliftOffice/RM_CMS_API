# The public website and the CMS API

How the public site at `C:\RM_Website_React` talks to this CMS, how to deploy it,
and the exact request shapes to send.

Written 2026-09-08.

---

## 0. The website

`C:\RM_Website_React` — a **React 19 + Vite + TypeScript** single-page app
(`resurrection-ministries`), styled with Tailwind 4, routed with React Router. It builds
to static files with `npm run build` and is served from `dist/`. It is the only website
project — there is no second copy of the site anywhere else.

**All four forms are wired to this CMS**, through one file — `src/lib/forms.ts`. Every
form component calls one of its four exported functions and only reads `{ ok, message }`
back, so the wire format lives in that single place.

| Form | Page | CMS `formType` |
|---|---|---|
| Prayer request | `/prayer`, `/contact` | `PRAYER_REQUEST` |
| Bible Study Groups registration | `/bsg` | `MINISTRY_REGISTRATION` |
| Join a serving team | `/ministries`, `/ministries/:slug` | `TEAM_REGISTRATION` |
| Plan a visit | `/contact` | `PLAN_VISIT` |

Fields without a column of their own — the ministry slug, team name, party size, which
Sunday — travel in `extra` and are shown on the coordinator's screen alongside the rest.

---

## 1. How the two halves fit together

```
   Visitor's browser
        │
        │  POST /api/public/enquiries        (anonymous, CORS-checked, rate-limited)
        ▼
   RM_CMS API  ──►  web_enquiry table        (raw, untrusted, NOT a church record)
        │
        │  a person with the WEB_COORDINATOR role reads the queue
        ▼
   /pages/admin/web-enquiries.html
```

**Nothing a stranger submits becomes a pastoral record on its own.** The submit endpoint
is open to the internet; writing straight into `person` would let anyone create people,
and the first spam run would sit beside real prayer requests with no way to tell them
apart. A submission lands in `web_enquiry`, a coordinator reads it, and decides.

---

## 1a. Running both locally

Four things have to line up. Get one wrong and the form appears to work while
nothing reaches the database.

**1. Start the CMS on its HTTPS profile.** In Visual Studio pick the `https` profile, or:

```bash
dotnet run --launch-profile https
```

That binds `https://localhost:7104` and `http://localhost:5043`.

**2. Point the site at the HTTPS port.** Create `.env.local` in the website project:

```bash
VITE_API_BASE_URL=https://localhost:7104
```

> **Not the http port.** `http://localhost:5043` answers every request with a 307 to
> https, and **browsers do not follow redirects on a CORS preflight** — the request fails
> before it is sent, with a console message that blames CORS rather than the redirect.

**3. Let the CMS accept the site's origin.** In `appsettings.Development.json`:

```json
"Auth": { "AllowedOrigins": [ "http://localhost:5173", "http://localhost:4173" ] }
```

5173 is `npm run dev`, 4173 is `npm run preview`. **Restart the CMS** — origins are read at
startup. The file is gitignored, so this never ships.

**4. Trust the dev certificate**, or the browser silently refuses the HTTPS call:

```bash
dotnet dev-certs https --trust
```

Then `npm run dev`, submit a form, and check:

```sql
SELECT reference_code, form_type, full_name, source_page FROM web_enquiry ORDER BY id DESC;
```

### When nothing arrives

`src/lib/forms.ts` prints the cause to the browser console — open it first. The three
usual answers:

| Symptom | Cause |
|---|---|
| "Not sent: VITE_API_BASE_URL is not set" | No `.env.local`, or Vite was not restarted after creating it. **Vite reads env files only at startup.** |
| Console names CORS / "Failed to fetch" | The origin is missing from `Auth:AllowedOrigins`, or the CMS was not restarted after adding it |
| Same, and the base URL is `http://…:5043` | The 307-to-https redirect on preflight. Use `https://localhost:7104` |

A form that reports success but writes no row should now be impossible: with no endpoint
configured the submission fails visibly, in development as well as production.

---

## 2. Deploy the website

It is static, so all three of these work. Pick by where you want the origin to sit,
because that decides whether CORS is involved at all.

### Option A — its own domain (recommended)

Site at `rmchurch.online`, API stays at `rmoffice.online`.

1. In Coolify: **New Resource → Application → Public Repository**, point at the website
   repo. **Build Pack: Nixpacks**, build command `npm run build`, publish directory `dist`.
2. Set the build-time environment variables. Vite inlines them, so they must be present
   **when the build runs**, not merely at runtime:
   ```bash
   VITE_API_BASE_URL=https://rmoffice.online
   VITE_SITE_URL=https://rmchurch.online
   ```
3. Give it the domain and let Coolify issue the certificate.
4. **Add the site's origin to the API's CORS allow-list** — see §3. Without this the
   browser blocks every submission before it leaves the page.

> `VITE_*` values are **public** — Vite bakes them into the client bundle. Never put a
> secret in one. The API base URL is not a secret; it is the address of a public endpoint.

Cleanest separation: a traffic spike or an outage on the public site cannot touch the
staff tooling.

### Option B — same origin as the CMS

Copy the site's files under the CMS's `wwwroot/` (say `wwwroot/site/`) and they are served
by the CMS itself.

- **No CORS needed** — same origin, so `Auth__AllowedOrigins` stays empty.
- But the public site then shares the CMS's `AllowedHosts`, its rate-limit budget and its
  deployment. A bot hammering the prayer form is drawing from the same bucket as staff.
- The CMS sets `no-store` on every `.html`, so the marketing site loses its caching.

Workable, and simplest if you want one deployment. Option A is better if the site gets
real traffic.

### Option C — any static host

Netlify, Cloudflare Pages, Apache, nginx. Upload the folder. Same CORS requirement
as Option A.

> Whichever you choose, serve it over **HTTPS**. The API is HTTPS-only in practice, and a
> browser refuses to send an HTTPS request from an HTTP page's script anyway.

---

## 3. Let the site call the API (CORS)

The API uses an explicit origin allow-list — there is no wildcard, and there cannot be one
because credentials are enabled on the policy.

On the **CMS** application in Coolify, add:

```bash
Auth__AllowedOrigins__0=https://rmchurch.online
```

Add `Auth__AllowedOrigins__1` for a second origin (a `www.` variant is a *different*
origin and needs its own entry). Restart the CMS after changing it.

Two rules the startup validator enforces, so a mistake stops the app rather than silently
disabling the site:

- entries must be **absolute URIs** — `https://rmchurch.online`, not `rmchurch.online`
- entries must use **https** outside Development

**Symptom of getting this wrong:** the browser console says the response is missing
`Access-Control-Allow-Origin`, the Network tab shows the request as blocked, and the API
log shows nothing at all — the request never reached it.

---

## 4. The API

Base URL: `https://rmoffice.online/api` (or your test host).

### 4.1 Submit a form

```
POST /api/public/enquiries
Content-Type: application/json
```

Anonymous. No token, no cookie.

| Field | Type | Notes |
|---|---|---|
| `formType` | string, **required** | One of the values from §4.2 |
| `fullName` | string ≤160 | Required for `MINISTRY_REGISTRATION` and `PLAN_VISIT` |
| `mobile` | string ≤20 | Required for those two. 10 digits starting 6-9; `+91` and spaces are accepted and stripped |
| `email` | string ≤255 | Optional, validated if present |
| `city` | string ≤100 | |
| `street` | string ≤200 | |
| `landmark` | string ≤200 | |
| `referredByName` | string ≤160 | The site's `refName` field |
| `referredByMobile` | string ≤20 | The site's `refMobile` field |
| `message` | string ≤4000 | The prayer request text, or any free message |
| `campusId` | string(26) | Optional. Omit unless the site knows which campus |
| `sourcePage` | string ≤255 | Defaults to the page's own path, e.g. `/ministries/bible-study-groups` |
| `website` | string ≤200 | **Honeypot — leave it empty.** See §5 |
| `extra` | object | Any extra string fields; max 20 keys, values truncated at 500 chars |

**Response is always HTTP 200** with the standard envelope:

```json
{ "responseType": 0, "message": "Thank you — your request has been received.",
  "data": { "referenceCode": "W0001", "receivedAt": "2026-09-08T07:44:43Z" } }
```

> **`responseType` is the thing to check, not the HTTP status.** `0` is success, `1` is a
> refusal you should show beside the form (`message` is already written for a visitor to
> read). A `fetch().then(r => r.ok)` check treats a refusal as a success — this API
> deliberately returns 200 for both.

A `429` means rate-limited; there is no JSON body on that one.

### 4.2 The form types

```
GET /api/public/enquiries/form-types
```

Anonymous. Returns the list the server will accept, so the site and the API cannot drift
apart silently:

| `code` | Use it for | Needs name + mobile |
|---|---|---|
| `MINISTRY_REGISTRATION` | The BSG registration form | yes |
| `PRAYER_REQUEST` | The prayer box | no — may be anonymous |
| `CONTACT_MESSAGE` | A general "get in touch" | no |
| `PLAN_VISIT` | "I'm coming this Sunday" | yes |
| `TEAM_REGISTRATION` | Volunteering for a serving team | yes |

A prayer request may be anonymous on purpose: someone may want prayer without leaving
their name, and demanding contact details would stop them asking at all.

### 4.3 The forms are already wired

`src/lib/forms.ts` in the React app posts this contract. Its four exported functions —
`submitPrayerRequest`, `submitBsgRegistration`, `submitTeamRegistration`, `submitVisitPlan`
— are unchanged in signature, so no form component needed editing.

Two things that file gets right and are easy to get wrong if you write your own client:

- **It checks `responseType`, not `response.ok`.** A refusal arrives as HTTP 200 with
  `responseType: 1`. A plain `if (response.ok)` would tell someone asking for prayer that
  their request was received when the server rejected it.
- **It treats 429 separately**, because that is the one response with no JSON body.

To point the site at a different CMS, change `VITE_API_BASE_URL` and rebuild. Nothing
else in the app knows the API exists.

## 4a. Events

The church calendar the website lists at `/events` and `/events/<slug>`.

Events used to be a hard-coded array in the website's own source, so publishing one meant
a code change and a redeploy. The CMS owns them now.

### Managing them

**CMS → Events.** Open to **administrators, pastors and the website coordinator** —
pastors because the calendar is theirs, the coordinator because publishing to the public
site is what that role is for.

- A new event is always a **draft**. Publishing is a separate button, so a half-typed
  event cannot reach the public site on a mis-click.
- **Cancel** rather than delete. The event stays on the site shown as cancelled, because
  somebody who saw it advertised will come looking and a dead link tells them nothing.
- **Times are local to the church.** Type 8:00 am and the server stores UTC and hands the
  website back `2027-04-02T08:00:00+05:30`. Nothing in either project does its own date
  arithmetic; a browser in another timezone would otherwise show a different answer.
- **The poster is uploaded**, one file per event. Staff pick the image from their own
  machine on the event editor; there is no picture list to choose from any more.

### The feed

```
GET /api/public/events
```

Anonymous, read-only, rate-limited with the same bucket as the forms. Returns published
and cancelled events — **never a draft**.

```json
{ "responseType": 0, "message": "1 event(s)", "data": [
  { "slug": "good-friday-service",
    "title": "Good Friday Service",
    "date": "2027-04-02T08:00:00+05:30",
    "endDate": "2027-04-02T12:00:00+05:30",
    "venue": "HCM Junior College, Ongole",
    "summary": "A morning of remembrance, worship and the Word.",
    "description": ["Join us as we remember the cross.", "The service runs…"],
    "image": "https://rmoffice.online/api/public/events/01J.../poster?v=6392…",
    "cancelled": false } ] }
```

`description` is the CMS's blank-line separated text split into paragraphs.

`image` is an **absolute URL** to the uploaded poster, or `null`. Absolute because the
website runs on a different origin — a path would resolve against the site and 404. The
field is still called `image` because it used to carry a picture slug.

The `?v=` is the upload's timestamp. The address is otherwise stable for the life of the
event, so without it a replaced poster would keep showing the old picture until every
visitor's cache expired.

### On the website

`src/lib/events.ts` fetches, `src/hooks/useEvents.ts` shares one request across the three
places that want the list — the homepage band, the Events page and an individual event
page. Nothing else changed.

### The poster

```
POST   /api/events/{id}/poster     multipart, field name "file"
DELETE /api/events/{id}/poster
GET    /api/public/events/{id}/poster    anonymous, what the site embeds
```

JPEG, PNG or WebP, up to 6 MB. **The format is decided by reading the first bytes of the
file**, and the content type served back is the one the server decided — never the one the
upload announced. A file renamed `poster.jpg` and sent as `image/jpeg` is refused if it is
not really an image, which is what stops an upload becoming a page that runs on the API's
own origin. SVG is refused for the same reason: it is a document and can carry script.

There is no picture list to keep in step any more. The old contract mirrored the website's
`src/data/generated/media.ts` in a C# file, and the CMS would only offer those twenty-odd
stock plates — so the poster actually designed for the event was never one of the options
and two unrelated events routinely showed the same photograph.

**The bytes live in the database**, in `church_event_poster`, not in `wwwroot`. Coolify
rebuilds the container on every push, so a file written into it survives exactly until the
next deploy — every poster would vanish and the events would quietly go back to having no
picture. Avoiding that with a file would need a mounted volume, and forgetting to configure
one fails invisibly until a visitor looks.

**Two headers matter on the serving endpoint.** `Cross-Origin-Resource-Policy:
cross-origin`, because the API's default is `same-origin` and a browser drops a
cross-origin image under that with nothing in the console to say why. And a long
`Cache-Control` with `immutable`, which is safe only because the URL carries the `?v=`
above.

The trade-off, stated plainly: an uploaded poster skips the site's build-time image
pipeline, so it has no AVIF or WebP variants, no `srcset` and no blurred placeholder. The
event card and hero render it as a plain `<img>` inside a fixed-ratio box so the layout
still does not shift. Showing the church's real poster is worth more than showing a stock
plate in four optimised widths.

### One trade-off worth knowing

**Individual events are no longer listed in `sitemap.xml`.** The sitemap is generated at
build time and events are published afterwards, so listing them would produce a file that
is stale the moment somebody adds one. `/events` is still in the sitemap with a weekly
change frequency and every event is linked from it, so crawlers reach them the ordinary
way. If per-event entries turn out to matter, the honest fix is for the CMS to serve that
part of the sitemap rather than the build script guessing.

---

## 5. What stops abuse

This is the only anonymous write in the whole application, so it has four layers. Worth
knowing so you do not mistake one firing for a bug.

| Layer | Limit | What the caller sees |
|---|---|---|
| CORS | Only listed origins | Browser blocks it; never reaches the API |
| Rate limiter | 12 requests / 5 min per IP | `429`, no body |
| Per-submitter throttle | 10 submissions / hour per fingerprint | 200, `responseType 1`, "Too many submissions from this device" |
| Honeypot | any value in `website` | 200, accepted, flagged and hidden from the default queue |

**The honeypot must stay empty and hidden.** Render it off-screen with no label and
`tabindex="-1"` / `autocomplete="off"` so a person never fills it in. A submission with it
filled is still *accepted* — replying with an error would just tell the bot which field to
leave blank next time — but it is flagged and kept out of the coordinator's view.

The per-submitter fingerprint is a SHA-256 of IP + user agent **salted with the date**, so
it rotates at midnight. That is deliberate: the only question worth answering is "did
these forty arrive from one place today", and a hash that never changed would be a
permanent identifier for somebody who did nothing but ask for prayer.

---

## 6. The coordinator

### Create one

**Admin → Users → Add a user**, role **Website Coordinator**. It needs a 10-digit mobile
(that is the username) and a password.

Signing in lands them straight on **`/pages/admin/web-enquiries.html`**, which is the only
screen they can open — every other route returns 403. That is the whole point of the role:
somebody can work the website queue without being given the pastoral records.

### The queue

- Counters double as filters; **New** is the only one that means "do something"
- Spam is hidden by default and never deleted, so the volume of attempts stays visible
- A mobile that matches somebody already on file is **called out before any decision**,
  because creating a second copy of an existing person is the mistake this queue is most
  likely to produce
- Statuses: `NEW` → `IN_REVIEW` → `ACTIONED` / `CLOSED`, or `SPAM`. Marking done or closed
  **requires a note** — it is what the next person reads
- An enquiry cannot go back to `NEW` once read

### Endpoints behind it

| Route | Who |
|---|---|
| `GET /api/web-enquiries` | ADMIN, WEB_COORDINATOR |
| `GET /api/web-enquiries/{id}` | ADMIN, WEB_COORDINATOR |
| `PUT /api/web-enquiries/{id}/status` | ADMIN, WEB_COORDINATOR |

---

## 7. Deploying the API side

> **If your database was built from `schema.sql` before 2026-09-08**, it has no
> `WEB_COORDINATOR` row in `app_role` and creating one of these users will fail with
> "Unable to create the account." Run migration `006` (below) — it inserts the role — or
> reload the corrected `schema.sql`.

1. Apply the migration to whichever database the CMS uses:
   ```bash
   mysql -u <user> -p <database> < docs/database/migrations/006_web_enquiries.sql
   ```
   It creates `web_enquiry` and inserts the `WEB_COORDINATOR` role. Safe to re-run.
2. Set `Auth__AllowedOrigins__0` to the website's origin (§3) and restart.
3. Deploy the CMS as usual — see [`COOLIFY.md`](COOLIFY.md).
4. Create a coordinator account (§6).
5. Smoke-test:
   ```bash
   curl -X POST https://rmoffice.online/api/public/enquiries -H "Content-Type: application/json" -d '{"formType":"PRAYER_REQUEST","message":"test"}'
   ```
   Expect `responseType: 0` and a reference code. Then confirm it appears in the queue and
   **delete it** — it is a real row.

---

## 8. Still to come

You said the workflow after a coordinator reads an enquiry is still being decided, so it
is deliberately not built. What exists is the queue and the triage states.

When you are ready, the natural next step is a **"Record as visitor"** action that turns
an enquiry into a `person` (and optionally a case) through `IPeopleService`, so it inherits
the duplicate detection the intake screen already has, and stamps `linked_person_id` —
the column is already there and already surfaced in the API and the screen.

`connection_source` already has `WEBSITE` and `FRIEND_INVITE`, which is what a website
registration with a referrer would be filed as.

---

## See also

- [`COOLIFY.md`](COOLIFY.md) — deploying the CMS itself
- [`../architecture/FRONTEND.md`](../architecture/FRONTEND.md) — the CMS's own front end
- [`../testing/TESTING.md`](../testing/TESTING.md) §8.7 — what was verified here
