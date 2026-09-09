# Vendored third-party front-end libraries

These were served from public CDNs (`code.jquery.com`, `cdn.jsdelivr.net`) and are now
committed here and served from our own origin.

**Why.** Executing third-party script from a CDN is a supply-chain risk that CSP cannot
mitigate — a compromised or hijacked CDN serves whatever it likes into pages that handle
pastoral records and authentication tokens, and `script-src https://cdn.example` permits
it by definition. `SecurityHeadersMiddleware` carried a standing FOLLOW-UP note to do
exactly this. Vendoring also removed a real inconsistency: pages were split between
jQuery **3.6.0** and **3.7.1**, so behaviour differed depending on which screen you were
on.

With nothing loaded cross-origin any more, the CSP `script-src` / `style-src` /
`font-src` directives are `'self'` and carry no CDN allowlist at all.

Secondary benefits: the app works with no outbound internet access, and a blocked or slow
CDN can no longer stall or break a screen.

## What is here

| File | Version | Source | Integrity (verified on download) |
|---|---|---|---|
| `jquery/jquery-3.7.1.min.js` | 3.7.1 | `https://code.jquery.com/jquery-3.7.1.min.js` | `sha256-/JqT3SQfawRcv/BIHPThkBvs0OEvtFFmqPF/lYI/Cxo=` |
| `bootstrap/bootstrap.min.css` | 5.3.0 | `https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css` | `sha384-9ndCyUaIbzAi2FUVXJi0CjmCapSmO7SnpJef0486qhLnuZ2cdeRhO02iuK6FUUVM` |
| `bootstrap/bootstrap.bundle.min.js` | 5.3.0 | `https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/js/bootstrap.bundle.min.js` | `sha384-geWF76RCwLtnZ8qwWowPQNguL3RmwHVBC9FhGdlKrxdiJJigb/j/68SIy3Te4Bkz` |

Each was checked against the hash the publisher documents for that exact file, on
2026-09-07, before being committed. Both hashes matched.

The only edit made to any file is the removal of the trailing `sourceMappingURL` comment
from the two Bootstrap files. The `.map` files are not distributed here, so the comment
only produced a 404 in browser devtools. License banners are untouched.

## Upgrading

1. Download the new version from the same publisher.
2. **Verify it against the publisher's own SRI hash before committing it** — that check is
   the entire security value of vendoring. Downloading without checking just moves an
   unverified file into the repo, where it looks trustworthy:
   ```bash
   openssl dgst -sha384 -binary <file> | openssl base64 -A
   ```
3. Strip the `sourceMappingURL` comment.
4. Update the table above with the new version and hash.
5. Update the filename in every page that references it — `jquery-<version>.min.js` is
   versioned in the filename on purpose, so a stale reference 404s loudly at once instead
   of silently serving the old library.

## Licences

jQuery and Bootstrap are both MIT-licensed. The license banners at the top of each
minified file are preserved, which is what the MIT terms require for redistribution.
