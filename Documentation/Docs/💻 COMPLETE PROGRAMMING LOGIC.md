💻 COMPLETE PROGRAMMING LOGIC FOR LEVEL 1 FOLLOW-UP SYSTEM
From Database to Working Application - Step-by-Step Logic

🎯 SYSTEM OVERVIEW
What We're Building
A follow-up management system with these core workflows:
1. VISITOR ENTRY → Person added to system
2. ASSIGNMENT → Volunteer gets assigned
3. CONTACT ATTEMPT → Volunteer calls/texts
4. OUTCOME LOGGING → Form completed
5. AUTO-ROUTING → System decides next action
6. DASHBOARD UPDATE → Metrics refresh
7. ALERTS → Team Lead notified if needed

🧠 CORE PROGRAMMING CONCEPTS
The State Machine Model
Think of each contact as moving through states:
┌─────────┐    ┌──────────┐    ┌───────────┐    ┌──────────┐
│   NEW   │───►│ ASSIGNED │───►│ CONTACTED │───►│ COMPLETE │
└─────────┘    └──────────┘    └───────────┘    └──────────┘
                                      │
                                      ├──►┌─────────────┐
                                      │   │   RETRY     │
                                      │   │  PENDING    │
                                      │   └─────────────┘
                                      │
                                      └──►┌─────────────┐
                                          │ ESCALATED   │
                                          └─────────────┘
Programming Principle: Each state has:

Entry conditions (how do you get here?)
Actions (what happens in this state?)
Exit conditions (where do you go next?)


📚 SECTION 1: VISITOR ENTRY LOGIC
Flow: Person fills connection card → Gets added to system

Pseudocode:
FUNCTION addNewVisitor(connectionCardData):
    // 1. Validate input
    IF missing required fields (name, contact method) THEN
        RETURN error "Missing required information"
    END IF
    
    // 2. Check for duplicates
    existing_person = findPersonByEmailOrPhone(connectionCardData.email, connectionCardData.phone)
    
    IF existing_person EXISTS THEN
        // Update existing record instead of creating new
        UPDATE existing_person SET
            last_visit_date = TODAY
            visit_count = visit_count + 1
            visit_type = "Returning Visitor"
        RETURN existing_person
    END IF
    
    // 3. Create new person record
    new_person = CREATE Person WITH
        person_id = generatePersonID()
        first_name = connectionCardData.first_name
        last_name = connectionCardData.last_name
        email = connectionCardData.email
        phone = connectionCardData.phone
        visit_type = "First-Time Visitor"
        first_visit_date = TODAY
        last_visit_date = TODAY
        visit_count = 1
        follow_up_status = "NEW"
        follow_up_priority = determinePriority(connectionCardData)
        created_at = NOW()
    
    // 4. Save to database
    database.save(new_person)
    
    // 5. Trigger assignment workflow
    assignToVolunteer(new_person)
    
    RETURN new_person
END FUNCTION

Real Code (JavaScript/Node.js):
javascriptasync function addNewVisitor(connectionCardData) {
    try {
        // 1. Validate input
        if (!connectionCardData.first_name || !connectionCardData.last_name) {
            throw new Error("Missing required fields: first_name and last_name");
        }
        
        if (!connectionCardData.email && !connectionCardData.phone) {
            throw new Error("Must provide at least email or phone");
        }
        
        // 2. Check for duplicates
        const existingPerson = await db.query(
            `SELECT * FROM people 
             WHERE email = ? OR phone = ?`,
            [connectionCardData.email, connectionCardData.phone]
        );
        
        if (existingPerson.length > 0) {
            // Update existing record
            await db.query(
                `UPDATE people SET 
                    last_visit_date = CURDATE(),
                    visit_count = visit_count + 1,
                    visit_type = 'Returning Visitor',
                    follow_up_status = 'NEW'
                 WHERE person_id = ?`,
                [existingPerson[0].person_id]
            );
            
            // Re-trigger assignment
            await assignToVolunteer(existingPerson[0].person_id);
            
            return existingPerson[0];
        }
        
        // 3. Generate new person ID
        const personId = await generatePersonID();
        
        // 4. Determine priority
        const priority = determinePriority(connectionCardData);
        
        // 5. Insert new person
        await db.query(
            `INSERT INTO people (
                person_id, first_name, last_name, email, phone,
                visit_type, first_visit_date, last_visit_date, visit_count,
                follow_up_status, follow_up_priority, created_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NOW())`,
            [
                personId,
                connectionCardData.first_name,
                connectionCardData.last_name,
                connectionCardData.email,
                connectionCardData.phone,
                'First-Time Visitor',
                new Date(),
                new Date(),
                1,
                'NEW',
                priority
            ]
        );
        
        // 6. Trigger assignment
        await assignToVolunteer(personId);
        
        return { person_id: personId, status: 'created' };
        
    } catch (error) {
        console.error('Error adding visitor:', error);
        throw error;
    }
}

// Helper: Generate unique person ID
async function generatePersonID() {
    const year = new Date().getFullYear();
    const result = await db.query(
        `SELECT MAX(CAST(SUBSTRING(person_id, 2) AS UNSIGNED)) as max_id 
         FROM people 
         WHERE person_id LIKE 'P${year}%'`
    );
    
    const nextNum = (result[0].max_id || 0) + 1;
    return `P${year}${String(nextNum).padStart(4, '0')}`;
}

// Helper: Determine follow-up priority
function determinePriority(data) {
    // High priority if:
    // - Specific need mentioned
    // - Prayer request
    // - Interested in counseling
    
    if (data.specific_needs || data.prayer_requests) {
        return 'High';
    }
    
    if (data.interested_in && data.interested_in.includes('Counseling')) {
        return 'Urgent';
    }
    
    return 'Normal';
}
```

---

## 📚 SECTION 2: ASSIGNMENT LOGIC

### **Flow**: New person → Find available volunteer → Assign

---

### **Assignment Algorithm**:
```
FUNCTION assignToVolunteer(person_id):
    // 1. Get person details
    person = database.getPerson(person_id)
    
    // 2. Get available volunteers (capacity check)
    available_volunteers = database.query(
        SELECT volunteer_id, capacity_max, current_assignments
        FROM volunteers
        WHERE status = 'Active'
          AND current_assignments < capacity_max
          AND campus = person.campus (if multi-campus)
        ORDER BY current_assignments ASC, RANDOM()
    )
    
    IF available_volunteers is EMPTY THEN
        // No capacity - alert Team Lead
        alertTeamLead("No available volunteers for assignment")
        SET person.follow_up_status = "NEW"
        RETURN false
    END IF
    
    // 3. Select volunteer (least loaded first, then random for fairness)
    selected_volunteer = available_volunteers[0]
    
    // 4. Create assignment
    UPDATE people SET
        assigned_volunteer = selected_volunteer.volunteer_id
        assigned_date = TODAY
        follow_up_status = "ASSIGNED"
        next_action_date = TODAY + 2 days (48-hour target)
    WHERE person_id = person_id
    
    // 5. Update volunteer workload
    UPDATE volunteers SET
        current_assignments = current_assignments + 1
    WHERE volunteer_id = selected_volunteer.volunteer_id
    
    // 6. Notify volunteer
    notifyVolunteer(selected_volunteer, person)
    
    RETURN true
END FUNCTION

Real Code (JavaScript):
javascriptasync function assignToVolunteer(personId) {
    try {
        // 1. Get person details
        const person = await db.query(
            'SELECT * FROM people WHERE person_id = ?',
            [personId]
        );
        
        if (person.length === 0) {
            throw new Error('Person not found');
        }
        
        // 2. Find available volunteers
        const availableVolunteers = await db.query(
            `SELECT v.volunteer_id, v.first_name, v.last_name, 
                    v.email, v.capacity_max, v.current_assignments,
                    (v.capacity_max - v.current_assignments) as available_capacity
             FROM volunteers v
             WHERE v.status = 'Active'
               AND v.current_assignments < v.capacity_max
               AND v.campus = ?
             ORDER BY v.current_assignments ASC, RAND()
             LIMIT 1`,
            [person[0].campus]
        );
        
        if (availableVolunteers.length === 0) {
            // No capacity available
            await alertTeamLead({
                type: 'NO_CAPACITY',
                message: 'No available volunteers for new assignment',
                person_id: personId
            });
            return false;
        }
        
        const selectedVolunteer = availableVolunteers[0];
        
        // 3. Calculate next action date (48 hours from now)
        const nextActionDate = new Date();
        nextActionDate.setHours(nextActionDate.getHours() + 48);
        
        // 4. Create assignment (use transaction for data integrity)
        await db.transaction(async (trx) => {
            // Update person record
            await trx.query(
                `UPDATE people SET
                    assigned_volunteer = ?,
                    assigned_date = NOW(),
                    follow_up_status = 'ASSIGNED',
                    next_action_date = ?
                 WHERE person_id = ?`,
                [selectedVolunteer.volunteer_id, nextActionDate, personId]
            );
            
            // Update volunteer workload
            await trx.query(
                `UPDATE volunteers SET
                    current_assignments = current_assignments + 1,
                    updated_at = NOW()
                 WHERE volunteer_id = ?`,
                [selectedVolunteer.volunteer_id]
            );
        });
        
        // 5. Send notification to volunteer
        await notifyVolunteer(selectedVolunteer, person[0]);
        
        return true;
        
    } catch (error) {
        console.error('Error in assignment:', error);
        throw error;
    }
}

// Notification function
async function notifyVolunteer(volunteer, person) {
    const message = {
        to: volunteer.email,
        subject: 'New Follow-Up Assignment',
        body: `
            Hi ${volunteer.first_name},
            
            You have a new follow-up assignment:
            
            Name: ${person.first_name} ${person.last_name}
            Phone: ${person.phone}
            Email: ${person.email}
            Visit Date: ${person.first_visit_date}
            
            Please contact them within 48 hours.
            
            Access your dashboard: [link]
        `
    };
    
    // Send email (using your email service)
    await emailService.send(message);
    
    // Optional: Send SMS
    if (volunteer.phone) {
        await smsService.send({
            to: volunteer.phone,
            body: `New follow-up assignment: ${person.first_name} ${person.last_name}. Check your email for details.`
        });
    }
}
```

---

## 📚 SECTION 3: CONTACT ATTEMPT LOGGING

### **Flow**: Volunteer completes call → Fills form → System processes

---

### **Form Submission Logic**:
```
FUNCTION logFollowUpAttempt(formData):
    // 1. Validate form data
    VALIDATE formData HAS REQUIRED FIELDS:
        - volunteer_id
        - person_id
        - contact_status (Contacted / Not Contacted)
        - IF Contacted: response_type required
    
    // 2. Get current attempt number
    previous_attempts = COUNT follow_ups WHERE person_id = formData.person_id
    attempt_number = previous_attempts + 1
    
    // 3. Create follow-up record
    follow_up_id = generateFollowUpID()
    
    INSERT INTO follow_ups (
        follow_up_id, person_id, volunteer_id,
        attempt_number, attempt_date, contact_method,
        contact_status, response_type, call_duration_min,
        notes, week_number, created_at
    ) VALUES (
        follow_up_id,
        formData.person_id,
        formData.volunteer_id,
        attempt_number,
        TODAY,
        formData.contact_method,
        formData.contact_status,
        formData.response_type,
        formData.call_duration_min,
        formData.notes,
        CURRENT_WEEK_NUMBER,
        NOW()
    )
    
    // 4. Route based on outcome
    IF formData.contact_status == "Contacted" THEN
        routeByResponseType(formData, follow_up_id)
    ELSE
        handleNoContact(formData, follow_up_id)
    END IF
    
    // 5. Update volunteer stats
    updateVolunteerStats(formData.volunteer_id)
    
    // 6. Update dashboard
    refreshDashboard(formData.volunteer_id)
    
    RETURN success
END FUNCTION

Real Code (JavaScript):
javascriptasync function logFollowUpAttempt(formData) {
    try {
        // 1. Validate form data
        if (!formData.volunteer_id || !formData.person_id || !formData.contact_status) {
            throw new Error('Missing required fields');
        }
        
        if (formData.contact_status === 'Contacted' && !formData.response_type) {
            throw new Error('response_type required when contact_status is Contacted');
        }
        
        // 2. Get attempt number
        const previousAttempts = await db.query(
            'SELECT COUNT(*) as count FROM follow_ups WHERE person_id = ?',
            [formData.person_id]
        );
        
        const attemptNumber = previousAttempts[0].count + 1;
        
        // 3. Generate follow-up ID
        const followUpId = await generateFollowUpID();
        
        // 4. Get current week number
        const weekNumber = getISOWeekNumber(new Date());
        
        // 5. Insert follow-up record
        await db.query(
            `INSERT INTO follow_ups (
                follow_up_id, person_id, volunteer_id, team_lead_id,
                attempt_number, attempt_date, attempt_time, contact_method,
                contact_status, response_type, call_duration_min,
                notes, tags, week_number, month_number, quarter_number, year,
                created_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NOW())`,
            [
                followUpId,
                formData.person_id,
                formData.volunteer_id,
                formData.team_lead_id,
                attemptNumber,
                new Date(),
                new Date().toTimeString().slice(0, 8),
                formData.contact_method || 'Phone Call',
                formData.contact_status,
                formData.response_type,
                formData.call_duration_min,
                formData.notes,
                formData.tags,
                weekNumber,
                new Date().getMonth() + 1,
                Math.floor((new Date().getMonth() / 3) + 1),
                new Date().getFullYear()
            ]
        );
        
        // 6. Route based on outcome
        if (formData.contact_status === 'Contacted') {
            await routeByResponseType(formData, followUpId);
        } else {
            await handleNoContact(formData, followUpId);
        }
        
        // 7. Update volunteer stats
        await updateVolunteerStats(formData.volunteer_id);
        
        // 8. Update person's last contact date
        await db.query(
            'UPDATE people SET last_contact_date = NOW() WHERE person_id = ?',
            [formData.person_id]
        );
        
        return { success: true, follow_up_id: followUpId };
        
    } catch (error) {
        console.error('Error logging follow-up:', error);
        throw error;
    }
}

// Helper: Get ISO week number
function getISOWeekNumber(date) {
    const d = new Date(date);
    d.setHours(0, 0, 0, 0);
    d.setDate(d.getDate() + 4 - (d.getDay() || 7));
    const yearStart = new Date(d.getFullYear(), 0, 1);
    return Math.ceil((((d - yearStart) / 86400000) + 1) / 7);
}
```

---

## 📚 SECTION 4: AUTO-ROUTING LOGIC (THE CORE)

### **The Decision Tree**:
```
             [Contact Made?]
                   |
        ┌──────────┴──────────┐
       YES                   NO
        |                     |
   [Response Type]      [Attempt #?]
        |                     |
    ┌───┴───┬───────┬────┐   ├──── 1 or 2 → Retry in 3 days
    │       │       │    │   └──── 3 → Mark Unresponsive
 Normal  Needs  Crisis │
    │    Follow-Up │  No Response
    │       │      │      │
Complete Escalate Crisis  Retry
         to TL   Protocol

Routing Function:
javascriptasync function routeByResponseType(formData, followUpId) {
    const responseType = formData.response_type;
    
    switch (responseType) {
        case 'Normal':
            await handleNormalResponse(formData, followUpId);
            break;
            
        case 'Needs Follow-Up':
            await handleNeedsFollowUp(formData, followUpId);
            break;
            
        case 'Crisis':
            await handleCrisisResponse(formData, followUpId);
            break;
            
        case 'No Response':
            await handleNoResponse(formData, followUpId);
            break;
            
        default:
            throw new Error('Unknown response type: ' + responseType);
    }
}

// ROUTE 1: Normal Response → Mark Complete
async function handleNormalResponse(formData, followUpId) {
    try {
        await db.transaction(async (trx) => {
            // 1. Update follow-up record
            await trx.query(
                `UPDATE follow_ups SET
                    next_action = 'Mark Complete',
                    next_action_date = NULL
                 WHERE follow_up_id = ?`,
                [followUpId]
            );
            
            // 2. Update person status
            await trx.query(
                `UPDATE people SET
                    follow_up_status = 'COMPLETE',
                    next_action_date = NULL
                 WHERE person_id = ?`,
                [formData.person_id]
            );
            
            // 3. Decrement volunteer's current assignments
            await trx.query(
                `UPDATE volunteers SET
                    current_assignments = current_assignments - 1,
                    total_completed = total_completed + 1
                 WHERE volunteer_id = ?`,
                [formData.volunteer_id]
            );
        });
        
        console.log(`✅ Normal response: ${formData.person_id} marked complete`);
        
    } catch (error) {
        console.error('Error handling normal response:', error);
        throw error;
    }
}

// ROUTE 2: Needs Follow-Up → Escalate to Team Lead
async function handleNeedsFollowUp(formData, followUpId) {
    try {
        await db.transaction(async (trx) => {
            // 1. Update follow-up record
            await trx.query(
                `UPDATE follow_ups SET
                    next_action = 'Escalate to Team Lead',
                    escalation_tier = 'Standard'
                 WHERE follow_up_id = ?`,
                [followUpId]
            );
            
            // 2. Update person status
            await trx.query(
                `UPDATE people SET
                    follow_up_status = 'ESCALATED',
                    follow_up_priority = 'High'
                 WHERE person_id = ?`,
                [formData.person_id]
            );
            
            // 3. Create escalation record
            const escalationId = await generateEscalationID();
            await trx.query(
                `INSERT INTO escalations (
                    escalation_id, follow_up_id, person_id, volunteer_id, team_lead_id,
                    escalation_date, escalation_tier, escalation_reason,
                    description, status, created_at
                ) VALUES (?, ?, ?, ?, ?, NOW(), 'Standard', 'Needs Follow-Up', ?, 'New', NOW())`,
                [
                    escalationId,
                    followUpId,
                    formData.person_id,
                    formData.volunteer_id,
                    formData.team_lead_id,
                    formData.notes || 'Volunteer indicated person needs additional follow-up'
                ]
            );
            
            // 4. Decrement volunteer's current assignments (no longer their responsibility)
            await trx.query(
                `UPDATE volunteers SET
                    current_assignments = current_assignments - 1
                 WHERE volunteer_id = ?`,
                [formData.volunteer_id]
            );
        });
        
        // 5. Alert Team Lead
        await alertTeamLead({
            type: 'NEEDS_FOLLOW_UP',
            person_id: formData.person_id,
            volunteer_id: formData.volunteer_id,
            notes: formData.notes
        });
        
        console.log(`🟡 Needs follow-up: ${formData.person_id} escalated to Team Lead`);
        
    } catch (error) {
        console.error('Error handling needs follow-up:', error);
        throw error;
    }
}

// ROUTE 3: Crisis → Immediate Escalation + Protocol
async function handleCrisisResponse(formData, followUpId) {
    try {
        await db.transaction(async (trx) => {
            // 1. Update follow-up record
            await trx.query(
                `UPDATE follow_ups SET
                    next_action = 'Crisis Protocol',
                    escalation_tier = 'Emergency'
                 WHERE follow_up_id = ?`,
                [followUpId]
            );
            
            // 2. Update person status
            await trx.query(
                `UPDATE people SET
                    follow_up_status = 'ESCALATED',
                    follow_up_priority = 'Urgent'
                 WHERE person_id = ?`,
                [formData.person_id]
            );
            
            // 3. Create URGENT escalation record
            const escalationId = await generateEscalationID();
            await trx.query(
                `INSERT INTO escalations (
                    escalation_id, follow_up_id, person_id, volunteer_id, team_lead_id,
                    escalation_date, escalation_tier, escalation_reason,
                    description, status, created_at
                ) VALUES (?, ?, ?, ?, ?, NOW(), 'Emergency', 'Crisis', ?, 'New', NOW())`,
                [
                    escalationId,
                    followUpId,
                    formData.person_id,
                    formData.volunteer_id,
                    formData.team_lead_id,
                    '🚨 CRISIS: ' + (formData.notes || 'Immediate attention required')
                ]
            );
        });
        
        // 4. IMMEDIATE ALERTS (Email + SMS)
        await alertTeamLead({
            type: 'CRISIS',
            urgency: 'IMMEDIATE',
            person_id: formData.person_id,
            volunteer_id: formData.volunteer_id,
            notes: formData.notes
        });
        
        await alertSeniorPastor({
            type: 'CRISIS',
            person_id: formData.person_id,
            volunteer_id: formData.volunteer_id,
            notes: formData.notes
        });
        
        console.log(`🚨 CRISIS: ${formData.person_id} - IMMEDIATE ESCALATION SENT`);
        
    } catch (error) {
        console.error('Error handling crisis:', error);
        throw error;
    }
}

// ROUTE 4: No Response → Retry Logic
async function handleNoResponse(formData, followUpId) {
    try {
        // Get attempt number
        const attempts = await db.query(
            'SELECT COUNT(*) as count FROM follow_ups WHERE person_id = ?',
            [formData.person_id]
        );
        
        const attemptNumber = attempts[0].count;
        
        if (attemptNumber < 3) {
            // Retry in 3 days
            const retryDate = new Date();
            retryDate.setDate(retryDate.getDate() + 3);
            
            await db.transaction(async (trx) => {
                // 1. Update follow-up record
                await trx.query(
                    `UPDATE follow_ups SET
                        next_action = 'Retry in 3 Days',
                        next_action_date = ?
                     WHERE follow_up_id = ?`,
                    [retryDate, followUpId]
                );
                
                // 2. Update person status
                await trx.query(
                    `UPDATE people SET
                        follow_up_status = 'RETRY PENDING',
                        next_action_date = ?
                     WHERE person_id = ?`,
                    [retryDate, formData.person_id]
                );
            });
            
            // 3. Schedule reminder for volunteer
            await scheduleRetryReminder(formData.volunteer_id, formData.person_id, retryDate);
            
            console.log(`📞 No response (attempt ${attemptNumber}/3): Retry scheduled for ${retryDate}`);
            
        } else {
            // Max attempts reached → Mark Unresponsive
            await db.transaction(async (trx) => {
                await trx.query(
                    `UPDATE follow_ups SET
                        next_action = 'Mark Unresponsive',
                        next_action_date = NULL
                     WHERE follow_up_id = ?`,
                    [followUpId]
                );
                
                await trx.query(
                    `UPDATE people SET
                        follow_up_status = 'UNRESPONSIVE',
                        next_action_date = NULL
                     WHERE person_id = ?`,
                    [formData.person_id]
                );
                
                // Decrement volunteer assignment
                await trx.query(
                    `UPDATE volunteers SET
                        current_assignments = current_assignments - 1
                     WHERE volunteer_id = ?`,
                    [formData.volunteer_id]
                );
            });
            
            console.log(`📭 Unresponsive: ${formData.person_id} marked unresponsive after 3 attempts`);
        }
        
    } catch (error) {
        console.error('Error handling no response:', error);
        throw error;
    }
}

📚 SECTION 5: DASHBOARD CALCULATION LOGIC
Real-Time Metrics Calculation:
javascript// Calculate Team Health Metrics
async function calculateTeamHealthMetrics(teamLeadId) {
    try {
        const metrics = {};
        
        // 1. Get all active volunteers for this Team Lead
        const volunteers = await db.query(
            `SELECT volunteer_id, first_name, last_name, capacity_band, 
                    capacity_min, capacity_max
             FROM volunteers
             WHERE team_lead = ? AND status = 'Active'`,
            [teamLeadId]
        );
        
        // 2. For each volunteer, calculate this week & last week
        const currentWeek = getISOWeekNumber(new Date());
        const lastWeek = currentWeek - 1;
        
        metrics.volunteers = await Promise.all(volunteers.map(async (v) => {
            // This week completed
            const thisWeekResult = await db.query(
                `SELECT COUNT(*) as count
                 FROM follow_ups
                 WHERE volunteer_id = ?
                   AND week_number = ?
                   AND contact_status = 'Contacted'`,
                [v.volunteer_id, currentWeek]
            );
            
            const thisWeekCompleted = thisWeekResult[0].count;
            
            // Last week completed
            const lastWeekResult = await db.query(
                `SELECT COUNT(*) as count
                 FROM follow_ups
                 WHERE volunteer_id = ?
                   AND week_number = ?
                   AND contact_status = 'Contacted'`,
                [v.volunteer_id, lastWeek]
            );
            
            const lastWeekCompleted = lastWeekResult[0].count;
            
            // Calculate trend
            let trend = '➡️';
            if (thisWeekCompleted > lastWeekCompleted) trend = '⬆️';
            if (thisWeekCompleted < lastWeekCompleted) trend = '⬇️';
            
            // Calculate flag
            const completionRate = (thisWeekCompleted / v.capacity_max) * 100;
            let flag = '🔴';
            if (completionRate >= 90) flag = '🟢';
            else if (completionRate >= 75) flag = '🟡';
            
            return {
                volunteer_id: v.volunteer_id,
                name: `${v.first_name} ${v.last_name}`,
                capacity_band: v.capacity_band,
                this_week: `${thisWeekCompleted}/${v.capacity_max}`,
                last_week: lastWeekCompleted,
                trend: trend,
                flag: flag,
                completion_rate: completionRate
            };
        }));
        
        // 3. Team Performance Summary
        const teamPerformance = await db.query(
            `SELECT 
                COUNT(*) as total_follow_ups,
                SUM(CASE WHEN contact_status = 'Contacted' THEN 1 ELSE 0 END) as completed,
                SUM(CASE WHEN response_type = 'Normal' THEN 1 ELSE 0 END) as normal,
                SUM(CASE WHEN response_type = 'Needs Follow-Up' THEN 1 ELSE 0 END) as needs_follow_up,
                SUM(CASE WHEN response_type = 'Crisis' THEN 1 ELSE 0 END) as crisis,
                AVG(call_duration_min) as avg_duration
             FROM follow_ups
             WHERE week_number = ?
               AND volunteer_id IN (
                   SELECT volunteer_id FROM volunteers WHERE team_lead = ?
               )`,
            [currentWeek, teamLeadId]
        );
        
        metrics.team_performance = teamPerformance[0];
        
        // 4. Attention Needed (auto-generate alerts)
        metrics.attention_needed = [];
        
        metrics.volunteers.forEach(v => {
            if (v.flag === '🔴' && v.trend === '⬇️') {
                metrics.attention_needed.push({
                    volunteer: v.name,
                    message: '2 weeks declining performance',
                    priority: 'URGENT'
                });
            } else if (v.flag === '🟡' && v.capacity_band === 'Consistent') {
                metrics.attention_needed.push({
                    volunteer: v.name,
                    message: 'Check capacity (might need reduction)',
                    priority: 'IMPORTANT'
                });
            }
        });
        
        // 5. Upcoming Check-Ins
        const upcomingCheckIns = await db.query(
            `SELECT v.first_name, v.last_name, v.next_check_in,
                    DAYNAME(v.next_check_in) as day_of_week
             FROM volunteers v
             WHERE v.team_lead = ?
               AND v.next_check_in BETWEEN CURDATE() AND DATE_ADD(CURDATE(), INTERVAL 7 DAY)
             ORDER BY v.next_check_in`,
            [teamLeadId]
        );
        
        metrics.upcoming_check_ins = upcomingCheckIns;
        
        return metrics;
        
    } catch (error) {
        console.error('Error calculating metrics:', error);
        throw error;
    }
}

📚 SECTION 6: AUTOMATED WORKFLOWS (Cron Jobs)
Background Jobs That Run Automatically:
javascript// JOB 1: Daily Check for Overdue Follow-Ups
async function checkOverdueFollowUps() {
    try {
        console.log('[CRON] Checking for overdue follow-ups...');
        
        // Find people with next_action_date in the past
        const overdueContacts = await db.query(
            `SELECT p.person_id, p.first_name, p.last_name,
                    p.assigned_volunteer, p.next_action_date,
                    v.first_name as volunteer_first_name,
                    v.last_name as volunteer_last_name,
                    v.email as volunteer_email,
                    tl.email as team_lead_email
             FROM people p
             JOIN volunteers v ON p.assigned_volunteer = v.volunteer_id
             JOIN team_leads tl ON v.team_lead = tl.team_lead_id
             WHERE p.follow_up_status IN ('ASSIGNED', 'RETRY PENDING')
               AND p.next_action_date < CURDATE()
               AND p.next_action_date IS NOT NULL`
        );
        
        console.log(`Found ${overdueContacts.length} overdue follow-ups`);
        
        // Send reminders
        for (const contact of overdueContacts) {
            // Reminder to volunteer
            await emailService.send({
                to: contact.volunteer_email,
                subject: 'Reminder: Follow-Up Overdue',
                body: `
                    Hi ${contact.volunteer_first_name},
                    
                    This is a reminder that you have an overdue follow-up:
                    
                    Name: ${contact.first_name} ${contact.last_name}
                    Due Date: ${contact.next_action_date}
                    
                    Please complete this follow-up as soon as possible.
                    
                    Access your dashboard: [link]
                `
            });
            
            // Alert Team Lead if >48 hours overdue
            const hoursSinceOverdue = (new Date() - new Date(contact.next_action_date)) / (1000 * 60 * 60);
            
            if (hoursSinceOverdue > 48) {
                await emailService.send({
                    to: contact.team_lead_email,
                    subject: '🚨 Follow-Up Significantly Overdue',
                    body: `
                        Volunteer ${contact.volunteer_first_name} ${contact.volunteer_last_name} 
                        has a follow-up that is ${Math.floor(hoursSinceOverdue)} hours overdue.
                        
                        Contact: ${contact.first_name} ${contact.last_name}
                        Due Date: ${contact.next_action_date}
                        
                        Please check in with the volunteer.
                    `
                });
            }
        }
        
    } catch (error) {
        console.error('[CRON ERROR] checkOverdueFollowUps:', error);
    }
}

// Schedule: Run daily at 9am
// cron.schedule('0 9 * * *', checkOverdueFollowUps);


// JOB 2: Daily Retry Reminders
async function sendRetryReminders() {
    try {
        console.log('[CRON] Sending retry reminders...');
        
        // Find people with retry due today
        const retryDueToday = await db.query(
            `SELECT p.person_id, p.first_name, p.last_name, p.phone, p.email,
                    v.volunteer_id, v.first_name as volunteer_first_name,
                    v.email as volunteer_email,
                    (SELECT COUNT(*) FROM follow_ups WHERE person_id = p.person_id) as attempt_number
             FROM people p
             JOIN volunteers v ON p.assigned_volunteer = v.volunteer_id
             WHERE p.follow_up_status = 'RETRY PENDING'
               AND DATE(p.next_action_date) = CURDATE()`
        );
        
        console.log(`Found ${retryDueToday.length} retries due today`);
        
        for (const contact of retryDueToday) {
            await emailService.send({
                to: contact.volunteer_email,
                subject: 'Follow-Up Retry Due Today',
                body: `
                    Hi ${contact.volunteer_first_name},
                    
                    Retry reminder for:
                    
                    Name: ${contact.first_name} ${contact.last_name}
                    Phone: ${contact.phone}
                    Attempt: ${contact.attempt_number + 1} of 3
                    
                    Please attempt contact today.
                `
            });
        }
        
    } catch (error) {
        console.error('[CRON ERROR] sendRetryReminders:', error);
    }
}

// Schedule: Run daily at 8am
// cron.schedule('0 8 * * *', sendRetryReminders);


// JOB 3: Weekly Dashboard Refresh
async function weeklyDashboardRefresh() {
    try {
        console.log('[CRON] Running weekly dashboard refresh...');
        
        // Get all Team Leads
        const teamLeads = await db.query('SELECT team_lead_id, email FROM team_leads');
        
        for (const tl of teamLeads) {
            // Calculate metrics
            const metrics = await calculateTeamHealthMetrics(tl.team_lead_id);
            
            // Generate email report
            const report = generateWeeklyReport(metrics);
            
            // Send to Team Lead
            await emailService.send({
                to: tl.email,
                subject: 'Weekly Follow-Up Dashboard Summary',
                body: report
            });
        }
        
    } catch (error) {
        console.error('[CRON ERROR] weeklyDashboardRefresh:', error);
    }
}

// Schedule: Run every Monday at 7am
// cron.schedule('0 7 * * 1', weeklyDashboardRefresh);


// JOB 4: Check for Escalations Pending >24 Hours
async function checkStaleEscalations() {
    try {
        console.log('[CRON] Checking for stale escalations...');
        
        const staleEscalations = await db.query(
            `SELECT e.escalation_id, e.escalation_tier,
                    CONCAT(p.first_name, ' ', p.last_name) as person_name,
                    tl.email as team_lead_email,
                    TIMESTAMPDIFF(HOUR, e.escalation_date, NOW()) as hours_pending
             FROM escalations e
             JOIN people p ON e.person_id = p.person_id
             JOIN team_leads tl ON e.team_lead_id = tl.team_lead_id
             WHERE e.status = 'New'
               AND TIMESTAMPDIFF(HOUR, e.escalation_date, NOW()) > 24`
        );
        
        console.log(`Found ${staleEscalations.length} stale escalations`);
        
        for (const esc of staleEscalations) {
            await emailService.send({
                to: esc.team_lead_email,
                subject: '⚠️ Escalation Pending >24 Hours',
                body: `
                    You have an escalation that has been pending for ${esc.hours_pending} hours:
                    
                    Person: ${esc.person_name}
                    Tier: ${esc.escalation_tier}
                    
                    Please take action immediately.
                `
            });
        }
        
    } catch (error) {
        console.error('[CRON ERROR] checkStaleEscalations:', error);
    }
}

// Schedule: Run every 6 hours
// cron.schedule('0 */6 * * *', checkStaleEscalations);

📚 SECTION 7: API ENDPOINTS (REST API Design)
Complete API Specification:
javascriptconst express = require('express');
const router = express.Router();

// ========================================
// PEOPLE ENDPOINTS
// ========================================

// POST /api/people - Add new visitor
router.post('/api/people', async (req, res) => {
    try {
        const result = await addNewVisitor(req.body);
        res.status(201).json(result);
    } catch (error) {
        res.status(400).json({ error: error.message });
    }
});

// GET /api/people/:id - Get person details
router.get('/api/people/:id', async (req, res) => {
    try {
        const person = await db.query(
            'SELECT * FROM people WHERE person_id = ?',
            [req.params.id]
        );
        
        if (person.length === 0) {
            return res.status(404).json({ error: 'Person not found' });
        }
        
        res.json(person[0]);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// GET /api/people - List all people (with filters)
router.get('/api/people', async (req, res) => {
    try {
        const { status, priority, assigned_to, campus } = req.query;
        
        let query = 'SELECT * FROM people WHERE 1=1';
        const params = [];
        
        if (status) {
            query += ' AND follow_up_status = ?';
            params.push(status);
        }
        
        if (priority) {
            query += ' AND follow_up_priority = ?';
            params.push(priority);
        }
        
        if (assigned_to) {
            query += ' AND assigned_volunteer = ?';
            params.push(assigned_to);
        }
        
        if (campus) {
            query += ' AND campus = ?';
            params.push(campus);
        }
        
        query += ' ORDER BY created_at DESC LIMIT 100';
        
        const people = await db.query(query, params);
        res.json(people);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});


// ========================================
// FOLLOW-UPS ENDPOINTS
// ========================================

// POST /api/follow-ups - Log follow-up attempt
router.post('/api/follow-ups', async (req, res) => {
    try {
        const result = await logFollowUpAttempt(req.body);
        res.status(201).json(result);
    } catch (error) {
        res.status(400).json({ error: error.message });
    }
});

// GET /api/follow-ups - List follow-ups (with filters)
router.get('/api/follow-ups', async (req, res) => {
    try {
        const { volunteer_id, person_id, week, status } = req.query;
        
        let query = 'SELECT * FROM follow_ups WHERE 1=1';
        const params = [];
        
        if (volunteer_id) {
            query += ' AND volunteer_id = ?';
            params.push(volunteer_id);
        }
        
        if (person_id) {
            query += ' AND person_id = ?';
            params.push(person_id);
        }
        
        if (week) {
            query += ' AND week_number = ?';
            params.push(week);
        }
        
        if (status) {
            query += ' AND contact_status = ?';
            params.push(status);
        }
        
        query += ' ORDER BY attempt_date DESC LIMIT 100';
        
        const followUps = await db.query(query, params);
        res.json(followUps);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});


// ========================================
// VOLUNTEERS ENDPOINTS
// ========================================

// GET /api/volunteers/:id - Get volunteer details
router.get('/api/volunteers/:id', async (req, res) => {
    try {
        const volunteer = await db.query(
            'SELECT * FROM volunteers WHERE volunteer_id = ?',
            [req.params.id]
        );
        
        if (volunteer.length === 0) {
            return res.status(404).json({ error: 'Volunteer not found' });
        }
        
        res.json(volunteer[0]);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// GET /api/volunteers/:id/assignments - Get volunteer's current assignments
router.get('/api/volunteers/:id/assignments', async (req, res) => {
    try {
        const assignments = await db.query(
            `SELECT p.* FROM people p
             WHERE p.assigned_volunteer = ?
               AND p.follow_up_status IN ('ASSIGNED', 'RETRY PENDING')
             ORDER BY p.next_action_date`,
            [req.params.id]
        );
        
        res.json(assignments);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});


// ========================================
// DASHBOARD ENDPOINTS
// ========================================

// GET /api/dashboard/:team_lead_id - Get dashboard metrics
router.get('/api/dashboard/:team_lead_id', async (req, res) => {
    try {
        const metrics = await calculateTeamHealthMetrics(req.params.team_lead_id);
        res.json(metrics);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});


// ========================================
// ESCALATIONS ENDPOINTS
// ========================================

// GET /api/escalations - List escalations
router.get('/api/escalations', async (req, res) => {
    try {
        const { team_lead_id, status, tier } = req.query;
        
        let query = `
            SELECT e.*, 
                   CONCAT(p.first_name, ' ', p.last_name) as person_name,
                   CONCAT(v.first_name, ' ', v.last_name) as volunteer_name
            FROM escalations e
            JOIN people p ON e.person_id = p.person_id
            JOIN volunteers v ON e.volunteer_id = v.volunteer_id
            WHERE 1=1
        `;
        const params = [];
        
        if (team_lead_id) {
            query += ' AND e.team_lead_id = ?';
            params.push(team_lead_id);
        }
        
        if (status) {
            query += ' AND e.status = ?';
            params.push(status);
        }
        
        if (tier) {
            query += ' AND e.escalation_tier = ?';
            params.push(tier);
        }
        
        query += ' ORDER BY e.escalation_date DESC';
        
        const escalations = await db.query(query, params);
        res.json(escalations);
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

// PATCH /api/escalations/:id - Update escalation status
router.patch('/api/escalations/:id', async (req, res) => {
    try {
        const { status, resolution_notes, outcome } = req.body;
        
        await db.query(
            `UPDATE escalations SET
                status = ?,
                resolution_notes = ?,
                outcome = ?,
                resolved_date = CASE WHEN ? = 'Resolved' THEN NOW() ELSE resolved_date END,
                updated_at = NOW()
             WHERE escalation_id = ?`,
            [status, resolution_notes, outcome, status, req.params.id]
        );
        
        res.json({ success: true });
    } catch (error) {
        res.status(400).json({ error: error.message });
    }
});


// ========================================
// CHECK-INS ENDPOINTS
// ========================================

// POST /api/check-ins - Log check-in
router.post('/api/check-ins', async (req, res) => {
    try {
        const checkInId = await generateCheckInID();
        
        await db.query(
            `INSERT INTO check_ins (
                check_in_id, volunteer_id, team_lead_id,
                check_in_date, duration_min, meeting_type,
                emotional_tone, capacity_adjustment, new_capacity_band,
                concerns_noted, follow_up_needed,
                completion_rate_discussed, boundary_issues,
                training_needs, action_items, next_check_in_date,
                created_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NOW())`,
            [
                checkInId,
                req.body.volunteer_id,
                req.body.team_lead_id,
                req.body.check_in_date || new Date(),
                req.body.duration_min,
                req.body.meeting_type || 'Monthly',
                req.body.emotional_tone,
                req.body.capacity_adjustment || false,
                req.body.new_capacity_band,
                req.body.concerns_noted,
                req.body.follow_up_needed || false,
                req.body.completion_rate_discussed || false,
                req.body.boundary_issues || false,
                req.body.training_needs,
                req.body.action_items,
                req.body.next_check_in_date
            ]
        );
        
        // Update volunteer record
        await db.query(
            `UPDATE volunteers SET
                last_check_in = ?,
                next_check_in = ?,
                emotional_tone = ?
             WHERE volunteer_id = ?`,
            [
                req.body.check_in_date || new Date(),
                req.body.next_check_in_date,
                req.body.emotional_tone,
                req.body.volunteer_id
            ]
        );
        
        res.status(201).json({ check_in_id: checkInId });
    } catch (error) {
        res.status(400).json({ error: error.message });
    }
});

module.exports = router;
```

---

## 🎯 COMPLETE PROGRAM FLOW DIAGRAM
```
┌─────────────────────────────────────────────────────────────┐
│                    SYSTEM INITIALIZATION                     │
└────────────────────────┬────────────────────────────────────┘
                         │
          ┌──────────────┴──────────────┐
          │                             │
    [Load Config]                 [Start Cron Jobs]
          │                             │
          │                             ├─► checkOverdueFollowUps (daily 9am)
          │                             ├─► sendRetryReminders (daily 8am)
          │                             ├─► weeklyDashboardRefresh (Mon 7am)
          │                             └─► checkStaleEscalations (every 6h)
          │
          ▼
┌─────────────────────────────────────────────────────────────┐
│                      EVENT LISTENERS                         │
└─────────────────────────────────────────────────────────────┘
          │
          ├─► POST /api/people (Visitor Entry)
          │        │
          │        ├─► Validate Data
          │        ├─► Check Duplicates
          │        ├─► Create Person Record
          │        └─► Trigger: assignToVolunteer()
          │                 │
          │                 ├─► Find Available Volunteer
          │                 ├─► Update Person (status=ASSIGNED)
          │                 ├─► Update Volunteer (assignments++)
          │                 └─► Send Notification
          │
          ├─► POST /api/follow-ups (Contact Attempt)
          │        │
          │        ├─► Validate Form Data
          │        ├─► Get Attempt Number
          │        ├─► Create Follow-Up Record
          │        └─► Route By Outcome:
          │                 │
          │                 ├─► Normal → handleNormalResponse()
          │                 │            └─► Mark Complete
          │                 │
          │                 ├─► Needs Follow-Up → handleNeedsFollowUp()
          │                 │                     └─► Create Escalation
          │                 │                     └─► Alert Team Lead
          │                 │
          │                 ├─► Crisis → handleCrisisResponse()
          │                 │            └─► URGENT Escalation
          │                 │            └─► Alert TL + Pastor
          │                 │
          │                 └─► No Response → handleNoResponse()
          │                                  │
          │                                  ├─► Attempt <3 → Schedule Retry
          │                                  └─► Attempt=3 → Mark Unresponsive
          │
          └─► GET /api/dashboard/:team_lead_id (Dashboard Request)
                   │
                   └─► calculateTeamHealthMetrics()
                            │
                            ├─► Query This Week Data
                            ├─► Query Last Week Data
                            ├─► Calculate Trends & Flags
                            ├─► Generate Alerts
                            └─► Return JSON
```

---

## ✅ IMPLEMENTATION CHECKLIST

### **Phase 1: Core Functions** (Week 1)
```
□ Write addNewVisitor()
□ Write assignToVolunteer()
□ Write logFollowUpAttempt()
□ Test basic flow: Add → Assign → Log
```

### **Phase 2: Routing Logic** (Week 2)
```
□ Write handleNormalResponse()
□ Write handleNeedsFollowUp()
□ Write handleCrisisResponse()
□ Write handleNoResponse()
□ Test all 4 paths
```

### **Phase 3: Dashboard** (Week 3)
```
□ Write calculateTeamHealthMetrics()
□ Write query functions for each metric
□ Test dashboard API endpoint
```

### **Phase 4: Automation** (Week 4)
```
□ Set up cron jobs
□ Write notification functions
□ Test automated workflows
```

### **Phase 5: API & Integration** (Week 5)
```
□ Build REST API endpoints
□ Connect Google Forms to API
□ Build frontend dashboard
□ End-to-end testing

🚀 YOU NOW UNDERSTAND THE COMPLETE LOGIC
You've learned:

✅ State machine model (NEW → ASSIGNED → CONTACTED → outcomes)
✅ Assignment algorithm (capacity-aware)
✅ Auto-routing decision tree (4 paths based on outcome)
✅ Dashboard calculation (real-time metrics)
✅ Background automation (cron jobs)
✅ API design (RESTful endpoints)