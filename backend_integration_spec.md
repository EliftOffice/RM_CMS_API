# RM Office Backend Starter

This is copy-paste starter code for:

- ASP.NET Core Web API
- MySQL
- Dapper
- Admin event configuration
- Recurring Sunday events
- Automatic activation every Sunday
- Same saved location reuse
- Dynamic geofence radius updates from admin panel
- Duplicate attendance prevention for same user, same event, same day

Base URL:

```text
https://rmoffice.online/api/
```

## API Endpoints Required

Mobile app:

- `POST /api/user/check`
- `POST /api/user/register`
- `GET /api/events/active`
- `POST /api/attendance/checkin`
- `GET /api/attendance/history?userId=1`

Admin panel:

- `POST /api/admin/events`
- `PUT /api/admin/events/{id}`
- `PATCH /api/admin/events/{id}/status`
- `GET /api/admin/events`
- `GET /api/admin/events/{id}`

## Project Structure

```text
RMOffice.Api/
  Controllers/
    AttendanceController.cs
    EventController.cs
    UserController.cs
    AdminEventsController.cs
  Data/
    DbConnectionFactory.cs
  Dtos/
    AdminEventRequest.cs
    AttendanceCheckInRequest.cs
    AttendanceCheckInResultDto.cs
    AttendanceHistoryItemDto.cs
    EventDto.cs
    RegisterUserRequest.cs
    ResponseDto.cs
    UserCheckRequest.cs
    UserDto.cs
  Repositories/
    AttendanceRepository.cs
    EventRepository.cs
    UserRepository.cs
  Services/
    AttendanceService.cs
    EventActivationService.cs
  Program.cs
  appsettings.json
```

## MySQL Schema

Run this schema first.

```sql
CREATE DATABASE IF NOT EXISTS rmoffice CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE rmoffice;

CREATE TABLE IF NOT EXISTS users (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(150) NOT NULL,
    mobile_number VARCHAR(30) NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_users_mobile_number (mobile_number)
);

CREATE TABLE IF NOT EXISTS events (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    title VARCHAR(200) NOT NULL,
    venue_name VARCHAR(200) NOT NULL,
    address TEXT NOT NULL,
    latitude DECIMAL(10,7) NOT NULL,
    longitude DECIMAL(10,7) NOT NULL,
    radius INT NOT NULL,
    start_time DATETIME NOT NULL,
    end_time DATETIME NOT NULL,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    recurrence_type VARCHAR(30) NOT NULL DEFAULT 'once',
    recurrence_day VARCHAR(20) NULL,
    repeat_until DATE NULL,
    reuse_same_location TINYINT(1) NOT NULL DEFAULT 1,
    auto_activate_recurring TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    KEY idx_events_active (is_active),
    KEY idx_events_time (start_time, end_time),
    KEY idx_events_recurring (recurrence_type, recurrence_day, repeat_until)
);

CREATE TABLE IF NOT EXISTS attendances (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    user_id BIGINT NOT NULL,
    event_id BIGINT NOT NULL,
    event_title VARCHAR(200) NOT NULL,
    attendance_day DATE NOT NULL,
    checkin_time DATETIME NOT NULL,
    latitude DECIMAL(10,7) NOT NULL,
    longitude DECIMAL(10,7) NOT NULL,
    device_info VARCHAR(255) NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_attendance_user FOREIGN KEY (user_id) REFERENCES users(id),
    CONSTRAINT fk_attendance_event FOREIGN KEY (event_id) REFERENCES events(id),
    UNIQUE KEY uq_attendance_user_event_day (user_id, event_id, attendance_day),
    KEY idx_attendance_user_time (user_id, checkin_time),
    KEY idx_attendance_day (attendance_day)
);
```

## Sample Seed Data

```sql
INSERT INTO events
(
    title,
    venue_name,
    address,
    latitude,
    longitude,
    radius,
    start_time,
    end_time,
    is_active,
    recurrence_type,
    recurrence_day,
    repeat_until,
    reuse_same_location,
    auto_activate_recurring
)
VALUES
(
    'Sunday Worship Service',
    'Resurrection Main Auditorium',
    '12 Faith Avenue, Lagos',
    6.5244000,
    3.3792000,
    300,
    '2026-05-25 09:00:00',
    '2026-05-25 13:00:00',
    1,
    'every_sunday',
    'Sunday',
    '2027-12-31',
    1,
    1
),
(
    'Leadership Conference',
    'Conference Hall B',
    '10 Hope Street, Abuja',
    9.0765000,
    7.3986000,
    500,
    '2026-05-26 14:00:00',
    '2026-05-26 19:00:00',
    1,
    'once',
    NULL,
    NULL,
    1,
    1
);
```

## NuGet Packages

```bash
dotnet add package Dapper
dotnet add package MySqlConnector
dotnet add package Swashbuckle.AspNetCore
```

## appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Port=3306;Database=rmoffice;Uid=root;Pwd=your_password;Allow User Variables=True;"
  },
  "AllowedHosts": "*",
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## Program.cs

```csharp
using RMOffice.Api.Data;
using RMOffice.Api.Repositories;
using RMOffice.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<EventRepository>();
builder.Services.AddScoped<AttendanceRepository>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<EventActivationService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
```

## Data/DbConnectionFactory.cs

```csharp
using System.Data;
using MySqlConnector;

namespace RMOffice.Api.Data;

public sealed class DbConnectionFactory
{
    private readonly IConfiguration _configuration;

    public DbConnectionFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IDbConnection CreateConnection()
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        return new MySqlConnection(connectionString);
    }
}
```

## Dtos/ResponseDto.cs

```csharp
namespace RMOffice.Api.Dtos;

public class ResponseDto<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }

    public static ResponseDto<T> Ok(T? data, string message = "Operation successful")
    {
        return new ResponseDto<T> { Success = true, Message = message, Data = data };
    }

    public static ResponseDto<T> Fail(string message)
    {
        return new ResponseDto<T> { Success = false, Message = message, Data = default };
    }
}
```

## Dtos/UserCheckRequest.cs

```csharp
namespace RMOffice.Api.Dtos;

public class UserCheckRequest
{
    public string Name { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
}
```

## Dtos/RegisterUserRequest.cs

```csharp
namespace RMOffice.Api.Dtos;

public class RegisterUserRequest
{
    public string Name { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
}
```

## Dtos/UserDto.cs

```csharp
namespace RMOffice.Api.Dtos;

public class UserDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
}
```

## Dtos/EventDto.cs

```csharp
namespace RMOffice.Api.Dtos;

public class EventDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string VenueName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Radius { get; set; }
    public DateTime DateTime { get; set; }
    public bool IsAttended { get; set; }
    public string RecurrenceType { get; set; } = "once";
    public string RecurrenceDay { get; set; } = string.Empty;
    public DateTime? RepeatUntil { get; set; }
    public bool ReuseSameLocation { get; set; }
    public bool AutoActivateRecurring { get; set; }
}
```

## Dtos/AdminEventRequest.cs

```csharp
namespace RMOffice.Api.Dtos;

public class AdminEventRequest
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string VenueName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Radius { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsActive { get; set; }
    public string RecurrenceType { get; set; } = "once";
    public string RecurrenceDay { get; set; } = string.Empty;
    public DateTime? RepeatUntil { get; set; }
    public bool ReuseSameLocation { get; set; } = true;
    public bool AutoActivateRecurring { get; set; } = true;
}
```

## Dtos/AttendanceCheckInRequest.cs

```csharp
namespace RMOffice.Api.Dtos;

public class AttendanceCheckInRequest
{
    public long UserId { get; set; }
    public long EventId { get; set; }
    public string EventTitle { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string DeviceInfo { get; set; } = string.Empty;
    public bool IsSynced { get; set; }
}
```

## Dtos/AttendanceCheckInResultDto.cs

```csharp
namespace RMOffice.Api.Dtos;

public class AttendanceCheckInResultDto
{
    public long AttendanceId { get; set; }
    public string Status { get; set; } = string.Empty;
}
```

## Dtos/AttendanceHistoryItemDto.cs

```csharp
namespace RMOffice.Api.Dtos;

public class AttendanceHistoryItemDto
{
    public long UserId { get; set; }
    public long EventId { get; set; }
    public string EventTitle { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string DeviceInfo { get; set; } = string.Empty;
    public bool IsSynced { get; set; }
}
```

## Repositories/UserRepository.cs

```csharp
using Dapper;
using RMOffice.Api.Data;
using RMOffice.Api.Dtos;

namespace RMOffice.Api.Repositories;

public class UserRepository
{
    private readonly DbConnectionFactory _dbFactory;

    public UserRepository(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<UserDto?> GetByMobileAsync(string mobileNumber)
    {
        const string sql = @"
            SELECT
                id AS Id,
                name AS Name,
                mobile_number AS MobileNumber,
                NULL AS ProfileImageUrl
            FROM users
            WHERE mobile_number = @MobileNumber
            LIMIT 1;";

        using var connection = _dbFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<UserDto>(sql, new { MobileNumber = mobileNumber });
    }

    public async Task<UserDto> CreateAsync(string name, string mobileNumber)
    {
        const string sql = @"
            INSERT INTO users (name, mobile_number)
            VALUES (@Name, @MobileNumber);
            SELECT LAST_INSERT_ID();";

        using var connection = _dbFactory.CreateConnection();
        var id = await connection.ExecuteScalarAsync<long>(sql, new { Name = name, MobileNumber = mobileNumber });

        return new UserDto
        {
            Id = id,
            Name = name,
            MobileNumber = mobileNumber,
            ProfileImageUrl = null
        };
    }
}
```

## Repositories/EventRepository.cs

```csharp
using Dapper;
using RMOffice.Api.Data;
using RMOffice.Api.Dtos;

namespace RMOffice.Api.Repositories;

public class EventRepository
{
    private readonly DbConnectionFactory _dbFactory;

    public EventRepository(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<EventDto>> GetAdminEventsAsync()
    {
        const string sql = @"
            SELECT
                id AS Id,
                title AS Title,
                venue_name AS VenueName,
                address AS Address,
                latitude AS Latitude,
                longitude AS Longitude,
                radius AS Radius,
                start_time AS DateTime,
                is_active AS IsAttended,
                recurrence_type AS RecurrenceType,
                COALESCE(recurrence_day, '') AS RecurrenceDay,
                repeat_until AS RepeatUntil,
                reuse_same_location AS ReuseSameLocation,
                auto_activate_recurring AS AutoActivateRecurring
            FROM events
            ORDER BY start_time DESC;";

        using var connection = _dbFactory.CreateConnection();
        var rows = await connection.QueryAsync<EventDto>(sql);
        return rows.ToList();
    }

    public async Task<EventDto?> GetByIdAsync(long eventId)
    {
        const string sql = @"
            SELECT
                id AS Id,
                title AS Title,
                venue_name AS VenueName,
                address AS Address,
                latitude AS Latitude,
                longitude AS Longitude,
                radius AS Radius,
                start_time AS DateTime,
                0 AS IsAttended,
                recurrence_type AS RecurrenceType,
                COALESCE(recurrence_day, '') AS RecurrenceDay,
                repeat_until AS RepeatUntil,
                reuse_same_location AS ReuseSameLocation,
                auto_activate_recurring AS AutoActivateRecurring
            FROM events
            WHERE id = @EventId
            LIMIT 1;";

        using var connection = _dbFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<EventDto>(sql, new { EventId = eventId });
    }

    public async Task<long> CreateAsync(AdminEventRequest request)
    {
        const string sql = @"
            INSERT INTO events
            (
                title, venue_name, address, latitude, longitude, radius,
                start_time, end_time, is_active, recurrence_type, recurrence_day,
                repeat_until, reuse_same_location, auto_activate_recurring
            )
            VALUES
            (
                @Title, @VenueName, @Address, @Latitude, @Longitude, @Radius,
                @StartTime, @EndTime, @IsActive, @RecurrenceType, @RecurrenceDay,
                @RepeatUntil, @ReuseSameLocation, @AutoActivateRecurring
            );
            SELECT LAST_INSERT_ID();";

        using var connection = _dbFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(sql, request);
    }

    public async Task UpdateAsync(long id, AdminEventRequest request)
    {
        const string sql = @"
            UPDATE events
            SET
                title = @Title,
                venue_name = @VenueName,
                address = @Address,
                latitude = @Latitude,
                longitude = @Longitude,
                radius = @Radius,
                start_time = @StartTime,
                end_time = @EndTime,
                is_active = @IsActive,
                recurrence_type = @RecurrenceType,
                recurrence_day = @RecurrenceDay,
                repeat_until = @RepeatUntil,
                reuse_same_location = @ReuseSameLocation,
                auto_activate_recurring = @AutoActivateRecurring
            WHERE id = @Id;";

        using var connection = _dbFactory.CreateConnection();
        await connection.ExecuteAsync(sql, new
        {
            Id = id,
            request.Title,
            request.VenueName,
            request.Address,
            request.Latitude,
            request.Longitude,
            request.Radius,
            request.StartTime,
            request.EndTime,
            request.IsActive,
            request.RecurrenceType,
            request.RecurrenceDay,
            request.RepeatUntil,
            request.ReuseSameLocation,
            request.AutoActivateRecurring
        });
    }

    public async Task UpdateStatusAsync(long id, bool isActive)
    {
        const string sql = "UPDATE events SET is_active = @IsActive WHERE id = @Id;";
        using var connection = _dbFactory.CreateConnection();
        await connection.ExecuteAsync(sql, new { Id = id, IsActive = isActive });
    }

    public async Task<List<EventDto>> GetActiveEventsAsync(long? userId)
    {
        const string sql = @"
            SELECT
                e.id AS Id,
                e.title AS Title,
                e.venue_name AS VenueName,
                e.address AS Address,
                e.latitude AS Latitude,
                e.longitude AS Longitude,
                e.radius AS Radius,
                CASE
                    WHEN e.recurrence_type IN ('weekly', 'every_sunday') THEN
                        TIMESTAMP(
                            UTC_DATE(),
                            TIME(e.start_time)
                        )
                    ELSE e.start_time
                END AS DateTime,
                CASE
                    WHEN @UserId IS NULL THEN 0
                    WHEN EXISTS (
                        SELECT 1
                        FROM attendances a
                        WHERE a.user_id = @UserId
                          AND a.event_id = e.id
                          AND a.attendance_day = UTC_DATE()
                    ) THEN 1
                    ELSE 0
                END AS IsAttended,
                e.recurrence_type AS RecurrenceType,
                COALESCE(e.recurrence_day, '') AS RecurrenceDay,
                e.repeat_until AS RepeatUntil,
                e.reuse_same_location AS ReuseSameLocation,
                e.auto_activate_recurring AS AutoActivateRecurring
            FROM events e
            WHERE
                (
                    e.recurrence_type = 'once'
                    AND e.is_active = 1
                    AND UTC_TIMESTAMP() BETWEEN e.start_time AND e.end_time
                )
                OR
                (
                    e.recurrence_type = 'weekly'
                    AND e.auto_activate_recurring = 1
                    AND
                    (
                        e.repeat_until IS NULL
                        OR UTC_DATE() <= e.repeat_until
                    )
                    AND DAYNAME(UTC_DATE()) = e.recurrence_day
                    AND TIME(UTC_TIME()) BETWEEN TIME(e.start_time) AND TIME(e.end_time)
                )
                OR
                (
                    e.recurrence_type = 'every_sunday'
                    AND e.auto_activate_recurring = 1
                    AND
                    (
                        e.repeat_until IS NULL
                        OR UTC_DATE() <= e.repeat_until
                    )
                    AND DAYNAME(UTC_DATE()) = 'Sunday'
                    AND TIME(UTC_TIME()) BETWEEN TIME(e.start_time) AND TIME(e.end_time)
                )
            ORDER BY e.start_time ASC;";

        using var connection = _dbFactory.CreateConnection();
        var rows = await connection.QueryAsync<EventDto>(sql, new { UserId = userId });
        return rows.ToList();
    }

    public async Task<bool> IsEventActiveAsync(long eventId)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM events e
            WHERE e.id = @EventId
              AND
              (
                  (
                      e.recurrence_type = 'once'
                      AND e.is_active = 1
                      AND UTC_TIMESTAMP() BETWEEN e.start_time AND e.end_time
                  )
                  OR
                  (
                      e.recurrence_type = 'weekly'
                      AND e.auto_activate_recurring = 1
                      AND (e.repeat_until IS NULL OR UTC_DATE() <= e.repeat_until)
                      AND DAYNAME(UTC_DATE()) = e.recurrence_day
                      AND TIME(UTC_TIME()) BETWEEN TIME(e.start_time) AND TIME(e.end_time)
                  )
                  OR
                  (
                      e.recurrence_type = 'every_sunday'
                      AND e.auto_activate_recurring = 1
                      AND (e.repeat_until IS NULL OR UTC_DATE() <= e.repeat_until)
                      AND DAYNAME(UTC_DATE()) = 'Sunday'
                      AND TIME(UTC_TIME()) BETWEEN TIME(e.start_time) AND TIME(e.end_time)
                  )
              );";

        using var connection = _dbFactory.CreateConnection();
        var count = await connection.ExecuteScalarAsync<int>(sql, new { EventId = eventId });
        return count > 0;
    }
}
```

## Repositories/AttendanceRepository.cs

```csharp
using Dapper;
using MySqlConnector;
using RMOffice.Api.Data;
using RMOffice.Api.Dtos;

namespace RMOffice.Api.Repositories;

public class AttendanceRepository
{
    private readonly DbConnectionFactory _dbFactory;

    public AttendanceRepository(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<bool> ExistsForSameDayAsync(long userId, long eventId, DateTime timestampUtc)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM attendances
            WHERE user_id = @UserId
              AND event_id = @EventId
              AND attendance_day = DATE(@TimestampUtc);";

        using var connection = _dbFactory.CreateConnection();
        var count = await connection.ExecuteScalarAsync<int>(sql, new
        {
            UserId = userId,
            EventId = eventId,
            TimestampUtc = timestampUtc
        });

        return count > 0;
    }

    public async Task<long?> GetExistingAttendanceIdAsync(long userId, long eventId, DateTime timestampUtc)
    {
        const string sql = @"
            SELECT id
            FROM attendances
            WHERE user_id = @UserId
              AND event_id = @EventId
              AND attendance_day = DATE(@TimestampUtc)
            LIMIT 1;";

        using var connection = _dbFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long?>(sql, new
        {
            UserId = userId,
            EventId = eventId,
            TimestampUtc = timestampUtc
        });
    }

    public async Task<long> InsertAsync(AttendanceCheckInRequest request)
    {
        const string sql = @"
            INSERT INTO attendances
            (
                user_id,
                event_id,
                event_title,
                attendance_day,
                checkin_time,
                latitude,
                longitude,
                device_info
            )
            VALUES
            (
                @UserId,
                @EventId,
                @EventTitle,
                DATE(@Timestamp),
                @Timestamp,
                @Latitude,
                @Longitude,
                @DeviceInfo
            );
            SELECT LAST_INSERT_ID();";

        using var connection = _dbFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(sql, request);
    }

    public static bool IsDuplicateKeyException(Exception exception)
    {
        return exception is MySqlException mysqlException && mysqlException.Number == 1062;
    }

    public async Task<List<AttendanceHistoryItemDto>> GetHistoryAsync(long userId)
    {
        const string sql = @"
            SELECT
                user_id AS UserId,
                event_id AS EventId,
                event_title AS EventTitle,
                checkin_time AS Timestamp,
                latitude AS Latitude,
                longitude AS Longitude,
                COALESCE(device_info, '') AS DeviceInfo,
                1 AS IsSynced
            FROM attendances
            WHERE user_id = @UserId
            ORDER BY checkin_time DESC;";

        using var connection = _dbFactory.CreateConnection();
        var rows = await connection.QueryAsync<AttendanceHistoryItemDto>(sql, new { UserId = userId });
        return rows.ToList();
    }
}
```

## Services/AttendanceService.cs

```csharp
using RMOffice.Api.Dtos;
using RMOffice.Api.Repositories;

namespace RMOffice.Api.Services;

public class AttendanceService
{
    private readonly EventRepository _eventRepository;
    private readonly AttendanceRepository _attendanceRepository;

    public AttendanceService(
        EventRepository eventRepository,
        AttendanceRepository attendanceRepository)
    {
        _eventRepository = eventRepository;
        _attendanceRepository = attendanceRepository;
    }

    public async Task<ResponseDto<AttendanceCheckInResultDto>> CheckInAsync(AttendanceCheckInRequest request)
    {
        if (request.UserId <= 0 || request.EventId <= 0)
        {
            return ResponseDto<AttendanceCheckInResultDto>.Fail("Invalid user or event");
        }

        var timestampUtc = request.Timestamp.ToUniversalTime();
        request.EventTitle = request.EventTitle?.Trim() ?? string.Empty;
        request.DeviceInfo = request.DeviceInfo?.Trim() ?? string.Empty;

        var eventIsActive = await _eventRepository.IsEventActiveAsync(request.EventId);
        if (!eventIsActive)
        {
            return ResponseDto<AttendanceCheckInResultDto>.Fail("Event is not active");
        }

        var existingAttendanceId = await _attendanceRepository.GetExistingAttendanceIdAsync(
            request.UserId,
            request.EventId,
            timestampUtc);

        if (existingAttendanceId.HasValue)
        {
            return ResponseDto<AttendanceCheckInResultDto>.Ok(
                new AttendanceCheckInResultDto
                {
                    AttendanceId = existingAttendanceId.Value,
                    Status = "duplicate"
                },
                "Attendance already marked");
        }

        try
        {
            var attendanceId = await _attendanceRepository.InsertAsync(new AttendanceCheckInRequest
            {
                UserId = request.UserId,
                EventId = request.EventId,
                EventTitle = request.EventTitle,
                Timestamp = timestampUtc,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                DeviceInfo = request.DeviceInfo,
                IsSynced = true
            });

            return ResponseDto<AttendanceCheckInResultDto>.Ok(
                new AttendanceCheckInResultDto
                {
                    AttendanceId = attendanceId,
                    Status = "present"
                },
                "Attendance marked successfully");
        }
        catch (Exception ex) when (AttendanceRepository.IsDuplicateKeyException(ex))
        {
            existingAttendanceId = await _attendanceRepository.GetExistingAttendanceIdAsync(
                request.UserId,
                request.EventId,
                timestampUtc);

            return ResponseDto<AttendanceCheckInResultDto>.Ok(
                new AttendanceCheckInResultDto
                {
                    AttendanceId = existingAttendanceId ?? 0,
                    Status = "duplicate"
                },
                "Attendance already marked");
        }
    }
}
```

## Services/EventActivationService.cs

```csharp
namespace RMOffice.Api.Services;

public class EventActivationService
{
    public bool ShouldAutoActivateToday(string recurrenceType, string recurrenceDay, DateTime? repeatUntilUtcDate)
    {
        var todayUtc = DateTime.UtcNow.Date;

        if (repeatUntilUtcDate.HasValue && todayUtc > repeatUntilUtcDate.Value.Date)
        {
            return false;
        }

        if (recurrenceType == "every_sunday")
        {
            return DateTime.UtcNow.DayOfWeek == DayOfWeek.Sunday;
        }

        if (recurrenceType == "weekly")
        {
            return string.Equals(
                DateTime.UtcNow.DayOfWeek.ToString(),
                recurrenceDay,
                StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
```

## Controllers/UserController.cs

```csharp
using System;
using Microsoft.AspNetCore.Mvc;
using RMOffice.Api.Dtos;
using RMOffice.Api.Repositories;

namespace RMOffice.Api.Controllers;

[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly UserRepository _userRepository;

    public UserController(UserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    [HttpPost("check")]
    public async Task<IActionResult> Check([FromBody] UserCheckRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MobileNumber))
        {
            return BadRequest(ResponseDto<object>.Fail("Mobile number is required"));
        }

        var user = await _userRepository.GetByMobileAsync(request.MobileNumber.Trim());
        if (user == null)
        {
            return Ok(new
            {
                success = true,
                exists = false,
                data = (object?)null
            });
        }

        return Ok(new
        {
            success = true,
            exists = true,
            data = user
        });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromForm] RegisterUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.MobileNumber))
        {
            return BadRequest(ResponseDto<object>.Fail("Name and mobile number are required"));
        }

        var mobile = request.MobileNumber.Trim();
        var existing = await _userRepository.GetByMobileAsync(mobile);
        if (existing != null)
        {
            return Ok(ResponseDto<UserDto>.Ok(existing, "User already exists"));
        }

        var user = await _userRepository.CreateAsync(request.Name.Trim(), mobile);
        return Ok(ResponseDto<UserDto>.Ok(user, "User registered successfully"));
    }
}
```

## Controllers/EventController.cs

```csharp
using Microsoft.AspNetCore.Mvc;
using RMOffice.Api.Repositories;

namespace RMOffice.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventController : ControllerBase
{
    private readonly EventRepository _eventRepository;

    public EventController(EventRepository eventRepository)
    {
        _eventRepository = eventRepository;
    }

    [HttpGet("active")]
    public async Task<IActionResult> Active([FromQuery] long? userId)
    {
        var events = await _eventRepository.GetActiveEventsAsync(userId);
        return Ok(new
        {
            success = true,
            data = events
        });
    }
}
```

## Controllers/AttendanceController.cs

```csharp
using System;
using Microsoft.AspNetCore.Mvc;
using RMOffice.Api.Dtos;
using RMOffice.Api.Repositories;
using RMOffice.Api.Services;

namespace RMOffice.Api.Controllers;

[ApiController]
[Route("api/attendance")]
public class AttendanceController : ControllerBase
{
    private readonly AttendanceService _attendanceService;
    private readonly AttendanceRepository _attendanceRepository;

    public AttendanceController(
        AttendanceService attendanceService,
        AttendanceRepository attendanceRepository)
    {
        _attendanceService = attendanceService;
        _attendanceRepository = attendanceRepository;
    }

    [HttpPost("checkin")]
    public async Task<IActionResult> CheckIn([FromBody] AttendanceCheckInRequest request)
    {
        var result = await _attendanceService.CheckInAsync(request);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        if (string.Equals(result.Data?.Status, "duplicate", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(result);
        }

        return Ok(result);
    }

    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] long userId)
    {
        if (userId <= 0)
        {
            return BadRequest(ResponseDto<object>.Fail("Invalid userId"));
        }

        var history = await _attendanceRepository.GetHistoryAsync(userId);
        return Ok(new
        {
            success = true,
            data = history
        });
    }
}
```

## Controllers/AdminEventsController.cs

```csharp
using Microsoft.AspNetCore.Mvc;
using RMOffice.Api.Dtos;
using RMOffice.Api.Repositories;

namespace RMOffice.Api.Controllers;

[ApiController]
[Route("api/admin/events")]
public class AdminEventsController : ControllerBase
{
    private readonly EventRepository _eventRepository;

    public AdminEventsController(EventRepository eventRepository)
    {
        _eventRepository = eventRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var events = await _eventRepository.GetAdminEventsAsync();
        return Ok(new { success = true, data = events });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var item = await _eventRepository.GetByIdAsync(id);
        if (item == null)
        {
            return NotFound(ResponseDto<object>.Fail("Event not found"));
        }

        return Ok(new { success = true, data = item });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminEventRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.VenueName))
        {
            return BadRequest(ResponseDto<object>.Fail("Title and venue name are required"));
        }

        if (request.Radius <= 0)
        {
            return BadRequest(ResponseDto<object>.Fail("Radius must be greater than zero"));
        }

        if (request.EndTime <= request.StartTime)
        {
            return BadRequest(ResponseDto<object>.Fail("End time must be greater than start time"));
        }

        var id = await _eventRepository.CreateAsync(request);
        return Ok(ResponseDto<object>.Ok(new { id }, "Event created successfully"));
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] AdminEventRequest request)
    {
        await _eventRepository.UpdateAsync(id, request);
        return Ok(ResponseDto<object>.Ok(new { id }, "Event updated successfully"));
    }

    [HttpPatch("{id:long}/status")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] JsonElement body)
    {
        if (!body.TryGetProperty("isActive", out var isActiveElement))
        {
            return BadRequest(ResponseDto<object>.Fail("isActive is required"));
        }

        await _eventRepository.UpdateStatusAsync(id, isActiveElement.GetBoolean());
        return Ok(ResponseDto<object>.Ok(new { id }, "Event status updated"));
    }
}
```

## Sample JSON For Admin Save

```json
{
  "title": "Sunday Worship Service",
  "venueName": "Resurrection Main Auditorium",
  "address": "12 Faith Avenue, Lagos",
  "latitude": 6.5244,
  "longitude": 3.3792,
  "radius": 500,
  "startTime": "2026-05-25T09:00:00Z",
  "endTime": "2026-05-25T13:00:00Z",
  "isActive": true,
  "recurrenceType": "every_sunday",
  "recurrenceDay": "Sunday",
  "repeatUntil": "2027-12-31",
  "reuseSameLocation": true,
  "autoActivateRecurring": true
}
```

## What The Flutter App Now Expects

The mobile app now supports:

- wrapped API responses like:

```json
{
  "success": true,
  "data": []
}
```

- recurring fields:
  - `recurrenceType`
  - `recurrenceDay`
  - `repeatUntil`
  - `reuseSameLocation`
  - `autoActivateRecurring`

- duplicate check-in detection response:

```json
{
  "success": true,
  "message": "Attendance already marked",
  "data": {
    "attendanceId": 15,
    "status": "duplicate"
  }
}
```

- duplicate check-in HTTP status:
  - `409 Conflict`
- successful first check-in HTTP status:
  - `200 OK`

## Copy Paste Test Requests

Run the API, then test with the exact requests below.

### 1. First attendance should succeed

```bash
curl -X POST "https://rmoffice.online/api/attendance/checkin" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": 1,
    "eventId": 1,
    "eventTitle": "Sunday Worship Service",
    "timestamp": "2026-05-26T09:05:00Z",
    "latitude": 6.5244,
    "longitude": 3.3792,
    "deviceInfo": "Android 14 - Pixel 7",
    "isSynced": true
  }'
```

Expected response:

```json
{
  "success": true,
  "message": "Attendance marked successfully",
  "data": {
    "attendanceId": 16,
    "status": "present"
  }
}
```

### 2. Same user, same event, same day should return duplicate

```bash
curl -X POST "https://rmoffice.online/api/attendance/checkin" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": 1,
    "eventId": 1,
    "eventTitle": "Sunday Worship Service",
    "timestamp": "2026-05-26T10:30:00Z",
    "latitude": 6.5244,
    "longitude": 3.3792,
    "deviceInfo": "Android 14 - Pixel 7",
    "isSynced": true
  }'
```

Expected response:

```json
{
  "success": true,
  "message": "Attendance already marked",
  "data": {
    "attendanceId": 16,
    "status": "duplicate"
  }
}
```

Expected HTTP status:

```text
409 Conflict
```

### 3. Attendance history test

```bash
curl "https://rmoffice.online/api/attendance/history?userId=1"
```

### 4. PowerShell test on Windows

```powershell
$body = @{
  userId = 1
  eventId = 1
  eventTitle = "Sunday Worship Service"
  timestamp = "2026-05-26T09:05:00Z"
  latitude = 6.5244
  longitude = 3.3792
  deviceInfo = "Android 14 - Pixel 7"
  isSynced = $true
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri "https://rmoffice.online/api/attendance/checkin" `
  -ContentType "application/json" `
  -Body $body
```

## Important Behavior

- No password
- No OTP
- No profile image required
- Admin can increase or decrease geofence radius dynamically
- Same location can be reused for recurring Sunday services
- Recurring weekly and Sunday events can auto-activate by current day and time
- Same user cannot mark attendance twice for the same event on the same day
- Duplicate protection exists in two layers:
  - app-side lock and queue protection
  - backend-side unique key plus duplicate-response handling
- Even if two requests arrive at the same time, MySQL unique key `uq_attendance_user_event_day` still guarantees one row only
