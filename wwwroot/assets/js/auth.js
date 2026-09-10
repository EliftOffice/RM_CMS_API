/*
 * RM_CMS — client-side authentication.
 *
 * TOKEN STORAGE
 *   Access token  : held in a closure variable only. Never localStorage, never
 *                   sessionStorage, never a readable cookie. It dies with the tab,
 *                   and a stored XSS payload cannot read it back out of storage.
 *   Refresh token : an HttpOnly + Secure + SameSite=Strict cookie set by the server.
 *                   JavaScript cannot read it at all; the browser attaches it only to
 *                   same-site requests to /api/auth/refresh.
 *
 * On page load the access token is gone (it was only in memory), so bootstrap() calls
 * /api/auth/refresh to trade the cookie for a fresh one. That is the "stay signed in"
 * mechanism — no long-lived token is ever exposed to script.
 *
 * Every jQuery and fetch call is intercepted so callers do not have to remember to
 * attach the header, and a 401 transparently refreshes and retries once.
 *
 * Load order matters — this file must come after api-config.js and jQuery, and before
 * any page script that issues requests.
 */
(function (window, $) {
    'use strict';

    var ACCESS_TOKEN = null;      // in-memory only, by design
    var CURRENT_USER = null;
    var EXPIRES_AT = 0;           // epoch ms
    var refreshPromise = null;    // de-duplicates concurrent refreshes

    var LOGIN_PAGE = '/pages/auth/login.html';
    var CHANGE_PASSWORD_PAGE = '/pages/auth/change-password.html';
    var LINK_TELEGRAM_PAGE = '/pages/auth/link-telegram.html';

    // Endpoints that must never trigger the refresh-and-retry loop.
    //
    // These are the anonymous ones. The interceptor demands a token for anything
    // NOT listed here and rejects with "Not authenticated" when there is none — so
    // an anonymous endpoint missing from this list fails on exactly the pages that
    // have no session yet (sign-in, change-password), which is where it is needed.
    // Keep it in step with the [AllowAnonymous] actions on AuthController.
    var AUTH_ENDPOINTS = [
        '/api/auth/login',
        '/api/auth/refresh',
        '/api/auth/logout',
        '/api/auth/password-policy'
    ];

    function apiBase() {
        return (typeof API_BASE_URL !== 'undefined')
            ? API_BASE_URL
            : window.location.origin + '/api';
    }

    /**
     * Reads a property regardless of casing.
     *
     * The API serialises camelCase (the ASP.NET Core default the whole frontend was
     * built against), but being tolerant here means a future serializer change cannot
     * silently break sign-in — which is exactly the kind of failure that locks
     * everyone out of a live system.
     */
    function pick(obj, name) {
        if (!obj) return undefined;
        var camel = name.charAt(0).toLowerCase() + name.slice(1);
        var pascal = name.charAt(0).toUpperCase() + name.slice(1);
        return obj[camel] !== undefined ? obj[camel] : obj[pascal];
    }

    /** Unwraps the ApiResponse<T> envelope. */
    function unwrap(body) { return pick(body, 'data'); }

    function origin() {
        return window.location.origin;
    }

    function absolute(url) {
        if (!url) return '';
        if (/^https?:\/\//i.test(url)) return url;
        return origin() + (url.charAt(0) === '/' ? url : '/' + url);
    }

    /** Only same-origin /api calls get the Authorization header — never a third-party CDN. */
    function isOurApi(url) {
        var abs = absolute(url);
        return abs.indexOf(origin() + '/api') === 0;
    }

    function isAuthEndpoint(url) {
        var abs = absolute(url);
        for (var i = 0; i < AUTH_ENDPOINTS.length; i++) {
            if (abs.indexOf(origin() + AUTH_ENDPOINTS[i]) === 0) return true;
        }
        return false;
    }

    function onLoginPage() {
        var path = window.location.pathname.toLowerCase();
        return path === '/' || path.indexOf('login.html') !== -1;
    }

    // ------------------------------------------------------------------
    // Token state
    // ------------------------------------------------------------------

    function applyAuthResult(payload) {
        var accessToken = pick(payload, 'accessToken');
        if (!payload || !accessToken) return false;

        ACCESS_TOKEN = accessToken;
        // The identity module returns `account`; older builds returned `user`.
        CURRENT_USER = pick(payload, 'account') || pick(payload, 'user') || null;

        // Refresh slightly early so a request is never sent with a token that
        // expires mid-flight.
        var lifetimeMs = (pick(payload, 'expiresInSeconds') || 0) * 1000;
        EXPIRES_AT = Date.now() + Math.max(0, lifetimeMs - 30000);

        return true;
    }

    function clearTokens() {
        ACCESS_TOKEN = null;
        CURRENT_USER = null;
        EXPIRES_AT = 0;
    }

    function tokenIsFresh() {
        return !!ACCESS_TOKEN && Date.now() < EXPIRES_AT;
    }

    // ------------------------------------------------------------------
    // Refresh (single-flight)
    // ------------------------------------------------------------------

    function refresh() {
        if (refreshPromise) return refreshPromise;   // collapse a burst into one call

        refreshPromise = new Promise(function (resolve) {
            window.fetch(apiBase() + '/auth/refresh', {
                method: 'POST',
                credentials: 'include',              // sends the HttpOnly refresh cookie
                headers: { 'Content-Type': 'application/json' },
                body: '{}'
            })
                .then(function (response) {
                    if (!response.ok) { clearTokens(); resolve(false); return null; }
                    return response.json();
                })
                .then(function (body) {
                    if (!body) return;
                    resolve(applyAuthResult(unwrap(body)));
                })
                .catch(function () { clearTokens(); resolve(false); })
                .then(function () { refreshPromise = null; });
        });

        return refreshPromise;
    }

    /** Returns a valid access token, refreshing first if it is missing or stale. */
    function ensureToken() {
        if (tokenIsFresh()) return Promise.resolve(ACCESS_TOKEN);

        return refresh().then(function (ok) { return ok ? ACCESS_TOKEN : null; });
    }

    // ------------------------------------------------------------------
    // Session lifecycle
    // ------------------------------------------------------------------

    function redirectToLogin() {
        if (onLoginPage()) return;
        clearTokens();
        window.location.href = origin() + LOGIN_PAGE;
    }

    function retryAfter(response) {
        var header = response.headers && response.headers.get('Retry-After');
        var seconds = parseInt(header, 10);

        return isNaN(seconds) ? null : seconds;
    }

    /**
     * Turns a failed sign-in into something the person can act on.
     *
     * The old behaviour reported "Sign-in failed." for anything without a `message`
     * field. A rate-limit response is RFC 7807 ProblemDetails, which has `title` and
     * `detail` but no `message` — so being locked out for five minutes looked exactly
     * like a wrong password, and the natural response (try again immediately) was the
     * one thing guaranteed not to work.
     *
     * Note the deliberate asymmetry: a genuine credential failure still gets the
     * server's single generic message, because distinguishing "no such user" from
     * "wrong password" would let someone enumerate accounts. Only the failures that
     * are NOT about credentials are explained.
     */
    function describeLoginFailure(response, body) {
        var serverMessage = pick(body, 'message');

        if (response.status === 429) {
            var seconds = retryAfter(response);

            if (seconds && seconds > 0) {
                var minutes = Math.ceil(seconds / 60);
                return 'Too many sign-in attempts. Please wait ' +
                       (minutes > 1 ? minutes + ' minutes' : 'a minute') + ' and try again.';
            }

            return 'Too many sign-in attempts. Please wait a few minutes and try again.';
        }

        if (response.status === 400) {
            // Model-binding failure: the field rules, not the credentials.
            var problems = body && body.errors;

            if (problems) {
                for (var key in problems) {
                    if (Object.prototype.hasOwnProperty.call(problems, key) && problems[key].length) {
                        return problems[key][0];
                    }
                }
            }

            return serverMessage || 'Check the mobile number and password and try again.';
        }

        if (response.status === 403) {
            return 'This account is not allowed to sign in. Contact an administrator.';
        }

        if (response.status >= 500) {
            return 'The server had a problem signing you in. Try again shortly.';
        }

        // Credential failures land here and keep the server's generic wording.
        return serverMessage || pick(body, 'detail') || 'Sign-in failed.';
    }

    /**
     * Asks whether this mobile number needs a password.
     *
     * The login screen calls it once the number is complete and then either signs the
     * person in or reveals the password box. Some of the people who use this system
     * cannot read a password prompt, so an administrator can mark their account as
     * signing in with the number alone.
     *
     * ALWAYS RESOLVES, and resolves to `true` on any doubt — an unreadable answer, a
     * refusal, a dead connection. Failing towards the password box is the safe
     * direction: the worst case is a password prompt somebody did not need, where the
     * opposite would be a sign-in attempt that silently could not work.
     *
     * The URL starts with /api/auth/login, so auth.js's own interceptor already treats
     * it as an auth endpoint and does not try to attach a token to it.
     */
    function loginMethod(username) {
        return window.fetch(apiBase() + '/auth/login-method', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ Username: username })
        }).then(function (response) {
            if (!response.ok) return { requiresPassword: true };

            return response.json().then(function (body) {
                var payload = unwrap(body);

                return {
                    requiresPassword: !payload || pick(payload, 'requiresPassword') !== false
                };
            });
        }).catch(function () {
            return { requiresPassword: true };
        });
    }

    /**
     * Asks the server to send the Telegram confirmation for a pending sign-in.
     *
     * The challenge id is the only thing the browser holds, and it was handed out to
     * a browser that already passed the first factor. It is not the thing that
     * approves the sign-in — that token lives in the Telegram button and never comes
     * near this page, which is what stops the browser confirming itself.
     */
    function sendTelegramVerification(challengeId) {
        return window.fetch(apiBase() + '/auth/verify/send', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ChallengeId: challengeId })
        }).then(function (response) {
            return response.json().catch(function () { return null; }).then(function (body) {
                return {
                    // responseType 0 == Success in Utilities/ApiResponse.cs
                    sent: response.ok && pick(body, 'responseType') === 0,
                    message: pick(body, 'message') || '',
                    retryAfterSeconds: retryAfter(response)
                };
            });
        }).catch(function () {
            return { sent: false, message: 'Unable to reach the server.' };
        });
    }

    /**
     * Asks whether the tap has arrived.
     *
     * The poll that finds the approval is also the one that receives the session, so
     * this applies it exactly like `login` does. Every other poll returns WAITING and
     * changes nothing.
     */
    function pollTelegramVerification(challengeId) {
        return window.fetch(apiBase() + '/auth/verify/poll', {
            method: 'POST',
            credentials: 'include',      // the refresh cookie is written on approval
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ChallengeId: challengeId })
        }).then(function (response) {
            return response.json().catch(function () { return null; }).then(function (body) {
                var data = pick(body, 'data');

                if (!response.ok || !data) return { outcome: 'WAITING' };

                var outcome = pick(data, 'outcome');
                var session = pick(data, 'session');

                if (outcome === 'APPROVED' && session) {
                    applyAuthResult(session);

                    return {
                        outcome: 'APPROVED',
                        user: CURRENT_USER,
                        mustChangePassword: !!pick(session, 'mustChangePassword')
                    };
                }

                return {
                    outcome: outcome || 'WAITING',
                    expiresInSeconds: pick(data, 'expiresInSeconds') || 0
                };
            });
        }).catch(function () {
            // A dropped poll is not a failed sign-in — the confirmation may still be
            // sitting on the person's phone. Keep waiting.
            return { outcome: 'WAITING' };
        });
    }

    function login(username, password, deviceLabel) {
        return window.fetch(apiBase() + '/auth/login', {
            method: 'POST',
            credentials: 'include',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                Username: username,
                Password: password,
                DeviceLabel: deviceLabel || navigator.userAgent.substring(0, 100)
            })
        }).then(function (response) {
            return response.json().catch(function () { return null; }).then(function (body) {
                var payload = unwrap(body);

                if (!response.ok || !payload) {
                    return {
                        success: false,
                        message: describeLoginFailure(response, body),
                        retryAfterSeconds: retryAfter(response)
                    };
                }

                // The credential was right but the sign-in is not finished: this
                // account confirms on Telegram. Checked BEFORE applyAuthResult,
                // because there is no token in this payload and treating it as a
                // session would leave the page believing it was signed in.
                if (pick(payload, 'requiresTelegramVerification')) {
                    return {
                        success: false,
                        pendingTelegram: true,
                        challengeId: pick(payload, 'challengeId'),
                        message: pick(body, 'message') || 'Confirm this sign-in on Telegram.'
                    };
                }

                applyAuthResult(payload);

                return {
                    success: true,
                    user: CURRENT_USER,
                    mustChangePassword: !!pick(payload, 'mustChangePassword')
                };
            });
        }).catch(function () {
            return { success: false, message: 'Unable to reach the server. Check your connection.' };
        });
    }

    function logout() {
        return window.fetch(apiBase() + '/auth/logout', {
            method: 'POST',
            credentials: 'include'
        }).catch(function () { /* sign out locally regardless */ })
          .then(function () {
              clearTokens();
              window.location.href = origin() + LOGIN_PAGE;
          });
    }

    /**
     * Called on every protected page. Trades the refresh cookie for an access token;
     * bounces to the login page if there is no valid session.
     */
    function bootstrap(options) {
        options = options || {};

        return refresh().then(function (ok) {
            if (!ok) {
                if (!options.allowAnonymous) redirectToLogin();
                return false;
            }

            if (CURRENT_USER && pick(CURRENT_USER, 'mustChangePassword') &&
                window.location.pathname.indexOf('ChangePassword') === -1) {
                window.location.href = origin() + CHANGE_PASSWORD_PAGE;
                return false;
            }

            if (options.roles && options.roles.length && !hasAnyRole(options.roles)) {
                // Signed in, but not for this page.
                window.location.href = origin() + LOGIN_PAGE;
                return false;
            }

            return requireTelegramIfNeeded();
        });
    }

    /**
     * The telegram.require_linking gate, checked on EVERY page rather than once at
     * sign-in.
     *
     * It used to run only in the login handler, so the linking screen appeared and
     * then the Back button went straight round it — the browser restored the previous
     * page and nothing asked the question again. The server refuses those calls now;
     * this is what turns that refusal into a redirect instead of a broken screen.
     *
     * Administrators are exempt, matching the server: they are the only role who can
     * switch the setting off, and that screen must stay reachable.
     */
    /**
     * The back-forward cache restores a page WITHOUT re-running its scripts, so a
     * gate that only runs at load would be skipped entirely by a Back press. This is
     * the one event that fires on such a restore.
     *
     * Registered once, and only re-checks on a genuine bfcache restore
     * (event.persisted) — a normal load has already been through bootstrap.
     */
    window.addEventListener('pageshow', function (event) {
        if (event.persisted && ACCESS_TOKEN) requireTelegramIfNeeded();
    });

    function requireTelegramIfNeeded() {
        if (window.location.pathname.indexOf('LinkTelegram') !== -1) return true;
        if (hasAnyRole(['ADMIN'])) return true;

        return fetch(origin() + '/api/telegram/status', {
            headers: { Authorization: 'Bearer ' + ACCESS_TOKEN }
        })
            .then(function (res) { return res.ok ? res.json() : null; })
            .then(function (body) {
                var status = (body && body.data) || {};

                // Not configured means the organisation has no bot yet; blocking
                // everyone out of the application over that would be worse than the
                // gap it is trying to close.
                if (!status.isRequired || !status.isConfigured || status.isLinked) return true;

                sessionStorage.setItem('rm_post_login_target',
                    window.location.pathname + window.location.search);

                window.location.href = origin() + LINK_TELEGRAM_PAGE;
                return false;
            })
            .catch(function () { return true; });   // never lock a page over a failed check
    }

    /**
     * Role codes held by the signed-in account.
     *
     * The API returns role GRANTS ({ roleCode, campusId }) rather than bare
     * strings, because a role can be scoped to one campus. Callers only ever
     * need the codes, so flatten here and keep that shape in one place.
     */
    function roleCodes() {
        var granted = pick(CURRENT_USER, 'roles') || [];

        return granted.map(function (r) {
            return (typeof r === 'string') ? r : pick(r, 'roleCode');
        }).filter(Boolean);
    }

    function hasAnyRole(roles) {
        var mine = roleCodes();

        for (var i = 0; i < roles.length; i++) {
            if (mine.indexOf(roles[i]) !== -1) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // jQuery interception
    //
    // The app is built on $.get/$.post/$.ajax. Wrapping $.ajax itself catches all
    // three, so no page script needs to change to become authenticated.
    // ------------------------------------------------------------------

    function installJQueryInterceptor() {
        if (!$ || !$.ajax || $.ajax.__rmAuthWrapped) return;

        var originalAjax = $.ajax;

        function wrapped(url, options) {
            // jQuery accepts $.ajax(url, settings) or $.ajax(settings).
            if (typeof url === 'object') { options = url; url = undefined; }
            options = options || {};
            if (url) options.url = url;

            if (!isOurApi(options.url)) return originalAjax(options);

            var deferred = $.Deferred();

            // Pull the caller's callbacks out so a 401 that we are about to retry does
            // not fire their error handler first.
            var userSuccess = options.success;
            var userError = options.error;
            var userComplete = options.complete;
            delete options.success; delete options.error; delete options.complete;

            function attempt(token, isRetry) {
                var settings = $.extend({}, options);
                settings.headers = $.extend({}, options.headers);

                if (token) settings.headers['Authorization'] = 'Bearer ' + token;
                settings.xhrFields = $.extend({}, options.xhrFields, { withCredentials: true });

                originalAjax(settings)
                    .done(function (data, textStatus, jqXHR) {
                        if (userSuccess) userSuccess(data, textStatus, jqXHR);
                        if (userComplete) userComplete(jqXHR, textStatus);
                        deferred.resolve(data, textStatus, jqXHR);
                    })
                    .fail(function (jqXHR, textStatus, errorThrown) {
                        if (jqXHR.status === 401 && !isRetry && !isAuthEndpoint(settings.url)) {
                            // Access token expired or was revoked — refresh once, then retry.
                            refresh().then(function (ok) {
                                if (ok) { attempt(ACCESS_TOKEN, true); return; }

                                if (userError) userError(jqXHR, textStatus, errorThrown);
                                if (userComplete) userComplete(jqXHR, textStatus);
                                deferred.reject(jqXHR, textStatus, errorThrown);
                                redirectToLogin();
                            });
                            return;
                        }

                        if (userError) userError(jqXHR, textStatus, errorThrown);
                        if (userComplete) userComplete(jqXHR, textStatus);
                        deferred.reject(jqXHR, textStatus, errorThrown);
                    });
            }

            if (isAuthEndpoint(options.url)) {
                attempt(null, true);
            } else {
                ensureToken().then(function (token) {
                    if (!token) {
                        deferred.reject({ status: 401, responseJSON: null }, 'error', 'Unauthorized');
                        redirectToLogin();
                        return;
                    }
                    attempt(token, false);
                });
            }

            return deferred.promise();
        }

        wrapped.__rmAuthWrapped = true;
        $.ajax = wrapped;
    }

    // ------------------------------------------------------------------
    // fetch interception (for the handful of pages that use fetch directly)
    // ------------------------------------------------------------------

    function installFetchInterceptor() {
        var originalFetch = window.fetch;
        if (originalFetch.__rmAuthWrapped) return;

        function wrapped(input, init) {
            var url = (typeof input === 'string') ? input : (input && input.url);

            if (!isOurApi(url) || isAuthEndpoint(url)) return originalFetch(input, init);

            init = init || {};

            function send(token) {
                var settings = Object.assign({}, init);
                settings.credentials = 'include';
                settings.headers = new Headers(init.headers || {});
                if (token) settings.headers.set('Authorization', 'Bearer ' + token);

                return originalFetch(input, settings);
            }

            return ensureToken().then(function (token) {
                if (!token) { redirectToLogin(); return Promise.reject(new Error('Not authenticated')); }

                return send(token).then(function (response) {
                    if (response.status !== 401) return response;

                    return refresh().then(function (ok) {
                        if (!ok) { redirectToLogin(); return response; }
                        return send(ACCESS_TOKEN);   // retry once
                    });
                });
            });
        }

        wrapped.__rmAuthWrapped = true;
        window.fetch = wrapped;
    }

    // ------------------------------------------------------------------
    // Public surface
    // ------------------------------------------------------------------

    var RmAuth = {
        login: login,
        loginMethod: loginMethod,
        sendTelegramVerification: sendTelegramVerification,
        pollTelegramVerification: pollTelegramVerification,
        logout: logout,
        refresh: refresh,
        bootstrap: bootstrap,
        ensureToken: ensureToken,
        redirectToLogin: redirectToLogin,
        hasAnyRole: hasAnyRole,
        roleCodes: roleCodes,
        getUser: function () { return CURRENT_USER; },
        pick: pick,
        isAuthenticated: function () { return !!ACCESS_TOKEN; },

        changePassword: function (currentPassword, newPassword, confirmPassword) {
            return ensureToken().then(function (token) {
                return window.fetch(apiBase() + '/auth/change-password', {
                    method: 'POST',
                    credentials: 'include',
                    headers: {
                        'Content-Type': 'application/json',
                        'Authorization': 'Bearer ' + token
                    },
                    body: JSON.stringify({
                        CurrentPassword: currentPassword,
                        NewPassword: newPassword,
                        ConfirmPassword: confirmPassword
                    })
                }).then(function (response) {
                    return response.json().then(function (body) {
                        return {
                            // ResponseType 0 == Success in Utilities/ApiResponse.cs
                            success: response.ok && pick(body, 'responseType') === 0,
                            message: pick(body, 'message') || ''
                        };
                    });
                });
            });
        }
    };

    installFetchInterceptor();
    installJQueryInterceptor();

    window.RmAuth = RmAuth;

    // Auto-guard every page except the login screen. Pages that need a specific role
    // can call RmAuth.bootstrap({ roles: ['TeamLead'] }) themselves instead.
    if (!onLoginPage() && !window.RM_AUTH_MANUAL_BOOTSTRAP) {
        window.addEventListener('load', function () { bootstrap(); });
    }

})(window, window.jQuery);
