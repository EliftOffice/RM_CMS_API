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
        $('#nurtureFollowupPage').hide();
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
                $('#hdntelegramchatId').val(v.telegramChatID || 0);
            },
            error: function () {
                $('#spnVolunteerName').text('Error');
                $('#spnteamLead').text('Error');
            }
        });
    }

    loadVolunteerHeader();
    loadAssignments();
    loadNurtureSteps(volunteerId);

    // ── ACTION BUTTON CLICK → Show Follow-up Page ─────────────────────────────
    // FIX: Instead of Bootstrap modal, switch to followupPage div
    $(document).on('click', '.action-btn', function () {
        if ($(this).data('personid') != null) {
            showFollowupPage({
                personId: $(this).data('personid'),
                name: $(this).data('name'),
                phone: $(this).data('phone')
            });
        }
        
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
        let responseType = $('input[name="response_type"]:checked').val() || '';

        if (contactStatus === 'Contacted' && !responseType) {
            $('#modalResponse').html(
                '<div class="alert alert-warning mt-2">Please select a Response Type.</div>'
            );
            return;
        }

        if (contactStatus === 'Not Contacted') {
            responseType = 'Not Contacted';
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
            team_lead_id: '',
            team_lead_name: $('#spnteamLead').text(),
            telegram_chat_id: $('#hdntelegramchatId').val(),
            volunteer_name: $('#spnVolunteerName').text()
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



    // ══════════════════════════════════════════
    // NURTURE STEPS — load and render
    // ══════════════════════════════════════════
    async function loadNurtureSteps(volunteerId) {
        try {
            const res = await fetch(`${API_BASE_URL}/nurture/volunteer/${volunteerId}/due`);
            const json = await res.json();
            const steps = json.data || [];
            const grid = document.getElementById('nurtureGrid');
            const title = document.getElementById('nurtureSectionTitle');
            grid.innerHTML = '';
            if (!steps.length) { title.style.display = 'none'; return; }
            title.style.display = 'block';
            steps.forEach(step => {
                const isOverdue = new Date(step.scheduledDate) < new Date(new Date().toDateString());
                const methodClass = step.method === 'Visit' ? 'visit' : '';
                const methodEmoji = step.method === 'Call' ? '📞' : '🏠';
                grid.innerHTML += `
                <div class="nurture-card">
                    <div class="card-header">
                        <div>
                            <div class="person-name">${step.personName}</div>
                            <div class="phone">${step.personPhone}</div>
                        </div>
                        <span class="nurture-badge">Step ${step.stepNumber}/7</span>
                    </div>
                    <div style="margin-top:8px;display:flex;align-items:center;gap:8px">
                        <span class="nurture-method-badge ${methodClass}">${methodEmoji} ${step.method}</span>
                        ${isOverdue ? '<span class="overdue-tag">Overdue</span>' : ''}
                        <span style="font-size:11px;color:#9ca3af">Due: ${new Date(step.scheduledDate).toLocaleDateString()}</span>
                    </div>
                    <div class="card-footer">
                        <span class="assign-date"></span>
                        <button class="action-btn btn-start"
                            onclick="openNurtureStep('${step.stepId}','${step.sequenceId}','${step.personId}','${step.personName}','${step.personPhone}',${step.stepNumber},'${step.method}')">
                            Log Step
                        </button>
                    </div>
                </div>`;
            });
        } catch (e) { console.error('Nurture steps error', e); }
    }

    function closeNurturePage() {
        document.getElementById('nurtureFollowupPage').style.display = 'none';
        document.getElementById('followupPage').style.display = 'none';
        document.getElementById('assignmentsPage').style.display = 'block';
    }

    document.getElementById('nurtureBackBtn').addEventListener('click', closeNurturePage);
    document.getElementById('nurtureBottomBackBtn').addEventListener('click', closeNurturePage);

    document.getElementById('nc_no').addEventListener('change', function () {
        document.getElementById('nurtureResponseSection').style.display = this.checked ? 'none' : 'block';
    });
    document.getElementById('nc_yes').addEventListener('change', function () {
        document.getElementById('nurtureResponseSection').style.display = 'block';
    });

    document.getElementById('submitNurtureStepBtn').addEventListener('click', async function () {
        const btn = this;
        btn.disabled = true; btn.textContent = 'Submitting...';
        const contactStatus = document.querySelector('input[name="nurture_contact"]:checked')?.value;
        const responseType = document.querySelector('input[name="nurture_response"]:checked')?.value;
        const payload = {
            stepId: document.getElementById('nurture_step_id').value,
            sequenceId: document.getElementById('nurture_sequence_id').value,
            personId: document.getElementById('nurture_person_id').value,
            volunteerId: document.getElementById('nurture_volunteer_id').value,
            contactStatus,
            responseType: contactStatus === 'Contacted' ? responseType : null,
            notes: document.getElementById('nurture_notes').value
        };
        try {
            const res = await fetch(`${API_BASE_URL}/api/nurture/step/log`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            const json = await res.json();
            const msgDiv = document.getElementById('nurtureModalResponse');
            if (json.responseType === 0) {
                msgDiv.innerHTML = '<div class="alert alert-success mt-2">Step logged successfully!</div>';
                setTimeout(() => { closeNurturePage(); const vId = localStorage.getItem('volunteer_id'); if (vId) loadNurtureSteps(vId); }, 1200);
            } else {
                msgDiv.innerHTML = `<div class="alert alert-danger mt-2">${json.message}</div>`;
                btn.disabled = false; btn.textContent = 'Submit Step';
            }
        } catch (e) {
            document.getElementById('nurtureModalResponse').innerHTML = '<div class="alert alert-danger mt-2">Network error</div>';
            btn.disabled = false; btn.textContent = 'Submit Step';
        }
    });

  
});
function openNurtureStep(stepId, sequenceId, personId, personName, personPhone, stepNum, method) {   
    document.getElementById('nurture_step_id').value = stepId;
    document.getElementById('nurture_sequence_id').value = sequenceId;
    document.getElementById('nurture_person_id').value = personId;
    document.getElementById('nurture_volunteer_id').value = localStorage.getItem('volunteer_id') || '';
    document.getElementById('nurturePersonName').textContent = personName;
    document.getElementById('nurturePersonPhone').textContent = personPhone;
    document.getElementById('nurtureAvatarInitial').textContent = personName.charAt(0).toUpperCase();
    document.getElementById('nurtureStepBadge').textContent = `Step ${stepNum}/7`;
    const mb = document.getElementById('nurtureMethodBadge');
    mb.textContent = (method === 'Call' ? '📞 Call' : '🏠 Visit');
    mb.className = 'nurture-method-badge' + (method === 'Visit' ? ' visit' : '');
    document.getElementById('assignmentsPage').style.display = 'none';
    document.getElementById('followupPage').style.display = 'none';
    document.getElementById('nurtureFollowupPage').style.display = 'block';
    window.scrollTo(0, 0);
}