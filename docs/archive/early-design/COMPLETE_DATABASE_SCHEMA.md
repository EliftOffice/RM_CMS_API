🗄️ COMPLETE DATABASE SCHEMA FOR LEVEL 1 FOLLOW-UP SYSTEM
Comprehensive Database Design with Tables, Fields, Relationships & Sample Data

🎯 DATABASE OVERVIEW
System Architecture
┌─────────────────────────────────────────────────────────┐
│                  LEVEL 1 FOLLOW-UP DATABASE             │
└─────────────────────────────────────────────────────────┘
                          │
        ┌─────────────────┼─────────────────┐
        │                 │                 │
    ┌───▼────┐      ┌────▼─────┐     ┌────▼─────┐
    │ PEOPLE │      │VOLUNTEERS│     │ TEAM     │
    │ (Core) │      │          │     │ LEADS    │
    └───┬────┘      └────┬─────┘     └────┬─────┘
        │                │                 │
        │         ┌──────▼──────┐          │
        └────────►│  FOLLOW-UPS │◄─────────┘
                  │  (Activity) │
                  └──────┬──────┘
                         │
            ┌────────────┼────────────┐
            │            │            │
      ┌─────▼─────┐ ┌───▼────┐ ┌────▼──────┐
      │ CHECK-INS │ │ NOTES  │ │ESCALATIONS│
      │(1-on-1s)  │ │        │ │           │
      └───────────┘ └────────┘ └───────────┘

📊 DATABASE TABLES (Complete Specification)

TABLE 1: PEOPLE (Contacts Being Followed Up)
Purpose: Central registry of all visitors/members who need follow-up
Schema:
sqlCREATE TABLE people (
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
    
    -- Foreign Keys
    FOREIGN KEY (assigned_volunteer) REFERENCES volunteers(volunteer_id)
);

Field Definitions:
Field NameTypeDescriptionExample Valuesperson_idVARCHAR(20)Unique identifierP001, P002, P2025001first_nameVARCHAR(50)First nameSarah, Johnlast_nameVARCHAR(50)Last nameJohnson, DavisemailVARCHAR(100)Email addresssarah@email.comphoneVARCHAR(20)Phone number555-123-4567age_rangeVARCHAR(20)Age bracket18-25, 26-35, 36-50, 51-65, 65+household_typeVARCHAR(50)Household compositionSingle, Married, Family with Kids, Empty Nestvisit_typeVARCHAR(30)Type of visitorFirst-Time, Returning, New Member, Inactive Memberfirst_visit_dateDATEDate of first visit2025-01-15connection_sourceVARCHAR(50)How they found churchFriend Invite, Online, Drove By, Eventfollow_up_statusVARCHAR(30)Current status in pipelineNew, Assigned, Contacted, Complete, Unresponsivefollow_up_priorityVARCHAR(20)Urgency levelNormal, High, Urgentassigned_volunteerVARCHAR(20)Who's responsible (FK)V001, V002next_action_dateDATEWhen next action due2025-01-22interested_inTEXTAreas of interestSmall Groups, Serving, Counselingspecific_needsTEXTDisclosed needsFinancial Help, Prayer, Community

Enumerated Values (Dropdown Options):
sql-- visit_type
ENUM('First-Time Visitor', 'Returning Visitor', 'New Member', 'Inactive Member', 'Guest')

-- follow_up_status
ENUM('New', 'Assigned', 'Contacted', 'Retry Pending', 'Escalated', 'Complete', 'Unresponsive')

-- follow_up_priority
ENUM('Normal', 'High', 'Urgent')

-- connection_source
ENUM('Friend/Family Invite', 'Online Search', 'Social Media', 'Drove By', 'Event', 'Other')

-- age_range
ENUM('Under 18', '18-25', '26-35', '36-50', '51-65', '65+', 'Prefer not to say')

Sample Data:
sqlINSERT INTO people VALUES
('P001', 'Sarah', 'Johnson', 'sarah.j@email.com', '555-0101', 
 '26-35', 'Married', '12345', 'First-Time Visitor', '2025-01-15', 
 '2025-01-15', 1, 'Friend Invite', 'Main Campus', 'Contacted', 
 'Normal', 'V001', '2025-01-16', '2025-01-18', NULL, 
 'Small Groups, Serving', NULL, NULL, 
 CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 'Admin'),

('P002', 'Mike', 'Thompson', 'mike.t@email.com', '555-0102',
 '36-50', 'Family with Kids', '12346', 'Returning Visitor', '2025-01-08',
 '2025-01-22', 3, 'Online Search', 'Main Campus', 'Complete',
 'Normal', 'V002', '2025-01-10', '2025-01-12', NULL,
 'Kids Ministry', 'Prayer for job situation', NULL,
 CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 'Admin');

TABLE 2: VOLUNTEERS (Level 1 Follow-Up Team)
Purpose: Master list of all Level 1 volunteers with capacity & status
Schema:
sqlCREATE TABLE volunteers (
    -- Primary Key
    volunteer_id        VARCHAR(20) PRIMARY KEY,
    
    -- Personal Information
    first_name          VARCHAR(50) NOT NULL,
    last_name           VARCHAR(50) NOT NULL,
    email               VARCHAR(100) NOT NULL,
    phone               VARCHAR(20),
    
    -- Volunteer Status
    status              VARCHAR(30) NOT NULL,
    level               VARCHAR(20) NOT NULL,
    start_date          DATE NOT NULL,
    end_date            DATE,
    
    -- Capacity Management
    capacity_band       VARCHAR(20) NOT NULL,
    capacity_min        INTEGER NOT NULL,
    capacity_max        INTEGER NOT NULL,
    current_assignments INTEGER DEFAULT 0,
    
    -- Performance Metrics
    total_completed     INTEGER DEFAULT 0,
    total_assigned      INTEGER DEFAULT 0,
    completion_rate     DECIMAL(5,2),
    avg_response_time   DECIMAL(5,2),
    
    -- Health Indicators
    last_check_in       DATE,
    next_check_in       DATE,
    emotional_tone      VARCHAR(10),
    vnps_score          INTEGER,
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
    boundary_violations INTEGER DEFAULT 0,
    last_violation_date DATE,
    
    -- Metadata
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    
    -- Foreign Keys
    FOREIGN KEY (team_lead) REFERENCES team_leads(team_lead_id)
);

Field Definitions:
Field NameTypeDescriptionExample Valuesvolunteer_idVARCHAR(20)Unique identifierV001, V002statusVARCHAR(30)Current statusActive, Care Path, Paused, ExitedlevelVARCHAR(20)Current level in systemLevel 0, Level 1, Level 2capacity_bandVARCHAR(20)Self-chosen capacityConsistent, Balanced, Limitedcapacity_minINTEGERMinimum per week4, 2, 1capacity_maxINTEGERMaximum per week6, 3, 2current_assignmentsINTEGERActive assignments now3, 5, 1completion_rateDECIMAL(5,2)% of assigned completed88.50emotional_toneVARCHAR(10)From check-ins😊, 😐, 😞vnps_scoreINTEGERVolunteer NPS (0-10)8, 9, 10burnout_riskVARCHAR(20)Risk levelLow, Medium, High

Enumerated Values:
sql-- status
ENUM('Active', 'Care Path', 'Paused', 'Exited', 'Level 0')

-- level
ENUM('Level 0', 'Level 1', 'Level 2', 'Level 3')

-- capacity_band
ENUM('Consistent', 'Balanced', 'Limited')

-- emotional_tone
ENUM('😊', '😐', '😞')

-- burnout_risk
ENUM('Low', 'Medium', 'High')

Capacity Band Definitions (Reference Table):
sqlCREATE TABLE capacity_bands (
    band_name       VARCHAR(20) PRIMARY KEY,
    min_per_week    INTEGER NOT NULL,
    max_per_week    INTEGER NOT NULL,
    description     TEXT
);

INSERT INTO capacity_bands VALUES
('Consistent', 4, 6, 'High capacity - 4-6 follow-ups per week'),
('Balanced', 2, 3, 'Moderate capacity - 2-3 follow-ups per week'),
('Limited', 1, 2, 'Low capacity - 1-2 follow-ups per week');

Sample Data:
sqlINSERT INTO volunteers VALUES
('V001', 'Sarah', 'Johnson', 'sarah.volunteer@church.com', '555-1001',
 'Active', 'Level 1', '2024-11-15', NULL,
 'Balanced', 2, 3, 3,
 47, 52, 90.38, 1.2,
 '2025-01-15', '2025-02-15', '😊', 9, 'Low',
 'TL001', 'Main Campus',
 '2024-11-01', '2024-11-08', '2024-11-05', '2024-10-20',
 0, NULL,
 CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),

('V002', 'Mike', 'Thompson', 'mike.volunteer@church.com', '555-1002',
 'Active', 'Level 1', '2024-12-01', NULL,
 'Consistent', 4, 6, 4,
 32, 40, 80.00, 1.5,
 '2025-01-10', '2025-02-10', '😐', 7, 'Medium',
 'TL001', 'Main Campus',
 '2024-11-15', '2024-11-22', '2024-11-18', '2024-11-01',
 0, NULL,
 CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

TABLE 3: TEAM_LEADS (Level 1 Supervisors)
Purpose: Team Lead information and span of control tracking
Schema:
sqlCREATE TABLE team_leads (
    -- Primary Key
    team_lead_id        VARCHAR(20) PRIMARY KEY,
    
    -- Personal Information
    first_name          VARCHAR(50) NOT NULL,
    last_name           VARCHAR(50) NOT NULL,
    email               VARCHAR(100) NOT NULL,
    phone               VARCHAR(20),
    
    -- Role Information
    role_type           VARCHAR(30) NOT NULL,
    campus              VARCHAR(50),
    start_date          DATE NOT NULL,
    term_end_date       DATE,
    
    -- Span of Control
    max_volunteers      INTEGER NOT NULL,
    current_volunteers  INTEGER DEFAULT 0,
    
    -- Performance Metrics
    team_vnps_avg       DECIMAL(5,2),
    team_retention_rate DECIMAL(5,2),
    team_completion_rate DECIMAL(5,2),
    boundary_incidents  INTEGER DEFAULT 0,
    
    -- Metadata
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

Field Definitions:
Field NameTypeDescriptionExample Valuesteam_lead_idVARCHAR(20)Unique identifierTL001, TL002role_typeVARCHAR(30)Type of Team LeadPlayer-Coach, Full-Timemax_volunteersINTEGERSpan of control limit8 (player-coach), 12 (full-time)current_volunteersINTEGERCurrent team size4, 7, 10team_vnps_avgDECIMAL(5,2)Average team vNPS52.5, 48.0term_end_dateDATEWhen term expires (24 mo)2026-12-01

Sample Data:
sqlINSERT INTO team_leads VALUES
('TL001', 'John', 'Smith', 'john.smith@church.com', '555-2001',
 'Player-Coach', 'Main Campus', '2024-06-01', '2026-06-01',
 8, 4,
 52.00, 90.00, 88.00, 0,
 CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

TABLE 4: FOLLOW_UPS (Activity Log - Core Transaction Table)
Purpose: Logs every follow-up attempt with outcome
Schema:
sqlCREATE TABLE follow_ups (
    -- Primary Key
    follow_up_id        VARCHAR(20) PRIMARY KEY,
    
    -- References
    person_id           VARCHAR(20) NOT NULL,
    volunteer_id        VARCHAR(20) NOT NULL,
    team_lead_id        VARCHAR(20),
    
    -- Attempt Details
    attempt_number      INTEGER NOT NULL,
    attempt_date        DATE NOT NULL,
    attempt_time        TIME,
    contact_method      VARCHAR(20),
    
    -- Outcome
    contact_status      VARCHAR(30) NOT NULL,
    response_type       VARCHAR(30),
    call_duration_min   INTEGER,
    
    -- Follow-Up Action
    next_action         VARCHAR(50),
    next_action_date    DATE,
    escalation_tier     VARCHAR(20),
    
    -- Documentation
    notes               TEXT,
    tags                VARCHAR(200),
    
    -- Derived Fields
    week_number         INTEGER,
    month_number        INTEGER,
    quarter_number      INTEGER,
    year                INTEGER,
    
    -- Metadata
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    
    -- Foreign Keys
    FOREIGN KEY (person_id) REFERENCES people(person_id),
    FOREIGN KEY (volunteer_id) REFERENCES volunteers(volunteer_id),
    FOREIGN KEY (team_lead_id) REFERENCES team_leads(team_lead_id)
);

Field Definitions:
Field NameTypeDescriptionExample Valuesfollow_up_idVARCHAR(20)Unique identifierFU001, FU2025001attempt_numberINTEGERWhich attempt (1, 2, 3)1, 2, 3contact_methodVARCHAR(20)How contactedPhone, Text, Emailcontact_statusVARCHAR(30)Was contact made?Contacted, Not Contacted, Unresponsiveresponse_typeVARCHAR(30)Outcome if contactedNormal, Needs Follow-Up, Crisiscall_duration_minINTEGERLength of conversation5, 8, 12next_actionVARCHAR(50)What happens nextMark Complete, Retry in 3 Days, Escalateescalation_tierVARCHAR(20)If escalatedStandard, Urgent, Emergencyweek_numberINTEGERISO week number4, 5, 6

Enumerated Values:
sql-- contact_method
ENUM('Phone Call', 'Text Message', 'Email', 'In-Person')

-- contact_status
ENUM('Contacted', 'Not Contacted', 'Unresponsive', 'Wrong Number')

-- response_type
ENUM('Normal', 'Needs Follow-Up', 'Crisis', 'No Response', 'Unsubscribe')

-- next_action
ENUM('Mark Complete', 'Retry in 3 Days', 'Escalate to Team Lead', 'Crisis Protocol', 'Archive')

-- escalation_tier
ENUM('Standard', 'Urgent', 'Emergency')

Sample Data:
sqlINSERT INTO follow_ups VALUES
('FU001', 'P001', 'V001', 'TL001',
 1, '2025-01-18', '10:30:00', 'Phone Call',
 'Contacted', 'Normal', 8,
 'Mark Complete', NULL, NULL,
 'Great conversation. Interested in small groups. Connected to Community Pastor.',
 'small-groups, positive',
 3, 1, 1, 2025,
 CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),

('FU002', 'P002', 'V002', 'TL001',
 1, '2025-01-20', '14:15:00', 'Phone Call',
 'Contacted', 'Needs Follow-Up', 12,
 'Escalate to Team Lead', '2025-01-21', 'Urgent',
 'Disclosed financial hardship. Needs benevolence assessment.',
 'financial, escalated',
 3, 1, 1, 2025,
 CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),

('FU003', 'P003', 'V001', 'TL001',
 1, '2025-01-19', '09:00:00', 'Phone Call',
 'Not Contacted', NULL, NULL,
 'Retry in 3 Days', '2025-01-22', NULL,
 'No answer. Left voicemail.',
 'no-answer, retry',
 3, 1, 1, 2025,
 CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

TABLE 5: CHECK_INS (Monthly 1-on-1 Meetings)
Purpose: Track monthly volunteer check-in meetings
Schema:
sqlCREATE TABLE check_ins (
    -- Primary Key
    check_in_id         VARCHAR(20) PRIMARY KEY,
    
    -- References
    volunteer_id        VARCHAR(20) NOT NULL,
    team_lead_id        VARCHAR(20) NOT NULL,
    
    -- Meeting Details
    check_in_date       DATE NOT NULL,
    duration_min        INTEGER,
    meeting_type        VARCHAR(30),
    
    -- Assessment
    emotional_tone      VARCHAR(10) NOT NULL,
    capacity_adjustment BOOLEAN DEFAULT FALSE,
    new_capacity_band   VARCHAR(20),
    concerns_noted      TEXT,
    follow_up_needed    BOOLEAN DEFAULT FALSE,
    
    -- Performance Discussion
    completion_rate_discussed BOOLEAN,
    boundary_issues     BOOLEAN DEFAULT FALSE,
    training_needs      TEXT,
    
    -- Next Steps
    action_items        TEXT,
    next_check_in_date  DATE,
    
    -- Metadata
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    
    -- Foreign Keys
    FOREIGN KEY (volunteer_id) REFERENCES volunteers(volunteer_id),
    FOREIGN KEY (team_lead_id) REFERENCES team_leads(team_lead_id)
);

Sample Data:
sqlINSERT INTO check_ins VALUES
('CI001', 'V001', 'TL001',
 '2025-01-15', 30, 'Monthly',
 '😊', FALSE, NULL, NULL, FALSE,
 TRUE, FALSE, NULL,
 'Keep up great work. Consider Level 2 in 3 months.', '2025-02-15',
 CURRENT_TIMESTAMP),

('CI002', 'V002', 'TL001',
 '2025-01-10', 45, 'Monthly',
 '😐', TRUE, 'Balanced', 'Feeling stretched with 6/week. Work got busier.', TRUE,
 TRUE, FALSE, 'Capacity management',
 'Reduce to Balanced band. Check in again in 2 weeks.', '2025-02-10',
 CURRENT_TIMESTAMP);

TABLE 6: ESCALATIONS (Cases Needing Team Lead Attention)
Purpose: Track escalated cases and resolution
Schema:
sqlCREATE TABLE escalations (
    -- Primary Key
    escalation_id       VARCHAR(20) PRIMARY KEY,
    
    -- References
    follow_up_id        VARCHAR(20) NOT NULL,
    person_id           VARCHAR(20) NOT NULL,
    volunteer_id        VARCHAR(20) NOT NULL,
    team_lead_id        VARCHAR(20),
    
    -- Escalation Details
    escalation_date     DATE NOT NULL,
    escalation_tier     VARCHAR(20) NOT NULL,
    escalation_reason   VARCHAR(50) NOT NULL,
    description         TEXT NOT NULL,
    
    -- Resolution
    status              VARCHAR(30) NOT NULL,
    assigned_to         VARCHAR(50),
    resolved_date       DATE,
    resolution_notes    TEXT,
    outcome             VARCHAR(50),
    
    -- Follow-Up Actions
    resource_connected  VARCHAR(100),
    follow_up_scheduled BOOLEAN DEFAULT FALSE,
    
    -- Crisis Handling (if Tier 3)
    crisis_protocol_followed BOOLEAN,
    authorities_contacted BOOLEAN,
    volunteer_debriefed BOOLEAN,
    
    -- Metadata
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    
    -- Foreign Keys
    FOREIGN KEY (follow_up_id) REFERENCES follow_ups(follow_up_id),
    FOREIGN KEY (person_id) REFERENCES people(person_id),
    FOREIGN KEY (volunteer_id) REFERENCES volunteers(volunteer_id),
    FOREIGN KEY (team_lead_id) REFERENCES team_leads(team_lead_id)
);

Enumerated Values:
sql-- escalation_tier
ENUM('Standard', 'Urgent', 'Emergency')

-- escalation_reason
ENUM('Needs Follow-Up', 'Financial Crisis', 'Health Crisis', 'Marriage Crisis', 
     'Spiritual Questions', 'Grief/Loss', 'Abuse Disclosure', 'Suicidal Ideation', 'Other')

-- status
ENUM('New', 'In Progress', 'Resolved', 'Referred Out', 'Closed')

-- outcome
ENUM('Connected to Resource', 'Pastoral Care Scheduled', 'Counseling Referral', 
     'Benevolence Provided', 'Emergency Services Called', 'Other')

Sample Data:
sqlINSERT INTO escalations VALUES
('ESC001', 'FU002', 'P002', 'V002', 'TL001',
 '2025-01-20', 'Urgent', 'Financial Crisis',
 'Member disclosed unable to pay rent this month. Has 2 children. Job loss 3 weeks ago.',
 'In Progress', 'Benevolence Team', NULL, NULL, NULL,
 'Benevolence Fund', TRUE,
 FALSE, FALSE, FALSE,
 CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

TABLE 7: NOTES (Additional Documentation)
Purpose: Flexible notes system for additional documentation
Schema:
sqlCREATE TABLE notes (
    -- Primary Key
    note_id             VARCHAR(20) PRIMARY KEY,
    
    -- References (polymorphic - can link to multiple entity types)
    entity_type         VARCHAR(20) NOT NULL,
    entity_id           VARCHAR(20) NOT NULL,
    
    -- Note Content
    note_type           VARCHAR(30),
    note_text           TEXT NOT NULL,
    tags                VARCHAR(200),
    
    -- Visibility
    is_private          BOOLEAN DEFAULT FALSE,
    visible_to_role     VARCHAR(30),
    
    -- Metadata
    created_by          VARCHAR(50) NOT NULL,
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

Field Definitions:
Field NameTypeDescriptionExample Valuesentity_typeVARCHAR(20)What this note is aboutPerson, Volunteer, Follow-Up, Check-Inentity_idVARCHAR(20)ID of that entityP001, V001, FU001note_typeVARCHAR(30)Category of noteGeneral, Prayer Request, Follow-Up, Alertis_privateBOOLEANRestricted visibility?TRUE, FALSEvisible_to_roleVARCHAR(30)Who can see if privateTeam Lead Only, Senior Pastor Only

Sample Data:
sqlINSERT INTO notes VALUES
('N001', 'Person', 'P001',
 'Follow-Up', 'Follow-up conversation went very well. Sarah is looking for community after moving to the area 6 months ago. Husband travels for work. Connected her to Tuesday night women''s group.',
 'positive, small-group',
 FALSE, NULL,
 'V001', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),

('N002', 'Volunteer', 'V002',
 'Alert', 'Mike mentioned in check-in that his work travel schedule has increased. May need to adjust capacity band from Consistent to Balanced.',
 'capacity, watch',
 TRUE, 'Team Lead Only',
 'TL001', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

TABLE 8: VNPS_SURVEYS (Volunteer Net Promoter Score)
Purpose: Track quarterly volunteer satisfaction surveys
Schema:
sqlCREATE TABLE vnps_surveys (
    -- Primary Key
    survey_id           VARCHAR(20) PRIMARY KEY,
    
    -- References
    volunteer_id        VARCHAR(20) NOT NULL,
    
    -- Survey Details
    survey_date         DATE NOT NULL,
    quarter             VARCHAR(10) NOT NULL,
    year                INTEGER NOT NULL,
    
    -- Core Question
    vnps_score          INTEGER NOT NULL CHECK (vnps_score BETWEEN 0 AND 10),
    vnps_category       VARCHAR(20),
    
    -- Open-Ended Feedback
    what_working_well   TEXT,
    what_could_improve  TEXT,
    additional_feedback TEXT,
    
    -- Sentiment Analysis
    sentiment           VARCHAR(20),
    
    -- Metadata
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    
    -- Foreign Keys
    FOREIGN KEY (volunteer_id) REFERENCES volunteers(volunteer_id)
);

Field Definitions:
Field NameTypeDescriptionExample Valuesvnps_scoreINTEGER0-10 rating0, 7, 9, 10vnps_categoryVARCHAR(20)Promoter/Passive/DetractorPromoter (9-10), Passive (7-8), Detractor (0-6)quarterVARCHAR(10)Which quarterQ1, Q2, Q3, Q4sentimentVARCHAR(20)Overall tonePositive, Neutral, Negative

vNPS Calculation Logic:
sql-- Auto-categorize based on score
UPDATE vnps_surveys
SET vnps_category = CASE
    WHEN vnps_score >= 9 THEN 'Promoter'
    WHEN vnps_score >= 7 THEN 'Passive'
    ELSE 'Detractor'
END;

-- Calculate team vNPS
SELECT 
    (COUNT(CASE WHEN vnps_category = 'Promoter' THEN 1 END) * 100.0 / COUNT(*)) -
    (COUNT(CASE WHEN vnps_category = 'Detractor' THEN 1 END) * 100.0 / COUNT(*)) AS team_vnps
FROM vnps_surveys
WHERE quarter = 'Q1' AND year = 2025;

Sample Data:
sqlINSERT INTO vnps_surveys VALUES
('VNP001', 'V001', '2025-01-31', 'Q1', 2025,
 9, 'Promoter',
 'Love the support from my Team Lead. Clear expectations. Feel like I''m making a difference.',
 'Would love more training on handling tough conversations.',
 'Great experience overall!',
 'Positive',
 CURRENT_TIMESTAMP),

('VNP002', 'V002', '2025-01-31', 'Q1', 2025,
 7, 'Passive',
 'Good system. Helps me stay organized.',
 'Sometimes feel like I''m just checking boxes. Want to see more impact stories.',
 NULL,
 'Neutral',
 CURRENT_TIMESTAMP);

TABLE 9: CAPACITY_HISTORY (Tracks Band Changes)
Purpose: Audit trail of capacity adjustments over time
Schema:
sqlCREATE TABLE capacity_history (
    -- Primary Key
    history_id          VARCHAR(20) PRIMARY KEY,
    
    -- References
    volunteer_id        VARCHAR(20) NOT NULL,
    
    -- Change Details
    change_date         DATE NOT NULL,
    old_capacity_band   VARCHAR(20),
    new_capacity_band   VARCHAR(20) NOT NULL,
    change_reason       VARCHAR(50),
    
    -- Context
    initiated_by        VARCHAR(50) NOT NULL,
    notes               TEXT,
    
    -- Metadata
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    
    -- Foreign Keys
    FOREIGN KEY (volunteer_id) REFERENCES volunteers(volunteer_id)
);

Sample Data:
sqlINSERT INTO capacity_history VALUES
('CH001', 'V002', '2025-01-10', 'Consistent', 'Balanced',
 'Work schedule changed',
 'Volunteer Request', 'Work travel increased. Requested reduction during monthly check-in.',
 CURRENT_TIMESTAMP);

TABLE 10: SYSTEM_CONFIG (Settings & Thresholds)
Purpose: Centralized configuration values
Schema:
sqlCREATE TABLE system_config (
    config_key          VARCHAR(50) PRIMARY KEY,
    config_value        VARCHAR(200) NOT NULL,
    config_type         VARCHAR(20) NOT NULL,
    description         TEXT,
    updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_by          VARCHAR(50)
);

Sample Data:
sqlINSERT INTO system_config VALUES
('consistent_min', '4', 'integer', 'Minimum follow-ups per week for Consistent band', CURRENT_TIMESTAMP, 'Admin'),
('consistent_max', '6', 'integer', 'Maximum follow-ups per week for Consistent band', CURRENT_TIMESTAMP, 'Admin'),
('balanced_min', '2', 'integer', 'Minimum follow-ups per week for Balanced band', CURRENT_TIMESTAMP, 'Admin'),
('balanced_max', '3', 'integer', 'Maximum follow-ups per week for Balanced band', CURRENT_TIMESTAMP, 'Admin'),
('limited_min', '1', 'integer', 'Minimum follow-ups per week for Limited band', CURRENT_TIMESTAMP, 'Admin'),
('limited_max', '2', 'integer', 'Maximum follow-ups per week for Limited band', CURRENT_TIMESTAMP, 'Admin'),
('green_threshold', '90', 'integer', 'Completion rate % for green flag', CURRENT_TIMESTAMP, 'Admin'),
('yellow_threshold', '75', 'integer', 'Completion rate % for yellow flag', CURRENT_TIMESTAMP, 'Admin'),
('red_threshold', '74', 'integer', 'Completion rate % below which red flag', CURRENT_TIMESTAMP, 'Admin'),
('max_retry_attempts', '3', 'integer', 'Maximum contact attempts before marking unresponsive', CURRENT_TIMESTAMP, 'Admin'),
('retry_delay_days', '3', 'integer', 'Days to wait before retry attempt', CURRENT_TIMESTAMP, 'Admin'),
('response_time_target', '48', 'integer', 'Target hours for first contact attempt', CURRENT_TIMESTAMP, 'Admin'),
('team_lead_span_player_coach', '8', 'integer', 'Max volunteers for player-coach Team Lead', CURRENT_TIMESTAMP, 'Admin'),
('team_lead_span_full_time', '12', 'integer', 'Max volunteers for full-time Team Lead', CURRENT_TIMESTAMP, 'Admin'),
('check_in_frequency_days', '30', 'integer', 'Days between monthly check-ins', CURRENT_TIMESTAMP, 'Admin'),
('vnps_frequency_months', '3', 'integer', 'Months between vNPS surveys', CURRENT_TIMESTAMP, 'Admin');
```

---

## 🔗 RELATIONSHIP DIAGRAM
```
┌────────────────┐
│   TEAM_LEADS   │
└───────┬────────┘
        │ 1
        │
        │ manages
        │
        │ N
┌───────▼────────┐
│  VOLUNTEERS    │◄──────────┐
└───────┬────────┘           │
        │ 1                  │
        │                    │
        │ assigned to        │
        │                    │
        │ N                  │ 1
┌───────▼────────┐     ┌────┴──────────┐
│  FOLLOW_UPS    │────►│    PEOPLE     │
└───────┬────────┘  N  └───────────────┘
        │ 1
        │
        │ may generate
        │
        │ N
┌───────▼────────┐
│  ESCALATIONS   │
└────────────────┘


┌────────────────┐
│  VOLUNTEERS    │
└───────┬────────┘
        │ 1
        │
        │ has
        │
        │ N
┌───────▼────────┐
│   CHECK_INS    │
└────────────────┘


┌────────────────┐
│  VOLUNTEERS    │
└───────┬────────┘
        │ 1
        │
        │ completes
        │
        │ N
┌───────▼────────┐
│ VNPS_SURVEYS   │
└────────────────┘


┌────────────────┐
│  VOLUNTEERS    │
└───────┬────────┘
        │ 1
        │
        │ has history
        │
        │ N
┌───────▼────────┐
│CAPACITY_HISTORY│
└────────────────┘


┌────────────────┐
│ NOTES          │ (Polymorphic - can link to any entity)
└────────────────┘
  entity_type + entity_id → Person, Volunteer, Follow-Up, etc.

📈 KEY QUERIES FOR DASHBOARD
Query 1: Team Health At-A-Glance
sqlSELECT 
    v.volunteer_id,
    CONCAT(v.first_name, ' ', v.last_name) AS volunteer_name,
    v.capacity_band,
    
    -- This Week
    (SELECT COUNT(*) 
     FROM follow_ups f
     WHERE f.volunteer_id = v.volunteer_id
       AND f.week_number = WEEK(CURRENT_DATE)
       AND f.contact_status = 'Contacted'
    ) AS this_week_completed,
    
    -- Last Week
    (SELECT COUNT(*) 
     FROM follow_ups f
     WHERE f.volunteer_id = v.volunteer_id
       AND f.week_number = WEEK(CURRENT_DATE) - 1
       AND f.contact_status = 'Contacted'
    ) AS last_week_completed,
    
    -- Trend
    CASE
        WHEN this_week_completed > last_week_completed THEN '⬆️'
        WHEN this_week_completed < last_week_completed THEN '⬇️'
        ELSE '➡️'
    END AS trend,
    
    -- Flag
    CASE
        WHEN (this_week_completed * 100.0 / v.capacity_max) >= 90 THEN '🟢'
        WHEN (this_week_completed * 100.0 / v.capacity_max) >= 75 THEN '🟡'
        ELSE '🔴'
    END AS flag
    
FROM volunteers v
WHERE v.status = 'Active'
  AND v.team_lead = 'TL001'
ORDER BY flag DESC, volunteer_name;

Query 2: Attention Needed Alerts
sqlSELECT 
    CONCAT(v.first_name, ' ', v.last_name) AS volunteer_name,
    CASE
        WHEN flag = '🔴' AND trend = '⬇️' THEN 'URGENT: 2 weeks declining performance'
        WHEN flag = '🟡' AND v.capacity_band = 'Consistent' THEN 'Check capacity (might need reduction)'
        WHEN DATEDIFF(CURRENT_DATE, v.last_check_in) > 35 THEN 'Monthly check-in overdue'
        ELSE NULL
    END AS alert_message,
    
    CASE
        WHEN flag = '🔴' THEN 1
        WHEN flag = '🟡' THEN 2
        ELSE 3
    END AS priority
    
FROM (
    -- Subquery with flag calculation
    SELECT 
        v.*,
        CASE
            WHEN (this_week_completed * 100.0 / v.capacity_max) >= 90 THEN '🟢'
            WHEN (this_week_completed * 100.0 / v.capacity_max) >= 75 THEN '🟡'
            ELSE '🔴'
        END AS flag,
        CASE
            WHEN this_week_completed > last_week_completed THEN '⬆️'
            WHEN this_week_completed < last_week_completed THEN '⬇️'
            ELSE '➡️'
        END AS trend
    FROM volunteers v
) AS v_with_flags
WHERE alert_message IS NOT NULL
ORDER BY priority, volunteer_name;

Query 3: Upcoming Check-Ins
sqlSELECT 
    CONCAT(v.first_name, ' ', v.last_name) AS volunteer_name,
    v.next_check_in AS due_date,
    DAYNAME(v.next_check_in) AS day_of_week,
    DATEDIFF(v.next_check_in, CURRENT_DATE) AS days_until
FROM volunteers v
WHERE v.status = 'Active'
  AND v.next_check_in BETWEEN CURRENT_DATE AND DATE_ADD(CURRENT_DATE, INTERVAL 7 DAY)
  AND v.team_lead = 'TL001'
ORDER BY v.next_check_in;

Query 4: Team Performance Summary
sqlSELECT 
    COUNT(*) AS total_follow_ups,
    SUM(CASE WHEN contact_status = 'Contacted' THEN 1 ELSE 0 END) AS completed,
    ROUND(SUM(CASE WHEN contact_status = 'Contacted' THEN 1 ELSE 0 END) * 100.0 / COUNT(*), 2) AS completion_rate,
    SUM(CASE WHEN response_type = 'Normal' THEN 1 ELSE 0 END) AS normal_conversations,
    SUM(CASE WHEN response_type = 'Needs Follow-Up' THEN 1 ELSE 0 END) AS needs_follow_up,
    SUM(CASE WHEN response_type = 'Crisis' THEN 1 ELSE 0 END) AS crisis_escalations,
    SUM(CASE WHEN contact_status = 'Not Contacted' AND next_action = 'Retry in 3 Days' THEN 1 ELSE 0 END) AS retry_pending,
    ROUND(AVG(call_duration_min), 1) AS avg_call_duration,
    ROUND(AVG(DATEDIFF(attempt_date, created_at)), 1) AS avg_response_time_days
FROM follow_ups
WHERE week_number = WEEK(CURRENT_DATE)
  AND volunteer_id IN (
      SELECT volunteer_id FROM volunteers WHERE team_lead = 'TL001'
  );

Query 5: Volunteer vNPS Score
sqlSELECT 
    v.volunteer_id,
    CONCAT(v.first_name, ' ', v.last_name) AS volunteer_name,
    vn.vnps_score,
    vn.vnps_category,
    vn.what_working_well,
    vn.what_could_improve
FROM volunteers v
LEFT JOIN vnps_surveys vn ON v.volunteer_id = vn.volunteer_id
WHERE vn.quarter = 'Q1' AND vn.year = 2025
  AND v.team_lead = 'TL001'
ORDER BY vn.vnps_score DESC;

🔒 INDEXES FOR PERFORMANCE
sql-- Primary lookups
CREATE INDEX idx_people_status ON people(follow_up_status);
CREATE INDEX idx_people_assigned ON people(assigned_volunteer);
CREATE INDEX idx_volunteers_status ON volunteers(status);
CREATE INDEX idx_volunteers_team_lead ON volunteers(team_lead);
CREATE INDEX idx_follow_ups_volunteer ON follow_ups(volunteer_id);
CREATE INDEX idx_follow_ups_person ON follow_ups(person_id);
CREATE INDEX idx_follow_ups_week ON follow_ups(week_number);
CREATE INDEX idx_follow_ups_date ON follow_ups(attempt_date);
CREATE INDEX idx_escalations_status ON escalations(status);
CREATE INDEX idx_check_ins_volunteer ON check_ins(volunteer_id);
CREATE INDEX idx_check_ins_date ON check_ins(check_in_date);

-- Composite indexes for common queries
CREATE INDEX idx_follow_ups_vol_week ON follow_ups(volunteer_id, week_number);
CREATE INDEX idx_volunteers_team_status ON volunteers(team_lead, status);

📝 VIEWS FOR COMMON QUERIES
View 1: Active Volunteers Dashboard
sqlCREATE VIEW vw_active_volunteers_dashboard AS
SELECT 
    v.volunteer_id,
    v.first_name,
    v.last_name,
    v.capacity_band,
    v.capacity_min,
    v.capacity_max,
    v.current_assignments,
    v.completion_rate,
    v.vnps_score,
    v.emotional_tone,
    v.last_check_in,
    v.next_check_in,
    v.team_lead,
    tl.first_name AS team_lead_first_name,
    tl.last_name AS team_lead_last_name
FROM volunteers v
LEFT JOIN team_leads tl ON v.team_lead = tl.team_lead_id
WHERE v.status = 'Active';

View 2: Follow-Up Pipeline
sqlCREATE VIEW vw_follow_up_pipeline AS
SELECT 
    p.person_id,
    CONCAT(p.first_name, ' ', p.last_name) AS person_name,
    p.email,
    p.phone,
    p.follow_up_status,
    p.follow_up_priority,
    p.assigned_volunteer,
    CONCAT(v.first_name, ' ', v.last_name) AS volunteer_name,
    p.assigned_date,
    p.last_contact_date,
    p.next_action_date,
    DATEDIFF(CURRENT_DATE, p.assigned_date) AS days_since_assigned,
    DATEDIFF(p.next_action_date, CURRENT_DATE) AS days_until_next_action
FROM people p
LEFT JOIN volunteers v ON p.assigned_volunteer = v.volunteer_id
WHERE p.follow_up_status IN ('New', 'Assigned', 'Retry Pending')
ORDER BY p.follow_up_priority DESC, p.next_action_date;

View 3: Escalations Pending
sqlCREATE VIEW vw_escalations_pending AS
SELECT 
    e.escalation_id,
    e.escalation_date,
    e.escalation_tier,
    e.escalation_reason,
    CONCAT(p.first_name, ' ', p.last_name) AS person_name,
    CONCAT(v.first_name, ' ', v.last_name) AS volunteer_name,
    e.status,
    e.assigned_to,
    DATEDIFF(CURRENT_DATE, e.escalation_date) AS days_pending
FROM escalations e
JOIN people p ON e.person_id = p.person_id
JOIN volunteers v ON e.volunteer_id = v.volunteer_id
WHERE e.status IN ('New', 'In Progress')
ORDER BY e.escalation_tier DESC, e.escalation_date;
```

---

## ✅ IMPLEMENTATION CHECKLIST

### **Phase 1: Core Tables** (Week 1)
```
□ Create PEOPLE table
□ Create VOLUNTEERS table
□ Create TEAM_LEADS table
□ Create FOLLOW_UPS table
□ Test basic data entry
```

### **Phase 2: Support Tables** (Week 2)
```
□ Create CHECK_INS table
□ Create ESCALATIONS table
□ Create NOTES table
□ Create VNPS_SURVEYS table
□ Create CAPACITY_HISTORY table
□ Create SYSTEM_CONFIG table
```

### **Phase 3: Indexes & Views** (Week 3)
```
□ Add all indexes
□ Create dashboard views
□ Test query performance
```

### **Phase 4: Sample Data** (Week 3)
```
□ Load sample volunteers
□ Load sample people
□ Load sample follow-ups
□ Test dashboard queries
```

### **Phase 5: Integration** (Week 4)
```
□ Connect Google Forms to database
□ Build dashboard (Google Sheets or web)
□ Test end-to-end workflow
□ Train Team Leads

🚀 READY TO IMPLEMENT
You now have:

✅ 10 complete database tables
✅ All field definitions
✅ Enumerated values
✅ Sample data
✅ Relationships mapped
✅ Key queries for dashboard
✅ Indexes for performance
✅ Views for common reports