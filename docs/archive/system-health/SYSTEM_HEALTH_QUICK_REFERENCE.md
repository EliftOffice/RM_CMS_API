# System Health Endpoint - Quick Reference

## Endpoint Summary

```
GET /api/pastors/system-health
```

## 30-Second Overview

The `GetSystemHealth` endpoint returns real-time system metrics including:
- Active volunteers & team leads count
- First-time visitors this month
- Follow-ups completed this month
- Volunteer satisfaction (vNPS) score
- Volunteer retention rate
- Follow-up completion rate
- **Overall health status** (🟢 HEALTHY / 🟡 NEEDS ATTENTION / 🔴 AT RISK)

## Files Implemented

```
✅ Controllers/Pastors/PastorsController.cs          → API endpoint
✅ BLL/Pastors/IPastorService.cs                    → Service interface
✅ BLL/Pastors/PastorService.cs                     → Service logic
✅ DAL/Pastors/PastorDashboard.cs                   → Database queries
✅ Data/DTO/SystemHealthDTO.cs                      → Response model
✅ Program.cs                                       → DI registration
```

## Quick Test

### Using Swagger
1. Start application
2. Go to `https://localhost:7096/swagger`
3. Find **Pastors** section
4. Click **GET /api/pastors/system-health**
5. Click **Try it out** → **Execute**

### Using cURL
```bash
curl https://localhost:7096/api/pastors/system-health
```

### Using JavaScript
```javascript
$.get('/api/pastors/system-health', function(response) {
    console.log(response.data.overallHealthStatus);
});
```

## Response Example

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

## Health Status Meanings

| Status | Meaning | Action |
|--------|---------|--------|
| 🟢 HEALTHY | All metrics exceed thresholds | Continue monitoring |
| 🟡 NEEDS ATTENTION | Some metrics below optimal | Review processes |
| 🔴 AT RISK | Multiple critical metrics low | Urgent action needed |

## Database Queries

The endpoint aggregates data from 4 core tables:

| Table | Used For | Key Field |
|-------|----------|-----------|
| `volunteers` | Active count, vNPS, retention | status, vnps_score, start_date |
| `team_leads` | Leadership capacity | status |
| `people` | Visitor metrics | visit_type, first_visit_date |
| `follow_ups` | Completion rates | contact_status, attempt_date |

## Thresholds for Health Status

### 🟢 HEALTHY
- vNPS ≥ 50
- Retention ≥ 90%
- Completion ≥ 85%

### 🟡 NEEDS ATTENTION
- vNPS ≥ 40
- Retention ≥ 85%
- Completion ≥ 80%

### 🔴 AT RISK
- Anything below NEEDS ATTENTION threshold

## Architecture Pattern

Same 3-layer pattern as other endpoints:

```
PastorsController
    ↓ (HTTP layer)
PastorService
    ↓ (Business logic)
PastorDashboard
    ↓ (Data access)
MySQL Database
```

## Common Issues & Solutions

| Issue | Solution |
|-------|----------|
| 404 Error | Verify endpoint: `/api/pastors/system-health` |
| null values | Check volunteer table has `vnps_score` field |
| All zeros | Normal if month just started; check historical data |
| Database error | Verify tables: volunteers, team_leads, people, follow_ups |

## Integration Tips

1. **Dashboard Widget:** Display health status on admin dashboard
2. **Alerts:** Send notification when status becomes 🔴
3. **Trends:** Call hourly and store results for historical analysis
4. **Thresholds:** Adjust health status thresholds in `PastorDashboard.cs`

## Next Steps

1. Test the endpoint (see "Quick Test" above)
2. Integrate into your admin dashboard
3. Set up monitoring/alerts for 🔴 status
4. Create historical trending reports
5. Customize thresholds based on your organization

## Related Documentation

- **Full Implementation:** See `SYSTEM_HEALTH_IMPLEMENTATION.md`
- **Testing Guide:** See `SYSTEM_HEALTH_TESTING.md`
- **Complete Guide:** See `SYSTEM_HEALTH_COMPLETE_GUIDE.md`

---

✅ **Status:** Ready to use | Build: Successful | All tests: Passing
