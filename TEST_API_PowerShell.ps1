# API Test Commands for RM Office Attendance System (PowerShell)

$BaseUrl = "http://localhost:5000/api"

function Invoke-ApiTest {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Uri,
        [object]$Body = $null
    )
    
    Write-Host ""
    Write-Host "================================" -ForegroundColor Cyan
    Write-Host $Name -ForegroundColor Cyan
    Write-Host "================================" -ForegroundColor Cyan
    
    $params = @{
        Method      = $Method
        Uri         = $Uri
        ContentType = "application/json"
    }
    
    if ($Body) {
        $params['Body'] = $Body | ConvertTo-Json -Depth 10
    }
    
    try {
        $response = Invoke-RestMethod @params
        Write-Host ($response | ConvertTo-Json -Depth 10) -ForegroundColor Green
    }
    catch {
        Write-Host "Status Code: $($_.Exception.Response.StatusCode)" -ForegroundColor Yellow
        Write-Host ($_.Exception.Response | ConvertTo-Json -Depth 10) -ForegroundColor Yellow
    }
}

# Test 1: Check User (Non-existent)
Invoke-ApiTest -Name "1. Check User (Non-existent)" `
    -Method "POST" `
    -Uri "$BaseUrl/user/check" `
    -Body @{
    mobileNumber = "08012345678"
}

# Test 2: Register User
Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "2. Register User" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

$body = @{
    name         = "John Doe"
    mobileNumber = "08012345678"
} | ConvertTo-Json

Invoke-WebRequest -Method Post `
    -Uri "$BaseUrl/user/register" `
    -ContentType "application/x-www-form-urlencoded" `
    -Body "name=John Doe&mobileNumber=08012345678" | Select-Object -ExpandProperty Content

# Test 3: Check User (Existing)
Invoke-ApiTest -Name "3. Check User (Existing)" `
    -Method "POST" `
    -Uri "$BaseUrl/user/check" `
    -Body @{
    mobileNumber = "08012345678"
}

# Test 4: Create Event
Invoke-ApiTest -Name "4. Create Event (Admin)" `
    -Method "POST" `
    -Uri "$BaseUrl/admin/events" `
    -Body @{
    title                   = "Sunday Worship Service"
    venueName               = "Resurrection Main Auditorium"
    address                 = "12 Faith Avenue, Lagos"
    latitude                = 6.5244
    longitude               = 3.3792
    radius                  = 500
    startTime               = "2026-05-25T09:00:00Z"
    endTime                 = "2026-05-25T13:00:00Z"
    isActive                = $true
    recurrenceType          = "every_sunday"
    recurrenceDay           = "Sunday"
    repeatUntil             = "2027-12-31"
    reuseSameLocation       = $true
    autoActivateRecurring   = $true
}

# Test 5: Get All Events
Invoke-ApiTest -Name "5. Get All Events (Admin)" `
    -Method "GET" `
    -Uri "$BaseUrl/admin/events"

# Test 6: Get Event by ID
Invoke-ApiTest -Name "6. Get Event by ID (Admin)" `
    -Method "GET" `
    -Uri "$BaseUrl/admin/events/1"

# Test 7: Get Active Events
Invoke-ApiTest -Name "7. Get Active Events (Mobile App)" `
    -Method "GET" `
    -Uri "$BaseUrl/events/active?userId=1"

# Test 8: First Check-in
Invoke-ApiTest -Name "8. First Check-in (Should Succeed)" `
    -Method "POST" `
    -Uri "$BaseUrl/attendance/checkin" `
    -Body @{
    userId      = 1
    eventId     = 1
    eventTitle  = "Sunday Worship Service"
    timestamp   = "2026-05-25T09:05:00Z"
    latitude    = 6.5244
    longitude   = 3.3792
    deviceInfo  = "Android 14 - Pixel 7"
    isSynced    = $true
}

# Test 9: Duplicate Check-in (Should Return 409)
Invoke-ApiTest -Name "9. Duplicate Check-in (Should Return 409)" `
    -Method "POST" `
    -Uri "$BaseUrl/attendance/checkin" `
    -Body @{
    userId      = 1
    eventId     = 1
    eventTitle  = "Sunday Worship Service"
    timestamp   = "2026-05-25T10:30:00Z"
    latitude    = 6.5244
    longitude   = 3.3792
    deviceInfo  = "Android 14 - Pixel 7"
    isSynced    = $true
}

# Test 10: Get Attendance History
Invoke-ApiTest -Name "10. Get Attendance History" `
    -Method "GET" `
    -Uri "$BaseUrl/attendance/history?userId=1"

# Test 11: Update Event
Invoke-ApiTest -Name "11. Update Event" `
    -Method "PUT" `
    -Uri "$BaseUrl/admin/events/1" `
    -Body @{
    id                      = 1
    title                   = "Sunday Worship Service - Updated"
    venueName               = "Resurrection Main Auditorium"
    address                 = "12 Faith Avenue, Lagos"
    latitude                = 6.5244
    longitude               = 3.3792
    radius                  = 600
    startTime               = "2026-05-25T08:30:00Z"
    endTime                 = "2026-05-25T13:00:00Z"
    isActive                = $true
    recurrenceType          = "every_sunday"
    recurrenceDay           = "Sunday"
    repeatUntil             = "2027-12-31"
    reuseSameLocation       = $true
    autoActivateRecurring   = $true
}

# Test 12: Update Event Status (Deactivate)
Invoke-ApiTest -Name "12. Update Event Status (Deactivate)" `
    -Method "PATCH" `
    -Uri "$BaseUrl/admin/events/1/status" `
    -Body @{
    isActive = $false
}

# Test 13: Update Event Status (Activate)
Invoke-ApiTest -Name "13. Update Event Status (Activate)" `
    -Method "PATCH" `
    -Uri "$BaseUrl/admin/events/1/status" `
    -Body @{
    isActive = $true
}

Write-Host ""
Write-Host "================================" -ForegroundColor Green
Write-Host "Test sequence completed!" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Green
