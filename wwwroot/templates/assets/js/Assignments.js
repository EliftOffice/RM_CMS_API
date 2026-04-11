$(document).ready(function () {
    const urlParams = new URLSearchParams(window.location.search);
    const volunteerId = urlParams.get('volunteerid');
    $('#volunteerIdDisplay').text(volunteerId || '-');
    if (!volunteerId) return;

    // ── helper: "10 Apr 2026" format ──────────────────────────────────────────
    function fmtDate(dateStr) {
        if (!dateStr) return '-';
        const d = new Date(dateStr);
        if (isNaN(d)) return '-';
        return d.toLocaleDateString('en-GB', {
            day: '2-digit',
            month: 'short',
            year: 'numeric'
        }).replace(/ /g, ' ');
    }

    // ── PAGE SWITCHING ────────────────────────────────────────────────────────
    function showAssignmentsPage() {
        $('#followupPage').hide();
        $('#assignmentsPage').show();
        window.scrollTo(0, 0);
    }

    function showFollowupPage(data) {
        // Populate hidden fields
        $('#person_id').val(data.personId || '');
        $('#volunteer_id').val(volunteerId || '');

        // Populate person card
        const initial = (data.name || 'V').trim()[0].toUpperCase();
        $('#avatarInitial').text(initial);
        $('#displayPersonName').text(data.name || '');
        $('#displayPhone').text(data.phone || '');

        // Update top bar title
        $('#followupPage .followup-topbar-title').text('Update Status');

        // Reset form state
        $('#cs_yes').prop('checked', true);
        $('input[name="response_type"]').prop('checked', false);
        $('#call_duration_min').val('');
        $('#notes').val('');
        $('#modalResponse').html('');
        $('#responseTypeSection').show();
        $('#submitFollowupBtn').prop('disabled', false).text('Submit');

        // Switch pages
        $('#assignmentsPage').hide();
        $('#followupPage').show();
        window.scrollTo(0, 0);
    }

    // Back buttons
    $('#topBackBtn, #bottomBackBtn').on('click', showAssignmentsPage);

    // ── Load assignments ──────────────────────────────────────────────────────
    function loadAssignments() {
        $('#assignmentsGrid').html("");
        $.ajax({
            url: API_BASE_URL + `/volunteers/${volunteerId}/assignments`,
            method: 'GET',

            success: function (res) {
                if (!res || !res.data || res.data.length === 0) {
                    $('#responseContainer').html(
                        '<div class="alert alert-warning">No assignments found.</div>'

                    );
                    return;
                }

                const filtered = res.data.filter(p => {
                    const status = p.followUpStatus?.toUpperCase();
                    if (status === 'ASSIGNED') return true;
                    if (status === 'RETRY PENDING') return true;
                    return false;
                });

                $('#listLabel').text(`My List (${filtered.length})`);

                const cards = filtered.map(p => {
                    const status = p.followUpStatus?.toUpperCase();
                    const name = `${p.firstName || ''} ${p.lastName || ''}`.trim();
                    const phone = p.phone || '-';
                    const assignedDate = fmtDate(p.assignedDate);
                    const nextActionDate = fmtDate(p.nextActionDate);

                    let statusClass = '';
                    let actionButton = '';
                    let retryText = '';

                    // CASE 1: ASSIGNED
                    if (status === 'ASSIGNED') {
                        statusClass = 'status-pending';
                        actionButton = `
                        <button class="action-btn btn-start start-followup"
                            data-personid="${p.personId}"
                            data-name="${name}"
                            data-phone="${phone}">
                            Start Follow-up
                        </button>`;
                    }

                    // CASE 2: RETRY PENDING
                    else if (status === 'RETRY PENDING') {
                        statusClass = 'status-contacted';
                        actionButton = `
                        <button class="action-btn btn-update update-status"
                            data-personid="${p.personId}"
                            data-name="${name}"
                            data-phone="${phone}">
                            Update Status
                        </button>`;

                        if (p.attemptDate) {
                            retryText = `
                            <div class="retry-text">
                                Last contacted on ${fmtDate(p.attemptDate)}
                            </div>`;
                        }
                    }

                    return `
                    <div class="assignment-card">
                        <div class="card-header">
                            <div>
                                <div class="person-name">${name}</div>
                                <div class="phone">${phone}</div>
                            </div>
                            <span class="status-badge ${statusClass}">
                                ${status === 'ASSIGNED' ? 'PENDING' : status}
                            </span>
                        </div>
                        ${retryText}
                        <div class="card-footer">
                            <span class="assign-date">Due date: ${nextActionDate}</span>
                            ${actionButton}
                        </div>
                    </div>`;
                }).join('');

                $('#assignmentsGrid').html(cards);
            },

            error: function (xhr) {
                $('#responseContainer').html(
                    `<div class="alert alert-danger">Error: ${xhr.responseText}</div>`
                );
            }
        });
    }

    // ── Load volunteer header ─────────────────────────────────────────────────
    function loadVolunteerHeader() {
        $.ajax({
            url: API_BASE_URL + `/volunteers/${volunteerId}`,
            method: 'GET',
            success: function (res) {
                if (!res || !res.data) return;
                const v = res.data;
                const fullName = `${v.firstName || ''} ${v.lastName || ''}`.trim();
                $('#spnVolunteerName').text(fullName || 'N/A');
                $('#spnteamLead').text(v.teamLeadFullName || 'N/A');
            },
            error: function () {
                $('#spnVolunteerName').text('Error');
                $('#spnteamLead').text('Error');
            }
        });
    }

    loadVolunteerHeader();
    loadAssignments();

    // ── ACTION BUTTON CLICK → Show Follow-up Page ─────────────────────────────
    // FIX: Instead of Bootstrap modal, switch to followupPage div
    $(document).on('click', '.action-btn', function () {
        showFollowupPage({
            personId: $(this).data('personid'),
            name: $(this).data('name'),
            phone: $(this).data('phone')
        });
    });

    // ── Show/Hide response type based on contact status ───────────────────────
    $(document).on('change', 'input[name="contact_status"]', function () {
        if ($(this).val() === 'Contacted') {
            $('#responseTypeSection').show();
        } else {
            $('#responseTypeSection').hide();
            $('input[name="response_type"]').prop('checked', false);
        }
    });

    // ── Submit follow-up ──────────────────────────────────────────────────────
    $('#submitFollowupBtn').on('click', function () {
        const contactStatus = $('input[name="contact_status"]:checked').val();
        const responseType = $('input[name="response_type"]:checked').val() || '';

        if (contactStatus === 'Contacted' && !responseType) {
            $('#modalResponse').html(
                '<div class="alert alert-warning mt-2">Please select a Response Type.</div>'
            );
            return;
        }

        const payload = {
            person_id: $('#person_id').val(),
            volunteer_id: $('#volunteer_id').val(),
            contact_method: 'Phone Call',
            contact_status: contactStatus,
            response_type: responseType,
            call_duration_min: parseInt($('#call_duration_min').val() || '0'),
            notes: $('#notes').val(),
            tags: '',
            team_lead_id: 'TL001'
        };

        const $btn = $(this);
        $btn.prop('disabled', true).text('Saving…');
        $('#modalResponse').html('');

        $.ajax({
            url: API_BASE_URL + '/followups/log-followup',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload),
            success: function () {
                $('#modalResponse').html(
                    '<div class="alert alert-success mt-2">Follow-up logged successfully.</div>'
                );
                loadAssignments();
                setTimeout(showAssignmentsPage, 1200);
            },
            error: function (xhr) {
                $('#modalResponse').html(
                    `<div class="alert alert-danger mt-2">Error: ${xhr.responseText || xhr.status}</div>`
                );
                $btn.prop('disabled', false).text('Submit');
            }
        });
    });

    // ── Logout ────────────────────────────────────────────────────────────────
    $(document).on('click', '.logout-btn, #logoutBtn', function () {
        setTimeout(() => { window.location.href = 'Login.html'; }, 400);
    });
});