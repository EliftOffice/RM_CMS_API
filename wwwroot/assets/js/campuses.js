/*
 * Campus management. Administrators only.
 *
 *   GET  /api/admin/campuses?includeInactive=   the list, with what is attached
 *   POST /api/admin/campuses                    create
 *   PUT  /api/admin/campuses/{id}               rename, re-zone, retire
 *
 * Campus is the boundary CanAccessCampus scopes on, so creating one decides who
 * can read whose pastoral records. That is why this page is admin-only and why
 * there is no delete: a closed site still has a history somebody may need.
 *
 * The code is set once at creation and locked afterwards. Operators quote it and
 * it appears in exports, so a silent rename breaks both.
 */
$(function () {
    'use strict';

    var esc = AdminShell.escapeHtml;

    var state = {
        campuses: [],
        includeInactive: true,
        editing: null      // null = creating
    };

    AdminShell.boot({
        roles: ['ADMIN'],
        active: { href: '/pages/admin/campuses.html', area: 'Admin' }
    }).then(function (ok) {
        if (!ok) return;
        $('#pageBody').prop('hidden', false);
        bind();
        load();
    });

    // ── Load ───────────────────────────────────────────────────────────────
    function load() {
        $.ajax({
            url: API_BASE_URL + '/admin/campuses',
            method: 'GET',
            data: { includeInactive: state.includeInactive }
        })
            .done(function (res) {
                state.campuses = (res && res.data) || [];
                render();
            })
            .fail(function () {
                $('#campusTable tbody').html(
                    '<tr><td colspan="6" class="empty">Could not load campuses.</td></tr>');
            });
    }

    // ── Render ─────────────────────────────────────────────────────────────
    function render() {
        var rows = state.campuses;

        if (!rows.length) {
            $('#campusTable tbody').html(
                '<tr><td colspan="6" class="empty">No campuses.</td></tr>');
            return;
        }

        $('#campusTable tbody').html(rows.map(function (c) {
            return '' +
                '<tr>' +
                    '<td><strong>' + esc(c.name) + '</strong></td>' +
                    '<td><span class="code-chip">' + esc(c.code) + '</span></td>' +
                    '<td class="tz">' + esc(c.timezone) + '</td>' +
                    '<td>' + attached(c) + '</td>' +
                    '<td>' + status(c) + '</td>' +
                    '<td class="cell-actions">' +
                        '<button type="button" class="btn btn-sm" data-edit="' +
                            esc(c.id) + '">Edit</button>' +
                    '</td>' +
                '</tr>';
        }).join(''));
    }

    /**
     * The counts double as the explanation for why a campus cannot be retired, so
     * they are shown even when zero — a blank cell would leave the operator
     * guessing what "cannot retire" is about.
     */
    function attached(c) {
        // 'people' used to fold staff and visitors into one figure, which made it
        // mean nothing: 172 for a campus with 122 visitors. They are separate rows
        // because they are separate decisions when emptying a campus.
        var bits = [
            ['visitors', c.personCount],
            ['staff', c.staffCount],
            ['volunteers', c.volunteerCount],
            ['teams', c.teamCount],
            ['open cases', c.openCaseCount]
        ];

        return '<div class="attached">' + bits.map(function (b) {
            // A count the API did not send reads as 0, not "undefined". An older
            // server is the usual reason, and a stale field should look like an
            // empty one rather than shouting a JavaScript keyword at the operator.
            var n = b[1] || 0;

            return '<span class="' + (n ? '' : 'zero') + '"><b>' + n + '</b> ' + b[0] + '</span>';
        }).join('') + '</div>';
    }

    function status(c) {
        return c.isActive
            ? '<span class="pill pill-ok">Active</span>'
            : '<span class="pill">Retired</span>';
    }

    // ── Editor ─────────────────────────────────────────────────────────────
    function openEditor(campus) {
        state.editing = campus || null;

        var creating = !campus;

        $('#editTitle').text(creating ? 'New campus' : 'Edit ' + campus.name);
        $('#editNotice').prop('hidden', true).text('');

        $('#fName').val(creating ? '' : campus.name);
        $('#fCode').val(creating ? '' : campus.code);
        $('#fTimezone').val(creating ? 'Asia/Kolkata' : campus.timezone);
        $('#fActive').prop('checked', creating ? true : campus.isActive);

        // The code is immutable after creation, so it is shown but not editable
        // rather than hidden — the operator still needs to read it.
        $('#fCode').prop('disabled', !creating);
        $('#codeField').toggleClass('field-locked', !creating);
        $('#codeHint').html(creating
            ? 'Short handle, letters and numbers. Uppercased automatically. ' +
              '<strong>It cannot be changed later</strong> — operators quote it and it ' +
              'appears in exports.'
            : 'Set when the campus was created and fixed from then on.');

        // Active is meaningless while creating (a new campus is always active), and
        // retiring is blocked while anything is still attached.
        var blocked = !creating && campus.isActive && !campus.canRetire;

        $('#fActive').prop('disabled', creating || blocked);
        $('#activeField').toggleClass('field-locked', creating || blocked);

        $('#retireWhy')
            .prop('hidden', !blocked)
            .text(blocked
                ? 'Cannot be retired yet: ' + describe(campus) +
                  ' still belong to it. Move them to another campus first.'
                : '');

        $('#editModal').addClass('open');
        $('#fName').trigger('focus');
    }

    function describe(c) {
        var parts = [];
        if (c.personCount)    parts.push(c.personCount + ' visitor(s)');
        if (c.staffCount)     parts.push(c.staffCount + ' person(s) with a sign-in');
        if (c.volunteerCount) parts.push(c.volunteerCount + ' active volunteer(s)');
        if (c.teamCount)      parts.push(c.teamCount + ' active team(s)');
        if (c.openCaseCount)  parts.push(c.openCaseCount + ' open case(s)');
        return parts.join(', ');
    }

    function closeEditor() {
        $('#editModal').removeClass('open');
        state.editing = null;
    }

    function save() {
        var name = $.trim($('#fName').val());
        var tz   = $.trim($('#fTimezone').val());

        if (!name) return notice('Give the campus a name.');
        if (!tz)   return notice('Give the campus a time zone.');

        var creating = !state.editing;
        var body, url, method;

        if (creating) {
            var code = $.trim($('#fCode').val());
            if (!code) return notice('Give the campus a code.');

            body = { code: code, name: name, timezone: tz };
            url = API_BASE_URL + '/admin/campuses';
            method = 'POST';
        } else {
            body = {
                name: name,
                timezone: tz,
                isActive: $('#fActive').is(':checked'),
                rowVersion: state.editing.rowVersion
            };
            url = API_BASE_URL + '/admin/campuses/' + encodeURIComponent(state.editing.id);
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

        $(document).on('click', '[data-edit]', function () {
            var id = $(this).data('edit');
            var campus = state.campuses.filter(function (c) { return c.id === id; })[0];
            if (campus) openEditor(campus);
        });

        $(document).on('click', '[data-close]', closeEditor);
        $('#saveBtn').on('click', save);

        // Codes are stored uppercase; showing that as it is typed avoids a
        // "why did it change?" moment after saving.
        $('#fCode').on('input', function () {
            this.value = this.value.toUpperCase();
        });

        $('#editModal').on('click', function (e) {
            if (e.target === this) closeEditor();
        });
    }
});
