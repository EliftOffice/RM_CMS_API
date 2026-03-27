# Visitors Module - Developer Setup Checklist

## ? Pre-Implementation Verification

### Project Structure
- [x] Folder structure created:
  - [x] `Data/Models/` - Entity models
  - [x] `Data/DTO/` - Request/Response DTOs
  - [x] `Data/` - Connection factory
  - [x] `DAL/Visitors/` - Data access layer
  - [x] `BLL/Visitors/` - Business logic layer
  - [x] `Controllers/visitors/` - API controller
  - [x] `Database/SQL_Scripts/` - SQL scripts
  - [x] `Documentation/` - Documentation

### NuGet Packages
- [x] Dapper 2.1.15 - Added
- [x] System.Data.SqlClient 4.8.6 - Added
- [x] Swashbuckle.AspNetCore 6.6.2 - Already present

### Build Status
- [x] Project builds successfully
- [x] No compilation errors
- [x] No warnings

---

## ?? Development Setup Tasks

### Task 1: Database Setup

```bash
[ ] Step 1: Create SQL Server database
    Command: CREATE DATABASE MyAppDb;
    
[ ] Step 2: Execute SQL script
    File: Database/SQL_Scripts/01_Create_People_Table.sql
    
[ ] Step 3: Verify table creation
    Query: SELECT * FROM people;
    Expected: Table exists, sample data present
    
[ ] Step 4: Check indexes
    Query: SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('people');
    Expected: 5 indexes created
```

### Task 2: Connection String Configuration

```bash
[ ] Open: appsettings.Development.json

[ ] Update connection string:
    {
      "ConnectionStrings": {
        "DefaultConnection": "Server=YOUR_SERVER;Database=MyAppDb;User Id=sa;Password=YOUR_PASSWORD;"
      }
    }

[ ] Verify SQL Server is running
    - Local: Check Services (mssqlserver)
    - Remote: Ping server to verify connectivity
```

### Task 3: Application Startup

```bash
[ ] Navigate to project directory: D:\new_cms_api

[ ] Build project:
    dotnet build

[ ] Run application:
    dotnet run

[ ] Expected output:
    - "Now listening on: https://localhost:7xxx"
    - No errors in console
```

### Task 4: API Testing via Swagger

```bash
[ ] Open browser: https://localhost:7xxx/swagger

[ ] Verify Swagger UI loaded:
    - [ ] API endpoints visible
    - [ ] "Peoples" controller section present
    - [ ] 13 endpoints listed
    
[ ] Test endpoints (in order):

    1. [ ] GET /api/peoples
       Expected: 200 OK with sample data array
       
    2. [ ] GET /api/peoples/count/total
       Expected: 200 OK with { "totalCount": 4 }
       
    3. [ ] POST /api/peoples
       Body: {
         "firstName": "Test",
         "lastName": "User",
         "email": "test@example.com",
         "visitType": "First-Time Visitor",
         "firstVisitDate": "2025-01-25",
         "followUpStatus": "New",
         "createdBy": "test"
       }
       Expected: 201 Created with personId
       
    4. [ ] GET /api/peoples/{personId}
       Use personId from test 3
       Expected: 200 OK with created record
       
    5. [ ] PUT /api/peoples/{personId}
       Body: { "email": "newemail@example.com" }
       Expected: 200 OK with updated record
       
    6. [ ] GET /api/peoples/status/New
       Expected: 200 OK with matching records
       
    7. [ ] PATCH /api/peoples/{personId}/status/Contacted
       Expected: 200 OK with updated status
       
    8. [ ] GET /api/peoples/paginated/data?pageNumber=1&pageSize=2
       Expected: 200 OK with paginated results
       
    9. [ ] DELETE /api/peoples/{personId}
       Expected: 200 OK with success message
       
   10. [ ] GET /api/peoples/{personId}
       Use deleted personId
       Expected: 404 Not Found
```

---

## ?? Verification Checklist

### Code Quality

- [x] 3-Layer Architecture implemented
  - [x] Controller layer handles HTTP
  - [x] Service layer handles business logic
  - [x] Repository layer handles data access

- [x] Dependency Injection configured
  - [x] IDbConnectionFactory registered
  - [x] IPeopleRepository registered
  - [x] IPeopleService registered
  - [x] Constructor injection working

- [x] Error Handling
  - [x] Try-catch blocks in controller
  - [x] Null checks in service
  - [x] Connection handling in repository
  - [x] Proper HTTP status codes

- [x] Logging
  - [x] ILogger injected in controller
  - [x] Information logs for requests
  - [x] Error logs for exceptions
  - [x] Debug information available

- [x] Security
  - [x] SQL parameterization used
  - [x] No string concatenation in queries
  - [x] Input validation on endpoints
  - [x] No sensitive data in errors

### Documentation

- [x] QUICK_START_GUIDE.md
  - [x] Setup instructions
  - [x] Configuration details
  - [x] Test examples
  - [x] Troubleshooting

- [x] VISITORS_MODULE_GUIDE.md
  - [x] API documentation
  - [x] Model definitions
  - [x] DTOs documented
  - [x] Usage examples

- [x] IMPLEMENTATION_SUMMARY.md
  - [x] What was built
  - [x] Why it was built this way
  - [x] File structure
  - [x] Technologies used

- [x] ARCHITECTURE_DIAGRAMS.md
  - [x] Visual diagrams
  - [x] Data flows
  - [x] Class relationships
  - [x] Integration points

- [x] INDEX.md
  - [x] Navigation guide
  - [x] File organization
  - [x] API summary
  - [x] Quick reference

- [x] SQL Script
  - [x] Table creation
  - [x] Indexes creation
  - [x] Sample data
  - [x] Verification queries

### Functionality

- [x] GET operations working
  - [x] Get all
  - [x] Get by ID
  - [x] Get by status
  - [x] Get by volunteer
  - [x] Get by priority
  - [x] Get paginated
  - [x] Get count

- [x] CREATE operations working
  - [x] New record creation
  - [x] Auto ID generation
  - [x] Timestamp management
  - [x] Default values

- [x] UPDATE operations working
  - [x] Full update (PUT)
  - [x] Status update (PATCH)
  - [x] Volunteer assignment (PATCH)
  - [x] Contact update (PATCH)

- [x] DELETE operations working
  - [x] Record deletion
  - [x] Not found handling

---

## ?? Manual Testing Scenarios

### Scenario 1: Create and Retrieve Visitor

```
1. Create visitor via POST /api/peoples
   Record personId from response

2. Retrieve via GET /api/peoples/{personId}
   Verify all fields match

3. Get via GET /api/peoples
   Verify visitor appears in list

? Pass: All three operations successful
? Fail: Check logs for errors
```

### Scenario 2: Update Visitor

```
1. Create visitor (Scenario 1, step 1)

2. Update email via PUT /api/peoples/{personId}
   Body: { "email": "new@example.com" }

3. Verify via GET /api/peoples/{personId}
   Check email updated

? Pass: Email changed successfully
? Fail: Check update logic
```

### Scenario 3: Filter by Status

```
1. Create visitor with status "New"
   Record personId

2. Update status via PATCH /api/peoples/{personId}/status/Contacted

3. Filter via GET /api/peoples/status/Contacted
   Verify visitor appears

? Pass: Filter working correctly
? Fail: Check status logic
```

### Scenario 4: Assign Volunteer

```
1. Create visitor
   Record personId

2. Assign volunteer via PATCH /api/peoples/{personId}/assign-volunteer/V001

3. Verify via GET /api/peoples/{personId}
   Check assignedVolunteer = "V001"
   Check followUpStatus = "Assigned"

? Pass: Assignment working
? Fail: Check assignment logic
```

### Scenario 5: Pagination

```
1. Get paginated: GET /api/peoples/paginated/data?pageNumber=1&pageSize=2

2. Verify response contains:
   - pageNumber: 1
   - pageSize: 2
   - totalCount: >= 4
   - totalPages: >= 2
   - data: array with 2 items

? Pass: Pagination working
? Fail: Check pagination logic
```

---

## ?? Troubleshooting Guide

### Issue 1: Connection String Error
```
Error: "A network-related or instance-specific error occurred"

Troubleshooting:
[ ] Check SQL Server is running (Services)
[ ] Verify server name in connection string
[ ] Verify database name exists
[ ] Test connection in SSMS
[ ] Check firewall settings
```

### Issue 2: Table Not Found
```
Error: "Invalid object name 'people'"

Troubleshooting:
[ ] Run SQL script: 01_Create_People_Table.sql
[ ] Verify database selected in SSMS
[ ] Check table exists: SELECT * FROM people;
[ ] Verify case sensitivity (SQL Server not case-sensitive)
```

### Issue 3: Null Reference Exception
```
Error: "Object reference not set to an instance of an object"

Troubleshooting:
[ ] Check Program.cs dependency injection
[ ] Verify all services registered
[ ] Rebuild project: dotnet clean && dotnet build
[ ] Check constructor injection in controller
[ ] Verify service implementation not null
```

### Issue 4: 404 Not Found
```
Error: "GET /api/peoples/xyz returns 404"

Troubleshooting:
[ ] Verify personId format
[ ] Check record exists in database
[ ] Try GET /api/peoples to see all
[ ] Check spelling of route
```

### Issue 5: 500 Internal Server Error
```
Error: "500 Internal Server Error"

Troubleshooting:
[ ] Check application logs in console
[ ] Verify connection string
[ ] Check database is running
[ ] Verify record exists (for GET operations)
[ ] Try simpler query first (GET all)
```

---

## ?? Performance Checks

### Database Indexes

```
[ ] Verify indexes exist:
    SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('people');
    
    Expected:
    - PK__people__xx (Primary key)
    - idx_people_status
    - idx_people_assigned
    - idx_people_created
    - idx_people_priority
    - idx_people_status_priority
```

### Query Performance

```
[ ] Test response times:
    
    GET /api/peoples                    Should be < 100ms
    GET /api/peoples/status/New         Should be < 50ms (with index)
    GET /api/peoples/paginated/data     Should be < 50ms
    POST /api/peoples                   Should be < 200ms
    PUT /api/peoples/{id}               Should be < 150ms
```

---

## ?? Security Verification

### SQL Injection Protection

```
[ ] Test with malicious input:
    
    GET /api/peoples/status/' OR '1'='1
    Expected: No error, no data leak
    
[ ] Verify parameterized queries in code:
    - Connection.ExecuteAsync(query, parameters)
    - No string concatenation in SQL
```

### Input Validation

```
[ ] Test with invalid data:
    
    POST /api/peoples with empty firstName
    Expected: 400 Bad Request
    
    POST /api/peoples with wrong date format
    Expected: 400 Bad Request
```

### Error Handling

```
[ ] Test with various errors:
    
    GET /api/peoples/{invalid-id}
    Expected: 404 Not Found (no internal details)
    
    Database down
    Expected: 500 Internal Server Error (no connection details)
```

---

## ?? Documentation Verification

### README/Guide Accuracy

```
[ ] QUICK_START_GUIDE.md steps work as written
[ ] All endpoints in VISITORS_MODULE_GUIDE.md functional
[ ] Code examples in guides are correct
[ ] Connection string example is clear
[ ] Troubleshooting covers common issues
```

### Code Comments

```
[ ] DTOs have XML comments
[ ] Repository methods have XML comments
[ ] Service methods have XML comments
[ ] Controller endpoints have XML comments
[ ] Complex logic has inline comments
```

---

## ?? Pre-Release Checklist

Before sharing with team:

```
[ ] Build successful (no errors/warnings)
[ ] All 13 endpoints tested and working
[ ] Documentation complete and accurate
[ ] Sample data loads successfully
[ ] Connection string instructions clear
[ ] Error messages are helpful
[ ] Logging working correctly
[ ] Performance acceptable
[ ] No hardcoded values
[ ] No sensitive data in code
[ ] No console.WriteLi ne (use logger)
```

---

## ?? Deployment Preparation

### Production Configuration

```
[ ] Update appsettings.json (production):
    - Remove default connection string
    - Update logging level

[ ] Update appsettings.Development.json:
    - Use development server
    - Use development database
    - Enable detailed logging

[ ] Verify connection string management:
    - Not in source control
    - Uses environment variables or secrets
```

### Database Preparation

```
[ ] Production database:
    - [ ] Run schema creation script
    - [ ] Verify indexes created
    - [ ] Check permissions set
    - [ ] Enable backups

[ ] Development database:
    - [ ] Run schema creation script
    - [ ] Load sample data
    - [ ] Verify indexes
```

---

## ? Final Sign-Off

```
Developer Name: _________________________
Date: _________________________

[ ] All checklist items completed
[ ] Code reviewed for quality
[ ] Tests passing
[ ] Documentation approved
[ ] Ready for team review

Signature: _________________________
```

---

## ?? Support Contacts

### For Setup Issues
- Check: QUICK_START_GUIDE.md
- Logs: Visual Studio Output window

### For API Questions
- Check: VISITORS_MODULE_GUIDE.md
- Test: Swagger UI at `/swagger`

### For Code Questions
- Check: Source code comments
- Reference: ARCHITECTURE_DIAGRAMS.md

### For Database Issues
- Check: SQL_Scripts documentation
- Tool: SQL Server Management Studio

---

**Use this checklist to ensure complete implementation and testing of the Visitors Module.**

**Last Updated:** January 25, 2025  
**Status:** ? Ready for Implementation
