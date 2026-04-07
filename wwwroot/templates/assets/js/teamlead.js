$(function () {
    $('#loadMetrics').on('click', function () {
        const teamLeadId = $('#teamLeadId').val();
        if (!teamLeadId) { alert('Enter team lead id'); return; }
        loadMetrics(teamLeadId);
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
                renderMetrics(res.data);
            },
            error: function () {
                $('#alerts').html('<div class="alert alert-danger">Error loading metrics</div>');
            }
        });
    }

    function renderMetrics(data) {
        // Header
        $('#dashboardTitle').text(`TEAM LEAD DASHBOARD - ${data.teamLeadName || data.TeamLeadName || ''}`);
        $('#dashboardSubtitle').text(data.weekOf || data.WeekOf || '');

        // Team Performance
        const tp = data.teamPerformance || data.TeamPerformance;
        $('#teamPerformance').html(`
            <div>Total Follow-ups: <strong>${tp.total_follow_ups}</strong></div>
            <div>Completed: <strong>${tp.completed}</strong></div>
            <div>Normal: <strong>${tp.normal}</strong></div>
            <div>Needs Follow-Up: <strong>${tp.needs_follow_up}</strong></div>
            <div>Crisis: <strong>${tp.crisis}</strong></div>
            <div>Avg Duration: <strong>${tp.avg_duration}</strong> min</div>
        `);

        // Volunteers (handle different casing)
        const volunteers = data.volunteers || data.Volunteers || [];
        const rows = volunteers.map(v => `
            <tr>
                <td>${v.name || v.Name}</td>
                <td>${v.capacityBand || v.CapacityBand}</td>
                <td>${v.thisWeek || v.ThisWeek}</td>
               
                <td>${v.trend || v.Trend}</td>
                <td>${v.flag || v.Flag}</td>
            </tr>
        `).join('');
        $('#volunteersTable tbody').html(rows);

        // Attention
        const attention = data.attentionNeeded || data.AttentionNeeded || [];
        const att = attention.map(a => `<li class="list-group-item">${a.volunteer || a.Volunteer} - ${a.message || a.Message} <span class="badge bg-secondary ms-2">${a.priority || a.Priority}</span></li>`).join('');
        $('#attentionList').html(att);

        // Checkins
        // Checkins Section
        const cis = (data.upcomingCheckIns || data.UpcomingCheckIns || [])
            .map(c => `
        <dt>${c.first_name} ${c.last_name}</dt>
        <dd>
            Date: ${new Date(c.next_check_in).toDateString()} <br/>
            Day: ${c.day_of_week}
        </dd>
    `).join('');

        // Escalations Section
        //    const escalations = (data.escalationsPending || data.EscalationsPending || [])
        //        .map(e => `
        //    <dt>${e.personName}</dt>
        //    <dd>
        //        Volunteer: ${e.volunteerName} <br/>
        //        Tier: ${e.escalationTier} <br/>
        //        Status: ${e.status} <br/>
        //        Date: ${new Date(e.escalationDate).toDateString()} <br/>
        //        Days Pending: ${e.daysPending}
        //    </dd>
        //`).join('');

        const escalationsData = (data.escalationsPending || data.EscalationsPending || []);

        const escalations = escalationsData
            .map(e => {
                const day = new Date(e.escalationDate)
                    .toLocaleDateString('en-US', { weekday: 'short' });

                return `- ${e.personName} (escalated ${day}, ${e.reason || e.description || 'no details'})`;
            })
            .join('<br/>');

        // ✅ Count
        const escalationCount = escalationsData.length;

        // Final HTML
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

        // Render
        $('#checkinsList').html(finalHtml);
    }
});
