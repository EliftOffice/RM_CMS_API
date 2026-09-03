/**
 * Pastor dashboard — oversight across every team in scope.
 *
 * One call fills the page, exactly like the team lead dashboard: the server
 * already knows who is asking and what campus (if any) their PASTOR grant
 * names, so nothing here ever sends a team, campus or pastor id of its own.
 */
$(function () {

    loadDashboard();

    function loadDashboard() {
        $.ajax({
            url: API_BASE_URL + '/dashboards/pastor',
            method: 'GET',
            success: function (res) {
                if (!res || !res.data) {
                    $('#alerts').html('<div class="alert alert-warning">No data</div>');
                    return;
                }
                renderDashboard(res.data);
            },
            error: function (xhr) {
                // 403 here means signed in but neither a pastor nor an administrator,
                // which reads differently from the server simply being unreachable.
                var message = (xhr && xhr.status === 403)
                    ? 'You do not have pastor access.'
                    : 'Error loading the dashboard.';

                $('#alerts').html('<div class="alert alert-danger">' + message + '</div>');
            }
        });
    }

    function renderDashboard(data) {
        var esc = data.escalations || {};
        var cases = data.cases || {};
        var contacts = data.contacts || {};
        var nurture = data.nurture || {};
        var teams = data.teams || [];
        var huddle = data.huddleCompliance || [];
        var atRisk = data.atRiskVolunteers || [];

        var subtitle = data.scopeLabel === 'your campus' && data.campusName
            ? 'Your campus — ' + data.campusName
            : (data.scopeLabel || '');

        $('#dashboardSubtitle').text(subtitle);
        if (window.PastorShell) PastorShell.setSubtitle(subtitle);

        // ── Overview ─────────────────────────────────────────────────────────
        $('#overviewGrid').html(
            '<div>Awaiting Assignment: <strong>' + (cases.awaitingAssignment || 0) + '</strong></div>' +
            '<div>In Progress: <strong>' + (cases.inProgress || 0) + '</strong></div>' +
            '<div>Escalated: <strong>' + (cases.escalated || 0) + '</strong></div>' +
            '<div>Awaiting Review: <strong>' + (cases.awaitingReview || 0) + '</strong></div>' +
            '<div>Contacts Due Today: <strong>' + (contacts.dueToday || 0) + '</strong></div>' +
            '<div>Contacts Overdue: <strong>' + (contacts.overdue || 0) + '</strong></div>' +
            '<div>Missed (7 days): <strong>' + (contacts.missedLast7Days || 0) + '</strong></div>' +
            '<div>Nurture Active: <strong>' + (nurture.active || 0) + '</strong></div>' +
            '<div>Nurture Overdue: <strong>' + (nurture.overdue || 0) + '</strong></div>' +
            '<div>Nurture Paused: <strong>' + (nurture.paused || 0) + '</strong></div>'
        );

        // ── Teams — worst first (already sorted by the server) ─────────────────
        var teamRows = teams.map(function (t) {
            return '<tr>' +
                '<td>' + escapeHtml(t.teamName) + '</td>' +
                '<td>' + escapeHtml(t.leadName || '—') + '</td>' +
                '<td>' + t.memberCount + '</td>' +
                '<td>' + badgeIfPositive(t.unacknowledgedEscalations, 'bg-danger') + '</td>' +
                '<td>' + (t.openEscalations || 0) + '</td>' +
                '<td>' + (t.overdueContacts || 0) + '</td>' +
                '<td>' + (t.awaitingReviewCases || 0) + '</td>' +
                '<td>' + badgeIfPositive(t.atRiskVolunteers, 'bg-warning text-dark') + '</td>' +
            '</tr>';
        }).join('');

        $('#teamTable tbody').html(
            teamRows || '<tr><td colspan="8" class="text-muted">No teams in scope.</td></tr>');

        // ── Huddle compliance ────────────────────────────────────────────────
        var huddleRows = huddle.map(function (h) {
            return '<tr>' +
                '<td>' + escapeHtml(h.teamName) + '</td>' +
                '<td>' + escapeHtml(h.leadName || '—') + '</td>' +
                '<td>' + (h.assessedThisWindow || 0) + '</td>' +
                '<td>' + badgeIfPositive(h.waitingThisWindow, 'bg-warning text-dark') + '</td>' +
                '<td>' + badgeIfPositive(h.olderBacklog, 'bg-danger') + '</td>' +
            '</tr>';
        }).join('');

        $('#huddleTable tbody').html(
            huddleRows || '<tr><td colspan="5" class="text-muted">No teams in scope.</td></tr>');

        // ── Attention Needed ─────────────────────────────────────────────────
        var attention = [];

        if (esc.unacknowledged > 0) {
            attention.push({
                who: 'Escalations',
                message: esc.unacknowledged + ' reached you and still need acknowledging' +
                    (esc.oldestUnacknowledgedHours
                        ? ' (oldest ' + Math.round(esc.oldestUnacknowledgedHours) + 'h)'
                        : ''),
                priority: 'High'
            });
        }

        if (atRisk.length > 0) {
            attention.push({
                who: 'Volunteers',
                message: atRisk.length + ' at risk across your teams',
                priority: 'Medium'
            });
        }

        if (cases.awaitingReview > 0) {
            attention.push({
                who: 'Review',
                message: cases.awaitingReview + ' case(s) awaiting review',
                priority: 'Medium'
            });
        }

        if (contacts.overdue > 0) {
            attention.push({
                who: 'Follow-ups',
                message: contacts.overdue + ' overdue',
                priority: 'Medium'
            });
        }

        $('#attentionList').html(attention.length
            ? attention.map(function (a) {
                return '<li class="list-group-item">' + a.who + ' - ' + a.message +
                       '<span class="badge bg-secondary ms-2">' + a.priority + '</span></li>';
              }).join('')
            : '<li class="list-group-item text-muted">' +
              (teams.length ? 'Nothing needs your attention right now.' : 'There are no teams in your scope yet.') +
              '</li>');

        // ── Escalations that reached pastor level ───────────────────────────
        var urgent = esc.urgent || [];

        var escalationsHtml = urgent.map(function (e) {
            var icon = (e.tier === 'EMERGENCY' || e.requiresProtocol)
                ? '<div class="blink-siren">🚨</div>'
                : '⚠️';

            var waited = e.waitingHours >= 24
                ? Math.round(e.waitingHours / 24) + 'd'
                : Math.round(e.waitingHours) + 'h';

            return '<div class="escalation-item" data-id="' + e.publicId + '">' +
                icon + ' ' + escapeHtml(e.personName || 'Unknown') +
                ' <span class="text-muted">(' + waited + ')</span>' +
                '</div>';
        }).join('');

        $('#pastorEscalations').html(
            escalationsHtml || '<p class="text-muted mb-0">No escalations are waiting on you.</p>');

        // ── At-risk volunteers ───────────────────────────────────────────────
        var atRiskHtml = atRisk.map(function (v) {
            return '<div class="at-risk-item">' +
                '<span class="at-risk-name">' + escapeHtml(v.name) +
                    (v.teamName ? ' <span class="text-muted">· ' + escapeHtml(v.teamName) + '</span>' : '') +
                '</span>' +
                '<span class="at-risk-meta">' + v.currentCaseLoad + ' / ' + v.capacityMaxPerWeek + '</span>' +
            '</div>';
        }).join('');

        $('#atRiskList').html(
            atRiskHtml || '<p class="text-muted mb-0">No volunteers are flagged at risk.</p>');

        $('#alerts').empty();
    }

    function badgeIfPositive(count, cssClass) {
        count = count || 0;
        return count > 0
            ? '<span class="badge ' + cssClass + '">' + count + '</span>'
            : String(count);
    }

    /** Names come from user data and land in HTML, so they are escaped. */
    function escapeHtml(value) {
        return String(value === null || value === undefined ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /**
     * Real navigation, not an iframe or modal — the application sends
     * X-Frame-Options: DENY and CSP frame-ancestors 'none', so a framed detail
     * screen would just show "refused to connect". See teamlead.js for the
     * same call on the equivalent screen.
     */
    $(document).on('click', '.escalation-item', function () {
        var id = $(this).data('id');
        window.location.href = '../TeamLeads/Escalations.html?id=' + encodeURIComponent(id);
    });
});
