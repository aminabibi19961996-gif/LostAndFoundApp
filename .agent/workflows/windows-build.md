---
description: Final Windows compatibility build before pushing to GitHub
---

# Windows Final Build Checklist

**IMPORTANT:** Always run this workflow before pushing to GitHub. The production target is **Windows Server / IIS** with **MSSQL Express**.

## Pre-Push Checklist

### 1. JSON Boolean Sanitization (Auto-Handled)
- `Program.cs` has a startup sanitizer that auto-corrects `True`/`False` → `true`/`false` in `appsettings*.json`
- **No action needed** — just be aware it exists for Windows/IIS admin edits

### 2. File Storage Path Normalization
- `FileService.cs` uses `NormalizePath()` on all storage paths
- Converts `./SecureStorage/Photos` → absolute path using `Path.GetFullPath()`
- Converts forward slashes to OS-native separators (`\` on Windows, `/` on Linux)
- **Verify:** If you add any new file storage paths, wrap them with `NormalizePath()`

### 3. CSV Export UTF-8 BOM
- Both CSV exports (Items + Activity Logs) prepend UTF-8 BOM bytes
- This ensures Windows Excel opens CSV files with correct encoding
- **Verify:** If you add new CSV exports, include the BOM prefix pattern:
  ```csharp
  var csvContent = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
  var bom = System.Text.Encoding.UTF8.GetPreamble();
  var bytes = new byte[bom.Length + csvContent.Length];
  bom.CopyTo(bytes, 0);
  csvContent.CopyTo(bytes, bom.Length);
  ```

### 4. Serilog Log Path
- Log path uses `Path.Combine("Logs", "log-.txt")` (not hardcoded `/`)
- Explicit `UTF8` encoding set for log files
- **Verify:** If you add new log sinks, use `Path.Combine()` for paths

### 5. Connection String Format
- **Development (Linux):** `Server=localhost,1433` (TCP) — used in current `appsettings.json`
- **Production (Windows):** `Server=.\SQLEXPRESS` (named pipes) or `Server=localhost,1433` (TCP)
- **Verify:** Update `appsettings.json` connection string for the target environment before deploying

### 6. Build Verification
// turbo
Run the build to check for errors:
```
dotnet build --no-restore
```
- **0 errors required** before pushing
- `CA1416` warnings about `System.DirectoryServices.AccountManagement` are expected (AD is Windows-only by design)

### 7. Known Windows-Specific Concerns
- **File locking:** Windows locks files more aggressively. Serilog handles this, but don't open log files in Notepad while the app runs
- **Case sensitivity:** Windows filesystem is case-insensitive. Don't rely on case differences in filenames
- **IIS App Pool Identity:** Make sure the app pool user has read/write access to `SecureStorage/` and `Logs/` directories
- **Long paths:** Windows has a 260-character path limit. Keep file storage paths short

### 8. Final Push
// turbo
```
git add -A && git status
```
Review the changes, then commit and push.
