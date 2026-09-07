// The team this lead runs, filled in from the API response. It used to be read
// from ?teamleadid= in the URL and trusted, which meant editing the address bar
// showed you somebody else's team — including the escalations on it, which name
// real people and their crises. The server now derives it from the signed-in
// account and ignores anything sent in the query string.
var TLID = "";

$(function () {

    loadDashboard();

    // The team lead CREATION form used to live on its own TeamLeads.html, posting
    // to /TeamLeadDashBoards/save-team-lead — a route that no longer exists. That
    // page (and its duplicate handler that double-submitted on save) is gone;
    // creating a team lead is Admin/add-user.html now, through /api/admin/users.

    /**
     * One call fills the page. The old dashboard made four (team-metrics,
     * team-huddle, nurture active, nurture review) and each carried the team lead
     * id as a parameter; this one carries nothing, because the server already
     * knows who is asking.
     */
    function loadDashboard() {
        $.ajax({
            url: API_BASE_URL + '/dashboards/team-lead',
            method: 'GET',
            success: function (res) {
                if (!res || !res.data) {
                    $('#alerts').html('<div class="alert alert-warning">No data</div>');
                    return;
                }
                renderMetrics(res.data);
            },
            error: function (xhr) {
                // 403 here means signed in but not a team lead, which is a different
                // problem from the server being unreachable and reads differently.
                var message = (xhr && xhr.status === 403)
                    ? 'You do not have team lead access.'
                    : 'Error loading metrics';

                $('#alerts').html('<div class="alert alert-danger">' + message + '</div>');
            }
        });
    }

    // Kept so the modal-close handler still has something to call.
    function loadMetrics() { loadDashboard(); }
    function checkLogin() {

        let isLogin = sessionStorage.getItem("IsLogin");

        if (
            isLogin === null ||
            isLogin === undefined ||
            isLogin === "false" ||
            isLogin === false
        ) {

            window.location.href = "../Volunteers/Login.html";

            return false;
        }

        return true;
    }
    /**
     * Fills the existing dashboard from the new payload.
     *
     * The markup below is deliberately the SAME markup the page has always
     * rendered — same ids, same classes, same card layout. Team leads already work
     * in this screen; the schema underneath changed, not their job. Only the
     * numbers' meaning moved, and where the new schema has no equivalent for an
     * old column it shows a dash rather than an invented figure.
     */
    function renderMetrics(data) {

        var teams = data.teams || [];
        var esc = data.escalations || {};
        var cases = data.cases || {};
        var contacts = data.contacts || {};
        var volunteers = data.volunteers || [];

        // Downstream handlers (the huddle button, the modal reload) still expect a
        // team id, so keep TLID meaningful — sourced from the response now.
        TLID = teams.length ? teams[0].publicId : "";

        // The huddle card appears on the configured huddle day, exactly as it did
        // before — the MVP decided this from system_config.team_hurdle, which the
        // migration carried over to app_setting as huddle.day_of_week.
        loadHuddleCard();

        renderNurture(data.nurture || {});

        var teamName = teams.length ? teams[0].name : '';
        $('#dashboardTitle').text('TEAM LEAD DASHBOARD' + (teamName ? ' - ' + teamName : ''));

        var subtitle = teams.length
            ? teams[0].campusName + ' · ' + teams[0].memberCount + ' of ' + teams[0].maxMembers + ' members'
            : 'No team assigned';

        // The header is the shared shell's now, so the subtitle goes through it
        // rather than to an element this page no longer owns.
        if (window.TeamLeadShell) TeamLeadShell.setSubtitle(subtitle);

        $('#dashboardSubtitle').text(subtitle);

        // ── Team Performance ────────────────────────────────────────────────
        // Same label/value rows as before. The old metrics came from `follow_ups`,
        // a table the new schema dropped, so these are the equivalent figures from
        // care_case and care_interaction.
        $('#teamPerformance').html(
            "<div>Awaiting Assignment: <strong>" + (cases.awaitingAssignment || 0) + "</strong></div>" +
            "<div>In Progress: <strong>" + (cases.inProgress || 0) + "</strong></div>" +
            "<div>Escalated: <strong>" + (cases.escalated || 0) + "</strong></div>" +
            "<div>Awaiting Review: <strong>" + (cases.awaitingReview || 0) + "</strong></div>" +
            "<div>Due Today: <strong>" + (contacts.dueToday || 0) + "</strong></div>" +
            "<div>Overdue: <strong>" + (contacts.overdue || 0) + "</strong></div>"
        );

        // ── Volunteers Metrics ──────────────────────────────────────────────
        var rows = volunteers.map(function (v) {
            var atCapacity = !v.hasSpareCapacity;

            var badge = (v.openEscalations || 0) > 0
                ? "<span style=\"background:#ef4444;color:#fff;min-width:18px;height:18px;" +
                  "border-radius:999px;display:flex;align-items:center;justify-content:center;" +
                  "font-size:11px;font-weight:bold;padding:0 5px;\">" + v.openEscalations + "</span>"
                : '';

            // ── Trend ────────────────────────────────────────────────────────
            // Last complete week's completion rate against the week before it.
            // NONE means one of those weeks had nothing scheduled — no work given
            // is not the same as work not done, so it shows a dash rather than a
            // red arrow the volunteer did not earn.
            var trendMark =
                v.trend === 'UP'   ? '<span title="Improving" style="color:#16a34a;font-size:15px;">&#8593;</span>'
              : v.trend === 'DOWN' ? '<span title="Down on last week" style="color:#dc2626;font-size:15px;">&#8595;</span>'
              : v.trend === 'FLAT' ? '<span title="Steady" style="color:#6b7280;font-size:15px;">&#8594;</span>'
              :                      '<span title="Not enough weeks to compare" style="color:#9ca3af;">—</span>';

            var rate = (v.completionRate === null || v.completionRate === undefined)
                ? ''
                : ' <span style="font-size:11px;color:#6b7280;">' + v.completionRate + '%</span>';

            // ── Flag ─────────────────────────────────────────────────────────
            // Health, not capacity: green steady or improving, amber one week down,
            // red two consecutive weeks down. Capacity is already the column two to
            // the left, so repeating it here wasted the only slot that could carry
            // this.
            var flag =
                v.healthFlag === 'RED'
                    ? '<span class="badge bg-danger" title="Two weeks down in a row">At risk</span>'
              : v.healthFlag === 'AMBER'
                    ? '<span class="badge bg-warning text-dark" title="Down on last week">Watch</span>'
                    : '<span class="badge bg-success">Healthy</span>';

            // A volunteer with no room is still worth seeing, so it rides along
            // beside the health flag rather than replacing it.
            if (atCapacity) {
                flag += ' <span class="badge bg-light text-dark" title="No spare capacity">Full</span>';
            }

            // Unreachable outranks everything else on this row. The card exists to
            // answer "who can take another one?", and this person cannot take any —
            // assignment refuses them. Without the badge they read as the team's
            // freest volunteer: a full allowance, nothing assigned, and no 'Full'
            // marker to suggest otherwise.
            if (v.isReachable === false) {
                var missing = (v.hasLogin === false ? 'no sign-in' : '') +
                              (v.hasLogin === false && v.hasTelegram === false ? ', ' : '') +
                              (v.hasTelegram === false ? 'no Telegram' : '');

                flag = '<span class="badge bg-secondary" title="Cannot be assigned: ' +
                       escapeHtml(missing) + '">Unreachable</span> ' + flag;
            }

            return "<tr>" +
                "<td class='v-name' style='cursor:pointer' data-id='" + v.publicId + "'>" +
                    "<span style=\"display:flex;align-items:center;gap:6px;\">" +
                        escapeHtml(v.name) + badge +
                    "</span>" +
                "</td>" +
                "<td>" + escapeHtml(v.capacityBandLabel || v.capacityBandCode) +
                    " | " + v.currentCaseLoad + " / " + v.capacityMaxPerWeek + "</td>" +
                "<td>" + v.currentCaseLoad + "</td>" +
                "<td>" + trendMark + rate + "</td>" +
                "<td>" + flag + "</td>" +
            "</tr>";
        }).join('');

        $('#volunteersTable tbody').html(
            rows || "<tr><td colspan='5' class='text-muted'>No volunteers on this team.</td></tr>");

        // ── Attention Needed ────────────────────────────────────────────────
        var attention = [];

        if (esc.unacknowledged > 0) {
            attention.push({
                who: 'Escalations',
                message: esc.unacknowledged + ' waiting to be acknowledged' +
                    (esc.oldestUnacknowledgedHours
                        ? ' (oldest ' + Math.round(esc.oldestUnacknowledgedHours) + 'h)'
                        : ''),
                priority: 'High'
            });
        }

        if (cases.awaitingReview > 0) {
            attention.push({
                who: 'Review',
                message: cases.awaitingReview + ' case(s) need your sign-off',
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

        if (cases.awaitingAssignment > 0) {
            attention.push({
                who: 'Assignment',
                message: cases.awaitingAssignment + ' case(s) with no volunteer',
                priority: 'Medium'
            });
        }

        $('#attentionList').html(attention.length
            ? attention.map(function (a) {
                return '<li class="list-group-item">' + a.who + ' - ' + a.message +
                       '<span class="badge bg-secondary ms-2">' + a.priority + '</span></li>';
              }).join('')
            : '<li class="list-group-item text-muted">Nothing needs your attention.</li>');

        // ── Escalations & Check-ins ─────────────────────────────────────────
        var escalations = (esc.urgent || []).map(function (e) {
            var date = new Date(e.raisedAt);
            var day = date.getDate();
            var month = date.toLocaleString('en-US', { month: 'short' });
            var formattedDate = day + getDaySuffix(day) + ', ' + month;

            // The siren is reserved for what actually warrants it: an EMERGENCY, or
            // a reason the schema flags as needing a safeguarding protocol.
            var icon = (e.tier === 'EMERGENCY' || e.requiresProtocol)
                ? '<div class="blink-siren">🚨</div>'
                : '⚠️';

            var waited = e.waitingHours >= 24
                ? Math.round(e.waitingHours / 24) + 'd'
                : Math.round(e.waitingHours) + 'h';

            return '<div class="escalation-item" data-id="' + e.publicId + '" ' +
                   'style="cursor:pointer; padding:6px; border-radius:5px;">' +
                   icon + ' ' + escapeHtml(e.personName || 'Unknown') +
                   ' -  Escalated on ' + formattedDate +
                   ' <span class="text-muted">(' + waited + ')</span>' +
                   '</div>';
        }).join('');

        var escalationCount = esc.unacknowledged || 0;

        $('#checkinsList').html(
            '<h5>Escalations Pending</h5>' +
            '<h6 class="text-muted">' + escalationCount + ' case' +
                (escalationCount !== 1 ? 's' : '') + ' need Team Lead Follow-up:</h6>' +
            '<div>' + (escalations || 'No pending escalations') + '</div>' +
            '<h5 class="mt-4">Check-ins</h5>' +
            '<dl class="mb-3" id="checkinsDue"><dd>Loading…</dd></dl>'
        );

        loadCheckInsDue();

        $('#alerts').empty();
    }

    /**
     * Who is due a check-in. A separate call because it is a separate concern from
     * the case dashboard, and a slow or failing check-in query should not blank the
     * escalations sitting above it.
     */
    function loadCheckInsDue() {
        // $.ajax, like the rest of this file: auth.js installs a jQuery interceptor
        // that attaches the bearer token to same-origin /api calls, so the header is
        // not set by hand here.
        $.ajax({ url: API_BASE_URL + '/check-ins/due', method: 'GET' })
            .done(function (body) {
                var due = (body && body.data) || [];

                if (!due.length) {
                    $('#checkinsDue').html('<dd>Nobody is due a check-in.</dd>');
                    return;
                }

                $('#checkinsDue').html(due.slice(0, 8).map(function (d) {
                    // Never having had one is worse than being late for one, so it is
                    // said plainly rather than shown as a very large number of days.
                    var when = d.neverCheckedIn
                        ? 'never checked in'
                        : d.daysSinceLastCheckIn + ' days ago';

                    var tone = d.lastEmotionalTone === 'RED'
                        ? ' <span class="badge bg-danger">RED last time</span>'
                        : d.lastEmotionalTone === 'AMBER'
                            ? ' <span class="badge bg-warning text-dark">AMBER last time</span>'
                            : '';

                    return '<dt class="checkin-ele" data-id="' + escapeHtml(d.volunteerId) + '" ' +
                                'style="cursor:pointer;">' +
                                escapeHtml(d.volunteerName) + tone +
                           '</dt>' +
                           '<dd>Last: ' + escapeHtml(when) + '</dd>';
                }).join(''));
            })
            .fail(function () {
                $('#checkinsDue').html('<dd class="text-muted">Check-ins could not be loaded.</dd>');
            });
    }

    function getDaySuffix(d) {
        if (d > 3 && d < 21) return 'th';
        switch (d % 10) {
            case 1: return 'st';
            case 2: return 'nd';
            case 3: return 'rd';
            default: return 'th';
        }
    }

    /** Names come from user data and land in HTML, so they are escaped. */
    function escapeHtml(value) {
        return String(value === null || value === undefined ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /**
     * Opens a detail screen.
     *
     * REAL NAVIGATION, NOT AN IFRAME. The application sends X-Frame-Options: DENY
     * and CSP frame-ancestors 'none' (SecurityHeadersMiddleware), so the browser
     * refuses ANY page loaded into #escIframe and the modal shows "refused to
     * connect". That was true of every modal on this page — escalations, check-ins
     * and volunteer detail alike — which meant clicking an escalation, the team
     * lead's main job, did nothing but show an error.
     *
     * Those headers are deliberate clickjacking defence and stay. The modal was
     * the wrong mechanism; the Add Volunteer button was already moved off it for
     * exactly this reason.
     */
    function openInModal(url) {
        window.location.href = url;
    }
    // The modal is no longer used for navigation, but the element may still be in
    // the page. Guard the lookup: an unconditional addEventListener on a missing
    // element throws, and a throw here would take the whole dashboard script with
    // it — the page would render empty with only a console error to explain why.
    var modalEl = document.getElementById('esclationModel');

    if (modalEl) {
        modalEl.addEventListener('hidden.bs.modal', function () { loadDashboard(); });
    }


    $(document).on('click', '.checkin-ele', function () {
        const id = $(this).data('id');
        const teamLeadId = $(this).data('teamleadid');
        const vName = $(this).data('volunteername');
        const tName = $(this).data('teamleadname');

        goToCheckIns(id, teamLeadId, vName, tName);
    });

    function goToCheckIns(id, teamLeadId, vName, tName) {
        const url = `CheckIns.html?id=${id}&teamLeadId=${teamLeadId}`;

        openInModal(url, 'CheckIns');
    }

    function openEscalation(id) {
        const url = `Escalations.html?id=${id}`;
        openInModal(url, 'Escalation');
    }
    $(document).on('click', '.escalation-item', function () {
        const id = $(this).data('id');
        openEscalation(id);
    });

    function openVolunteerDashboard(id) {
        const url = `../Volunteers/Assignments.html?volunteerid=${id}`;
        openInModal(url, 'Volunteer Details..');
    }
    // The header (logo, menu, sign out) is rendered by teamlead-shell.js and is
    // the same on every team lead screen. Add volunteer is deliberately not on it:
    // enrolling a volunteer is a pastor's or administrator's decision.

    $(document).on('click', '.v-name', function () {
        const id = $(this).data('id');
        openVolunteerDashboard(id);
    });

    // ══════════════════════════════════════════════════════════════════════
    // TEAM HUDDLE
    //
    // The weekly meeting where the lead reviews the team's contacts and records,
    // for each, whether the volunteer's escalation judgement was right. It is the
    // only thing in the system that can catch an UNDER-escalation — a concern that
    // was heard and never passed on. The chase-up job cannot: it only chases
    // escalations that were actually raised.
    //
    // Rebuilt against /api/huddle. Two things changed from the MVP, both because
    // the MVP version recorded ZERO verdicts in three months of production:
    //
    //   1. The window is real. The old modal was titled "for this Week" but the
    //      week filter was commented out of the SQL, so it returned every
    //      unassessed contact ever — an unpaginated wall that only grew.
    //   2. Verdicts save as a batch. The old screen made the lead click Update and
    //      confirm a dialog per row.
    // ══════════════════════════════════════════════════════════════════════

    var HUDDLE_ITEMS = [];

    $(document).on('click', '#teamHuddleBtn', function () {
        var modal = new bootstrap.Modal(document.getElementById('teamHuddleModal'));
        modal.show();
        loadHuddle();
    });

    /**
     * The nurture card: journeys quietly in progress, and how far each has reached.
     *
     * This is the one thing about nurture the rest of the page does not already
     * answer. Overdue steps show as "Overdue" in Team Performance and finished
     * plans as "Awaiting Review"; neither says how many people are mid-journey or
     * where they have got to — which is what you look at to see whether nurture is
     * actually moving rather than just not on fire.
     */
    function renderNurture(n) {
        var active = n.active || 0;

        // Hidden when the team has nobody in nurture: an empty card teaches the eye
        // to skip that part of the page.
        if (!active) { $('#nurtureCard').hide(); return; }

        $('#nurtureCard').show();

        $('#nurtureActiveCount').text('Active: ' + active);
        $('#nurtureOverdueCount').text('Overdue Steps: ' + (n.overdue || 0));
        $('#nurtureReviewCount').text('Paused: ' + (n.paused || 0));

        var items = n.items || [];

        var rows = items.map(function (i) {
            var pct = i.planStepCount > 0
                ? Math.round((i.currentStepNumber / i.planStepCount) * 100)
                : 0;

            var flag = i.isPaused
                ? ' <span class="badge bg-danger">paused</span>'
                : (i.daysOverdue ? ' <span class="badge bg-warning text-dark">' +
                                   i.daysOverdue + 'd late</span>' : '');

            return '<div style="padding:7px 0;border-bottom:1px solid #f1f2f4;">' +
                       '<div style="display:flex;justify-content:space-between;gap:8px;align-items:baseline;">' +
                           '<span style="font-weight:600;font-size:13px;">' +
                               escapeHtml(i.personName) + flag + '</span>' +
                           '<span style="font-size:11px;color:#6b7280;white-space:nowrap;">' +
                               escapeHtml(i.progress) + '</span>' +
                       '</div>' +
                       '<div style="height:4px;background:#e5e7eb;border-radius:999px;margin-top:5px;overflow:hidden;">' +
                           '<div style="height:100%;width:' + pct + '%;background:#085c40;border-radius:999px;"></div>' +
                       '</div>' +
                       (i.volunteerName
                           ? '<div style="font-size:11px;color:#6b7280;margin-top:3px;">' +
                                 escapeHtml(i.volunteerName) + '</div>'
                           : '') +
                   '</div>';
        }).join('');

        $('#nurtureList').html(rows);

        // The full picture lives on the pipeline screen, which can filter and page;
        // the card is a summary, not a second list view.
        $('#viewNurtureBtn')
            .text('See all in the pipeline')
            .off('click')
            .on('click', function () {
                window.location.href = '/templates/Peoples/Pipeline.html?stage=NURTURE';
            });
    }

    /**
     * Decides whether the huddle card is shown, and says how much is waiting.
     *
     * A separate call from the dashboard, the same way check-ins are: it is a
     * different concern, and a slow huddle query should not hold up the escalations
     * above it.
     */
    function loadHuddleCard() {
        $.ajax({ url: API_BASE_URL + '/huddle', method: 'GET' })
            .done(function (res) {
                var data = res && res.data;

                if (!data) { $('.team-huddle').hide(); return; }

                var waiting = (data.items || []).length;

                // Shown on the day, or any day there is a backlog — a huddle that was
                // missed last week is precisely when the card needs to be visible.
                var show = data.isHuddleDay || waiting > 0 || data.olderUnassessedCount > 0;

                $('.team-huddle').toggle(show);

                if (!show) return;

                $('#teamHuddleBtn').text(waiting > 0
                    ? 'Team Huddle (' + waiting + ' to review)'
                    : 'Team Huddle');
            })
            .fail(function () { $('.team-huddle').hide(); });
    }

    function loadHuddle() {
        $('#huddleTable tbody').html(
            '<tr><td colspan="7" class="text-muted">Loading…</td></tr>');

        $.ajax({ url: API_BASE_URL + '/huddle', method: 'GET' })
            .done(function (res) {
                var data = res && res.data;

                if (!data) {
                    $('#huddleTable tbody').html(
                        '<tr><td colspan="7" class="text-muted">Nothing to review.</td></tr>');
                    return;
                }

                HUDDLE_ITEMS = data.items || [];

                $('#teamHuddleModal .modal-title').text(
                    'Team Huddle — ' + shortDate(data.fromDate) + ' to ' + shortDate(data.toDate));

                if (!HUDDLE_ITEMS.length) {
                    $('#huddleTable tbody').html(
                        '<tr><td colspan="7" class="text-muted">' +
                        (data.olderUnassessedCount > 0
                            ? 'Nothing from this week. ' + data.olderUnassessedCount +
                              ' older contact(s) are still unassessed.'
                            : 'Nothing to review this week.') +
                        '</td></tr>');
                    renderHuddleFooter(data);
                    return;
                }

                $('#huddleTable tbody').html(HUDDLE_ITEMS.map(function (r) {
                    // A contact that already raised an escalation is marked, because
                    // "did they escalate?" is the question being judged and the lead
                    // should not have to remember.
                    var raised = r.raisedEscalation
                        ? ' <span class="badge bg-danger">escalated</span>'
                        : '';

                    return '<tr data-id="' + escapeHtml(r.interactionId) + '">' +
                        '<td>' + escapeHtml(r.volunteerName || '—') + '</td>' +
                        '<td>' + escapeHtml(r.personName || '—') + raised + '</td>' +
                        '<td>' + escapeHtml(r.outcome || r.status || '—') + '</td>' +
                        '<td>' + escapeHtml(r.intent || '—') + '</td>' +
                        '<td>' + (r.occurredAt ? formatDate(r.occurredAt) : '—') + '</td>' +
                        '<td style="max-width:260px;">' + escapeHtml(r.notes || '') + '</td>' +
                        '<td>' +
                            '<select class="form-select form-select-sm escalation-dropdown" ' +
                                    'data-id="' + escapeHtml(r.interactionId) + '">' +
                                '<option value="">Not assessed</option>' +
                                '<option value="CORRECT">Correct</option>' +
                                '<option value="UNDER_ESCALATED">Under-escalated</option>' +
                                '<option value="OVER_ESCALATED">Over-escalated</option>' +
                            '</select>' +
                            // The note is required for a miscalibration: the verdict
                            // exists to be raised at the volunteer's next check-in, and
                            // "you over-escalated" with nothing attached is not a
                            // conversation. It stays hidden until it is needed.
                            '<input type="text" class="form-control form-control-sm mt-1 huddle-note" ' +
                                   'data-id="' + escapeHtml(r.interactionId) + '" ' +
                                   'placeholder="What should have happened?" style="display:none;" />' +
                        '</td>' +
                    '</tr>';
                }).join(''));

                renderHuddleFooter(data);
            })
            .fail(function (xhr) {
                $('#huddleTable tbody').html(
                    '<tr><td colspan="7" class="text-danger">' +
                    escapeHtml(huddleError(xhr)) + '</td></tr>');
            });
    }

    /** The Save button and the backlog note live in the modal footer. */
    function renderHuddleFooter(data) {
        var $footer = $('#teamHuddleModal .modal-footer');

        $footer.find('.huddle-save, .huddle-backlog').remove();

        if (data.olderUnassessedCount > 0) {
            $footer.prepend(
                '<span class="huddle-backlog text-muted me-auto">' +
                data.olderUnassessedCount + ' older contact(s) still unassessed' +
                '</span>');
        }

        if ((data.items || []).length) {
            $footer.append(
                '<button type="button" class="btn btn-primary huddle-save">' +
                'Save verdicts</button>');
        }
    }

    // Show the note box only where a reason is actually required.
    $(document).on('change', '.escalation-dropdown', function () {
        var id = $(this).data('id');
        var needsNote = this.value === 'UNDER_ESCALATED' || this.value === 'OVER_ESCALATED';

        $('.huddle-note[data-id="' + id + '"]').toggle(needsNote);
    });

    $(document).on('click', '.huddle-save', function () {
        var verdicts = [];
        var missingNote = 0;

        $('.escalation-dropdown').each(function () {
            var value = this.value;
            if (!value) return;                  // left unassessed; simply not sent

            var id = $(this).data('id');
            var note = ($('.huddle-note[data-id="' + id + '"]').val() || '').trim();

            if ((value === 'UNDER_ESCALATED' || value === 'OVER_ESCALATED') && !note) {
                missingNote++;
                return;
            }

            verdicts.push({ interactionId: id, assessment: value, note: note || null });
        });

        if (missingNote > 0) {
            showToast(missingNote + ' verdict(s) need a note before they can be saved.', 'warning');
            return;
        }

        if (!verdicts.length) {
            showToast('Nothing selected.', 'warning');
            return;
        }

        var $btn = $(this).prop('disabled', true).text('Saving…');

        $.ajax({
            url: API_BASE_URL + '/huddle/verdicts',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ verdicts: verdicts })
        })
        .done(function (res) {
            // A refusal arrives as HTTP 200 with responseType 1.
            if (res && res.responseType !== 0) {
                showToast(res.message || 'That could not be saved.', 'warning');
                return;
            }

            showToast(res.message || 'Recorded.', 'success');
            loadHuddle();
            loadDashboard();
        })
        .fail(function (xhr) { showToast(huddleError(xhr), 'error'); })
        .always(function () { $btn.prop('disabled', false).text('Save verdicts'); });
    });

    function huddleError(xhr) {
        var b = xhr && xhr.responseJSON;
        return (b && (b.message || b.detail || b.title)) || 'Could not load the huddle.';
    }

    function shortDate(value) {
        var d = new Date(value);
        return d.toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
    }


    function formatDate(date) {
        const d = new Date(date);
        const day = String(d.getDate()).padStart(2, '0');
        const month = String(d.getMonth() + 1).padStart(2, '0');
        const year = d.getFullYear();
        return `${day}-${month}-${year}`;
    }

});