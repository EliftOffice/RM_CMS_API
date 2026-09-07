# Assignment API - Usage Examples

## Setup & Integration

### 1. Configure Dependency Injection (Program.cs)

```csharp
var builder = WebApplicationBuilder.CreateBuilder(args);

// Add DbConnection Factory
builder.Services.AddScoped<IDbConnectionFactory, DbConnectionFactory>();

// Add Repositories
builder.Services.AddScoped<IPeopleRepository, PeopleRepository>();
builder.Services.AddScoped<IVolunteerRepository, VolunteerRepository>();

// Add Services
builder.Services.AddScoped<IPeopleService, PeopleService>();

// Add Controllers
builder.Services.AddControllers();

var app = builder.Build();
// ... rest of configuration
```

## REST API Usage

### Example 1: Successful Assignment

**Request:**
```http
POST /api/peoples/P001/assign HTTP/1.1
Host: localhost:5000
Content-Type: application/json
```

**Response (200 OK):**
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

### Example 2: No Available Volunteers

**Request:**
```http
POST /api/peoples/P005/assign HTTP/1.1
Host: localhost:5000
```

**Response (200 OK - Warning Status):**
```json
{
  "responseType": 1,
  "message": "No available volunteers with capacity for assignment",
  "data": {
    "personId": "P005",
    "campus": "NewCampus"
  }
}
```

### Example 3: Person Not Found

**Request:**
```http
POST /api/peoples/INVALID_ID/assign HTTP/1.1
Host: localhost:5000
```

**Response (400 Bad Request):**
```json
{
  "responseType": 2,
  "message": "Person with ID 'INVALID_ID' not found",
  "data": null
}
```

### Example 4: Missing Campus

**Request:**
```http
POST /api/peoples/P999/assign HTTP/1.1
Host: localhost:5000
```

**Response (400 Bad Request):**
```json
{
  "responseType": 2,
  "message": "Person does not have a campus assigned",
  "data": null
}
```

## C# Client Usage

### Using HttpClient

```csharp
using var httpClient = new HttpClient();

try
{
    var response = await httpClient.PostAsync(
        "http://localhost:5000/api/peoples/P001/assign",
        null
    );

    var content = await response.Content.ReadAsStringAsync();
    var result = JsonSerializer.Deserialize<ApiResponse<object>>(content);

    if (result?.ResponseType == ResponseType.Success)
    {
        Console.WriteLine("Assignment successful!");
        Console.WriteLine($"Volunteer: {result.Data}");
    }
    else if (result?.ResponseType == ResponseType.Warning)
    {
        Console.WriteLine("Warning: " + result.Message);
    }
    else
    {
        Console.WriteLine("Error: " + result?.Message);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Request failed: {ex.Message}");
}
```

### Using RestSharp

```csharp
var client = new RestClient("http://localhost:5000");
var request = new RestRequest("/api/peoples/P001/assign", Method.Post);

var response = await client.ExecuteAsync(request);

if (response.IsSuccessful)
{
    var result = JsonSerializer.Deserialize<ApiResponse<object>>(response.Content);
    Console.WriteLine(result?.Message);
}
```

## Database State Changes

### Before Assignment

**people table:**
```sql
SELECT person_id, assigned_volunteer, follow_up_status, next_action_date 
FROM people WHERE person_id = 'P001';

-- Result:
person_id | assigned_volunteer | follow_up_status | next_action_date
P001      | NULL              | Pending          | NULL
```

**volunteers table:**
```sql
SELECT volunteer_id, current_assignments FROM volunteers WHERE volunteer_id = 'V001';

-- Result:
volunteer_id | current_assignments
V001         | 2
```

### After Assignment

**people table:**
```sql
-- Result:
person_id | assigned_volunteer | follow_up_status | next_action_date
P001      | V001               | ASSIGNED         | 2026-04-02 10:30:00
```

**volunteers table:**
```sql
-- Result:
volunteer_id | current_assignments
V001         | 3
```

## Unit Test Examples

```csharp
[TestClass]
public class AssignmentTests
{
    private IPeopleRepository _peopleRepository;
    private IVolunteerRepository _volunteerRepository;
    private IPeopleService _peopleService;

    [TestInitialize]
    public void Setup()
    {
        // Mock repositories
        _peopleRepository = new Mock<IPeopleRepository>().Object;
        _volunteerRepository = new Mock<IVolunteerRepository>().Object;
        _peopleService = new PeopleService(_peopleRepository, _volunteerRepository);
    }

    [TestMethod]
    public async Task AssignPersonToVolunteer_Success()
    {
        // Arrange
        var personId = "P001";
        var person = new People 
        { 
            PersonId = personId,
            FirstName = "John",
            LastName = "Doe",
            Campus = "Ongole"
        };
        
        var volunteer = new Volunteer
        {
            VolunteerId = "V001",
            FirstName = "Ravi",
            LastName = "Kumar",
            CurrentAssignments = 2,
            CapacityMax = 8
        };

        // Act
        var result = await _peopleService.AssignPersonToVolunteerAsync(personId);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(ResponseType.Success, result.ResponseType);
    }

    [TestMethod]
    public async Task AssignPersonToVolunteer_NotFound()
    {
        // Arrange
        var personId = "INVALID";

        // Act
        var result = await _peopleService.AssignPersonToVolunteerAsync(personId);

        // Assert
        Assert.AreEqual(ResponseType.Error, result.ResponseType);
    }
}
```

## Postman Collection

```json
{
  "info": {
    "name": "Assignment API",
    "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
  },
  "item": [
    {
      "name": "Assign Person to Volunteer",
      "request": {
        "method": "POST",
        "header": [],
        "url": {
          "raw": "http://localhost:5000/api/peoples/P001/assign",
          "protocol": "http",
          "host": ["localhost"],
          "port": "5000",
          "path": ["api", "peoples", "P001", "assign"]
        }
      }
    },
    {
      "name": "Assign Person P002",
      "request": {
        "method": "POST",
        "header": [],
        "url": {
          "raw": "http://localhost:5000/api/peoples/P002/assign",
          "protocol": "http",
          "host": ["localhost"],
          "port": "5000",
          "path": ["api", "peoples", "P002", "assign"]
        }
      }
    }
  ]
}
```

## Performance Testing

### Load Test with Apache Bench

```bash
# Send 100 concurrent requests
ab -n 1000 -c 100 http://localhost:5000/api/peoples/P001/assign
```

### Expected Performance

- **Throughput:** ~100-200 requests/second (depends on database)
- **Response Time:** 5-15ms average
- **Error Rate:** <1% (under normal conditions)
- **CPU Usage:** Minimal (async operations)

## Monitoring & Logging

### Log Output Example

```
[Information] 2026-03-31 10:30:00 - Attempting to assign person P001 to a volunteer
[Information] 2026-03-31 10:30:00 - Successfully assigned person P001
[Result] Assignment: P001 → V001 (Volunteer: Ravi Kumar)

[Warning] 2026-03-31 10:31:00 - Assignment failed for person P002: No available volunteers
[Warning] Assignment: P002 → FAILED (Campus: NewCampus, No capacity)

[Error] 2026-03-31 10:32:00 - Error assigning person P003: Database connection timeout
[Error] Assignment: P003 → ERROR (Exception: Connection timeout)
```

## Troubleshooting

### Issue: "No available volunteers"
**Cause:** All volunteers at max capacity
**Solution:** 
- Check volunteer capacity settings
- Train new volunteers
- Increase capacity bands

### Issue: "Person does not have a campus"
**Cause:** Person record missing campus field
**Solution:**
- Update person record with campus
- Validate during person creation

### Issue: Database timeout
**Cause:** Connection pool exhausted
**Solution:**
- Increase connection pool size
- Check for connection leaks
- Monitor active connections

### Issue: Slow response time
**Cause:** Large volunteer table or missing indexes
**Solution:**
- Add database indexes
- Archive old volunteers
- Optimize query
