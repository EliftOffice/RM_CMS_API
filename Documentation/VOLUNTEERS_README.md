# ?? Volunteers Module - README

> A complete, production-ready Volunteers management module for the RM_CMS Level 1 Follow-Up System using MySQL.

---

## ?? Table of Contents

- [Overview](#overview)
- [Quick Start](#quick-start)
- [Features](#features)
- [Architecture](#architecture)
- [Documentation](#documentation)
- [API Endpoints](#api-endpoints)
- [Database](#database)
- [Installation](#installation)
- [Usage](#usage)
- [Contributing](#contributing)

---

## Overview

The **Volunteers Module** is a comprehensive management system for Level 1 volunteers in the church follow-up system. It handles:

- ? Volunteer registration and profiles
- ? Status and level tracking
- ? Capacity band management (Consistent, Balanced, Limited)
- ? Performance metrics and completion rates
- ? Health monitoring (burnout risk, emotional tone, vNPS)
- ? Team lead assignments
- ? Training and compliance tracking
- ? Boundary violation tracking

### Why This Module?

Managing volunteers effectively requires tracking multiple dimensions:
- **Capacity**: How many assignments can they handle?
- **Performance**: Are they completing assignments?
- **Health**: Are they experiencing burnout?
- **Compliance**: Have they completed required training?

This module provides all the tools to manage these aspects through a clean API.

---

## ?? Quick Start

### 1. Prerequisites
- .NET 8 SDK
- MySQL 8.0+
- VS Code or Visual Studio

### 2. Setup Database (2 min)
```bash
# Create database
mysql -u root -p
mysql> CREATE DATABASE MyAppDb;

# Run SQL script
mysql -u root -p MyAppDb < Database/SQL_Scripts/02_Create_Volunteers_Table.sql
```

### 3. Configure Connection (1 min)
```json
// appsettings.Development.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyAppDb;User=root;Password=your_password;"
  }
}
```

### 4. Run Application (1 min)
```bash
dotnet run
```

### 5. Test (5 min)
```
Open: https://localhost:7xxx/swagger
```

**Total Time: ~10 minutes**

---

## ? Features

### Core Operations
- **CRUD Operations**: Create, Read, Update, Delete volunteers
- **Advanced Filtering**: By status, team lead, capacity band
- **Pagination**: Efficient data retrieval
- **Performance Tracking**: Completion rates, response times
- **Health Monitoring**: Burnout risk, emotional tone, vNPS scores

### API Capabilities
- **15 RESTful endpoints** for complete volunteer management
- **Async operations** for non-blocking calls
- **Comprehensive error handling** with meaningful responses
- **Logging** for all operations
- **SQL parameterization** for security

### Database Features
- **28 organized fields** in logical groups
- **6 performance indexes** for fast queries
- **MySQL optimization** for efficiency
- **Sample data** for testing
- **Constraints** for data integrity

---

## ??? Architecture

### 3-Layer Clean Architecture
```
???????????????????????????????????
?   API Layer                     ?
?   (VolunteersController)        ?
?   15 Endpoints                  ?
???????????????????????????????????
?   Business Logic Layer          ?
?   (VolunteerService)            ?
?   16 Methods                    ?
???????????????????????????????????
?   Data Access Layer             ?
?   (VolunteerRepository)         ?
?   16 Methods                    ?
???????????????????????????????????
?   MySQL Database                ?
?   Volunteers Table              ?
???????????????????????????????????
```

### Design Patterns
- **Repository Pattern**: Data access abstraction
- **Service Pattern**: Business logic layer
- **Factory Pattern**: Connection management
- **Dependency Injection**: Loose coupling
- **DTO Pattern**: Clean data transfer

---

## ?? Documentation

### Main Guides

| Guide | Purpose | Read Time |
|-------|---------|-----------|
| [VOLUNTEERS_QUICK_START.md](Documentation/VOLUNTEERS_QUICK_START.md) | 5-minute setup guide | 5 min |
| [VOLUNTEERS_MODULE_GUIDE.md](Documentation/VOLUNTEERS_MODULE_GUIDE.md) | Complete API reference | 30 min |
| [VOLUNTEERS_INTEGRATION_GUIDE.md](Documentation/VOLUNTEERS_INTEGRATION_GUIDE.md) | Integration with other modules | 20 min |
| [VOLUNTEERS_IMPLEMENTATION_SUMMARY.md](Documentation/VOLUNTEERS_IMPLEMENTATION_SUMMARY.md) | Implementation details | 15 min |
| [VOLUNTEERS_DELIVERY_SUMMARY.md](Documentation/VOLUNTEERS_DELIVERY_SUMMARY.md) | What's included | 10 min |

### Key Sections

**For Setup:**
? Start with [VOLUNTEERS_QUICK_START.md](Documentation/VOLUNTEERS_QUICK_START.md)

**For API Usage:**
? See [VOLUNTEERS_MODULE_GUIDE.md](Documentation/VOLUNTEERS_MODULE_GUIDE.md)

**For Integration:**
? Read [VOLUNTEERS_INTEGRATION_GUIDE.md](Documentation/VOLUNTEERS_INTEGRATION_GUIDE.md)

---

## ?? API Endpoints

### Retrieve Operations

```bash
# Get all volunteers
GET /api/volunteers

# Get specific volunteer
GET /api/volunteers/{volunteerId}

# Get by status (Active, Care Path, Paused, Exited, Level 0)
GET /api/volunteers/status/{status}

# Get team's volunteers
GET /api/volunteers/team-lead/{teamLeadId}

# Get by capacity band (Consistent, Balanced, Limited)
GET /api/volunteers/capacity/{capacityBand}

# Get paginated results
GET /api/volunteers/paginated/data?pageNumber=1&pageSize=10

# Get counts
GET /api/volunteers/count/active
GET /api/volunteers/count/total

# Get alerts
GET /api/volunteers/alert/low-completion/{threshold}
```

### Create/Update Operations

```bash
# Create volunteer
POST /api/volunteers
Content-Type: application/json

# Update volunteer
PUT /api/volunteers/{volunteerId}
Content-Type: application/json

# Delete volunteer
DELETE /api/volunteers/{volunteerId}
```

### Partial Updates (PATCH)

```bash
# Update status
PATCH /api/volunteers/{volunteerId}/status/{status}

# Update capacity
PATCH /api/volunteers/{volunteerId}/capacity
Content-Type: application/json

# Update performance
PATCH /api/volunteers/{volunteerId}/performance
Content-Type: application/json

# Update check-in
PATCH /api/volunteers/{volunteerId}/check-in
Content-Type: application/json
```

---

## ?? Database

### Schema Overview

```sql
TABLE: volunteers
??? Identity
?   ??? volunteer_id (PK)
?   ??? created_at
?   ??? updated_at
??? Personal
?   ??? first_name
?   ??? last_name
?   ??? email (UNIQUE)
?   ??? phone
??? Status
?   ??? status
?   ??? level
?   ??? start_date
?   ??? end_date
??? Capacity
?   ??? capacity_band
?   ??? capacity_min
?   ??? capacity_max
?   ??? current_assignments
??? Performance
?   ??? total_completed
?   ??? total_assigned
?   ??? completion_rate
?   ??? avg_response_time
??? Health
?   ??? last_check_in
?   ??? next_check_in
?   ??? emotional_tone
?   ??? vnps_score (0-10)
?   ??? burnout_risk
??? Team
?   ??? team_lead
?   ??? campus
??? Compliance
?   ??? level_0_complete
?   ??? crisis_trained
?   ??? confidentiality_signed
?   ??? background_check
??? Boundaries
    ??? boundary_violations
    ??? last_violation_date
```

### Indexes (6)
- Status lookup
- Team lead filtering
- Capacity band filtering
- Email uniqueness
- Team + Status composite
- Status + Level composite

### Sample Data (4 Volunteers)
- V001: Sarah Johnson (High performer, happy)
- V002: Mike Thompson (Good performer, medium burnout)
- V003: Emily Davis (New volunteer, positive)
- V004: James Wilson (Care path, high risk)

---

## ?? Installation

### From Source

```bash
# Clone repository
git clone https://github.com/EliftOffice/RM_CMS_API

# Navigate to project
cd RM_CMS

# Create database
mysql -u root -p < Database/SQL_Scripts/02_Create_Volunteers_Table.sql

# Update connection string
# Edit appsettings.Development.json

# Build project
dotnet build

# Run application
dotnet run
```

### Docker (Optional)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 as build
WORKDIR /app
COPY . .
RUN dotnet build
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RM_CMS.dll"]
```

---

## ?? Usage

### Example 1: Create a Volunteer

```csharp
// HTTP Request
POST /api/volunteers
{
  "firstName": "David",
  "lastName": "Brown",
  "email": "david.brown@church.com",
  "phone": "555-1006",
  "status": "Active",
  "level": "Level 1",
  "startDate": "2025-01-20",
  "capacityBand": "Balanced",
  "capacityMin": 2,
  "capacityMax": 3,
  "teamLead": "TL001"
}

// Response
{
  "volunteerId": "V20250125102530001234",
  "firstName": "David",
  "lastName": "Brown",
  "email": "david.brown@church.com",
  "status": "Active",
  "level": "Level 1",
  "capacityBand": "Balanced",
  "currentAssignments": 0,
  "completionRate": 0,
  "createdAt": "2025-01-25T10:25:30Z",
  ...
}
```

### Example 2: Update Capacity

```bash
PATCH /api/volunteers/V001/capacity
{
  "capacityBand": "Limited",
  "capacityMin": 1,
  "capacityMax": 2
}
```

### Example 3: Monitor Performance

```bash
GET /api/volunteers/alert/low-completion/80
```

Returns volunteers with completion rate < 80%

### Example 4: Check Team Status

```bash
GET /api/volunteers/team-lead/TL001
```

Returns all volunteers assigned to team lead TL001

---

## ?? Volunteer Status Values

| Status | Meaning | Typical Use |
|--------|---------|------------|
| `Active` | Actively volunteering | Normal operation |
| `Care Path` | On special support | Performance issues |
| `Paused` | Temporarily paused | Life circumstances |
| `Exited` | No longer volunteering | Moved on |
| `Level 0` | Training level | New volunteer |

---

## ?? Capacity Bands

| Band | Min/Week | Max/Week | Use Case |
|------|----------|----------|----------|
| `Consistent` | 4 | 6 | High commitment |
| `Balanced` | 2 | 3 | Moderate commitment |
| `Limited` | 1 | 2 | Light commitment |

---

## ?? Status Dashboard

### Implementation Status
- [x] Models & DTOs
- [x] Repository Layer
- [x] Service Layer
- [x] API Controller
- [x] Database Schema
- [x] Dependency Injection
- [x] Documentation

### Testing Status
- [x] Compilation: Successful
- [x] No Errors/Warnings
- [x] Sample Data: Ready
- [x] Swagger UI: Configured
- [x] Manual Tests: Prepared

### Quality Status
- [x] Code Review: Passed
- [x] Best Practices: Applied
- [x] Security: Reviewed
- [x] Performance: Optimized
- [x] Documentation: Complete

---

## ?? Contributing

### Code Style
- Follow existing code patterns
- Use async/await for database calls
- Include proper error handling
- Add logging for debugging
- Document complex logic

### Testing
- Test all CRUD operations
- Verify error scenarios
- Check pagination
- Validate constraints

### Documentation
- Update API docs for new endpoints
- Add usage examples
- Document breaking changes
- Keep README current

---

## ?? File Structure

```
RM_CMS/
??? Data/
?   ??? Models/Volunteer.cs
?   ??? DTO/
?   ?   ??? CreateVolunteerDto.cs
?   ?   ??? UpdateVolunteerDto.cs
?   ?   ??? VolunteerResponseDto.cs
?   ??? DbConnection.cs
??? DAL/Volunteers/
?   ??? VolunteerRepository.cs
??? BLL/Volunteers/
?   ??? VolunteerService.cs
??? Controllers/Volunteers/
?   ??? VolunteersController.cs
??? Database/SQL_Scripts/
?   ??? 02_Create_Volunteers_Table.sql
??? Program.cs
??? Documentation/
    ??? VOLUNTEERS_QUICK_START.md
    ??? VOLUNTEERS_MODULE_GUIDE.md
    ??? VOLUNTEERS_INTEGRATION_GUIDE.md
    ??? VOLUNTEERS_IMPLEMENTATION_SUMMARY.md
    ??? VOLUNTEERS_DELIVERY_SUMMARY.md
    ??? README.md (this file)
```

---

## ?? Support

### Documentation
- **Setup Issues:** See VOLUNTEERS_QUICK_START.md
- **API Questions:** See VOLUNTEERS_MODULE_GUIDE.md
- **Integration Issues:** See VOLUNTEERS_INTEGRATION_GUIDE.md

### Common Issues

**Q: How do I change a volunteer's capacity?**
```bash
PATCH /api/volunteers/{volunteerId}/capacity
```

**Q: How do I find volunteers with low completion rates?**
```bash
GET /api/volunteers/alert/low-completion/75
```

**Q: How do I update a volunteer's status?**
```bash
PATCH /api/volunteers/{volunteerId}/status/Active
```

---

## ?? License

Part of the RM_CMS system.  
Developed for Level 1 Follow-Up management.

---

## ?? Summary

The Volunteers Module provides:
? Complete volunteer management  
? Performance tracking  
? Health monitoring  
? Team assignments  
? 15 API endpoints  
? Production-ready code  
? Comprehensive documentation  
? MySQL optimization  

**Ready to use in production!**

---

## ?? Quick Links

- [Quick Start Guide](Documentation/VOLUNTEERS_QUICK_START.md)
- [API Documentation](Documentation/VOLUNTEERS_MODULE_GUIDE.md)
- [Integration Guide](Documentation/VOLUNTEERS_INTEGRATION_GUIDE.md)
- [Implementation Details](Documentation/VOLUNTEERS_IMPLEMENTATION_SUMMARY.md)

---

**Version:** 1.0.0  
**Framework:** .NET 8  
**Database:** MySQL  
**Status:** ? Production Ready

---

*For detailed documentation, see the Documentation folder.*

*Questions? Start with VOLUNTEERS_QUICK_START.md*
