# Visitors Module - Quick Start Guide

## Setup Instructions

### 1. Database Setup

#### Step 1a: Create Database (SQL Server)
```sql
CREATE DATABASE MyAppDb;
```

#### Step 1b: Create People Table
Run the SQL script: `Database/SQL_Scripts/01_Create_People_Table.sql`

This will:
- Create the `people` table
- Create indexes for performance
- Insert sample data
- Display verification queries

### 2. Update Connection String

In `appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=MyAppDb;User Id=YOUR_USER;Password=YOUR_PASSWORD;"
  }
}
```

### 3. Install Dependencies
The project already has these packages installed:
- ? Dapper (2.1.15)
- ? System.Data.SqlClient (4.8.6)
- ? Swashbuckle.AspNetCore (6.6.2)

### 4. Run the Application
```bash
dotnet run
```

### 5. Access Swagger UI
Navigate to: `https://localhost:7xxx/swagger`

## Quick Test Workflow

### Test 1: Create a Visitor
```bash
curl -X POST "https://localhost:7xxx/api/peoples" \
  -H "Content-Type: application/json" \
  -d '{
    "firstName": "John",
    "lastName": "Doe",
    "email": "john@example.com",
    "phone": "555-1234",
    "visitType": "First-Time Visitor",
    "firstVisitDate": "2025-01-25",
    "followUpStatus": "New",
    "campus": "Main Campus",
    "createdBy": "admin"
  }'
```

### Test 2: Get All Visitors
```bash
curl "https://localhost:7xxx/api/peoples"
```

### Test 3: Get Paginated Results
```bash
curl "https://localhost:7xxx/api/peoples/paginated/data?pageNumber=1&pageSize=5"
```

### Test 4: Update Visitor Status
```bash
curl -X PATCH "https://localhost:7xxx/api/peoples/P001/status/Contacted"
```

### Test 5: Assign Volunteer
```bash
curl -X PATCH "https://localhost:7xxx/api/peoples/P001/assign-volunteer/V001"
```

## File Structure Overview

```
RM_CMS/
?
??? Data/
?   ??? Models/
?   ?   ??? People.cs                    ? Core entity model
?   ??? DTO/
?   ?   ??? CreatePeopleDto.cs          ? Request DTO
?   ?   ??? UpdatePeopleDto.cs          ? Update DTO
?   ?   ??? PeopleResponseDto.cs        ? Response DTO
?   ??? DbConnection.cs                 ? DB connection factory
?
??? DAL/
?   ??? Visitors/
?       ??? PeopleRepository.cs         ? Data access layer
?
??? BLL/
?   ??? Visitors/
?       ??? PeopleService.cs            ? Business logic layer
?
??? Controllers/
?   ??? visitors/
?       ??? PeoplesController.cs        ? API endpoints
?
??? Database/
?   ??? SQL_Scripts/
?       ??? 01_Create_People_Table.sql  ? DB schema
?
??? Documentation/
?   ??? VISITORS_MODULE_GUIDE.md        ? Detailed docs
?
??? Program.cs                           ? Dependency injection setup
??? RM_CMS.csproj                        ? Project file
??? appsettings.json                     ? Configuration

```

## Key API Endpoints Summary

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/peoples` | Get all visitors |
| GET | `/api/peoples/{id}` | Get visitor by ID |
| GET | `/api/peoples/status/{status}` | Filter by status |
| GET | `/api/peoples/volunteer/{id}` | Get volunteer's assignments |
| GET | `/api/peoples/priority/{priority}` | Filter by priority |
| GET | `/api/peoples/paginated/data` | Get paginated results |
| POST | `/api/peoples` | Create new visitor |
| PUT | `/api/peoples/{id}` | Update visitor |
| DELETE | `/api/peoples/{id}` | Delete visitor |
| PATCH | `/api/peoples/{id}/status/{status}` | Update status |
| PATCH | `/api/peoples/{id}/assign-volunteer/{vid}` | Assign volunteer |
| PATCH | `/api/peoples/{id}/contact` | Update contact info |

## Common Issues & Solutions

### Issue 1: Connection String Error
**Error:** "A network-related or instance-specific error"
**Solution:** 
- Verify SQL Server is running
- Check connection string in `appsettings.Development.json`
- Ensure database exists

### Issue 2: Table Not Found
**Error:** "Invalid object name 'people'"
**Solution:**
- Run the SQL script: `01_Create_People_Table.sql`
- Verify database and table were created

### Issue 3: Null Reference Exception
**Error:** "Object reference not set to an instance of an object"
**Solution:**
- Check dependency injection in `Program.cs`
- Verify all services are registered
- Rebuild the project

## Configuration Files

### appsettings.json (Production)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### appsettings.Development.json (Development)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyAppDb;User Id=sa;Password=Your_password123;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## Next Steps

1. ? Create database and run SQL script
2. ? Update connection string
3. ? Run application
4. ? Test endpoints in Swagger
5. ?? Create Volunteers module
6. ?? Create Follow-Ups module
7. ?? Create Team Leads module

## Support Resources

- **API Documentation:** `/Documentation/VISITORS_MODULE_GUIDE.md`
- **Database Schema:** `/Database/SQL_Scripts/01_Create_People_Table.sql`
- **Full System Docs:** `/Documentation/Docs/??? COMPLETE DATABASE SCHEMA.md`

## Development Checklist

- ? Models created (People.cs)
- ? DTOs created (CreatePeopleDto, UpdatePeopleDto, PeopleResponseDto)
- ? Repository layer implemented (PeopleRepository.cs)
- ? Service layer implemented (PeopleService.cs)
- ? Controller implemented (PeoplesController.cs)
- ? Dependency injection configured (Program.cs)
- ? SQL script provided (01_Create_People_Table.sql)
- ? API documentation (VISITORS_MODULE_GUIDE.md)
- ?? Unit tests
- ?? Integration tests

---

**Module Status:** ? Ready for Development

**Version:** 1.0.0  
**Last Updated:** January 25, 2025
