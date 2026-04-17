$(function () {
    $('#loadMetrics').on('click', function () {
        const teamLeadId = $('#teamLeadId').val();
        if (!teamLeadId) { alert('Enter team lead id'); return; }
        loadMetrics(teamLeadId);
        // show huddle button if today is Friday
        const today = new Date();
        if (today.getDay() === 5) { // Friday (0=Sun,5=Fri)
            $('#teamHuddleBtn').show();
        } else {
            $('#teamHuddleBtn').hide();
        }
    });

    //----TeamLeads Screen

    $("#saveBtn").click(function () {

        var data = {
            FirstName: $("#first_name").val().trim(),
            LastName: $("#last_name").val().trim(),
            Email: $("#email").val().trim(),
            Phone: $("#phone").val().trim(),
            RoleType: $("#role_type").val() || "TeamLead",
            MaxVolunteers: parseInt($("#max_volunteers").val()) || 0
        };

        console.log("DATA:", data);

        if (!data.FirstName || !data.LastName || !data.Phone) {
            $("#message").text("First Name, Last Name and Phone are required")
                .addClass("error");
            return;
        }

        $.ajax({
            url: API_BASE_URL + '/TeamLeadDashBoards/save-team-lead',
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify(data),

            success: function (res) {
                console.log("SUCCESS:", res);

                if (res.responseType === 1) {
                    $("#message").text(res.message)
                        .removeClass("error").addClass("success");
                } else {
                    $("#message").text(res.message)
                        .removeClass("success").addClass("error");
                }
            },

            error: function (err) {
                console.error("ERROR FULL:", err);

                if (err.status === 404) {
                    $("#message").text("API route not found (404)").addClass("error");
                }
                else if (err.status === 500) {
                    $("#message").text("Server error (500)").addClass("error");
                }
                else {
                    $("#message").text("Unknown error").addClass("error");
                }
            }
        });
    });

    function loadMetrics(teamLeadId) {
        $.ajax({
            url: API_BASE_URL + '/TeamLeadDashBoards/team-metrics?teamLeadId=' + encodeURIComponent(teamLeadId),
            method: 'GET',
            success: function (res) {
                if (!res || !res.data) {
                    $('#alerts').html('<div class="alert alert-warning">No data</div>');
                    return;
                }
                renderMetrics(res.data, teamLeadId);
            },
            error: function () {
                $('#alerts').html('<div class="alert alert-danger">Error loading metrics</div>');
            }
        });
    }

    function renderMetrics(data, teamLeadId) {
        $('#dashboardTitle').text(`TEAM LEAD DASHBOARD - ${data.teamLeadName || data.TeamLeadName || ''}`);
        $('#dashboardSubtitle').text(data.weekOf || data.WeekOf || '');

        const tp = data.teamPerformance || data.TeamPerformance;
        $('#teamPerformance').html(`
            <div>Total Follow-ups: <strong>${tp.total_follow_ups}</strong></div>
            <div>Completed: <strong>${tp.completed}</strong></div>
            <div>Normal: <strong>${tp.normal}</strong></div>
            <div>Needs Follow-Up: <strong>${tp.needs_follow_up}</strong></div>
            <div>Crisis: <strong>${tp.crisis}</strong></div>
            <div>Avg Duration: <strong>${tp.avg_duration}</strong> min</div>
        `);

        const volunteers = data.volunteers || data.Volunteers || [];
        const rows = volunteers.map(v => `
            <tr>
                <td class='v-name' data-id='${v.VolunteerId || v.volunteerId}'>${v.name || v.Name}</td>
                <td>${v.capacityBand || v.CapacityBand}</td>
                <td>${v.thisWeek || v.ThisWeek}</td>
                <td>${v.trend || v.Trend}</td>
                <td>${v.flag || v.Flag}</td>
            </tr>
        `).join('');
        $('#volunteersTable tbody').html(rows);

        const attention = data.attentionNeeded || data.AttentionNeeded || [];
        const att = attention.map(a => `<li class="list-group-item">${a.volunteer || a.Volunteer} - ${a.message || a.Message} <span class="badge bg-secondary ms-2">${a.priority || a.Priority}</span></li>`).join('');
        $('#attentionList').html(att);

        const cis = (data.upcomingCheckIns || data.UpcomingCheckIns || [])
            .map(c => {
                const checkInDate = new Date(c.next_check_in);
                const today = new Date();

                today.setHours(0, 0, 0, 0);
                checkInDate.setHours(0, 0, 0, 0);

                let message = '';

                if (checkInDate < today) {
                    message = `${c.first_name} ${c.last_name} – Check-in Overdue`;
                } else {
                    message = `${c.first_name} ${c.last_name} – Monthly Check-in On: (${c.day_of_week})`;
                }

                return `
        <dt class='checkin-ele'  
            data-id='${c.volunteer_id}' 
            data-teamLeadId='${teamLeadId}'
            data-volunteerName='${c.first_name} ${c.last_name}'
            data-teamLeadName='${data.teamLeadName || data.TeamLeadName}'
            style="cursor:pointer;">
            ${message}
        </dt>
        <dd>
            Date: ${checkInDate.toDateString()}
        </dd>
        `;
            }).join('');

        const escalationsData = (data.escalationsPending || data.EscalationsPending || []);
        const escalations = escalationsData.map(e => {
            const day = new Date(e.escalationDate)
                .toLocaleDateString('en-US', { weekday: 'short' });

            const reason = e.escalationReason || e.reason || e.description || 'no details';

            return `
            <div class="escalation-item"
                 data-id="${e.escalationId}"
                 style="cursor:pointer; padding:6px; border-radius:5px;">
                - ${e.personName} (escalated ${day}, ${reason})
            </div>
        `;
        }).join('');

        const escalationCount = escalationsData.length;

        const finalHtml = `
        <h5>Check-ins</h5>
        <dl class="mb-3">
            ${cis || '<dd>No upcoming check-ins</dd>'}
        </dl>

        <h5>Escalations Pending</h5>
        <h6 class="text-muted">
            ${escalationCount} case${escalationCount !== 1 ? 's' : ''} need Team Lead Follow-up:
        </h6>

        <div>
            ${escalations || 'No pending escalations'}
        </div>
        `;

        $('#checkinsList').html(finalHtml);
    }

    $(document).on('click', '.checkin-ele', function () {
        const id = $(this).data('id');
        const teamLeadId = $(this).data('teamleadid');
        const vName = $(this).data('volunteername');
        const tName = $(this).data('teamleadname');

        goToCheckIns(id, teamLeadId, vName, tName);
    });

    function goToCheckIns(id, teamLeadId, vName, tName) {
        window.location.href =
            `CheckIns.html?id=${id}&teamLeadId=${teamLeadId}&vName=${encodeURIComponent(vName)}&tName=${encodeURIComponent(tName)}`;
    }

    function openEscalation(id) {
        window.location.href = `Escalations.html?id=${id}`;
    }

    $(document).on('click', '.escalation-item', function () {
        const id = $(this).data('id');
        openEscalation(id);
    });

    function openVolunteerDashboard(id) {
        window.location.href = `../Volunteers/Assignments.html?volunteerid=${id}`;
    }

    $(document).on('click', '.v-name', function () {
        const id = $(this).data('id');
        openVolunteerDashboard(id);
    });

    $(document).on('click', '#teamHuddleBtn', function () {
        const teamLeadId = $('#teamLeadId').val();
        if (!teamLeadId) return alert('TeamLeadId required');
        // call DTO endpoint to get rich data
        $.ajax({
            url: API_BASE_URL + '/TeamLeadDashBoards/team-huddle/dto?teamLeadId=' + encodeURIComponent(teamLeadId),
            method: 'GET',
            success: function (res) {
                if (!res || !res.data) {
                    $('#huddleTable tbody').html('<tr><td colspan="8">No data</td></tr>');
                } else {
                    const rows = res.data.map(r => `
                        <tr>
                            <td>${r.volunteerId || r.VolunteerId}</td>                        
                            <td>${r.personFirstName || r.PersonFirstName} ${r.personLastName || r.PersonLastName}</td>
                         
                            <td>${r.contactStatus || r.ContactStatus}</td>
                            <td>${r.responseType || r.ResponseType}</td>
                        <td>${formatDate(r.attemptDate || r.AttemptDate)}</td>
                            <td>${r.notes || r.Notes || ''}</td>
                        </tr>
                    `).join('');
                    $('#huddleTable tbody').html(rows);
                }

                var modal = new bootstrap.Modal(document.getElementById('teamHuddleModal'));
                modal.show();
            },
            error: function () { alert('Error loading team huddle follow-ups'); }
        });
    });

    function formatDate(date) {
        const d = new Date(date);
        const day = String(d.getDate()).padStart(2, '0');
        const month = String(d.getMonth() + 1).padStart(2, '0');
        const year = d.getFullYear();
        return `${day}-${month}-${year}`;
    }
});