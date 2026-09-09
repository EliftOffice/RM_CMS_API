// Escalation detail — the screen a team lead acts on.
//
// Rewired to the v2 API. The UI is unchanged; what moved is the route shapes, the
// vocabulary (the MVP sent display labels like "Referred Out"; v2 takes codes like
// REFERRED_OUT), and two things the MVP had no concept of:
//
//   1. ROW VERSION. Resolving is a concurrent write to a pastoral record. Two leads
//      with this screen open must not silently overwrite each other's outcome, so
//      the version read with the escalation is sent back and the server rejects a
//      stale one.
//   2. SAFEGUARDING. Reasons flagged requires_protocol (abuse disclosure, self-harm
//      risk) cannot be resolved without answering whether the protocol was followed.
//      The server refuses otherwise, so the questions appear on the form for exactly
//      those reasons rather than being asked of everyone.

$(document).ready(function () {

    var id = new URLSearchParams(window.location.search).get('id');
    var current = null;          // the loaded escalation, incl. its row version

    if (!id) {
        showMessage('No escalation was named.', 'error');
        return;
    }

    loadEscalation();

    // ------------------------------------------------------------------
    // Load
    // ------------------------------------------------------------------
    function loadEscalation() {
        $.get(API_BASE_URL + '/escalations/' + encodeURIComponent(id))
            .done(function (res) {
                if (!res || !res.data) {
                    showMessage(res && res.message ? res.message : 'Not found.', 'error');
                    return;
                }

                current = res.data;

                $('#escId').text(current.referenceCode || current.id);
                $('#personId').text(current.personName || '—');
                $('#reason').text(current.reasonLabel || current.reason);
                $('#status').text(prettyStatus(current.status));
                $('#desc').text(current.description || '—');
                $('#raisedBy').text(current.raisedByName || '—');

                $('#tier').attr('class', 'tl-badge tier-' + current.tier).text(current.tier);

                // How long it has waited, in the coarsest honest unit. Past a day
                // it is marked, because an escalation nobody picked up for two days
                // is the failure this screen exists to prevent.
                var hours = Math.round(current.hoursWaiting || 0);
                var waited = hours < 1 ? 'under an hour'
                           : hours < 48 ? hours + ' hours'
                           : Math.floor(hours / 24) + ' days';

                $('#waiting')
                    .toggleClass('waiting-long', hours >= 24 && !current.acknowledgedAt)
                    .text(waited + (current.acknowledgedAt ? ' before it was acknowledged' : ' so far'));

                if (window.TeamLeadShell) {
                    TeamLeadShell.setSubtitle(current.personName || '');
                }

                $('#escSummary').text(
                    (current.reasonLabel || current.reason) + ' — ' +
                    current.tier.toLowerCase() + ' tier');

                applyState();
            })
            .fail(function (xhr) { showMessage(getError(xhr), 'error'); });
    }

    /**
     * Shows only the actions that are actually available. An Acknowledge button on
     * an already-acknowledged escalation invites a click that can only fail.
     */
    function applyState() {
        var acknowledged = !!current.acknowledgedAt;
        var closed = ['RESOLVED', 'CLOSED', 'REFERRED_OUT'].indexOf(current.status) !== -1;

        $('#ackBtn').prop('disabled', acknowledged || closed)
                    .text(acknowledged ? 'Acknowledged' : 'Acknowledge');

        $('#ackHint').text(acknowledged
            ? 'Acknowledged. The case stays paused until this is resolved.'
            : 'Acknowledging tells the system somebody has this. The case stays paused until it is resolved.');

        $('#resolveDiv').toggle(!closed);

        // The safeguarding block is asked for only where the reason demands it.
        $('#safeguardingGroup').toggle(!!current.requiresProtocol);

        if (closed) {
            showMessage('This escalation is ' + current.status.toLowerCase().replace('_', ' ') + '.', 'success');
        }
    }

    // ------------------------------------------------------------------
    // Acknowledge
    // ------------------------------------------------------------------
    $('#ackBtn').click(function () {
        if (!current) return;

        $.post(API_BASE_URL + '/escalations/' + encodeURIComponent(id) + '/acknowledge')
            .done(function (res) {
                if (res && res.responseType !== 0) {
                    showMessage(res.message || 'That could not be saved.', 'error');
                    loadEscalation();
                    return;
                }

                showMessage(res.message || 'Acknowledged.', 'success');
                loadEscalation();          // refresh state and row version
            })
            .fail(function (xhr) { showMessage(getError(xhr), 'error'); });
    });

    // ------------------------------------------------------------------
    // Resolve
    // ------------------------------------------------------------------
    $('#resolveBtn').click(function () {
        if (!current) return;

        var status = $('#statusSelect').val();
        var outcome = $('#outcomeSelect').val();

        if (!status || !outcome) {
            showMessage('Status and outcome are both required.', 'error');
            return;
        }

        var payload = {
            status: status,
            outcomeCode: outcome,
            resolutionNotes: $('#notes').val() || null,
            resourceConnected: $('#resource').val() || null,
            // resumeInDays is left unset so the case picks its follow-up back up on
            // the nurture plan's own schedule. The old form had a "Follow-up
            // scheduled" checkbox here with nothing behind it — v2 decides the
            // resume date from the plan, and a control that changes nothing is
            // worse than no control.
            rowVersion: current.rowVersion
        };

        if (current.requiresProtocol) {
            payload.protocolFollowed = $('#protocolFollowed').is(':checked');
            payload.authoritiesContacted = $('#authoritiesContacted').is(':checked');
            payload.volunteerDebriefed = $('#volunteerDebriefed').is(':checked');

            if (!payload.protocolFollowed) {
                showMessage(
                    'This reason requires the safeguarding protocol. Confirm it was followed, ' +
                    'or record what happened in the notes and raise it with a pastor.', 'error');
                return;
            }
        }

        $.ajax({
            url: API_BASE_URL + '/escalations/' + encodeURIComponent(id) + '/resolve',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
        .done(function (res) {
            // A refusal arrives as HTTP 200 with responseType 1 (Warning) — the
            // ApiResponse envelope, not an HTTP error. Treating every 200 as success
            // would report "somebody else changed this" as if the resolve had worked,
            // which is the one outcome that must never be misreported on a pastoral
            // record. 0 == Success, per Utilities/ApiResponse.cs.
            if (res && res.responseType !== 0) {
                showMessage(res.message || 'That could not be saved.', 'error');
                loadEscalation();       // pick up whatever the other person wrote
                return;
            }

            showMessage(res.message || 'Resolved.', 'success');
            loadEscalation();
        })
        .fail(function (xhr) { showMessage(getError(xhr), 'error'); });
    });

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    function showMessage(msg, type) {
        $('#message')
            .removeClass('is-error is-ok')
            .addClass(type === 'success' ? 'is-ok' : 'is-error')
            .text(msg);
    }

    function prettyStatus(code) {
        if (!code) return '—';
        return code.charAt(0) + code.slice(1).toLowerCase().replace(/_/g, ' ');
    }

    function getError(xhr) {
        if (!xhr) return 'Something went wrong.';

        var body = xhr.responseJSON;
        if (!body) return 'Something went wrong.';

        // ProblemDetails uses `detail`/`title`; ApiResponse uses `message`.
        return body.message || body.detail || body.title || 'Something went wrong.';
    }
});
