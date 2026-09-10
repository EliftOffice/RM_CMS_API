/*
 * Telegram message templates.
 *
 *   GET    /api/admin/telegram-templates          every scenario, wording and placeholders
 *   PUT    /api/admin/telegram-templates/{code}   change the wording
 *   DELETE /api/admin/telegram-templates/{code}   back to the standard wording
 *
 * ADMIN only.
 *
 * The wording of every Telegram message used to be a string in the C# source, so
 * changing a word meant a deployment — and the people who know how these should
 * read are the pastors, not whoever can rebuild the application.
 *
 * The scenario list comes from the server, not from here. A message added in code
 * appears on this screen by itself, and there is nothing to keep in step.
 */
$(function () {
    'use strict';

    var esc = AdminShell.escapeHtml;

    var state = {
        items: [],
        selected: null,   // the code being edited
        original: ''      // its wording when it was opened, for "undo my edits"
    };

    AdminShell.boot({
        roles: ['ADMIN'],
        active: { href: '/pages/admin/telegram-messages.html', area: 'Telegram' }
    }).then(function (ok) {
        if (!ok) return;
        $('#pageBody').prop('hidden', false);
        bind();
        load();
    });

    // ------------------------------------------------------------------ data

    function load(keepSelection) {
        $.ajax({ url: API_BASE_URL + '/admin/telegram-templates', method: 'GET' })
            .done(function (res) {
                // A refusal arrives as HTTP 200 with responseType 1.
                if (!res || res.responseType !== 0) {
                    $('#templateList').html(listShell(
                        '<div class="empty" style="padding:18px;">' +
                        esc((res && res.message) || 'Could not load the messages.') + '</div>'));
                    return;
                }

                state.items = res.data || [];
                renderList();

                var wanted = keepSelection || state.selected ||
                             (state.items.length ? state.items[0].code : null);

                if (wanted) select(wanted);
            })
            .fail(function () {
                $('#templateList').html(listShell(
                    '<div class="empty" style="padding:18px;">Could not load the messages.</div>'));
            });
    }

    // ---------------------------------------------------------------- render

    function listShell(inner) {
        return '<div class="card-head"><span class="card-title">Messages</span></div>' + inner;
    }

    /**
     * Grouped by the server's own grouping, in the order it sent them — the
     * catalogue is ordered so that connecting comes before the alerts, which is the
     * order somebody reads them in when setting the system up.
     */
    function renderList() {
        if (!state.items.length) {
            $('#templateList').html(listShell(
                '<div class="empty" style="padding:18px;">No messages are defined.</div>'));
            return;
        }

        var html = '';
        var group = null;

        state.items.forEach(function (t) {
            if (t.group !== group) {
                group = t.group;
                html += '<div class="tpl-group-title">' + esc(group) + '</div>';
            }

            html += '<button type="button" class="tpl-item' +
                        (t.code === state.selected ? ' is-active' : '') +
                        '" data-code="' + esc(t.code) + '">' +
                        '<div class="tpl-item-name">' + esc(t.label) +
                            (t.isCustomised ? ' <span class="badge badge-warn">edited</span>' : '') +
                        '</div>' +
                        '<div class="tpl-item-sub">' + esc(t.description) + '</div>' +
                    '</button>';
        });

        $('#templateList').html(listShell(html));
    }

    function find(code) {
        return state.items.filter(function (t) { return t.code === code; })[0] || null;
    }

    function select(code) {
        var t = find(code);
        if (!t) return;

        state.selected = code;
        state.original = t.body;

        renderList();

        $('#editorCard').prop('hidden', false);
        $('#editorTitle').text(t.label);
        $('#editorDescription').text(t.description);
        $('#customBadge').prop('hidden', !t.isCustomised);
        $('#resetBtn').prop('hidden', !t.isCustomised);
        $('#body').val(t.body);
        $('#editorNotice').prop('hidden', true).text('');

        $('#editorMeta').text(t.isCustomised && t.updatedAt
            ? 'Edited ' + new Date(t.updatedAt).toLocaleString() +
              (t.updatedByName ? ' by ' + t.updatedByName : '')
            : 'Standard wording');

        renderChips(t);
        renderHelp(t);
        renderPreview();
    }

    function renderChips(t) {
        $('#chips').html((t.placeholders || []).map(function (p) {
            return '<button type="button" class="chip" data-token="' + esc(p.token) + '"' +
                   ' title="' + esc(p.description) + '">' + esc(p.token) + '</button>';
        }).join(''));
    }

    function renderHelp(t) {
        $('#placeholderHelp').html((t.placeholders || []).map(function (p) {
            return '<dt>' + esc(p.token) + '</dt><dd>' + esc(p.description) + '</dd>';
        }).join(''));
    }

    /**
     * The preview.
     *
     * Rendered with .text(), never .html(). The body is Telegram markup and may
     * legitimately contain tags — but this page is not Telegram, and putting an
     * administrator's draft through innerHTML would execute whatever it contained on
     * an authenticated admin page. Showing the tags as written is also more useful:
     * the editor sees exactly what will be sent.
     *
     * Substituted here rather than round-tripping to the server on every keystroke.
     * The server sends the sample values, so the two agree, and the saved copy always
     * gets a real server-rendered preview back.
     */
    function renderPreview() {
        var t = find(state.selected);
        if (!t) return;

        var body = $('#body').val() || '';

        (t.placeholders || []).forEach(function (p) {
            var token = p.token.replace(/^\{\{|\}\}$/g, '').trim();
            // Global, because a placeholder can appear more than once.
            body = body.replace(new RegExp('\\{\\{\\s*' + token + '\\s*\\}\\}', 'g'), p.sample || '');
        });

        // Anything left is a placeholder this message does not have. Left visible on
        // purpose so the editor sees the mistake before the server refuses the save.
        $('#preview').text(body.replace(/\n{3,}/g, '\n\n').trim());
    }

    // ----------------------------------------------------------------- edits

    function insertToken(token) {
        var el = document.getElementById('body');
        var start = el.selectionStart;
        var end = el.selectionEnd;
        var value = el.value;

        el.value = value.slice(0, start) + token + value.slice(end);

        // Cursor after what was just inserted, so several can be added in a row.
        el.selectionStart = el.selectionEnd = start + token.length;
        el.focus();

        renderPreview();
    }

    function save() {
        var t = find(state.selected);
        if (!t) return;

        var body = $.trim($('#body').val());

        if (!body) return notice('The message cannot be empty.');

        var $btn = $('#saveBtn').prop('disabled', true).text('Saving…');

        $.ajax({
            url: API_BASE_URL + '/admin/telegram-templates/' + encodeURIComponent(t.code),
            method: 'PUT',
            contentType: 'application/json',
            data: JSON.stringify({ Body: body })
        })
            .done(function (res) {
                // The server refuses an unknown placeholder, which is the mistake this
                // screen exists to catch — a typed {{Name}} would otherwise arrive on
                // somebody's phone as a gap in the sentence.
                if (!res || res.responseType !== 0) { notice((res && res.message) || 'That could not be saved.'); return; }

                showToast(res.message || 'Saved.', 'success');
                load(t.code);
            })
            .fail(function (xhr) {
                var b = xhr && xhr.responseJSON;
                notice((b && (b.message || b.detail || b.title)) || 'That could not be saved.');
            })
            .always(function () { $btn.prop('disabled', false).text('Save'); });
    }

    function reset() {
        var t = find(state.selected);
        if (!t) return;

        if (!window.confirm(
                'Put "' + t.label + '" back to the standard wording?\n\n' +
                'Your version will be discarded.')) return;

        $.ajax({
            url: API_BASE_URL + '/admin/telegram-templates/' + encodeURIComponent(t.code),
            method: 'DELETE'
        })
            .done(function (res) {
                if (!res || res.responseType !== 0) { notice((res && res.message) || 'That could not be reset.'); return; }

                showToast(res.message || 'Reset.', 'success');
                load(t.code);
            })
            .fail(function () { notice('That could not be reset.'); });
    }

    function notice(text) {
        $('#editorNotice').prop('hidden', false).text(text);
    }

    // --------------------------------------------------------------- wiring

    function bind() {
        $(document).on('click', '.tpl-item', function () {
            select($(this).data('code'));
        });

        $(document).on('click', '.chip', function () {
            insertToken($(this).data('token'));
        });

        $('#body').on('input', function () {
            $('#editorNotice').prop('hidden', true);
            renderPreview();
        });

        $('#saveBtn').on('click', save);
        $('#resetBtn').on('click', reset);

        // Undoes typing that has not been saved. Distinct from Reset, which throws
        // away the SAVED version and goes back to what the application ships with.
        $('#revertBtn').on('click', function () {
            $('#body').val(state.original);
            $('#editorNotice').prop('hidden', true);
            renderPreview();
        });
    }
});
