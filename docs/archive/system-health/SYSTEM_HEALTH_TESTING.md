# System Health Endpoint - Testing Guide

## Quick Test

### cURL
```bash
curl -X GET "https://localhost:7096/api/pastors/system-health" \
  -H "Content-Type: application/json"
```

### PowerShell
```powershell
$response = Invoke-RestMethod -Uri "https://localhost:7096/api/pastors/system-health" `
  -Method GET `
  -ContentType "application/json"

$response | ConvertTo-Json -Depth 10
```

### JavaScript/jQuery
```javascript
// From the web interface
$.ajax({
    type: 'GET',
    url: 'https://localhost:7096/api/pastors/system-health',
    contentType: 'application/json',
    success: function (response) {
        console.log('System Health:', response);
        console.log('Status:', response.data.overallHealthStatus);
    },
    error: function (xhr) {
        console.error('Error:', xhr.responseJSON.message);
    }
});
```

### Swagger UI
1. Navigate to `https://localhost:7096/swagger`
2. Find **Pastors** section
3. Click **GET /api/pastors/system-health**
4. Click **Try it out**
5. Click **Execute**

## Response Interpretation

### Success Response (200 OK)
```json
{
  "responseType": "Success",
  "message": "System health metrics retrieved successfully",
  "data": {
    "activeVolunteers": 45,
    "activeTeamLeads": 12,
    "firstTimeVisitorsMTD": 128,
    "followUpsCompletedMTD": 342,
    "systemvNPS": 52,
    "volunteerRetention": 92.5,
    "completionRateMTD": 87.3,
    "overallHealthStatus": "🟢 HEALTHY"
  }
}
```

**Interpretation:**
- All metrics are available
- System is in **HEALTHY** status
- 45 active volunteers are available
- 92.5% retention rate indicates good volunteer stability
- 87.3% completion rate shows effective follow-up execution

### Warning Response (200 OK with Warning Status)
```json
{
  "responseType": "Success",
  "message": "System health metrics retrieved successfully",
  "data": {
    "activeVolunteers": 12,
    "activeTeamLeads": 3,
    "firstTimeVisitorsMTD": 45,
    "followUpsCompletedMTD": 98,
    "systemvNPS": 42,
    "volunteerRetention": 86.5,
    "completionRateMTD": 81.2,
    "overallHealthStatus": "🟡 NEEDS ATTENTION"
  }
}
```

**Interpretation:**
- System requires attention
- Volunteer count is low (12)
- Completion rate at 81.2% is below ideal (85%+)
- Retention at 86.5% is stable but below optimal
- Action: Review volunteer recruitment and follow-up processes

### At-Risk Response (200 OK with At-Risk Status)
```json
{
  "responseType": "Success",
  "message": "System health metrics retrieved successfully",
  "data": {
    "activeVolunteers": 5,
    "activeTeamLeads": 1,
    "firstTimeVisitorsMTD": 12,
    "followUpsCompletedMTD": 18,
    "systemvNPS": 35,
    "volunteerRetention": 72.0,
    "completionRateMTD": 65.5,
    "overallHealthStatus": "🔴 AT RISK"
  }
}
```

**Interpretation:**
- System health is critical
- Very few volunteers available (5)
- vNPS score (35) is below minimum threshold (40)
- Retention at 72% indicates significant volunteer churn
- Completion rate at 65.5% is significantly below target
- **Urgent Action Required:** Address volunteer burnout, recruitment, and follow-up capacity

### Error Response (400 Bad Request)
```json
{
  "responseType": "Error",
  "message": "An error occurred while retrieving system health metrics",
  "data": null
}
```

**Causes:**
- Database connection failure
- SQL query error
- Invalid database state

**Resolution:**
1. Check database connectivity
2. Review database logs
3. Verify all required tables exist:
   - `volunteers`
   - `team_leads`
   - `people`
   - `follow_ups`

## Metrics Breakdown

### Active Volunteers
- **What it measures:** Number of volunteers currently marked as "Active"
- **Why it matters:** Indicates available capacity for person assignments
- **Healthy range:** 20+

### Active Team Leads
- **What it measures:** Number of team leads currently active in the system
- **Why it matters:** Indicates management capacity for volunteer oversight
- **Healthy range:** 5+

### First-Time Visitors MTD
- **What it measures:** New visitors in current calendar month
- **Why it matters:** Indicates outreach effectiveness and church growth metrics
- **Healthy target:** 50+ per month

### Follow-Ups Completed MTD
- **What it measures:** Successful follow-up contacts completed in current month
- **Why it matters:** Core metric for care delivery and visitor experience
- **Healthy target:** 200+ per month

### System vNPS
- **What it measures:** Average Volunteer Net Promoter Score
- **Range:** 0-100
- **Healthy threshold:** 50+
- **Why it matters:** Volunteer satisfaction and retention indicator

### Volunteer Retention
- **What it measures:** Percentage of volunteers from 3 months ago still active
- **Calculation:** Active volunteers from 3+ months ago / Total volunteers from 3+ months ago
- **Healthy threshold:** 90%+
- **Why it matters:** Indicates volunteer sustainability and program effectiveness

### Completion Rate MTD
- **What it measures:** Percentage of follow-ups successfully contacted this month
- **Calculation:** Contacted follow-ups / Total follow-ups × 100
- **Healthy threshold:** 85%+
- **Why it matters:** Measures program execution and care delivery effectiveness

## Automated Monitoring

To monitor system health regularly, create a scheduled job:

```csharp
// In Program.cs or a background service
public class HealthMonitoringService : BackgroundService
{
    private readonly IPastorService _pastorService;
    private readonly ILogger<HealthMonitoringService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var health = await _pastorService.GetSystemHealthAsync();
                _logger.LogInformation($"System Health: {health.Data.OverallHealthStatus}");
                
                // Alert if at risk
                if (health.Data.OverallHealthStatus.Contains("AT RISK"))
                {
                    _logger.LogWarning("System health is at risk!");
                    // Send alert to admins
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking system health");
            }

            // Check every 6 hours
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }
}
```

## Troubleshooting

### Getting 404 Error
- Verify endpoint path: `/api/pastors/system-health`
- Ensure PastorsController is in the Pastors folder
- Confirm application has been rebuilt

### Getting null data values
- Check if required database tables are populated
- Verify volunteer table has `vnps_score` field
- Ensure `start_date` field exists on volunteers

### Metrics showing as 0
- May indicate no data in the current month
- Normal for first day of month
- Check historical data in Swagger

## Next Steps

1. **Integrate into Dashboard:** Add system health widget to administrative dashboard
2. **Set Alerts:** Configure alerts for "AT RISK" status
3. **Historical Tracking:** Log health metrics for trend analysis
4. **Custom Thresholds:** Adjust health status thresholds based on your organization's goals
