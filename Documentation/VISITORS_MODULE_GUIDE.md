# Visitors (Peoples) Module - Implementation Guide

## Overview
The Visitors Module manages the `people` entity from the Level 1 Follow-Up System. This module provides complete CRUD operations and specialized endpoints for managing visitor data, follow-up assignments, and contact tracking.

## Project Structure

```
RM_CMS/
??? Data/
?   ??? Models/
?   ?   ??? People.cs                    # Core People model
?   ??? DTO/
?   ?   ??? CreatePeopleDto.cs           # DTO for creating people
?   ?   ??? UpdatePeopleDto.cs           # DTO for updating people
?   ?   ??? PeopleResponseDto.cs         # DTO for API responses
?   ??? DbConnection.cs                  # Database connection factory
??? DAL/
?   ??? Visitors/
?       ??? PeopleRepository.cs          # Data Access Layer for People
??? BLL/
?   ??? Visitors/
?       ??? PeopleService.cs             # Business Logic Layer for People
??? Controllers/
    ??? visitors/
        ??? PeoplesController.cs         # API Controller
```

## Architecture

### 3-Layer Architecture Pattern

```
???????????????????????????
?   Controller Layer      ?  (PeoplesController)
?  - HTTP Requests/Responses
?  - Input Validation
?  - Error Handling
???????????????????????????
             ?
???????????????????????????
?   Business Logic Layer  ?  (PeopleService)
?  - Data Transformation
?  - Business Rules
?  - Pagination Logic
???????????????????????????
             ?
???????????????????????????
?   Data Access Layer     ?  (PeopleRepository)
?  - Database Queries
?  - Dapper ORM
?  - Query Execution
???????????????????????????
             ?
             ?
        SQL Database
```

## API Endpoints

### 1. Get All People
```
GET /api/peoples
```
**Response:**
```json
[
  {
    "personId": "P20250115123456789",
    "firstName": "Sarah",
    "lastName": "Johnson",
    "email": "sarah.j@email.com",
    "phone": "555-0101",
    "followUpStatus": "Contacted",
    "followUpPriority": "Normal",
    ...
  }
]
```

### 2. Get Person by ID
```
GET /api/peoples/{personId}
```
**Example:**
```
GET /api/peoples/P20250115123456789
```

### 3. Get People by Follow-Up Status
```
GET /api/peoples/status/{followUpStatus}
```
**Valid Status Values:**
- `New` - Not yet assigned
- `Assigned` - Assigned to volunteer
- `Contacted` - Contact made
- `Retry Pending` - Awaiting retry
- `Escalated` - Escalated to Team Lead
- `Complete` - Completed
- `Unresponsive` - No response after max attempts

### 4. Get People Assigned to Volunteer
```
GET /api/peoples/volunteer/{volunteerId}
```
**Example:**
```
GET /api/peoples/volunteer/V001
```

### 5. Get People by Priority
```
GET /api/peoples/priority/{priority}
```
**Valid Priority Values:**
- `Normal`
- `High`
- `Urgent`

### 6. Get Paginated People
```
GET /api/peoples/paginated/data?pageNumber=1&pageSize=10
```
**Response:**
```json
{
  "pageNumber": 1,
  "pageSize": 10,
  "totalCount": 45,
  "totalPages": 5,
  "data": [...]
}
```

### 7. Create New Person
```
POST /api/peoples
Content-Type: application/json
```
**Request Body:**
```json
{
  "firstName": "Sarah",
  "lastName": "Johnson",
  "email": "sarah.j@email.com",
  "phone": "555-0101",
  "ageRange": "26-35",
  "householdType": "Married",
  "zipCode": "12345",
  "visitType": "First-Time Visitor",
  "firstVisitDate": "2025-01-15",
  "connectionSource": "Friend/Family Invite",
  "campus": "Main Campus",
  "followUpStatus": "New",
  "followUpPriority": "Normal",
  "interestedIn": "Small Groups, Serving",
  "createdBy": "admin"
}
```

### 8. Update Person
```
PUT /api/peoples/{personId}
Content-Type: application/json
```
**Request Body (all fields optional):**
```json
{
  "firstName": "Sarah",
  "email": "sarah.newemail@email.com",
  "followUpStatus": "Contacted",
  "lastContactDate": "2025-01-18T10:30:00Z"
}
```

### 9. Delete Person
```
DELETE /api/peoples/{personId}
```

### 10. Update Follow-Up Status
```
PATCH /api/peoples/{personId}/status/{status}
```
**Example:**
```
PATCH /api/peoples/P20250115123456789/status/Contacted
```

### 11. Assign Volunteer
```
PATCH /api/peoples/{personId}/assign-volunteer/{volunteerId}
```
**Example:**
```
PATCH /api/peoples/P20250115123456789/assign-volunteer/V001
```

### 12. Update Contact Information
```
PATCH /api/peoples/{personId}/contact
Content-Type: application/json
```
**Request Body:**
```json
{
  "lastContactDate": "2025-01-18T10:30:00Z",
  "nextActionDate": "2025-01-22T00:00:00Z"
}
```

### 13. Get Total Count
```
GET /api/peoples/count/total
```
**Response:**
```json
{
  "totalCount": 45
}
```

## Data Models

### People Model
Main entity representing a visitor/member in the system.

```csharp
public class People
{
    public string PersonId { get; set; }              // Unique identifier (auto-generated)
    public string FirstName { get; set; }             // First name
    public string LastName { get; set; }              // Last name
    public string? Email { get; set; }                // Email address
    public string? Phone { get; set; }                // Phone number
    public string? AgeRange { get; set; }             // Age bracket
    public string? HouseholdType { get; set; }        // Household composition
    public string? ZipCode { get; set; }              // ZIP code
    public string VisitType { get; set; }             // Type of visitor
    public DateTime FirstVisitDate { get; set; }      // Date of first visit
    public DateTime? LastVisitDate { get; set; }      // Date of last visit
    public int VisitCount { get; set; }               // Total visit count
    public string? ConnectionSource { get; set; }     // How they found the church
    public string? Campus { get; set; }               // Campus location
    public string FollowUpStatus { get; set; }        // Current follow-up status
    public string? FollowUpPriority { get; set; }     // Priority level
    public string? AssignedVolunteer { get; set; }    // Assigned volunteer ID
    public DateTime? AssignedDate { get; set; }       // Date assigned
    public DateTime? LastContactDate { get; set; }    // Last contact date
    public DateTime? NextActionDate { get; set; }     // Next action due date
    public string? InterestedIn { get; set; }         // Areas of interest
    public string? PrayerRequests { get; set; }       // Prayer requests
    public string? SpecificNeeds { get; set; }        // Specific needs
    public DateTime CreatedAt { get; set; }           // Created timestamp
    public DateTime UpdatedAt { get; set; }           // Last updated timestamp
    public string? CreatedBy { get; set; }            // Created by user
}
```

## DTOs

### CreatePeopleDto
Used for creating new people records. Contains only the fields needed for creation.

### UpdatePeopleDto
Used for updating existing records. All fields are optional - only provided fields are updated.

### PeopleResponseDto
Used for API responses. Contains all fields from the People model.

## Database Schema

### PEOPLE Table
```sql
CREATE TABLE people (
    person_id VARCHAR(20) PRIMARY KEY,
    first_name VARCHAR(50) NOT NULL,
    last_name VARCHAR(50) NOT NULL,
    email VARCHAR(100),
    phone VARCHAR(20),
    age_range VARCHAR(20),
    household_type VARCHAR(50),
    zip_code VARCHAR(10),
    visit_type VARCHAR(30) NOT NULL,
    first_visit_date DATE NOT NULL,
    last_visit_date DATE,
    visit_count INTEGER DEFAULT 1,
    connection_source VARCHAR(50),
    campus VARCHAR(50),
    follow_up_status VARCHAR(30) NOT NULL,
    follow_up_priority VARCHAR(20),
    assigned_volunteer VARCHAR(20),
    assigned_date DATE,
    last_contact_date DATE,
    next_action_date DATE,
    interested_in TEXT,
    prayer_requests TEXT,
    specific_needs TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    created_by VARCHAR(50),
    FOREIGN KEY (assigned_volunteer) REFERENCES volunteers(volunteer_id)
);
```

### Key Indexes
```sql
CREATE INDEX idx_people_status ON people(follow_up_status);
CREATE INDEX idx_people_assigned ON people(assigned_volunteer);
```

## Service Layer Features

### PeopleService
Handles all business logic for people management:

- **GetAllAsync()** - Retrieve all people
- **GetByIdAsync(personId)** - Get specific person
- **GetByStatusAsync(status)** - Filter by follow-up status
- **GetByAssignedVolunteerAsync(volunteerId)** - Get person's assignments
- **GetByPriorityAsync(priority)** - Filter by priority
- **CreateAsync(dto)** - Create new person
- **UpdateAsync(personId, dto)** - Update person
- **DeleteAsync(personId)** - Delete person
- **GetPaginatedAsync(pageNumber, pageSize)** - Get paginated results
- **UpdateFollowUpStatusAsync(personId, status)** - Update status
- **AssignVolunteerAsync(personId, volunteerId)** - Assign to volunteer
- **UpdateContactAsync(personId, lastContact, nextAction)** - Update contact info
- **GeneratePersonId()** - Generate unique person ID

## Dependency Injection Setup

In `Program.cs`:
```csharp
// Register database connection factory
builder.Services.AddScoped<RM_CMS.Data.IDbConnectionFactory, RM_CMS.Data.DbConnectionFactory>();

// Register Data Access Layer
builder.Services.AddScoped<RM_CMS.DAL.Visitors.IPeopleRepository, RM_CMS.DAL.Visitors.PeopleRepository>();

// Register Business Logic Layer
builder.Services.AddScoped<RM_CMS.BLL.Visitors.IPeopleService, RM_CMS.BLL.Visitors.PeopleService>();
```

## Usage Examples

### Create a New Visitor
```csharp
var createDto = new CreatePeopleDto
{
    FirstName = "Sarah",
    LastName = "Johnson",
    Email = "sarah@email.com",
    Phone = "555-0101",
    VisitType = "First-Time Visitor",
    FirstVisitDate = DateTime.Now,
    FollowUpStatus = "New",
    Campus = "Main Campus",
    CreatedBy = "Admin"
};

var result = await _peopleService.CreateAsync(createDto);
```

### Update Follow-Up Status
```csharp
await _peopleService.UpdateFollowUpStatusAsync("P20250115123456789", "Contacted");
```

### Assign Volunteer
```csharp
await _peopleService.AssignVolunteerAsync("P20250115123456789", "V001");
```

### Get Paginated Results
```csharp
var (data, totalCount) = await _peopleService.GetPaginatedAsync(1, 10);
```

## Error Handling

All endpoints return appropriate HTTP status codes:
- **200 OK** - Successful request
- **201 Created** - Resource created successfully
- **400 Bad Request** - Invalid input data
- **404 Not Found** - Resource not found
- **500 Internal Server Error** - Server error

## Connection String Configuration

In `appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyAppDb;User Id=sa;Password=Your_password123;"
  }
}
```

## Technologies Used

- **.NET 8** - Framework
- **Dapper ORM** - Data access
- **SQL Server** - Database
- **Swagger** - API documentation
- **Async/Await** - Asynchronous operations

## Best Practices Implemented

1. ? **3-Layer Architecture** - Separation of concerns
2. ? **Dependency Injection** - Loose coupling
3. ? **DTOs** - Data transfer objects for clean API contracts
4. ? **Async Operations** - Non-blocking database calls
5. ? **Error Handling** - Comprehensive exception handling
6. ? **Logging** - Built-in logging for debugging
7. ? **SQL Parameterization** - Protection against SQL injection
8. ? **Repository Pattern** - Data access abstraction

## Testing the Module

### Using Swagger UI
1. Run the application
2. Navigate to `https://localhost:7xxx/swagger`
3. Test endpoints directly from the UI

### Using Postman/Insomnia
Import the endpoints and test with sample data provided in the documentation.

## Future Enhancements

- Add caching layer for frequently accessed data
- Implement audit logging for data changes
- Add advanced filtering and search capabilities
- Implement soft delete functionality
- Add batch operations for bulk updates
