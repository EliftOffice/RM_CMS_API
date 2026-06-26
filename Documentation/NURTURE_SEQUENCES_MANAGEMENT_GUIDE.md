# Nurture Sequences Management View

## Overview
The **Nurture Sequences Management** page provides a comprehensive, space-efficient interface for viewing all active nurture sequences, filtering by status, and drilling down into detailed tracking information for each sequence.

## File Location
- **Frontend**: `/wwwroot/templates/nurture-sequences-management.html`
- **Access**: Open in browser at `/templates/nurture-sequences-management.html`

## Features

### 1. Sequence List (Left Panel - Desktop / Full Screen - Mobile)
- **Total Count**: Shows total number of sequences in current filter
- **Status Filtering**: Quick filters for All, Active, InReview, Completed, Failed
- **Compact Items**: Each sequence shows:
  - Status icon (● for Active, ⦿ for InReview, ✓ for Completed, ✗ for Failed)
  - Person name & phone number
  - Progress (Current Step/7)
  - Volunteer name
  - Team Lead name
  - Start date
  - Sequence ID
  - Color-coded left border on hover (green = Active)

### 2. Tracking Details (Right Panel - Desktop / Modal - Mobile)
When a sequence is selected, the right panel displays:

#### Sequence Summary Section
- Person (name, phone)
- Volunteer assigned
- Team Lead assigned
- Sequence ID
- Current Status
- Progress indicator

#### 7-Step Timeline
Each step card shows:
- **Step Number** (1-7) with color coding
- **Step Title** (auto-generated: "First Contact Call", "First In-Person Visit", etc.)
- **Method** badge (Call or Visit)
- **Scheduled Date** and **Completed Date**
- **Status**: Pending, Done, or Missed
- **Contact Status**: Contacted / Not Contacted (with icon)
- **Response Type**: Normal, Needs Follow-Up, Crisis (color-coded)
- **Step Notes**: Full text in bordered box below step details

### 3. Actions
- **Print**: Prints the current tracking timeline
- **Export CSV**: Downloads tracking as CSV file for spreadsheet analysis
- **Back** (mobile): Returns to sequence list

## API Endpoints Used

### 1. Get All Sequences
```
GET /api/nurture/teamlead/all
Response (Success):
{
  "responseType": "Success",
  "message": "OK",
  "data": [
    {
      "sequenceId": "NS0001",
      "personId": "P001",
      "personName": "John Smith",
      "personPhone": "555-1234",
      "volunteerId": "V001",
      "volunteerName": "Jane Volunteer",
      "teamLeadName": "Bob Lead",
      "currentStep": 3,
      "status": "Active",
      "startedAt": "2024-01-15T10:30:00"
    },
    ...
  ]
}
```

### 2. Get Sequence Steps
```
GET /api/nurture/sequence/{sequenceId}/steps
Response (Success):
{
  "responseType": "Success",
  "message": "OK",
  "data": [
    {
      "stepId": "NST0001",
      "sequenceId": "NS0001",
      "stepNumber": 1,
      "method": "Call",
      "scheduledDate": "2024-01-22",
      "status": "Done",
      "contactStatus": "Contacted",
      "responseType": "Normal",
      "notes": "Conversation went well. Person interested in follow-up.",
      "completedAt": "2024-01-23T14:15:00"
    },
    ...
  ]
}
```

## Color Coding

### Status Icons
- **● Green** = Active sequence
- **⦿ Orange** = InReview (awaiting TL final call)
- **✓ Blue** = Permanent (completed successfully)
- **✗ Red** = Failed

### Step Status
- **Yellow** = Pending (not yet done)
- **Green** = Done
- **Red** = Missed

### Response Types
- **Green** = Normal
- **Yellow** = Needs Follow-Up
- **Red** = Crisis (🚨 prefix in notes)

## Usage

### Desktop (>1024px)
1. Opens with list panel on left, empty tracking panel on right
2. Click any sequence to view its full tracking timeline on the right
3. Filtering updates the list in real-time
4. Click Print or Export CSV to save tracking data

### Mobile (<1024px)
1. Opens with list panel full-width
2. Click a sequence to switch to tracking panel
3. Tracking panel shows back button to return to list
4. All filtering and actions work the same

## Responsive Breakpoint
- **Desktop**: 1024px and above (split panel layout)
- **Mobile**: Below 1024px (stacked single panel with toggle)

## Backend Dependencies

The feature relies on two API endpoints that must be implemented:

✅ **GET /api/nurture/teamlead/all** - Returns all sequences with summary data
- **Implemented in**: `NurtureController.cs` (line ~75)
- **Uses**: `GetAllSequencesAsync()` from BLL → DAL
- **Database Query**: Joins nurture_sequences with people, volunteers, team_leads

✅ **GET /api/nurture/sequence/{sequenceId}/steps** - Returns all steps for a sequence
- **Implemented in**: `NurtureController.cs` (line ~70)
- **Uses**: `GetStepsBySequenceAsync()` from BLL → DAL
- **Database Query**: SELECT * FROM nurture_steps WHERE sequence_id

## Testing Checklist

- [ ] Page loads without errors
- [ ] Sequence list populates with all sequences
- [ ] Filtering by status works (All, Active, InReview, Completed, Failed)
- [ ] Clicking a sequence loads its tracking details
- [ ] All 7 steps display in correct order
- [ ] Step titles match sequence (Call, Visit, Call, Visit, Call, Visit, Call)
- [ ] Dates format correctly (MMM D, YY)
- [ ] Print functionality works (Ctrl+P or Print button)
- [ ] Export CSV creates valid CSV file
- [ ] Mobile responsive behavior at <1024px
- [ ] No console errors in browser DevTools

## Expected Data

With `schema.sql` sample data (4 sequences: NS0001-NS0004), you should see:
- 4 items in the list by default (when filtering "All")
- Each sequence has person name, phone, and volunteer/TL assignments
- Clicking a sequence shows 7 pre-created nurture steps
- Steps display method (Call/Visit) alternating pattern
- Status indicators show progress visually

## Integration Points

### Link from Main Navigation
Add a link to the team leads or admin dashboard:
```html
<a href="/templates/nurture-sequences-management.html">
  <i class="fas fa-list"></i> Nurture Management
</a>
```

### Embed in Another Page
To embed the tracking view in another page:
```html
<iframe src="/templates/nurture-sequences-management.html" 
        style="width:100%; height:600px; border:none;">
</iframe>
```

Or use the reusable module from `nurture-sequence-history.js` for custom integration.

## Performance Notes

- **Page Size**: ~25KB (HTML + CSS + JS combined)
- **API Calls**: 2 requests on load (list + first sequence details if auto-selected)
- **Rendering**: Fast timeline rendering (Vanilla JS, no frameworks)
- **Scrolling**: Smooth with custom scrollbar styling
- **Print Performance**: Optimized print stylesheet included

## Customization Options

### Change Colors
All colors are in CSS variables at the top of the `<style>` block:
```css
--color-primary: #085c40;      /* Main green */
--color-success: #10b981;      /* Done/success green */
--color-warning: #f59e0b;      /* In-review yellow */
--color-danger: #ef4444;       /* Failed/crisis red */
--color-info: #3b82f6;         /* Completed blue */
```

### Adjust Spacing
All padding/margins are specified explicitly to allow customization:
- List padding: `padding: 10px 12px`
- Info grid: `gap: 12px`
- Timeline items: `margin-bottom: 12px`

### Add More Step Types
Update the `getStepTitle()` function to customize step names:
```javascript
function getStepTitle(stepNumber) {
    const titles = [
        'First Contact Call',      // Step 1
        'First In-Person Visit',   // Step 2
        // ... etc
    ];
    return titles[stepNumber - 1] || `Step ${stepNumber}`;
}
```

## Troubleshooting

### "Failed to load sequences"
- Check API endpoint `/api/nurture/teamlead/all` is responding
- Verify database connection and query syntax
- Check browser console for error details

### Steps don't show when clicking sequence
- Verify `/api/nurture/sequence/{sequenceId}/steps` endpoint exists
- Check that sequence has associated nurture_steps records
- Confirm sequenceId format matches (NS####)

### Dates showing as "—" (dash)
- Check if step dates are NULL in database
- Verify date format from API (should be ISO 8601: YYYY-MM-DD)
- Check `formatDate()` function in JavaScript

### Mobile view not stacking
- Verify viewport meta tag is present in `<head>`
- Check media query breakpoint (default: 1024px)
- Use browser DevTools to test responsive width

## Files Modified

1. **Created**: `/wwwroot/templates/nurture-sequences-management.html`
   - Complete standalone page with HTML, CSS, JS

2. **Modified**: `/DAL/Nurture/NurtureDAL.cs`
   - Added interface: `GetAllSequencesAsync()`
   - Added implementation: SQL query to join sequences with people/volunteers/team_leads

3. **Modified**: `/BLL/Nurture/NurtureBLL.cs`
   - Added interface: `GetAllSequencesAsync()`
   - Added implementation: wraps DAL call with error handling

4. **Modified**: `/Controllers/Nurture/NurtureController.cs`
   - Added endpoint: `GET /api/nurture/teamlead/all`
   - Returns all sequences for list view

## Version History

- **v1.0** (Current)
  - Initial release with list + tracking view
  - Desktop/mobile responsive split panel
  - Status filtering
  - CSV export and print functionality
  - 7-step timeline display

## Support

For issues or questions about implementation:
1. Check API response in browser Network tab
2. Review database schema in `schema.sql`
3. Verify controller endpoints are registered in `Program.cs`
4. Check browser console for JavaScript errors
