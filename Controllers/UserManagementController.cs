using System.DirectoryServices.AccountManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LostAndFoundApp.Data;
using LostAndFoundApp.Models;
using LostAndFoundApp.Services;
using LostAndFoundApp.ViewModels;

namespace LostAndFoundApp.Controllers
{
    /// <summary>
    /// User management and AD sync.
    /// SuperAdmin and Admin: Full CRUD on users, roles, AD groups, AD sync.
    /// Supervisor: Read-only access to user list (Index only).
    /// User role: No access.
    /// </summary>
    [Authorize(Policy = "RequireSupervisorOrAbove")]
    public class UserManagementController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly AdSyncService _adSyncService;
        private readonly ActivityLogService _activityLogService;
        private readonly IConfiguration _config;
        private readonly ILogger<UserManagementController> _logger;

        // Whitelist of valid roles to prevent arbitrary role assignment via crafted POST requests
        private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "SuperAdmin", "Admin", "Supervisor", "User"
        };

        public UserManagementController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext context,
            AdSyncService adSyncService,
            ActivityLogService activityLogService,
            IConfiguration config,
            ILogger<UserManagementController> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _adSyncService = adSyncService;
            _activityLogService = activityLogService;
            _config = config;
            _logger = logger;
        }

        // GET: /UserManagement
        public async Task<IActionResult> Index(string search, string role, string accountType, string status, int page = 1)
        {
            const int pageSize = 100;

            // Batched query: get all users and roles in single queries instead of N+1
            var users = await _userManager.Users.ToListAsync();

            // Get all user-role mappings in one query
            var userRolePairs = await _context.UserRoles
                .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, RoleName = r.Name })
                .ToListAsync();

            // Build a lookup dictionary for O(1) role lookup
            var userRolesLookup = userRolePairs
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => g.First().RoleName);

            var userList = new List<UserListViewModel>();
            foreach (var user in users)
            {
                userRolesLookup.TryGetValue(user.Id, out var roleName);
                userList.Add(new UserListViewModel
                {
                    Id = user.Id,
                    UserName = user.UserName ?? "",
                    DisplayName = user.DisplayName,
                    Email = user.Email,
                    Role = roleName ?? "None",
                    AccountType = user.IsAdUser ? "Active Directory" : "Local",
                    IsActive = user.IsActive
                });
            }

            // SuperAdmin invisibility: non-SuperAdmin users cannot see SuperAdmin accounts
            if (!User.IsInRole("SuperAdmin"))
            {
                userList = userList.Where(u => !u.Role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Apply filters
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower();
                userList = userList.Where(u =>
                    u.UserName.ToLower().Contains(term) ||
                    (u.DisplayName?.ToLower().Contains(term) ?? false) ||
                    (u.Email?.ToLower().Contains(term) ?? false)
                ).ToList();
            }

            if (!string.IsNullOrWhiteSpace(role) && role != "All")
            {
                userList = userList.Where(u => u.Role.Equals(role, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrWhiteSpace(accountType) && accountType != "All")
            {
                userList = userList.Where(u => u.AccountType.Equals(accountType, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrWhiteSpace(status) && status != "All")
            {
                var isActive = status == "Active";
                userList = userList.Where(u => u.IsActive == isActive).ToList();
            }

            // Pagination
            var totalFiltered = userList.Count;
            var totalPages = (int)Math.Ceiling((double)totalFiltered / pageSize);
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var pagedList = userList
                .OrderBy(u => u.UserName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Store filter values and pagination for the view
            ViewBag.Search = search;
            ViewBag.Role = role;
            ViewBag.AccountType = accountType;
            ViewBag.Status = status;
            ViewBag.TotalCount = userList.Count; // Use filtered count (excludes hidden SuperAdmins)
            ViewBag.FilteredCount = totalFiltered;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            return View(pagedList);
        }

        // GET: /UserManagement/Create
        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public IActionResult Create()
        {
            return View(new CreateUserViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> Create(CreateUserViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Server-side role validation — reject arbitrary role names from crafted POST requests
            if (!ValidRoles.Contains(model.Role))
            {
                ModelState.AddModelError("Role", $"Invalid role '{model.Role}'. Must be Admin, Supervisor, or User.");
                return View(model);
            }

            // SuperAdmin role can only be assigned by SuperAdmin
            if (model.Role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) && !User.IsInRole("SuperAdmin"))
            {
                ModelState.AddModelError("Role", "You do not have permission to assign the SuperAdmin role.");
                return View(model);
            }

            var existing = await _userManager.FindByNameAsync(model.UserName);
            if (existing != null)
            {
                ModelState.AddModelError("UserName", "A user with this username already exists.");
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName = model.UserName,
                Email = model.Email,
                DisplayName = model.DisplayName,
                EmailConfirmed = true,
                IsAdUser = false,
                MustChangePassword = true,
                IsActive = true
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return View(model);
            }

            if (!string.IsNullOrEmpty(model.Role))
            {
                await _userManager.AddToRoleAsync(user, model.Role);
            }

            await _activityLogService.LogAsync(HttpContext, "Create User",
                $"Created local user '{model.UserName}' with role '{model.Role}'.", "UserManagement");
            _logger.LogInformation("Super Admin created local user '{User}' with role '{Role}'.", model.UserName, model.Role);
            TempData["SuccessMessage"] = $"User '{model.UserName}' created successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /UserManagement/EditRole/userId
        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> EditRole(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var roles = await _userManager.GetRolesAsync(user);

            var model = new EditUserRoleViewModel
            {
                UserId = user.Id,
                UserName = user.UserName ?? "",
                DisplayName = user.DisplayName,
                CurrentRole = roles.FirstOrDefault() ?? "None",
                NewRole = roles.FirstOrDefault() ?? "User"
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> EditRole(EditUserRoleViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Server-side role validation — reject arbitrary role names from crafted POST requests
            if (!ValidRoles.Contains(model.NewRole))
            {
                ModelState.AddModelError("NewRole", $"Invalid role '{model.NewRole}'. Must be SuperAdmin, Admin, or User.");
                return View(model);
            }

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            // SuperAdmin protection: non-SuperAdmin cannot edit SuperAdmin users
            var currentRoles = await _userManager.GetRolesAsync(user);
            var actualCurrentRole = currentRoles.FirstOrDefault() ?? "None";

            if (actualCurrentRole.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) && !User.IsInRole("SuperAdmin"))
            {
                TempData["ErrorMessage"] = "You do not have permission to modify SuperAdmin users.";
                return RedirectToAction(nameof(Index));
            }

            // SuperAdmin role can only be assigned by SuperAdmin
            if (model.NewRole.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) && !User.IsInRole("SuperAdmin"))
            {
                ModelState.AddModelError("NewRole", "You do not have permission to assign the SuperAdmin role.");
                return View(model);
            }

            if (currentRoles.Any())
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
            }

            await _userManager.AddToRoleAsync(user, model.NewRole);

            await _activityLogService.LogAsync(HttpContext, "Change Role",
                $"Role for user '{user.UserName}' changed from '{actualCurrentRole}' to '{model.NewRole}'.", "UserManagement");
            _logger.LogInformation("Role for user '{User}' changed from '{OldRole}' to '{NewRole}'.",
                user.UserName, actualCurrentRole, model.NewRole);
            TempData["SuccessMessage"] = $"Role for '{user.UserName}' changed to '{model.NewRole}'.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> ToggleActive(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            // SuperAdmin protection
            var userRoles = await _userManager.GetRolesAsync(user);
            if (userRoles.Contains("SuperAdmin") && !User.IsInRole("SuperAdmin"))
            {
                TempData["ErrorMessage"] = "You do not have permission to modify SuperAdmin users.";
                return RedirectToAction(nameof(Index));
            }

            if (user.UserName == User.Identity?.Name)
            {
                TempData["ErrorMessage"] = "You cannot deactivate your own account.";
                return RedirectToAction(nameof(Index));
            }

            user.IsActive = !user.IsActive;
            await _userManager.UpdateAsync(user);

            var status = user.IsActive ? "activated" : "deactivated";
            await _activityLogService.LogAsync(HttpContext, "Toggle User Active",
                $"User '{user.UserName}' has been {status}.", "UserManagement");
            _logger.LogInformation("User '{User}' has been {Status}.", user.UserName, status);
            TempData["SuccessMessage"] = $"User '{user.UserName}' has been {status}.";
            return RedirectToAction(nameof(Index));
        }

        // =====================================================================
        // ADMIN PASSWORD RESET — SuperAdmin can reset any local user's password
        // =====================================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> ResetPassword(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            // SuperAdmin protection
            var userRoles = await _userManager.GetRolesAsync(user);
            if (userRoles.Contains("SuperAdmin") && !User.IsInRole("SuperAdmin"))
            {
                TempData["ErrorMessage"] = "You do not have permission to reset SuperAdmin passwords.";
                return RedirectToAction(nameof(Index));
            }

            if (user.IsAdUser)
            {
                TempData["ErrorMessage"] = "Cannot reset password for Active Directory users. They must use their organization's password management.";
                return RedirectToAction(nameof(Index));
            }

            // Generate a strong temporary password
            var tempPassword = GenerateTempPassword();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, tempPassword);

            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                TempData["ErrorMessage"] = $"Failed to reset password: {errors}";
                return RedirectToAction(nameof(Index));
            }

            user.MustChangePassword = true;
            await _userManager.UpdateAsync(user);

            await _activityLogService.LogAsync(HttpContext, "Reset Password",
                $"Password reset for user '{user.UserName}' by administrator.", "UserManagement");
            _logger.LogInformation("Password reset for user '{User}' by '{Admin}'.", user.UserName, User.Identity?.Name);
            TempData["SuccessMessage"] = $"Password for '{user.UserName}' has been reset to: {tempPassword}";
            return RedirectToAction(nameof(Index));
        }

        // =====================================================================
        // DELETE USER — SuperAdmin can permanently remove a user
        // =====================================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            // SuperAdmin protection
            var userRoles = await _userManager.GetRolesAsync(user);
            if (userRoles.Contains("SuperAdmin") && !User.IsInRole("SuperAdmin"))
            {
                TempData["ErrorMessage"] = "You do not have permission to delete SuperAdmin users.";
                return RedirectToAction(nameof(Index));
            }

            if (user.UserName == User.Identity?.Name)
            {
                TempData["ErrorMessage"] = "You cannot delete your own account.";
                return RedirectToAction(nameof(Index));
            }

            var userName = user.UserName;

            // Clean up AnnouncementReads to prevent orphaned records
            await _context.AnnouncementReads.Where(r => r.UserId == user.Id).ExecuteDeleteAsync();

            var result = await _userManager.DeleteAsync(user);

            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                TempData["ErrorMessage"] = $"Failed to delete user: {errors}";
                return RedirectToAction(nameof(Index));
            }

            await _activityLogService.LogAsync(HttpContext, "Delete User",
                $"User '{userName}' permanently deleted.", "UserManagement");
            _logger.LogInformation("User '{User}' deleted by '{Admin}'.", userName, User.Identity?.Name);
            TempData["SuccessMessage"] = $"User '{userName}' has been permanently deleted.";
            return RedirectToAction(nameof(Index));
        }

        private static string GenerateTempPassword()
        {
            const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string lower = "abcdefghijklmnopqrstuvwxyz";
            const string digits = "0123456789";
            const string special = "!@#$%&*";
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var bytes = new byte[8];
            rng.GetBytes(bytes);
            var chars = new char[12];
            chars[0] = upper[bytes[0] % upper.Length];
            chars[1] = lower[bytes[1] % lower.Length];
            chars[2] = digits[bytes[2] % digits.Length];
            chars[3] = special[bytes[3] % special.Length];
            var all = upper + lower + digits + special;
            for (int i = 4; i < 12; i++)
            {
                rng.GetBytes(bytes);
                chars[i] = all[bytes[0] % all.Length];
            }
            // Shuffle
            for (int i = chars.Length - 1; i > 0; i--)
            {
                rng.GetBytes(bytes);
                int j = bytes[0] % (i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }
            return new string(chars);
        }

        // =====================================================================
        // ACTIVE DIRECTORY GROUP MANAGEMENT WITH ROLE MAPPING
        // =====================================================================

        // GET: /UserManagement/AdGroups
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> AdGroups()
        {
            ViewBag.AdEnabled = _config.GetValue<bool>("ActiveDirectory:Enabled", false);
            ViewBag.AdDomain = _config["ActiveDirectory:Domain"] ?? "(not configured)";

            // Fetch last sync info for display (Gap 2 fix)
            var lastSync = await _context.AdSyncLogs
                .OrderByDescending(l => l.Timestamp)
                .FirstOrDefaultAsync();
            ViewBag.LastSyncTimestamp = lastSync?.Timestamp;
            ViewBag.LastSyncSuccess = lastSync?.Success;
            ViewBag.LastSyncSummary = lastSync != null
                ? $"Created: {lastSync.UsersCreated}, Updated: {lastSync.UsersUpdated}, Deactivated: {lastSync.UsersDeactivated}, Roles: {lastSync.RolesUpdated}"
                : null;
            ViewBag.LastSyncTrigger = lastSync?.TriggerType;
            ViewBag.LastSyncBy = lastSync?.TriggeredBy;
            ViewBag.LastSyncErrors = lastSync?.ErrorSummary;

            var groups = await _context.AdGroups.OrderBy(g => g.GroupName).ToListAsync();
            return View(groups);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> AddAdGroups(string groupNames, string mappedRole)
        {
            if (string.IsNullOrWhiteSpace(groupNames))
            {
                TempData["ErrorMessage"] = "At least one group name is required.";
                return RedirectToAction(nameof(AdGroups));
            }

            // Validate mapped role
            var validRoles = new[] { "Admin", "Supervisor", "User" };
            if (string.IsNullOrWhiteSpace(mappedRole) || !validRoles.Contains(mappedRole))
            {
                TempData["ErrorMessage"] = "Please select a valid role mapping (Admin or User).";
                return RedirectToAction(nameof(AdGroups));
            }

            // Parse comma-separated group names
            var groupNameList = groupNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(g => g.Trim())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!groupNameList.Any())
            {
                TempData["ErrorMessage"] = "No valid group names provided.";
                return RedirectToAction(nameof(AdGroups));
            }

            var addedCount = 0;
            var duplicateCount = 0;
            var errorCount = 0;

            foreach (var groupName in groupNameList)
            {
                try
                {
                    if (await _context.AdGroups.AnyAsync(g => g.GroupName.ToLower() == groupName.ToLower()))
                    {
                        duplicateCount++;
                        continue;
                    }

                    _context.AdGroups.Add(new AdGroup
                    {
                        GroupName = groupName,
                        MappedRole = mappedRole,
                        IsActive = true,
                        DateAdded = DateTime.Now
                    });
                    addedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add AD group '{GroupName}'", groupName);
                    errorCount++;
                }
            }

            if (addedCount > 0)
            {
                await _context.SaveChangesAsync();
                await _activityLogService.LogAsync(HttpContext, "Add AD Groups",
                    $"Added {addedCount} AD group(s) with role mapping '{mappedRole}'.", "ADSync");
            }

            // Build result message
            var messages = new List<string>();
            if (addedCount > 0) messages.Add($"{addedCount} group(s) added");
            if (duplicateCount > 0) messages.Add($"{duplicateCount} duplicate(s) skipped");
            if (errorCount > 0) messages.Add($"{errorCount} error(s)");

            TempData[errorCount > 0 ? "ErrorMessage" : "SuccessMessage"] = string.Join(", ", messages);
            return RedirectToAction(nameof(AdGroups));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> UpdateAdGroupRole(int id, string mappedRole)
        {
            var group = await _context.AdGroups.FindAsync(id);
            if (group == null) return NotFound();

            var validRoles = new[] { "Admin", "Supervisor", "User" };
            if (!validRoles.Contains(mappedRole))
            {
                TempData["ErrorMessage"] = "Invalid role mapping.";
                return RedirectToAction(nameof(AdGroups));
            }

            var oldRole = group.MappedRole;
            group.MappedRole = mappedRole;
            await _context.SaveChangesAsync();

            await _activityLogService.LogAsync(HttpContext, "Update AD Group Role",
                $"AD group '{group.GroupName}' role mapping changed from '{oldRole}' to '{mappedRole}'.", "ADSync");
            _logger.LogInformation("AD group '{GroupName}' role changed from '{OldRole}' to '{NewRole}' by '{User}'.",
                group.GroupName, oldRole, mappedRole, User.Identity?.Name);
            TempData["SuccessMessage"] = $"Role mapping for '{group.GroupName}' updated to '{mappedRole}'.";
            return RedirectToAction(nameof(AdGroups));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> ToggleAdGroupActive(int id)
        {
            var group = await _context.AdGroups.FindAsync(id);
            if (group == null) return NotFound();

            group.IsActive = !group.IsActive;
            await _context.SaveChangesAsync();

            var status = group.IsActive ? "activated" : "deactivated";
            await _activityLogService.LogAsync(HttpContext, "Toggle AD Group",
                $"AD group '{group.GroupName}' has been {status}.", "ADSync");
            TempData["SuccessMessage"] = $"AD group '{group.GroupName}' has been {status}.";
            return RedirectToAction(nameof(AdGroups));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> RemoveAdGroup(int id)
        {
            var group = await _context.AdGroups.FindAsync(id);
            if (group == null) return NotFound();

            _context.AdGroups.Remove(group);
            await _context.SaveChangesAsync();

            await _activityLogService.LogAsync(HttpContext, "Remove AD Group",
                $"AD group '{group.GroupName}' (role: {group.MappedRole}) removed.", "ADSync");
            _logger.LogInformation("AD group '{GroupName}' removed by '{User}'.", group.GroupName, User.Identity?.Name);
            TempData["SuccessMessage"] = $"AD group '{group.GroupName}' removed.";
            return RedirectToAction(nameof(AdGroups));
        }

        // =====================================================================
        // INDIVIDUAL AD USERS MANAGEMENT
        // =====================================================================

        // GET: /UserManagement/AdUsers
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> AdUsers()
        {
            ViewBag.AdEnabled = _config.GetValue<bool>("ActiveDirectory:Enabled", false);
            ViewBag.AdDomain = _config["ActiveDirectory:Domain"] ?? "(not configured)";

            // Fetch last sync info for display
            var lastSync = await _context.AdSyncLogs
                .OrderByDescending(l => l.Timestamp)
                .FirstOrDefaultAsync();
            ViewBag.LastSyncTimestamp = lastSync?.Timestamp;
            ViewBag.LastSyncSuccess = lastSync?.Success;
            ViewBag.LastSyncSummary = lastSync != null
                ? $"Created: {lastSync.UsersCreated}, Updated: {lastSync.UsersUpdated}, Deactivated: {lastSync.UsersDeactivated}, Roles: {lastSync.RolesUpdated}"
                : null;
            ViewBag.LastSyncTrigger = lastSync?.TriggerType;
            ViewBag.LastSyncBy = lastSync?.TriggeredBy;
            ViewBag.LastSyncErrors = lastSync?.ErrorSummary;

            var adUsers = await _context.AdUsers.OrderBy(u => u.Username).ToListAsync();
            return View(adUsers);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> AddAdUser(string username, string mappedRole)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                TempData["ErrorMessage"] = "Username is required.";
                return RedirectToAction(nameof(AdUsers));
            }

            // Validate mapped role
            var validRoles = new[] { "Admin", "Supervisor", "User" };
            if (string.IsNullOrWhiteSpace(mappedRole) || !validRoles.Contains(mappedRole))
            {
                TempData["ErrorMessage"] = "Please select a valid role mapping (Admin, Supervisor, or User).";
                return RedirectToAction(nameof(AdUsers));
            }

            // Check for duplicates
            if (await _context.AdUsers.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
            {
                TempData["ErrorMessage"] = $"User '{username}' is already added.";
                return RedirectToAction(nameof(AdUsers));
            }

            _context.AdUsers.Add(new AdUser
            {
                Username = username.Trim(),
                MappedRole = mappedRole,
                IsActive = true,
                DateAdded = DateTime.Now
            });
            await _context.SaveChangesAsync();

            await _activityLogService.LogAsync(HttpContext, "Add AD User",
                $"Added individual AD user '{username}' with role mapping '{mappedRole}'.", "ADSync");
            _logger.LogInformation("Added individual AD user '{Username}' with role '{Role}'.", username, mappedRole);
            TempData["SuccessMessage"] = $"User '{username}' added successfully with role '{mappedRole}'.";
            return RedirectToAction(nameof(AdUsers));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> UpdateAdUserRole(int id, string mappedRole)
        {
            var adUser = await _context.AdUsers.FindAsync(id);
            if (adUser == null) return NotFound();

            var validRoles = new[] { "Admin", "Supervisor", "User" };
            if (!validRoles.Contains(mappedRole))
            {
                TempData["ErrorMessage"] = "Invalid role mapping.";
                return RedirectToAction(nameof(AdUsers));
            }

            var oldRole = adUser.MappedRole;
            adUser.MappedRole = mappedRole;
            await _context.SaveChangesAsync();

            await _activityLogService.LogAsync(HttpContext, "Update AD User Role",
                $"AD user '{adUser.Username}' role mapping changed from '{oldRole}' to '{mappedRole}'.", "ADSync");
            _logger.LogInformation("AD user '{Username}' role changed from '{OldRole}' to '{NewRole}' by '{User}'.",
                adUser.Username, oldRole, mappedRole, User.Identity?.Name);
            TempData["SuccessMessage"] = $"Role mapping for '{adUser.Username}' updated to '{mappedRole}'.";
            return RedirectToAction(nameof(AdUsers));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> ToggleAdUserActive(int id)
        {
            var adUser = await _context.AdUsers.FindAsync(id);
            if (adUser == null) return NotFound();

            adUser.IsActive = !adUser.IsActive;
            await _context.SaveChangesAsync();

            var status = adUser.IsActive ? "activated" : "deactivated";
            await _activityLogService.LogAsync(HttpContext, "Toggle AD User",
                $"AD user '{adUser.Username}' has been {status}.", "ADSync");
            TempData["SuccessMessage"] = $"AD user '{adUser.Username}' has been {status}.";
            return RedirectToAction(nameof(AdUsers));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> RemoveAdUser(int id)
        {
            var adUser = await _context.AdUsers.FindAsync(id);
            if (adUser == null) return NotFound();

            var username = adUser.Username;
            _context.AdUsers.Remove(adUser);
            await _context.SaveChangesAsync();

            await _activityLogService.LogAsync(HttpContext, "Remove AD User",
                $"Individual AD user '{username}' (role: {adUser.MappedRole}) removed.", "ADSync");
            _logger.LogInformation("Individual AD user '{Username}' removed by '{User}'.", username, User.Identity?.Name);
            TempData["SuccessMessage"] = $"AD user '{username}' has been removed.";
            return RedirectToAction(nameof(AdUsers));
        }

        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public IActionResult SearchAdUsers(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
                return Json(new { results = Array.Empty<object>(), error = "" });

            if (!_config.GetValue<bool>("ActiveDirectory:Enabled", false))
                return Json(new { results = Array.Empty<object>(), error = "Active Directory integration is disabled." });

            var domain = _config["ActiveDirectory:Domain"];
            var container = _config["ActiveDirectory:Container"];
            var useSsl = _config.GetValue<bool>("ActiveDirectory:UseSSL", false);

            if (string.IsNullOrEmpty(domain))
                return Json(new { results = Array.Empty<object>(), error = "AD domain not configured." });

            try
            {
                var options = useSsl
                    ? ContextOptions.Negotiate | ContextOptions.SecureSocketLayer
                    : ContextOptions.Negotiate;
                using var ctx = new PrincipalContext(ContextType.Domain, domain, container, options);

                // Use UserPrincipal query to find users matching by name
                using var queryFilter = new UserPrincipal(ctx) { SamAccountName = $"*{term.Trim()}*" };
                using var searcher = new PrincipalSearcher(queryFilter);

                var results = searcher.FindAll()
                    .Take(15)
                    .OfType<UserPrincipal>()
                    .Select(p => new
                    {
                        name = p.SamAccountName,
                        displayName = p.DisplayName,
                        email = p.EmailAddress
                    })
                    .ToList();

                return Json(new { results, error = "" });
            }
            catch (PrincipalServerDownException)
            {
                _logger.LogWarning("AD server not reachable during user search.");
                return Json(new { results = Array.Empty<object>(), error = "AD server is not reachable." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching AD users for term '{Term}'.", term);
                return Json(new { results = Array.Empty<object>(), error = "Error searching Active Directory." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public IActionResult SyncNow()
        {
            // Fire sync in background to avoid HTTP timeout on large directories (Gap 3 fix)
            var triggeredBy = User.Identity?.Name ?? "Unknown";
            var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var adSync = scope.ServiceProvider.GetRequiredService<AdSyncService>();
                    var activityLog = scope.ServiceProvider.GetRequiredService<ActivityLogService>();
                    var result = await adSync.SyncUsersAsync("Manual", triggeredBy);
                    await activityLog.LogAsync(
                        "Manual AD Sync",
                        result.Summary,
                        triggeredBy,
                        "ADSync",
                        null,
                        result.Success ? "Success" : "Failed");
                }
                catch (Exception ex)
                {
                    var logger = _logger;
                    logger.LogError(ex, "Background AD sync failed.");
                }
            });

            TempData["SuccessMessage"] = "AD sync triggered successfully.";
            return RedirectToAction(nameof(AdGroups));
        }

        // =====================================================================
        // PASSWORD POLICY CONFIGURATION — SuperAdmin only
        // =====================================================================

        // GET: /UserManagement/PasswordPolicy
        [HttpGet]
        [Authorize(Policy = "RequireSuperAdmin")]
        public IActionResult PasswordPolicy()
        {
            // Read current policy from database (Gap 1 fix)
            var policy = _context.PasswordPolicySettings.FirstOrDefault();
            var vm = new PasswordPolicyViewModel
            {
                MinimumLength = policy?.MinimumLength ?? 8,
                RequireDigit = policy?.RequireDigit ?? true,
                RequireLowercase = policy?.RequireLowercase ?? true,
                RequireUppercase = policy?.RequireUppercase ?? true,
                RequireNonAlphanumeric = policy?.RequireNonAlphanumeric ?? true
            };
            return View(vm);
        }

        // POST: /UserManagement/PasswordPolicy
        [HttpPost]
        [Authorize(Policy = "RequireSuperAdmin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PasswordPolicy(PasswordPolicyViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Persist to database (Gap 1 fix)
            var policy = await _context.PasswordPolicySettings.FirstOrDefaultAsync();
            if (policy == null)
            {
                policy = new Models.PasswordPolicySetting();
                _context.PasswordPolicySettings.Add(policy);
            }

            policy.MinimumLength = model.MinimumLength;
            policy.RequireDigit = model.RequireDigit;
            policy.RequireLowercase = model.RequireLowercase;
            policy.RequireUppercase = model.RequireUppercase;
            policy.RequireNonAlphanumeric = model.RequireNonAlphanumeric;

            await _context.SaveChangesAsync();

            await _activityLogService.LogAsync(HttpContext, "Update Password Policy",
                $"Password policy updated: MinLength={model.MinimumLength}, Digit={model.RequireDigit}, Lower={model.RequireLowercase}, Upper={model.RequireUppercase}, NonAlpha={model.RequireNonAlphanumeric}.", "UserManagement");

            TempData["SuccessMessage"] = "Password policy updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /UserManagement/OverdueSettings
        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> OverdueSettings()
        {
            var settings = await _context.OverdueSettings.FirstOrDefaultAsync();
            var vm = new OverdueSettingsViewModel
            {
                ShortOverdueDays = settings?.ShortOverdueDays ?? 7,
                LongOverdueDays  = settings?.LongOverdueDays  ?? 30
            };
            return View(vm);
        }

        // POST: /UserManagement/OverdueSettings
        [HttpPost]
        [Authorize(Policy = "RequireAdminOrAbove")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OverdueSettings(OverdueSettingsViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // bug fix #8: Short must be strictly less than Long
            if (model.ShortOverdueDays >= model.LongOverdueDays)
            {
                ModelState.AddModelError(nameof(model.ShortOverdueDays),
                    $"Short overdue days ({model.ShortOverdueDays}) must be less than long overdue days ({model.LongOverdueDays}).");
                return View(model);
            }

            var settings = await _context.OverdueSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new Models.OverdueSettings();
                _context.OverdueSettings.Add(settings);
            }

            settings.ShortOverdueDays = model.ShortOverdueDays;
            settings.LongOverdueDays  = model.LongOverdueDays;

            await _context.SaveChangesAsync();

            await _activityLogService.LogAsync(HttpContext, "Update Overdue Settings",
                $"Overdue thresholds updated: Short={model.ShortOverdueDays} days, Long={model.LongOverdueDays} days.", "UserManagement");

            TempData["SuccessMessage"] = $"Overdue thresholds updated: {model.ShortOverdueDays} days (short) / {model.LongOverdueDays} days (long).";
            return RedirectToAction(nameof(OverdueSettings));
        }

        // =====================================================================
        // LOG RETENTION SETTINGS — Admin / SuperAdmin configurable
        // =====================================================================

        // GET: /UserManagement/LogRetentionSettings
        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> LogRetentionSettings()
        {
            var settings = await _context.LogRetentionSettings.FirstOrDefaultAsync();
            var vm = new LogRetentionSettingsViewModel
            {
                RetentionDays = settings?.RetentionDays ?? 30,
                LastPurgedAt = settings?.LastPurgedAt,
                LastPurgedCount = settings?.LastPurgedCount
            };
            return View(vm);
        }

        // POST: /UserManagement/LogRetentionSettings
        [HttpPost]
        [Authorize(Policy = "RequireAdminOrAbove")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogRetentionSettings(LogRetentionSettingsViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Server-side validation: only 30, 60, or 90 days allowed
            var allowedValues = new[] { 30, 60, 90 };
            if (!allowedValues.Contains(model.RetentionDays))
            {
                ModelState.AddModelError(nameof(model.RetentionDays),
                    "Retention period must be 30, 60, or 90 days.");
                return View(model);
            }

            var settings = await _context.LogRetentionSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new Models.LogRetentionSettings();
                _context.LogRetentionSettings.Add(settings);
            }

            settings.RetentionDays = model.RetentionDays;
            await _context.SaveChangesAsync();

            await _activityLogService.LogAsync(HttpContext, "Update Log Retention",
                $"Activity log retention period updated to {model.RetentionDays} days.", "UserManagement");

            TempData["SuccessMessage"] = $"Log retention period updated to {model.RetentionDays} days.";
            return RedirectToAction(nameof(LogRetentionSettings));
        }

        // GET: /UserManagement/LogRetentionCount?days=30
        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> LogRetentionCount(int days)
        {
            var allowedValues = new[] { 30, 60, 90 };
            if (!allowedValues.Contains(days))
                return BadRequest(new { error = "Days must be 30, 60, or 90." });

            var cutoff = DateTime.Now.AddDays(-days);
            var count = await _context.ActivityLogs.CountAsync(l => l.Timestamp < cutoff);
            return Json(new { count });
        }

        // =====================================================================
        // ITEM RETENTION SETTINGS — Admin / SuperAdmin configurable
        // =====================================================================

        // GET: /UserManagement/ItemRetentionSettings
        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> ItemRetentionSettings()
        {
            var settings = await _context.ItemRetentionSettings.FirstOrDefaultAsync();
            var vm = new ItemRetentionSettingsViewModel
            {
                RetentionDays = settings?.RetentionDays ?? 365,
                LastPurgedAt = settings?.LastPurgedAt,
                LastPurgedCount = settings?.LastPurgedCount
            };
            return View(vm);
        }

        // POST: /UserManagement/ItemRetentionSettings
        [HttpPost]
        [Authorize(Policy = "RequireAdminOrAbove")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ItemRetentionSettings(ItemRetentionSettingsViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Server-side validation: only 365 or 730 days allowed
            var allowedValues = new[] { 365, 730 };
            if (!allowedValues.Contains(model.RetentionDays))
            {
                ModelState.AddModelError(nameof(model.RetentionDays),
                    "Retention period must be 365 days (1 year) or 730 days (2 years).");
                return View(model);
            }

            var settings = await _context.ItemRetentionSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new Models.ItemRetentionSettings();
                _context.ItemRetentionSettings.Add(settings);
            }

            settings.RetentionDays = model.RetentionDays;
            await _context.SaveChangesAsync();

            var label = model.RetentionDays == 365 ? "1 year" : "2 years";
            await _activityLogService.LogAsync(HttpContext, "Update Item Retention",
                $"Item retention period updated to {model.RetentionDays} days ({label}).", "UserManagement");

            TempData["SuccessMessage"] = $"Item retention period updated to {model.RetentionDays} days ({label}).";
            return RedirectToAction(nameof(ItemRetentionSettings));
        }

        // GET: /UserManagement/ItemRetentionCount?days=365
        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> ItemRetentionCount(int days)
        {
            var allowedValues = new[] { 365, 730 };
            if (!allowedValues.Contains(days))
                return BadRequest(new { error = "Days must be 365 or 730." });

            var cutoff = DateTime.Now.AddDays(-days);
            var count = await _context.LostFoundItems.CountAsync(i => i.CreatedDateTime < cutoff);
            return Json(new { count });
        }

        // POST: /UserManagement/RunLogPurgeNow
        [HttpPost]
        [Authorize(Policy = "RequireAdminOrAbove")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunLogPurgeNow()
        {
            var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
            using var scope = scopeFactory.CreateScope();
            var purgeService = scope.ServiceProvider.GetRequiredService<Services.LogRetentionHostedService>();

            await purgeService.RunPurgeNowAsync();

            // Refresh the settings to get updated LastPurgedAt/LastPurgedCount
            var settings = await _context.LogRetentionSettings.FirstOrDefaultAsync();
            var deletedCount = settings?.LastPurgedCount ?? 0;

            await _activityLogService.LogAsync(HttpContext, "Manual Log Purge",
                $"Manual log retention purge executed. Deleted {deletedCount} record(s).", "UserManagement");

            TempData["SuccessMessage"] = $"Log purge completed. {deletedCount} record(s) deleted.";
            return RedirectToAction(nameof(LogRetentionSettings));
        }

        // POST: /UserManagement/RunItemPurgeNow
        [HttpPost]
        [Authorize(Policy = "RequireAdminOrAbove")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunItemPurgeNow()
        {
            var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
            using var scope = scopeFactory.CreateScope();
            var purgeService = scope.ServiceProvider.GetRequiredService<Services.ItemRetentionHostedService>();

            await purgeService.RunPurgeNowAsync();

            // Refresh the settings to get updated LastPurgedAt/LastPurgedCount
            var settings = await _context.ItemRetentionSettings.FirstOrDefaultAsync();
            var deletedCount = settings?.LastPurgedCount ?? 0;

            await _activityLogService.LogAsync(HttpContext, "Manual Item Purge",
                $"Manual item retention purge executed. Deleted {deletedCount} record(s).", "UserManagement");

            TempData["SuccessMessage"] = $"Item purge completed. {deletedCount} record(s) deleted.";
            return RedirectToAction(nameof(ItemRetentionSettings));
        }

        // =====================================================================
        // AD GROUP SEARCH — live search against Active Directory
        // =====================================================================

        /// <summary>
        /// Searches Active Directory for security groups matching the given term.
        /// Returns JSON array of { name, distinguishedName } for the autocomplete UI.
        /// </summary>
        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public IActionResult SearchAdGroups(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
                return Json(new { results = Array.Empty<object>(), error = "" });

            if (!_config.GetValue<bool>("ActiveDirectory:Enabled", false))
                return Json(new { results = Array.Empty<object>(), error = "Active Directory integration is disabled." });

            var domain = _config["ActiveDirectory:Domain"];
            var container = _config["ActiveDirectory:Container"];
            var useSsl = _config.GetValue<bool>("ActiveDirectory:UseSSL", false);

            if (string.IsNullOrEmpty(domain))
                return Json(new { results = Array.Empty<object>(), error = "AD domain not configured." });

            try
            {
                var options = useSsl
                    ? ContextOptions.Negotiate | ContextOptions.SecureSocketLayer
                    : ContextOptions.Negotiate;
                using var ctx = new PrincipalContext(ContextType.Domain, domain, container, options);

                // Use GroupPrincipal query to find groups matching by name
                using var queryFilter = new GroupPrincipal(ctx) { Name = $"*{term.Trim()}*" };
                using var searcher = new PrincipalSearcher(queryFilter);

                var results = searcher.FindAll()
                    .Take(15)
                    .Select(p => new
                    {
                        name = p.Name,
                        distinguishedName = p.DistinguishedName
                    })
                    .ToList();

                return Json(new { results, error = "" });
            }
            catch (PrincipalServerDownException)
            {
                _logger.LogWarning("AD server not reachable during group search.");
                return Json(new { results = Array.Empty<object>(), error = "AD server is not reachable." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching AD groups for term '{Term}'.", term);
                return Json(new { results = Array.Empty<object>(), error = "Error searching Active Directory." });
            }
        }
    }
}
