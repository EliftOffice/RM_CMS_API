# ?? Quick Start Guide - RM Office Attendance API

## For the Developer: Next Steps (5 Minutes)

### Step 1: Setup Database (2 minutes)

1. Open MySQL Workbench or MySQL CLI
2. Copy the SQL script from `ATTENDANCE_API_INTEGRATION.md` under "Database Setup"
3. Run the entire script
4. Verify the tables are created:
   ```sql
   SHOW TABLES IN rmoffice;
   ```

### Step 2: Update Configuration (1 minute)

1. Open `appsettings.json`
2. Update the connection string:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=localhost;Port=3306;Database=rmoffice;Uid=root;Pwd=YOUR_PASSWORD;Allow User Variables=True;"
     }
   }
   ```

### Step 3: Run Application (1 minute)

1. Build the project: `dotnet build`
2. Run the project: `dotnet run`
3. Application starts on `http://localhost:5000`

### Step 4: Test APIs (1 minute)

**Option A: Using PowerShell (Windows)**
```powershell
# Run the test script
.\TEST_API_PowerShell.ps1
```

**Option B: Using Bash (Linux/Mac)**
```bash
# Run the test script
chmod +x TEST_API_CURL.sh
./TEST_API_CURL.sh
```

**Option C: Using Postman/Insomnia**
- Import the API collection
- Use examples from `ATTENDANCE_API_INTEGRATION.md`

---

## ?? Key Endpoints to Test

### 1. Register a User
```bash
POST http://localhost:5000/api/user/register
Content-Type: application/x-www-form-urlencoded

name=John Doe&mobileNumber=08012345678
```

### 2. Check User
```bash
POST http://localhost:5000/api/user/check
Content-Type: application/json

{
  "mobileNumber": "08012345678"
}
```

### 3. Create an Event
```bash
POST http://localhost:5000/api/admin/events
Content-Type: application/json

{
  "title": "Sunday Worship",
  "venueName": "Main Hall",
  "address": "12 Faith Ave",
  "latitude": 6.5244,
  "longitude": 3.3792,
  "radius": 500,
  "startTime": "2026-05-25T09:00:00Z",
  "endTime": "2026-05-25T13:00:00Z",
  "isActive": true,
  "recurrenceType": "every_sunday",
  "recurrenceDay": "Sunday"
}
```

### 4. Get Active Events
```bash
GET http://localhost:5000/api/events/active?userId=1
```

### 5. Check In
```bash
POST http://localhost:5000/api/attendance/checkin
Content-Type: application/json

{
  "userId": 1,
  "eventId": 1,
  "eventTitle": "Sunday Worship",
  "timestamp": "2026-05-25T09:05:00Z",
  "latitude": 6.5244,
  "longitude": 3.3792,
  "deviceInfo": "Android 14",
  "isSynced": true
}
```

### 6. Get Attendance History
```bash
GET http://localhost:5000/api/attendance/history?userId=1
```

---

## ?? What Each Module Does

### Users Module
- Manages user registration by mobile number
- Checks user existence
- No password/authentication required

### Events Module
- Admin manages events
- Supports recurring events (weekly, every Sunday)
- Auto-activation for recurring events
- Geofence radius management

### Attendance Module
- Users check in to events
- GPS coordinates and device info logged
- Prevents duplicate check-ins (409 Conflict)
- Tracks attendance history

---

## ?? Documentation Reference

| Document | Purpose |
|----------|---------|
| `ATTENDANCE_API_INTEGRATION.md` | **Full API documentation** with schemas |
| `README_INTEGRATION.md` | Architecture and features overview |
| `INTEGRATION_COMPLETE.md` | Integration details summary |
| `COMPLETION_CHECKLIST.md` | Verification checklist |
| `FINAL_SUMMARY.md` | Project completion summary |
| `TEST_API_CURL.sh` | **Bash test script** (13 tests) |
| `TEST_API_PowerShell.ps1` | **PowerShell test script** (13 tests) |

---

## ?? Expected Test Results

When you run the test scripts, you should see:

1. ? User check - Returns not found
2. ? User register - Creates user
3. ? User check - Returns found
4. ? Create event - Returns event ID
5. ? Get all events - Returns list
6. ? Get event by ID - Returns event details
7. ? Get active events - Returns active events
8. ? First check-in - Returns 200 OK with "present" status
9. ? Duplicate check-in - Returns **409 Conflict** with "duplicate" status
10. ? Get history - Returns attendance records
11. ? Update event - Returns success
12. ? Deactivate event - Returns success
13. ? Activate event - Returns success

---

## ?? Troubleshooting

### Issue: Database connection failed
**Solution:**
- Verify MySQL is running
- Check connection string in appsettings.json
- Verify database exists: `rmoffice`
- Check username/password

### Issue: Tables not found
**Solution:**
- Run the SQL schema script
- Verify tables exist: `SHOW TABLES IN rmoffice;`

### Issue: Port 5000 already in use
**Solution:**
```bash
# Run on different port
dotnet run --urls "http://localhost:5001"
```

### Issue: Duplicate check-in not returning 409
**Solution:**
- Verify same user/event/day
- Check the test is using correct userId/eventId
- Database constraint should prevent duplicates

---

## ?? Tips

1. **Load Seed Data** (Optional)
   - Use seed data from `ATTENDANCE_API_INTEGRATION.md`
   - Provides pre-created events for testing

2. **Check Swagger UI**
   - Navigate to `http://localhost:5000/swagger`
   - Visual API documentation

3. **Enable Debug Logging**
   - Check Console output
   - See detailed SQL queries and timing

4. **Test Recurring Events**
   - Create event with `recurrenceType: "every_sunday"`
   - Should auto-activate on Sundays

---

## ?? Support Information

### Architecture
- **Pattern**: BLL-DAL (Business Logic - Data Access)
- **ORM**: Dapper
- **Database**: MySQL
- **Framework**: ASP.NET Core 8

### Key Files Modified
- `Program.cs` - Added service registrations

### Key Files Created
- 20+ files across Models, DTOs, DAL, BLL, Controllers

---

## ? Verification Checklist

Before marking as "done":

- [ ] Database created and tables verified
- [ ] Connection string configured
- [ ] Application builds successfully
- [ ] Application starts without errors
- [ ] Test script runs all 13 tests
- [ ] User registration works
- [ ] Event creation works
- [ ] Check-in works
- [ ] Duplicate check-in returns 409
- [ ] Attendance history shows records

---

## ?? Common Tasks

### Add a New Field to Event
1. Update `Event.cs` model
2. Update `EventDto.cs`
3. Update `AdminEventRequest.cs`
4. Update SQL schema
5. Update DAL query
6. Build and test

### Create New API Endpoint
1. Create service method in BLL
2. Create DAL method
3. Create Controller action
4. Register in Program.cs
5. Test with curl/postman

### Modify Response Format
1. Update the DTO
2. Update BLL return type
3. Update Controller response
4. Build and test

---

## ?? You're All Set!

Everything is configured and ready. Just:

1. ? Run the SQL script
2. ? Update appsettings.json
3. ? Start the application
4. ? Run the test script
5. ? You're done!

**Happy coding!** ??

---

**Questions?** Check the documentation files listed above.

**Ready to deploy?** See `COMPLETION_CHECKLIST.md` for pre-deployment steps.
