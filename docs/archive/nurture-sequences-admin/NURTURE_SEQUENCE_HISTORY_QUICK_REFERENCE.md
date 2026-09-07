# Nurture Sequence History - Quick Reference

## Quick Links
- **Full HTML:** `/templates/nurture-sequence-history.html`
- **JavaScript Module:** `/js/nurture-sequence-history.js`
- **Styles:** `/css/nurture-sequence-history.css`
- **Full Guide:** See `NURTURE_SEQUENCE_HISTORY_GUIDE.md`

## API Endpoint

```
GET /api/nurture/sequence/{sequenceId}/steps
```

Response: Array of NurtureStep objects with person/volunteer details

## 3-Minute Integration

### Step 1: Add Links to Your HTML

```html
<link rel="stylesheet" href="/css/nurture-sequence-history.css">
<script src="/js/nurture-sequence-history.js"></script>
```

### Step 2: Create a Container

```html
<div id="sequence-history"></div>
```

### Step 3: Initialize & Load

```javascript
const audit = new NurtureSequenceHistory({
    containerId: 'sequence-history'
});

audit.loadSequence('NS0001');
```

That's it! The full UI is rendered automatically.

## Common Patterns

### Pattern 1: Modal Dialog

```html
<button onclick="showSequenceHistory('NS0001')">View History</button>

<div id="history-modal" style="display:none; position:fixed; inset:0; 
     background:rgba(0,0,0,0.5); z-index:1000; overflow:auto; padding:20px;">
    <div style="background:white; max-width:1000px; margin:0 auto; 
                border-radius:12px; max-height:90vh; overflow:auto;">
        <button onclick="this.parentElement.parentElement.style.display='none'" 
                style="float:right; font-size:24px; border:none; cursor:pointer;">×</button>
        <div id="history-container"></div>
    </div>
</div>

<script src="/css/nurture-sequence-history.css"></script>
<script src="/js/nurture-sequence-history.js"></script>

<script>
function showSequenceHistory(sequenceId) {
    document.getElementById('history-modal').style.display = 'block';
    const audit = new NurtureSequenceHistory({ containerId: 'history-container' });
    audit.loadSequence(sequenceId);
}
</script>
```

### Pattern 2: Side-by-Side with Details

```html
<div style="display: grid; grid-template-columns: 1fr 1fr; gap: 20px;">
    <div>
        <h2>Sequence Details</h2>
        <!-- Your existing details panel -->
    </div>
    <div id="history-panel"></div>
</div>

<script>
const audit = new NurtureSequenceHistory({ 
    containerId: 'history-panel' 
});
audit.loadSequence(getSelectedSequenceId());
</script>
```

### Pattern 3: Tab Interface

```html
<div style="border-bottom: 2px solid #e5e7eb; margin-bottom: 20px;">
    <button onclick="switchTab('details')" class="tab-btn">Details</button>
    <button onclick="switchTab('history')" class="tab-btn">History</button>
    <button onclick="switchTab('notes')" class="tab-btn">Notes</button>
</div>

<div id="details-tab"></div>
<div id="history-tab" style="display:none;"></div>
<div id="notes-tab" style="display:none;"></div>

<script>
function switchTab(tabName) {
    document.querySelectorAll('[id$="-tab"]').forEach(el => el.style.display = 'none');
    document.getElementById(tabName + '-tab').style.display = 'block';
    
    if(tabName === 'history') {
        const audit = new NurtureSequenceHistory({ 
            containerId: 'history-tab' 
        });
        audit.loadSequence(getCurrentSequenceId());
    }
}
</script>
```

## Key Methods

```javascript
// Initialize module
const audit = new NurtureSequenceHistory(options);

// Load sequence data and render
await audit.loadSequence(sequenceId);

// Export to CSV
audit.exportToCSV();

// Internally used methods
audit.render(stepsData);           // Render timeline
audit.formatDate(dateString);      // Format dates
audit.escapeHtml(htmlString);      // XSS protection
```

## Options Object

```javascript
new NurtureSequenceHistory({
    apiBaseUrl: '/api/nurture',           // API base URL
    containerId: 'my-container'           // HTML container ID
})
```

## Styling Customization

### Change Primary Color

```css
:root {
    --color-primary: #your-color;         /* Primary actions */
    --color-success: #your-success;       /* Completed steps */
    --color-danger: #your-danger;         /* Crisis/Missed */
    --color-warning: #your-warning;       /* Pending */
}
```

### Adjust Typography

```css
.nurture-step-title h3 {
    font-size: 16px;                      /* Step title size */
}

.nurture-detail-label {
    font-size: 11px;                      /* Label size */
}

.nurture-detail-value {
    font-size: 14px;                      /* Value size */
}
```

### Custom Spacing

```css
.nurture-timeline-item {
    margin-bottom: 40px;                  /* Space between steps */
}

.nurture-step-card {
    padding: 24px;                        /* Card padding */
}
```

## Event Listeners

The module automatically handles:
- CSV export button click
- Print button click
- Loading states
- Error display
- Data rendering

No additional event setup needed.

## Data Format

Each step in response contains:

```javascript
{
    stepId: "NST0001",
    sequenceId: "NS0001",
    personId: "P001",
    personName: "Sarah Johnson",
    personPhone: "555-0101",
    volunteerName: "Mike Thompson",
    stepNumber: 1,
    method: "Call",
    scheduledDate: "2025-01-21T00:00:00Z",
    status: "Done",
    contactStatus: "Contacted",
    responseType: "Normal",
    notes: "Good conversation",
    completedAt: "2025-01-21T14:30:00Z",
    createdAt: "2025-01-21T00:00:00Z",
    updatedAt: "2025-01-21T14:30:00Z"
}
```

## CSS Classes Reference

```
.nurture-timeline           /* Timeline container */
.nurture-timeline-item      /* Individual step item */
.nurture-step-card          /* Step content card */
.nurture-step-header        /* Header section */
.nurture-step-details       /* Details grid */
.nurture-detail-field       /* Individual field */
.nurture-status-badge       /* Status pill */
.nurture-response-badge     /* Response type badge */
.nurture-notes-section      /* Notes container */
.nurture-btn                /* Button */
```

## Status Classes

```
.nurture-done               /* Completed step */
.nurture-missed             /* Missed step */
.nurture-pending            /* Pending step */
```

## Response Type Classes

```
.nurture-response-normal
.nurture-response-no-response
.nurture-response-needs-followup
.nurture-response-crisis
```

## Troubleshooting Quick Guide

| Issue | Solution |
|-------|----------|
| Nothing displays | Check container ID matches, CSS/JS files loaded |
| API 404 error | Verify sequence ID format (NS0001), API endpoint working |
| Styles wrong | Clear browser cache, check CSS file link |
| Export not working | Check browser supports Blob API, downloads not blocked |
| Blank page | Check browser console for errors, API response format |

## Browser Console Debugging

```javascript
// Check if module loaded
console.log(typeof NurtureSequenceHistory);

// Check container exists
console.log(document.getElementById('my-container'));

// Test API endpoint
fetch('/api/nurture/sequence/NS0001/steps')
    .then(r => r.json())
    .then(d => console.log(d));
```

## Performance Tips

1. Load only when needed (lazy load)
2. Use single instance per page
3. Don't re-initialize unnecessarily
4. Browser caches CSS/JS after first load
5. API response is ~2-5KB for typical sequence

## Security

- HTML content escaped to prevent XSS
- No inline scripts in generated HTML
- CORS headers required for cross-origin API
- User data sanitized before display

## Accessibility

- Keyboard navigation fully supported
- Focus indicators on interactive elements
- Semantic HTML (headers, buttons, etc.)
- ARIA labels where needed
- High contrast colors

## Mobile Responsive

- Full-width on mobile
- Single column layout on small screens
- Touch-friendly buttons (large hit targets)
- Optimized for portrait orientation

## Testing

### Test with Sample Data

```javascript
const testData = [
    {
        stepNumber: 1,
        method: 'Call',
        scheduledDate: '2025-01-21T00:00:00Z',
        status: 'Done',
        contactStatus: 'Contacted',
        responseType: 'Normal',
        notes: 'Test note',
        personName: 'Test Person',
        volunteerName: 'Test Volunteer',
        sequenceId: 'NS0001'
    },
    // ... more steps
];

const audit = new NurtureSequenceHistory();
audit.render(testData);
```

### Test CSV Export

```javascript
const audit = new NurtureSequenceHistory();
audit.loadSequence('NS0001').then(() => {
    audit.exportToCSV();
});
```

## Support Resources

- Full Implementation Guide: `NURTURE_SEQUENCE_HISTORY_GUIDE.md`
- Source Code Comments: View `/js/nurture-sequence-history.js`
- API Documentation: See `/Controllers/Nurture/NurtureController.cs`
- Database Schema: See `/Documentation/DB_Schema/`

## Version

**v1.0.0** - Initial Release (2025-01-24)

Components:
- ✓ HTML template
- ✓ JavaScript module
- ✓ Complete CSS styling
- ✓ Documentation
- ✓ CSV export
- ✓ Print support
- ✓ Responsive design
- ✓ Error handling
