/*
 * Password policy display.
 *
 * The rules come from GET /api/auth/password-policy, which is served from the same
 * AuthOptions the server-side validator reads. Restating them in the page would let
 * the two drift, and a screen that promises different rules from the ones enforced
 * is worse than a screen that promises nothing.
 *
 * Two rules deliberately cannot be ticked in the browser:
 *   - "not one of your last N passwords" — the client has no way to know
 *   - "not a common password"           — the list lives on the server
 * Those render as advisory rather than being marked satisfied, because claiming a
 * pass the server has not agreed to is how you get a confident submit and a refusal.
 *
 * The rule that catches people out — "must not contain the username or name" — CAN
 * be checked here, provided the caller supplies the account's identifiers.
 */
(function (window) {
    'use strict';

    var cached = null;

    function apiBase() {
        return (typeof API_BASE_URL !== 'undefined') ? API_BASE_URL : (window.location.origin + '/api');
    }

    /** Fetches the policy once per page and caches it. */
    function load() {
        if (cached) return Promise.resolve(cached);

        return window.fetch(apiBase() + '/auth/password-policy')
            .then(function (response) { return response.json(); })
            .then(function (body) {
                cached = (body && body.data) || null;
                return cached;
            })
            .catch(function () { return null; });
    }

    /**
     * Renders the rule list into `listEl`.
     * Returns the policy so a caller can wire live checking.
     */
    function render(listEl) {
        if (!listEl) return Promise.resolve(null);

        return load().then(function (policy) {
            if (!policy || !policy.rules) {
                listEl.innerHTML = '<li>Could not load the password rules.</li>';
                return null;
            }

            listEl.innerHTML = policy.rules.map(function (rule, index) {
                // Rules the browser cannot verify are marked so they are never ticked.
                var checkable = index < checkableCount(policy);
                return '<li data-rule="' + index + '"' +
                       (checkable ? '' : ' class="server"') + '>' +
                       escapeHtml(rule) + '</li>';
            }).join('');

            return policy;
        });
    }

    /**
     * How many of the leading rules can be evaluated client-side. The server builds
     * the list in a fixed order: length, then the character classes, then the
     * identifier rule, then the ones only it can judge.
     */
    function checkableCount(policy) {
        var n = 1;  // length

        if (policy.requireUppercase)       n++;
        if (policy.requireLowercase)       n++;
        if (policy.requireDigit)           n++;
        if (policy.requireNonAlphanumeric) n++;

        return n + 1;   // + the username/name rule
    }

    /**
     * Ticks each satisfied rule as the user types.
     *
     * `identifiers` are the account's username, first name and last name. Pass them
     * when they are known — without them the "must not contain your name" rule cannot
     * be checked here, which is exactly the rule people trip over.
     */
    function attach(inputEl, listEl, identifiers) {
        if (!inputEl || !listEl) return;

        render(listEl).then(function (policy) {
            if (!policy) return;

            function evaluate() {
                var value = inputEl.value || '';
                var tests = buildTests(policy, identifiers);

                tests.forEach(function (passes, index) {
                    var li = listEl.querySelector('[data-rule="' + index + '"]');
                    if (!li) return;

                    // Nothing is ticked while the box is empty — an all-green list
                    // above an empty field reads as "you are done".
                    li.classList.toggle('ok', value.length > 0 && passes(value));
                });
            }

            inputEl.addEventListener('input', evaluate);
            evaluate();
        });
    }

    /** Client-side equivalents of the server's checks, in the server's order. */
    function buildTests(policy, identifiers) {
        var tests = [
            function (v) { return v.length >= policy.minLength && v.length <= policy.maxLength; }
        ];

        if (policy.requireUppercase)       tests.push(function (v) { return /[A-Z]/.test(v); });
        if (policy.requireLowercase)       tests.push(function (v) { return /[a-z]/.test(v); });
        if (policy.requireDigit)           tests.push(function (v) { return /[0-9]/.test(v); });
        if (policy.requireNonAlphanumeric) tests.push(function (v) { return /[^A-Za-z0-9]/.test(v); });

        tests.push(function (v) { return !containsIdentifier(v, identifiers); });

        return tests;
    }

    /**
     * Mirrors PasswordService.Contains: case-insensitive, and only for identifiers of
     * four characters or more — "Ravi" counts, a two-letter name does not.
     */
    function containsIdentifier(password, identifiers) {
        if (!identifiers || !identifiers.length) return false;

        var haystack = password.toLowerCase();

        return identifiers.some(function (identifier) {
            if (!identifier || String(identifier).length < 4) return false;
            return haystack.indexOf(String(identifier).toLowerCase()) !== -1;
        });
    }

    /**
     * The client-checkable rules this candidate fails, as display strings.
     *
     * Only covers what the browser can judge — the server remains the authority, and
     * an empty result means "nothing obviously wrong", not "this will be accepted".
     * Use it to stop an obviously-doomed submit, never to conclude success.
     *
     * Resolves against the cached policy; call after render/attach has loaded it.
     */
    function unmetRules(value, identifiers) {
        if (!cached || !cached.rules) return [];

        var tests = buildTests(cached, identifiers);
        var failed = [];

        tests.forEach(function (passes, index) {
            if (!passes(value || '')) failed.push(cached.rules[index]);
        });

        return failed;
    }

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    window.PasswordPolicy = {
        load: load,
        render: render,
        attach: attach,
        unmetRules: unmetRules,
        containsIdentifier: containsIdentifier
    };

})(window);
