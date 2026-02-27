# Changelog

All notable changes to the **Lost & Found Application** are documented in this file.

---

## [2.1.0] — 2026-02-27 — Comprehensive Codebase Audit & Bug Fixes

### 🔴 Critical Fixes
- **BulkDelete data loss prevention** — Files are now deleted **after** the database commit succeeds. Previously, files were deleted first, so if `SaveChangesAsync()` failed (FK constraint, timeout, etc.), records became orphaned with permanently lost files. Added try/catch with user-friendly error message on failure.
- **Bulk actions form binding fixed** — `BulkDelete` and `BulkStatusUpdate` controllers now accept a comma-separated string and parse it via a new `ParseIds()` helper. Previously, the JavaScript joined IDs into `"1,5,12"` in a single hidden input, but the controller expected `int[]` — ASP.NET model binding couldn't parse this, so bulk operations silently failed every time.
- **Removed dead MasterDataApproval code** — The `MasterDataApproval` model, `IsPending` flags on all 6 master data entities, and the `DbSet<MasterDataApproval>` were removed. This approval workflow was never implemented (no controller, no views, no UI), so the code was misleading dead weight.

### 🟠 Major Fixes
- **Password policy no longer hardcoded** — Removed `MinimumLength = 8` from `StringLength` attributes in `ChangePasswordViewModel` and `CreateUserViewModel`. The `DatabasePasswordValidator` reads the real minimum from the database, so the ViewModel was overriding the dynamic policy.
- **File upload size limits aligned** — Request limits on Create/Edit POST actions changed from 15MB to 10MB, matching the `FileUpload:MaxFileSizeBytes` configuration in `appsettings.json` and the `FileService` validation.
- **Inline "+" buttons role-gated** — All 12 inline master data creation buttons (6 each in Create and Edit views) are now wrapped in `@if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin"))`. Regular "User" role users no longer see buttons they can't use (the AJAX endpoints require Admin+).
- **CSS `d-md-none` fixed** — Changed from `@media (max-width: 767px)` to `@media (min-width: 768px)` to match Bootstrap convention (hide at md+, show on mobile).
- **CSS `d-none.d-md-flex` fixed** — Changed from `@media (min-width: 992px)` to `@media (min-width: 768px)` so mobile-only nav links (Profile, Change Password) now appear correctly on mobile devices.

### 🟡 Minor Fixes
- **Semantic CSS colors differentiated** — `text-danger`, `btn-danger`, `alert-danger` now use **red** (#ef4444). `text-success`, `btn-success`, `alert-success` now use **green** (#22c55e). `text-warning`, `alert-warning` use **amber** (#f59e0b). `text-info`, `alert-info` use **blue** (#3b82f6). Previously all mapped to the same purple accent color, making errors indistinguishable from success messages.
- **Validation field colors** — `.field-validation-error` and `.input-validation-error` now use red instead of purple for error highlighting.
- **CSV formula injection protection** — `EscapeCsv()` now prefixes values starting with `=`, `+`, `-`, `@`, or `\t` with a single quote to prevent Excel formula execution. Tab characters are also stripped.
- **AJAX 403 error handling** — Inline-create fetch in `site.js` now checks `res.ok` before parsing JSON. A 403 Forbidden response (when a User role triggers the endpoint) now shows "You do not have permission" toast instead of crashing.
- **StatusDate type mismatch** — `BulkStatusUpdate` now sets `DateTime.Today` instead of `DateTime.UtcNow` for the date-only `StatusDate` field.
- **MIME type validation** — `FileService` now validates that the browser-reported `Content-Type` matches expected MIME types for the file extension (defense in depth for file uploads).
- **User list pagination** — `UserManagementController.Index` now paginates results at 50 users per page with Previous/Next navigation controls.
- **AD sync retry configurable** — `AdSyncHostedService` retry interval is now configurable via `ActiveDirectory:SyncRetryMinutes` in appsettings (default: 60 minutes).

### Files Modified (13)
| File | Changes |
|------|---------|
| `Controllers/LostFoundItemController.cs` | BulkDelete order, form binding, file size, CSV injection, StatusDate, ParseIds helper |
| `Controllers/UserManagementController.cs` | 50-per-page pagination |
| `Models/MasterDataModels.cs` | Removed MasterDataApproval + IsPending |
| `Data/ApplicationDbContext.cs` | Removed DbSet<MasterDataApproval> |
| `ViewModels/AccountViewModels.cs` | Removed hardcoded MinimumLength=8 |
| `ViewModels/UserManagementViewModels.cs` | Removed hardcoded MinimumLength=8 |
| `Views/LostFoundItem/Create.cshtml` | Role-gated inline create buttons |
| `Views/LostFoundItem/Edit.cshtml` | Role-gated inline create buttons |
| `Views/UserManagement/Index.cshtml` | Pagination controls |
| `Services/FileService.cs` | MIME type validation |
| `Services/AdSyncHostedService.cs` | Configurable retry interval |
| `wwwroot/css/site.css` | Semantic colors, responsive classes, validation colors |
| `wwwroot/js/site.js` | AJAX 403 handling |

---

## [2.0.0] — 2026-02-27 — AD Integration & Security Enhancements

### Added
- **Active Directory integration** — LDAP authentication, AD group management UI, background sync service
- **AdLoginRateLimiter** — Application-level rate limiting for AD login attempts
- **DatabasePasswordValidator** — Dynamic password policy configurable by SuperAdmin
- **PasswordPolicySetting model** — Single-row table for password complexity rules
- **AdSyncLog model** — History tracking for AD sync operations
- **MustChangePassword middleware** — Forces password change on first login
- **Username recovery** — ForgotUsername flow for users who forgot their credentials
- **User profile page** — Self-service profile viewing
- **Announcement system** — Create, manage, and deliver announcements with read tracking
- **Activity logging** — Comprehensive audit trail for all user actions
- **Print search results** — Printable view for search results
- **Export to CSV** — Download search results as CSV file
- **Bulk operations** — Bulk delete and bulk status update from search results
- **Master data AJAX creation** — Inline "+" buttons to add new dropdown items without leaving the form
- **Dark theme UI** — Modern dark theme with glassmorphism and micro-animations

### Security
- Anti-forgery tokens on all POST forms
- HTTPS redirection + HSTS
- Secure cookies (HttpOnly, SecurePolicy.Always)
- File uploads stored outside web root
- Path traversal protection in FileService
- Double extension rejection for uploads
- Role-based authorization (SuperAdmin, Admin, User)
- Ownership checks on edit/delete operations
- Audit field tampering prevention (CreatedBy/CreatedDateTime preserved from DB)

---

## [1.0.0] — 2026-02-25 — Initial Release

### Added
- Core Lost & Found item management (CRUD)
- User authentication with ASP.NET Core Identity
- Role-based access control
- Master data management (Items, Routes, Vehicles, Storage Locations, Statuses, Found By Names)
- Search with filters and sorting
- Photo and attachment upload/download
- Dashboard with statistics
- MSSQL database with Entity Framework Core
- Database seeding for initial data
