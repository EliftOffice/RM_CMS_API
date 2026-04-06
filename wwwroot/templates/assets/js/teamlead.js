$(function(){
    $('#loadMetrics').on('click', function(){
        const teamLeadId = $('#teamLeadId').val();
        if(!teamLeadId) { alert('Enter team lead id'); return; }
        loadMetrics(teamLeadId);
    });

    function loadMetrics(teamLeadId){
        $.ajax({
            url: API_BASE_URL + '/TeamLeadDashBoards/team-metrics?teamLeadId=' + encodeURIComponent(teamLeadId),
            method: 'GET',
            success: function(res){
                if(!res || !res.data){
                    $('#alerts').html('<div class="alert alert-warning">No data</div>');
                    return;
                }
                renderMetrics(res.data);
            },
            error: function(){
                $('#alerts').html('<div class="alert alert-danger">Error loading metrics</div>');
            }
        });
    }

    function renderMetrics(data){
        // Team Performance
        const tp = data.teamPerformance;
        $('#teamPerformance').html(`
            <div>Follow-ups: <strong>${tp.total_follow_ups}</strong></div>
            <div>Completed: <strong>${tp.completed}</strong></div>
            <div>Avg Duration: <strong>${tp.avg_duration}</strong> min</div>
        `);

        // Volunteers
        const rows = data.volunteers.map(v => `
            <tr>
                <td>${v.name}</td>
                <td>${v.capacityBand}</td>
                <td>${v.thisWeek}</td>
                <td>${v.lastWeek}</td>
                <td>${v.trend}</td>
                <td>${v.flag}</td>
            </tr>
        `).join('');
        $('#volunteersTable tbody').html(rows);

        // Attention
        const att = data.attentionNeeded.map(a => `<li class="list-group-item">${a.volunteer} - ${a.message} <span class="badge bg-secondary ms-2">${a.priority}</span></li>`).join('');
        $('#attentionList').html(att);

        // Checkins
        const cis = data.upcomingCheckIns.map(c => `<li class="list-group-item">${c.first_name} ${c.last_name} - ${new Date(c.next_check_in).toDateString()} (${c.day_of_week})</li>`).join('');
        $('#checkinsList').html(cis);
    }
});
