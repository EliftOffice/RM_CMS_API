// Manual assignment — placing a case with a volunteer by hand.
//
// Rewired to the v2 API. The screen looks the same; three things changed underneath:
//
//   1. It lists CASES, not people. In v2 a person and their follow-up journey are
//      separate records, and it is the case that gets assigned. The table still
//      shows the person's name and phone, because that is what a team lead reads.
//   2. Identity comes from the token. The old page carried ?teamleadid= in the URL
//      and in every request.
//   3. The volunteer list shows EVERYONE active with their load, not only those
//      with room. Manual assignment exists precisely for the times the automatic
//      picker finds nobody — so it must be possible to place a case deliberately,
//      with the cost visible, rather than the dropdown simply being empty.

$(document).ready(function () {

    var selected = null;   // the case being assigned: { id, rowVersion, personName }

    $('#back_tl').attr('href', '../TeamLeads/TeamLeadDashboard.html');

    $('#signOutBtn').on('click', function (e) {
        e.preventDefault();
        if (window.RmAuth && RmAuth.logout) { RmAuth.logout(); return; }
        window.location.href = '../Volunteers/Login.html';
    });

    loadCases();

    // ------------------------------------------------------------------
    // The queue
    // ------------------------------------------------------------------
    function loadCases() {
        $.get(API_BASE_URL + '/cases?unassigned=true&pageSize=100')
            .done(function (res) {
                var items = (res && res.data && res.data.items) || [];

                $('#queueCount').text(items.length
                    ? items.length + (items.length === 1 ? ' case' : ' cases')
                    : '');

                if (!items.length) {
                    $('#manualTable tbody').html(
                        '<tr><td colspan="4" class="empty-row">' +
                        'Nothing is waiting for assignment.</td></tr>');
                    return;
                }

                $('#manualTable tbody').html(items.map(function (c) {
                    return '<tr>' +
                        '<td class="cell-ref">' + esc(c.referenceCode || '—') + '</td>' +
                        '<td class="cell-name">' + esc(c.personName) + '</td>' +
                        '<td class="cell-muted">' + esc(c.personPhone || '—') + '</td>' +
                        '<td style="text-align:right;">' +
                          '<button class="btn-assign assign-btn" ' +
                                  'data-id="' + esc(c.id) + '" ' +
                                  'data-name="' + esc(c.personName) + '">Assign</button>' +
                        '</td>' +
                    '</tr>';
                }).join(''));
            })
            .fail(function (xhr) { toastError(getError(xhr)); });
    }

    // ------------------------------------------------------------------
    // Choose a volunteer
    // ------------------------------------------------------------------
    $(document).on('click', '.assign-btn', function () {
        var id = $(this).data('id');
        var name = $(this).data('name');

        // The case is re-read rather than trusted from the row: assignment needs the
        // current row version, and the row may have been rendered minutes ago.
        $.get(API_BASE_URL + '/cases/' + encodeURIComponent(id))
            .done(function (res) {
                if (!res || !res.data) { toastError('That case could not be loaded.'); return; }

                selected = { id: id, rowVersion: res.data.rowVersion, personName: name };

                $('#assignPersonId').val(id);
                $('#capacityInfo').removeClass('over-capacity').text('—');

                loadVolunteers();

                new bootstrap.Modal(document.getElementById('assignModal')).show();
            })
            .fail(function (xhr) { toastError(getError(xhr)); });
    });

    function loadVolunteers() {
        $.get(API_BASE_URL + '/volunteers?status=ACTIVE&pageSize=200')
            .done(function (res) {
                var items = (res && res.data && res.data.items) || [];
                var sel = $('#volunteerSelect').empty();

                sel.append('<option value="">Select a volunteer</option>');

                // Least loaded first, so the sensible choice is nearest the top even
                // though every choice stays available.
                items.sort(function (a, b) {
                    return (a.currentCaseLoad - a.capacityMaxPerWeek) -
                           (b.currentCaseLoad - b.capacityMaxPerWeek);
                });

                items.forEach(function (v) {
                    var full = v.currentCaseLoad >= v.capacityMaxPerWeek;

                    sel.append(
                        '<option value="' + esc(v.id) + '" ' +
                                'data-load="' + v.currentCaseLoad + '" ' +
                                'data-max="' + v.capacityMaxPerWeek + '">' +
                            esc(v.fullName) +
                            ' (' + v.currentCaseLoad + '/' + v.capacityMaxPerWeek + ')' +
                            (full ? ' — at capacity' : '') +
                        '</option>');
                });

                if (!items.length) {
                    sel.append('<option value="" disabled>No active volunteers on this campus.</option>');
                }
            })
            .fail(function (xhr) { toastError(getError(xhr)); });
    }

    $(document).on('change', '#volunteerSelect', function () {
        var opt = this.options[this.selectedIndex];

        if (!opt || !opt.value) { $('#capacityInfo').text('-'); return; }

        var load = parseInt(opt.getAttribute('data-load'), 10);
        var max = parseInt(opt.getAttribute('data-max'), 10);

        var over = load >= max;

        $('#capacityInfo')
            .toggleClass('over-capacity', over)
            .text(over
                ? load + ' of ' + max + ' — already at capacity. Assigning will put them over.'
                : load + ' of ' + max + ' — ' + (max - load) + ' free');
    });

    // ------------------------------------------------------------------
    // Assign
    // ------------------------------------------------------------------
    $('#confirmAssign').on('click', function () {
        if (!selected) return;

        var volunteerId = $('#volunteerSelect').val();

        if (!volunteerId) { toastError('Choose a volunteer first.'); return; }

        $.ajax({
            url: API_BASE_URL + '/cases/' + encodeURIComponent(selected.id) + '/assign',
            method: 'PUT',
            contentType: 'application/json',
            data: JSON.stringify({
                volunteerId: volunteerId,
                reason: 'MANUAL',
                handoverNote: 'Assigned by hand from the manual assignment screen.',
                rowVersion: selected.rowVersion
            })
        })
        .done(function (res) {
            // A refusal comes back as HTTP 200 with responseType 1, not an HTTP
            // error — reporting it as success would tell a team lead the case is
            // placed when it is not.
            if (res && res.responseType !== 0) {
                toastError(res.message || 'That could not be assigned.');
                return;
            }

            toastOk(res.message || 'Assigned.');
            bootstrap.Modal.getInstance(document.getElementById('assignModal')).hide();
            selected = null;
            loadCases();
        })
        .fail(function (xhr) { toastError(getError(xhr)); });
    });

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    function esc(v) {
        return String(v === null || v === undefined ? '' : v)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function toastOk(m) {
        if (typeof showToast === 'function') showToast(m, 'success'); else alert(m);
    }

    function toastError(m) {
        if (typeof showToast === 'function') showToast(m, 'error'); else alert(m);
    }

    function getError(xhr) {
        var b = xhr && xhr.responseJSON;
        return (b && (b.message || b.detail || b.title)) || 'Something went wrong.';
    }
});
