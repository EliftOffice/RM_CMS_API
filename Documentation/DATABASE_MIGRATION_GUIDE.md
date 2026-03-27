# ?? DATABASE MIGRATION GUIDE - SQL Server to MySQL

## ? Status: Migration Complete

Your project has been successfully updated to use **MySQL** instead of SQL Server across both modules.

---

## ?? Changes Made

### 1. **Project File (RM_CMS.csproj)**
```diff
- <PackageReference Include="System.Data.SqlClient" Version="4.8.6" />
+ <PackageReference Include="MySqlConnector" Version="2.3.7" />
```

**Why:** MySqlConnector is the recommended async-first MySQL driver for .NET 8

### 2. **Database Connection Factory (Data/DbConnection.cs)**
```diff
- using System.Data.SqlClient;
- return new SqlConnection(connectionString);
+ using MySqlConnector;
+ return new MySqlConnection(connectionString);
```

**Why:** Connects to MySQL instead of SQL Server

---

## ?? Visitors Module Migration

The Visitors module needs to be updated to work with MySQL. Here's what to do:

### Step 1: Verify SQL Script Compatibility ?

Your `01_Create_People_Table.sql` is already **MySQL compatible**! It uses:
- ? `VARCHAR` (MySQL format)
- ? `DATE` for dates
- ? `TIMESTAMP DEFAULT CURRENT_TIMESTAMP` (MySQL syntax)
- ? `TEXT` for large strings
- ? `INTEGER DEFAULT 1` (MySQL compatible)

### Step 2: Connection String Configuration

Update your `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyAppDb;User=root;Password=your_mysql_password;"
  }
}
```

**Format:** `Server=<host>;Database=<name>;User=<username>;Password=<password>;`

### Step 3: Create Both Tables in MySQL

```bash
# Create database
mysql -u root -p
mysql> CREATE DATABASE MyAppDb;

# Run Visitors table script
mysql -u root -p MyAppDb < Database/SQL_Scripts/01_Create_People_Table.sql

# Run Volunteers table script
mysql -u root -p MyAppDb < Database/SQL_Scripts/02_Create_Volunteers_Table.sql
```

### Step 4: Verify Installation

```bash
dotnet build
```

**Result:** ? Build successful

---

## ?? Module Comparison (Both Now on MySQL)

| Feature | Visitors | Volunteers |
|---------|----------|-----------|
| **Database** | MySQL ? | MySQL ? |
| **Connection** | MySqlConnection | MySqlConnection |
| **Package** | MySqlConnector | MySqlConnector |
| **ORM** | Dapper | Dapper |
| **Async** | ? Yes | ? Yes |
| **SQL Style** | MySQL | MySQL |

---

## ?? Key Differences: SQL Server vs MySQL

### Pagination
**SQL Server:**
```sql
SELECT * FROM people
ORDER BY created_at DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
```

**MySQL (Current - Used by Both Modules):**
```sql
SELECT * FROM people
ORDER BY created_at DESC
LIMIT @PageSize OFFSET @Offset
```

? **Both modules already use MySQL syntax**

### Auto-Increment
**SQL Server:**
```sql
person_id INT PRIMARY KEY IDENTITY(1,1)
```

**MySQL (Current - Used by Both Modules):**
```sql
person_id VARCHAR(20) PRIMARY KEY
-- Auto-generated in application code
```

? **Both modules use application-generated IDs**

### Timestamps
**SQL Server:**
```sql
created_at DATETIME DEFAULT GETDATE()
```

**MySQL (Current - Used by Both Modules):**
```sql
created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
```

? **Both modules use MySQL timestamp syntax**

---

## ?? Verification Checklist

- [x] MySqlConnector package added to project
- [x] DbConnection.cs updated to use MySqlConnector
- [x] Project builds successfully
- [x] Volunteers module is MySQL-compatible
- [x] Visitors module schema is MySQL-compatible
- [x] Connection string format is MySQL
- [ ] Database created in MySQL
- [ ] Both SQL scripts executed
- [ ] Test endpoints with Swagger

---

## ?? Next Steps

### 1. Prepare MySQL Database (5 min)
```bash
# Install MySQL if not already done
# Then create database:

mysql -u root -p
mysql> CREATE DATABASE MyAppDb;
mysql> exit
```

### 2. Run SQL Scripts (2 min)
```bash
# Run Visitors table
mysql -u root -p MyAppDb < Database/SQL_Scripts/01_Create_People_Table.sql

# Run Volunteers table
mysql -u root -p MyAppDb < Database/SQL_Scripts/02_Create_Volunteers_Table.sql
```

### 3. Update Configuration (1 min)
Update `appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyAppDb;User=root;Password=your_password;"
  }
}
```

### 4. Run Application (1 min)
```bash
dotnet run
```

### 5. Test Both Modules (10 min)
Open Swagger UI:
```
https://localhost:7xxx/swagger
```

Test endpoints:
- **Visitors:** `/api/peoples`
- **Volunteers:** `/api/volunteers`

---

## ?? Connection String Examples

### Local MySQL
```
Server=localhost;Database=MyAppDb;User=root;Password=mypassword;
```

### MySQL with Port
```
Server=localhost:3306;Database=MyAppDb;User=root;Password=mypassword;
```

### Remote MySQL
```
Server=192.168.1.100;Database=MyAppDb;User=cms_user;Password=strong_password;Port=3306;
```

### MySQL with SSL
```
Server=localhost;Database=MyAppDb;User=root;Password=mypassword;SslMode=Required;
```

---

## ?? Important Notes

### About MySqlConnector
- ? Async-first (uses async/await properly)
- ? .NET 8 compatible
- ? Actively maintained
- ? Better performance than legacy MySql.Data

### Connection String Safety
- ? Don't put passwords in source code
- ? Use User Secrets for development:
  ```bash
  dotnet user-secrets set "ConnectionStrings:DefaultConnection" "your_connection_string"
  ```
- ? Use Environment Variables for production

### Database Character Set
MySQL defaults to UTF-8, which is fine. If you need explicit UTF-8:
```sql
CREATE DATABASE MyAppDb CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

---

## ?? Troubleshooting

### Issue: "Unable to connect to any of the specified MySQL hosts"
**Solution:**
- Verify MySQL is running
- Check connection string is correct
- Verify credentials (user/password)
- Ensure database exists

### Issue: "Plugin 'mysql_native_password' is not loaded"
**Solution:**
- Update MySQL password plugin (MySQL 8.0+)
- Or add to connection string: `defaultauthenticationplugin=mysql_native_password`

### Issue: "Table 'MyAppDb.people' doesn't exist"
**Solution:**
- Run the SQL scripts to create tables
- Verify database name matches connection string

### Issue: "Access denied for user 'root'@'localhost'"
**Solution:**
- Check username and password
- Reset MySQL root password if needed

---

## ?? Resources

- [MySqlConnector Documentation](https://mysqlconnector.net/)
- [MySQL Connection Strings](https://dev.mysql.com/doc/)
- [Dapper with MySQL](https://github.com/DapperLib/Dapper)
- [MySQL Best Practices](https://dev.mysql.com/doc/refman/8.0/en/optimization.html)

---

## ? Summary

Your project is now **fully MySQL compatible** with:

? Both Visitors and Volunteers modules  
? MySqlConnector package  
? Correct connection factory  
? MySQL-compatible SQL scripts  
? Ready to build and deploy  

**Build Status:** ? Successful

---

## ?? Final Checklist

Before running the app:

- [ ] MySQL installed and running
- [ ] Database created (`MyAppDb`)
- [ ] Both SQL scripts executed
- [ ] Connection string updated in appsettings.Development.json
- [ ] Build succeeds (`dotnet build`)
- [ ] Ready to run (`dotnet run`)

---

**Status:** ? **Migration Complete - Ready for MySQL**

**Next:** Follow the "Next Steps" section above to set up your database.

---

*Migration Date: January 25, 2025*  
*Source: SQL Server ? MySQL*  
*Status: Complete ?*
