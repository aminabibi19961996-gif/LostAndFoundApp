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
    /// Index (user list) is accessible to Admin and Super Admin.
    /// All mutating actions (create, edit, delete, AD sync) are Super Admin only.
    /// </summary>
    [Authorize(Policy = "RequireAdminOrAbove")]
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
            "SuperAdmin", "Admin", "User"
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
        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users.ToListAsync();
            var userList = new List<UserListViewModel>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                userList.Add(new UserListViewModel
                {
                    Id = user.Id,
                    UserName = user.UserName ?? "",
                    DisplayName = user.DisplayName,
                    Email = user.Email,
                    Role = roles.FirstOrDefault() ?? "None",
                    AccountType = user.IsAdUser ? "Active Directory" : "Local",
                    IsActive = user.IsActive
                });
            }

            return View(userList.OrderBy(u => u.UserName).ToList());
        }

        // GET: /UserManagement/Create
        [HttpGet]
        [Authorize(Policy = "RequireSuperAdmin")]
        public IActionResult Create()
        {
            return View(new CreateUserViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSuperAdmin")]
        public async Task<IActionResult> Create(CreateUserViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Server-side role validation — reject arbitrary role names from crafted POST requests
            if (!ValidRoles.Contains(model.Role))
            {
                ModelState.AddModelError("Role", $"Invalid role '{model.Role}'. Must be SuperAdmin, Admin, or User.");
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
            TempData["SuccessMessage"] = $"User '{model.UserName}' created successfully with role '{model.Role}'. They must change their password on first login.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /UserManagement/EditRole/userId
        [HttpGet]
        [Authorize(Policy = "RequireSuperAdmin")]
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
        [Authorize(Policy = "RequireSuperAdmin")]
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

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Any())
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
            }

            await _userManager.AddToRoleAsync(user, model.NewRole);

            await _activityLogService.LogAsync(HttpContext, "Change Role",
                $"Role for user '{user.UserName}' changed from '{model.CurrentRole}' to '{model.NewRole}'.", "UserManagement");
            _logger.LogInformation("Role for user '{User}' changed from '{OldRole}' to '{NewRole}'.",
                user.UserName, model.CurrentRole, model.NewRole);
            TempData["SuccessMessage"] = $"Role for '{user.UserName}' changed to '{model.NewRole}'.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSuperAdmin")]
        public async Task<IActionResult> ToggleActive(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

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
        // ACTIVE DIRECTORY GROUP MANAGEMENT WITH ROLE MAPPING
        // =====================================================================

        // GET: /UserManagement/AdGroups
        [Authorize(Policy = "RequireSuperAdmin")]
        public async Task<IActionResult> AdGroups()
        {
            ViewBag.AdEnabled = _config.GetValue<bool>("ActiveDirectory:Enabled", false);
            ViewBag.AdDomain = _config["ActiveDirectory:Domain"] ?? "(not configured)";
            var groups = await _context.AdGroups.OrderBy(g => g.GroupName).ToListAsync();
            return View(groups);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSuperAdmin")]
        public async Task<IActionResult> AddAdGroup(string groupName, string mappedRole)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                TempData["ErrorMessage"] = "Group name is required.";
                return RedirectToAction(nameof(AdGroups));
            }

            // Validate mapped role
            var validRoles = new[] { "Admin", "User" };
            if (string.IsNullOrWhiteSpace(mappedRole) || !validRoles.Contains(mappedRole))
            {
                TempData["ErrorMessage"] = "Please select a valid role mapping (Admin or User).";
                return RedirectToAction(nameof(AdGroups));
            }

            var trimmed = groupName.Trim();
            if (await _context.AdGroups.AnyAsync(g => g.GroupName == trimmed))
            {
                TempData["ErrorMessage"] = $"AD group '{trimmed}' is already configured.";
                return RedirectToAction(nameof(AdGroups));
            }

            _context.AdGroups.Add(new AdGroup
            {
                GroupName = trimmed,
                MappedRole = mappedRole,
                IsActive = true,
                DateAdded = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            await _activityLogService.LogAsync(HttpContext, "Add AD Group",
                $"AD group '{trimmed}' added with role mapping '{mappedRole}'.", "ADSync");
            _logger.LogInformation("AD group '{GroupName}' added with role '{Role}' by '{User}'.", trimmed, mappedRole, User.Identity?.Name);
            TempData["SuccessMessage"] = $"AD group '{trimmed}' added with role mapping '{mappedRole}'.";
            return RedirectToAction(nameof(AdGroups));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSuperAdmin")]
        public async Task<IActionResult> UpdateAdGroupRole(int id, string mappedRole)
        {
            var group = await _context.AdGroups.FindAsync(id);
            if (group == null) return NotFound();

            var validRoles = new[] { "Admin", "User" };
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
        [Authorize(Policy = "RequireSuperAdmin")]
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
        [Authorize(Policy = "RequireSuperAdmin")]
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSuperAdmin")]
        public async Task<IActionResult> SyncNow()
        {
            var result = await _adSyncService.SyncUsersAsync();

            await _activityLogService.LogAsync(HttpContext, "Manual AD Sync",
                result.Summary, "ADSync", result.Success ? "Success" : "Failed");

            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Summary;
            }
            else
            {
                TempData["ErrorMessage"] = result.Summary;
            }

            _logger.LogInformation("AD sync triggered by '{User}': {Summary}", User.Identity?.Name, result.Summary);
            return RedirectToAction(nameof(AdGroups));
        }
    }
}
