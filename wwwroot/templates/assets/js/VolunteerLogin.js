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

    // If a valid refresh cookie is still present, skip the form entirely.
    RmAuth.refresh().then(function (ok) {
        if (ok) navigateForUser(RmAuth.getUser());
    });

    $form.on('submit', function (e) {
        e.preventDefault();

        var mobile = $('#mobile').val().trim();
        var password = $('#password').val();

        if (!/^\d{10}$/.test(mobile)) {
            showToast('Enter a valid 10-digit mobile number', 'warning');
            return;
        }

        if (!password) {
            showToast('Enter your password', 'warning');
            return;
        }

        setBusy(true);

        RmAuth.login(mobile, password)
            .then(function (result) {
                setBusy(false);

                if (!result.success) {
                    // One generic message for every failure mode — the server does not
                    // reveal whether the account exists, so neither does the UI.
                    showToast(result.message || 'Invalid mobile number or password', 'error');
                    $('#password').val('').focus();
                    return;
                }

                if (result.mustChangePassword) {
                    showToast('Please set a new password to continue', 'warning');
                    setTimeout(function () {
                        window.location.href = '../../templates/Volunteers/ChangePassword.html';
                    }, 600);
                    return;
                }

                showToast('Signed in successfully', 'success');
                setTimeout(function () { navigateForUser(result.user); }, 500);
            })
            .catch(function () {
                setBusy(false);
                showToast('Sign-in failed. Please try again.', 'error');
            });
    });

    /**
     * Routes to the landing page for the account's highest-privilege role.
     * Ids come from the authenticated profile, never from user input.
     */
    function navigateForUser(account) {
        if (!account) { showToast('Unable to load your profile', 'error'); return; }

        // Role codes are ADMIN / PASTOR / TEAM_LEAD / VOLUNTEER / DATA_ENTRY, and
        // arrive as grants ({ roleCode, campusId }) because a role can be scoped
        // to a campus. RmAuth.roleCodes() flattens that.
        var roles = RmAuth.roleCodes();
        var volunteerId = RmAuth.pick(account, 'volunteerId');
        var teamId = RmAuth.pick(account, 'teamId');
        var target;

        // Highest privilege wins.
        if (roles.indexOf('ADMIN') !== -1) {
            target = '../../templates/Admin/accounts.html';
        } else if (roles.indexOf('PASTOR') !== -1) {
            target = '../../templates/Pastor/Dashboard.html';
        } else if (roles.indexOf('TEAM_LEAD') !== -1) {
            target = '../../templates/TeamLeads/TeamLeadDashboard.html'
                   + (teamId ? '?teamid=' + encodeURIComponent(teamId) : '');
        } else if (roles.indexOf('VOLUNTEER') !== -1) {
            target = '../../templates/Volunteers/Assignments.html'
                   + (volunteerId ? '?volunteerid=' + encodeURIComponent(volunteerId) : '');
        } else if (roles.indexOf('DATA_ENTRY') !== -1) {
            target = '../../templates/Peoples/PeopleEntry.html';
        } else {
            showToast('Your account has no assigned role. Contact an administrator.', 'error');
            return;
        }

        window.location.href = target;
    }

    function setBusy(busy) {
        if (busy) {
            $overlay.css('display', 'flex');
            $button.prop('disabled', true);
        } else {
            $overlay.hide();
            $button.prop('disabled', false);
        }
    }

    $('#forgotLink').on('click', function () {
        showToast('Contact an administrator to reset your password.', 'info');
    });
});
