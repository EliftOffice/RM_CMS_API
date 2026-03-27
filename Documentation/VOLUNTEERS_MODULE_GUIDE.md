# Volunteers Module - Implementation Guide (MySQL)

## Overview
The Volunteers Module manages the `volunteers` entity from the Level 1 Follow-Up System. This module provides complete CRUD operations and specialized endpoints for managing volunteer data, performance tracking, and health monitoring with **MySQL database**.

---

## Project Structure

```
RM_CMS/
??? Data/
?   ??? Models/
?   ?   ??? Volunteer.cs                 # Core Volunteer model
?   ??? DTO/
?   ?   ??? CreateVolunteerDto.cs        # DTO for creating volunteers
?   ?   ??? UpdateVolunteerDto.cs        # DTO for updating volunteers
?   ?   ??? VolunteerResponseDto.cs      # DTO for API responses
?   ??? DbConnection.cs                  # Database connection factory
??? DAL/
?   ??? Volunteers/
?       ??? VolunteerRepository.cs       # Data Access Layer
??? BLL/
?   ??? Volunteers/
?       ??? VolunteerService.cs          # Business Logic Layer
??? Controllers/
?   ??? Volunteers/
?       ??? VolunteersController.cs      # API Controller
??? Database/
    ??? SQL_Scripts/
        ??? 02_Create_Volunteers_Table.sql
```

---

## Architecture

### 3-Layer Architecture Pattern

```
???????????????????????????
?   Controller Layer      ?
? (VolunteersController)  ?
?  - HTTP Requests/Responses
?  - Input Validation
?  - Error Handling
???????????????????????????
             ?
???????????????????????????
?   Business Logic Layer  ?
? (VolunteerService)      ?
?  - Data Transformation
?  - Business Rules
?  - Pagination Logic
???????????????????????????
             ?
???????????????????????????
?   Data Access Layer     ?
? (VolunteerRepository)   ?
?  - MySQL Queries
?  - Dapper ORM
?  - Query Execution
???????????????????????????
             ?
             ?
        MySQL Database
```

---

## API Endpoints (15 Total)

### 1. Get All Volunteers
```
GET /api/volunteers
```
**Response:**
```json
[
  {
    "volunteerId": "V001",
    "firstName": "Sarah",
    "lastName": "Johnson",
    "email": "sarah.volunteer@church.com",
    "phone": "555-1001",
    "status": "Active",
    "level": "Level 1",
    "capacityBand": "Balanced",
    "currentAssignments": 3,
    "completionRate": 90.38,
    ...
  }
]
```

### 2. Get Volunteer by ID
```
GET /api/volunteers/{volunteerId}
```

### 3. Get Volunteers by Status
```
GET /api/volunteers/status/{status}
```
**Valid Status Values:**
- `Active` - Actively volunteering
- `Care Path` - On special support
- `Paused` - Temporarily paused
- `Exited` - No longer volunteering
- `Level 0` - Training level

### 4. Get Volunteers by Team Lead
```
GET /api/volunteers/team-lead/{teamLeadId}
```

### 5. Get Volunteers by Capacity Band
```
GET /api/volunteers/capacity/{capacityBand}
```
**Valid Capacity Bands:**
- `Consistent` - 4-6 per week
- `Balanced` - 2-3 per week
- `Limited` - 1-2 per week

### 6. Get Paginated Volunteers
```
GET /api/volunteers/paginated/data?pageNumber=1&pageSize=10
```

### 7. Get Active Volunteers Count
```
GET /api/volunteers/count/active
```

### 8. Get Volunteers with Low Completion Rate
```
GET /api/volunteers/alert/low-completion/{threshold}
```
Example: `GET /api/volunteers/alert/low-completion/75` returns volunteers below 75% completion

### 9. Get Total Count
```
GET /api/volunteers/count/total
```

### 10. Create Volunteer
```
POST /api/volunteers
Content-Type: application/json
```
**Request Body:**
```json
{
  "firstName": "John",
  "lastName": "Smith",
  "email": "john.smith@church.com",
  "phone": "555-1005",
  "status": "Active",
  "level": "Level 1",
  "startDate": "2025-01-15",
  "capacityBand": "Balanced",
  "capacityMin": 2,
  "capacityMax": 3,
  "teamLead": "TL001",
  "campus": "Main Campus"
}
```

### 11. Update Volunteer
```
PUT /api/volunteers/{volunteerId}
Content-Type: application/json
```
**Request Body (all fields optional):**
```json
{
  "firstName": "John",
  "email": "newemail@church.com",
  "status": "Active",
  "emotionalTone": "??"
}
```

### 12. Delete Volunteer
```
DELETE /api/volunteers/{volunteerId}
```

### 13. Update Volunteer Status
```
PATCH /api/volunteers/{volunteerId}/status/{status}
```
Example: `PATCH /api/volunteers/V001/status/Active`

### 14. Update Volunteer Capacity
```
PATCH /api/volunteers/{volunteerId}/capacity
Content-Type: application/json
```
**Request Body:**
```json
{
  "capacityBand": "Consistent",
  "capacityMin": 4,
  "capacityMax": 6
}
```

### 15. Update Volunteer Performance
```
PATCH /api/volunteers/{volunteerId}/performance
Content-Type: application/json
```
**Request Body:**
```json
{
  "totalCompleted": 47,
  "totalAssigned": 52
}
```

### 16. Update Volunteer Check-In
```
PATCH /api/volunteers/{volunteerId}/check-in
Content-Type: application/json
```
**Request Body:**
```json
{
  "lastCheckIn": "2025-01-20T10:30:00Z",
  "nextCheckIn": "2025-02-20T10:30:00Z"
}
```

---

## Data Models

### Volunteer Model
Main entity representing a Level 1 volunteer.

```csharp
public class Volunteer
{
    public string VolunteerId { get; set; }          // Auto-generated
    public string FirstName { get; set; }            // First name
    public string LastName { get; set; }             // Last name
    public string Email { get; set; }                // Email (unique)
    public string? Phone { get; set; }               // Phone number
    
    // Status & Level
    public string Status { get; set; }               // Active, Care Path, etc.
    public string Level { get; set; }                // Level 0, 1, 2, 3
    public DateTime StartDate { get; set; }          // Start date
    public DateTime? EndDate { get; set; }           // End date (if exited)
    
    // Capacity
    public string CapacityBand { get; set; }         // Consistent, Balanced, Limited
    public int CapacityMin { get; set; }             // Min assignments per week
    public int CapacityMax { get; set; }             // Max assignments per week
    public int CurrentAssignments { get; set; }      // Current active assignments
    
    // Performance
    public int TotalCompleted { get; set; }          // Total completed follow-ups
    public int TotalAssigned { get; set; }           // Total assigned follow-ups
    public decimal? CompletionRate { get; set; }     // % completed
    public decimal? AvgResponseTime { get; set; }    // Avg response time (hours)
    
    // Health
    public DateTime? LastCheckIn { get; set; }       // Last check-in date
    public DateTime? NextCheckIn { get; set; }       // Next check-in due
    public string? EmotionalTone { get; set; }       // ??, ??, ??
    public int? VnpsScore { get; set; }              // 0-10 satisfaction
    public string? BurnoutRisk { get; set; }         // Low, Medium, High
    
    // Team Assignment
    public string? TeamLead { get; set; }            // Assigned team lead ID
    public string? Campus { get; set; }              // Campus location
    
    // Compliance
    public DateTime? Level0Complete { get; set; }    // Training completion date
    public DateTime? CrisisTrained { get; set; }     // Crisis training date
    public DateTime? ConfidentialitySigned { get; set; }  // Confidentiality date
    public DateTime? BackgroundCheck { get; set; }   // Background check date
    
    // Boundaries
    public int BoundaryViolations { get; set; }      // Count of violations
    public DateTime? LastViolationDate { get; set; } // Last violation date
    
    // Metadata
    public DateTime CreatedAt { get; set; }          // Created timestamp
    public DateTime UpdatedAt { get; set; }          // Last updated timestamp
}
```

---

## DTOs

### CreateVolunteerDto
Used for creating new volunteer records.

### UpdateVolunteerDto
Used for updating existing records. All fields are optional.

### VolunteerResponseDto
Used for API responses. Contains all fields from the Volunteer model.

---

## Database Schema

### VOLUNTEERS Table (MySQL)
```sql
CREATE TABLE volunteers (
    volunteer_id VARCHAR(20) PRIMARY KEY,
    first_name VARCHAR(50) NOT NULL,
    last_name VARCHAR(50) NOT NULL,
    email VARCHAR(100) NOT NULL UNIQUE,
    phone VARCHAR(20),
    status VARCHAR(30) NOT NULL DEFAULT 'Active',
    level VARCHAR(20) NOT NULL DEFAULT 'Level 0',
    start_date DATE NOT NULL,
    end_date DATE,
    capacity_band VARCHAR(20) NOT NULL DEFAULT 'Balanced',
    capacity_min INT NOT NULL,
    capacity_max INT NOT NULL,
    current_assignments INT DEFAULT 0,
    total_completed INT DEFAULT 0,
    total_assigned INT DEFAULT 0,
    completion_rate DECIMAL(5,2),
    avg_response_time DECIMAL(5,2),
    last_check_in DATE,
    next_check_in DATE,
    emotional_tone VARCHAR(10),
    vnps_score INT CHECK (vnps_score >= 0 AND vnps_score <= 10),
    burnout_risk VARCHAR(20),
    team_lead VARCHAR(20),
    campus VARCHAR(50),
    level_0_complete DATE,
    crisis_trained DATE,
    confidentiality_signed DATE,
    background_check DATE,
    boundary_violations INT DEFAULT 0,
    last_violation_date DATE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);
```

### Key Indexes
- `idx_volunteers_status` - For filtering by status
- `idx_volunteers_team_lead` - For team lead assignments
- `idx_volunteers_capacity_band` - For capacity filtering
- `idx_volunteers_email` - For email uniqueness
- `idx_volunteers_team_status` - For combined queries
- `idx_volunteers_status_level` - For status and level filtering

---

## Service Layer Features

### VolunteerService Methods

- **GetAllAsync()** - Retrieve all volunteers
- **GetByIdAsync(volunteerId)** - Get specific volunteer
- **GetByStatusAsync(status)** - Filter by status
- **GetByTeamLeadAsync(teamLeadId)** - Get team's volunteers
- **GetByCapacityBandAsync(capacityBand)** - Filter by capacity
- **CreateAsync(dto)** - Create new volunteer
- **UpdateAsync(volunteerId, dto)** - Update volunteer
- **DeleteAsync(volunteerId)** - Delete volunteer
- **GetPaginatedAsync(pageNumber, pageSize)** - Paginated results
- **UpdateStatusAsync(volunteerId, status)** - Update status
- **UpdateCapacityAsync(volunteerId, band, min, max)** - Update capacity
- **UpdatePerformanceAsync(volunteerId, completed, assigned)** - Update metrics
- **UpdateCheckInAsync(volunteerId, lastCheckIn, nextCheckIn)** - Update check-in
- **GetActiveVolunteerCountAsync()** - Count active volunteers
- **GetWithLowCompletionRateAsync(threshold)** - Alert on low completion
- **GenerateVolunteerId()** - Auto-generate unique ID

---

## MySQL-Specific Differences

### Column Types
- `INT` instead of `INTEGER`
- `DECIMAL(5,2)` for precision decimal fields
- `DATE` for date-only fields
- `TIMESTAMP DEFAULT CURRENT_TIMESTAMP` for created_at
- `TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP` for updated_at

### Pagination Query
```sql
-- MySQL uses LIMIT and OFFSET
SELECT * FROM volunteers 
ORDER BY created_at DESC 
LIMIT @PageSize OFFSET @Offset
```

### Unique Email Constraint
```sql
email VARCHAR(100) NOT NULL UNIQUE
```

### Check Constraint for vNPS Score
```sql
vnps_score INT CHECK (vnps_score >= 0 AND vnps_score <= 10)
```

---

## Dependency Injection Setup

In `Program.cs`:
```csharp
// Register database connection factory
builder.Services.AddScoped<RM_CMS.Data.IDbConnectionFactory, RM_CMS.Data.DbConnectionFactory>();

// Register Volunteers Data Access Layer
builder.Services.AddScoped<RM_CMS.DAL.Volunteers.IVolunteerRepository, 
    RM_CMS.DAL.Volunteers.VolunteerRepository>();

// Register Volunteers Business Logic Layer
builder.Services.AddScoped<RM_CMS.BLL.Volunteers.IVolunteerService, 
    RM_CMS.BLL.Volunteers.VolunteerService>();
```

---

## Usage Examples

### Create a New Volunteer
```csharp
var createDto = new CreateVolunteerDto
{
    FirstName = "John",
    LastName = "Smith",
    Email = "john@church.com",
    Phone = "555-1005",
    Status = "Active",
    Level = "Level 1",
    StartDate = DateTime.Now,
    CapacityBand = "Balanced",
    CapacityMin = 2,
    CapacityMax = 3,
    TeamLead = "TL001"
};

var result = await _volunteerService.CreateAsync(createDto);
```

### Update Volunteer Status
```csharp
await _volunteerService.UpdateStatusAsync("V001", "Active");
```

### Update Volunteer Capacity
```csharp
await _volunteerService.UpdateCapacityAsync("V001", "Consistent", 4, 6);
```

### Update Performance Metrics
```csharp
await _volunteerService.UpdatePerformanceAsync("V001", 47, 52);
```

### Get Volunteers with Low Completion
```csharp
var lowPerformers = await _volunteerService.GetWithLowCompletionRateAsync(75);
```

---

## Error Handling

All endpoints return appropriate HTTP status codes:
- **200 OK** - Successful request
- **201 Created** - Resource created successfully
- **400 Bad Request** - Invalid input data
- **404 Not Found** - Resource not found
- **500 Internal Server Error** - Server error

---

## Connection String Configuration

In `appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyAppDb;User=root;Password=your_password;"
  }
}
```

---

## Technologies Used

- **.NET 8** - Framework
- **Dapper ORM** - Data access
- **MySQL** - Database
- **C# 12** - Language
- **Async/Await** - Asynchronous operations

---

## Best Practices Implemented

1. ? **3-Layer Architecture** - Separation of concerns
2. ? **Dependency Injection** - Loose coupling
3. ? **DTOs** - Clean API contracts
4. ? **Async Operations** - Non-blocking calls
5. ? **Error Handling** - Comprehensive exceptions
6. ? **Logging** - Built-in tracking
7. ? **SQL Parameterization** - SQL injection protection
8. ? **Repository Pattern** - Data access abstraction
9. ? **MySQL Compatibility** - Optimized for MySQL
10. ? **Performance** - Indexed queries

---

## Testing the Module

### Using Swagger UI
1. Run the application
2. Navigate to `https://localhost:7xxx/swagger`
3. Test endpoints directly from the UI

### Sample Test Data
The SQL script includes 4 sample volunteers:
- V001: Sarah Johnson (Active, Balanced capacity)
- V002: Mike Thompson (Active, Consistent capacity)
- V003: Emily Davis (Active, Limited capacity)
- V004: James Wilson (Care Path, Burnout risk)

---

## Future Enhancements

- Add caching layer for frequently accessed data
- Implement audit logging for changes
- Add advanced filtering and search
- Implement soft delete functionality
- Add batch operations for bulk updates
- Create volunteer performance dashboard views

---

**Module Status:** ? Ready for Development

**Version:** 1.0.0  
**Framework:** .NET 8  
**Database:** MySQL  
**ORM:** Dapper
