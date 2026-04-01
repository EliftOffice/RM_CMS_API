# Person to Volunteer Assignment API - Implementation Guide

## Overview
This document describes the implementation of the assignment algorithm that assigns people to available volunteers based on capacity and campus criteria.

## API Endpoint

### Assign Person to Volunteer
**POST** `/api/peoples/{personId}/assign`

**Parameters:**
- `personId` (string, required, path parameter): The unique identifier of the person to assign

**Response Format:** `ApiResponse<object>`

**Success Response (200):**
```json
{
  "responseType": 0,
  "message": "Person successfully assigned to volunteer",
  "data": {
    "personId": "P001",
    "personName": "John Doe",
    "email": "john@example.com",
    "phone": "9876543210",
    "campus": "Ongole",
    "assignedVolunteerId": "V001",
    "volunteerName": "Ravi Kumar",
    "volunteerEmail": "ravi.kumar@example.com",
    "volunteerPhone": "+91-9876543210",
    "volunteerCapacityMax": 8,
    "volunteerCurrentAssignments": 3,
    "assignedDate": "2026-03-31T10:30:00Z",
    "nextActionDate": "2026-04-02T10:30:00Z",
    "status": "ASSIGNED"
  }
}
```

**Warning Response (400) - No Available Volunteer:**
```json
{
  "responseType": 1,
  "message": "No available volunteers with capacity for assignment",
  "data": {
    "personId": "P001",
    "campus": "Ongole"
  }
}
```

**Error Response (400/500):**
```json
{
  "responseType": 2,
  "message": "Error description",
  "data": null
}
```

## Architecture

### 1. DAL Layer (Data Access Layer)

#### **IPeopleRepository** interface methods:
- `GetPersonByIdAsync(string personId)` - Retrieves person details
- `UpdatePersonAssignmentAsync(string personId, string volunteerId, DateTime nextActionDate)` - Updates person record with assignment

#### **IVolunteerRepository** interface methods:
- `GetAvailableVolunteerAsync(string campus)` - Finds the least-loaded active volunteer with available capacity
- `UpdateCurrentAssignmentsAsync(string volunteerId)` - Increments volunteer's current assignments count

### 2. BLL Layer (Business Logic Layer)

#### **IPeopleService.AssignPersonToVolunteerAsync(string personId)**
Orchestrates the assignment process:
1. Validates person exists
2. Checks if person is already assigned
3. Validates person has a campus assigned
4. Finds available volunteer for the campus
5. Updates person assignment
6. Updates volunteer workload
7. Returns detailed assignment response

### 3. Controller Layer

#### **PeoplesController.AssignPersonToVolunteer(string personId)**
- Validates input parameters
- Calls the BLL service
- Handles responses and HTTP status codes
- Includes logging for debugging

## Data Flow

```
HTTP POST Request
    ↓
Controller validates input
    ↓
BLL Service orchestrates logic:
    ├─→ DAL: Get person details
    ├─→ Check person validity
    ├─→ DAL: Find available volunteer (least-loaded, same campus)
    ├─→ DAL: Update person assignment
    ├─→ DAL: Update volunteer assignments
    ↓
Returns ApiResponse object
    ↓
HTTP Response
```

## Assignment Algorithm Details

### Volunteer Selection Criteria:
1. **Status:** Must be "Active"
2. **Capacity:** `current_assignments < capacity_max`
3. **Campus:** Must match person's campus
4. **Selection Order:** 
   - Primary: Least number of current assignments (load balancing)
   - Secondary: Random order among volunteers with same assignment count (fairness)

### Next Action Date:
- Calculated as current UTC time + 48 hours
- Represents the target contact deadline per requirement

### Data Updates:
1. **People table:**
   - `assigned_volunteer` = selected volunteer ID
   - `assigned_date` = current timestamp
   - `follow_up_status` = "ASSIGNED"
   - `next_action_date` = current + 48 hours
   - `updated_at` = current timestamp

2. **Volunteers table:**
   - `current_assignments` = incremented by 1
   - `updated_at` = current timestamp

## Error Handling

The implementation includes comprehensive error handling:

1. **Person Not Found:** Returns error response
2. **Person Already Assigned:** Returns warning response
3. **Campus Missing:** Returns error response
4. **No Available Volunteers:** Returns warning response with campus info
5. **Database Update Failures:** Returns error response
6. **Exception Handling:** Try-catch blocks capture all exceptions

## Usage Example

```bash
# Assign person P001 to an available volunteer
curl -X POST http://localhost:5000/api/peoples/P001/assign
```

## Response Types

```csharp
public enum ResponseType
{
    Success = 0,   // Operation completed successfully
    Warning = 1,   // Operation completed with warnings (e.g., no capacity)
    Error = 2      // Operation failed
}
```

## Database Considerations

### Indexes Used:
- `people.person_id` (PRIMARY KEY)
- `volunteers.volunteer_id` (PRIMARY KEY)
- `volunteers.status` (helps filter Active volunteers)
- `volunteers.campus` (helps filter by campus)
- `volunteers.current_assignments` (helps identify available capacity)

### Query Optimization:
- Uses parameterized queries to prevent SQL injection
- LIMIT 1 reduces data transfer for volunteer selection
- Indexes on filtering columns improve query performance

## Future Enhancements

1. **Notification Service Integration:** Email/SMS notifications to volunteers
2. **Transaction Support:** Wrap multiple database operations in transactions
3. **Logging Service:** Detailed logging of assignment events
4. **Assignment History:** Track assignment changes and reasoning
5. **Fairness Metrics:** Track assignment distribution over time
6. **Team Lead Alerts:** Notification when no capacity available
