# Security Best Practices

This document outlines security best practices for the SingleStepViewer application and the security fixes that have been implemented.

## Critical Security Fixes

### 1. Command Injection Prevention (CRITICAL)

**Issue**: User-supplied video URLs were being passed to yt-dlp process without proper escaping, allowing potential command injection attacks.

**Files Fixed**:
- `Services/DownloadService.cs`
- `Services/VideoService.cs`

**Fix**: Replaced string concatenation of process arguments with `ProcessStartInfo.ArgumentList`, which properly escapes arguments and prevents command injection.

**Before**:
```csharp
Arguments = $"--dump-json --no-playlist \"{videoUrl}\""
```

**After**:
```csharp
startInfo.ArgumentList.Add("--dump-json");
startInfo.ArgumentList.Add("--no-playlist");
startInfo.ArgumentList.Add(videoUrl);  // Safely handled
```

**Impact**: Prevents attackers from executing arbitrary commands through malicious video URLs.

---

### 2. Path Traversal Prevention (CRITICAL)

**Issue**: Video filenames derived from user-controlled titles could contain path traversal sequences (`../`), allowing files to be written outside the intended storage directory.

**File Fixed**: `BackgroundServices/VideoDownloaderService.cs`

**Fix**: Added explicit path separator removal and use `Path.GetFileName()` to ensure only the filename portion is used.

**Before**:
```csharp
var safe = new string(fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
```

**After**:
```csharp
var safe = new string(fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
safe = safe.Replace('/', '_').Replace('\\', '_');
safe = Path.GetFileName(safe);  // Ensure no path components
```

**Impact**: Prevents attackers from writing files to arbitrary locations on the filesystem.

---

### 3. Hardcoded Credentials Removal (HIGH)

**Issue**: Default admin credentials were hardcoded in source code, making them accessible to anyone with code access.

**Files Fixed**:
- `Program.cs`
- `appsettings.json`
- Added: `Configuration/AdminUserOptions.cs`

**Fix**: Moved credentials to configuration file, allowing them to be changed without code modification.

**Deployment Recommendation**: 
- **IMPORTANT**: Change the default admin password in production deployments
- Use environment variables or Azure Key Vault for production secrets
- Never commit production credentials to source control

**Configuration**:
```json
{
  "AdminUser": {
    "Username": "admin",
    "Email": "admin@singlestep.local",
    "Password": "Admin123!"  // CHANGE IN PRODUCTION
  }
}
```

**Production Deployment**:
```bash
# Use environment variables
export AdminUser__Password="YourSecurePassword123!"
dotnet run
```

---

### 4. Information Disclosure Prevention (MEDIUM)

**Issue**: Admin password was being logged at startup, potentially exposing it in log files.

**File Fixed**: `Program.cs`

**Fix**: Removed password from log output.

**Before**:
```csharp
Log.Information("Default admin user created - Username: {Username}, Email: {Email}, Password: {Password}", 
    adminUsername, adminEmail, adminPassword);
```

**After**:
```csharp
Log.Information("Default admin user created - Username: {Username}, Email: {Email}", 
    adminUsername, adminEmail);
```

---

## Additional Security Improvements

### 5. Startup Configuration Validation

**File**: `Program.cs`

**Description**: Added comprehensive validation of configuration at startup to detect missing or invalid dependencies before they cause runtime errors.

**Validates**:
- Database connection string presence
- yt-dlp executable existence (warns if not found)
- VLC installation directory (warns if not found)
- Video storage path writability
- Required configuration sections

**Impact**: Fail-fast on misconfiguration, preventing runtime errors and improving security posture.

---

### 6. Concurrency Control

**File**: `Data/Entities/PlaybackHistory.cs`

**Description**: Added row version concurrency token to prevent duplicate playback history records in race conditions.

**Fix**:
```csharp
[Timestamp]
public byte[]? RowVersion { get; set; }
```

**Impact**: Prevents duplicate history records when multiple threads attempt to update the same record simultaneously.

---

### 7. Resource Cleanup Improvements

**File**: `Services/PlaybackService.cs`

**Description**: Improved LibVLC initialization to properly clean up resources if MediaPlayer creation fails.

**Impact**: Prevents resource leaks when VLC initialization fails partially.

---

### 8. Fire-and-Forget Task Handling

**File**: `Services/PlaylistItemService.cs`

**Description**: Improved error handling for background metadata extraction task to catch and log unhandled exceptions.

**Impact**: Prevents silent failures and ensures exceptions are properly logged.

---

## Security Best Practices for Deployment

### Production Checklist

- [ ] Change default admin password
- [ ] Use environment variables for sensitive configuration
- [ ] Enable HTTPS/TLS
- [ ] Implement rate limiting on authentication endpoints
- [ ] Review and restrict user permissions
- [ ] Enable database backups
- [ ] Configure firewall rules
- [ ] Keep dependencies updated
- [ ] Monitor logs for suspicious activity
- [ ] Implement CSP headers
- [ ] Enable CORS restrictions

### Secure Configuration

**Option 1: Environment Variables**
```bash
export ConnectionStrings__DefaultConnection="Data Source=/secure/path/db.sqlite"
export AdminUser__Password="SecurePassword123!"
export Video__StoragePath="/secure/videos"
```

**Option 2: User Secrets (Development)**
```bash
dotnet user-secrets set "AdminUser:Password" "DevPassword123!"
```

**Option 3: Azure Key Vault (Production)**
```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri("https://your-keyvault.vault.azure.net/"),
    new DefaultAzureCredential());
```

### Input Validation

All user inputs should be validated:
- Video URLs: Validate format and allowed domains
- File names: Already sanitized, but consider allowlisting extensions
- User data: Already handled by ASP.NET Identity validation

### Regular Security Updates

```bash
# Check for vulnerable dependencies
dotnet list package --vulnerable

# Update packages
dotnet outdated
dotnet add package <PackageName> --version <NewVersion>
```

---

## Remaining Security Considerations

While the critical issues have been addressed, consider these additional hardening measures:

1. **Content Security Policy**: Add CSP headers to prevent XSS attacks
2. **CSRF Protection**: Already provided by Blazor, but verify configuration
3. **SQL Injection**: Using EF Core parameterized queries (already safe)
4. **Authentication**: Consider adding 2FA for admin accounts
5. **Session Management**: Review session timeout settings
6. **Logging**: Consider PII redaction in logs
7. **Error Messages**: Avoid exposing stack traces in production

---

## Security Contact

For security issues, please:
1. Do not open public GitHub issues
2. Contact the repository maintainers privately
3. Allow reasonable time for fixes before public disclosure

---

Last Updated: 2026-01-29
