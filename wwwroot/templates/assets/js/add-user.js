/*
 * Add a user — direct creation, without the person having to arrive as a visitor.
 *
 *   GET  /api/people/picker?q=      attach to somebody already on file
 *   GET  /api/volunteers/reference  capacity bands
 *   GET  /api/teams                 team placement
 *   POST /api/admin/users           create
 *
 * The "existing person" path matters as much as the new-person one: attaching access
 * to a person already on file is what keeps their visitor history, cases and
 * follow-ups joined to the same identity instead of starting a second record.
 */
$(function () {
    'use strict';

    var ROLES = [
        { code: 'DATA_ENTRY', label: 'Data Entry Operator',
          hint: 'Records visitors at intake. No access to cases or volunteers.' },
        { code: 'VOLUNTEER',  label: 'Volunteer',
          hint: 'Follows up with people. Needs a weekly capacity.' },
        { code: 'TEAM_LEAD',  label: 'Team Lead',
          hint: 'Runs a team: escalations, check-ins, nurture review.' },
        { code: 'PASTOR',     label: 'Pastor',
          hint: 'Cross-team oversight and reporting.' },
        { code: 'ADMIN',      label: 'Administrator',
          hint: 'Full administration, including user management. Grant sparingly.' }
    ];

    var state = { person: null, bands: [], teams: [] };
    var esc = AdminShell.escapeHtml;
    var searchTimer = null;

    AdminShell.boot({
        roles: ['ADMIN'],
        active: { href: '/templates/Admin/users.html', area: 'Admin' }
    }).then(function (ok) {
        if (!ok) return;
        $('#pageBody').prop('hidden', false);
        init();
    });

    function init() {
        renderRoles();
        loadReference();
        attachPolicy();

        $('input[name="mode"]').on('change', syncMode);
        $('#roleOptions').on('change', 'input[name="role"]', syncRole);
        $('#grantAccess').on('change', syncAccess);

        $('#personSearch').on('input', onSearch);
        $('#clearPerson').on('click', clearPerson);
        $('#clearBtn').on('click', resetForm);
        $('#userForm').on('submit', onSubmit);
        $('[data-close]').on('click', function () { $('.modal-backdrop').removeClass('open'); });

        syncMode();
        syncRole();
    }

    function renderRoles() {
        $('#roleOptions').html(ROLES.map(function (r, i) {
            return '<label class="role-pick">' +
                     '<input type="radio" name="role" value="' + esc(r.code) + '"' +
                       (i === 1 ? ' checked' : '') + '>' +
                     '<span><strong>' + esc(r.label) + '</strong>' +
                       '<span class="sub">' + esc(r.hint) + '</span></span>' +
                   '</label>';
        }).join(''));
    }

    function loadReference() {
        $.ajax({ url: API_BASE_URL + '/volunteers/reference', method: 'GET' })
            .done(function (res) {
                state.bands = (res.data && res.data.capacityBands) || [];
                $('#capacityBand').html('<option value="">Choose…</option>' + state.bands.map(function (b) {
                    return '<option value="' + esc(b.code) + '">' + esc(b.label) +
                           ' — ' + b.minPerWeek + ' to ' + b.maxPerWeek + ' a week</option>';
                }).join(''));
            });

        $.ajax({ url: API_BASE_URL + '/teams', method: 'GET' })
            .done(function (res) {
                state.teams = res.data || [];
                $('#teamId').html('<option value="">No team yet</option>' + state.teams.map(function (t) {
                    return '<option value="' + esc(t.id) + '">' + esc(t.name) + '</option>';
                }).join(''));
            });
    }

    function attachPolicy() {
        // Identifiers are re-read at submit time; here the list is mainly informational
        // because the name is not known until it is typed.
        PasswordPolicy.attach(
            document.getElementById('initialPassword'),
            document.getElementById('passwordRulesList'),
            []);
    }

    // ------------------------------------------------------------------ mode

    function syncMode() {
        var existing = $('input[name="mode"]:checked').val() === 'existing';
        $('#existingBlock').prop('hidden', !existing);
        $('#newBlock').prop('hidden', existing);
    }

    function syncRole() {
        var role = $('input[name="role"]:checked').val();

        $('#bandField').prop('hidden', role !== 'VOLUNTEER');
        $('#teamField').prop('hidden', role !== 'VOLUNTEER' && role !== 'TEAM_LEAD');

        // Only a volunteer may work without a sign-in; the rest are exercised entirely
        // through the UI, so the choice is removed rather than silently ignored.
        var optional = role === 'VOLUNTEER';

        $('#accessField').prop('hidden', !optional);

        if (!optional) $('#grantAccess').prop('checked', true);

        syncAccess();
    }

    function syncAccess() {
        $('#passwordBlock').prop('hidden', !$('#grantAccess').is(':checked'));
    }

    // ------------------------------------------------------------------ picker

    function onSearch() {
        var q = $('#personSearch').val().trim();
        clearTimeout(searchTimer);

        if (q.length < 2) { $('#pickerResults').prop('hidden', true); return; }

        searchTimer = setTimeout(function () {
            $.ajax({ url: API_BASE_URL + '/people/picker', method: 'GET', data: { q: q } })
                .done(function (res) {
                    var matches = res.data || [];

                    if (!matches.length) {
                        $('#pickerResults')
                            .html('<div class="picker-item"><span class="sub">Nobody matches.</span></div>')
                            .prop('hidden', false);
                        return;
                    }

                    $('#pickerResults').html(matches.map(function (m, i) {
                        return '<div class="picker-item" data-i="' + i + '">' + esc(m.fullName) +
                               '<div class="sub">' + esc(m.maskedContact || '') +
                               ' · ' + esc(m.lifecycleStatus || '') + '</div></div>';
                    }).join('')).prop('hidden', false);

                    $('#pickerResults .picker-item').on('click', function () {
                        var i = $(this).data('i');
                        if (i == null) return;
                        choose(matches[i]);
                    });
                });
        }, 250);
    }

    function choose(person) {
        state.person = person;
        $('#chosenName').text(person.fullName);
        $('#chosenSub').text([person.maskedContact, person.lifecycleStatus].filter(Boolean).join(' · '));
        $('#chosenField').prop('hidden', false);
        $('#pickerResults').prop('hidden', true);
        $('#personSearch').closest('.field').prop('hidden', true);
    }

    function clearPerson() {
        state.person = null;
        $('#chosenField').prop('hidden', true);
        $('#personSearch').val('').closest('.field').prop('hidden', false);
    }

    // ------------------------------------------------------------------ submit

    function onSubmit(e) {
        e.preventDefault();
        $('#formError').prop('hidden', true);

        var existing = $('input[name="mode"]:checked').val() === 'existing';
        var role = $('input[name="role"]:checked').val();

        var body = {
            roleCode: role,
            grantSystemAccess: $('#grantAccess').is(':checked'),
            mustChangePassword: $('#mustChange').is(':checked')
        };

        if (existing) {
            if (!state.person) return fail('Search for and select the person first.');
            body.personId = state.person.id;
        } else {
            var given = $('#givenName').val().trim();
            var mobile = $('#mobile').val().trim();

            if (!given)  return fail('Enter a first name.');
            if (!mobile) return fail('Enter a mobile number — it is also the username.');

            body.givenName = given;
            body.familyName = $('#familyName').val().trim() || undefined;
            body.mobile = mobile;
            body.email = $('#email').val().trim() || undefined;
        }

        if (role === 'VOLUNTEER') {
            var band = $('#capacityBand').val();
            if (!band) return fail('Choose their weekly capacity.');
            body.capacityBandCode = band;
        }

        var team = $('#teamId').val();
        if (team) {
            if (role === 'TEAM_LEAD') body.leadsTeamId = team;
            else body.teamId = team;
        }

        var password = $('#initialPassword').val();
        if (password && body.grantSystemAccess) body.initialPassword = password;

        busy(true);

        $.ajax({
            url: API_BASE_URL + '/admin/users',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(body)
        })
            .done(function (res) {
                busy(false);

                if (!res || res.responseType !== 0) {
                    return fail((res && res.message) || 'Could not create this user.');
                }

                showToast(res.message, 'success');

                if (res.data && res.data.generatedPassword) {
                    $('#credentialFor').text('For ' + (res.data.user.username || 'this account'));
                    $('#credentialValue').text(res.data.generatedPassword);
                    $('#credentialModal').addClass('open');
                }

                resetForm();
            })
            .fail(function (xhr) {
                busy(false);
                var body = xhr.responseJSON;
                fail((body && (body.message || body.title)) || 'Could not create this user.');
            });
    }

    function fail(message) {
        $('#formError').text(message).prop('hidden', false);
        showToast(message, 'warning');
    }

    function busy(on) {
        $('#saveBtn').prop('disabled', on);
        $('#clearBtn').prop('disabled', on);
        $('#statusHint').text(on ? 'Creating…' : '');
    }

    function resetForm() {
        $('#userForm')[0].reset();
        clearPerson();
        $('#formError').prop('hidden', true);
        syncMode();
        syncRole();
    }
});
