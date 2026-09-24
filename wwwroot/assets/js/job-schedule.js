/*
 * When the routine work runs.
 *
 * Until this screen existed there was no schedule at all. JobsController said an
 * external cron would call it; none was ever set up, so every sweep waited for
 * somebody to open Settings and press "Run now". Cases sat unassigned for as long
 * as nobody remembered.
 *
 *   GET /api/jobs/schedules             every job, its rule, and when it last ran
 *   PUT /api/jobs/schedules/{jobName}   change one rule
 *
 * Each row saves on its own, guarded by rowVersion. Two administrators editing the
 * schedule at once get a warning rather than one silently overwriting the other.
 */
$(function () {
    'use strict';

    var esc = AdminShell.escapeHtml;

    // ISO numbering, 1 = Monday ... 7 = Sunday. The same numbering
    // assignment.week_starts_on and job_schedule.day_of_week use — NOT JavaScript's
    // getDay(), which counts from Sunday = 0 and would shift every label by one.
    var DAYS = [
        { value: 1, label: 'Monday' },
        { value: 2, label: 'Tuesday' },
        { value: 3, label: 'Wednesday' },
        { value: 4, label: 'Thursday' },
        { value: 5, label: 'Friday' },
        { value: 6, label: 'Saturday' },
        { value: 7, label: 'Sunday' }
    ];

    // Offered rather than free text: a mistyped zone is rejected by the server, but
    // only after somebody has wondered why their schedule would not save. The row's
    // own zone is added below if it is not one of these.
    var ZONES = ['Asia/Kolkata', 'UTC'];

    var rows = [];   // as loaded, keyed by index; the edit baseline

    AdminShell.boot({
        roles: ['ADMIN'],
        active: { href: '/pages/admin/job-schedule.html', area: 'Admin' }
    }).then(function (ok) {
        if (!ok) return;
        $('#pageBody').prop('hidden', false);
        load();
    });

    // ---------------------------------------------------------------- load

    function load() {
        $('#scheduleError').prop('hidden', true);

        $.ajax({ url: API_BASE_URL + '/jobs/schedules', method: 'GET' })
            .done(function (res) {
                rows = (res && res.data) || [];

                if (!rows.length) {
                    $('#scheduleBody').html('<p class="empty">No jobs are registered.</p>');
                    return;
                }

                render();
            })
            .fail(function (xhr) {
                $('#scheduleBody').html('<p class="empty">Could not load the schedule.</p>');

                $('#scheduleError')
                    .text(xhr.status === 403
                        ? 'You do not have permission to see the schedule.'
                        : 'Could not reach the server to load the schedule.')
                    .prop('hidden', false);
            });
    }

    // ---------------------------------------------------------------- render

    function render() {
        $('#scheduleBody').html(rows.map(rowHtml).join(''));
        rows.forEach(function (row, i) { syncRow(i); });
        warnAboutQueue();
    }

    function rowHtml(row, i) {
        var zones = ZONES.slice();
        if (zones.indexOf(row.timezone) === -1) zones.unshift(row.timezone);

        return '' +
        '<div class="sched-row' + (row.isEnabled ? '' : ' is-off') + '" data-row="' + i + '">' +
          '<div>' +
            '<div class="sched-title">' + esc(row.title || row.jobName) + '</div>' +
            '<div class="sched-desc">' + esc(row.description) + '</div>' +
          '</div>' +

          '<div class="switch-cell">' +
            '<label class="switch-cell">' +
              '<input type="checkbox" data-field="enabled"' + (row.isEnabled ? ' checked' : '') + '>' +
              '<span>' + (row.isEnabled ? 'On' : 'Off') + '</span>' +
            '</label>' +
          '</div>' +

          '<div class="sched-controls">' +
            '<div class="field">' +
              '<label class="label">How often</label>' +
              '<select class="select" data-field="cadence">' +
                option('WEEKLY', 'Weekly', row.cadence) +
                option('DAILY', 'Every day', row.cadence) +
                option('EVERY', 'Every few minutes', row.cadence) +
              '</select>' +
            '</div>' +

            '<div class="field" data-day-field>' +
              '<label class="label">Day</label>' +
              '<select class="select" data-field="day">' +
                DAYS.map(function (d) {
                    return option(String(d.value), d.label, String(row.dayOfWeek || ''));
                }).join('') +
              '</select>' +
            '</div>' +

            '<div class="field" data-time-field>' +
              '<label class="label">Time</label>' +
              '<input class="input" type="time" data-field="time" value="' + esc(row.timeOfDay) + '">' +
            '</div>' +

            '<div class="field" data-interval-field>' +
              '<label class="label">Minutes apart</label>' +
              '<input class="input" type="number" min="1" max="1440" data-field="interval" value="' +
                esc(row.intervalMinutes || 5) + '">' +
            '</div>' +

            '<div class="field">' +
              '<label class="label">Time zone</label>' +
              '<select class="select" data-field="zone">' +
                zones.map(function (z) { return option(z, z, row.timezone); }).join('') +
              '</select>' +
            '</div>' +

            '<div class="field">' +
              '<button type="button" class="btn btn-primary btn-sm" data-save disabled>Save</button>' +
            '</div>' +
          '</div>' +

          '<div class="sched-when" data-when></div>' +
        '</div>';
    }

    function option(value, label, selected) {
        return '<option value="' + esc(value) + '"' +
               (String(value) === String(selected) ? ' selected' : '') + '>' +
               esc(label) + '</option>';
    }

    /**
     * Brings one row's derived parts back in step with its controls.
     *
     * Covers the day field (meaningless on a daily job), the on/off wording, and the
     * "next run" line. Called after every change so the sentence under the controls
     * always describes what is actually in them, not what was last saved.
     */
    function syncRow(i) {
        var $row = $('[data-row="' + i + '"]');
        var row = rows[i];

        var enabled = $row.find('[data-field="enabled"]').is(':checked');
        var cadence = $row.find('[data-field="cadence"]').val();
        var weekly = cadence === 'WEEKLY';
        var interval = cadence === 'EVERY';

        $row.toggleClass('is-off', !enabled);
        $row.find('[data-field="enabled"]').next('span').text(enabled ? 'On' : 'Off');

        // Each cadence reads exactly one of these three, and the server clears the
        // others. Showing a field the rule does not read invites somebody to set it
        // and then wonder why nothing changed.
        $row.find('[data-day-field]').prop('hidden', !weekly);
        $row.find('[data-time-field]').prop('hidden', interval);
        $row.find('[data-interval-field]').prop('hidden', !interval);

        $row.find('[data-save]').prop('disabled', !isDirty(i));

        var when;

        if (!enabled) {
            when = 'Switched off. This job only runs when somebody runs it by hand.';
        } else if (isDirty(i)) {
            when = 'Not saved yet. ' + describe($row) + '.';
        } else {
            when = describe($row) + '. Next run <strong>' + esc(when0(row.nextDueAt)) + '</strong>.';
        }

        var last = row.lastRunAt
            ? ' Last scheduled run ' + esc(when0(row.lastRunAt)) +
              (row.lastStatus ? ' (' + esc(row.lastStatus.toLowerCase()) + ')' : '') + '.'
            : ' It has not run on a schedule yet.';

        $row.find('[data-when]').html(when + (enabled ? last : ''));
    }

    function describe($row) {
        var cadence = $row.find('[data-field="cadence"]').val();
        var time = $row.find('[data-field="time"]').val() || '';
        var zone = $row.find('[data-field="zone"]').val() || '';

        if (cadence === 'EVERY') {
            var mins = parseInt($row.find('[data-field="interval"]').val(), 10);

            // No zone in this sentence on purpose: an interval is the same length of
            // time everywhere, so naming one would imply a clock it does not use.
            return 'Every ' + (mins === 1 ? 'minute' : esc(String(mins)) + ' minutes');
        }

        if (cadence !== 'WEEKLY') return 'Every day at ' + esc(time) + ' ' + esc(zone);

        var day = DAYS.filter(function (d) {
            return String(d.value) === String($row.find('[data-field="day"]').val());
        })[0];

        return 'Every ' + esc(day ? day.label : 'week') + ' at ' + esc(time) + ' ' + esc(zone);
    }

    /**
     * A UTC instant as the reader's own local time.
     *
     * The API sends UTC and the schedule is written in the job's zone, which need not
     * be the reader's. Showing the browser's local rendering is the one thing that is
     * true for whoever is looking at the screen.
     */
    function when0(utc) {
        if (!utc) return 'not scheduled';

        var parsed = new Date(utc.charAt(utc.length - 1) === 'Z' ? utc : utc + 'Z');

        if (isNaN(parsed.getTime())) return 'unknown';

        return parsed.toLocaleString();
    }

    // ---------------------------------------------------------------- edit

    function current(i) {
        var $row = $('[data-row="' + i + '"]');
        var cadence = $row.find('[data-field="cadence"]').val();
        var weekly = cadence === 'WEEKLY';
        var interval = cadence === 'EVERY';

        return {
            isEnabled: $row.find('[data-field="enabled"]').is(':checked'),
            cadence: cadence,
            dayOfWeek: weekly ? parseInt($row.find('[data-field="day"]').val(), 10) : null,
            timeOfDay: $row.find('[data-field="time"]').val(),
            intervalMinutes: interval ? parseInt($row.find('[data-field="interval"]').val(), 10) : null,
            timezone: $row.find('[data-field="zone"]').val(),
            rowVersion: rows[i].rowVersion
        };
    }

    function isDirty(i) {
        var now = current(i);
        var was = rows[i];

        // dayOfWeek is compared only when weekly: a daily job carries whatever day the
        // select happens to show, and treating that as a change would leave the Save
        // button lit on a row nobody touched.
        return now.isEnabled !== was.isEnabled ||
               now.cadence !== was.cadence ||
               now.timezone !== was.timezone ||
               (now.cadence !== 'EVERY' && now.timeOfDay !== was.timeOfDay) ||
               (now.cadence === 'WEEKLY' && now.dayOfWeek !== was.dayOfWeek) ||
               (now.cadence === 'EVERY' && now.intervalMinutes !== was.intervalMinutes);
    }

    $('#scheduleBody').on('change input', '[data-field]', function () {
        syncRow($(this).closest('[data-row]').data('row'));
    });

    $('#scheduleBody').on('click', '[data-save]', function () {
        var i = $(this).closest('[data-row]').data('row');
        save(i, $(this));
    });

    function save(i, $btn) {
        var payload = current(i);

        $btn.prop('disabled', true).text('Saving…');

        $.ajax({
            url: API_BASE_URL + '/jobs/schedules/' + encodeURIComponent(rows[i].jobName),
            method: 'PUT',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function (res) {
                $btn.text('Save');

                // responseType 0 == Success in Utilities/ApiResponse.cs. A warning is a
                // real answer here — a stale rowVersion, a day missing from a weekly
                // rule — so it is shown rather than treated as a failure.
                if (!res || res.responseType !== 0 || !res.data) {
                    $btn.prop('disabled', false);
                    showToast((res && res.message) || 'That schedule was not saved.', 'warning');
                    return;
                }

                rows[i] = res.data;
                $('[data-row="' + i + '"]').replaceWith(rowHtml(res.data, i));
                syncRow(i);
                warnAboutQueue();

                showToast(res.message || 'Schedule saved.', 'success');
            })
            .fail(function (xhr) {
                $btn.prop('disabled', false).text('Save');

                showToast(xhr.status === 403
                    ? 'You do not have permission to change the schedule.'
                    : 'Could not save that schedule.', 'error');
            });
    }

    // ---------------------------------------------------------------- queue warning

    /**
     * Catches the one combination that fails silently.
     *
     * Most of these jobs do not message anybody directly — they put an alert on the
     * notification queue, and "Send notifications" is what drains it. Scheduling the
     * producers without the sender fills that queue and delivers nothing, and every
     * screen still reports that somebody was told.
     */
    function warnAboutQueue() {
        var sender = rows.filter(function (r) { return r.jobName === 'send-notifications'; })[0];

        var producers = rows.filter(function (r) {
            return r.isEnabled && r.queuesNotifications;
        });

        if (!producers.length || (sender && sender.isEnabled)) {
            $('#queueWarning').prop('hidden', true);
            return;
        }

        $('#queueWarning')
            .text('"Send notifications" is switched off, so the alerts these jobs raise will ' +
                  'queue up and reach nobody. Switch it on, and schedule it after them: ' +
                  producers.map(function (r) { return r.title; }).join(', ') + '.')
            .prop('hidden', false);
    }
});
