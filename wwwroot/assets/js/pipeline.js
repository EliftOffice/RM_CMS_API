// People pipeline — every visitor and where their journey has reached.
//
// The screen nothing else in the system provides: the dashboard answers "what needs
// me today" and the case screens answer "what about this one person". Neither
// answers "where is everybody", which is the question you ask to find out whether
// the funnel is working rather than whether today is busy.
//
// SCOPE IS NOT A PARAMETER. What you see is decided by the server from your role —
// a team lead gets their own teams, a pastor their campus, an administrator
// everything. There is nothing here to edit to see more.

$(function () {

    var state = { page: 1, pageSize: 50, search: '', stage: '' };
    var debounce = null;

    // The dashboard's nurture card links here with ?stage=NURTURE, so arriving
    // from it lands on the filtered view rather than the whole list.
    var initialStage = new URLSearchParams(window.location.search).get('stage');

    if (initialStage) {
        state.stage = initialStage.toUpperCase();
        $('#stageFilter').val(state.stage);
    }

    load();

    // ------------------------------------------------------------------
    // Filters
    // ------------------------------------------------------------------
    $('#search').on('input', function () {
        var value = this.value;

        // Debounced: a keystroke per request would put thirty queries behind a
        // name somebody is still typing.
        clearTimeout(debounce);
        debounce = setTimeout(function () {
            state.search = value;
            state.page = 1;
            load();
        }, 300);
    });

    $('#stageFilter').on('change', function () {
        state.stage = this.value;
        state.page = 1;
        load();
    });

    $('#clearBtn').on('click', function () {
        state.search = '';
        state.stage = '';
        state.page = 1;
        $('#search').val('');
        $('#stageFilter').val('');
        load();
    });

    // The funnel doubles as the stage filter — clicking a stage is the obvious
    // thing to try, and it is cheaper than reaching for the dropdown.
    $(document).on('click', '.funnel-stage', function () {
        var stage = $(this).data('stage') || '';

        state.stage = (state.stage === stage) ? '' : stage;
        state.page = 1;
        $('#stageFilter').val(state.stage);
        load();
    });

    // ------------------------------------------------------------------
    // Load
    // ------------------------------------------------------------------
    function load() {
        var url = API_BASE_URL + '/pipeline' +
            '?page=' + state.page +
            '&pageSize=' + state.pageSize +
            '&search=' + encodeURIComponent(state.search) +
            '&stage=' + encodeURIComponent(state.stage);

        $.ajax({ url: url, method: 'GET' })
            .done(function (res) {
                var d = res && res.data;

                if (!d) {
                    setEmpty(res && res.message ? res.message : 'Nothing to show.');
                    return;
                }

                $('#scopeLine').text(
                    'Everyone in ' + d.scope + ' and where their journey has reached.');

                if (window.TeamLeadShell) TeamLeadShell.setSubtitle(d.scope);

                renderFunnel(d.summary);
                renderRows(d.items);
                renderPager(d);

                $('#resultCount').text(d.totalCount + (d.totalCount === 1 ? ' person' : ' people'));
            })
            .fail(function (xhr) {
                setEmpty(errorFrom(xhr));
            });
    }

    // ------------------------------------------------------------------
    // Funnel
    // ------------------------------------------------------------------
    function renderFunnel(s) {
        s = s || {};

        // Journey order, so the drop-off reads as a shape. "Not started" sits at
        // the front because a person on file with no case is the very first kind
        // of person to fall through.
        var stages = [
            { key: '',                   label: 'Not started', count: s.noCase || 0 },
            { key: 'INTAKE',             label: 'Intake',      count: s.intake || 0 },
            { key: 'INITIAL_FOLLOW_UP',  label: 'First contact', count: s.initialFollowUp || 0 },
            { key: 'NURTURE',            label: 'Nurture',     count: s.nurture || 0 },
            { key: 'REVIEW',             label: 'Review',      count: s.review || 0 },
            { key: 'CLOSED',             label: 'Closed',      count: s.closed || 0 }
        ];

        $('#funnel').html(stages.map(function (st) {
            // "Not started" has no stage to filter by, so it is shown but not
            // clickable — a button that does nothing is worse than plain text.
            var clickable = st.key !== '';

            return '<' + (clickable ? 'button' : 'div') + ' class="funnel-stage' +
                       (clickable && state.stage === st.key ? ' is-active' : '') + '"' +
                       (clickable ? ' data-stage="' + st.key + '"' : ' style="cursor:default;"') + '>' +
                       '<div class="funnel-count">' + st.count + '</div>' +
                       '<div class="funnel-label">' + esc(st.label) + '</div>' +
                   '</' + (clickable ? 'button' : 'div') + '>';
        }).join(''));
    }

    // ------------------------------------------------------------------
    // Rows
    // ------------------------------------------------------------------
    function renderRows(items) {
        if (!items || !items.length) {
            setEmpty('Nobody matches that.');
            return;
        }

        $('#pipelineTable tbody').html(items.map(function (r) {
            var stage = r.stage || 'none';

            var stagePill = '<span class="stage-pill stage-' + esc(stage) + '">' +
                                esc(pretty(r.stage) || 'Not started') + '</span>' +
                            (r.hasOpenEscalation
                                ? ' <span class="tl-badge tl-badge-red" title="Paused by an open escalation">paused</span>'
                                : '');

            // The bar only means something in the nurture stage; elsewhere the
            // step number is not a position in anything.
            var progress = r.nurtureProgress
                ? '<div class="progress-wrap">' +
                      '<div class="progress-track"><div class="progress-fill" style="width:' +
                          Math.round((r.currentStepNumber / Math.max(1, r.planStepCount)) * 100) + '%"></div></div>' +
                      '<span class="cell-muted">' + esc(r.nurtureProgress) + '</span>' +
                  '</div>'
                : '<span class="cell-muted">—</span>';

            // Weeks without contact is the number that says "nobody has touched
            // this person", which no stage label conveys on its own.
            var days = r.daysSinceContact;
            var lastContact = r.lastContactAt
                ? formatDate(r.lastContactAt)
                : '<span class="cell-muted">never</span>';

            var age = (days === null || days === undefined || r.stage === 'CLOSED')
                ? ''
                : ' <span class="' + (days >= 30 ? 'stale' : 'cell-muted') + '">(' + days + 'd)</span>';

            return '<tr>' +
                '<td>' +
                    '<div class="cell-name">' + esc(r.personName) + '</div>' +
                    '<div class="cell-muted">' + esc(r.phone || '—') + '</div>' +
                '</td>' +
                '<td class="cell-ref">' + esc(r.caseReference || '—') + '</td>' +
                '<td>' + stagePill + '</td>' +
                '<td>' +
                    '<div>' + esc(r.volunteerName || '—') + '</div>' +
                    (r.teamName ? '<div class="cell-muted">' + esc(r.teamName) + '</div>' : '') +
                '</td>' +
                '<td>' + progress + '</td>' +
                '<td>' + lastContact + age + '</td>' +
            '</tr>';
        }).join(''));
    }

    function renderPager(d) {
        var pages = Math.max(1, Math.ceil(d.totalCount / d.pageSize));

        if (pages <= 1) { $('#pager').empty(); return; }

        $('#pager').html(
            '<button class="tl-secondary" id="prevPage"' + (d.page <= 1 ? ' disabled' : '') + '>Previous</button>' +
            '<span class="cell-muted">Page ' + d.page + ' of ' + pages + '</span>' +
            '<button class="tl-secondary" id="nextPage"' + (d.page >= pages ? ' disabled' : '') + '>Next</button>');

        $('#prevPage').on('click', function () { state.page--; load(); window.scrollTo(0, 0); });
        $('#nextPage').on('click', function () { state.page++; load(); window.scrollTo(0, 0); });
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    function setEmpty(message) {
        $('#pipelineTable tbody').html(
            '<tr><td colspan="6" class="tl-empty">' + esc(message) + '</td></tr>');
        $('#pager').empty();
    }

    function pretty(code) {
        if (!code) return null;
        return code.charAt(0) + code.slice(1).toLowerCase().replace(/_/g, ' ');
    }

    function formatDate(value) {
        if (!value) return '—';
        var d = new Date(value);
        return isNaN(d) ? '—' : d.toLocaleDateString();
    }

    function esc(value) {
        return String(value === null || value === undefined ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function errorFrom(xhr) {
        if (xhr && xhr.status === 403) return 'You do not have access to this.';

        var b = xhr && xhr.responseJSON;
        return (b && (b.message || b.detail || b.title)) || 'Could not load the pipeline.';
    }
});
