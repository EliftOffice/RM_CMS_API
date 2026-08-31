/*
 * User management.
 *
 * One screen, filtered by role tab, because "All Users", "Volunteers", "Team Leads",
 * "Pastors" and "Data Entry Operators" are the same view over the same endpoint —
 * five near-identical pages would drift apart the first time a column changed.
 * Each tab is deep-linkable as ?role=TEAM_LEAD.
 *
 *   GET  /api/admin/users            list, filtered by role/search/status
 *   GET  /api/admin/users/{personId} detail + role history
 *   POST /api/admin/users/{id}/promote
 *   PUT  /api/admin/accounts/{id}/roles | /status
 *   POST /api/admin/accounts/{id}/password
 *
 * The unit here is a PERSON, not an account: someone can hold a volunteer record
 * with no sign-in, so rows are keyed by person id and account actions are hidden
 * when there is no account to act on.
 */
$(function () {
    'use strict';

    var TABS = [
        { code: '',            label: 'All users' },
        { code: 'VOLUNTEER',   label: 'Volunteers' },
        { code: 'TEAM_LEAD',   label: 'Team Leads' },
        { code: 'PASTOR',      label: 'Pastors' },
        { code: 'DATA_ENTRY',  label: 'Data Entry' },
        { code: 'ADMIN',       label: 'Admins' }
    ];

    var ROLE_LABELS = {
        ADMIN: 'Administrator',
        PASTOR: 'Pastor',
        TEAM_LEAD: 'Team Lead',
        VOLUNTEER: 'Volunteer',
        DATA_ENTRY: 'Data Entry Operator'
    };

    var state = {
        role: new URLSearchParams(window.location.search).get('role') || '',
        search: '',
        isActive: '',
        page: 1,
        pageSize: 25,
        total: 0,
        editing: null,
        bands: [],
        teams: []
    };

    /**
     * Telegram mark plus a green tick. Inline SVG rather than an image so it
     * inherits colour and stays crisp, and carries its own accessible label —
     * a bare tick in a table tells a screen reader nothing.
     */
    var TELEGRAM_LINKED =
        '<span class="tg-ok" role="img" aria-label="Telegram connected">' +
          '<svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">' +
            '<path fill="#229ED9" d="M21.9 4.3 18.9 19c-.2 1-.8 1.2-1.7.8l-4.6-3.4-2.2 2.2c-.3.3-.5.5-1 .5l.3-4.7 8.6-7.8c.4-.3-.1-.5-.6-.2L6.900 11l-4.6-1.4c-1-.3-1-1 .2-1.5l18-6.9c.8-.3 1.5.2 1.2 1.4z"/>' +
          '</svg>' +
          '<svg class="tg-tick" viewBox="0 0 24 24" width="11" height="11" aria-hidden="true">' +
            '<circle cx="12" cy="12" r="12" fill="#16a34a"/>' +
            '<path d="M7 12.5l3.2 3.2L17 9" fill="none" stroke="#fff" ' +
                  'stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"/>' +
          '</svg>' +
        '</span>';

    var esc = AdminShell.escapeHtml;

    AdminShell.boot({
        roles: ['ADMIN'],
        active: { href: '/templates/Admin/users.html', area: 'Admin' }
    }).then(function (ok) {
        if (!ok) return;
        $('#pageBody').prop('hidden', false);
        init();
    });

    function init() {
        renderTabs();
        loadReference();
        load();

        var searchTimer = null;
        $('#search').on('input', function () {
            var value = $(this).val();
            clearTimeout(searchTimer);
            searchTimer = setTimeout(function () {
                state.search = value; state.page = 1; load();
            }, 250);
        });

        $('#statusFilter').on('change', function () {
            state.isActive = $(this).val(); state.page = 1; load();
        });

        $('#prevPage').on('click', function () { if (state.page > 1) { state.page--; load(); } });
        $('#nextPage').on('click', function () {
            if (state.page * state.pageSize < state.total) { state.page++; load(); }
        });

        $('[data-close]').on('click', closeModals);
        $('.modal-backdrop').on('click', function (e) { if (e.target === this) closeModals(); });

        wireRowActions();
        wireTelegram();
        wireModals();
    }

    // ------------------------------------------------------------------ tabs

    function renderTabs() {
        $('#roleTabs').html(TABS.map(function (t) {
            var cls = (t.code === state.role) ? 'tab tab-active' : 'tab';
            return '<button type="button" class="' + cls + '" data-role="' + esc(t.code) + '">' +
                   esc(t.label) + '</button>';
        }).join(''));

        $('#roleTabs .tab').on('click', function () {
            state.role = $(this).data('role') || '';
            state.page = 1;

            // Keep the URL honest so a tab can be linked to and reloaded.
            var url = state.role
                ? '?role=' + encodeURIComponent(state.role)
                : window.location.pathname;
            window.history.replaceState({}, '', url);

            renderTabs();
            load();
        });
    }

    // ------------------------------------------------------------------ data

    function loadReference() {
        $.ajax({ url: API_BASE_URL + '/volunteers/reference', method: 'GET' })
            .done(function (res) { state.bands = (res.data && res.data.capacityBands) || []; })
            .fail(function () { /* only needed when promoting to volunteer */ });

        $.ajax({ url: API_BASE_URL + '/teams', method: 'GET' })
            .done(function (res) { state.teams = res.data || []; })
            .fail(function () { /* team assignment is optional */ });
    }

    function load() {
        var query = {
            page: state.page,
            pageSize: state.pageSize,
            search: state.search || undefined,
            role: state.role || undefined,
            isActive: state.isActive === '' ? undefined : state.isActive
        };

        $.ajax({ url: API_BASE_URL + '/admin/users', method: 'GET', data: query })
            .done(function (res) {
                if (!res || res.responseType === 2) {
                    showToast((res && res.message) || 'Could not load users.', 'error');
                    return;
                }

                state.total = res.data.totalCount;
                renderRows(res.data.items);
                $('#countInfo').text(res.data.totalCount + ' user(s)');
            })
            .fail(function () { showToast('Could not load users.', 'error'); });
    }

    function renderRows(items) {
        if (!items.length) {
            $('#rows').html('<tr><td colspan="7" class="empty">Nobody matches.</td></tr>');
            return;
        }

        $('#rows').html(items.map(function (u) {
            var roles = u.roles.length
                ? u.roles.map(function (r) {
                    return '<span class="badge badge-role">' + esc(ROLE_LABELS[r] || r) + '</span>';
                  }).join('')
                : '<span class="cell-sub">no roles</span>';

            // Someone can be a volunteer without any grant — worth showing, because it
            // explains why they carry cases yet hold no role.
            var volunteer = u.isVolunteer
                ? esc(u.volunteerReferenceCode || 'yes') +
                  '<div class="cell-sub">' + esc(u.capacityBandCode || '') +
                  ' · load ' + (u.currentCaseLoad == null ? 0 : u.currentCaseLoad) + '</div>'
                : '<span class="cell-sub">—</span>';

            var status = !u.hasAccount
                ? '<span class="badge badge-off">No sign-in</span>'
                : (u.isActive ? '<span class="badge badge-ok">Active</span>'
                              : '<span class="badge badge-off">Disabled</span>');

            if (u.hasAccount && u.mustChangePassword)
                status += '<span class="badge badge-warn">Must set password</span>';

            return '<tr data-person="' + esc(u.personId) + '">' +
                '<td><div class="cell-name">' + esc(u.fullName) + '</div>' +
                    '<div class="cell-sub">' + esc(u.personReferenceCode || '') + '</div></td>' +
                '<td>' + esc(u.username || '—') + '</td>' +
                '<td>' + roles + '</td>' +
                '<td>' + volunteer + '</td>' +
                '<td>' + status + '</td>' +
                '<td class="cell-sub">' +
                    (u.lastLoginAt ? esc(new Date(u.lastLoginAt).toLocaleString()) : 'never') + '</td>' +
                '<td class="cell-actions">' + actionsFor(u) + '</td>' +
            '</tr>';
        }).join(''));

        // Stash the row data so actions do not have to re-query.
        $('#rows tr').each(function (i) { $(this).data('user', items[i]); });
    }

    /** Only offer what this person's current standing actually allows. */
    function actionsFor(u) {
        var buttons = ['<button type="button" class="btn btn-sm" data-act="view">View</button>'];

        if (u.promotableTo.length)
            buttons.push('<button type="button" class="btn btn-sm" data-act="promote">Promote</button>');

        // Offered to everyone on file, account or not: reachability is a property of
        // the PERSON, and a volunteer without a login still needs alerts to arrive.
        //
        // Linked shows as an icon rather than a word, so a column of ticks can be
        // scanned for the gaps. It stays a button — clicking it still opens the
        // manage view — it just stops shouting at the people who are already fine.
        buttons.push(u.hasTelegram
            ? '<button type="button" class="btn btn-sm btn-icon" data-act="telegram" ' +
                  'title="Telegram connected">' + TELEGRAM_LINKED + '</button>'
            : '<button type="button" class="btn btn-sm" data-act="telegram">Telegram</button>');

        if (u.hasAccount) {
            buttons.push('<button type="button" class="btn btn-sm" data-act="roles">Roles</button>');
            buttons.push('<button type="button" class="btn btn-sm" data-act="password">Password</button>');
            buttons.push('<button type="button" class="btn btn-sm ' + (u.isActive ? 'btn-danger' : '') +
                         '" data-act="toggle">' + (u.isActive ? 'Disable' : 'Enable') + '</button>');
        }

        return buttons.join('');
    }

    // ------------------------------------------------------------------ actions

    function wireRowActions() {
        $('#rows').on('click', 'button[data-act]', function () {
            var $row = $(this).closest('tr');
            var user = $row.data('user');
            var action = $(this).data('act');

            state.editing = user;

            if (action === 'view')     return openDetail(user);
            if (action === 'promote')  return openPromote(user);
            if (action === 'roles')    return openRoles(user);
            if (action === 'password') return openPassword(user);
            if (action === 'telegram') return openTelegram(user);
            if (action === 'toggle')   return toggleStatus(user);
        });
    }

    // ------------------------------------------------------------------ telegram

    /**
     * Admin-side Telegram linking.
     *
     * Two ways in, and the order on screen is the order of preference:
     *
     *   1. Send them an invitation link. Telegram then tells us who they are, which
     *      is proof of ownership.
     *   2. Enter a chat id directly, when the office already holds one. No proof —
     *      the server checks the id is real and reachable, and nothing more.
     *
     * The second exists because it is sometimes the only option, not because it is
     * as good. The screen says so rather than presenting them as equals.
     */
    function openTelegram(user) {
        $('#tgPersonName').text(user.fullName);
        $('#tgChatId').val('');
        $('#tgUsername').val('');
        $('#tgNotice').prop('hidden', true).text('');
        $('#tgInvite').prop('hidden', true).text('');
        $('#tgStatus').text('Checking…');
        $('#tgDisconnectBtn').prop('hidden', true);
        $('#tgTestBtn').prop('hidden', true);

        $('#telegramModal').addClass('open');

        $.ajax({
            url: API_BASE_URL + '/telegram/status',
            method: 'GET',
            data: { personId: user.personId }
        })
            .done(function (res) {
                var s = (res && res.data) || {};

                if (!s.isConfigured) {
                    $('#tgStatus').text('The Telegram bot is not configured yet.');
                    return;
                }

                if (s.isLinked) {
                    var line = 'Connected' + (s.username ? ' as ' + s.username : '') + '.';

                    if (s.sharedWithCount > 0) {
                        line += s.sharedWithCount === 1
                            ? ' Shared with 1 other person, who receives the same messages.'
                            : ' Shared with ' + s.sharedWithCount +
                              ' other people, who all receive the same messages.';
                    }

                    $('#tgStatus').text(line);
                    $('#tgDisconnectBtn').prop('hidden', false);
                    $('#tgTestBtn').prop('hidden', false);
                } else {
                    $('#tgStatus').text('Not connected. They will not receive alerts.');
                }
            })
            .fail(function () { $('#tgStatus').text('Could not read their Telegram status.'); });
    }

    function tgNotice(text) { $('#tgNotice').prop('hidden', false).text(text); }

    function submitLink() {
        var chatId = $.trim($('#tgChatId').val());

        if (!chatId) return tgNotice('Enter the numeric chat id.');

        var $b = $('#tgLinkBtn').prop('disabled', true).text('Checking…');

        $.ajax({
            url: API_BASE_URL + '/telegram/link',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                personId: state.editing.personId,
                chatId: chatId,
                username: $.trim($('#tgUsername').val()) || null
            })
        })
            .done(function (res) {
                if (!res || res.responseType !== 0) { tgNotice(res && res.message); return; }

                // Carries the sharing count when there is one, which is the only
                // place that overlap is ever stated.
                showToast(res.message, 'success');
                openTelegram(state.editing);
                load();
            })
            .fail(function (xhr) {
                var b = xhr && xhr.responseJSON;
                tgNotice((b && (b.message || b.detail || b.title)) || 'That could not be linked.');
            })
            .always(function () { $b.prop('disabled', false).text('Link this chat id'); });
    }

    function wireTelegram() {
        // 1 — the route that proves ownership.
        $('#tgInviteBtn').on('click', function () {
            var $b = $(this).prop('disabled', true).text('Creating…');

            $.ajax({
                url: API_BASE_URL + '/telegram/invitation',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({ personId: state.editing.personId })
            })
                .done(function (res) {
                    if (!res || res.responseType !== 0) { tgNotice(res && res.message); return; }

                    $('#tgInvite').prop('hidden', false).text(res.data.deepLink);
                    tgNotice('Send them this link. It expires, and it can only be used once.');
                })
                .fail(function () { tgNotice('Could not create an invitation.'); })
                .always(function () { $b.prop('disabled', false).text('Create invitation link'); });
        });

        // 2 — the route that asserts it.
        $('#tgLinkBtn').on('click', submitLink);

        $('#tgTestBtn').on('click', function () {
            var $b = $(this).prop('disabled', true).text('Sending…');

            $.ajax({
                url: API_BASE_URL + '/telegram/test-message',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({ personId: state.editing.personId })
            })
                .done(function (res) {
                    if (!res || res.responseType !== 0) { tgNotice(res && res.message); return; }
                    tgNotice(res.message);
                })
                .fail(function () { tgNotice('Could not send a test message.'); })
                .always(function () { $b.prop('disabled', false).text('Send test message'); });
        });

        $('#tgDisconnectBtn').on('click', function () {
            var $b = $(this).prop('disabled', true).text('Disconnecting…');

            $.ajax({
                url: API_BASE_URL + '/telegram/disconnect',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({ personId: state.editing.personId })
            })
                .done(function (res) {
                    if (!res || res.responseType !== 0) { tgNotice(res && res.message); return; }

                    showToast(res.message, 'success');
                    openTelegram(state.editing);
                })
                .fail(function () { tgNotice('Could not disconnect.'); })
                .always(function () { $b.prop('disabled', false).text('Disconnect'); });
        });
    }

    function openDetail(user) {
        $.ajax({ url: API_BASE_URL + '/admin/users/' + encodeURIComponent(user.personId), method: 'GET' })
            .done(function (res) {
                var d = res.data;
                if (!d) { showToast(res.message || 'Not found.', 'warning'); return; }

                var u = d.user;

                $('#detailBody').html([
                    row('Name', u.fullName),
                    row('Reference', u.personReferenceCode || '—'),
                    row('Lifecycle', u.lifecycleStatus),
                    row('Mobile', u.mobile || '—'),
                    row('Email', u.email || '—'),
                    row('Campus', u.campusName || '—'),
                    row('Username', u.username || 'no sign-in'),
                    row('Roles', u.roles.map(function (r) { return ROLE_LABELS[r] || r; }).join(', ') || 'none'),
                    row('Volunteer', u.isVolunteer
                        ? (u.volunteerReferenceCode || 'yes') + ' · ' + (u.volunteerStatus || '') +
                          ' · load ' + (u.currentCaseLoad == null ? 0 : u.currentCaseLoad)
                        : 'no'),
                    row('Team', u.teamName || '—'),
                    row('Can become', u.promotableTo.map(function (r) { return ROLE_LABELS[r] || r; }).join(' → ') || '—'),
                    row('Last sign-in', u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : 'never')
                ].join(''));

                $('#historyBody').html(d.roleHistory.length
                    ? d.roleHistory.map(function (h) {
                        return '<div class="history-item">' +
                                 '<strong>' + esc(ROLE_LABELS[h.roleCode] || h.roleCode) + '</strong>' +
                                 '<div class="history-meta">granted ' +
                                   esc(new Date(h.grantedAt).toLocaleString()) +
                                   (h.grantedBy ? ' by ' + esc(h.grantedBy) : '') +
                                   (h.campusName ? ' · ' + esc(h.campusName) : ' · organisation-wide') +
                                 '</div>' +
                               '</div>';
                      }).join('')
                    : '<p class="empty">No role grants recorded.</p>');

                openModal('detailModal');
            })
            .fail(function () { showToast('Could not load that user.', 'error'); });
    }

    function row(label, value) {
        return '<dt>' + esc(label) + '</dt><dd>' + esc(value) + '</dd>';
    }

    function openPromote(user) {
        $('#promoteFor').text(user.fullName + ' — currently ' +
            (user.roles.map(function (r) { return ROLE_LABELS[r] || r; }).join(', ') ||
             (user.isVolunteer ? 'Volunteer (no sign-in)' : 'no role')));

        $('#promoteOptions').html(user.promotableTo.map(function (code, i) {
            return '<label class="role-pick">' +
                     '<input type="radio" name="promoteTo" value="' + esc(code) + '"' +
                       (i === 0 ? ' checked' : '') + '>' +
                     '<span><strong>' + esc(ROLE_LABELS[code] || code) + '</strong>' +
                       '<span class="sub">' + esc(promoteHint(code)) + '</span></span>' +
                   '</label>';
        }).join(''));

        $('#promoteBand').html('<option value="">Choose…</option>' + state.bands.map(function (b) {
            return '<option value="' + esc(b.code) + '">' + esc(b.label) +
                   ' — ' + b.minPerWeek + ' to ' + b.maxPerWeek + ' a week</option>';
        }).join(''));

        $('#promoteTeam').html('<option value="">No team yet</option>' + state.teams.map(function (t) {
            return '<option value="' + esc(t.id) + '">' + esc(t.name) + '</option>';
        }).join(''));

        syncPromoteFields();
        openModal('promoteModal');
    }

    function promoteHint(code) {
        if (code === 'VOLUNTEER') return 'Creates a volunteer record so they can be assigned people to follow up.';
        if (code === 'TEAM_LEAD') return 'Can see the whole campus, handle escalations and run check-ins. Keeps any volunteer record.';
        if (code === 'PASTOR')    return 'Cross-team oversight and reporting.';
        return '';
    }

    function syncPromoteFields() {
        var target = $('input[name="promoteTo"]:checked').val();
        var user = state.editing || {};

        // A capacity band is only needed when a volunteer record is about to be created.
        $('#bandField').prop('hidden', !(target === 'VOLUNTEER' && !user.isVolunteer));
        $('#teamField').prop('hidden', target !== 'TEAM_LEAD');

        $('#promoteNote').text(user.hasAccount
            ? 'Their existing record, cases and history stay exactly as they are.'
            : 'They have no sign-in yet, so one will be created using their mobile number as the username.');
    }

    function openRoles(user) {
        $('#rolesFor').text(user.fullName);

        $('#roleChoices').html(Object.keys(ROLE_LABELS).map(function (code) {
            var checked = user.roles.indexOf(code) !== -1 ? ' checked' : '';
            return '<label class="role-option">' +
                     '<input type="checkbox" value="' + esc(code) + '"' + checked + '>' +
                     '<span>' + esc(ROLE_LABELS[code]) + '</span>' +
                   '</label>';
        }).join(''));

        openModal('rolesModal');
    }

    function openPassword(user) {
        $('#resetPassword').val('');
        $('#forceChange').prop('checked', true);
        $('#passwordFor').text(user.fullName);

        // Identifiers so the "must not contain your name" rule is checked as you type.
        var identifiers = String(user.fullName || '').split(/\s+/).filter(Boolean);
        if (user.username) identifiers.push(user.username);

        PasswordPolicy.attach(
            document.getElementById('resetPassword'),
            document.getElementById('passwordRulesList'),
            identifiers);

        openModal('passwordModal');
    }

    function toggleStatus(user) {
        var enabling = !user.isActive;

        if (!enabling && !window.confirm('Disable ' + user.fullName + '? They will be signed out everywhere.'))
            return;

        $.ajax({
            url: API_BASE_URL + '/admin/accounts/' + encodeURIComponent(user.accountId) + '/status',
            method: 'PUT',
            contentType: 'application/json',
            data: JSON.stringify({ IsActive: enabling })
        })
            .done(function (res) {
                showToast(res.message || 'Updated', res.responseType === 0 ? 'success' : 'warning');
                load();
            })
            .fail(function () { showToast('Could not update that account.', 'error'); });
    }

    // ------------------------------------------------------------------ modals

    function wireModals() {
        $('#promoteOptions').on('change', 'input[name="promoteTo"]', syncPromoteFields);

        $('#btnConfirmPromote').on('click', function () {
            var target = $('input[name="promoteTo"]:checked').val();
            if (!target) { showToast('Choose a role.', 'warning'); return; }

            var body = { targetRole: target };
            var band = $('#promoteBand').val();
            var team = $('#promoteTeam').val();

            if (!$('#bandField').prop('hidden')) {
                if (!band) { showToast('Choose their weekly capacity.', 'warning'); return; }
                body.capacityBandCode = band;
            }

            if (target === 'TEAM_LEAD' && team) body.leadsTeamId = team;

            $('#btnConfirmPromote').prop('disabled', true);

            $.ajax({
                url: API_BASE_URL + '/admin/users/' + encodeURIComponent(state.editing.personId) + '/promote',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(body)
            })
                .done(function (res) {
                    $('#btnConfirmPromote').prop('disabled', false);

                    if (!res || res.responseType !== 0) {
                        showToast((res && res.message) || 'Could not promote.', 'warning');
                        return;
                    }

                    closeModals();
                    showToast(res.message, 'success');

                    // A generated password is shown once and never again, so it gets a
                    // modal rather than a toast that can be missed.
                    if (res.data && res.data.generatedPassword) {
                        showCredential(res.data.generatedPassword, res.data.user.username);
                    }

                    load();
                })
                .fail(function () {
                    $('#btnConfirmPromote').prop('disabled', false);
                    showToast('Could not promote.', 'error');
                });
        });

        $('#btnSaveRoles').on('click', function () {
            var roles = $('#roleChoices input:checked').map(function () {
                return { RoleCode: $(this).val(), CampusId: null };
            }).get();

            if (!roles.length) { showToast('Choose at least one role.', 'warning'); return; }

            $.ajax({
                url: API_BASE_URL + '/admin/accounts/' + encodeURIComponent(state.editing.accountId) + '/roles',
                method: 'PUT',
                contentType: 'application/json',
                data: JSON.stringify({ Roles: roles })
            })
                .done(function (res) {
                    closeModals();
                    showToast(res.message || 'Roles updated', res.responseType === 0 ? 'success' : 'warning');
                    load();
                })
                .fail(function () { showToast('Could not update roles.', 'error'); });
        });

        $('#btnSavePassword').on('click', function () {
            var body = { MustChangePassword: $('#forceChange').is(':checked') };
            var password = $('#resetPassword').val();
            if (password) body.NewPassword = password;

            $.ajax({
                url: API_BASE_URL + '/admin/accounts/' + encodeURIComponent(state.editing.accountId) + '/password',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(body)
            })
                .done(function (res) {
                    if (!res || res.responseType !== 0) {
                        showToast((res && res.message) || 'Could not set the password.', 'warning');
                        return;
                    }

                    closeModals();

                    var generated = res.data && res.data.generatedPassword;
                    if (generated) showCredential(generated, state.editing.username);
                    else showToast('Password updated', 'success');

                    load();
                })
                .fail(function () { showToast('Could not set the password.', 'error'); });
        });
    }

    function showCredential(password, who) {
        $('#credentialFor').text('For ' + (who || 'this account'));
        $('#credentialValue').text(password);
        openModal('credentialModal');
    }

    function openModal(id) { $('#' + id).addClass('open'); }
    function closeModals() { $('.modal-backdrop').removeClass('open'); }
});
