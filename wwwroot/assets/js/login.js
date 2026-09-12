/*
 * Login screen. The mobile number comes first and decides what else appears.
 *
 * THE FLOW
 *   1. The number is typed. Nothing else is on screen.
 *   2. At ten digits the page asks POST /api/auth/login-method what this account
 *      needs.
 *   3. No password needed -> it signs in there and then, with no button to press.
 *      Password needed    -> the password box, the Sign in button and the
 *                            forgotten-password link all appear together.
 *
 * WHY: some of the people who use this system cannot read. A password prompt does
 * not make their account safer, it makes the system unusable to them — and what
 * actually happens is that somebody literate types it for them, so the credential
 * ends up shared anyway. An administrator marks those accounts individually on the
 * Accounts screen.
 *
 * What that costs is not hidden anywhere: for such an account the mobile number IS
 * the credential. The server decides which accounts qualify, never this page — the
 * probe answers "password required" for an unknown number, a disabled account, a
 * locked one, or any error, so the only way to reach the passwordless path is to be
 * an account somebody deliberately granted it to.
 *
 * The access token is held in memory by auth.js and the refresh token lands in an
 * HttpOnly cookie. The volunteer and team-lead ids come from the token's claims, not
 * from a query string, so they cannot be tampered with.
 */
$(document).ready(function () {

    var $form = $('#loginForm');
    var $overlay = $('#loadingOverlay');
    var $error = $('#loginError');
    var $mobile = $('#mobile');
    var $password = $('#password');
    var $passwordField = $('#passwordField');
    var $button = $('#loginBtn');
    var $forgotWrap = $('#forgotWrap');
    var $checking = $('#checkingHint');
    var $sub = $('#loginSub');

    // The number the probe last answered for, so re-typing the same digits does not
    // ask again and editing the number forgets the previous answer.
    var probedFor = null;
    var probing = false;
    var probeTimer = null;

    // If a valid refresh cookie is still present, skip the form entirely.
    RmAuth.refresh().then(function (ok) {
        if (ok) navigateForUser(RmAuth.getUser());
    });

    // Clear a stale failure as soon as the user starts correcting it — leaving the
    // message up while they retype makes it look like the new attempt failed too.
    $form.on('input', 'input', clearError);

    // ------------------------------------------------------------------
    // Step 1 — the number decides what happens next
    // ------------------------------------------------------------------

    $mobile.on('input', function () {
        var mobile = digits($mobile.val());

        // Anything typed after an answer invalidates it: the digits on screen may now
        // belong to a different account entirely.
        if (probedFor !== null && mobile !== probedFor) resetToNumberOnly();

        window.clearTimeout(probeTimer);

        if (mobile.length !== 10 || probing || mobile === probedFor) return;

        // Debounced, because "10 digits" is reached on the last keystroke of a paste
        // and on the way through when somebody is correcting a longer string.
        probeTimer = window.setTimeout(function () { probe(mobile); }, 250);
    });

    /**
     * Asks the server what this number needs, then either signs in or reveals the
     * password box.
     *
     * RmAuth.loginMethod never rejects and answers "password required" on any doubt,
     * so there is no failure path here — the worst case is a password prompt for
     * somebody who did not need one, which they can still complete.
     */
    function probe(mobile) {
        probing = true;
        $checking.text('Checking…');

        RmAuth.loginMethod(mobile).then(function (method) {
            probing = false;
            probedFor = mobile;
            $checking.text('');

            if (method.requiresPassword) {
                revealPasswordField();
                return;
            }

            // No password on this account. Sign in with the number alone.
            submit(mobile, '');
        });
    }

    /**
     * Reveals the password step: the box, the Sign in button and the forgotten-password
     * link, which all belong to it.
     *
     * An account that signs in on its number alone never sees any of them — it is
     * signed in before there is a button to press, and offering "forgot your password"
     * to somebody who has never had one only invites a call to the office.
     */
    function revealPasswordField() {
        if (!$passwordField.prop('hidden')) return;

        $passwordField.prop('hidden', false);
        $button.prop('hidden', false);
        $forgotWrap.prop('hidden', false);
        $sub.text('Enter your password.');
        $password.focus();
    }

    /** Back to a bare number field, as though nothing had been typed. */
    function resetToNumberOnly() {
        probedFor = null;
        $checking.text('');
        $passwordField.prop('hidden', true);
        $button.prop('hidden', true);
        $forgotWrap.prop('hidden', true);
        $password.val('');
        $sub.text('Enter your mobile number.');
    }

    function digits(value) {
        return String(value == null ? '' : value).replace(/\D/g, '');
    }

    $('#revealPassword').on('click', function () {
        var reveal = $password.attr('type') === 'password';

        $password.attr('type', reveal ? 'text' : 'password');
        $(this).attr('aria-pressed', reveal ? 'true' : 'false')
               .text(reveal ? 'Hide' : 'Show');

        $password.focus();
    });

    /**
     * The Sign in button, and Enter in either field.
     *
     * Reached only when the password box is showing: a passwordless account is signed
     * in by `probe` before this form can be submitted at all. Pressing Enter in the
     * number field before the probe answers falls through to the guard below.
     */
    $form.on('submit', function (e) {
        e.preventDefault();

        var mobile = digits($mobile.val());

        clearError();

        if (mobile.length !== 10) {
            showError('Enter a valid 10-digit mobile number.', $mobile);
            return;
        }

        // Enter pressed on the number before the answer came back. Ask now rather
        // than guessing, so an impatient passwordless user is not shown a password
        // box they do not have.
        if ($passwordField.prop('hidden')) {
            window.clearTimeout(probeTimer);
            if (!probing) probe(mobile);
            return;
        }

        var password = $password.val();

        if (!password) {
            showError('Enter your password.', $password);
            return;
        }

        submit(mobile, password);
    });

    /**
     * Signs in and routes onwards. `password` is an empty string for an account that
     * signs in with its number alone; the server decides whether that is acceptable
     * for this account, and answers with the same generic failure as a wrong password
     * when it is not.
     */
    function submit(mobile, password) {
        clearError();
        setBusy(true);

        RmAuth.login(mobile, password)
            .then(function (result) {
                setBusy(false);

                // The credential was accepted but the church requires this sign-in to
                // be confirmed on Telegram. Not a failure — the opposite.
                if (result.pendingTelegram && result.challengeId) {
                    showVerifyStep(result.challengeId);
                    return;
                }

                if (!result.success) {
                    // One generic message for every failure mode — the server does not
                    // reveal whether the account exists, so neither does the UI.
                    //
                    // A passwordless attempt that fails leaves the person on a bare
                    // number field with no way forward, so the password box is opened
                    // as the fallback: either they do have one, or they are on the
                    // wrong number and the box is harmless.
                    showError(result.message || 'Invalid mobile number or password.',
                              $passwordField.prop('hidden') ? $mobile : $password);

                    probedFor = null;
                    revealPasswordField();
                    $password.val('');
                    return;
                }

                if (result.mustChangePassword) {
                    showToast('Please set a new password to continue', 'warning');
                    setTimeout(function () {
                        window.location.href = '../../pages/auth/change-password.html';
                    }, 600);
                    return;
                }

                showToast('Signed in successfully', 'success');
                setTimeout(function () { continueAfterSignIn(result.user); }, 500);
            })
            .catch(function () {
                setBusy(false);
                showError('Sign-in failed. Please check your connection and try again.');
            });
    }

    // ------------------------------------------------------------------
    // Step 3 — confirm on Telegram
    //
    // Only reached when the church has sign-in confirmation switched on AND this
    // account has Telegram linked. The server decides both; this screen is told
    // after the credential has already been accepted.
    //
    // What the browser holds is a challenge id, which identifies the pending sign-in
    // and nothing more. The token that actually approves it lives in the Telegram
    // button and never reaches this page — so the browser can wait for approval but
    // cannot grant it to itself.
    // ------------------------------------------------------------------

    var verify = { challengeId: null, timer: null, deadline: 0, pausedUntil: 0 };

    function showVerifyStep(challengeId) {
        verify.challengeId = challengeId;

        // The credential fields go away. They have already done their job, and
        // leaving them on screen invites somebody to retype a password that was
        // accepted a second ago.
        $('#mobile').closest('.vl-field').prop('hidden', true);
        $passwordField.prop('hidden', true);
        $button.prop('hidden', true);
        $forgotWrap.prop('hidden', true);

        $('#verifyStep').prop('hidden', false);
        $sub.text('One more step.');
        setVerifyStatus('', null);

        $('#verifyBtn').prop('disabled', false).text('Verify on Telegram').focus();
    }

    function sendVerification() {
        if (!verify.challengeId) return;

        var $btn = $('#verifyBtn').prop('disabled', true).text('Sending…');

        RmAuth.sendTelegramVerification(verify.challengeId).then(function (result) {
            if (!result.sent) {
                setVerifyStatus(result.message || 'The confirmation could not be sent.', 'bad');
                $btn.prop('disabled', false).text('Try again');
                return;
            }

            $btn.prop('disabled', false).text('Send it again');
            setVerifyStatus('Waiting for you to confirm on Telegram…', 'waiting');

            startPolling();
        });
    }

    /**
     * Watches for the tap.
     *
     * Every two seconds, and stopped on the first answer that is not WAITING. The
     * poll has its own rate limit on the server precisely because a screen waiting
     * three minutes would otherwise exhaust the sign-in bucket in fifteen seconds.
     */
    function startPolling() {
        stopPolling();

        // Matches the server's challenge lifetime. Kept slightly longer so the
        // server's own answer is what ends the wait, not this timer racing it.
        verify.deadline = Date.now() + (3 * 60 + 15) * 1000;
        verify.pausedUntil = 0;

        verify.timer = window.setInterval(function () {
            if (Date.now() > verify.deadline) {
                stopPolling();
                setVerifyStatus('That confirmation expired. Start again.', 'bad');
                return;
            }

            // Standing back after a 429. Continuing to ask on an empty bucket only
            // keeps it empty, and every one of those answers looks like "not yet".
            if (Date.now() < verify.pausedUntil) return;

            RmAuth.pollTelegramVerification(verify.challengeId).then(function (result) {
                if (result.throttled) {
                    verify.pausedUntil = Date.now() +
                        Math.max(5, result.retryAfterSeconds || 10) * 1000;
                    return;
                }

                if (result.outcome === 'WAITING') return;

                stopPolling();

                if (result.outcome !== 'APPROVED') {
                    setVerifyStatus(
                        'That sign-in was not confirmed. If you refused it, tell the church office.', 'bad');
                    return;
                }

                setVerifyStatus('Confirmed. Signing you in…', 'waiting');

                if (result.mustChangePassword) {
                    showToast('Please set a new password to continue', 'warning');
                    setTimeout(function () {
                        window.location.href = '../../pages/auth/change-password.html';
                    }, 600);
                    return;
                }

                showToast('Signed in successfully', 'success');
                setTimeout(function () { continueAfterSignIn(result.user); }, 400);
            });
        }, 2000);
    }

    function stopPolling() {
        if (verify.timer) { window.clearInterval(verify.timer); verify.timer = null; }
    }

    function setVerifyStatus(text, tone) {
        $('#verifyStatus')
            .text(text)
            .removeClass('is-waiting is-bad')
            .addClass(tone ? 'is-' + tone : '');
    }

    /**
     * Sends the user on after a successful sign-in.
     *
     * Telegram linking sits in the same position as the password screen: a one-time
     * step between signing in and reaching the application. Whether it can be skipped
     * is an administrator setting, so this only decides whether to SHOW it — the page
     * itself honours the setting.
     *
     * Any failure here falls through to the normal landing page. A status check that
     * cannot answer must never be what stops somebody working.
     */
    function continueAfterSignIn(account) {
        var target = landingPageFor(account);

        if (!target) return;    // no role; landingPageFor has already explained

        $.ajax({ url: API_BASE_URL + '/telegram/status', method: 'GET', timeout: 4000 })
            .done(function (res) {
                var status = (res && res.data) || {};

                // Matches auth.js's own requireTelegramIfNeeded exactly: not required,
                // not configured, or already linked all mean "do not show this screen".
                // This used to skip only the last two, so with the organisation-wide
                // setting turned OFF every fresh sign-in was still routed through
                // link-telegram.html once — the one thing the setting exists to prevent.
                if (!status.isRequired || !status.isConfigured || status.isLinked) {
                    window.location.href = target;
                    return;
                }

                // Remembered so the linking page can hand control straight to the page
                // this account would otherwise have landed on.
                sessionStorage.setItem('rm_post_login_target', target);
                window.location.href = '/pages/auth/link-telegram.html';
            })
            .fail(function () { window.location.href = target; });
    }

    /**
     * Shows a failure in the inline region AND as a toast. The inline region is the
     * accessible one — it carries role="alert", stays on screen, and sits next to the
     * field it concerns; the toast just catches the eye.
     */
    function showError(message, $focusField) {
        $error.text(message).addClass('is-visible');
        showToast(message, 'error');

        if ($focusField && $focusField.length) {
            $focusField.attr('aria-invalid', 'true').focus();
        }
    }

    function clearError() {
        $error.removeClass('is-visible').text('');
        $mobile.removeAttr('aria-invalid');
        $password.removeAttr('aria-invalid');
    }

    /** Routes to the landing page for the account's highest-privilege role. */
    function navigateForUser(account) {
        var target = landingPageFor(account);
        if (target) window.location.href = target;
    }

    /**
     * The landing page for the account's highest-privilege role, or null when there
     * is none. Separated from the navigation itself so the sign-in flow can hold the
     * destination while an intermediate step (password, Telegram) runs first.
     *
     * Ids come from the authenticated profile, never from user input.
     */
    function landingPageFor(account) {
        if (!account) { showToast('Unable to load your profile', 'error'); return null; }

        // Role codes are ADMIN / PASTOR / TEAM_LEAD / VOLUNTEER / DATA_ENTRY /
        // WEB_COORDINATOR, and arrive as grants ({ roleCode, campusId }) because a
        // role can be scoped to a campus. RmAuth.roleCodes() flattens that.
        var roles = RmAuth.roleCodes();
        var volunteerId = RmAuth.pick(account, 'volunteerId');
        var teamId = RmAuth.pick(account, 'teamId');
        var target;

        // Highest privilege wins.
        if (roles.indexOf('ADMIN') !== -1) {
            target = '/pages/admin/accounts.html';
        } else if (roles.indexOf('PASTOR') !== -1) {
            target = '/pages/dashboard/pastor.html';
        } else if (roles.indexOf('TEAM_LEAD') !== -1) {
            target = '/pages/dashboard/team-lead.html'
                   + (teamId ? '?teamid=' + encodeURIComponent(teamId) : '');
        } else if (roles.indexOf('VOLUNTEER') !== -1) {
            target = '/pages/care/my-assignments.html'
                   + (volunteerId ? '?volunteerid=' + encodeURIComponent(volunteerId) : '');
        } else if (roles.indexOf('DATA_ENTRY') !== -1) {
            target = '/pages/intake/record-visitor.html';
        } else if (roles.indexOf('WEB_COORDINATOR') !== -1) {
            // Their whole job is this one queue. Anything else they open is a 403.
            target = '/pages/admin/web-enquiries.html';
        } else {
            showToast('Your account has no assigned role. Contact an administrator.', 'error');
            return null;
        }

        return target;
    }

    // The button is disabled as well as hidden-or-not: a passwordless sign-in runs with
    // no button on screen at all, so the fields and the overlay are what actually stop
    // a second attempt while the first is in flight.
    function setBusy(busy) {
        if (busy) {
            $overlay.css('display', 'flex').attr('aria-hidden', 'false');
            $button.prop('disabled', true).text('Signing in…');
            $mobile.prop('disabled', true);
            $password.prop('disabled', true);
        } else {
            $overlay.hide().attr('aria-hidden', 'true');
            $button.prop('disabled', false).text('Sign in');
            $mobile.prop('disabled', false);
            $password.prop('disabled', false);
        }
    }

    $('#verifyBtn').on('click', sendVerification);

    // Starting again is a full reload. There is a pending challenge on the server and
    // a half-filled form on screen, and reloading is the one way to be sure neither is
    // left in a state the next attempt trips over.
    $('#verifyCancel').on('click', function () {
        stopPolling();
        window.location.reload();
    });

    $('#forgotLink').on('click', function () {
        // Self-service reset does not exist: an administrator issues passwords through
        // POST /api/admin/accounts/{id}/password. Saying so plainly beats a dead link.
        showToast('Contact an administrator to reset your password.', 'info');
    });
});
