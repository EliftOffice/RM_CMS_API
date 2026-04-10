//Assignments.js File

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
        }).replace(/ /g, ' '); // e.g. "10 Apr 2026"
    }

    // ── Load assignments ──────────────────────────────────────────────────────
    function loadAssignments() {
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

                const today = new Date();
                today.setHours(0, 0, 0, 0);

                const filtered = res.data.filter(p => {
                    const status = p.followUpStatus?.toUpperCase();
                    if (status === 'ASSIGNED') return true;
                    if (status === 'RETRY PENDING' && p.nextActionDate) {
                        const nextDate = new Date(p.nextActionDate);
                        nextDate.setHours(0, 0, 0, 0);
                        return nextDate <= today;
                    }
                    return false;
                });

                // Update section label with count
                $('#listLabel').text(`My List (${filtered.length})`);

                const cards = filtered.map(p => {
                    const status = p.followUpStatus?.toUpperCase();
                    const name = `${p.firstName || ''} ${p.lastName || ''}`.trim();
                    const phone = p.phone || '-';
                    const assignedDate = fmtDate(p.assignedDate);

                    let statusClass = '';
                    let actionButton = '';
                    let retryText = '';

                    // CASE 1: ASSIGNED
                    if (status === 'ASSIGNED') {
                        statusClass = 'status-pending';
                        actionButton = `
                            <button class="action-btn btn-start start-followup"
                                data-person="${p.personId}" data-personname="${name}">
                                Start Follow-up
                            </button>`;
                    }

                    // CASE 2: RETRY PENDING
                    else if (status === 'RETRY PENDING') {
                        const nextDateObj = new Date(p.nextActionDate);
                        nextDateObj.setHours(0, 0, 0, 0);
                        const isToday = nextDateObj.getTime() === today.getTime();

                        if (isToday) {
                            statusClass = 'status-contacted';
                            actionButton = `
                                <button class="action-btn btn-update update-status"
                                    data-person="${p.personId}" data-personname="${name}">
                                    Update Status
                                </button>`;
                        }

                        if (p.attemptDate) {
                            const attemptDate = new Date(p.attemptDate);
                            attemptDate.setHours(0, 0, 0, 0);
                            const diffDays = Math.floor((today - attemptDate) / (1000 * 60 * 60 * 24));
                            if (diffDays >= 0) {
                                retryText = `
                                    <div class="retry-text">
                                        Contacted ${diffDays} day${diffDays !== 1 ? 's' : ''} ago
                                    </div>`;
                            }
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
                                <span class="assign-date">${assignedDate}</span>
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

    // ── Open modal on action button click ────────────────────────────────────
    $(document).on('click', '.action-btn', function () {
        const personId = $(this).data('person');
        const name = $(this).data('personname');
        $('#person_id').val(personId);
        $('#volunteer_id').val(volunteerId);
        $('#followUpModal .modal-title').text(`Log Follow-up: ${name} (${personId})`);
        var modal = new bootstrap.Modal(document.getElementById('followUpModal'));
        modal.show();
    });

    // ── Logout ────────────────────────────────────────────────────────────────
    $(document).on('click', '.logout-btn', function () {
        setTimeout(() => { window.location.href = 'Login.html'; }, 500);
    });

    // ── Toggle response type based on contact status ──────────────────────────
    $('#contact_status').on('change', function () {
        if ($(this).val() === 'Contacted') {
            $('#responseTypeGroup').show();
        } else {
            $('#responseTypeGroup').hide();
        }
    }).trigger('change');

    // ── Save follow-up ────────────────────────────────────────────────────────
    $('#saveFollowUp').on('click', function () {
        const payload = {
            person_id: $('#person_id').val(),
            volunteer_id: $('#volunteer_id').val(),
            contact_method: $('#contact_method').val(),
            contact_status: $('#contact_status').val(),
            response_type: $('#response_type').val(),
            call_duration_min: parseInt($('#call_duration_min').val() || '0'),
            notes: $('#notes').val(),
            tags: $('#tags').val(),
            team_lead_id: 'TL001'
        };

        $.ajax({
            url: API_BASE_URL + '/followups/log-followup',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload),
            success: function () {
                $('#modalResponse').html('<div class="alert alert-success">Follow-up logged successfully.</div>');
                loadAssignments();
                setTimeout(() => { $('#followUpModal .btn-close').click(); }, 1000);
            },
            error: function () {
                $('#modalResponse').html('<div class="alert alert-danger">Error logging follow-up.</div>');
            }
        });
    });
});