-- ============================================================================
-- VOLUNTEERS TABLE - Level 1 Follow-Up Team
-- Level 1 Follow-Up System Database Schema (MySQL)
-- ============================================================================

-- Drop table if it exists (for development/testing)
-- DROP TABLE IF EXISTS volunteers;

-- Create VOLUNTEERS table
CREATE TABLE IF NOT EXISTS volunteers (
    -- Primary Key
    volunteer_id           VARCHAR(20) PRIMARY KEY,
    
    -- Personal Information
    first_name          VARCHAR(50) NOT NULL,
    last_name           VARCHAR(50) NOT NULL,
    email               VARCHAR(100) NOT NULL UNIQUE,
    phone               VARCHAR(20),
    
    -- Volunteer Status
    status              VARCHAR(30) NOT NULL DEFAULT 'Active',
    level               VARCHAR(20) NOT NULL DEFAULT 'Level 0',
    start_date          DATE NOT NULL,
    end_date            DATE,
    
    -- Capacity Management
    capacity_band       VARCHAR(20) NOT NULL DEFAULT 'Balanced',
    capacity_min        INT NOT NULL,
    capacity_max        INT NOT NULL,
    current_assignments INT DEFAULT 0,
    
    -- Performance Metrics
    total_completed     INT DEFAULT 0,
    total_assigned      INT DEFAULT 0,
    completion_rate     DECIMAL(5,2),
    avg_response_time   DECIMAL(5,2),
    
    -- Health Indicators
    last_check_in       DATE,
    next_check_in       DATE,
    emotional_tone      VARCHAR(10),
    vnps_score          INT CHECK (vnps_score >= 0 AND vnps_score <= 10),
    burnout_risk        VARCHAR(20),
    
    -- Team Assignment
    team_lead           VARCHAR(20),
    campus              VARCHAR(50),
    
    -- Training & Compliance
    level_0_complete    DATE,
    crisis_trained      DATE,
    confidentiality_signed DATE,
    background_check    DATE,
    
    -- Boundary Tracking
    boundary_violations INT DEFAULT 0,
    last_violation_date DATE,
    
    -- Metadata
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

-- ============================================================================
-- INDEXES - For Performance Optimization
-- ============================================================================

-- Primary lookups
CREATE INDEX IF NOT EXISTS idx_volunteers_status 
    ON volunteers(status);

CREATE INDEX IF NOT EXISTS idx_volunteers_team_lead 
    ON volunteers(team_lead);

CREATE INDEX IF NOT EXISTS idx_volunteers_capacity_band 
    ON volunteers(capacity_band);

CREATE INDEX IF NOT EXISTS idx_volunteers_email 
    ON volunteers(email);

-- Composite indexes for common queries
CREATE INDEX IF NOT EXISTS idx_volunteers_team_status 
    ON volunteers(team_lead, status);

CREATE INDEX IF NOT EXISTS idx_volunteers_status_level
    ON volunteers(status, level);

-- ============================================================================
-- SAMPLE DATA - For Development/Testing
-- ============================================================================

INSERT INTO volunteers (
    volunteer_id, first_name, last_name, email, phone, status, level,
    start_date, end_date, capacity_band, capacity_min, capacity_max,
    current_assignments, total_completed, total_assigned, completion_rate,
    avg_response_time, last_check_in, next_check_in, emotional_tone,
    vnps_score, burnout_risk, team_lead, campus,
    level_0_complete, crisis_trained, confidentiality_signed, background_check,
    boundary_violations, last_violation_date, created_at, updated_at
) VALUES
(
    'V001', 'Sarah', 'Johnson', 'sarah.volunteer@church.com', '555-1001',
    'Active', 'Level 1', '2024-11-15', NULL,
    'Balanced', 2, 3, 3, 47, 52, 90.38, 1.2,
    '2025-01-15', '2025-02-15', '??', 9, 'Low',
    'TL001', 'Main Campus',
    '2024-11-01', '2024-11-08', '2024-11-05', '2024-10-20',
    0, NULL, NOW(), NOW()
),
(
    'V002', 'Mike', 'Thompson', 'mike.volunteer@church.com', '555-1002',
    'Active', 'Level 1', '2024-12-01', NULL,
    'Consistent', 4, 6, 4, 32, 40, 80.00, 1.5,
    '2025-01-10', '2025-02-10', '??', 7, 'Medium',
    'TL001', 'Main Campus',
    '2024-11-15', '2024-11-22', '2024-11-18', '2024-11-01',
    0, NULL, NOW(), NOW()
),
(
    'V003', 'Emily', 'Davis', 'emily.volunteer@church.com', '555-1003',
    'Active', 'Level 0', '2025-01-01', NULL,
    'Limited', 1, 2, 1, 5, 8, 62.50, 2.0,
    '2025-01-20', '2025-02-20', '??', 8, 'Low',
    'TL001', 'Main Campus',
    '2024-12-15', NULL, '2024-12-20', '2024-12-01',
    0, NULL, NOW(), NOW()
),
(
    'V004', 'James', 'Wilson', 'james.volunteer@church.com', '555-1004',
    'Care Path', 'Level 1', '2024-08-15', NULL,
    'Balanced', 2, 3, 2, 28, 35, 80.00, 1.8,
    '2025-01-05', '2025-02-05', '??', 5, 'High',
    'TL001', 'Main Campus',
    '2024-08-01', '2024-08-10', '2024-08-05', '2024-07-20',
    0, NULL, NOW(), NOW()
);

-- ============================================================================
-- VERIFY DATA
-- ============================================================================

-- View all volunteers
-- SELECT * FROM volunteers;

-- Count total records
-- SELECT COUNT(*) as total_count FROM volunteers;

-- View volunteers by status
-- SELECT status, COUNT(*) as count FROM volunteers GROUP BY status;

-- View active volunteers by team lead
-- SELECT volunteer_id, CONCAT(first_name, ' ', last_name) as full_name, 
--        status, capacity_band, completion_rate 
-- FROM volunteers 
-- WHERE status = 'Active' AND team_lead = 'TL001';

-- ============================================================================
-- ENUM VALUES REFERENCE (for application validation)
-- ============================================================================

/*
status values:
  - 'Active'
  - 'Care Path'
  - 'Paused'
  - 'Exited'
  - 'Level 0'

level values:
  - 'Level 0'
  - 'Level 1'
  - 'Level 2'
  - 'Level 3'

capacity_band values:
  - 'Consistent' (4-6 per week)
  - 'Balanced' (2-3 per week)
  - 'Limited' (1-2 per week)

emotional_tone values:
  - '??'
  - '??'
  - '??'

burnout_risk values:
  - 'Low'
  - 'Medium'
  - 'High'
*/

-- ============================================================================
-- END OF SCRIPT
-- ============================================================================
