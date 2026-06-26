# System Health Endpoint Implementation

## Overview
Implemented the `GetSystemHealth` endpoint following the same architecture pattern as the `AssignPersonToVolunteer` function. This endpoint retrieves comprehensive system health metrics across the RM CMS application.

## Architecture Pattern
The implementation follows the standard 3-layer architecture:

```
Controller (PastorsController)
         ↓
BLL (PastorService)
         ↓
DAL (PastorDashboard)
         ↓
Database
```

## Files Created/Modified

### 1. **Controllers/Pastors/PastorsController.cs** (NEW)
- Endpoint: `GET /api/pastors/system-health`
- Returns: `ApiResponse<SystemHealthDTO>`
- Follows the same try-catch pattern as PeoplesController
- Includes logging for monitoring

### 2. **BLL/Pastors/IPastorService.cs** (NEW)
- Interface: `IPastorService`
- Method: `GetSystemHealthAsync()`
- Defines the business logic contract

### 3. **BLL/Pastors/PastorService.cs** (NEW)
- Implementation of `IPastorService`
- Delegates to the DAL layer (PastorDashboard)
- Handles exception management

### 4. **DAL/Pastors/PastorDashboard.cs** (UPDATED)
- Implements `IPastorDashboard` interface
- Method: `GetSystemHealthAsync()`
- Contains SQL queries to calculate all health metrics
- Determines overall health status based on thresholds

### 5. **Data/DTO/SystemHealthDTO.cs** (UPDATED)
- Added property: `OverallHealthStatus` (string)
- Changed numeric types to nullable (int?, double?) for accurate calculations
- Properties:
  - `ActiveVolunteers` - Count of active volunteers
  - `ActiveTeamLeads` - Count of active team leads
  - `FirstTimeVisitorsMTD` - First-time visitors in current month
  - `FollowUpsCompletedMTD` - Follow-ups marked as contacted in current month
  - `SystemvNPS` - Average vNPS score
  - `VolunteerRetention` - Percentage of volunteers retained from 3 months ago
  - `CompletionRateMTD` - Percentage of follow-ups completed this month
  - `OverallHealthStatus` - Health indicator (🟢 HEALTHY, 🟡 NEEDS ATTENTION, 🔴 AT RISK)

### 6. **Program.cs** (UPDATED)
- Registered `IPastorDashboard` → `PastorDashboard`
- Registered `IPastorService` → `PastorService`
- Added to dependency injection container

## System Health Calculations

### Active Volunteers
```sql
SELECT COUNT(*) FROM volunteers WHERE status = 'Active'
```

### Active Team Leads
```sql
SELECT COUNT(*) FROM team_leads WHERE status = 'Active'
```

### First-Time Visitors MTD
```sql
SELECT COUNT(*) FROM people 
WHERE visit_type = 'First-Time Visitor'
  AND DATE_FORMAT(first_visit_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
```

### Follow-Ups Completed MTD
```sql
SELECT COUNT(*) FROM follow_ups 
WHERE contact_status = 'Contacted'
  AND DATE_FORMAT(attempt_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
```

### System vNPS
```sql
SELECT AVG(vnps_score) FROM volunteers WHERE vnps_score IS NOT NULL
```

### Volunteer Retention
```sql
SELECT (COUNT(CASE WHEN status = 'Active' THEN 1 END) * 100.0 / COUNT(*))
FROM volunteers
WHERE start_date <= DATE_SUB(CURDATE(), INTERVAL 3 MONTH)
```

### Completion Rate MTD
```sql
SELECT (SUM(CASE WHEN contact_status = 'Contacted' THEN 1 ELSE 0 END) * 100.0 / COUNT(*))
FROM follow_ups
WHERE DATE_FORMAT(attempt_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
```

## Health Status Thresholds

### 🟢 HEALTHY
- vNPS ≥ 50
- Retention ≥ 90%
- Completion Rate ≥ 85%

### 🟡 NEEDS ATTENTION
- vNPS ≥ 40
- Retention ≥ 85%
- Completion Rate ≥ 80%

### 🔴 AT RISK
- Any condition not meeting the above thresholds

## API Usage

### Request
```http
GET /api/pastors/system-health
```

### Response (Success)
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

### Response (Error)
```json
{
  "responseType": "Error",
  "message": "An error occurred while retrieving system health metrics",
  "data": null
}
```

## Integration with Frontend

The web interface can be updated to call this endpoint:

```javascript
// Add to main.js or create a dashboard-module.js
$.ajax({
    type: 'GET',
    url: `${API_BASE_URL}/pastors/system-health`,
    contentType: 'application/json',
    success: function (response) {
        displayResponse('healthContainer', 'info', 'System Health', response.data);
    },
    error: function (xhr) {
        let errorMessage = 'Failed to retrieve system health';
        if (xhr.responseJSON && xhr.responseJSON.message) {
            errorMessage = xhr.responseJSON.message;
        }
        displayResponse('healthContainer', 'error', errorMessage, xhr.responseJSON);
    }
});
```

## Testing

To test the endpoint:

1. Build the solution (✅ Successful)
2. Start the application
3. Navigate to: `https://localhost:7096/api/pastors/system-health`
4. Verify the response contains all health metrics with appropriate status

## Notes

- All queries use MySQL-specific functions (DATE_FORMAT, DATE_SUB, CURDATE())
- The endpoint returns aggregate data; individual entity details are available through other endpoints
- Health calculations are based on month-to-date (MTD) metrics
- The nullable properties allow proper handling of cases where no data is available
