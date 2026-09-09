// One visitor's whole history.
//
//   GET /api/pipeline/{personId}   profile + every case + a merged timeline
//
// Opened from the pipeline's "History" button. The unit is the PERSON, not the
// case: somebody who visited, went quiet and came back a year later has two
// cases, and reading them separately loses the shape of the relationship.
//
// Scope is enforced by the server. A team lead who edits the id in the address
// bar gets "That visitor was not found." for anyone outside their teams — the
// same message as a genuinely missing person, so the screen cannot be used to
// discover who exists.

$(function () {

    var personId = new URLSearchParams(window.location.search).get('person');

    // Everything the caller was looking at on the pipeline, so Back returns them
    // to their filtered page rather than the top of an unfiltered list.
    var back = new URLSearchParams(window.location.search).get('back');
    if (back) $('#backLink').attr('href', '/pages/care/pipeline.html?' + back);

    var events = [];
    var activeFilter = '';

    if (!personId) {
        notice('No visitor was specified.');
        return;
    }

    load();

    // ------------------------------------------------------------------
    // Load
    // ------------------------------------------------------------------
    function load() {
        $.ajax({ url: API_BASE_URL + '/pipeline/' + encodeURIComponent(personId), method: 'GET' })
            .done(function (res) {
                // A refusal arrives as HTTP 200 with responseType 1, so .done() fires
                // for it. Without this check the page would render an empty shell.
                if (!res || res.responseType !== 0 || !res.data) {
                    notice((res && res.message) || 'That visitor could not be loaded.');
                    return;
                }

                render(res.data);
            })
            .fail(function (xhr) {
                notice(xhr && xhr.status === 403
                    ? 'You do not have access to this visitor.'
                    : errorFrom(xhr));
            });
    }

    function render(d) {
        var p = d.profile || {};

        document.title = p.fullName + ' — history';

        $('#personName').text(p.fullName || 'Visitor');
        $('#personRef').text(p.referenceCode || '');

        renderDoNotContact(p);
        renderHeadBadges(p);
        renderStats(d.stats || {}, p);
        renderDetails(p);
        renderCases(d.cases || []);

        events = d.events || [];
        renderFilters();
        renderTimeline();

        $('#journey').prop('hidden', false);
    }

    // ------------------------------------------------------------------
    // Consent — first, and impossible to miss
    // ------------------------------------------------------------------
    function renderDoNotContact(p) {
        if (!p.doNotContact) return;

        $('#dncBanner')
            .html('&#9940; Do not contact' +
                  (p.doNotContactNote ? '<span>' + esc(p.doNotContactNote) + '</span>' : '') +
                  (p.doNotContactAt ? '<span>Recorded ' + formatDate(p.doNotContactAt) + '</span>' : ''))
            .prop('hidden', false);
    }

    function renderHeadBadges(p) {
        var badges = [];

        if (p.lifecycleStatus === 'MEMBER') {
            badges.push('<span class="tl-badge tl-badge-green">Member' +
                        (p.becameMemberOn ? ' since ' + formatDate(p.becameMemberOn) : '') + '</span>');
        } else if (p.lifecycleStatus && p.lifecycleStatus !== 'VISITOR') {
            badges.push('<span class="tl-badge tl-badge-grey">' + esc(pretty(p.lifecycleStatus)) + '</span>');
        } else {
            badges.push('<span class="tl-badge tl-badge-grey">Visitor</span>');
        }

        if (!p.isLocal) badges.push('<span class="tl-badge tl-badge-amber">Out of town</span>');
        if (p.hasTelegram) badges.push('<span class="tl-badge tl-badge-green">Telegram</span>');

        $('#headBadges').html(badges.join(' '));
    }

    // ------------------------------------------------------------------
    // The numbers that say how this has actually gone
    // ------------------------------------------------------------------
    function renderStats(s, p) {
        var cards = [];

        cards.push(stat(
            s.daysInJourney === null || s.daysInJourney === undefined ? '—' : s.daysInJourney,
            'Days in journey',
            s.firstSeenOn ? 'since ' + formatDate(s.firstSeenOn) : 'never started'));

        cards.push(stat(s.contactsLogged || 0, 'Contacts logged',
            (s.contactsMissed || 0) + ' missed'));

        // The single most honest number here: a case can sit in NURTURE for months
        // while every call goes unanswered.
        cards.push(stat(
            s.contactSuccessRate === null || s.contactSuccessRate === undefined
                ? '—' : s.contactSuccessRate + '%',
            'Reached',
            (s.contactsMade || 0) + ' of ' + (s.contactsLogged || 0) + ' attempts'));

        // Silence is the thing this whole system exists to prevent, so it is called
        // out in red past a month.
        var days = s.daysSinceContact;
        var stale = days !== null && days !== undefined && days >= 30;

        cards.push(stat(
            days === null || days === undefined ? 'never' : days,
            'Days since contact',
            s.lastContactAt ? formatDate(s.lastContactAt) : 'nobody has reached them',
            stale));

        cards.push(stat(s.escalationsRaised || 0, 'Concerns raised',
            (s.openEscalations || 0) + ' still open', (s.openEscalations || 0) > 0));

        $('#stats').html(cards.join(''));
    }

    function stat(value, label, sub, bad) {
        return '<div class="vj-stat' + (bad ? ' is-bad' : '') + '">' +
                   '<div class="vj-stat-value">' + esc(value) + '</div>' +
                   '<div class="vj-stat-label">' + esc(label) + '</div>' +
                   (sub ? '<div class="vj-stat-sub">' + esc(sub) + '</div>' : '') +
               '</div>';
    }

    // ------------------------------------------------------------------
    // Who they are
    // ------------------------------------------------------------------
    function renderDetails(p) {
        // Ordered the way somebody about to make contact reads it: how to reach
        // them, then where, then who recorded them.
        var rows = [
            ['Mobile', p.mobile],
            ['Email', p.email],
            ['Area', p.areaName || p.locality],
            ['Address', p.addressLine],
            ['Postal code', p.postalCode],
            ['Campus', p.campusName],
            ['Age group', p.ageBand ? band(p.ageBand) : null],
            ['Gender', p.gender ? pretty(p.gender) : null],
            ['Household', p.householdType ? pretty(p.householdType) : null],
            ['Recorded', p.recordedAt ? formatDate(p.recordedAt) +
                (p.recordedBy ? ' by ' + p.recordedBy : '') : null],
            ['Notes', p.notes]
        ].filter(function (r) { return r[1]; });

        if (!rows.length) {
            $('#details').html('<div><dd class="vj-stat-sub">Nothing else was recorded.</dd></div>');
            return;
        }

        $('#details').html(rows.map(function (r) {
            return '<div><dt>' + esc(r[0]) + '</dt><dd>' + esc(r[1]) + '</dd></div>';
        }).join(''));
    }

    // ------------------------------------------------------------------
    // Their cases
    // ------------------------------------------------------------------
    function renderCases(cases) {
        $('#caseCount').text(cases.length + (cases.length === 1 ? ' case' : ' cases'));

        if (!cases.length) {
            $('#cases').html('<p class="tl-empty" style="padding:18px 0;">' +
                'No follow-up was ever opened for this visitor.</p>');
            return;
        }

        $('#cases').html(cases.map(function (c) {
            var open = c.status !== 'CLOSED';

            var progress = (c.stage === 'NURTURE' && c.planStepCount)
                ? 'Step ' + c.currentStepNumber + ' of ' + c.planStepCount +
                  (c.planName ? ' · ' + c.planName : '')
                : null;

            var meta = [
                c.volunteerName ? 'With ' + c.volunteerName : 'Unassigned',
                c.teamName,
                progress,
                open
                    ? (c.nextStepDueOn ? 'Next due ' + formatDate(c.nextStepDueOn) : null)
                    : 'Closed ' + formatDate(c.closedAt) +
                      (c.closeReasonLabel ? ' — ' + c.closeReasonLabel : '')
            ].filter(Boolean);

            return '<div class="vj-case' + (open ? ' is-open' : '') + '">' +
                       '<div class="vj-case-top">' +
                           '<span class="vj-ref">' + esc(c.referenceCode || '') + '</span>' +
                           '<span class="tl-badge ' + (open ? 'tl-badge-green' : 'tl-badge-grey') + '">' +
                               esc(pretty(c.stage)) + '</span>' +
                           (c.status === 'ESCALATED'
                               ? '<span class="tl-badge tl-badge-red">paused</span>' : '') +
                       '</div>' +
                       '<div class="vj-case-meta">' + esc(meta.join(' · ')) + '</div>' +
                       '<div class="vj-case-meta">Opened ' + formatDate(c.openedAt) + '</div>' +
                   '</div>';
        }).join(''));
    }

    // ------------------------------------------------------------------
    // The timeline
    // ------------------------------------------------------------------
    function renderFilters() {
        var counts = {};
        events.forEach(function (e) { counts[e.kind] = (counts[e.kind] || 0) + 1; });

        var kinds = [
            { key: '', label: 'Everything', count: events.length },
            { key: 'CONTACT', label: 'Contacts', count: counts.CONTACT || 0 },
            { key: 'ESCALATION', label: 'Concerns', count: counts.ESCALATION || 0 },
            { key: 'NOTE', label: 'Notes', count: counts.NOTE || 0 },
            { key: 'ASSIGNMENT', label: 'Assignments', count: counts.ASSIGNMENT || 0 }
        // A filter that can only ever return nothing is noise, so kinds with no
        // events are not offered.
        ].filter(function (k) { return k.key === '' || k.count > 0; });

        $('#filters').html(kinds.map(function (k) {
            return '<button class="vj-filter' + (activeFilter === k.key ? ' is-active' : '') +
                   '" data-kind="' + esc(k.key) + '">' +
                   esc(k.label) + ' ' + k.count + '</button>';
        }).join(''));
    }

    $(document).on('click', '.vj-filter', function () {
        activeFilter = $(this).data('kind') || '';
        renderFilters();
        renderTimeline();
    });

    function renderTimeline() {
        var shown = activeFilter
            ? events.filter(function (e) { return e.kind === activeFilter; })
            : events;

        $('#eventCount').text(shown.length + (shown.length === 1 ? ' event' : ' events'));

        if (!shown.length) {
            $('#timeline').html('<p class="tl-empty">' +
                (events.length ? 'Nothing of that kind.' : 'Nothing has happened yet.') + '</p>');
            return;
        }

        $('#timeline').html(shown.map(function (e) {
            var chips = (e.tags || []).map(function (t) {
                return '<span class="vj-chip">' + esc(t) + '</span>';
            }).join('');

            return '<div class="vj-event tone-' + esc(e.tone || 'NEUTRAL') + '">' +
                       '<div class="vj-event-head">' +
                           '<span class="vj-event-title">' + esc(e.title) + '</span>' +
                           '<span class="vj-event-when">' + esc(formatDateTime(e.occurredAt)) + '</span>' +
                           (e.actor ? '<span class="vj-event-actor">· ' + esc(e.actor) + '</span>' : '') +
                           (e.caseReference
                               ? '<span class="vj-ref" style="margin-left:auto;">' +
                                     esc(e.caseReference) + '</span>'
                               : '') +
                       '</div>' +
                       (chips ? '<div class="vj-chips">' + chips + '</div>' : '') +
                       (e.detail ? '<div class="vj-event-detail">' + esc(e.detail) + '</div>' : '') +
                   '</div>';
        }).join(''));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    function notice(message) {
        $('#notice').html('<div class="tl-card"><div class="tl-card-body">' +
            '<p class="tl-empty">' + esc(message) + '</p></div></div>');
    }

    function band(code) {
        if (code === 'UNDER_18') return 'Under 18';
        if (code === 'OVER_60') return 'Over 60';
        return String(code).replace('_', '–');
    }

    function pretty(code) {
        if (!code) return '';
        return code.charAt(0) + code.slice(1).toLowerCase().replace(/_/g, ' ');
    }

    function formatDate(value) {
        if (!value) return '—';
        var d = new Date(value);
        return isNaN(d) ? '—' : d.toLocaleDateString();
    }

    function formatDateTime(value) {
        if (!value) return '';
        var d = new Date(value);
        if (isNaN(d)) return '';

        return d.toLocaleDateString() + ' ' +
               d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    function esc(value) {
        return String(value === null || value === undefined ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function errorFrom(xhr) {
        var b = xhr && xhr.responseJSON;
        return (b && (b.message || b.detail || b.title)) || 'Could not load this visitor.';
    }
});
