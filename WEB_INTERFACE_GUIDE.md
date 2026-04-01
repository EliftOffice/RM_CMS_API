# RM CMS Web Interface Setup Guide

## Accessing the HTML Interface

After starting the application, you can access the web interface through any of the following URLs:

### Primary Routes:
1. **Root URL** (Recommended):
   ```
   https://localhost:7096/
   ```

2. **Direct Template Path**:
   ```
   https://localhost:7096/templates/index.html
   ```

## Troubleshooting

### If you get "Not Reachable" Error:

#### 1. **Ensure the API is Running**
   - Make sure your ASP.NET Core application is running
   - Check that it's listening on `https://localhost:7096`

#### 2. **Clear Browser Cache**
   - Press `Ctrl + Shift + Delete` to open browser cache settings
   - Clear cache and cookies
   - Hard refresh the page with `Ctrl + Shift + R`

#### 3. **Check Firewall**
   - Ensure Windows Firewall isn't blocking port 7096
   - Allow the application through firewall if prompted

#### 4. **Verify wwwroot Folder**
   - Make sure the `wwwroot` folder exists in your project root
   - Verify the folder structure: `wwwroot/templates/index.html`

#### 5. **Check SSL Certificate**
   - If you get an SSL error, click "Advanced" and "Proceed to localhost"
   - Or use `http://localhost:5054` if HTTP is configured

#### 6. **Verify API Base URL in HTML**
   - Open your browser's developer tools (`F12`)
   - Check the Console tab for any JavaScript errors
   - The HTML file has this configuration:
     ```javascript
     const API_BASE_URL = 'https://localhost:7096/api';
     ```
   - Update the port/protocol if your API runs on a different address

### 7. **Browser Development Tools**
   - Press `F12` to open Developer Tools
   - Go to **Network** tab
   - Try accessing `https://localhost:7096/`
   - Check if requests are being made and what responses you get
   - Look for any 404 or 500 errors

## Features Available

### 1. **People Management**
   - Assign a person to an available volunteer
   - Required: Person ID (e.g., P001)

### 2. **Volunteers Management**
   - Create new volunteer records
   - Required: First Name, Last Name, Email, Status

### 3. **Follow-ups Management**
   - Create follow-up records
   - Required: Person ID, Volunteer ID, Contact Method, Contact Status

## API Endpoint Examples

All API calls are made to `https://localhost:7096/api`:

- `POST /api/peoples/{personId}/assign` - Assign person to volunteer
- `POST /api/volunteers` - Create volunteer
- `POST /api/followups` - Create follow-up

## Technical Stack

- **Frontend**: HTML5, Bootstrap 5, jQuery 3.6
- **Backend**: ASP.NET Core 8, Dapper ORM
- **Database**: MySQL
- **Design**: Responsive, Mobile-friendly

## Notes

- All validation is handled in the Business Logic Layer (BLL)
- API responses are automatically formatted and displayed
- Loading indicators show during API calls
- Responses are color-coded (green = success, red = error, yellow = warning)
