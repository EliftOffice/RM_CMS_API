/*
 * Settings and scheduled work.
 *
 * Rewritten onto the new API. The previous version called:
 *   /api/systemconfig                      -> system_config table, dropped
 *   /api/cornjobs/*                        -> legacy jobs, replaced by /api/jobs/*
 *   /api/notifications/broadcast/volunteers -> never rebuilt
 * All three returned errors against the current schema, which is why the screen
 * appeared broken rather than merely empty.
 *
 * The broadcast panel is deliberately NOT reinstated. Notifications are queued but
 * nothing sends them yet, so a "Send to all volunteers" button would report success
 * while reaching nobody — worse than not offering it.
 *
 *   GET /api/admin/settings   grouped rules with type and bounds
 *   PUT /api/admin/settings   batch save, all-or-nothing validation
 *   POST /api/jobs/{name}     run a sweep now
 */
$(function () {
    'use strict';

    var JOBS = [
        { route: 'assign-unassigned',  title: 'Assign new people',
          blurb: 'Gives unassigned cases to the least-loaded volunteer with capacity.' },
        { route: 'advance-nurture',    title: 'Advance nurture',
          blurb: 'Creates the next contact for anyone whose step has fallen due.' },
        { route: 'mark-overdue',       title: 'Mark overdue contacts',
          blurb: 'Marks planned contacts that are past their date as missed.' },
        { route: 'chase-escalations',  title: 'Chase escalations',
          blurb: 'Reminds the team lead about unacknowledged concerns, then the pastor.' }
    ];

    var esc = AdminShell.escapeHtml;
    var original = {};   // key -> value as loaded
    var meta = {};       // key -> setting definition

    AdminShell.boot({
        roles: ['ADMIN'],
        active: { href: '/templates/Admin/siteadmin.html', area: 'Admin' }
    }).then(function (ok) {
        if (!ok) return;
        $('#pageBody').prop('hidden', false);
        renderJobs();
        loadSettings();
        $('#saveBtn').on('click', save);
        $('#resetBtn').on('click', function () { loadSettings(); });
    });

    // ---------------------------------------------------------------- jobs

    function renderJobs() {
        $('#jobGrid').html(JOBS.map(function (j) {
            return '<div class="job-card">' +
                     '<h3>' + esc(j.title) + '</h3>' +
                     '<p>' + esc(j.blurb) + '</p>' +
                     '<button type="button" class="btn btn-sm" data-job="' + esc(j.route) + '">Run now</button>' +
                     '<div class="job-result" data-result="' + esc(j.route) + '"></div>' +
                   '</div>';
        }).join(''));

        $('#jobGrid').on('click', 'button[data-job]', function () {
            var route = $(this).data('job');
            var $btn = $(this);
            var $out = $('[data-result="' + route + '"]');

            $btn.prop('disabled', true);
            $out.text('Running…');

            $.ajax({ url: API_BASE_URL + '/jobs/' + route, method: 'POST' })
                .done(function (res) {
                    $btn.prop('disabled', false);

                    var report = res && res.data;

                    if (!report) {
                        $out.text((res && res.message) || 'No result.');
                        showToast((res && res.message) || 'The job did not run.', 'warning');
                        return;
                    }

                    // Notes carry the explanation — "nothing waiting", "no volunteer
                    // with spare capacity at campus X" — which is usually the answer
                    // the administrator actually wanted.
                    var summary = report.processed + ' processed, ' +
                                  report.skipped + ' skipped, ' +
                                  report.failed + ' failed';

                    $out.html(esc(summary) +
                        (report.notes && report.notes.length
                            ? '<div>' + esc(report.notes.join(' · ')) + '</div>'
                            : ''));

                    showToast(summary, report.failed > 0 ? 'warning' : 'success');
                })
                .fail(function (xhr) {
                    $btn.prop('disabled', false);
                    $out.text('Could not run.');
                    showToast(xhr.status === 403
                        ? 'You do not have permission to run jobs.'
                        : 'Could not run that job.', 'error');
                });
        });
    }

    // ---------------------------------------------------------------- settings

    function loadSettings() {
        $('#settingsError').prop('hidden', true);

        $.ajax({ url: API_BASE_URL + '/admin/settings', method: 'GET' })
            .done(function (res) {
                var groups = (res && res.data) || [];

                if (!groups.length) {
                    $('#settingsBody').html('<p class="empty">No settings are configured.</p>');
                    return;
                }

                original = {};
                meta = {};

                $('#settingsBody').html(groups.map(function (g) {
                    return '<h3 class="card-title" style="margin:18px 0 4px;">' + esc(g.label) + '</h3>' +
                           g.settings.map(renderSetting).join('');
                }).join(''));

                groups.forEach(function (g) {
                    g.settings.forEach(function (s) {
                        original[s.key] = s.value;
                        meta[s.key] = s;
                    });
                });

                wireInputs();
                refreshDirty();
            })
            .fail(function (xhr) {
                $('#settingsBody').html('<p class="empty">Could not load settings.</p>');
                fail(xhr.status === 403
                    ? 'You do not have permission to view settings.'
                    : 'Could not load settings.');
            });
    }

    function renderSetting(s) {
        var bounds = '';

        if (s.minValue !== null && s.minValue !== undefined &&
            s.maxValue !== null && s.maxValue !== undefined) {
            bounds = 'between ' + s.minValue + ' and ' + s.maxValue;
        }

        var control;

        if (s.valueType === 'BOOLEAN') {
            control = '<select class="input setting-input" data-key="' + esc(s.key) + '"' +
                        (s.isEditable ? '' : ' disabled') + '>' +
                        '<option value="true"' + (s.value === 'true' ? ' selected' : '') + '>Yes</option>' +
                        '<option value="false"' + (s.value === 'false' ? ' selected' : '') + '>No</option>' +
                      '</select>';
        } else {
            control = '<input class="input setting-input" data-key="' + esc(s.key) + '"' +
                        ' type="' + (s.valueType === 'INTEGER' || s.valueType === 'DECIMAL' ? 'number' : 'text') + '"' +
                        (s.minValue !== null && s.minValue !== undefined ? ' min="' + s.minValue + '"' : '') +
                        (s.maxValue !== null && s.maxValue !== undefined ? ' max="' + s.maxValue + '"' : '') +
                        ' value="' + esc(s.value) + '"' +
                        (s.isEditable ? '' : ' disabled') + '>';
        }

        return '<div class="setting-row">' +
                 '<div>' +
                   '<div class="setting-label">' + esc(s.label) + '</div>' +
                   (s.description ? '<div class="setting-desc">' + esc(s.description) + '</div>' : '') +
                   '<div class="setting-key">' + esc(s.key) + '</div>' +
                   (bounds ? '<div class="setting-bounds">' + esc(bounds) + '</div>' : '') +
                   '<div class="setting-error" data-error="' + esc(s.key) + '"></div>' +
                 '</div>' +
                 '<div>' + control + '</div>' +
               '</div>';
    }

    function wireInputs() {
        $('.setting-input').on('input change', function () {
            var key = $(this).data('key');
            $(this).toggleClass('changed', String($(this).val()) !== original[key]);
            $(this).removeClass('invalid');
            $('[data-error="' + key + '"]').text('');
            refreshDirty();
        });
    }

    function dirtyKeys() {
        return $('.setting-input').filter(function () {
            return String($(this).val()) !== original[$(this).data('key')];
        }).map(function () { return $(this).data('key'); }).get();
    }

    function refreshDirty() {
        var count = dirtyKeys().length;

        $('#saveBtn').prop('disabled', count === 0);
        $('#resetBtn').prop('disabled', count === 0);
        $('#changeCount').text(count === 0 ? 'No changes' : count + ' unsaved change(s)');
    }

    function save() {
        var keys = dirtyKeys();
        if (!keys.length) return;

        var payload = keys.map(function (key) {
            return { Key: key, Value: String($('.setting-input[data-key="' + cssEscape(key) + '"]').val()) };
        });

        $('#saveBtn').prop('disabled', true).text('Saving…');
        $('#settingsError').prop('hidden', true);

        $.ajax({
            url: API_BASE_URL + '/admin/settings',
            method: 'PUT',
            contentType: 'application/json',
            data: JSON.stringify({ Settings: payload })
        })
            .done(function (res) {
                $('#saveBtn').text('Save changes');

                var data = res && res.data;

                // The server validates the batch as a whole, so a rejection means
                // NOTHING was saved — say so, and point at the offending fields.
                if (data && data.rejected && data.rejected.length) {
                    data.rejected.forEach(function (r) {
                        $('.setting-input[data-key="' + cssEscape(r.key) + '"]').addClass('invalid');
                        $('[data-error="' + cssEscape(r.key) + '"]').text(r.reason);
                    });

                    fail(res.message || 'Nothing was saved — some values are not valid.');
                    refreshDirty();
                    return;
                }

                showToast(res.message || 'Saved', 'success');
                loadSettings();
            })
            .fail(function (xhr) {
                $('#saveBtn').prop('disabled', false).text('Save changes');
                var body = xhr.responseJSON;
                fail((body && (body.message || body.title)) || 'Could not save settings.');
            });
    }

    function fail(message) {
        $('#settingsError').text(message).prop('hidden', false);
        showToast(message, 'warning');
    }

    /** Setting keys contain dots, which are class selectors inside an attribute filter. */
    function cssEscape(value) {
        return String(value).replace(/(["\\])/g, '\\$1');
    }
});
