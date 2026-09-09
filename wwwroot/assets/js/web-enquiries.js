/*
 * Website enquiries — the coordinator's queue.
 *
 *   GET /api/web-enquiries                 the queue, with counts
 *   GET /api/web-enquiries/{id}            one enquiry
 *   PUT /api/web-enquiries/{id}/status     pick up / done / close / spam
 *
 * Everything on this screen was typed by a stranger into a form on the public
 * website. It is deliberately NOT a church record yet: the whole point of this
 * queue is that a person reads each one and decides what it becomes. That is why
 * the detail view says so out loud, and why a mobile number that matches somebody
 * already on file is called out before any decision is made.
 *
 * ADMIN and WEB_COORDINATOR only. A coordinator has this screen and nothing else —
 * every other route in the application returns 403 for them.
 */
$(function () {
    'use strict';

    var esc = AdminShell.escapeHtml;

    var state = {
        page: 1,
        pageSize: 50,
        status: '',
        formType: '',
        search: '',
        includeSpam: false,
        current: null      // the enquiry open in the modal
    };

    var debounce = null;

    AdminShell.boot({
        roles: ['ADMIN', 'WEB_COORDINATOR'],
        active: { href: '/pages/admin/web-enquiries.html', area: 'Website' }
    }).then(function (ok) {
        if (!ok) return;
        $('#pageBody').prop('hidden', false);
        loadFormTypes();
        bind();
        load();
    });

    // ------------------------------------------------------------------ data

    /**
     * The form list comes from the server rather than being hard-coded, so a form
     * added to the website appears here without a matching edit in this file —
     * and a filter can never offer a value the API would reject.
     */
    function loadFormTypes() {
        $.ajax({ url: API_BASE_URL + '/public/enquiries/form-types', method: 'GET' })
            .done(function (res) {
                var types = (res && res.data) || [];

                $('#formFilter').append(types.map(function (t) {
                    return '<option value="' + esc(t.code) + '">' + esc(t.label) + '</option>';
                }).join(''));
            });
    }

    function load() {
        $.ajax({
            url: API_BASE_URL + '/web-enquiries',
            method: 'GET',
            data: {
                page: state.page,
                pageSize: state.pageSize,
                status: state.status,
                formType: state.formType,
                search: state.search,
                includeSpam: state.includeSpam
            }
        })
            .done(function (res) {
                // A refusal arrives as HTTP 200 with responseType 1.
                if (!res || res.responseType !== 0 || !res.data) {
                    setEmpty((res && res.message) || 'Could not load the queue.');
                    return;
                }

                renderCounts(res.data.summary || {});
                renderRows(res.data.items || []);
                renderPager(res.data);

                $('#resultCount').text(
                    res.data.totalCount + (res.data.totalCount === 1 ? ' enquiry' : ' enquiries'));
            })
            .fail(function (xhr) {
                setEmpty(xhr && xhr.status === 403
                    ? 'You do not have access to website enquiries.'
                    : 'Could not load the queue.');
            });
    }

    // ------------------------------------------------------------------ render

    /**
     * The counters double as the status filter. "New" is first and coloured,
     * because it is the only one that means somebody has to do something.
     */
    function renderCounts(s) {
        var cards = [
            { key: 'NEW',       label: 'New',        value: s.new || 0,      highlight: true },
            { key: 'IN_REVIEW', label: 'In review',  value: s.inReview || 0 },
            { key: 'ACTIONED',  label: 'Done',       value: s.actioned || 0 },
            { key: 'CLOSED',    label: 'Closed',     value: s.closed || 0 },
            { key: 'SPAM',      label: 'Spam',       value: s.spam || 0 },
            { key: '',          label: 'Last 24h',   value: s.today || 0, readonly: true }
        ];

        $('#counts').html(cards.map(function (c) {
            // The "last 24 hours" tile is a fact, not a filter — a button that
            // filters by nothing would just clear the others unexpectedly.
            if (c.readonly) {
                return '<div class="count-card" style="cursor:default;">' +
                           '<div class="count-value">' + c.value + '</div>' +
                           '<div class="count-label">' + esc(c.label) + '</div>' +
                       '</div>';
            }

            return '<button type="button" class="count-card' +
                       (c.highlight ? ' is-new' : '') +
                       (state.status === c.key ? ' is-active' : '') +
                       '" data-status="' + esc(c.key) + '">' +
                       '<div class="count-value">' + c.value + '</div>' +
                       '<div class="count-label">' + esc(c.label) + '</div>' +
                   '</button>';
        }).join(''));
    }

    function renderRows(items) {
        if (!items.length) {
            setEmpty(state.search || state.status || state.formType
                ? 'Nothing matches that.'
                : 'Nothing has been submitted through the website yet.');
            return;
        }

        $('#enquiryTable tbody').html(items.map(function (e) {
            var who = e.fullName
                ? '<div class="cell-name">' + esc(e.fullName) + '</div>' +
                  (e.mobile ? '<div class="cell-sub">' + esc(e.mobile) + '</div>' : '')
                : '<span class="cell-sub">Anonymous</span>';

            // A prayer request has no name; its substance is the message. A
            // registration has no message; its substance is where they live.
            var said = e.message
                ? esc(e.message)
                : [e.city, e.street, e.landmark].filter(Boolean).map(esc).join(', ') || '—';

            return '<tr' + (e.isSuspectedSpam ? ' class="row-spam"' : '') + '>' +
                '<td class="cell-sub" style="font-family:ui-monospace,Menlo,Consolas,monospace;">' +
                    esc(e.referenceCode || '—') + '</td>' +
                '<td><span class="form-chip chip-' + esc(e.formType) + '">' +
                    esc(e.formLabel) + '</span>' +
                    (e.isSuspectedSpam
                        ? ' <span class="badge badge-warn" title="The honeypot field was filled in">bot</span>'
                        : '') + '</td>' +
                '<td>' + who + '</td>' +
                '<td><div class="cell-msg">' + said + '</div></td>' +
                '<td class="cell-sub">' + esc(formatDate(e.submittedAt)) + '</td>' +
                '<td>' + statusBadge(e.status) + '</td>' +
                '<td class="cell-actions">' +
                    '<button type="button" class="btn btn-sm" data-open="' + esc(e.id) + '">Open</button>' +
                '</td>' +
            '</tr>';
        }).join(''));
    }

    function statusBadge(status) {
        var cls = status === 'NEW' ? 'badge-role'
                : status === 'IN_REVIEW' ? 'badge-warn'
                : status === 'ACTIONED' ? 'badge-ok'
                : 'badge-off';

        return '<span class="badge ' + cls + '">' + esc(pretty(status)) + '</span>';
    }

    function renderPager(d) {
        var pages = Math.max(1, Math.ceil(d.totalCount / d.pageSize));

        if (pages <= 1) { $('#pager').empty(); return; }

        $('#pager').html(
            '<button class="btn btn-sm" id="prevPage"' + (d.page <= 1 ? ' disabled' : '') + '>Previous</button>' +
            '<span class="pager-info">Page ' + d.page + ' of ' + pages + '</span>' +
            '<button class="btn btn-sm" id="nextPage"' + (d.page >= pages ? ' disabled' : '') + '>Next</button>');
    }

    // ------------------------------------------------------------------ detail

    function open(id) {
        $.ajax({ url: API_BASE_URL + '/web-enquiries/' + encodeURIComponent(id), method: 'GET' })
            .done(function (res) {
                if (!res || res.responseType !== 0 || !res.data) {
                    showToast((res && res.message) || 'Could not open that enquiry.', 'warning');
                    return;
                }

                state.current = res.data;
                renderDetail(res.data);
                $('#detailModal').addClass('open');
            })
            .fail(function () { showToast('Could not open that enquiry.', 'error'); });
    }

    function renderDetail(e) {
        $('#detailTitle').text(e.formLabel + ' · ' + (e.referenceCode || ''));
        $('#detailNotice').prop('hidden', true).text('');
        $('#reviewNote').val(e.reviewNote || '');

        // Called out before any decision: creating a second copy of somebody who is
        // already on file is the mistake this queue is most likely to produce.
        $('#matchWarn')
            .prop('hidden', !e.matchingPeople)
            .text(e.matchingPeople
                ? e.matchingPeople + ' person(s) already on file share this mobile number. ' +
                  'Check before recording them again.'
                : '');

        var rows = [
            ['Name', e.fullName],
            ['Mobile', e.mobile],
            ['Email', e.email],
            ['City', e.city],
            ['Street', e.street],
            ['Landmark', e.landmark],
            ['Referred by', e.referredByName],
            ['Their mobile', e.referredByMobile],
            ['Campus', e.campusName],
            ['Submitted', formatDateTime(e.submittedAt)],
            ['From page', e.sourcePage],
            ['Status', pretty(e.status)],
            ['Reviewed by', e.reviewedByName ? e.reviewedByName + ' · ' + formatDateTime(e.reviewedAt) : null],
            ['Linked to', e.linkedPersonName]
        ];

        // Whatever the website sent that has no column of its own.
        Object.keys(e.extra || {}).forEach(function (k) {
            rows.push([pretty(k), e.extra[k]]);
        });

        $('#detailFields').html(rows
            .filter(function (r) { return r[1]; })
            .map(function (r) {
                return '<div><dt>' + esc(r[0]) + '</dt><dd>' + esc(r[1]) + '</dd></div>';
            }).join(''));

        $('#messageWrap').prop('hidden', !e.message);
        $('#detailMessage').text(e.message || '');

        // Already picked up, so offering "I'm looking at it" again does nothing.
        $('#reviewBtn').prop('disabled', e.status === 'IN_REVIEW');
    }

    function setStatus(status) {
        var e = state.current;
        if (!e) return;

        var note = $.trim($('#reviewNote').val());

        // Checked here as well as on the server so the coordinator is told before
        // the round trip, not after it.
        if (!note && (status === 'ACTIONED' || status === 'CLOSED')) {
            notice('Add a short note saying what was done.');
            $('#reviewNote').trigger('focus');
            return;
        }

        $('.modal-foot .btn').prop('disabled', true);

        $.ajax({
            url: API_BASE_URL + '/web-enquiries/' + encodeURIComponent(e.id) + '/status',
            method: 'PUT',
            contentType: 'application/json',
            data: JSON.stringify({ status: status, note: note || null, rowVersion: e.rowVersion })
        })
            .done(function (res) {
                if (!res || res.responseType !== 0) {
                    notice((res && res.message) || 'That could not be saved.');
                    return;
                }

                showToast('Saved.', 'success');
                close();
                load();
            })
            .fail(function (xhr) {
                var b = xhr && xhr.responseJSON;
                notice((b && (b.message || b.detail || b.title)) || 'That could not be saved.');
            })
            .always(function () { $('.modal-foot .btn').prop('disabled', false); });
    }

    function close() {
        $('#detailModal').removeClass('open');
        state.current = null;
    }

    function notice(message) {
        $('#detailNotice').text(message).prop('hidden', false);
    }

    // ------------------------------------------------------------------ wiring

    function bind() {
        $('#search').on('input', function () {
            var value = this.value;
            clearTimeout(debounce);
            debounce = setTimeout(function () {
                state.search = value;
                state.page = 1;
                load();
            }, 300);
        });

        $('#formFilter').on('change', function () {
            state.formType = this.value;
            state.page = 1;
            load();
        });

        $('#includeSpam').on('change', function () {
            state.includeSpam = this.checked;
            state.page = 1;
            load();
        });

        $('#clearBtn').on('click', function () {
            state = $.extend(state, { page: 1, status: '', formType: '', search: '', includeSpam: false });
            $('#search').val('');
            $('#formFilter').val('');
            $('#includeSpam').prop('checked', false);
            load();
        });

        // Clicking the active counter clears the filter — the same gesture both ways.
        $(document).on('click', '[data-status]', function () {
            var status = $(this).data('status') || '';
            state.status = (state.status === status) ? '' : status;
            state.page = 1;
            load();
        });

        $(document).on('click', '[data-open]', function () { open($(this).data('open')); });
        $(document).on('click', '#prevPage', function () { state.page--; load(); });
        $(document).on('click', '#nextPage', function () { state.page++; load(); });
        $(document).on('click', '[data-close]', close);

        $('#reviewBtn').on('click', function () { setStatus('IN_REVIEW'); });
        $('#actionBtn').on('click', function () { setStatus('ACTIONED'); });
        $('#closeOffBtn').on('click', function () { setStatus('CLOSED'); });
        $('#spamBtn').on('click', function () { setStatus('SPAM'); });

        $('#detailModal').on('click', function (e) { if (e.target === this) close(); });
    }

    // ------------------------------------------------------------------ helpers

    function setEmpty(message) {
        $('#enquiryTable tbody').html(
            '<tr><td colspan="7" class="empty">' + esc(message) + '</td></tr>');
        $('#pager').empty();
    }

    function pretty(code) {
        if (!code) return '';
        return String(code).charAt(0).toUpperCase() +
               String(code).slice(1).toLowerCase().replace(/_/g, ' ');
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
});
