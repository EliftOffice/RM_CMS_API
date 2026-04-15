$(function(){
    function loadSystemHealth(){
        $.ajax({url: API_BASE_URL + '/Pastors/system-health', method: 'GET', success:function(res){
            if(!res || !res.data) { $('#systemHealth').html('<div class="text-danger">No data</div>'); return; }
            const d = res.data;
            $('#systemHealth').html(`
                <div>Active Volunteers: <strong>${d.active_volunteers}</strong></div>
                <div>Active Team Leads: <strong>${d.active_team_leads}</strong></div>
                <div>First-time visitors (MTD): <strong>${d.first_time_visitors_mtd}</strong></div>
                <div>Follow-ups completed (MTD): <strong>${d.follow_ups_completed_mtd}</strong></div>
                <div>System vNPS: <strong>${d.system_vnps}</strong></div>
                <div>Volunteer Retention: <strong>${d.volunteer_retention}</strong></div>
                <div>Completion Rate (MTD): <strong>${d.completion_rate_mtd}</strong></div>
                <div class="mt-2">Status: <strong>${d.OverallHealthStatus}</strong></div>
            `);
        }, error:function(){ $('#systemHealth').html('<div class="text-danger">Error</div>'); }});
    }

    function loadKPIs(){
        $.ajax({url: API_BASE_URL + '/Pastors/kpis', method: 'GET', success:function(res){
            if(!res || !res.data) { $('#kpis').html('<div class="text-danger">No data</div>'); return; }
            const d = res.data;
            $('#kpis').html(`
                <div class="kpi">Completion: ${d.completionRate.current} (${d.completionRate.status})</div>
                <div class="kpi">First Contact 48h: ${d.firstContact48h.current} (${d.firstContact48h.status})</div>
                <div class="kpi">Escalation Rate: ${d.escalationRate.current} (${d.escalationRate.status})</div>
                <div class="kpi">Crisis Safe: ${d.crisisHandledSafely.current} (${d.crisisHandledSafely.status})</div>
                <div class="kpi">Volunteer Retention: ${d.volunteerRetention.current} (${d.volunteerRetention.status})</div>
                <div class="kpi">System vNPS: ${d.systemVnps.current} (${d.systemVnps.status})</div>
            `);
        }, error:function(){ $('#kpis').html('<div class="text-danger">Error</div>'); }});
    }

    function loadTeamLeadPerformance(){
        $.ajax({url: API_BASE_URL + '/Pastors/team-lead-performance', method: 'GET', success:function(res){
            if(!res || !res.data) { $('#teamLeadTable tbody').html('<tr><td colspan="6">No data</td></tr>'); return; }
            const rows = res.data.map(t => `<tr>
                <td>${t.team_lead_name}</td>
                <td>${t.team_size}</td>
                <td>${t.completion_rate}</td>
                <td>${t.team_vnps}</td>
                <td>${t.retention_rate}</td>
                <td>${t.flag}</td>
            </tr>`).join('');
            $('#teamLeadTable tbody').html(rows);
        }, error:function(){ $('#teamLeadTable tbody').html('<tr><td colspan="6">Error</td></tr>'); }});
    }

    function loadPipeline(){
        $.ajax({url: API_BASE_URL + '/Pastors/pipeline-health', method: 'GET', success:function(res){
            if(!res || !res.data) { $('#pipeline').html('<div class="text-danger">No data</div>'); return; }
            const d = res.data;
            const html = d.stages.map(s => `<div>${s.follow_up_status}: <strong>${s.count}</strong> (${(s.percentage*100).toFixed(1)}%)</div>`).join('');
            $('#pipeline').html(html);
        }, error:function(){ $('#pipeline').html('<div class="text-danger">Error</div>'); }});
    }

    function loadAll(){ loadSystemHealth(); loadKPIs(); loadTeamLeadPerformance(); loadPipeline(); }

    $('#refreshNow').on('click', loadAll);

    let timer = null;
    $('#refreshInterval').on('change', function(){
        clearInterval(timer);
        const sec = parseInt($(this).val()||'0');
        if(sec>0) timer = setInterval(loadAll, sec*1000);
    });

    loadAll();
});
