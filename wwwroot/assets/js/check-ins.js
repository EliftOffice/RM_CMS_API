// Volunteer check-in — the form a team lead fills in after the conversation.
//
// Rewired to the v2 API (`Modules/CheckIns`). Four things were wrong before, and
// the first meant the screen could never work at all:
//
//   1. The tone dropdown sent EMOJI ('😊', '😴'). The API stores GREEN/AMBER/RED
//      and rejected everything, so no check-in could be saved from this form.
//   2. The check-in date input was commented out, and the next-check-in date and
//      action items were inside display:none wrappers — three fields the form
//      collected nowhere.
//   3. Meeting type was disabled and offered "Bi-Weekly" and "Emergency", which
//      are not in the schema's vocabulary.
//   4. A hidden teamLeadId was posted, so the record of WHO held a pastoral
//      conversation was whatever the browser said. It comes from the token now.

var CHECKIN_VOLUNTEER_ID = null;

function setMessage(text, kind) {
    $('#message').removeClass('is-error is-ok').addClass(kind || '').text(text || '');
}

$(function () {

    var params = new URLSearchParams(window.location.search);
    CHECKIN_VOLUNTEER_ID = params.get('id') || params.get('volunteerid');

    $('#check_in_date').val(new Date().toISOString().split('T')[0]);

    $('#capBandWrap').toggle($('#capacity_adjustment').is(':checked'));

    $('#capacity_adjustment').on('change', function () {
        $('#capBandWrap').toggle(this.checked);
        if (!this.checked) $('#new_capacity_band').val('');
    });

    if (!CHECKIN_VOLUNTEER_ID) {
        setMessage('No volunteer was named. Open a check-in from the dashboard.', 'is-error');
        $('#saveBtn').prop('disabled', true);
        return;
    }

    loadVolunteer();
    loadCapacityBands();
    loadHistory();

    // ------------------------------------------------------------------
    // Who this is about
    // ------------------------------------------------------------------
    function loadVolunteer() {
        $.get(API_BASE_URL + '/volunteers/' + encodeURIComponent(CHECKIN_VOLUNTEER_ID))
            .done(function (res) {
                var v = res && res.data;

                if (!v) { setMessage('That volunteer was not found.', 'is-error'); return; }

                $('#volunteer_id').val(v.id);
                $('#volunteer_name').text(v.fullName || 'Volunteer');
                $('#volunteerTeam').text(v.teamName || '');

                if (window.TeamLeadShell) {
                    TeamLeadShell.setSubtitle(v.fullName || '');
                }

                if (v.capacityBandCode) $('#new_capacity_band').val(v.capacityBandCode);
            })
            .fail(function (xhr) { setMessage(getError(xhr), 'is-error'); });
    }

    function loadCapacityBands() {
        // The bands come from the shared volunteer reference payload; there is no
        // dedicated capacity-bands route on the v2 API.
        $.get(API_BASE_URL + '/volunteers/reference')
            .done(function (res) {
                var bands = (res && res.data && res.data.capacityBands) || [];
                var sel = $('#new_capacity_band').empty();

                sel.append('<option value="">— Select —</option>');

                bands.forEach(function (b) {
                    sel.append('<option value="' + escapeHtml(b.code) + '">' +
                        escapeHtml(b.label || b.code) +
                        ' (' + b.minPerWeek + '–' + b.maxPerWeek + ' a week)</option>');
                });
            })
            .fail(function () {
                $('#new_capacity_band').html('<option value="">Bands unavailable</option>');
            });
    }

    /** What was agreed last time, so the lead is not starting cold. */
    function loadHistory() {
        $.get(API_BASE_URL + '/check-ins/volunteer/' + encodeURIComponent(CHECKIN_VOLUNTEER_ID) + '?limit=5')
            .done(function (res) {
                var rows = (res && res.data) || [];

                if (!rows.length) {
                    $('#lastCheckIn').text('No check-in recorded for them yet.');
                    return;
                }

                var last = rows[0];

                $('#lastCheckIn').text(
                    'Last check-in ' + formatDate(last.heldOn) +
                    ' — tone ' + (last.emotionalTone || 'not recorded') +
                    (last.followUpRequired ? ', follow-up was flagged.' : '.'));

                $('#historyTable tbody').html(rows.map(function (r) {
                    return '<tr>' +
                        '<td>' + formatDate(r.heldOn) + '</td>' +
                        '<td>' + escapeHtml(prettyType(r.meetingType)) + '</td>' +
                        '<td>' + toneBadge(r.emotionalTone) + '</td>' +
                        '<td>' + escapeHtml(r.concerns || r.actionItems || '—') + '</td>' +
                    '</tr>';
                }).join(''));

                $('#historyCard').show();
            });
    }

    // ------------------------------------------------------------------
    // Save
    // ------------------------------------------------------------------
    $('#saveBtn').click(function () {
        setMessage('');

        var tone = $('#emotional_tone').val();

        if (!tone) {
            toast('Choose an emotional tone before saving.', 'warning');
            $('#emotional_tone').focus();
            return;
        }

        var payload = {
            volunteerId: $('#volunteer_id').val() || CHECKIN_VOLUNTEER_ID,
            heldOn: $('#check_in_date').val() || null,
            durationMinutes: parseInt($('#duration_min').val(), 10) || null,
            meetingType: $('#meeting_type').val() || 'MONTHLY',
            emotionalTone: tone,
            concerns: $('#concerns_noted').val() || null,
            trainingNeeds: $('#training_needs').val() || null,
            actionItems: $('#action_items').val() || null,
            // The band change below is itself a capacity review, so the flag is
            // set from the thing that happened rather than from a checkbox that
            // asked the lead to remember to tick it.
            capacityReviewed: $('#capacity_adjustment').is(':checked'),
            boundaryIssuesRaised: $('#boundary_issues').is(':checked'),
            followUpRequired: $('#follow_up_needed').is(':checked'),
            nextCheckInOn: $('#next_check_in_date').val() || null,
            newCapacityBandCode: $('#capacity_adjustment').is(':checked')
                ? ($('#new_capacity_band').val() || null)
                : null
        };

        $('#saveBtn').prop('disabled', true).text('Saving…');

        $.ajax({
            url: API_BASE_URL + '/check-ins',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
        .done(function (res) {
            // A refusal is an HTTP 200 carrying responseType 1, so a handler that
            // assumes 200 == saved would tell a team lead the conversation is on
            // file when it is not.
            if (res && res.responseType !== 0) {
                toast(res.message || 'That could not be saved.', 'warning');
                setMessage(res.message || '', 'is-error');
                return;
            }

            toast(res.message || 'Check-in recorded.', 'success');
            setMessage(res.message || 'Check-in recorded.', 'is-ok');
            resetForm();
            loadHistory();
        })
        .fail(function (xhr) {
            toast(getError(xhr), 'error');
            setMessage(getError(xhr), 'is-error');
        })
        .always(function () {
            $('#saveBtn').prop('disabled', false).text('Save check-in');
        });
    });

    function resetForm() {
        $('#duration_min, #concerns_noted, #training_needs, #action_items').val('');
        $('#emotional_tone').val('');
        $('#capacity_adjustment, #follow_up_needed, #boundary_issues').prop('checked', false);
        $('#capBandWrap').hide();
        $('#next_check_in_date').val('');
        $('#check_in_date').val(new Date().toISOString().split('T')[0]);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    function toneBadge(tone) {
        if (tone === 'GREEN') return '<span class="tl-badge tl-badge-green">Green</span>';
        if (tone === 'AMBER') return '<span class="tl-badge tl-badge-amber">Amber</span>';
        if (tone === 'RED') return '<span class="tl-badge tl-badge-red">Red</span>';

        return '<span class="tl-badge tl-badge-grey">—</span>';
    }

    function prettyType(code) {
        if (!code) return '—';
        return code.charAt(0) + code.slice(1).toLowerCase().replace(/_/g, '-');
    }

    function formatDate(value) {
        if (!value) return '—';
        var d = new Date(value);
        return isNaN(d) ? '—' : d.toLocaleDateString();
    }

    function toast(message, kind) {
        if (typeof showToast === 'function') showToast(message, kind);
    }

    function escapeHtml(value) {
        return String(value === null || value === undefined ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function getError(xhr) {
        var b = xhr && xhr.responseJSON;
        return (b && (b.message || b.detail || b.title)) || 'Something went wrong.';
    }
});
