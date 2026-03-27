# Volunteers Module - Quick Start Guide (MySQL)

## Setup Instructions

### 1. Database Setup

#### Step 1a: Create Database (MySQL)
```sql
CREATE DATABASE IF NOT EXISTS MyAppDb;
USE MyAppDb;
```

#### Step 1b: Create Volunteers Table
Run the SQL script: `Database/SQL_Scripts/02_Create_Volunteers_Table.sql`

This will:
- Create the `volunteers` table with all 28 fields
- Create 6 performance indexes
- Insert sample data (4 volunteers)
- Display verification queries

### 2. Update Connection String

In `appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyAppDb;User=root;Password=your_mysql_password;"
  }
}
```

**For Remote MySQL:**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=192.168.1.100;Database=MyAppDb;User=cms_user;Password=strong_password;Port=3306;"
  }
}
```

### 3. Run the Application
```bash
dotnet run
```

### 4. Access Swagger UI
Navigate to: `https://localhost:7xxx/swagger`

---

## Quick Test Workflow

### Test 1: Create a Volunteer
```bash
curl -X POST "https://localhost:7xxx/api/volunteers" \
  -H "Content-Type: application/json" \
  -d '{
    "firstName": "David",
    "lastName": "Brown",
    "email": "david.brown@church.com",
    "phone": "555-1006",
    "status": "Active",
    "level": "Level 1",
    "startDate": "2025-01-20",
    "capacityBand": "Consistent",
    "capacityMin": 4,
    "capacityMax": 6,
    "teamLead": "TL001",
    "campus": "Main Campus"
  }'
```

### Test 2: Get All Volunteers
```bash
curl "https://localhost:7xxx/api/volunteers"
```

### Test 3: Get Paginated Results
```bash
curl "https://localhost:7xxx/api/volunteers/paginated/data?pageNumber=1&pageSize=5"
```

### Test 4: Get Active Volunteers Count
```bash
curl "https://localhost:7xxx/api/volunteers/count/active"
```

### Test 5: Update Volunteer Status
```bash
curl -X PATCH "https://localhost:7xxx/api/volunteers/V001/status/Care%20Path"
```

### Test 6: Update Volunteer Capacity
```bash
curl -X PATCH "https://localhost:7xxx/api/volunteers/V001/capacity" \
  -H "Content-Type: application/json" \
  -d '{
    "capacityBand": "Limited",
    "capacityMin": 1,
    "capacityMax": 2
  }'
```

### Test 7: Update Performance Metrics
```bash
curl -X PATCH "https://localhost:7xxx/api/volunteers/V001/performance" \
  -H "Content-Type: application/json" \
  -d '{
    "totalCompleted": 48,
    "totalAssigned": 52
  }'
```

### Test 8: Get Volunteers with Low Completion
```bash
curl "https://localhost:7xxx/api/volunteers/alert/low-completion/80"
```

---

## Key API Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/volunteers` | Get all |
| GET | `/api/volunteers/{id}` | Get by ID |
| GET | `/api/volunteers/status/{status}` | Filter by status |
| GET | `/api/volunteers/team-lead/{id}` | Get by team lead |
| GET | `/api/volunteers/capacity/{band}` | Filter by capacity |
| GET | `/api/volunteers/paginated/data` | Paginated list |
| GET | `/api/volunteers/count/active` | Active count |
| GET | `/api/volunteers/alert/low-completion/{threshold}` | Performance alert |
| GET | `/api/volunteers/count/total` | Total count |
| POST | `/api/volunteers` | Create |
| PUT | `/api/volunteers/{id}` | Update |
| DELETE | `/api/volunteers/{id}` | Delete |
| PATCH | `/api/volunteers/{id}/status/{status}` | Update status |
| PATCH | `/api/volunteers/{id}/capacity` | Update capacity |
| PATCH | `/api/volunteers/{id}/performance` | Update performance |

---

## Status Values

- `Active` - Actively volunteering
- `Care Path` - On special support
- `Paused` - Temporarily paused
- `Exited` - No longer volunteering
- `Level 0` - Training level

## Capacity Bands

- `Consistent` - 4-6 follow-ups per week
- `Balanced` - 2-3 follow-ups per week
- `Limited` - 1-2 follow-ups per week

## Emotional Tone

- `??` - Happy
- `??` - Neutral
- `??` - Struggling

## Burnout Risk

- `Low` - Healthy status
- `Medium` - Monitor closely
- `High` - Needs intervention

---

## Sample Volunteer Data

### V001 - Sarah Johnson
- Status: Active
- Level: Level 1
- Capacity: Balanced (2-3/week)
- Performance: 90.38% completion
- Emotional Tone: ?? (Happy)
- vNPS: 9/10

### V002 - Mike Thompson
- Status: Active
- Level: Level 1
- Capacity: Consistent (4-6/week)
- Performance: 80% completion
- Emotional Tone: ?? (Neutral)
- vNPS: 7/10
- Burnout Risk: Medium

### V003 - Emily Davis
- Status: Active
- Level: Level 0 (Training)
- Capacity: Limited (1-2/week)
- Performance: 62.5% completion (new)
- Emotional Tone: ?? (Happy)
- vNPS: 8/10

### V004 - James Wilson
- Status: Care Path (needs support)
- Level: Level 1
- Capacity: Balanced (2-3/week)
- Performance: 80% completion
- Emotional Tone: ?? (Struggling)
- vNPS: 5/10
- Burnout Risk: High ??

---

## Common Issues & Solutions

### Issue 1: Connection String Error
**Error:** "No appropriate protocol (protocol error)"
**Solution:**
- Verify MySQL is running
- Check connection string format for MySQL
- Use `Server=localhost` for local MySQL
- Test connection in MySQL Workbench

### Issue 2: Table Not Found
**Error:** "Table 'MyAppDb.volunteers' doesn't exist"
**Solution:**
- Run the SQL script: `02_Create_Volunteers_Table.sql`
- Verify database selected: `USE MyAppDb;`
- Check table exists: `SHOW TABLES;`

### Issue 3: Email Already Exists
**Error:** "Duplicate entry for key 'email'"
**Solution:**
- Email field is UNIQUE in MySQL
- Use different email for testing
- Or delete existing volunteer first

### Issue 4: Invalid vNPS Score
**Error:** "Check constraint 'volunteers_chk_1' is violated"
**Solution:**
- vNPS score must be between 0-10
- Use valid integer values

---

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
    "DefaultConnection": "Server=localhost;Database=MyAppDb;User=root;Password=your_password;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

---

## Next Steps

1. ? Create MySQL database
2. ? Run SQL script to create table
3. ? Update connection string
4. ? Run application
5. ? Test endpoints in Swagger
6. ?? Implement Check-Ins module
7. ?? Implement Escalations module
8. ?? Create Team Leads module

---

## Development Checklist

- [x] Models created (Volunteer.cs)
- [x] DTOs created (CreateVolunteerDto, UpdateVolunteerDto, VolunteerResponseDto)
- [x] Repository layer implemented (VolunteerRepository.cs)
- [x] Service layer implemented (VolunteerService.cs)
- [x] Controller implemented (VolunteersController.cs)
- [x] Dependency injection configured (Program.cs)
- [x] SQL script provided (02_Create_Volunteers_Table.sql)
- [x] API documentation (VOLUNTEERS_MODULE_GUIDE.md)
- [x] 15 API endpoints ready
- ? Unit tests (coming soon)
- ? Integration tests (coming soon)

---

**Module Status:** ? Ready for Development

**Version:** 1.0.0  
**Framework:** .NET 8  
**Database:** MySQL  
**Last Updated:** January 25, 2025
