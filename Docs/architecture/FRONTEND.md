# Frontend layout

The whole UI is static files under `wwwroot/`, served by `UseStaticFiles`. There is no
build step, no bundler and no framework — pages are hand-written HTML that call the API
with `fetch` or jQuery `$.ajax`.

Restructured 2026-09-07. What follows is the layout and the rules that keep it coherent.

---

## Layout

```
wwwroot/
  assets/
    css/      admin.css, team-lead-shell.css, toast.css
    img/      favicon.ico, favicon-16x16.png, favicon-32x32.png, logo.jpeg
    js/       one file per page, plus the shared modules below
    vendor/   jQuery and Bootstrap, committed — see vendor/README.md
  pages/
    auth/       login, change-password, link-telegram
    admin/      users, add-user, accounts, teams, campuses, areas, settings, telegram
    care/       my-assignments, assign-cases, pipeline, visitor-journey,
                escalations, check-ins
    dashboard/  team-lead, pastor
    intake/     record-visitor
```

### Why grouped by function, not by role

The old tree had `Admin/`, `Volunteers/`, `TeamLeads/`, `Peoples/`, `Pastor/` — folders
named after who was expected to use each screen. That stopped being true as the app grew:
`TeamLeads/Escalations.html` is in the pastor's nav, `Admin/teams.html` is too, and
`Peoples/ManualAssignments.html` is a team lead screen. A reader looking for the
escalation screen had to know its history, not its purpose.

`care/` holds the screens about following someone up, whoever is doing it. Who may open
each one is decided by `AdminShell.boot({roles})` and re-checked by the API — never by
which folder a file sits in.

`auth/` is the exception that is genuinely about identity rather than function, and it is
also the only group any signed-out visitor can reach.

---

## Naming

**Files are lowercase kebab-case.** `record-visitor.html`, not `PeopleEntry.html`.

This is not only tidiness. The dev machine is Windows (case-insensitive) and the container
is Linux (case-sensitive), so a link whose casing does not match the file works locally and
**404s in production**. One convention, applied to filenames and to every reference,
removes the whole class of bug. The tree had already drifted — `assets/Images/` beside
`assets/css/`, `PeopleEntry.html` beside `add-user.html`.

**A page and its script share a name.** `pages/care/assign-cases.html` loads
`assets/js/assign-cases.js`. Shared modules are the exception and are named for what they
provide (`auth.js`, `toast.js`, `admin-shell.js`).

**Names say what the screen does, not who opens it.** `my-assignments.html` is the
volunteer's own work list; `assign-cases.html` is the team lead handing cases out. The old
`Assignments.html` / `ManualAssignments.html` pair did not distinguish those.

---

## Paths are absolute

Every `src`, `href` and navigation target is an absolute path from the site root:
`/assets/js/auth.js`, `/pages/dashboard/team-lead.html`.

Relative paths were the single biggest source of breakage during the restructure. A page
one folder deeper needed `../../assets/`, the same asset was referenced three different
ways, and a link built by concatenation — `ROOT + '/TeamLeads/TeamLeadDashboard.html'` in
the two shells — was invisible to a project-wide search for the path it produced, so it
survived a rename untouched and pointed at nothing.

An absolute path is greppable, identical from every page, and unaffected by moving a file.

---

## Shared modules

Load order matters: `api-config.js` → `auth.js` → everything else.

| File | What it does |
|---|---|
| `api-config.js` | Defines `API_BASE_URL` as `<origin>/api`. Must load first. |
| `auth.js` | Session, token refresh, and role gating. **Intercepts both `$.ajax` and `window.fetch`**, so calls in page scripts are already authenticated — do not add headers by hand. Redirects to the login page on 401. |
| `admin-shell.js` | The header and nav for admin-themed screens. Adding a page means adding a line to its `NAV` array. |
| `team-lead-shell.js` / `pastor-shell.js` | The equivalent for the team lead and pastor screens, which have their own theme. |
| `toast.js` | `showToast(message, 'success' \| 'warning' \| 'error')`. |
| `area-picker.js` | The type-ahead used by both intake and add-user. Plain DOM, no jQuery, because intake does not load jQuery. |
| `mobile-input.js`, `password-policy.js`, `utils.js` | Input rules shared between screens that must agree. |

---

## Two themes, deliberately

`admin.css` is for admin and intake screens. `team-lead-shell.css` + Bootstrap is for the
volunteer, team lead and pastor screens, which have their own established look.

**Do not migrate a screen from one to the other.** People already use these pages; an
earlier attempt to rebuild the team lead dashboard on the admin theme was rejected.
Rewiring a page's data layer means the same file, same ids, same markup.

---

## Third-party libraries are vendored

jQuery and Bootstrap are committed under `assets/vendor/` and served from this origin.
They were on public CDNs, which meant the CSP had to allow `code.jquery.com` and
`cdn.jsdelivr.net` to execute script — a supply-chain exposure CSP cannot close, because
naming a trusted origin is exactly what it does and a compromised CDN is still that origin.

With nothing cross-origin, `script-src` / `style-src` / `font-src` are `'self'` and carry
no allowlist at all. See [`../../wwwroot/assets/vendor/README.md`](../../wwwroot/assets/vendor/README.md)
for versions, provenance and how to upgrade one safely.

Vendoring also fixed a real inconsistency: pages were split between jQuery **3.6.0** and
**3.7.1**. Everything is on 3.7.1 now.

---

## Things that will bite

- **No iframes, ever.** The app sends `X-Frame-Options: DENY` and CSP
  `frame-ancestors 'none'`. Several screens once used iframe modals and were silently
  dead. Detail views navigate. The headers are deliberate clickjacking defence.

- **A refusal arrives as HTTP 200 with `responseType: 1`.** `ApiResponse` wraps warnings
  in a 200, so jQuery `.done()` and a `fetch` success handler both fire for them. Check
  `responseType !== 0` in every handler, or failures are reported as successes.

- **Cache headers are set for you.** `.html` is `no-store`; `.js` and `.css` are
  `no-cache, must-revalidate`. Filenames carry no version hash, so without this a browser
  runs the previous deployment's script against the new API — which fails in ways that
  look like application bugs. Do not add far-future caching without versioned filenames.

- **`GET /` is mapped in `Program.cs`**, not served as a directory index. It sends
  `pages/auth/login.html`. Moving or renaming the login page means editing that route too.

---

## Adding a page

1. Create `pages/<group>/<name>.html` and `assets/js/<name>.js`, same base name.
2. Load `api-config.js`, then `auth.js`, then the shell, then the page script.
3. Boot it: `AdminShell.boot({ roles: [...], active: { href: '/pages/<group>/<name>.html' } })`.
4. Add it to the shell's `NAV` array if it needs a nav entry.
5. Use absolute paths for everything.
6. The roles in `boot` are a courtesy that hides what the caller cannot use. **The API
   enforces access again on every request** — never treat the client check as the control.
