/*
 * Team management.
 *
 *   GET  /api/teams?includeInactive=      the list
 *   GET  /api/teams/access                what THIS caller may do
 *   GET  /api/teams/lead-candidates       who could lead a team
 *   POST /api/teams                       create        (admin only)
 *   PUT  /api/teams/{id}                  edit
 *
 * The screen renders from /access, never from the role claim. Three roles can
 * reach this page and what each may touch is decided by settings an administrator
 * controls, so asking the server is the only way to be right — and hiding a
 * control is a courtesy anyway: the API applies the same rules again on save.
 *
 * Scopes, as the server reports them:
 *
 *   ADMINISTRATOR   every team, every field, plus create
 *   CAMPUSWIDE      pastor, granted — every team at their campus, every field
 *   OWNTEAMONLY     team lead, granted — their own team, name and size only
 *   NONE            the page is replaced by an explanation
 */
$(function () {
    'use strict';

    var esc = AdminShell.escapeHtml;

    var state = {
        access: null,
        teams: [],
        leads: [],
        campuses: [],
        search: '',
        includeInactive: false,
        editing: null      // null = creating
    };

    // Every role that could possibly hold a grant. The server decides whether they
    // actually do; this only stops the page booting for a volunteer.
    AdminShell.boot({
        roles: ['ADMIN', 'PASTOR', 'TEAM_LEAD'],
        active: { href: '/pages/admin/teams.html', area: 'Admin' }
    }).then(function (ok) {
        if (!ok) return;
        loadAccess();
    });

    // ── Access ─────────────────────────────────────────────────────────────
    function loadAccess() {
        $.ajax({ url: API_BASE_URL + '/teams/access', method: 'GET' })
            .done(function (res) {
                state.access = (res && res.data) || {};

                if (!state.access.canOpen) { showLocked(); return; }

                $('#pageBody').prop('hidden', false);
                renderScopeNote();
                bind();

                if (state.access.canCreate) {
                    $('#newTeamBtn').prop('hidden', false);
                    loadCampuses();
                }
                if (state.access.canReassignLead) loadLeads();

                loadTeams();
            })
            .fail(function (xhr) {
                // 403 here is the policy refusing the role outright, which is a
                // different thing from a role that simply has not been granted.
                if (xhr && xhr.status === 403) { showLocked(); return; }
                showToast('Could not check your access.', 'error');
            });
    }

    function showLocked() {
        var roles = RmAuth.roleCodes();

        var why = roles.indexOf('PASTOR') !== -1
            ? 'Pastors can be given access to manage teams, but it is switched off. ' +
              'The setting is team.manage_by_pastor.'
            : roles.indexOf('TEAM_LEAD') !== -1
                ? 'Team leads can be given access to rename and resize the team they lead, ' +
                  'but it is switched off. The setting is team.manage_by_team_lead.'
                : 'Your role cannot manage teams.';

        $('#lockedWhy').html(esc(why).replace(
            /(team\.manage_by_[a-z_]+)/,
            '<span class="grant-key">$1</span>'));

        $('#lockedBody').prop('hidden', false);
    }

    /**
     * Says plainly what this person can do, because the difference between
     * "every team" and "the one you lead" is not visible from the table alone.
     */
    function renderScopeNote() {
        var a = state.access;
        var text = null;
        var tone = 'notice';

        if (a.scope === 'CAMPUSWIDE') {
            text = 'You can edit teams at your campus. Creating and retiring teams stays with an administrator.';
        } else if (a.scope === 'OWNTEAMONLY') {
            text = 'You can rename and resize the team you lead. Everything else here is read-only.';
        }

        if (!text) return;

        $('#scopeNote').html(
            '<div class="' + tone + ' scope-note"><div>' + esc(text) + '</div></div>');
    }

    // ── Data ───────────────────────────────────────────────────────────────
    function loadTeams() {
        $.ajax({
            url: API_BASE_URL + '/teams',
            method: 'GET',
            data: { includeInactive: state.includeInactive }
        })
            .done(function (res) {
                state.teams = (res && res.data) || [];
                render();
            })
            .fail(function () {
                $('#teamsTable tbody').html(
                    '<tr><td colspan="6" class="empty">Teams could not be loaded.</td></tr>');
            });
    }

    function loadLeads() {
        $.ajax({ url: API_BASE_URL + '/teams/lead-candidates', method: 'GET' })
            .done(function (res) { state.leads = (res && res.data) || []; })
            .fail(function () { state.leads = []; });
    }

    // ── Table ──────────────────────────────────────────────────────────────
    function visibleTeams() {
        var q = state.search.trim().toLowerCase();
        if (!q) return state.teams;

        return state.teams.filter(function (t) {
            return (t.name || '').toLowerCase().indexOf(q) !== -1 ||
                   (t.leadName || '').toLowerCase().indexOf(q) !== -1;
        });
    }

    /** True when this caller may edit this particular row. */
    function canEdit(team) {
        var a = state.access;
        if (a.canEditAnyTeam) return true;

        // OWNTEAMONLY: the row must be the team they lead. The server checks the
        // same thing against the token, so a tampered id changes nothing.
        return a.canEditOwnTeam && team.leadAccountId === myAccountId();
    }

    function myAccountId() {
        var u = RmAuth.getUser() || {};
        return RmAuth.pick(u, 'id') || RmAuth.pick(u, 'accountId') || '';
    }

    function capacityCell(t) {
        var max = t.maxMembers || 0;
        var n = t.memberCount || 0;
        var pct = max > 0 ? Math.min(100, Math.round((n / max) * 100)) : 0;

        // Over capacity is real — a band can be lowered, or members moved in by
        // hand — so it gets its own colour rather than a bar pinned at full.
        var cls = n > max ? 'cap-fill-over' : (n >= max ? 'cap-fill-full' : '');

        return '<div class="cap">' +
                   '<div class="cap-track">' +
                       '<div class="cap-fill ' + cls + '" style="width:' + pct + '%"></div>' +
                   '</div>' +
                   '<span class="cap-text">' + n + ' / ' + max + '</span>' +
               '</div>';
    }

    function render() {
        var rows = visibleTeams();

        if (!rows.length) {
            $('#teamsTable tbody').html(
                '<tr><td colspan="6" class="empty">' +
                (state.search ? 'No team matches that.' : 'No teams yet.') +
                '</td></tr>');
            return;
        }

        $('#teamsTable tbody').html(rows.map(function (t) {
            var lead = t.leadName
                ? esc(t.leadName)
                : '<span class="lead-none">No lead assigned</span>';

            var status = t.isActive
                ? '<span class="badge badge-ok">Active</span>'
                : '<span class="badge badge-off">Retired</span>';

            var full = t.isActive && (t.memberCount >= t.maxMembers)
                ? ' <span class="badge badge-warn">Full</span>'
                : '';

            var action = canEdit(t)
                ? '<button type="button" class="btn btn-sm" data-edit="' + esc(t.id) + '">Edit</button>'
                : '';

            return '<tr class="' + (t.isActive ? '' : 'row-muted') + '">' +
                       '<td class="cell-name">' + esc(t.name) + '</td>' +
                       '<td>' + esc(t.campusName || '—') + '</td>' +
                       '<td>' + lead + '</td>' +
                       '<td>' + capacityCell(t) + '</td>' +
                       '<td>' + status + full + '</td>' +
                       '<td class="cell-actions">' + action + '</td>' +
                   '</tr>';
        }).join(''));
    }

    // ── Modal ──────────────────────────────────────────────────────────────
    function openEditor(team) {
        var a = state.access;
        state.editing = team || null;

        var creating = !team;

        $('#editTitle').text(creating ? 'New team' : 'Edit ' + team.name);
        $('#editNotice').prop('hidden', true).text('');

        $('#fName').val(creating ? '' : team.name);
        $('#fMax').val(creating ? 12 : team.maxMembers);
        $('#fActive').prop('checked', creating ? true : team.isActive);

        // A team lead gets the name and the size. The other two fields are shown
        // disabled rather than hidden, so it is clear they exist and who owns them.
        var lockLead = !a.canReassignLead;
        var lockActive = !a.canDeactivate;

        buildLeadOptions(creating ? null : team.leadAccountId);

        $('#fLead').prop('disabled', lockLead);
        $('#leadField').toggleClass('field-locked', lockLead);
        $('#leadHint').text(lockLead
            ? 'Only a pastor or an administrator can change who leads a team.'
            : "The lead reads every escalation raised on this team's people.");

        $('#fActive').prop('disabled', lockActive);
        $('#activeField').toggleClass('field-locked', lockActive);
        $('#activeField').prop('hidden', creating);

        renderCampusField(creating, team);

        $('#editModal').addClass('open');
        $('#fName').trigger('focus');
    }

    /**
     * Campus is a create-time decision.
     *
     * An administrator is organisation-wide and can put a team at any site, which
     * is the whole point of having more than one. It used to be taken silently
     * from whoever was signed in — invisible while there was one campus, and wrong
     * the moment there were two.
     *
     * On edit it becomes a read-only line rather than disappearing: which campus a
     * team belongs to is worth seeing, and it cannot be changed because the team's
     * volunteers and cases belong to that campus too.
     */
    function renderCampusField(creating, team) {
        if (!creating) {
            $('#campusField').prop('hidden', false);
            $('#fCampus').prop('hidden', true);
            $('#campusHint').text('At ' + (team.campusName || 'an unknown campus') +
                                  '. A team cannot move — its volunteers and cases belong there too.');
            return;
        }

        // One option is not a choice, and the server applies the same default.
        var choices = state.campuses || [];

        $('#campusField').prop('hidden', choices.length < 2);
        $('#fCampus').prop('hidden', false).html(choices.map(function (c) {
            return '<option value="' + esc(c.id) + '">' + esc(c.name) + '</option>';
        }).join(''));

        $('#campusHint').text('Which site this team serves. It cannot be changed afterwards.');
    }

    function loadCampuses() {
        $.ajax({ url: API_BASE_URL + '/campuses/options', method: 'GET' })
            .done(function (res) { state.campuses = (res && res.data) || []; })
            .fail(function () { state.campuses = []; });
    }

    function buildLeadOptions(selectedId) {
        var options = ['<option value="">— No lead —</option>'];

        // A lead the caller cannot see in the candidate list (another campus, or
        // the roster was not loaded) must still survive a save, so the current
        // value is kept as an option rather than silently reset to none.
        var known = false;

        // Only leads at the campus this team is (or will be) at. The server refuses
        // a mismatch, so offering one would be a control whose only outcome is an
        // error message.
        var campusName = state.editing
            ? state.editing.campusName
            : selectedCampusName();

        var eligible = state.leads.filter(function (c) {
            return !campusName || !c.campusName || c.campusName === campusName;
        });

        eligible.forEach(function (c) {
            if (c.accountId === selectedId) known = true;

            var suffix = c.leadsTeam ? ' — leads ' + c.leadsTeam : '';
            var role = c.roleCode === 'PASTOR' ? ' (Pastor)' : '';

            options.push(
                '<option value="' + esc(c.accountId) + '"' +
                (c.accountId === selectedId ? ' selected' : '') + '>' +
                esc(c.name + role + suffix) + '</option>');
        });

        if (selectedId && !known) {
            var current = state.editing && state.editing.leadName ? state.editing.leadName : 'Current lead';
            options.push('<option value="' + esc(selectedId) + '" selected>' + esc(current) + '</option>');
        }

        $('#fLead').html(options.join(''));
    }

    /** The campus name currently chosen in the create picker, if any. */
    function selectedCampusName() {
        var id = $('#fCampus').val();
        var match = (state.campuses || []).filter(function (c) { return c.id === id; })[0];

        return match ? match.name : null;
    }

    function closeEditor() {
        $('#editModal').removeClass('open');
        state.editing = null;
    }

    function noticeInModal(message) {
        $('#editNotice').text(message).prop('hidden', false);
    }

    function save() {
        var name = ($('#fName').val() || '').trim();
        var max = parseInt($('#fMax').val(), 10);

        if (name.length < 2) { noticeInModal('Give the team a name of at least 2 characters.'); return; }
        if (!(max >= 1 && max <= 100)) { noticeInModal('Maximum members must be between 1 and 100.'); return; }

        var $btn = $('#saveBtn').prop('disabled', true).text('Saving…');
        var creating = !state.editing;

        var request = creating
            ? {
                url: API_BASE_URL + '/teams',
                method: 'POST',
                body: {
                    name: name,
                    leadAccountId: $('#fLead').val() || null,
                    maxMembers: max,

                    // Omitted when the picker was hidden, which lets the server fall
                    // back to the caller's own campus — right when there is only one.
                    campusId: $('#campusField').prop('hidden')
                        ? null
                        : ($('#fCampus').val() || null)
                }
            }
            : {
                url: API_BASE_URL + '/teams/' + encodeURIComponent(state.editing.id),
                method: 'PUT',
                body: {
                    name: name,
                    leadAccountId: $('#fLead').val() || null,
                    maxMembers: max,
                    isActive: $('#fActive').is(':checked'),
                    rowVersion: state.editing.rowVersion
                }
            };

        $.ajax({
            url: request.url,
            method: request.method,
            contentType: 'application/json',
            data: JSON.stringify(request.body)
        })
            .done(function (res) {
                // A refusal arrives as HTTP 200 with responseType 1 — a rejected
                // rename is not a server error.
                if (res && res.responseType !== 0) {
                    noticeInModal(res.message || 'That could not be saved.');
                    return;
                }

                showToast(res.message || 'Saved.', 'success');
                closeEditor();
                loadTeams();
            })
            .fail(function (xhr) {
                var body = xhr && xhr.responseJSON;
                noticeInModal((body && (body.message || body.detail || body.title)) ||
                              'That could not be saved.');
            })
            .always(function () { $btn.prop('disabled', false).text('Save'); });
    }

    // ── Wiring ─────────────────────────────────────────────────────────────
    function bind() {
        $('#search').on('input', function () {
            state.search = this.value;
            render();
        });

        $('#statusFilter').on('change', function () {
            state.includeInactive = this.value === 'all';
            loadTeams();
        });

        $('#newTeamBtn').on('click', function () { openEditor(null); });

        $('#teamsTable').on('click', '[data-edit]', function () {
            var id = $(this).data('edit');
            var team = state.teams.filter(function (t) { return t.id === id; })[0];
            if (team) openEditor(team);
        });

        // Changing the campus changes who is eligible to lead it.
        $('#fCampus').on('change', function () { buildLeadOptions($('#fLead').val()); });

        $('#editModal').on('click', '[data-close]', closeEditor);

        // Clicking the backdrop closes; clicking inside the dialog must not.
        $('#editModal').on('click', function (e) {
            if (e.target === this) closeEditor();
        });

        $('#saveBtn').on('click', save);

        $(document).on('keydown', function (e) {
            if (e.key === 'Escape' && $('#editModal').hasClass('open')) closeEditor();
        });
    }
});
