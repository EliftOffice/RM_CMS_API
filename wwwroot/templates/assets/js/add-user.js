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

    var state = { person: null, bands: [], teams: [], campuses: [] };
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
        attachMobile();
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

    /**
     * Capacity as radio cards, matching the screen this replaced.
     *
     * A dropdown hid the one thing the choice turns on — the weekly numbers — behind
     * a closed control, so the administrator picked a label. Here the range and the
     * band's own note are both visible while the decision is made.
     *
     * The first band is pre-selected: the API refuses a volunteer with no band, and
     * an empty "Choose…" that only ever produces an error is a worse default than
     * the lightest real one.
     */
    function renderBands() {
        var host = $('#bandOptions');

        if (!state.bands.length) {
            host.html('<p class="hint">No capacity bands are configured.</p>');
            return;
        }

        host.html(state.bands.map(function (b, i) {
            var note = b.description || '';

            return '<label class="band-option">' +
                     '<input type="radio" name="band" value="' + esc(b.code) + '"' +
                       (i === 0 ? ' checked' : '') + '>' +
                     '<span class="band-text">' +
                       '<strong>' + esc(b.label) + ' — ' + b.minPerWeek +
                         ' to ' + b.maxPerWeek + ' a week</strong>' +
                       (note ? '<span class="sub">' + esc(note) + '</span>' : '') +
                     '</span>' +
                   '</label>';
        }).join(''));
    }

    function loadReference() {
        $.ajax({ url: API_BASE_URL + '/volunteers/reference', method: 'GET' })
            .done(function (res) {
                state.bands = (res.data && res.data.capacityBands) || [];
                renderBands();
            });

        $.ajax({ url: API_BASE_URL + '/teams', method: 'GET' })
            .done(function (res) {
                state.teams = res.data || [];
                $('#teamId').html('<option value="">No team yet</option>' + state.teams.map(function (t) {
                    return '<option value="' + esc(t.id) + '">' + esc(t.name) + '</option>';
                }).join(''));
            });

        // Campus decides which visitors this person can ever be given, so it is
        // offered whenever there is a real choice. One campus means one option, and
        // a dropdown that can only be answered one way is noise: the server applies
        // the same default when the field is absent.
        $.ajax({ url: API_BASE_URL + '/campuses/options', method: 'GET' })
            .done(function (res) {
                state.campuses = (res && res.data) || [];

                $('#campusId').html(state.campuses.map(function (c) {
                    return '<option value="' + esc(c.id) + '">' + esc(c.name) + '</option>';
                }).join(''));

                $('#campusField').prop('hidden', state.campuses.length < 2);
            })
            .fail(function () { $('#campusField').prop('hidden', true); });
    }

    function attachMobile() {
        // The mobile number becomes the username, so the same rule has to hold here
        // as on intake — otherwise one screen creates a login the other cannot.
        MobileInput.attach(document.getElementById('mobile'), {
            hint: document.getElementById('mobileHint'),
            hintText: '10 digits, starting 6-9. This is also their username for signing in.'
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

        // Start date lives on the volunteer record, so it only means anything for a
        // role that creates one.
        $('#startedField').prop('hidden', role !== 'VOLUNTEER');

        // The team dropdown does two different jobs: for a team lead it is the team
        // they will LEAD, for a volunteer the team they JOIN. Saying which stops a
        // wrong pick that is invisible until somebody wonders why a lead has no team.
        $('#teamHint').text(role === 'TEAM_LEAD'
            ? 'The team they will lead. They will read every escalation raised on its people.'
            : 'A volunteer can be added to a team later.');

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

            if (!MobileInput.isValid(mobile)) {
                return fail(MobileInput.problem(mobile) ||
                            'Enter a 10-digit Indian mobile number starting 6, 7, 8 or 9.');
            }

            body.givenName = given;
            body.familyName = $('#familyName').val().trim() || undefined;
            body.mobile = mobile;
            body.email = $('#email').val().trim() || undefined;
        }

        // Sent only when the picker was shown. Omitted, the server files them at the
        // caller's own campus, which is the right answer while there is only one.
        if (!$('#campusField').prop('hidden')) {
            var campus = $('#campusId').val();
            if (campus) body.campusId = campus;
        }

        if (role === 'VOLUNTEER') {
            var band = $('input[name="band"]:checked').val();
            if (!band) return fail('Choose how much they can take on each week.');
            body.capacityBandCode = band;

            // Omitted means today, which the volunteer service applies.
            var started = $('#startedOn').val();
            if (started) body.startedOn = started;
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
