#!/bin/bash
# API Test Commands for RM Office Attendance System

BASE_URL="http://localhost:5000/api"

echo "================================"
echo "1. Check User (Non-existent)"
echo "================================"
curl -X POST "$BASE_URL/user/check" \
  -H "Content-Type: application/json" \
  -d '{
    "mobileNumber": "08012345678"
  }'

echo -e "\n\n================================"
echo "2. Register User"
echo "================================"
curl -X POST "$BASE_URL/user/register" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d 'name=John Doe&mobileNumber=08012345678'

echo -e "\n\n================================"
echo "3. Check User (Existing)"
echo "================================"
curl -X POST "$BASE_URL/user/check" \
  -H "Content-Type: application/json" \
  -d '{
    "mobileNumber": "08012345678"
  }'

echo -e "\n\n================================"
echo "4. Create Event (Admin)"
echo "================================"
curl -X POST "$BASE_URL/admin/events" \
  -H "Content-Type: application/json" \
  -d '{
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
  }'

echo -e "\n\n================================"
echo "5. Get All Events (Admin)"
echo "================================"
curl -X GET "$BASE_URL/admin/events"

echo -e "\n\n================================"
echo "6. Get Event by ID (Admin)"
echo "================================"
curl -X GET "$BASE_URL/admin/events/1"

echo -e "\n\n================================"
echo "7. Get Active Events (Mobile App)"
echo "================================"
curl -X GET "$BASE_URL/events/active?userId=1"

echo -e "\n\n================================"
echo "8. First Check-in (Should Succeed)"
echo "================================"
curl -X POST "$BASE_URL/attendance/checkin" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": 1,
    "eventId": 1,
    "eventTitle": "Sunday Worship Service",
    "timestamp": "2026-05-25T09:05:00Z",
    "latitude": 6.5244,
    "longitude": 3.3792,
    "deviceInfo": "Android 14 - Pixel 7",
    "isSynced": true
  }'

echo -e "\n\n================================"
echo "9. Duplicate Check-in (Should Return 409)"
echo "================================"
curl -X POST "$BASE_URL/attendance/checkin" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": 1,
    "eventId": 1,
    "eventTitle": "Sunday Worship Service",
    "timestamp": "2026-05-25T10:30:00Z",
    "latitude": 6.5244,
    "longitude": 3.3792,
    "deviceInfo": "Android 14 - Pixel 7",
    "isSynced": true
  }'

echo -e "\n\n================================"
echo "10. Get Attendance History"
echo "================================"
curl -X GET "$BASE_URL/attendance/history?userId=1"

echo -e "\n\n================================"
echo "11. Update Event"
echo "================================"
curl -X PUT "$BASE_URL/admin/events/1" \
  -H "Content-Type: application/json" \
  -d '{
    "id": 1,
    "title": "Sunday Worship Service - Updated",
    "venueName": "Resurrection Main Auditorium",
    "address": "12 Faith Avenue, Lagos",
    "latitude": 6.5244,
    "longitude": 3.3792,
    "radius": 600,
    "startTime": "2026-05-25T08:30:00Z",
    "endTime": "2026-05-25T13:00:00Z",
    "isActive": true,
    "recurrenceType": "every_sunday",
    "recurrenceDay": "Sunday",
    "repeatUntil": "2027-12-31",
    "reuseSameLocation": true,
    "autoActivateRecurring": true
  }'

echo -e "\n\n================================"
echo "12. Update Event Status (Deactivate)"
echo "================================"
curl -X PATCH "$BASE_URL/admin/events/1/status" \
  -H "Content-Type: application/json" \
  -d '{
    "isActive": false
  }'

echo -e "\n\n================================"
echo "13. Update Event Status (Activate)"
echo "================================"
curl -X PATCH "$BASE_URL/admin/events/1/status" \
  -H "Content-Type: application/json" \
  -d '{
    "isActive": true
  }'

echo -e "\n\nTest sequence completed!"
