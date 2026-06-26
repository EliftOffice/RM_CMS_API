# RM Office Attendance API Integration

This document describes the new Attendance and Event Management APIs integrated into the RM_CMS project.

## Database Setup

Run the following SQL script to create the required tables:

```sql
CREATE DATABASE IF NOT EXISTS rmoffice CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE rmoffice;

CREATE TABLE IF NOT EXISTS app_users (
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

## Seed Data (Optional)

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

## Configuration

Update your `appsettings.json` with the database connection string:

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

## Project Structure

```
RMOffice/
  DAL/
    Users/
      UsersDAL.cs
    Events/
      EventsDAL.cs
    Attendance/
      AttendanceDAL.cs
  BLL/
    Users/
      UsersBLL.cs
    Events/
      EventsBLL.cs
    Attendance/
      AttendanceBLL.cs
  Controllers/
    Users/
      UserController.cs
    Events/
      EventController.cs
    Attendance/
      AttendanceController.cs
    Admin/
      AdminEventsController.cs
  Data/
    Models/
      User.cs
      Event.cs
      Attendance.cs
    DTO/
      Users/
        UserCheckRequest.cs
        UserRegisterRequest.cs
        UserDto.cs
      Events/
        EventDto.cs
        AdminEventRequest.cs
      Attendance/
        AttendanceCheckInRequest.cs
        AttendanceCheckInResultDto.cs
        AttendanceHistoryItemDto.cs
```

## API Endpoints

### User Management

#### Check User
- **POST** `/api/user/check`
- Check if a user exists by mobile number
- Request:
```json
{
  "mobileNumber": "08012345678"
}
```
- Response:
```json
{
  "success": true,
  "exists": true,
  "data": {
    "id": 1,
    "name": "John Doe",
    "mobileNumber": "08012345678"
  }
}
```

#### Register User
- **POST** `/api/user/register`
- Register a new user
- Request (form-data):
```
name: John Doe
mobileNumber: 08012345678
```
- Response:
```json
{
  "responseType": 0,
  "message": "User created successfully",
  "data": {
    "id": 1,
    "name": "John Doe",
    "mobileNumber": "08012345678"
  }
}
```

### Event Management (Mobile)

#### Get Active Events
- **GET** `/api/events/active?userId=1`
- Get all active events, optionally with attendance status for a user
- Response:
```json
{
  "success": true,
  "data": [
    {
      "id": 1,
      "title": "Sunday Worship Service",
      "venueName": "Resurrection Main Auditorium",
      "address": "12 Faith Avenue, Lagos",
      "latitude": 6.5244,
      "longitude": 3.3792,
      "radius": 300,
      "startTime": "2026-05-25T09:00:00",
      "endTime": "2026-05-25T13:00:00",
      "isActive": true,
      "recurrenceType": "every_sunday",
      "recurrenceDay": "Sunday",
      "repeatUntil": "2027-12-31",
      "reuseSameLocation": true,
      "autoActivateRecurring": true,
      "isAttended": false
    }
  ]
}
```

### Attendance Management

#### Check In
- **POST** `/api/attendance/checkin`
- Mark attendance for an event
- Request:
```json
{
  "userId": 1,
  "eventId": 1,
  "eventTitle": "Sunday Worship Service",
  "timestamp": "2026-05-25T09:05:00Z",
  "latitude": 6.5244,
  "longitude": 3.3792,
  "deviceInfo": "Android 14 - Pixel 7",
  "isSynced": true
}
```
- Success Response (200):
```json
{
  "responseType": 0,
  "message": "Attendance marked successfully",
  "data": {
    "attendanceId": 1,
    "status": "present"
  }
}
```
- Duplicate Response (409):
```json
{
  "responseType": 0,
  "message": "Attendance already marked",
  "data": {
    "attendanceId": 1,
    "status": "duplicate"
  }
}
```

#### Get Attendance History
- **GET** `/api/attendance/history?userId=1`
- Get attendance history for a user
- Response:
```json
{
  "success": true,
  "data": [
    {
      "userId": 1,
      "eventId": 1,
      "eventTitle": "Sunday Worship Service",
      "timestamp": "2026-05-25T09:05:00",
      "latitude": 6.5244,
      "longitude": 3.3792,
      "deviceInfo": "Android 14 - Pixel 7",
      "isSynced": true
    }
  ]
}
```

### Admin Event Management

#### Get All Events
- **GET** `/api/admin/events`
- Get all events with full details
- Response:
```json
{
  "success": true,
  "data": [...]
}
```

#### Get Event by ID
- **GET** `/api/admin/events/{id}`
- Get a specific event
- Response:
```json
{
  "success": true,
  "data": {...}
}
```

#### Create Event
- **POST** `/api/admin/events`
- Create a new event
- Request:
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
- Response:
```json
{
  "responseType": 0,
  "message": "Event created successfully",
  "data": {
    "id": 1
  }
}
```

#### Update Event
- **PUT** `/api/admin/events/{id}`
- Update an existing event
- Request: (Same as Create)
- Response:
```json
{
  "responseType": 0,
  "message": "Event updated successfully",
  "data": {
    "id": 1
  }
}
```

#### Update Event Status
- **PATCH** `/api/admin/events/{id}/status`
- Update event active/inactive status
- Request:
```json
{
  "isActive": false
}
```
- Response:
```json
{
  "responseType": 0,
  "message": "Event status updated",
  "data": {
    "id": 1
  }
}
```

## Features

- ? User registration and verification
- ? Event management with admin panel
- ? Recurring events (weekly, every Sunday)
- ? Automatic activation for recurring events
- ? Geofence radius management
- ? Duplicate attendance prevention (same user, same event, same day)
- ? Attendance history tracking
- ? Location reuse for recurring events
- ? UTC timestamp handling
- ? Two-layer duplicate protection (app-side + database unique constraint)

## Design Pattern

The API follows the **BLL-DAL (Business Logic Layer - Data Access Layer)** pattern:

- **Controllers**: HTTP request handling and response formatting
- **BLL**: Business logic, validation, and orchestration
- **DAL**: Database access using Dapper ORM
- **Models**: Data representation
- **DTOs**: Data transfer objects for requests/responses
- **Utilities**: Common helpers like ApiResponse and HttpResponseHelper
