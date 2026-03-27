# Visitors Module - Complete Documentation Index

## ?? Documentation Overview

This document serves as a central index for all Visitors Module documentation and implementation files.

---

## ?? Quick Navigation

### **For First-Time Setup** ?? Start here
1. Read: `QUICK_START_GUIDE.md`
2. Follow the database setup instructions
3. Update connection strings
4. Run the application

### **For API Usage**
1. Read: `VISITORS_MODULE_GUIDE.md` - Full API reference
2. Check: `ARCHITECTURE_DIAGRAMS.md` - Visual flows
3. Use: Swagger UI at `https://localhost:7xxx/swagger`

### **For Development**
1. Review: `IMPLEMENTATION_SUMMARY.md` - What was built
2. Study: `ARCHITECTURE_DIAGRAMS.md` - How it works
3. Reference: Source code comments

### **For Database**
1. Execute: `Database/SQL_Scripts/01_Create_People_Table.sql`
2. Review: Schema in `??? COMPLETE DATABASE SCHEMA.md`
3. Check: Sample data provided

---

## ?? Documentation Files

### Core Documentation

| File | Purpose | Read Time |
|------|---------|-----------|
| **QUICK_START_GUIDE.md** | Setup & initial testing | 10 min |
| **VISITORS_MODULE_GUIDE.md** | Complete API reference | 20 min |
| **IMPLEMENTATION_SUMMARY.md** | What was built & why | 15 min |
| **ARCHITECTURE_DIAGRAMS.md** | Visual diagrams & flows | 15 min |
| **INDEX.md** (this file) | Navigation & overview | 5 min |

### Reference Documentation

| File | Purpose |
|------|---------|
| **??? COMPLETE DATABASE SCHEMA.md** | Full system database design |
| **01_Create_People_Table.sql** | SQL for creating database |

---

## ?? Source Code Files

### Data Layer
```
Data/
??? Models/
?   ??? People.cs                    # Core entity model
??? DTO/
?   ??? CreatePeopleDto.cs           # Request DTO
?   ??? UpdatePeopleDto.cs           # Update DTO
?   ??? PeopleResponseDto.cs         # Response DTO
??? DbConnection.cs                  # DB connection factory
```

### Business Logic Layer
```
BLL/
??? Visitors/
    ??? PeopleService.cs             # Business logic
        - GetAllAsync()
        - GetByIdAsync()
        - CreateAsync()
        - UpdateAsync()
        - DeleteAsync()
        - GetPaginatedAsync()
        - GetByStatusAsync()
        - GetByAssignedVolunteerAsync()
        - UpdateFollowUpStatusAsync()
        - AssignVolunteerAsync()
        - UpdateContactAsync()
        - GeneratePersonId()
```

### Data Access Layer
```
DAL/
??? Visitors/
    ??? PeopleRepository.cs          # Data access
        - GetAllAsync()
        - GetByIdAsync()
        - CreateAsync()
        - UpdateAsync()
        - DeleteAsync()
        - GetPaginatedAsync()
        - GetByStatusAsync()
        - GetByAssignedVolunteerAsync()
        - UpdateFollowUpStatusAsync()
        - UpdateAssignmentAsync()
        - UpdateLastContactAsync()
```

### Controllers
```
Controllers/
??? visitors/
    ??? PeoplesController.cs         # API endpoints
        - GET /api/peoples
        - GET /api/peoples/{id}
        - GET /api/peoples/status/{status}
        - GET /api/peoples/volunteer/{id}
        - GET /api/peoples/priority/{priority}
        - GET /api/peoples/paginated/data
        - GET /api/peoples/count/total
        - POST /api/peoples
        - PUT /api/peoples/{id}
        - DELETE /api/peoples/{id}
        - PATCH /api/peoples/{id}/status/{status}
        - PATCH /api/peoples/{id}/assign-volunteer/{vid}
        - PATCH /api/peoples/{id}/contact
```

### Configuration
```
Program.cs                          # Dependency injection setup
RM_CMS.csproj                       # Project configuration
appsettings.json                    # Production settings
appsettings.Development.json        # Development settings
```

---

## ?? API Endpoints Summary

### GET Endpoints
```
GET /api/peoples                           ? Get all visitors
GET /api/peoples/{personId}                ? Get specific visitor
GET /api/peoples/status/{status}           ? Filter by status
GET /api/peoples/volunteer/{volunteerId}   ? Get volunteer's assignments
GET /api/peoples/priority/{priority}       ? Filter by priority
GET /api/peoples/paginated/data            ? Paginated results
GET /api/peoples/count/total               ? Total count
```

### POST Endpoints
```
POST /api/peoples                          ? Create new visitor
```

### PUT Endpoints
```
PUT /api/peoples/{personId}                ? Update visitor
```

### PATCH Endpoints
```
PATCH /api/peoples/{personId}/status/{status}           ? Update status
PATCH /api/peoples/{personId}/assign-volunteer/{vid}    ? Assign volunteer
PATCH /api/peoples/{personId}/contact                   ? Update contact
```

### DELETE Endpoints
```
DELETE /api/peoples/{personId}             ? Delete visitor
```

---

## ??? Architecture Overview

### 3-Layer Architecture
```
???????????????????????????????????
?   CONTROLLER LAYER              ?  HTTP Requests/Responses
?   (PeoplesController.cs)        ?  Validation, Error Handling
???????????????????????????????????
?   BUSINESS LOGIC LAYER          ?  Rules, Transformations
?   (PeopleService.cs)            ?  DTO Mapping, Pagination
???????????????????????????????????
?   DATA ACCESS LAYER             ?  Dapper ORM
?   (PeopleRepository.cs)         ?  SQL Queries
???????????????????????????????????
?   DATABASE LAYER                ?  SQL Server
?   (people table)                ?  Indexes, Constraints
???????????????????????????????????
```

### Dependency Injection
```
Program.cs
??? IDbConnectionFactory ? DbConnectionFactory
??? IPeopleRepository ? PeopleRepository
??? IPeopleService ? PeopleService
```

---

## ??? Database Schema

### People Table
- `person_id` (PK) - Auto-generated identifier
- `first_name`, `last_name` - Contact information
- `email`, `phone` - Communication channels
- `age_range`, `household_type`, `zip_code` - Demographics
- `visit_type`, `first_visit_date`, `last_visit_date` - Visit tracking
- `visit_count` - Total visits
- `connection_source`, `campus` - Where they came from
- `follow_up_status` - Current status (New, Assigned, Contacted, etc.)
- `follow_up_priority` - Priority level (Normal, High, Urgent)
- `assigned_volunteer` - Assigned to which volunteer (FK)
- `assigned_date` - When assigned
- `last_contact_date` - Last contact date
- `next_action_date` - Next action due
- `interested_in`, `prayer_requests`, `specific_needs` - Notes
- `created_at`, `updated_at` - Timestamps
- `created_by` - Creator username

### Indexes
- `idx_people_status` - Fast status filtering
- `idx_people_assigned` - Fast volunteer lookup
- `idx_people_created` - Recent records
- `idx_people_priority` - Priority filtering
- `idx_people_status_priority` - Combined lookup

---

## ? Key Features Implemented

? **Full CRUD Operations**
- Create, Read, Update, Delete visitors

? **Advanced Filtering**
- By status, volunteer, priority

? **Pagination Support**
- Efficient data retrieval with page/size parameters

? **Dapper ORM**
- Lightweight, high-performance data access

? **Async Operations**
- Non-blocking database calls with async/await

? **Error Handling**
- Comprehensive exception management and HTTP responses

? **Logging**
- Built-in request/response logging

? **SQL Injection Protection**
- Parameterized queries

? **Repository Pattern**
- Clean data access abstraction

? **Dependency Injection**
- Loose coupling, easy testing

? **Swagger Documentation**
- Auto-generated API documentation

? **Clean Architecture**
- 3-layer separation of concerns

---

## ?? Getting Started Checklist

### Phase 1: Setup (30 minutes)
- [ ] Read `QUICK_START_GUIDE.md`
- [ ] Create database
- [ ] Run `01_Create_People_Table.sql`
- [ ] Update `appsettings.Development.json`
- [ ] Run application: `dotnet run`

### Phase 2: Testing (20 minutes)
- [ ] Open Swagger: `https://localhost:7xxx/swagger`
- [ ] Create a visitor (POST)
- [ ] Get all visitors (GET)
- [ ] Update visitor (PUT)
- [ ] Update status (PATCH)
- [ ] Delete visitor (DELETE)

### Phase 3: Integration (Later)
- [ ] Create Volunteers module
- [ ] Create Follow-Ups module
- [ ] Create Team Leads module
- [ ] Add unit tests
- [ ] Add integration tests

---

## ?? Relationship Diagram

```
PEOPLE
  ?
  ?? FOREIGN KEY: assigned_volunteer ? VOLUNTEERS.volunteer_id
  ?
  ?? Referenced by: FOLLOW_UPS.person_id
  ?
  ?? Referenced by: ESCALATIONS.person_id
```

### Future Relationships (when other modules implemented)
```
PEOPLE ??? VOLUNTEERS (assigned to)
        ?? FOLLOW_UPS (has many)
        ?? ESCALATIONS (may have)
        ?? CHECK_INS (volunteer has)
        ?? NOTES (has many)
```

---

## ?? Data Model Overview

### Core Fields
- **Identity**: person_id, created_at, created_by
- **Contact**: first_name, last_name, email, phone
- **Demographics**: age_range, household_type, zip_code
- **Visitor Info**: visit_type, first_visit_date, last_visit_date, visit_count
- **Source**: connection_source, campus
- **Follow-up**: follow_up_status, follow_up_priority
- **Assignment**: assigned_volunteer, assigned_date
- **Activity**: last_contact_date, next_action_date
- **Notes**: interested_in, prayer_requests, specific_needs

### Enumerated Values

**follow_up_status**
- New, Assigned, Contacted, Retry Pending, Escalated, Complete, Unresponsive

**follow_up_priority**
- Normal, High, Urgent

**visit_type**
- First-Time Visitor, Returning Visitor, New Member, Inactive Member, Guest

**connection_source**
- Friend/Family Invite, Online Search, Social Media, Drove By, Event, Other

**age_range**
- Under 18, 18-25, 26-35, 36-50, 51-65, 65+, Prefer not to say

---

## ??? Technologies Used

- **.NET 8** - Latest framework
- **C# 12** - Modern language features
- **Dapper 2.1.15** - ORM
- **SQL Server** - Database
- **Swagger** - API documentation
- **Dependency Injection** - Built-in container
- **Async/Await** - Modern async patterns

---

## ?? Sample API Request/Response

### Create Visitor
**Request:**
```bash
POST /api/peoples
Content-Type: application/json

{
  "firstName": "Sarah",
  "lastName": "Johnson",
  "email": "sarah@example.com",
  "phone": "555-1234",
  "visitType": "First-Time Visitor",
  "firstVisitDate": "2025-01-25",
  "followUpStatus": "New",
  "campus": "Main Campus",
  "createdBy": "admin"
}
```

**Response:**
```json
HTTP/1.1 201 Created
Location: /api/peoples/P20250125123456789

{
  "personId": "P20250125123456789",
  "firstName": "Sarah",
  "lastName": "Johnson",
  "email": "sarah@example.com",
  "phone": "555-1234",
  "visitType": "First-Time Visitor",
  "firstVisitDate": "2025-01-25",
  "lastVisitDate": null,
  "visitCount": 1,
  "followUpStatus": "New",
  "followUpPriority": "Normal",
  "assignedVolunteer": null,
  "createdAt": "2025-01-25T12:34:56.789Z",
  "updatedAt": "2025-01-25T12:34:56.789Z"
}
```

---

## ? Common Questions

**Q: How do I set up the database?**
A: See `QUICK_START_GUIDE.md` - Database Setup section

**Q: What's the person ID format?**
A: Format is `P` + Timestamp (YYYYMMDDHHmmss) + Random 4-digit number
Example: `P20250125123456789`

**Q: Can I use pagination?**
A: Yes! Use `/api/peoples/paginated/data?pageNumber=1&pageSize=10`

**Q: How do I assign a volunteer?**
A: Use `/api/peoples/{personId}/assign-volunteer/{volunteerId}`

**Q: What are the valid status values?**
A: New, Assigned, Contacted, Retry Pending, Escalated, Complete, Unresponsive

**Q: Can I update just one field?**
A: Yes! Use PUT endpoint and only include fields you want to update

**Q: What happens if I delete a visitor?**
A: The record is permanently deleted from the database

---

## ?? Security Notes

? **SQL Injection Protection** - All queries use parameterized statements
? **Input Validation** - ModelState validation on all endpoints
? **Error Details** - Production errors don't expose sensitive info
? **Connection Security** - Use SQL Server authentication
? **Data Privacy** - Keep connection strings in configuration files

---

## ?? Support Resources

### Need Help?
1. Check **QUICK_START_GUIDE.md** for setup issues
2. Review **VISITORS_MODULE_GUIDE.md** for API questions
3. Study **ARCHITECTURE_DIAGRAMS.md** for design questions
4. Check source code comments

### Report Issues
Include:
- Error message (from logs)
- Endpoint used
- Request body (if applicable)
- Expected vs. actual result

---

## ?? Next Steps

### Immediate (Week 1)
1. Set up database
2. Run and test API
3. Explore Swagger UI
4. Test all endpoints

### Short Term (Week 2)
1. Create Volunteers module (same pattern)
2. Create Follow-Ups module
3. Add unit tests
4. Create Team Leads module

### Medium Term (Week 3-4)
1. Implement Check-Ins module
2. Implement Escalations module
3. Create integration tests
4. Build dashboard views

### Long Term
1. Add caching layer
2. Implement audit logging
3. Add advanced reporting
4. Build frontend UI

---

## ?? Development Status

| Component | Status | Notes |
|-----------|--------|-------|
| Models | ? Complete | All fields implemented |
| DTOs | ? Complete | Create, Update, Response |
| Repository | ? Complete | All CRUD + custom queries |
| Service | ? Complete | Business logic & mapping |
| Controller | ? Complete | 13 endpoints |
| Database | ? Complete | Schema + indexes + sample data |
| Documentation | ? Complete | 5 comprehensive guides |
| Tests | ? Pending | For next phase |
| Integration | ? Pending | Waiting for other modules |

---

## ?? Learning Resources

### For Understanding the Architecture
1. Read: `ARCHITECTURE_DIAGRAMS.md` - Visual representations
2. Study: `IMPLEMENTATION_SUMMARY.md` - What was built and why
3. Review: Source code comments in repository

### For API Usage
1. Swagger UI - Built-in at `/swagger`
2. `VISITORS_MODULE_GUIDE.md` - Complete endpoint reference
3. Postman/Insomnia - API testing tools

### For Database
1. `01_Create_People_Table.sql` - Schema definition
2. `??? COMPLETE DATABASE SCHEMA.md` - Full system design
3. SQL Server Management Studio - Database exploration

---

## ?? Best Practices Checklist

- [x] 3-Layer Architecture
- [x] SOLID Principles
- [x] Repository Pattern
- [x] DTO Pattern
- [x] Async Operations
- [x] Dependency Injection
- [x] Error Handling
- [x] Input Validation
- [x] Logging
- [x] SQL Parameterization
- [x] Clean Code
- [x] Documentation
- [x] Security
- [x] Performance Optimization (Indexes)

---

## ?? NuGet Packages

```
Dapper 2.1.15             (ORM)
System.Data.SqlClient 4.8.6  (SQL Server driver)
Swashbuckle.AspNetCore 6.6.2 (Swagger/OpenAPI)
```

All packages are already added to `RM_CMS.csproj`

---

## ?? Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2025-01-25 | Initial implementation complete |

---

## ?? License & Attribution

This module was implemented following:
- The Level 1 Follow-Up System database schema
- Clean Architecture principles
- SOLID design patterns
- Microsoft .NET best practices

---

**Last Updated:** January 25, 2025  
**Module Status:** ? Ready for Production  
**Framework:** .NET 8  
**ORM:** Dapper 2.1.15

**Happy Coding! ??**
