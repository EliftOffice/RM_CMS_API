/*
 * Account management.
 *
 * Backs Admin/accounts.html against the identity module:
 *   GET    /api/admin/accounts
 *   POST   /api/admin/accounts
 *   POST   /api/admin/accounts/{id}/password
 *   PUT    /api/admin/accounts/{id}/roles
 *   PUT    /api/admin/accounts/{id}/status
 *   GET    /api/admin/roles
 *
 * auth.js has already wrapped $.ajax, so every call here carries the bearer
 * token and transparently refreshes on a 401.
 */
(function (window, $) {
    'use strict';

    var esc = AdminShell.escapeHtml;

    var state = {
        page: 1,
        pageSize: 25,
        total: 0,
        search: '',
        role: '',
        roles: [],          // assignable role codes
        editing: null       // account being edited
    };

    // Human wording for each role. The API returns codes; an admin should not
    // have to know what DATA_ENTRY means.
    var ROLE_LABELS = {
        ADMIN:      { label: 'Administrator', desc: 'Full access, including accounts and settings' },
        PASTOR:     { label: 'Pastor',        desc: 'Cross-team oversight and reporting' },
        TEAM_LEAD:  { label: 'Team Lead',     desc: 'Runs a team: escalations, check-ins, reviews' },
        VOLUNTEER:  { label: 'Volunteer',     desc: 'Handles assigned care cases' },
        DATA_ENTRY: { label: 'Data Entry',    desc: 'Records visitors at intake only' },
        WEB_COORDINATOR: { label: 'Website Coordinator',
                           desc: 'Reviews enquiries from the public website only' }
    };

    function roleLabel(code) {
        return (ROLE_LABELS[code] || {}).label || code;
    }

    // ------------------------------------------------------------------
    // Data
    // ------------------------------------------------------------------

    function loadRoles() {
        return $.get(API_BASE_URL + '/admin/roles').then(function (res) {
            state.roles = (RmAuth.pick(res, 'data') || []).filter(function (c) { return !!c; });

            var $filter = $('#roleFilter');
            state.roles.forEach(function (code) {
                $filter.append('<option value="' + esc(code) + '">' + esc(roleLabel(code)) + '</option>');
            });
        });
    }

    function loadAccounts() {
        var query = {
            page: state.page,
            pageSize: state.pageSize
        };
        if (state.search) query.search = state.search;
        if (state.role) query.role = state.role;

        $('#rows').html('<tr><td colspan="6" class="empty">Loading…</td></tr>');

        return $.get(API_BASE_URL + '/admin/accounts', query)
            .then(function (res) {
                var data = RmAuth.pick(res, 'data') || {};
                var items = RmAuth.pick(data, 'items') || [];

                state.total = RmAuth.pick(data, 'totalCount') || 0;

                renderRows(items);
                renderPager(RmAuth.pick(data, 'totalPages') || 0);
            })
            .fail(function () {
                $('#rows').html('<tr><td colspan="6" class="empty">Could not load accounts.</td></tr>');
            });
    }

    // ------------------------------------------------------------------
    // Rendering
    // ------------------------------------------------------------------

    function codesOf(account) {
        return (RmAuth.pick(account, 'roles') || []).map(function (r) {
            return (typeof r === 'string') ? r : RmAuth.pick(r, 'roleCode');
        }).filter(Boolean);
    }

    function renderRows(items) {
        if (!items.length) {
            $('#rows').html('<tr><td colspan="6" class="empty">No accounts match.</td></tr>');
            $('#countInfo').text('');
            return;
        }

        var html = items.map(function (a) {
            var id = RmAuth.pick(a, 'id');
            var active = RmAuth.pick(a, 'isActive');
            var mustChange = RmAuth.pick(a, 'mustChangePassword');
            var last = RmAuth.pick(a, 'lastLoginAt');

            var roleBadges = codesOf(a).map(function (c) {
                return '<span class="badge badge-role">' + esc(roleLabel(c)) + '</span>';
            }).join('') || '<span class="cell-sub">none</span>';

            var status = active
                ? '<span class="badge badge-ok">Active</span>'
                : '<span class="badge badge-off">Disabled</span>';

            if (mustChange) status += '<span class="badge badge-warn">Must set password</span>';

            return '<tr data-id="' + esc(id) + '" data-username="' +
                       esc(RmAuth.pick(a, 'username') || '') + '">' +
                '<td><div class="cell-name">' + esc(RmAuth.pick(a, 'fullName') || '—') + '</div></td>' +
                '<td>' + esc(RmAuth.pick(a, 'username') || '') + '</td>' +
                '<td>' + roleBadges + '</td>' +
                '<td>' + status + '</td>' +
                '<td class="cell-sub">' + (last ? esc(new Date(last).toLocaleString()) : 'never') + '</td>' +
                '<td class="cell-actions">' +
                    '<button type="button" class="btn btn-sm" data-act="roles">Roles</button>' +
                    '<button type="button" class="btn btn-sm" data-act="password">Password</button>' +
                    '<button type="button" class="btn btn-sm ' + (active ? 'btn-danger' : '') + '" data-act="toggle">' +
                        (active ? 'Disable' : 'Enable') +
                    '</button>' +
                '</td>' +
            '</tr>';
        }).join('');

        $('#rows').html(html);
        $('#countInfo').text(state.total + (state.total === 1 ? ' account' : ' accounts'));
    }

    function renderPager(totalPages) {
        $('#pageInfo').text(totalPages ? ('Page ' + state.page + ' of ' + totalPages) : '');
        $('#prevPage').prop('disabled', state.page <= 1);
        $('#nextPage').prop('disabled', state.page >= totalPages);
    }

    function renderRoleChoices(hostId, selected) {
        selected = selected || [];

        $('#' + hostId).html(state.roles.map(function (code) {
            var meta = ROLE_LABELS[code] || {};
            var checked = selected.indexOf(code) !== -1 ? ' checked' : '';

            return '<label class="role-option">' +
                '<input type="checkbox" value="' + esc(code) + '"' + checked + '>' +
                '<span><strong>' + esc(meta.label || code) + '</strong>' +
                (meta.desc ? '<div class="role-desc">' + esc(meta.desc) + '</div>' : '') +
                '</span></label>';
        }).join(''));
    }

    function selectedRoles(hostId) {
        return $('#' + hostId + ' input:checked').map(function () {
            // campusId null == organisation-wide. Campus-scoped grants are a
            // later screen; the API already supports them.
            return { RoleCode: this.value, CampusId: null };
        }).get();
    }

    // ------------------------------------------------------------------
    // Modals
    // ------------------------------------------------------------------

    function openModal(id) { $('#' + id).addClass('open'); }
    function closeModal(id) { $('#' + id).removeClass('open'); }

    /**
     * The identifiers the server refuses to see inside a password: the username and
     * each part of the person's name. Split on whitespace because the server checks
     * GivenName and FamilyName separately, and only the parts of four characters or
     * more matter — "Kumar" is what rejects "Prasanthkumar@1234".
     */
    function identifiersFor(editing) {
        if (!editing) return [];

        var parts = String(editing.name || '').split(/\s+/).filter(Boolean);

        if (editing.username) parts.push(String(editing.username));

        return parts;
    }

    function showCredential(password, who) {
        $('#credentialValue').text(password);
        $('#credentialFor').text(who ? ('For ' + who) : '');
        openModal('credentialModal');
    }

    /** Reads {message} out of an ApiResponse or a ProblemDetails body. */
    function errorFrom(jqXHR, fallback) {
        var body = jqXHR && jqXHR.responseJSON;
        if (!body) return fallback;

        var message = RmAuth.pick(body, 'message') || RmAuth.pick(body, 'title');

        var errors = RmAuth.pick(body, 'errors');
        if (errors) {
            var first = Object.keys(errors)[0];
            if (first && errors[first] && errors[first].length) return errors[first][0];
        }

        return message || fallback;
    }

    // ------------------------------------------------------------------
    // Wiring
    // ------------------------------------------------------------------

    $(function () {
        AdminShell.boot({ roles: ['ADMIN'], active: 'accounts.html' }).then(function (ok) {
            if (!ok) return;

            loadRoles().then(loadAccounts);

            // ---- search / filter ----
            var searchTimer;
            $('#search').on('input', function () {
                clearTimeout(searchTimer);
                var value = this.value.trim();
                searchTimer = setTimeout(function () {
                    state.search = value;
                    state.page = 1;
                    loadAccounts();
                }, 300);
            });

            $('#roleFilter').on('change', function () {
                state.role = this.value;
                state.page = 1;
                loadAccounts();
            });

            $('#prevPage').on('click', function () { state.page--; loadAccounts(); });
            $('#nextPage').on('click', function () { state.page++; loadAccounts(); });

            $('[data-close]').on('click', function () {
                $(this).closest('.modal-backdrop').removeClass('open');
            });

            // ---- create ----
            $('#btnNew').on('click', function () {
                $('#newPersonId, #newUsername, #newPassword').val('');
                renderRoleChoices('newRoles', ['VOLUNTEER']);
                openModal('createModal');
            });

            $('#btnCreate').on('click', function () {
                var roles = selectedRoles('newRoles');

                if (!roles.length) { showToast('Select at least one role', 'warning'); return; }

                var body = {
                    PersonId: $('#newPersonId').val().trim(),
                    Username: $('#newUsername').val().trim(),
                    Roles: roles
                };

                var password = $('#newPassword').val();
                if (password) body.InitialPassword = password;

                var $btn = $(this).prop('disabled', true);

                $.ajax({
                    url: API_BASE_URL + '/admin/accounts',
                    method: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify(body)
                }).done(function (res) {
                    var data = RmAuth.pick(res, 'data') || {};
                    var generated = RmAuth.pick(data, 'generatedPassword');
                    var account = RmAuth.pick(data, 'account') || {};

                    closeModal('createModal');
                    showToast('Account created', 'success');
                    loadAccounts();

                    if (generated) showCredential(generated, RmAuth.pick(account, 'fullName'));
                }).fail(function (jqXHR) {
                    showToast(errorFrom(jqXHR, 'Could not create the account'), 'error');
                }).always(function () {
                    $btn.prop('disabled', false);
                });
            });

            // ---- row actions ----
            $('#rows').on('click', 'button[data-act]', function () {
                var $row = $(this).closest('tr');
                var id = $row.data('id');
                var action = $(this).data('act');
                var name = $row.find('.cell-name').text();

                state.editing = { id: id, name: name, username: $row.data('username') };

                if (action === 'roles') {
                    var current = $row.find('.badge-role').map(function () {
                        var label = $(this).text();
                        return Object.keys(ROLE_LABELS).filter(function (c) {
                            return ROLE_LABELS[c].label === label;
                        })[0] || label;
                    }).get();

                    $('#rolesFor').text(name);
                    renderRoleChoices('editRoles', current);
                    openModal('rolesModal');
                }

                if (action === 'password') {
                    $('#resetPassword').val('');
                    $('#forceChange').prop('checked', true);
                    $('#passwordFor').text(name);

                    // Pass the account's identifiers so the rule people actually trip
                    // over — "must not contain the username or name" — is checked as
                    // the administrator types, not after the server refuses.
                    PasswordPolicy.attach(
                        document.getElementById('resetPassword'),
                        document.getElementById('passwordRulesList'),
                        identifiersFor(state.editing));

                    openModal('passwordModal');
                }

                if (action === 'toggle') {
                    var enabling = $(this).text().trim() === 'Enable';

                    if (!enabling && !window.confirm('Disable ' + name + '? They will be signed out everywhere.')) return;

                    $.ajax({
                        url: API_BASE_URL + '/admin/accounts/' + encodeURIComponent(id) + '/status',
                        method: 'PUT',
                        contentType: 'application/json',
                        data: JSON.stringify({ IsActive: enabling })
                    }).done(function () {
                        showToast(enabling ? 'Account enabled' : 'Account disabled', 'success');
                        loadAccounts();
                    }).fail(function (jqXHR) {
                        showToast(errorFrom(jqXHR, 'Could not update the account'), 'error');
                    });
                }
            });

            // ---- save roles ----
            $('#btnSaveRoles').on('click', function () {
                var roles = selectedRoles('editRoles');

                if (!roles.length) { showToast('Select at least one role', 'warning'); return; }

                $.ajax({
                    url: API_BASE_URL + '/admin/accounts/' + encodeURIComponent(state.editing.id) + '/roles',
                    method: 'PUT',
                    contentType: 'application/json',
                    data: JSON.stringify({ Roles: roles })
                }).done(function () {
                    closeModal('rolesModal');
                    showToast('Roles updated', 'success');
                    loadAccounts();
                }).fail(function (jqXHR) {
                    showToast(errorFrom(jqXHR, 'Could not update roles'), 'error');
                });
            });

            // ---- set password ----
            $('#btnSavePassword').on('click', function () {
                var body = { MustChangePassword: $('#forceChange').is(':checked') };
                var password = $('#resetPassword').val();
                if (password) body.NewPassword = password;

                $.ajax({
                    url: API_BASE_URL + '/admin/accounts/' + encodeURIComponent(state.editing.id) + '/password',
                    method: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify(body)
                }).done(function (res) {
                    var data = RmAuth.pick(res, 'data') || {};
                    var generated = RmAuth.pick(data, 'generatedPassword');

                    closeModal('passwordModal');
                    showToast('Password updated', 'success');
                    loadAccounts();

                    if (generated) showCredential(generated, state.editing.name);
                }).fail(function (jqXHR) {
                    showToast(errorFrom(jqXHR, 'Could not set the password'), 'error');
                });
            });

            // ---- copy credential ----
            $('#btnCopyCredential').on('click', function () {
                var value = $('#credentialValue').text();

                if (navigator.clipboard) {
                    navigator.clipboard.writeText(value).then(function () {
                        showToast('Copied', 'success');
                    });
                }
            });
        });
    });

})(window, window.jQuery);
