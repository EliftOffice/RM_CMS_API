# Volunteers Module - Integration Guide

## Integration with Existing System

This document explains how the Volunteers module integrates with the existing Visitors module and the overall system architecture.

---

## ?? System Architecture Overview

```
???????????????????????????????????????????????????????
?         RM_CMS APPLICATION (.NET 8)                 ?
?         Using MySQL Database                        ?
???????????????????????????????????????????????????????
                        ?
        ?????????????????????????????????
        ?               ?               ?
    ??????????    ???????????    ???????????
    ? SHARED ?    ? VISITORS ?    ?VOLUNTEERS?
    ? CONFIG ?    ? MODULE   ?    ? MODULE   ?
    ??????????    ????????????    ????????????
        ?               ?               ?
    ?????????????????????????????????????????
    ?  DbConnectionFactory (Shared)        ?
    ?  - Connection string configuration   ?
    ?  - MySQL connection management       ?
    ????????????????????????????????????????
        ?
        ?
    MySQL Database
    ???????????????????????
    ? PEOPLE TABLE        ?
    ???????????????????????
    ? assigned_volunteer  ? ???? FK Reference
    ???????????????????????
        ?
        ?
    ???????????????????????
    ? VOLUNTEERS TABLE    ?
    ???????????????????????
    ? volunteer_id (PK)   ?
    ? team_lead (FK)      ?
    ???????????????????????
```

---

## ?? Data Flow: Person to Volunteer Assignment

### Workflow
```
1. Person is created in PEOPLE table
2. Person status: "New"
3. Assign volunteer: Update people.assigned_volunteer = V001
4. Volunteer receives assignment
5. Volunteer updates performance metrics
6. Track completion rate and response time
7. Monitor burnout risk from check-ins
```

### Database Relationship
```sql
-- PEOPLE table has reference to VOLUNTEERS
ALTER TABLE people
ADD CONSTRAINT fk_people_volunteer
FOREIGN KEY (assigned_volunteer) 
REFERENCES volunteers(volunteer_id)
ON DELETE SET NULL
ON UPDATE CASCADE;
```

---

## ?? Shared Components

### 1. DbConnectionFactory (Shared)
Located in: `Data/DbConnection.cs`

```csharp
// Shared between both modules
builder.Services.AddScoped<IDbConnectionFactory, DbConnectionFactory>();

// Used by both repositories
public IDbConnection GetConnection()
{
    var connectionString = _configuration.GetConnectionString("DefaultConnection");
    return new MySqlConnection(connectionString);  // MySQL connection
}
```

### 2. Configuration

Same connection string used for both modules:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyAppDb;User=root;Password=your_password;"
  }
}
```

---

## ?? Project Structure

```
RM_CMS/
??? Data/
?   ??? Models/
?   ?   ??? People.cs              ???? VISITORS
?   ?   ??? Volunteer.cs           ???? VOLUNTEERS (NEW)
?   ?
?   ??? DTO/
?   ?   ??? CreatePeopleDto.cs     ???? VISITORS
?   ?   ??? UpdatePeopleDto.cs     ???? VISITORS
?   ?   ??? PeopleResponseDto.cs   ???? VISITORS
?   ?   ??? CreateVolunteerDto.cs  ???? VOLUNTEERS (NEW)
?   ?   ??? UpdateVolunteerDto.cs  ???? VOLUNTEERS (NEW)
?   ?   ??? VolunteerResponseDto.cs???? VOLUNTEERS (NEW)
?   ?
?   ??? DbConnection.cs            ???? SHARED
?
??? DAL/
?   ??? Visitors/
?   ?   ??? PeopleRepository.cs    ???? VISITORS
?   ?
?   ??? Volunteers/               ???? VOLUNTEERS (NEW)
?       ??? VolunteerRepository.cs
?
??? BLL/
?   ??? Visitors/
?   ?   ??? PeopleService.cs      ???? VISITORS
?   ?
?   ??? Volunteers/              ???? VOLUNTEERS (NEW)
?       ??? VolunteerService.cs
?
??? Controllers/
?   ??? Visitors/
?   ?   ??? PeoplesController.cs  ???? VISITORS
?   ?
?   ??? Volunteers/              ???? VOLUNTEERS (NEW)
?       ??? VolunteersController.cs
?
??? Database/
?   ??? SQL_Scripts/
?       ??? 01_Create_People_Table.sql        ???? VISITORS
?       ??? 02_Create_Volunteers_Table.sql    ???? VOLUNTEERS (NEW)
?
??? Program.cs                   ???? SHARED (UPDATED)
??? Documentation/
    ??? VISITORS_MODULE_GUIDE.md
    ??? VOLUNTEERS_MODULE_GUIDE.md            ???? NEW
    ??? ...
```

---

## ?? Dependency Injection Configuration

### Program.cs Setup
```csharp
// Shared Connection Factory
builder.Services.AddScoped<IDbConnectionFactory, DbConnectionFactory>();

// VISITORS Module
builder.Services.AddScoped<IPeopleRepository, PeopleRepository>();
builder.Services.AddScoped<IPeopleService, PeopleService>();

// VOLUNTEERS Module (NEW)
builder.Services.AddScoped<IVolunteerRepository, VolunteerRepository>();
builder.Services.AddScoped<IVolunteerService, VolunteerService>();
```

---

## ?? API Endpoints (Both Modules)

### Visitors Endpoints
```
GET    /api/peoples
GET    /api/peoples/{peopleId}
GET    /api/peoples/status/{status}
GET    /api/peoples/paginated/data
POST   /api/peoples
PUT    /api/peoples/{peopleId}
DELETE /api/peoples/{peopleId}
PATCH  /api/peoples/{peopleId}/status/{status}
```

### Volunteers Endpoints (NEW)
```
GET    /api/volunteers
GET    /api/volunteers/{volunteerId}
GET    /api/volunteers/status/{status}
GET    /api/volunteers/team-lead/{teamLeadId}
GET    /api/volunteers/capacity/{capacityBand}
GET    /api/volunteers/paginated/data
GET    /api/volunteers/count/active
GET    /api/volunteers/alert/low-completion/{threshold}
GET    /api/volunteers/count/total
POST   /api/volunteers
PUT    /api/volunteers/{volunteerId}
DELETE /api/volunteers/{volunteerId}
PATCH  /api/volunteers/{volunteerId}/status/{status}
PATCH  /api/volunteers/{volunteerId}/capacity
PATCH  /api/volunteers/{volunteerId}/performance
PATCH  /api/volunteers/{volunteerId}/check-in
```

---

## ?? Usage Scenarios

### Scenario 1: Create Follow-Up Assignment

**Step 1: Create a Person**
```bash
POST /api/peoples
{
  "firstName": "John",
  "lastName": "Doe",
  "email": "john@example.com",
  "phoneNumber": "555-0001",
  "visitType": "First-Time Visitor",
  "firstVisitDate": "2025-01-25"
}
```

**Step 2: Assign a Volunteer**
```bash
PUT /api/peoples/{personId}
{
  "followUpStatus": "Assigned",
  "assignedVolunteer": "V001",
  "assignedDate": "2025-01-25"
}
```

**Step 3: Check Volunteer Performance**
```bash
GET /api/volunteers/V001
```

Response includes:
- Total assignments
- Current assignments
- Completion rate
- Performance metrics

---

### Scenario 2: Monitor Volunteer Health

**Step 1: Get Volunteers with Burnout Risk**
```bash
GET /api/volunteers/status/Active
```

Response includes emotional tone and burnout risk for each.

**Step 2: Update Volunteer Check-In**
```bash
PATCH /api/volunteers/V001/check-in
{
  "lastCheckIn": "2025-01-25T10:30:00Z",
  "nextCheckIn": "2025-02-25T10:30:00Z"
}
```

**Step 3: Monitor Low Performers**
```bash
GET /api/volunteers/alert/low-completion/80
```

Returns volunteers below 80% completion rate.

---

### Scenario 3: Adjust Volunteer Capacity

**Current Capacity:**
```bash
GET /api/volunteers/capacity/Consistent
```

**Reduce Capacity:**
```bash
PATCH /api/volunteers/V002/capacity
{
  "capacityBand": "Balanced",
  "capacityMin": 2,
  "capacityMax": 3
}
```

**Verify Change:**
```bash
GET /api/volunteers/V002
```

---

## ??? Database Integration Points

### Foreign Key Relationship (to be added when Team Leads created)

```sql
-- Current (existing)
ALTER TABLE people
ADD CONSTRAINT fk_people_volunteer
FOREIGN KEY (assigned_volunteer) 
REFERENCES volunteers(volunteer_id);

-- Future (when Team Leads created)
ALTER TABLE volunteers
ADD CONSTRAINT fk_volunteer_team_lead
FOREIGN KEY (team_lead) 
REFERENCES team_leads(team_lead_id);
```

---

## ?? Common Queries (Cross-Module)

### Query 1: Active Assignments
```sql
SELECT 
    p.person_id,
    CONCAT(p.first_name, ' ', p.last_name) as person_name,
    v.volunteer_id,
    CONCAT(v.first_name, ' ', v.last_name) as volunteer_name,
    p.follow_up_status,
    v.current_assignments
FROM people p
JOIN volunteers v ON p.assigned_volunteer = v.volunteer_id
WHERE p.follow_up_status = 'Assigned'
  AND v.status = 'Active'
ORDER BY v.current_assignments DESC;
```

### Query 2: Volunteer Performance Report
```sql
SELECT 
    v.volunteer_id,
    CONCAT(v.first_name, ' ', v.last_name) as volunteer_name,
    COUNT(DISTINCT p.person_id) as total_assigned,
    v.total_completed as completed,
    v.completion_rate,
    v.capacity_band,
    v.burnout_risk
FROM volunteers v
LEFT JOIN people p ON v.volunteer_id = p.assigned_volunteer
WHERE v.status = 'Active'
GROUP BY v.volunteer_id
ORDER BY v.completion_rate DESC;
```

### Query 3: Overloaded Volunteers Alert
```sql
SELECT 
    v.volunteer_id,
    CONCAT(v.first_name, ' ', v.last_name) as volunteer_name,
    v.current_assignments,
    v.capacity_max,
    (v.current_assignments - v.capacity_max) as over_capacity,
    COUNT(p.person_id) as actual_assigned
FROM volunteers v
LEFT JOIN people p ON v.volunteer_id = p.assigned_volunteer 
    AND p.follow_up_status IN ('Assigned', 'Contacted')
WHERE v.current_assignments > v.capacity_max
  AND v.status = 'Active'
GROUP BY v.volunteer_id
ORDER BY over_capacity DESC;
```

---

## ?? Data Integrity

### Cascade Rules (When Team Leads Created)
```sql
-- Soft delete approach recommended
ALTER TABLE volunteers
ADD COLUMN is_deleted BOOLEAN DEFAULT FALSE;

-- When Team Lead is deleted
UPDATE volunteers
SET is_deleted = TRUE
WHERE team_lead = @TeamLeadId;
```

### Constraints Implemented
? Email uniqueness on volunteers  
? vNPS score check (0-10)  
? Status validation  
? Capacity band validation  
? Foreign key to People (assignment)  

---

## ?? Migration Path

### Phase 1: Current (Volunteers Module Ready)
- [x] Visitors module implemented
- [x] Volunteers module implemented
- [ ] Both modules tested

### Phase 2: Team Leads (Next)
- [ ] Create Team Leads table
- [ ] Link Volunteers to Team Leads
- [ ] Create Team Leads API

### Phase 3: Check-Ins (Week 3)
- [ ] Create Check-Ins table
- [ ] Link to Volunteers
- [ ] Create Check-Ins API

### Phase 4: Escalations (Week 3)
- [ ] Create Escalations table
- [ ] Link to People, Volunteers, Follow-Ups
- [ ] Create Escalations API

### Phase 5: Follow-Ups (Week 4)
- [ ] Create Follow-Ups table
- [ ] Link all entities
- [ ] Create Follow-Ups API

### Phase 6: Complete System
- [ ] All tables with relationships
- [ ] Dashboard views
- [ ] Performance queries
- [ ] Integration testing

---

## ?? Testing Integration

### Unit Tests
```csharp
[TestClass]
public class VolunteerServiceTests
{
    [TestMethod]
    public async Task CreateVolunteer_ShouldGenerateUniqueId()
    {
        // Arrange
        var service = new VolunteerService(repository);
        var dto = new CreateVolunteerDto { /* ... */ };
        
        // Act
        var result = await service.CreateAsync(dto);
        
        // Assert
        Assert.IsNotNull(result.VolunteerId);
        Assert.IsTrue(result.VolunteerId.StartsWith("V"));
    }
}
```

### Integration Tests
```csharp
[TestClass]
public class VolunteerIntegrationTests
{
    [TestMethod]
    public async Task AssignVolunteerToPerson_ShouldUpdateBothRecords()
    {
        // Arrange
        var volunteer = await volunteerService.CreateAsync(volunteerDto);
        var person = await peopleService.CreateAsync(peopleDto);
        
        // Act
        var updated = await peopleService.UpdateAsync(person.PersonId, 
            new UpdatePeopleDto { AssignedVolunteer = volunteer.VolunteerId });
        
        // Assert
        Assert.AreEqual(volunteer.VolunteerId, updated.AssignedVolunteer);
    }
}
```

---

## ?? Checklist for Integration

- [ ] Both modules compile without errors
- [ ] Both modules have dependency injection configured
- [ ] MySQL connection string works for both
- [ ] Sample data loads successfully
- [ ] All Visitors endpoints work
- [ ] All Volunteers endpoints work
- [ ] Cross-module queries return correct data
- [ ] Error handling works correctly
- [ ] Logging captures both modules' activities
- [ ] Swagger UI shows both modules' endpoints
- [ ] Hot reload works for both modules
- [ ] No conflicts in route names

---

## ?? Next Steps

### Immediate (Today)
1. ? Set up MySQL database
2. ? Create both tables
3. ? Test individual modules
4. ? Review integration points

### Short Term (This Week)
1. Run integration tests
2. Verify foreign keys work
3. Test assignment workflow
4. Monitor performance

### Medium Term (Next Week)
1. Create Team Leads module
2. Implement team relationships
3. Create Check-Ins module
4. Build dashboard views

---

## ?? Troubleshooting

### Issue: Foreign Key Constraint Fails
```
Error: Cannot add or update a child row: a foreign key constraint fails

Solution: Ensure volunteer exists before assigning to person
```

### Issue: Connection String Not Used
```
Error: Connection to database failed

Solution: Check appsettings.Development.json for DefaultConnection
```

### Issue: Duplicate Email Error
```
Error: Duplicate entry for key 'email'

Solution: Volunteers.Email field is UNIQUE - use different email
```

---

## ?? Support Resources

| Topic | Location |
|-------|----------|
| Visitors Setup | QUICK_START_GUIDE.md |
| Volunteers Setup | VOLUNTEERS_QUICK_START.md |
| Visitors API | VISITORS_MODULE_GUIDE.md |
| Volunteers API | VOLUNTEERS_MODULE_GUIDE.md |
| Database Schema | Database/SQL_Scripts/*.sql |

---

## ? Summary

The Volunteers module is now fully integrated with:
- ? Shared connection factory
- ? Same MySQL database
- ? Consistent architecture
- ? Similar API patterns
- ? Related to Visitors module via People.assigned_volunteer

**System Status:** Ready for Team Leads and advanced modules

---

**Version:** 1.0.0  
**Last Updated:** January 25, 2025  
**Status:** Integration Complete ?
