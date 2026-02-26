# 📋 Lost & Found Application — Complete Detailed Report

> **Generated:** 2026-02-26  
> **Tech Stack:** ASP.NET Core 8.0 MVC · Entity Framework Core 8.0 · Microsoft SQL Server · ASP.NET Core Identity · Serilog  
> **Architecture:** Server-side rendered MVC with Razor Views, custom CSS design system, and vanilla JavaScript

---

## Table of Contents

1. [Technology & Architecture Overview](#1-technology--architecture-overview)
2. [Database Schema & Data Models](#2-database-schema--data-models)
3. [Authentication System](#3-authentication-system)
4. [Authorization & Role System](#4-authorization--role-system)
5. [Feature Inventory](#5-feature-inventory)
6. [User Role Deep Dive — Who Can Do What](#6-user-role-deep-dive--who-can-do-what)
7. [Complete Role Permission Matrix](#7-complete-role-permission-matrix)
8. [Services & Business Logic](#8-services--business-logic)
9. [Security Mechanisms](#9-security-mechanisms)
10. [Frontend Architecture](#10-frontend-architecture)
11. [Configuration & Environment](#11-configuration--environment)
12. [File & Directory Structure](#12-file--directory-structure)

---

## 1. Technology & Architecture Overview

### Core Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Framework | ASP.NET Core | 8.0 |
| Language | C# | Latest |
| ORM | Entity Framework Core | 8.0.24 |
| Database | Microsoft SQL Server | Express / LocalDB |
| Identity | ASP.NET Core Identity | 8.0.24 |
| Logging | Serilog | 10.0.0 |
| AD Integration | System.DirectoryServices.AccountManagement | 8.0.0 |
| Frontend | Razor Views + Vanilla CSS + Vanilla JS | — |
| Icons | Bootstrap Icons (CDN) | — |
| Fonts | Google Fonts (Inter) | — |

### Architecture Pattern

- **MVC (Model-View-Controller)** — server-side rendered, no SPA framework
- **Repository-less** — controllers interact directly with `ApplicationDbContext` (EF Core)
- **Service layer** for cross-cutting concerns: `FileService`, `AdSyncService`, `ActivityLogService`
- **Middleware** for password change enforcement (`MustChangePasswordMiddleware`)
- **Background hosted service** for scheduled AD sync (`AdSyncHostedService`)

### Project Structure

```
LostAndFoundApp/
├── Controllers/          6 controllers (Account, Home, Logs, LostFoundItem, MasterData, UserManagement)
├── Data/                 DbContext + DbInitializer (seeding)
├── Middleware/            MustChangePassword middleware
├── Migrations/           3 EF Core migration files
├── Models/               7 entity models
├── Services/             4 services (ActivityLog, AdSync, AdSyncHosted, File)
├── ViewModels/           5 ViewModel files (Account, Dashboard, Log, LostFoundItem, UserManagement)
├── Views/                35 Razor views across 7 folders + shared layout
├── wwwroot/              Static assets (1 CSS file: 27KB, 1 JS file: 4KB)
├── Program.cs            Application entry point & DI configuration
├── appsettings.json      Configuration
├── .env.example          Environment variable template
└── LostAndFoundApp.csproj  Project definition
```

---

## 2. Database Schema & Data Models

### 2.1 Primary Tracking Table: `LostFoundItem`

The central entity. Tracks every lost & found item with full audit trail.

| Column | Type | Required | Description |
|--------|------|----------|-------------|
| `TrackingId` | `int` (auto-increment PK) | ✅ | Unique identifier, auto-generated |
| `DateFound` | `DateTime` | ✅ | When the item was found (cannot be future date) |
| `ItemId` | `int` (FK → Item) | ✅ | Type of item (from master data) |
| `Description` | `string(500)` | ❌ | Free-text description |
| `LocationFound` | `string(300)` | ✅ | Where the item was found |
| `RouteId` | `int?` (FK → Route) | ❌ | Route number (from master data) |
| `VehicleId` | `int?` (FK → Vehicle) | ❌ | Vehicle number (from master data) |
| `PhotoPath` | `string(500)` | ❌ | Stored photo file name (GUID-based) |
| `StorageLocationId` | `int?` (FK → StorageLocation) | ❌ | Where item is stored (from master data) |
| `StatusId` | `int` (FK → Status) | ✅ | Current status (from master data) |
| `StatusDate` | `DateTime?` | ❌ | Date of status change |
| `FoundById` | `int?` (FK → FoundByName) | ❌ | Who found the item (from master data) |
| `ClaimedBy` | `string(200)` | ❌ | Name of person who claimed the item |
| `CreatedBy` | `string(256)` | Auto | Username who created (auto-populated, never editable) |
| `CreatedDateTime` | `DateTime` | Auto | UTC timestamp of creation (auto-populated, DB default: `GETUTCDATE()`) |
| `ModifiedBy` | `string(256)` | Auto | Username who last edited (auto-populated on edit) |
| `ModifiedDateTime` | `DateTime?` | Auto | UTC timestamp of last edit (auto-populated on edit) |
| `Notes` | `string(1000)` | ❌ | Free-text notes |
| `AttachmentPath` | `string(500)` | ❌ | Stored attachment file name (GUID-based) |
| `DaysSinceFound` | `int` (computed) | N/A | `[NotMapped]` — calculated at read time: `(Today - DateFound).Days` |

**Indexes:** `DateFound`, `StatusId`  
**FK Behavior:**
- `ItemId`, `StatusId` → `Restrict` (cannot delete master data in use)
- `RouteId`, `VehicleId`, `StorageLocationId`, `FoundById` → `SetNull` (set to null if master data deleted)

---

### 2.2 Master Data Tables (6 tables)

All six master data tables share the same schema pattern:

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `int` (auto-increment PK) | Unique identifier |
| `Name` | `string` (required, unique index) | Display name |
| `IsActive` | `bool` (default: `true`) | Soft-delete/deactivation flag |

**The 6 master data entities:**

| # | Entity | `Name` Max Length | Purpose |
|---|--------|-------------------|---------|
| 1 | `Item` | 200 | Types of lost/found items (e.g., "Wallet", "Phone", "Keys") |
| 2 | `Route` | 100 | Route numbers for transit/transportation context |
| 3 | `Vehicle` | 100 | Vehicle numbers/identifiers |
| 4 | `StorageLocation` | 200 | Physical locations where items are stored |
| 5 | `Status` | 100 | Item statuses (e.g., "Found", "Claimed", "Stored", "Disposed", "Transferred") |
| 6 | `FoundByName` | 200 | Names of people who found items |

---

### 2.3 `ApplicationUser` (extends ASP.NET Core `IdentityUser`)

| Column | Type | Description |
|--------|------|-------------|
| *(all IdentityUser fields)* | — | Id, UserName, Email, PasswordHash, etc. |
| `DisplayName` | `string(200)` | Human-readable display name |
| `IsAdUser` | `bool` (default: `false`) | `true` if synced from Active Directory |
| `MustChangePassword` | `bool` (default: `false`) | `true` forces password change on next login (local users only) |
| `IsActive` | `bool` (default: `true`) | Deactivated users cannot log in |
| `SamAccountName` | `string(256)` | AD SAM Account Name for AD-synced users |

---

### 2.4 `AdGroup` (Active Directory Group Mapping)

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `int` (PK) | Unique identifier |
| `GroupName` | `string(256)` (required, unique) | AD security group name |
| `MappedRole` | `string(50)` (required, default: `"User"`) | Application role assigned to members (`"Admin"` or `"User"`) |
| `DateAdded` | `DateTime` (default: `GETUTCDATE()`) | When the group was added |
| `IsActive` | `bool` (default: `true`) | Whether this group is actively synced |

---

### 2.5 `ActivityLog` (Audit Trail)

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `int` (PK) | Unique identifier |
| `Timestamp` | `DateTime` (required) | UTC timestamp of action |
| `Action` | `string(100)` (required) | Short action name (e.g., "Login", "Create Record", "AD Sync") |
| `Details` | `string(2000)` (required) | Detailed description of what happened |
| `PerformedBy` | `string(256)` (required) | Username who performed the action |
| `Category` | `string(50)` (required) | Filter category: `Auth`, `ADSync`, `UserManagement`, `MasterData`, `Items`, `System` |
| `IpAddress` | `string(50)` | Client IP address for security auditing |
| `Status` | `string(20)` (default: `"Success"`) | `"Success"` or `"Failed"` |

**Indexes:** `Timestamp`, `Category`, `PerformedBy`

---

## 3. Authentication System

### 3.1 Login Flow (`AccountController`)

The application supports **two authentication methods**:

#### Local User Authentication
1. User submits username + password on `/Account/Login`
2. System checks if user exists → if not, logs "Login Failed" and shows generic error
3. System checks `IsActive` → if `false`, logs "Login Blocked" and shows "account deactivated" message
4. System checks `IsAdUser` → if `false`, uses Identity `PasswordSignInAsync` with lockout
5. On success → logs "Login" activity and redirects to dashboard
6. On failure → logs "Login Failed" with details
7. On lockout → logs "Account Locked" and shows lockout message

#### Active Directory Authentication
1. If `IsAdUser == true`, system calls `AdSyncService.ValidateAdCredentials()` 
2. Credentials are validated against AD in real-time using `System.DirectoryServices.AccountManagement`
3. AD credentials are **never stored locally** — only validated at login time
4. On success → calls `SignInManager.SignInAsync()` and logs "AD Login"
5. On failure → logs "AD Login Failed"

### 3.2 Password Policy

Configured in `Program.cs` via ASP.NET Core Identity:

| Policy | Value |
|--------|-------|
| Require Digit | ✅ Yes |
| Require Lowercase | ✅ Yes |
| Require Uppercase | ✅ Yes |
| Require Non-Alphanumeric | ✅ Yes |
| Minimum Length | 8 characters |

### 3.3 Account Lockout Policy

Configurable via `appsettings.json` → `Identity` section:

| Setting | Default Value |
|---------|--------------|
| Max Failed Attempts | 5 |
| Lockout Duration | 15 minutes |
| Allow Lockout for New Users | ✅ Yes |

### 3.4 Session / Cookie Configuration

| Setting | Value |
|---------|-------|
| Login Path | `/Account/Login` |
| Logout Path | `/Account/Logout` |
| Access Denied Path | `/Account/AccessDenied` |
| Cookie Expiry | 8 hours |
| Sliding Expiration | ✅ Yes |
| HttpOnly Cookie | ✅ Yes |
| Secure Policy | SameAsRequest |
| SameSite | Lax |

### 3.5 First-Login Password Change

- **Middleware:** `MustChangePasswordMiddleware` runs after authentication
- **Behavior:** If `user.MustChangePassword == true` and `user.IsAdUser == false`:
  - All page requests → redirected to `/Account/ChangePassword`
  - AJAX requests → returns `401` JSON: `{"success":false,"message":"You must change your password before continuing.","redirect":"/Account/ChangePassword"}`
- **Exemptions:** AD users are exempt (they manage passwords through their organization)
- **Allowed paths during forced change:** `/Account/ChangePassword`, `/Account/Logout`, `/Account/Login`, `/Account/AccessDenied`, `/Home/Error`, static files (`/css/`, `/js/`, `/lib/`, `/images/`, `/favicon`), photo/attachment endpoints

### 3.6 Change Password (`AccountController.ChangePassword`)

- Available to all authenticated local users
- AD users are blocked with message: "Active Directory users must change their password through their organization's password management system."
- Requires current password + new password + confirm password
- New password must meet the Identity password policy
- On success: sets `MustChangePassword = false`, refreshes sign-in, logs activity

### 3.7 Logout

- POST-only with anti-forgery token
- Logs "Logout" activity with username
- Redirects to login page

---

## 4. Authorization & Role System

### 4.1 Three Roles

| Role | Level | Description |
|------|-------|-------------|
| **SuperAdmin** | Highest | Full system control — IT administrator |
| **Admin** | Middle | Day-to-day operations manager — supervisor/team lead |
| **User** | Basic | Regular staff — finds and registers items |

### 4.2 Authorization Policies

Defined in `Program.cs`:

```
RequireSuperAdmin     → Role: "SuperAdmin" only
RequireAdminOrAbove   → Role: "SuperAdmin" OR "Admin"
RequireAnyRole        → Role: "SuperAdmin" OR "Admin" OR "User"
```

### 4.3 Default Seeded Accounts

Created by `DbInitializer.SeedAsync()` on first run:

| Username | Password | Role | Must Change Password |
|----------|----------|------|---------------------|
| `superadmin` | `SuperAdmin123!` | SuperAdmin | ✅ Yes |
| `admin` | `Admin123!` | Admin | ✅ Yes |
| `user` | `User123!` | User | ✅ Yes |

All three accounts have `IsActive = true`, `IsAdUser = false`, `EmailConfirmed = true`.

---

## 5. Feature Inventory

### 5.1 Dashboard (`HomeController.Index` → `Views/Home/Index.cshtml`)

**Route:** `GET /` or `GET /Home/Index`  
**Auth:** All authenticated users (`[Authorize]`)

#### Data Shown to ALL Roles:
- Welcome message with user display name and role
- **Status summary cards:** Total Items, Found count, Claimed count, Stored count, Disposed count, Transferred count
- **Recent records table:** Last 10 records (for Users) or 15 records (for Admin/SuperAdmin) showing TrackingId, DateFound, Item, Location, Status, Days Since Found, Claimed By, Created By

#### Additional Data for Admin + SuperAdmin:
- **User statistics:** Total Users, Active Users, Inactive Users, Local Users, AD Users, AD Group Count
- **Role distribution:** SuperAdmin count, Admin count, User count
- **Time-based stats:** Items This Week, Items This Month, Unclaimed Over 30 Days
- **Master data counts:** Items, Routes, Vehicles, Storage Locations, Statuses, Found By Names
- **Items Awaiting Action:** Count of items with status "Found" or "Stored"
- **Status Breakdown:** Pie/bar chart data with status name, count, percentage
- **Top 5 Item Types:** Most frequently found item types by count

---

### 5.2 Lost & Found Item Management (`LostFoundItemController`)

**Auth:** All authenticated users (`[Authorize]` on controller)

#### 5.2.1 Create Record
- **Route:** `GET /LostFoundItem/Create` (form), `POST /LostFoundItem/Create` (submit)
- **Access:** All authenticated users
- **Form Fields:** Date Found, Item Type (dropdown), Description, Location Found, Route # (dropdown), Vehicle # (dropdown), Storage Location (dropdown), Status (dropdown), Status Date, Found By (dropdown), Claimed By, Notes, Photo Upload, Attachment Upload
- **Dropdowns:** Populated with **active-only** master data items
- **Auto-populated:** `CreatedBy` = current username, `CreatedDateTime` = UTC now
- **File upload limits:** 15MB total form size, 10MB per file
- **Photo validation:** .jpg, .jpeg, .png, .gif only
- **Attachment validation:** .pdf, .doc, .docx, .xls, .xlsx, .txt, .jpg, .jpeg, .png only
- **Date validation:** `[NotFutureDate]` custom attribute prevents future dates
- **On success:** Redirects to Details page, shows success toast

#### 5.2.2 View Details
- **Route:** `GET /LostFoundItem/Details/{id}`
- **Access:** All authenticated users
- **Displays:** All item fields including read-only audit trail (Created By, Created DateTime, Modified By, Modified DateTime), photo preview, attachment download link, Days Since Found (calculated)

#### 5.2.3 Edit Record
- **Route:** `GET /LostFoundItem/Edit/{id}` (form), `POST /LostFoundItem/Edit/{id}` (submit)
- **Access:** All authenticated users
- **Dropdowns:** Populated with active master data **PLUS** the currently-selected value even if deactivated (prevents losing existing selection)
- **Photo/Attachment:** Can replace existing files; old files are deleted from disk after successful DB save
- **Auto-populated on save:** `ModifiedBy` = current username, `ModifiedDateTime` = UTC now
- **`CreatedBy` and `CreatedDateTime` are never modified**

#### 5.2.4 Delete Record
- **Route:** `POST /LostFoundItem/Delete/{id}`
- **Access:** Admin and SuperAdmin only (`[Authorize(Policy = "RequireAdminOrAbove")]`)
- **Behavior:** Permanently removes the DB record, then deletes associated photo and attachment files from disk
- **Safety:** Files are deleted AFTER successful DB commit to prevent orphaned records
- **On success:** Redirects to Search page with success message

#### 5.2.5 Search / List Records
- **Route:** `GET /LostFoundItem/Search`
- **Access:** All authenticated users
- **Filters (all optional):** Tracking ID, Date Found From, Date Found To, Item Type, Status, Route #, Vehicle #, Storage Location, Found By
- **Sorting:** By TrackingId, DateFound, ItemName, StatusName, LocationFound — ascending or descending
- **Pagination:** 25 records per page with page navigation
- **Filter Summary:** Human-readable summary of applied filters (for display and print)
- **Dropdowns:** Show ALL master data items (including inactive) for filtering historical records

#### 5.2.6 Print Search Results
- **Route:** `GET /LostFoundItem/PrintSearch`
- **Access:** All authenticated users
- **Behavior:** Same filters as Search but returns ALL matching results (no pagination) in a printer-friendly layout
- **Shows:** Filter summary header + full results table

#### 5.2.7 Photo Streaming
- **Route:** `GET /LostFoundItem/Photo/{fileName}`
- **Access:** All authenticated users
- **Behavior:** Securely streams photo file from `SecureStorage/Photos/` directory
- **Fallback:** Returns 1×1 transparent PNG if file not found (prevents broken image icons)

#### 5.2.8 Attachment Download
- **Route:** `GET /LostFoundItem/Attachment/{fileName}`
- **Access:** All authenticated users
- **Behavior:** Securely streams attachment with proper `Content-Disposition` for download
- **Fallback:** Redirects to Search with error message if file not found

---

### 5.3 Master Data Management (`MasterDataController`)

**Auth:** Controller-level `[Authorize]`, all CRUD actions require `[Authorize(Policy = "RequireAdminOrAbove")]`

Each of the 6 master data tables has **identical CRUD operations:**

#### For Each Table (Items, Routes, Vehicles, StorageLocations, Statuses, FoundByNames):

| Operation | Route | Method | Description |
|-----------|-------|--------|-------------|
| **List** | `GET /MasterData/{TableName}` | GET | Shows all entries ordered by Name, with active/inactive status |
| **Create (page)** | `GET /MasterData/Create{Entity}` | GET | Shows creation form |
| **Create (submit)** | `POST /MasterData/Create{Entity}` | POST | Creates new entry; validates uniqueness; logs activity |
| **Edit (page)** | `GET /MasterData/Edit{Entity}/{id}` | GET | Shows edit form pre-filled with current data |
| **Edit (submit)** | `POST /MasterData/Edit{Entity}` | POST | Updates name and IsActive; validates uniqueness; logs activity |
| **Delete** | `POST /MasterData/Delete{Entity}/{id}` | POST | Permanently deletes; **blocks if in use** by any LostFoundItem with error message; logs activity |
| **Toggle Active** | `POST /MasterData/Toggle{Entity}Active/{id}` | POST | Flips `IsActive` flag (soft deactivation); shows success toast |

**In-Use Protection:** Delete operations check if any `LostFoundItem` references the master data entry. If so, deletion is blocked with message: _"Cannot delete '{Name}' because it is in use by existing records. Deactivate it instead."_

#### AJAX Inline Creation Endpoints

For each of the 6 tables, there are AJAX POST endpoints for creating new entries directly from the item form dropdowns:

| Endpoint | Route | Auth |
|----------|-------|------|
| `AddItemAjax` | `POST /MasterData/AddItemAjax` | Admin+ |
| `AddRouteAjax` | `POST /MasterData/AddRouteAjax` | Admin+ |
| `AddVehicleAjax` | `POST /MasterData/AddVehicleAjax` | Admin+ |
| `AddStorageLocationAjax` | `POST /MasterData/AddStorageLocationAjax` | Admin+ |
| `AddStatusAjax` | `POST /MasterData/AddStatusAjax` | Admin+ |
| `AddFoundByNameAjax` | `POST /MasterData/AddFoundByNameAjax` | Admin+ |

**Behavior:**
- Accepts JSON body `{ "name": "..." }`
- Returns existing entry if name already exists (deduplication)
- Creates new entry if name is unique
- Returns `{ "success": true, "id": ..., "name": "..." }` or `{ "success": false, "message": "..." }`
- All protected by `[ValidateAntiForgeryToken]`

---

### 5.4 User Management (`UserManagementController`)

**Auth:** Controller-level `[Authorize(Policy = "RequireAdminOrAbove")]`

#### 5.4.1 User List
- **Route:** `GET /UserManagement`
- **Access:** Admin and SuperAdmin
- **Shows:** Username, Display Name, Email, Role, Account Type (Local / Active Directory), Active Status
- **Sorted by:** Username alphabetically

#### 5.4.2 Create User
- **Route:** `GET /UserManagement/Create` (form), `POST /UserManagement/Create` (submit)
- **Access:** SuperAdmin only (`[Authorize(Policy = "RequireSuperAdmin")]`)
- **Form Fields:** Username (3–50 chars), Email, Display Name, Password (8+ chars with complexity), Role (dropdown: SuperAdmin/Admin/User)
- **Validation:** Username uniqueness, server-side role whitelist validation (prevents arbitrary role injection from crafted POST)
- **Defaults:** `IsAdUser = false`, `MustChangePassword = true`, `IsActive = true`, `EmailConfirmed = true`
- **Logs:** "Create User" activity

#### 5.4.3 Edit Role
- **Route:** `GET /UserManagement/EditRole/{userId}`, `POST /UserManagement/EditRole`
- **Access:** SuperAdmin only
- **Behavior:** Removes all current roles, assigns the new single role
- **Validation:** Server-side role whitelist (SuperAdmin, Admin, User only)
- **Logs:** "Change Role" activity with old and new role

#### 5.4.4 Toggle Active (Activate/Deactivate)
- **Route:** `POST /UserManagement/ToggleActive/{id}`
- **Access:** SuperAdmin only
- **Safety check:** Cannot deactivate your own account (shows error)
- **Effect:** Flips `IsActive` flag. Deactivated users are blocked at login
- **Logs:** "Toggle User Active" activity

---

### 5.5 Active Directory Sync (`UserManagementController` + `AdSyncService`)

**Auth:** All AD features require SuperAdmin (`[Authorize(Policy = "RequireSuperAdmin")]`)

#### 5.5.1 AD Groups Management Page
- **Route:** `GET /UserManagement/AdGroups`
- **Shows:** All configured AD groups with name, mapped role, active status, date added
- **Shows:** AD integration status (enabled/disabled) and configured domain

#### 5.5.2 Add AD Group
- **Route:** `POST /UserManagement/AddAdGroup`
- **Parameters:** Group name, mapped role (Admin or User — **not** SuperAdmin for safety)
- **Validation:** Group name uniqueness, role validation (Admin/User only)
- **Logs:** "Add AD Group" activity

#### 5.5.3 Update AD Group Role
- **Route:** `POST /UserManagement/UpdateAdGroupRole`
- **Behavior:** Changes the mapped application role for an existing AD group
- **Validation:** Role must be Admin or User
- **Logs:** "Update AD Group Role" activity

#### 5.5.4 Toggle AD Group Active
- **Route:** `POST /UserManagement/ToggleAdGroupActive/{id}`
- **Behavior:** Activates/deactivates AD group for sync
- **Logs:** "Toggle AD Group" activity

#### 5.5.5 Remove AD Group
- **Route:** `POST /UserManagement/RemoveAdGroup/{id}`
- **Behavior:** Permanently deletes the AD group configuration
- **Logs:** "Remove AD Group" activity

#### 5.5.6 Manual Sync Now
- **Route:** `POST /UserManagement/SyncNow`
- **Behavior:** Triggers immediate AD synchronization
- **Logs:** "Manual AD Sync" activity with full summary

#### 5.5.7 AD Sync Engine (`AdSyncService.SyncUsersAsync`)

**Full sync behavior:**

1. Reads all active `AdGroup` configurations from database
2. Connects to AD using configured domain, container, and SSL settings
3. For each active AD group:
   - Looks up the group in AD via `GroupPrincipal.FindByIdentity`
   - Enumerates all members (recursive)
   - Extracts `SamAccountName`, `DisplayName`, `Email`
   - Tracks highest-priority role if user appears in multiple groups (Admin > User)
4. For each discovered AD user:
   - **If new:** Creates local `ApplicationUser` with `IsAdUser=true`, `MustChangePassword=false`, assigned mapped role
   - **If existing:** Updates DisplayName, Email, reactivates if previously deactivated, updates role if changed
5. **Deactivation safety:** AD users no longer in ANY configured group are deactivated — but ONLY if ALL groups were processed successfully. If any group had an error, deactivation is skipped entirely to prevent false deactivations.

#### 5.5.8 Scheduled Daily Sync (`AdSyncHostedService`)

- **Type:** `BackgroundService` (ASP.NET Core Hosted Service)
- **Schedule:** Runs once daily at configurable hour (default: 2 AM UTC)
- **Config:** `ActiveDirectory:DailySyncHourUtc` in `appsettings.json`
- **Enabled only if:** `ActiveDirectory:Enabled = true` in config
- **Error recovery:** On failure, retries in 1 hour
- **Logs:** "Scheduled AD Sync" activity

---

### 5.6 Activity Logs (`LogsController`)

**Auth:** All authenticated users (`[Authorize]`)

#### 5.6.1 View Logs
- **Route:** `GET /Logs`
- **Access:**
  - **User role:** Can only see their own logs (filtered by `PerformedBy == currentUser`)
  - **Admin, SuperAdmin:** Can see all logs from all users
- **Filters:** Category dropdown, free-text search (searches Action, Details, PerformedBy), Date From, Date To
- **Pagination:** 50 records per page
- **Columns:** Timestamp, Action, Details, Performed By, Category, IP Address, Status

#### 5.6.2 Export Logs (CSV)
- **Route:** `GET /Logs/Export`
- **Access:** SuperAdmin only (`[Authorize(Policy = "RequireSuperAdmin")]`)
- **Behavior:** Exports filtered logs as CSV file download
- **CSV columns:** Timestamp, Action, Details, Performed By, Category, IP Address, Status
- **Security:** CSV injection protection — values starting with `=`, `+`, `-`, `@` are prefixed with `'` to prevent formula injection in Excel
- **Logs:** "Export Logs" activity

#### 5.6.3 Clear All Logs
- **Route:** `POST /Logs/Clear`
- **Access:** SuperAdmin only (`[Authorize(Policy = "RequireSuperAdmin")]`)
- **Behavior:** Permanently deletes all activity logs from database (uses `ExecuteDeleteAsync` for bulk delete)
- **After clearing:** Logs the clear action itself (so there's always at least one log entry)
- **Logs:** "Clear Logs" activity with count of deleted records

---

### 5.7 Error Handling

- **Route:** `GET /Home/Error?statusCode={code}`
- **Auth:** `[AllowAnonymous]` — accessible even if not logged in
- **Mechanism:** `UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}")` in pipeline
- **Friendly messages for:** 400, 403, 404, 405, 408, 500, 503
- **Each error has:** Title, Message, Icon (Bootstrap Icons), Icon Color

---

## 6. User Role Deep Dive — Who Can Do What

### 🔴 SuperAdmin — Complete System Control

**Default Account:** `superadmin` / `SuperAdmin123!`

The SuperAdmin has **unrestricted access** to every feature in the system. This role is designed for the IT administrator who manages the entire application.

**Everything SuperAdmin can do:**

#### Dashboard
- ✅ View welcome message with name and role
- ✅ View status summary cards (Total, Found, Claimed, Stored, Disposed, Transferred)
- ✅ View last 15 recent records (more than regular Users who see 10)
- ✅ View user statistics (Total, Active, Inactive, Local, AD users)
- ✅ View role distribution (SuperAdmin count, Admin count, User count)
- ✅ View AD Groups count
- ✅ View time-based stats (Items This Week, Items This Month, Unclaimed 30+ Days)
- ✅ View master data counts (all 6 tables)
- ✅ View items awaiting action count
- ✅ View status breakdown chart data
- ✅ View top 5 item types

#### Lost & Found Items
- ✅ Create new records (with photo and attachment upload)
- ✅ View details of any record
- ✅ Edit any record (update all fields, replace photo/attachment)
- ✅ **Delete any record permanently** (removes DB record + files from disk)
- ✅ Search/filter records with full filter set
- ✅ Print search results (all matching records, no pagination)
- ✅ View photos and download attachments

#### Master Data Management
- ✅ View all master data tables (Items, Routes, Vehicles, StorageLocations, Statuses, FoundByNames)
- ✅ Create new entries in any master data table
- ✅ Edit entries (name, active status) in any master data table
- ✅ Delete entries from any master data table (blocked if in use)
- ✅ Toggle active/inactive status on any master data entry
- ✅ Use AJAX inline creation from item form dropdowns

#### User Management
- ✅ View list of all users (username, display name, email, role, account type, active status)
- ✅ Create new local user accounts (set username, email, display name, password, role)
- ✅ Change any user's role (SuperAdmin, Admin, or User)
- ✅ Activate/deactivate any user account (cannot deactivate self)

#### Active Directory Integration
- ✅ View AD Groups management page with integration status
- ✅ Add new AD groups with role mapping
- ✅ Update role mapping for existing AD groups
- ✅ Toggle AD groups active/inactive
- ✅ Remove AD groups
- ✅ Trigger manual "Sync Now" to synchronize users from AD

#### Activity Logs
- ✅ View ALL activity logs from all users
- ✅ Filter logs by category, search term, date range
- ✅ **Export logs as CSV** (with formula injection protection)
- ✅ **Clear all logs** (permanent deletion of all log entries)

#### Account
- ✅ Change own password
- ✅ Logout

---

### 🟠 Admin — Day-to-Day Operations Manager

**Default Account:** `admin` / `Admin123!`

The Admin manages the operational aspects of the lost & found system. They have access to most features except user management and AD integration.

**Everything Admin can do:**

#### Dashboard
- ✅ View welcome message with name and role
- ✅ View status summary cards (Total, Found, Claimed, Stored, Disposed, Transferred)
- ✅ View last 15 recent records
- ✅ View user statistics (Total, Active, Inactive, Local, AD users)
- ✅ View role distribution
- ✅ View AD Groups count
- ✅ View time-based stats (Items This Week, Items This Month, Unclaimed 30+ Days)
- ✅ View master data counts
- ✅ View items awaiting action count
- ✅ View status breakdown chart data
- ✅ View top 5 item types

#### Lost & Found Items
- ✅ Create new records (with photo and attachment upload)
- ✅ View details of any record
- ✅ Edit any record
- ✅ **Delete any record permanently**
- ✅ Search/filter records
- ✅ Print search results
- ✅ View photos and download attachments

#### Master Data Management
- ✅ View all master data tables
- ✅ Create new entries
- ✅ Edit entries
- ✅ Delete entries (blocked if in use)
- ✅ Toggle active/inactive status
- ✅ Use AJAX inline creation

#### User Management
- ✅ **View list of all users** (read-only)
- ❌ Cannot create new users
- ❌ Cannot change user roles
- ❌ Cannot activate/deactivate users

#### Active Directory Integration
- ❌ Cannot access AD Groups page
- ❌ Cannot add/edit/remove AD groups
- ❌ Cannot trigger AD sync

#### Activity Logs
- ✅ View ALL activity logs from all users
- ✅ Filter logs by category, search term, date range
- ❌ Cannot export logs as CSV
- ❌ Cannot clear logs

#### Account
- ✅ Change own password
- ✅ Logout

---

### 🟢 User — Regular Staff Member

**Default Account:** `user` / `User123!`

The User is a regular staff member who finds and registers lost items. This is the most basic role for everyday use.

**Everything User can do:**

#### Dashboard
- ✅ View welcome message with name and role
- ✅ View status summary cards (Total, Found, Claimed, Stored, Disposed, Transferred)
- ✅ View last **10** recent records (fewer than Admin/SuperAdmin)
- ❌ Cannot see user statistics
- ❌ Cannot see role distribution
- ❌ Cannot see time-based stats
- ❌ Cannot see master data counts
- ❌ Cannot see items awaiting action
- ❌ Cannot see status breakdown chart
- ❌ Cannot see top item types

#### Lost & Found Items
- ✅ Create new records (with photo and attachment upload)
- ✅ View details of any record
- ✅ Edit any record
- ❌ **Cannot delete any records**
- ✅ Search/filter records
- ✅ Print search results
- ✅ View photos and download attachments

#### Master Data Management
- ❌ Cannot access any master data pages
- ❌ Cannot create, edit, or delete master data
- ❌ Cannot use AJAX inline creation from dropdowns

#### User Management
- ❌ Cannot access user list
- ❌ Cannot create, edit, or manage users

#### Active Directory Integration
- ❌ Cannot access any AD features

#### Activity Logs
- ✅ View **only their own** activity logs
- ✅ Filter their own logs by category, search term, date range
- ❌ Cannot see other users' logs
- ❌ Cannot export logs
- ❌ Cannot clear logs

#### Account
- ✅ Change own password
- ✅ Logout

---

## 7. Complete Role Permission Matrix

### 7.1 Navigation Visibility

| Menu Item | SuperAdmin | Admin | User |
|-----------|:----------:|:-----:|:----:|
| Dashboard | ✅ | ✅ | ✅ |
| New Record | ✅ | ✅ | ✅ |
| Search | ✅ | ✅ | ✅ |
| Master Data (dropdown) | ✅ | ✅ | ❌ Hidden |
| → Items | ✅ | ✅ | ❌ |
| → Routes | ✅ | ✅ | ❌ |
| → Vehicles | ✅ | ✅ | ❌ |
| → Storage Locations | ✅ | ✅ | ❌ |
| → Statuses | ✅ | ✅ | ❌ |
| → Found By Names | ✅ | ✅ | ❌ |
| Admin (dropdown) | ✅ | ✅ (limited) | ❌ Hidden |
| → User Management | ✅ | ✅ Read-only | ❌ |
| → AD Groups | ✅ | ❌ | ❌ |
| → Activity Logs | ✅ | ✅ | ❌ |
| My Logs | N/A (in Admin menu) | N/A (in Admin menu) | ✅ (standalone link) |

### 7.2 API/Controller Action Permissions

| Action | HTTP | Route | SuperAdmin | Admin | User |
|--------|------|-------|:----------:|:-----:|:----:|
| **Dashboard** | GET | `/` | ✅ Full | ✅ Full | ✅ Basic |
| **Login** | GET/POST | `/Account/Login` | 🔓 Public | 🔓 Public | 🔓 Public |
| **Change Password** | GET/POST | `/Account/ChangePassword` | ✅ | ✅ | ✅ |
| **Logout** | POST | `/Account/Logout` | ✅ | ✅ | ✅ |
| **Access Denied** | GET | `/Account/AccessDenied` | 🔓 Public | 🔓 Public | 🔓 Public |
| **Error** | GET | `/Home/Error` | 🔓 Public | 🔓 Public | 🔓 Public |
| **Create Record** | GET/POST | `/LostFoundItem/Create` | ✅ | ✅ | ✅ |
| **View Details** | GET | `/LostFoundItem/Details/{id}` | ✅ | ✅ | ✅ |
| **Edit Record** | GET/POST | `/LostFoundItem/Edit/{id}` | ✅ | ✅ | ✅ |
| **Delete Record** | POST | `/LostFoundItem/Delete/{id}` | ✅ | ✅ | ❌ 403 |
| **Search** | GET | `/LostFoundItem/Search` | ✅ | ✅ | ✅ |
| **Print Search** | GET | `/LostFoundItem/PrintSearch` | ✅ | ✅ | ✅ |
| **View Photo** | GET | `/LostFoundItem/Photo/{name}` | ✅ | ✅ | ✅ |
| **Download Attachment** | GET | `/LostFoundItem/Attachment/{name}` | ✅ | ✅ | ✅ |
| **List Master Data** | GET | `/MasterData/{Table}` | ✅ | ✅ | ❌ 403 |
| **Create Master Data** | GET/POST | `/MasterData/Create{Entity}` | ✅ | ✅ | ❌ 403 |
| **Edit Master Data** | GET/POST | `/MasterData/Edit{Entity}/{id}` | ✅ | ✅ | ❌ 403 |
| **Delete Master Data** | POST | `/MasterData/Delete{Entity}/{id}` | ✅ | ✅ | ❌ 403 |
| **Toggle Master Data** | POST | `/MasterData/Toggle{Entity}Active/{id}` | ✅ | ✅ | ❌ 403 |
| **AJAX Create Master Data** | POST | `/MasterData/Add{Entity}Ajax` | ✅ | ✅ | ❌ 403 |
| **User List** | GET | `/UserManagement` | ✅ | ✅ Read-only | ❌ 403 |
| **Create User** | GET/POST | `/UserManagement/Create` | ✅ | ❌ 403 | ❌ 403 |
| **Edit User Role** | GET/POST | `/UserManagement/EditRole/{id}` | ✅ | ❌ 403 | ❌ 403 |
| **Toggle User Active** | POST | `/UserManagement/ToggleActive/{id}` | ✅ | ❌ 403 | ❌ 403 |
| **AD Groups Page** | GET | `/UserManagement/AdGroups` | ✅ | ❌ 403 | ❌ 403 |
| **Add AD Group** | POST | `/UserManagement/AddAdGroup` | ✅ | ❌ 403 | ❌ 403 |
| **Update AD Group Role** | POST | `/UserManagement/UpdateAdGroupRole` | ✅ | ❌ 403 | ❌ 403 |
| **Toggle AD Group** | POST | `/UserManagement/ToggleAdGroupActive/{id}` | ✅ | ❌ 403 | ❌ 403 |
| **Remove AD Group** | POST | `/UserManagement/RemoveAdGroup/{id}` | ✅ | ❌ 403 | ❌ 403 |
| **Sync Now** | POST | `/UserManagement/SyncNow` | ✅ | ❌ 403 | ❌ 403 |
| **View Logs** | GET | `/Logs` | ✅ All | ✅ All | ✅ Own only |
| **Export Logs** | GET | `/Logs/Export` | ✅ | ❌ 403 | ❌ 403 |
| **Clear Logs** | POST | `/Logs/Clear` | ✅ | ❌ 403 | ❌ 403 |

---

## 8. Services & Business Logic

### 8.1 `ActivityLogService`

**Purpose:** Centralized audit trail for all application operations.

| Method | Description |
|--------|-------------|
| `LogAsync(action, details, performedBy, category, ipAddress?, status)` | Core logging method — writes to `ActivityLogs` table |
| `LogAsync(httpContext, action, details, category, status)` | Convenience overload — auto-extracts username and IP from HttpContext |
| `ClearAllLogsAsync()` | Bulk-deletes all logs using `ExecuteDeleteAsync`; returns count |

**Resilience:** Logging failures are caught and written to Serilog — they never crash the parent operation.  
**Detail truncation:** Details are automatically truncated to 2000 characters.

**Categories used across the application:**
- `Auth` — Login, Logout, Login Failed, AD Login, Account Locked, Change Password
- `ADSync` — Add/Update/Toggle/Remove AD Group, Manual/Scheduled AD Sync
- `UserManagement` — Create User, Change Role, Toggle User Active
- `MasterData` — Create/Edit/Delete Item/Route/Vehicle/StorageLocation/Status/FoundByName
- `Items` — Create/Edit/Delete Record
- `System` — Clear Logs, Export Logs

---

### 8.2 `FileService`

**Purpose:** Secure file upload, download, and deletion with defense-in-depth security.

| Method | Description |
|--------|-------------|
| `SavePhotoAsync(IFormFile)` | Validates & saves photo; returns GUID filename or null |
| `SaveAttachmentAsync(IFormFile)` | Validates & saves attachment; returns GUID filename or null |
| `GetPhoto(fileName)` | Returns FileStream + ContentType for authenticated streaming |
| `GetAttachment(fileName)` | Returns FileStream + ContentType for authenticated download |
| `DeletePhoto(fileName?)` | Deletes photo from disk; safe if null/missing |
| `DeleteAttachment(fileName?)` | Deletes attachment from disk; safe if null/missing |

**Security measures:**
1. **File size validation:** Configurable max (default 10MB)
2. **Extension whitelist:** Only allowed extensions are accepted
3. **Double extension detection:** Rejects files like `malware.exe.jpg`
4. **GUID-based renaming:** All files renamed to `{GUID}{extension}` — prevents overwriting and directory traversal
5. **Path traversal prevention:** `Path.GetFileName()` strips directory components
6. **Defense-in-depth path check:** Ensures resolved path stays within storage directory via `StartsWith` comparison
7. **Files stored outside web root:** `SecureStorage/` directory is not served by `UseStaticFiles()`
8. **Authenticated access only:** Files are served through controller actions behind `[Authorize]`

**Storage paths (configurable):**
- Photos: `./SecureStorage/Photos/`
- Attachments: `./SecureStorage/Attachments/`

---

### 8.3 `AdSyncService`

**Purpose:** Synchronizes users from Active Directory into the local database.

| Method | Description |
|--------|-------------|
| `ValidateAdCredentials(username, password)` | Real-time AD credential validation at login; credentials never stored |
| `SyncUsersAsync()` | Full sync: creates new users, updates existing, deactivates removed |

**Sync details documented in [Section 5.5.7](#557-ad-sync-engine-adsyncservicesyncusersasync)**

**Role priority when user appears in multiple AD groups:** Admin (2) > User (1)  
**AD groups can ONLY map to Admin or User roles — never SuperAdmin** (security design decision)

---

### 8.4 `AdSyncHostedService`

**Purpose:** Background service for automatic daily AD sync.

**Details documented in [Section 5.5.8](#558-scheduled-daily-sync-adsynchosedservice)**

---

## 9. Security Mechanisms

### 9.1 Anti-Forgery (CSRF) Protection

- ✅ `[ValidateAntiForgeryToken]` on **every** POST action (forms and AJAX)
- ✅ Antiforgery header configured: `options.HeaderName = "RequestVerificationToken"` for AJAX requests
- ✅ All forms include `@Html.AntiForgeryToken()` or `asp-antiforgery="true"`

### 9.2 Input Validation

- ✅ Data annotations on all Models and ViewModels (`[Required]`, `[StringLength]`, `[EmailAddress]`, etc.)
- ✅ Custom `[NotFutureDate]` attribute for date validation
- ✅ Server-side role whitelist in `UserManagementController` and `MasterDataController`
- ✅ Duplicate name checks on all master data creation/editing
- ✅ Request size limits: `[RequestFormLimits(MultipartBodyLengthLimit = 15_728_640)]` and `[RequestSizeLimit(15_728_640)]`

### 9.3 File Upload Security

- ✅ Extension whitelist validation
- ✅ File size validation (configurable, default 10MB)
- ✅ Double-extension attack prevention
- ✅ GUID-based file renaming
- ✅ Path traversal prevention (double-check: `Path.GetFileName` + `StartsWith`)
- ✅ Files stored outside web root
- ✅ Authenticated-only file access

### 9.4 Authentication Security

- ✅ Password complexity requirements (digit, lowercase, uppercase, special char, 8+ chars)
- ✅ Account lockout after configurable failed attempts
- ✅ Forced password change on first login
- ✅ AD credentials validated in real-time, never stored
- ✅ Deactivated user login blocking
- ✅ Session expiry (8 hours) with sliding window

### 9.5 Authorization Security

- ✅ Server-side authorization policies on all controller actions
- ✅ Role-based UI rendering (nav items hidden for unauthorized roles)
- ✅ Self-deactivation prevention (cannot deactivate own account)
- ✅ AD group to role mapping restricted to Admin/User (never SuperAdmin)
- ✅ Server-side role whitelist validation on user creation/role change

### 9.6 Error Handling

- ✅ Custom error pages for all HTTP status codes (400, 403, 404, 405, 408, 500, 503)
- ✅ `UseStatusCodePagesWithReExecute` for consistent error presentation
- ✅ Developer exception page in Development environment only
- ✅ No stack traces exposed in production

### 9.7 Logging & Audit

- ✅ Serilog to console and daily rolling files (`Logs/log-{date}.txt`)
- ✅ Activity logs for every significant user action with IP address
- ✅ Failed login tracking with type (unknown user, wrong password, locked out, AD failed)
- ✅ CSV export with formula injection protection

---

## 10. Frontend Architecture

### 10.1 CSS (`wwwroot/css/site.css` — 27KB)

Custom design system built without any CSS framework. Uses:
- **Google Fonts:** Inter (weights: 300, 400, 500, 600, 700, 800)
- **Icons:** Bootstrap Icons (CDN)
- **Design language:** Modern, professional UI with custom variables/tokens

### 10.2 JavaScript (`wwwroot/js/site.js` — 4KB)

Vanilla JavaScript for:
- Navbar toggle (mobile responsive)
- Dropdown menus
- Auto-dismiss alerts/toasts
- Active nav link highlighting
- AJAX inline master data creation from form dropdowns
- Print functionality

### 10.3 Layout (`Views/Shared/_Layout.cshtml`)

- Responsive navbar with role-conditional menu items
- User info display (avatar initial, username)
- Logout button
- Success/Error toast notifications via `TempData`
- Section support for per-page styles (`@RenderSectionAsync("Styles")`) and scripts (`@RenderSectionAsync("Scripts")`)

### 10.4 Views Inventory (35 Razor views)

| Folder | Views | Description |
|--------|-------|-------------|
| `Account/` | `Login.cshtml`, `ChangePassword.cshtml`, `AccessDenied.cshtml` | Authentication pages |
| `Home/` | `Index.cshtml`, `Error.cshtml` | Dashboard and error page |
| `Logs/` | `Index.cshtml` | Activity logs list with filters |
| `LostFoundItem/` | `Create.cshtml`, `Details.cshtml`, `Edit.cshtml`, `Search.cshtml`, `PrintSearch.cshtml` | Item CRUD + search |
| `MasterData/` | 18 views (3 per table × 6 tables: List, Create, Edit) | Master data management |
| `UserManagement/` | `Index.cshtml`, `Create.cshtml`, `EditRole.cshtml`, `AdGroups.cshtml` | User & AD management |
| `Shared/` | `_Layout.cshtml` | Main layout template |
| Root | `_ViewImports.cshtml`, `_ViewStart.cshtml` | Razor configuration |

---

## 11. Configuration & Environment

### 11.1 `appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.\\SQLEXPRESS;Database=LostAndFoundDb;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "ActiveDirectory": {
    "Enabled": false,
    "Domain": "corp.lostandfoundapp.com",
    "Container": "DC=corp,DC=lostandfoundapp,DC=com",
    "UseSSL": true,
    "DailySyncHourUtc": 2
  },
  "FileUpload": {
    "PhotoStoragePath": "./SecureStorage/Photos",
    "AttachmentStoragePath": "./SecureStorage/Attachments",
    "MaxFileSizeBytes": 10485760,
    "AllowedPhotoExtensions": [".jpg", ".jpeg", ".png", ".gif"],
    "AllowedAttachmentExtensions": [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".jpg", ".jpeg", ".png"]
  },
  "Identity": {
    "MaxFailedAccessAttempts": 5,
    "LockoutMinutes": 15
  }
}
```

### 11.2 Environment Variables (`.env.example`)

| Variable | Description | Default |
|----------|-------------|---------|
| `SEED_DATABASE` | Set to `"true"` to seed default data on startup | `true` |
| `ASPNETCORE_ENVIRONMENT` | .NET environment name | `Development` |
| `ConnectionStrings__DefaultConnection` | Override DB connection string | — |
| `ActiveDirectory__Domain` | AD domain name | — |
| `ActiveDirectory__Container` | AD container DN | — |
| `ActiveDirectory__UseSSL` | Use SSL for AD connection | — |
| `FileUpload__PhotoStoragePath` | Override photo storage path | — |
| `FileUpload__AttachmentStoragePath` | Override attachment storage path | — |
| `FileUpload__MaxFileSizeBytes` | Override max file size | — |
| `Identity__MaxFailedAccessAttempts` | Override lockout threshold | — |
| `Identity__LockoutMinutes` | Override lockout duration | — |

### 11.3 Database Initialization

On startup, `Program.cs` automatically:
1. **Applies EF Core migrations** (`context.Database.MigrateAsync()`) — creates/updates all tables
2. **Seeds default data** (if `SEED_DATABASE=true` or `IsDevelopment()`):
   - Creates 3 roles: SuperAdmin, Admin, User
   - Creates 3 default user accounts with `MustChangePassword = true`
   - Seeds default master data entries across all 6 tables
   - Seeds default AD group configurations

---

## 12. File & Directory Structure

```
LostAndFoundApp/
│
├── Controllers/
│   ├── AccountController.cs          (210 lines)  — Login, Logout, ChangePassword, AccessDenied
│   ├── HomeController.cs             (188 lines)  — Dashboard, Error page
│   ├── LogsController.cs             (161 lines)  — View, Export, Clear activity logs
│   ├── LostFoundItemController.cs    (649 lines)  — Full CRUD + Search + Print + File streaming
│   ├── MasterDataController.cs       (719 lines)  — CRUD for 6 tables + Toggle + AJAX
│   └── UserManagementController.cs   (363 lines)  — User CRUD + AD Groups + Sync
│
├── Data/
│   ├── ApplicationDbContext.cs       (161 lines)  — EF Core context with Fluent API
│   └── DbInitializer.cs             (224 lines)  — Seed roles, users, master data
│
├── Middleware/
│   └── MustChangePasswordMiddleware.cs (103 lines) — Force password change on first login
│
├── Migrations/
│   ├── 20250217...Initial.cs                      — Initial schema
│   ├── 20250217...Initial.Designer.cs             — Migration metadata
│   └── ApplicationDbContextModelSnapshot.cs       — Current schema snapshot
│
├── Models/
│   ├── ActivityLog.cs                (53 lines)   — Audit trail entity
│   ├── AdGroup.cs                    (35 lines)   — AD group configuration entity
│   ├── ApplicationUser.cs           (34 lines)   — Extended Identity user
│   ├── ErrorViewModel.cs            (50 lines)   — Error page model with friendly messages
│   ├── LostFoundItem.cs             (121 lines)  — Primary tracking entity
│   ├── MasterDataModels.cs          (83 lines)   — 6 master data entities
│   └── NotFutureDateAttribute.cs    (29 lines)   — Custom validation attribute
│
├── Services/
│   ├── ActivityLogService.cs        (79 lines)   — Centralized activity logging
│   ├── AdSyncHostedService.cs       (101 lines)  — Daily background AD sync
│   ├── AdSyncService.cs             (330 lines)  — AD credential validation + user sync
│   └── FileService.cs              (181 lines)  — Secure file upload/download/delete
│
├── ViewModels/
│   ├── AccountViewModels.cs         (40 lines)   — LoginViewModel, ChangePasswordViewModel
│   ├── DashboardViewModels.cs       (73 lines)   — Dashboard data models
│   ├── LogViewModels.cs             (24 lines)   — LogListViewModel
│   ├── LostFoundItemViewModels.cs   (221 lines)  — Create, Edit, Detail, Search VMs
│   └── UserManagementViewModels.cs  (54 lines)   — UserList, CreateUser, EditRole VMs
│
├── Views/
│   ├── Account/                     (3 views)    — Login, ChangePassword, AccessDenied
│   ├── Home/                        (2 views)    — Dashboard, Error
│   ├── Logs/                        (1 view)     — Activity logs list
│   ├── LostFoundItem/              (5 views)    — Create, Details, Edit, Search, PrintSearch
│   ├── MasterData/                  (18 views)   — 3 views × 6 tables
│   ├── UserManagement/             (4 views)    — Index, Create, EditRole, AdGroups
│   ├── Shared/                      (1 view)     — _Layout.cshtml
│   ├── _ViewImports.cshtml                       — Tag helpers registration
│   └── _ViewStart.cshtml                         — Default layout assignment
│
├── wwwroot/
│   ├── css/site.css                 (27,221 bytes) — Custom design system
│   └── js/site.js                   (4,134 bytes)  — Interactive behaviors
│
├── Properties/
│   └── launchSettings.json                       — VS launch profiles
│
├── .github/workflows/                            — CI/CD workflows
├── .gitignore                                    — Git exclusions
├── .env.example                                  — Environment template
├── appsettings.json                              — Application configuration
├── LostAndFoundApp.csproj                        — Project file
├── Program.cs                       (161 lines)  — App entry point, DI, pipeline
├── README.md                                     — Setup guide
└── DETAILED_REPORT.md                            — This report
```

---

> **Total codebase size:** ~62 files, ~4,600+ lines of C# code, 35 Razor views, 1 CSS file (27KB), 1 JS file (4KB)

---

*End of report.*
