/*
 * Area management.
 *
 *   GET  /api/areas/access              what THIS caller may do
 *   GET  /api/areas?includeInactive=    the list, with who is filed against each
 *   POST /api/areas                     create
 *   PUT  /api/areas/{id}                rename, retire, reactivate
 *
 * Two roles reach this page and only one of them reaches it by right, so the
 * screen renders from /access and never from the role claim:
 *
 *   ADMINISTRATOR    every campus's areas, plus the campus picker on create
 *   OWNCAMPUSONLY    a data-entry operator an administrator has granted this
 *                    (area.manage_by_data_entry) — their own campus only
 *   NONE             the page is replaced by an explanation
 *
 * There is no delete. People point at these rows and their records outlive the
 * label; retiring takes an area out of the picker and leaves everyone who lives
 * there pointing at it.
 */
$(function () {
    'use strict';

    var esc = AdminShell.escapeHtml;

    var state = {
        access: null,
        areas: [],
        campuses: [],
        search: '',
        includeInactive: true,
        editing: null      // null = creating
    };

    // Both roles that could hold this. The server decides whether they actually
    // do; this only stops the page booting for anyone else.
    AdminShell.boot({
        roles: ['ADMIN', 'DATA_ENTRY'],
        active: { href: '/templates/Admin/areas.html', area: 'Areas' }
    }).then(function (ok) {
        if (!ok) return;
        loadAccess();
    });

    // ── Access ─────────────────────────────────────────────────────────────
    function loadAccess() {
        $.ajax({ url: API_BASE_URL + '/areas/access', method: 'GET' })
            .done(function (res) {
                state.access = (res && res.data) || {};

                if (!state.access.canOpen) { showLocked(); return; }

                $('#pageBody').prop('hidden', false);
                renderScopeNote();
                bind();

                $('#newBtn').prop('hidden', !state.access.canCreate);

                // The campus column and the campus picker are only meaningful to
                // someone who can see more than one campus. For a granted operator
                // every row is their own campus, so the column would repeat the same
                // value down the page.
                if (state.access.canSeeAllCampuses) loadCampuses();
                else $('.campus-col').hide();

                load();
            })
            .fail(function (xhr) {
                // 403 is the policy refusing the role outright, which is a different
                // thing from a role that simply has not been granted.
                if (xhr && xhr.status === 403) { showLocked(); return; }
                showToast('Could not check your access.', 'error');
            });
    }

    function showLocked() {
        var roles = RmAuth.roleCodes();

        var why = roles.indexOf('DATA_ENTRY') !== -1
            ? 'Data entry operators can be given access to manage areas, but it is ' +
              'switched off. The setting is area.manage_by_data_entry.'
            : 'Your role cannot manage areas.';

        $('#lockedWhy').html(esc(why).replace(
            /(area\.manage_by_[a-z_]+)/,
            '<span class="grant-key">$1</span>'));

        $('#lockedBody').prop('hidden', false);
    }

    /**
     * Says plainly what this person can do. A granted operator sees the same table
     * an administrator does, minus other campuses, and nothing on screen would
     * otherwise tell them which they are looking at.
     */
    function renderScopeNote() {
        if (state.access.scope !== 'OWNCAMPUSONLY') return;

        $('#scopeNote').html(
            '<div class="notice scope-note"><div>' +
            esc('You can add, rename and retire areas at your own campus. ' +
                'An administrator granted this; it is not part of the data entry role.') +
            '</div></div>');
    }

    // ── Data ───────────────────────────────────────────────────────────────
    function load() {
        $.ajax({
            url: API_BASE_URL + '/areas',
            method: 'GET',
            data: { includeInactive: state.includeInactive }
        })
            .done(function (res) {
                // A refusal arrives as HTTP 200 with responseType 1, so .done() fires
                // for it and an unchecked handler would render an empty table as
                // though there were simply no areas.
                if (!res || res.responseType !== 0) {
                    $('#areaTable tbody').html(
                        '<tr><td colspan="5" class="empty">' +
                        esc((res && res.message) || 'Could not load areas.') +
                        '</td></tr>');
                    return;
                }

                state.areas = res.data || [];
                render();
            })
            .fail(function () {
                $('#areaTable tbody').html(
                    '<tr><td colspan="5" class="empty">Could not load areas.</td></tr>');
            });
    }

    function loadCampuses() {
        $.ajax({ url: API_BASE_URL + '/campuses/options', method: 'GET' })
            .done(function (res) {
                state.campuses = (res && res.data) || [];

                $('#fCampus').html(state.campuses.map(function (c) {
                    return '<option value="' + esc(c.id) + '">' + esc(c.name) + '</option>';
                }).join(''));
            })
            .fail(function () { state.campuses = []; });
    }

    // ── Render ─────────────────────────────────────────────────────────────
    function visible() {
        var term = state.search.trim().toLowerCase();

        return state.areas.filter(function (a) {
            return !term || String(a.name).toLowerCase().indexOf(term) !== -1;
        });
    }

    function render() {
        var rows = visible();

        if (!rows.length) {
            $('#areaTable tbody').html(
                '<tr><td colspan="5" class="empty">' +
                (state.areas.length
                    ? 'No area matches that.'
                    : 'No areas yet. One is added automatically the first time somebody ' +
                      'types a new one while recording a visitor.') +
                '</td></tr>');
            return;
        }

        var showCampus = state.access.canSeeAllCampuses;

        $('#areaTable tbody').html(rows.map(function (a) {
            return '' +
                '<tr>' +
                    '<td><strong>' + esc(a.name) + '</strong></td>' +
                    '<td class="campus-col"' + (showCampus ? '' : ' style="display:none"') + '>' +
                        '<span class="cell-sub">' + esc(a.campusName || '') + '</span></td>' +
                    '<td>' + filed(a) + '</td>' +
                    '<td>' + status(a) + '</td>' +
                    '<td class="cell-actions">' +
                        (state.access.canEdit
                            ? '<button type="button" class="btn btn-sm" data-edit="' +
                                  esc(a.id) + '">Edit</button>'
                            : '') +
                    '</td>' +
                '</tr>';
        }).join(''));
    }

    /**
     * Shown even when zero, because this is also the answer to "what happens if I
     * retire it" — a blank cell would leave that to guesswork.
     */
    function filed(a) {
        var bits = [
            ['people', a.personCount],
            ['volunteers', a.volunteerCount]
        ];

        return '<div class="filed">' + bits.map(function (b) {
            // A count an older server did not send reads as 0, not "undefined".
            var n = b[1] || 0;
            return '<span class="' + (n ? '' : 'zero') + '"><b>' + n + '</b> ' + b[0] + '</span>';
        }).join('') + '</div>';
    }

    function status(a) {
        return a.isActive
            ? '<span class="badge badge-ok">Active</span>'
            : '<span class="badge badge-off">Retired</span>';
    }

    // ── Editor ─────────────────────────────────────────────────────────────
    function openEditor(area) {
        state.editing = area || null;

        var creating = !area;

        $('#editTitle').text(creating ? 'New area' : 'Edit ' + area.name);
        $('#editNotice').prop('hidden', true).text('');

        $('#fName').val(creating ? '' : area.name);
        $('#fActive').prop('checked', creating ? true : area.isActive);

        // The campus is set once. Moving an area between sites would silently
        // re-file everybody who lives there, so it is offered on create and shown
        // as plain text afterwards.
        var pickCampus = creating && state.access.canSeeAllCampuses && state.campuses.length > 1;

        $('#campusField').prop('hidden', !pickCampus);

        if (pickCampus && area) $('#fCampus').val(area.campusId);

        // A new area is always active; the checkbox would be a control with one
        // possible answer.
        $('#activeField').prop('hidden', creating);

        $('#retireWhy')
            .prop('hidden', !(area && area.isActive && area.personCount > 0))
            .text(area && area.personCount > 0
                ? area.personCount + ' person(s) are filed here. Retiring it does not move ' +
                  'them — they keep this area, it just stops being offered when recording ' +
                  'someone new.'
                : '');

        $('#editModal').addClass('open');
        $('#fName').trigger('focus');
    }

    function closeEditor() {
        $('#editModal').removeClass('open');
        state.editing = null;
    }

    function save() {
        var name = $.trim($('#fName').val());

        if (name.length < 2) return notice('Give the area a name of at least two characters.');

        var creating = !state.editing;
        var body, url, method;

        if (creating) {
            body = { name: name };

            if (!$('#campusField').prop('hidden')) {
                var campus = $('#fCampus').val();
                if (campus) body.campusId = campus;
            }

            url = API_BASE_URL + '/areas';
            method = 'POST';
        } else {
            body = {
                name: name,
                isActive: $('#fActive').is(':checked'),
                rowVersion: state.editing.rowVersion
            };

            url = API_BASE_URL + '/areas/' + encodeURIComponent(state.editing.id);
            method = 'PUT';
        }

        var $btn = $('#saveBtn').prop('disabled', true).text('Saving…');

        $.ajax({ url: url, method: method, contentType: 'application/json', data: JSON.stringify(body) })
            .done(function (res) {
                // A refusal arrives as HTTP 200 with responseType 1.
                if (res && res.responseType !== 0) { notice(res.message); return; }

                showToast(res.message || 'Saved.', 'success');
                closeEditor();
                load();
            })
            .fail(function (xhr) {
                var b = xhr && xhr.responseJSON;
                notice((b && (b.message || b.detail || b.title)) || 'That could not be saved.');
            })
            .always(function () { $btn.prop('disabled', false).text('Save'); });
    }

    function notice(text) {
        $('#editNotice').prop('hidden', false).text(text);
    }

    // ── Wiring ─────────────────────────────────────────────────────────────
    function bind() {
        $('#newBtn').on('click', function () { openEditor(null); });

        $('#statusFilter').on('change', function () {
            state.includeInactive = this.value === 'all';
            load();
        });

        // Filtered in the browser: the whole list is already here, and a round trip
        // per keystroke would make it slower, not faster.
        $('#search').on('input', function () {
            state.search = this.value;
            render();
        });

        $(document).on('click', '[data-edit]', function () {
            var id = $(this).data('edit');
            var area = state.areas.filter(function (a) { return a.id === id; })[0];
            if (area) openEditor(area);
        });

        $(document).on('click', '[data-close]', closeEditor);
        $('#saveBtn').on('click', save);

        $('#editModal').on('click', function (e) {
            if (e.target === this) closeEditor();
        });
    }
});
