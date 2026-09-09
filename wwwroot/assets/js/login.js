/*
 * Login screen.
 *
 * Replaces the previous flow, which looked a mobile number up via
 * /api/volunteers/GetVolunteersByMobileAsyncV1/{mobile} and redirected on success —
 * anyone who knew a mobile number was "logged in", and the API itself was wide open.
 *
 * Now: username + password -> POST /api/auth/login. The access token is held in memory
 * by auth.js and the refresh token lands in an HttpOnly cookie. The volunteer/team-lead
 * id comes from the token's claims, not from a query string, so it cannot be tampered with.
 */
$(document).ready(function () {

    var $form = $('#loginForm');
    var $overlay = $('#loadingOverlay');
    var $button = $('#loginBtn');
    var $error = $('#loginError');
    var $mobile = $('#mobile');
    var $password = $('#password');

    // If a valid refresh cookie is still present, skip the form entirely.
    RmAuth.refresh().then(function (ok) {
        if (ok) navigateForUser(RmAuth.getUser());
    });

    // Clear a stale failure as soon as the user starts correcting it — leaving the
    // message up while they retype makes it look like the new attempt failed too.
    $form.on('input', 'input', clearError);

    $('#revealPassword').on('click', function () {
        var reveal = $password.attr('type') === 'password';

        $password.attr('type', reveal ? 'text' : 'password');
        $(this).attr('aria-pressed', reveal ? 'true' : 'false')
               .text(reveal ? 'Hide' : 'Show');

        $password.focus();
    });

    $form.on('submit', function (e) {
        e.preventDefault();

        var mobile = $mobile.val().trim();
        var password = $password.val();

        clearError();

        if (!/^\d{10}$/.test(mobile)) {
            showError('Enter a valid 10-digit mobile number.', $mobile);
            return;
        }

        if (!password) {
            showError('Enter your password.', $password);
            return;
        }

        setBusy(true);

        RmAuth.login(mobile, password)
            .then(function (result) {
                setBusy(false);

                if (!result.success) {
                    // One generic message for every failure mode — the server does not
                    // reveal whether the account exists, so neither does the UI.
                    showError(result.message || 'Invalid mobile number or password.', $password);
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
    });

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

    $('#forgotLink').on('click', function () {
        // Self-service reset does not exist: an administrator issues passwords through
        // POST /api/admin/accounts/{id}/password. Saying so plainly beats a dead link.
        showToast('Contact an administrator to reset your password.', 'info');
    });
});
