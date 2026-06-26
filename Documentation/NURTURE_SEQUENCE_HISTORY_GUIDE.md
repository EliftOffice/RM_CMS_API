# Nurture Sequence History / Audit View - Implementation Guide

## Overview

A comprehensive history and audit view for nurture sequences that displays all 7 steps of a person's nurture journey, tracking contact attempts, responses, and outcomes.

## Features

### 📊 What's Displayed
- **Sequence Summary Header**
  - Person name and contact info
  - Assigned volunteer
  - Sequence ID
  - Start date
  - Current status (Active / In Progress / Completed)
  - Progress bar showing completion percentage

- **7-Step Timeline**
  - Visual timeline with status indicators
  - Step number and title (e.g., "First Contact Call")
  - Contact method (Call or Visit)
  - Scheduled vs. completed dates
  - Contact status (Contacted / Not Contacted)
  - Response type (Normal / No Response / Needs Follow-Up / Crisis)
  - Notes and observations
  - Current status (Pending / Done / Missed)

- **Audit Trail**
  - Color-coded status indicators
  - Complete timestamp tracking
  - Full notes and response documentation
  - Visual highlighting for crisis situations

- **Action Items**
  - Export to CSV
  - Print/PDF capability
  - Easy navigation

## Files Created

### 1. Frontend HTML
**Location:** `/wwwroot/templates/nurture-sequence-history.html`
- Standalone HTML page for embedded iframe or separate view
- Complete styling and interactivity
- No external dependencies (uses Font Awesome icons)

### 2. JavaScript Module
**Location:** `/wwwroot/js/nurture-sequence-history.js`
- Reusable `NurtureSequenceHistory` class
- Can be embedded in existing pages
- Handles API calls and rendering
- Export and print functionality

### 3. CSS Styles
**Location:** `/wwwroot/css/nurture-sequence-history.css`
- Comprehensive styling for all components
- Responsive design (mobile, tablet, desktop)
- Print-friendly styles
- Dark mode support (optional)
- Accessibility features

## Usage Methods

### Method 1: Embedded HTML (Simple)

Embed the complete HTML file directly in your page:

```html
<iframe 
    src="/templates/nurture-sequence-history.html?sequenceId=NS0001"
    width="100%"
    height="800px"
    style="border: none; border-radius: 8px;">
</iframe>
```

Or open in a modal:

```html
<button onclick="openSequenceHistory('NS0001')">View Sequence History</button>

<script>
function openSequenceHistory(sequenceId) {
    const url = `/templates/nurture-sequence-history.html?sequenceId=${sequenceId}`;
    const width = 1000;
    const height = 800;
    const left = (window.innerWidth - width) / 2;
    const top = (window.innerHeight - height) / 2;
    
    window.open(url, 'SequenceHistory', 
        `width=${width},height=${height},left=${left},top=${top},resizable=yes,scrollbars=yes`);
}
</script>
```

### Method 2: JavaScript Module (Recommended)

Include the CSS and JS files in your main application:

```html
<link rel="stylesheet" href="/css/nurture-sequence-history.css">
<script src="/js/nurture-sequence-history.js"></script>

<div id="history-container"></div>

<script>
    // Initialize the module
    const historyModule = new NurtureSequenceHistory({
        apiBaseUrl: '/api/nurture',
        containerId: 'history-container'
    });
    
    // Load a specific sequence
    historyModule.loadSequence('NS0001');
</script>
```

### Method 3: Integration with Existing Pages

Add a history view section to your team lead dashboard:

```html
<!-- In your team lead dashboard -->
<div class="dashboard-section">
    <h2>Sequence Details</h2>
    <div id="sequence-audit-view"></div>
</div>

<script src="/css/nurture-sequence-history.css"></script>
<script src="/js/nurture-sequence-history.js"></script>

<script>
    // When user clicks on a sequence
    function viewSequenceHistory(sequenceId) {
        const auditModule = new NurtureSequenceHistory({
            containerId: 'sequence-audit-view'
        });
        auditModule.loadSequence(sequenceId);
    }
</script>
```

## API Endpoint Used

The view uses the existing API endpoint:

```
GET /api/nurture/sequence/{sequenceId}/steps
```

**Response Format:**
```json
{
    "responseType": "Success",
    "message": "Steps retrieved successfully",
    "data": [
        {
            "stepId": "NST0001",
            "sequenceId": "NS0001",
            "personId": "P001",
            "personName": "Sarah Johnson",
            "personPhone": "555-0101",
            "volunteeerId": "V001",
            "volunteerName": "Mike Thompson",
            "stepNumber": 1,
            "method": "Call",
            "scheduledDate": "2025-01-21T00:00:00Z",
            "status": "Done",
            "contactStatus": "Contacted",
            "responseType": "Normal",
            "notes": "Great conversation. Interested in small groups.",
            "completedAt": "2025-01-21T14:30:00Z",
            "createdAt": "2025-01-21T00:00:00Z",
            "updatedAt": "2025-01-21T14:30:00Z"
        },
        // ... 6 more steps
    ]
}
```

## Data Fields Explained

| Field | Description | Example |
|-------|-------------|---------|
| **stepNumber** | 1-7 indicating position in sequence | 1, 2, 3 |
| **method** | Contact method for this step | "Call" or "Visit" |
| **scheduledDate** | When step was scheduled | "2025-01-21T00:00:00Z" |
| **status** | Current step status | "Pending", "Done", "Missed" |
| **contactStatus** | Whether contact was made | "Contacted", "Not Contacted" |
| **responseType** | How person responded | "Normal", "No Response", "Needs Follow-Up", "Crisis" |
| **notes** | Detailed notes from volunteer | Text content |
| **completedAt** | When step was actually completed | Timestamp |
| **createdAt** | When record created | Timestamp |
| **updatedAt** | Last modification | Timestamp |

## Styling & Customization

### Color Scheme

The view uses a modern green theme (church brand colors):

```css
--color-primary: #085c40          /* Primary green */
--color-primary-dark: #064e35     /* Dark green */
--color-primary-light: #0d7a52    /* Light green */
--color-success: #10b981          /* Success green */
--color-danger: #ef4444           /* Red for crisis/missed */
--color-warning: #f59e0b          /* Yellow for pending */
```

### Customizing Colors

Override CSS variables in your stylesheet:

```css
:root {
    --color-primary: #your-primary-color;
    --color-success: #your-success-color;
    --color-danger: #your-danger-color;
}
```

### Responsive Breakpoints

- **Desktop:** Full 3-column grid layout
- **Tablet (≤1024px):** 2-column grid
- **Mobile (≤768px):** Single column, stacked layout
- **Small Mobile (≤480px):** Minimal padding, optimized for small screens

### Dark Mode

The CSS includes dark mode support. Enable with:

```css
@media (prefers-color-scheme: dark) {
    /* Automatically applies dark theme */
}
```

## Features Explained

### Status Indicators

Each step has a visual indicator on the timeline:

- **Green Circle** - Step completed (Done)
- **Red Circle** - Step missed (Missed)
- **Gray Circle** - Step pending (Pending)

Color-coded status pills also show in the step card.

### Response Type Badges

Visual indicators for how the person responded:

- **Normal** - Green badge, normal follow-up outcome
- **No Response** - Gray badge, no contact made
- **Needs Follow-Up** - Yellow badge, requires additional contact
- **Crisis** - Red badge, crisis situation (urgent)

### Notes Section

When a step has notes, they appear with color-coded background:

- **Default** - Green left border for normal notes
- **Crisis** - Red left border for crisis notes

### Progress Tracking

- Shows X of 7 steps completed
- Visual progress bar at top
- Status changes based on completion:
  - 0% complete: "Active"
  - 1-99% complete: "In Progress"
  - 100% complete: "Completed"

## Export Functionality

### CSV Export

Users can export the complete sequence history to CSV:

```
Step #,Title,Method,Scheduled Date,Completed Date,Status,Contact Status,Response Type,Notes
1,First Contact Call,Call,Jan 21 2025,Jan 21 2025,Done,Contacted,Normal,"Great conversation..."
2,First In-Person Visit,Visit,Jan 28 2025,Not completed,Pending,—,—,—
...
```

**File naming:** `nurture-sequence-{SEQUENCE_ID}-{TIMESTAMP}.csv`

### Print/PDF

Built-in print styling optimizes the view for paper:

- Removes buttons and interactive elements
- Optimizes colors for printing
- Ensures each step stays on one page when possible
- Professional formatting

Use browser's Print function (Ctrl+P or Cmd+P)

## Integration Examples

### Example 1: Add to Team Lead Dashboard

```html
<!-- In your team lead's active sequences list -->
<div class="sequence-card">
    <h3>Sarah Johnson - NS0001</h3>
    <p>Step 4 of 7 - In Progress</p>
    <button onclick="viewSequenceAudit('NS0001')">
        <i class="fas fa-history"></i> View History
    </button>
</div>

<div id="audit-modal" class="modal" style="display:none;">
    <div class="modal-content">
        <button onclick="closeAudit()" class="close">&times;</button>
        <div id="audit-container"></div>
    </div>
</div>

<script src="/css/nurture-sequence-history.css"></script>
<script src="/js/nurture-sequence-history.js"></script>

<script>
    function viewSequenceAudit(sequenceId) {
        document.getElementById('audit-modal').style.display = 'block';
        
        const auditModule = new NurtureSequenceHistory({
            containerId: 'audit-container'
        });
        auditModule.loadSequence(sequenceId);
    }
    
    function closeAudit() {
        document.getElementById('audit-modal').style.display = 'none';
    }
</script>
```

### Example 2: Full-Page View

```html
<!-- nurture-audit.html -->
<!DOCTYPE html>
<html>
<head>
    <link rel="stylesheet" href="/css/nurture-sequence-history.css">
</head>
<body>
    <div id="audit-container"></div>
    
    <script src="/js/nurture-sequence-history.js"></script>
    <script>
        const sequenceId = new URLSearchParams(window.location.search).get('id');
        
        const audit = new NurtureSequenceHistory({
            containerId: 'audit-container',
            apiBaseUrl: '/api/nurture'
        });
        
        audit.loadSequence(sequenceId);
    </script>
</body>
</html>
```

### Example 3: Timeline Component in React

```javascript
import React, { useEffect, useState } from 'react';

function NurtureAuditView({ sequenceId }) {
    const [module, setModule] = useState(null);
    
    useEffect(() => {
        const auditModule = new NurtureSequenceHistory({
            containerId: 'audit-container',
            apiBaseUrl: '/api/nurture'
        });
        
        auditModule.loadSequence(sequenceId);
        setModule(auditModule);
    }, [sequenceId]);
    
    return <div id="audit-container"></div>;
}

export default NurtureAuditView;
```

## Error Handling

### Network Errors

If the API call fails:
```
Failed to load sequence history: HTTP error! status: 404
```

The view displays an error message and suggests checking the sequence ID.

### Empty States

If no steps are found:
```
No Steps Found
No nurture steps have been recorded for this sequence yet.
```

### Invalid Sequence ID

Pass a valid sequence ID in URL or via `loadSequence()` method:

```javascript
// Valid
auditModule.loadSequence('NS0001');

// Invalid - will show error
auditModule.loadSequence('invalid-id');
```

## Browser Support

- Chrome/Edge 90+
- Firefox 88+
- Safari 14+
- Mobile browsers (iOS Safari, Chrome Mobile)

## Accessibility

- Keyboard navigation fully supported
- ARIA labels on interactive elements
- High contrast colors for readability
- Focus indicators on buttons
- Semantic HTML structure
- Screen reader friendly

## Performance

- Loads single sequence at a time (optimized)
- CSS animations are GPU-accelerated
- Lazy loading of step cards
- Minimal DOM manipulation
- ~50KB total (HTML + CSS + JS combined)

## Troubleshooting

### Issue: Sequence not loading

**Check:**
1. Correct sequence ID format (e.g., NS0001)
2. API endpoint is accessible
3. CORS headers are configured
4. Network tab shows successful API call

### Issue: Styles not applying

**Check:**
1. CSS file is linked correctly
2. No conflicting CSS rules
3. Browser dev tools show CSS is loaded
4. No typos in class names

### Issue: Export button not working

**Check:**
1. Browser supports Blob API
2. `Download` permission is granted
3. Browser isn't blocking pop-ups/downloads
4. Check browser console for errors

## Future Enhancements

Possible improvements:

- [ ] Add email export option
- [ ] Implement real-time updates via WebSocket
- [ ] Add filtering/search functionality
- [ ] Timeline zoom controls
- [ ] Notes editing capability
- [ ] Photo/media attachments display
- [ ] Comparison between multiple sequences
- [ ] Automated follow-up suggestions based on history

## Support

For issues or questions:
1. Check the implementation guide
2. Review API endpoint response format
3. Check browser console for errors
4. Verify CSS/JS files are loaded
5. Test with sample data

## Version History

- **v1.0.0** (2025-01-24)
  - Initial release
  - 7-step timeline display
  - CSV export
  - Print/PDF support
  - Responsive design
  - Full audit trail display
