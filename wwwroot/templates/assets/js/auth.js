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

    var LOGIN_PAGE = '/templates/Volunteers/Login.html';
    var CHANGE_PASSWORD_PAGE = '/templates/Volunteers/ChangePassword.html';

    // Endpoints that must never trigger the refresh-and-retry loop.
    var AUTH_ENDPOINTS = ['/api/auth/login', '/api/auth/refresh', '/api/auth/logout'];

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
        CURRENT_USER = pick(payload, 'user') || null;

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
            return response.json().then(function (body) {
                var payload = unwrap(body);

                if (!response.ok || !payload) {
                    return {
                        success: false,
                        // The server deliberately returns one generic message for every
                        // failure mode; surface it verbatim rather than guessing.
                        message: pick(body, 'message') || 'Sign-in failed.'
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

            return true;
        });
    }

    function hasAnyRole(roles) {
        var mine = pick(CURRENT_USER, 'roles');
        if (!mine) return false;

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
        logout: logout,
        refresh: refresh,
        bootstrap: bootstrap,
        ensureToken: ensureToken,
        redirectToLogin: redirectToLogin,
        hasAnyRole: hasAnyRole,
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
