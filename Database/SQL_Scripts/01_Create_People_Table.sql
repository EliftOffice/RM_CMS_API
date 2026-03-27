-- ============================================================================
-- PEOPLE TABLE - Visitors/Members Being Followed Up
-- Level 1 Follow-Up System Database Schema
-- ============================================================================

-- Drop table if it exists (for development/testing)
-- DROP TABLE IF EXISTS people;

-- Create PEOPLE table
CREATE TABLE people (
    -- Primary Key
    person_id           VARCHAR(20) PRIMARY KEY,
    
    -- Basic Information
    first_name          VARCHAR(50) NOT NULL,
    last_name           VARCHAR(50) NOT NULL,
    email               VARCHAR(100),
    phone               VARCHAR(20),
    
    -- Demographics
    age_range           VARCHAR(20),
    household_type      VARCHAR(50),
    zip_code            VARCHAR(10),
    
    -- Church Context
    visit_type          VARCHAR(30) NOT NULL,
    first_visit_date    DATE NOT NULL,
    last_visit_date     DATE,
    visit_count         INTEGER DEFAULT 1,
    connection_source   VARCHAR(50),
    campus              VARCHAR(50),
    
    -- Follow-Up Status
    follow_up_status    VARCHAR(30) NOT NULL,
    follow_up_priority  VARCHAR(20),
    assigned_volunteer  VARCHAR(20),
    assigned_date       DATE,
    last_contact_date   DATE,
    next_action_date    DATE,
    
    -- Interests & Needs
    interested_in       TEXT,
    prayer_requests     TEXT,
    specific_needs      TEXT,
    
    -- Metadata
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    created_by          VARCHAR(50),
    
    -- Foreign Keys (when volunteers table exists)
    -- FOREIGN KEY (assigned_volunteer) REFERENCES volunteers(volunteer_id)
);

-- ============================================================================
-- INDEXES - For Performance Optimization
-- ============================================================================

-- Primary lookups
CREATE INDEX idx_people_status 
    ON people(follow_up_status);

CREATE INDEX idx_people_assigned 
    ON people(assigned_volunteer);

CREATE INDEX idx_people_created 
    ON people(created_at DESC);

CREATE INDEX idx_people_priority 
    ON people(follow_up_priority);

-- Composite indexes for common queries
CREATE INDEX idx_people_status_priority 
    ON people(follow_up_status, follow_up_priority);

-- ============================================================================
-- SAMPLE DATA - For Development/Testing
-- ============================================================================

INSERT INTO people (
    person_id, first_name, last_name, email, phone, age_range, 
    household_type, zip_code, visit_type, first_visit_date, 
    last_visit_date, visit_count, connection_source, campus, 
    follow_up_status, follow_up_priority, assigned_volunteer, 
    assigned_date, last_contact_date, next_action_date, 
    interested_in, prayer_requests, specific_needs, 
    created_at, updated_at, created_by
) VALUES
(
    'P001', 'Sarah', 'Johnson', 'sarah.j@email.com', '555-0101', 
    '26-35', 'Married', '12345', 'First-Time Visitor', '2025-01-15', 
    '2025-01-15', 1, 'Friend/Family Invite', 'Main Campus', 
    'Contacted', 'Normal', 'V001', '2025-01-16', '2025-01-18', NULL, 
    'Small Groups, Serving', NULL, NULL, 
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 'Admin'
),
(
    'P002', 'Mike', 'Thompson', 'mike.t@email.com', '555-0102',
    '36-50', 'Family with Kids', '12346', 'Returning Visitor', '2025-01-08',
    '2025-01-22', 3, 'Online Search', 'Main Campus', 
    'Complete', 'Normal', 'V002', '2025-01-10', '2025-01-12', NULL,
    'Kids Ministry', 'Prayer for job situation', NULL,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 'Admin'
),
(
    'P003', 'John', 'Davis', 'john.d@email.com', '555-0103',
    '18-25', 'Single', '12347', 'First-Time Visitor', '2025-01-20',
    NULL, 1, 'Drove By', 'Main Campus',
    'New', 'High', NULL, NULL, NULL, NULL,
    'Worship, Community', NULL, 'New believer',
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 'Admin'
),
(
    'P004', 'Emma', 'Wilson', 'emma.w@email.com', '555-0104',
    '51-65', 'Married', '12348', 'Inactive Member', '2024-08-15',
    '2024-08-15', 2, 'Member', 'Main Campus',
    'Retry Pending', 'Urgent', 'V001', '2025-01-17', NULL, '2025-01-24',
    'Prayer Group', 'Health issues', NULL,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 'Admin'
);

-- ============================================================================
-- VERIFY DATA
-- ============================================================================

-- View all people
SELECT * FROM people;

-- Count total records
SELECT COUNT(*) as total_count FROM people;

-- View people by status
SELECT 
    follow_up_status,
    COUNT(*) as count
FROM people
GROUP BY follow_up_status;

-- View unassigned people
SELECT 
    person_id,
    CONCAT(first_name, ' ', last_name) as full_name,
    follow_up_status,
    follow_up_priority,
    created_at
FROM people
WHERE assigned_volunteer IS NULL;

-- ============================================================================
-- ENUM VALUES REFERENCE (for application validation)
-- ============================================================================

/*
visit_type values:
  - 'First-Time Visitor'
  - 'Returning Visitor'
  - 'New Member'
  - 'Inactive Member'
  - 'Guest'

follow_up_status values:
  - 'New'
  - 'Assigned'
  - 'Contacted'
  - 'Retry Pending'
  - 'Escalated'
  - 'Complete'
  - 'Unresponsive'

follow_up_priority values:
  - 'Normal'
  - 'High'
  - 'Urgent'

connection_source values:
  - 'Friend/Family Invite'
  - 'Online Search'
  - 'Social Media'
  - 'Drove By'
  - 'Event'
  - 'Other'

age_range values:
  - 'Under 18'
  - '18-25'
  - '26-35'
  - '36-50'
  - '51-65'
  - '65+'
  - 'Prefer not to say'

household_type values:
  - 'Single'
  - 'Married'
  - 'Family with Kids'
  - 'Empty Nest'
  - 'Other'
*/

-- ============================================================================
-- END OF SCRIPT
-- ============================================================================
