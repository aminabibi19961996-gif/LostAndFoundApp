using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LostAndFoundApp.Data;
using LostAndFoundApp.Models;
using LostAndFoundApp.ViewModels;
using System.Diagnostics;
using System.Linq;

namespace LostAndFoundApp.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public HomeController(
            ILogger<HomeController> logger,
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _logger = logger;
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var roles = currentUser != null ? await _userManager.GetRolesAsync(currentUser) : new List<string>();
            var primaryRole = roles.FirstOrDefault() ?? "User";

            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var isAdmin = User.IsInRole("Admin");

            // --- Common data for all roles ---
            var vm = new DashboardViewModel
            {
                UserDisplayName = currentUser?.DisplayName ?? currentUser?.UserName ?? "User",
                UserRole = primaryRole,
                UserName = currentUser?.UserName ?? "",
                TotalItems = await _context.LostFoundItems.CountAsync(),
                FoundCount = await _context.LostFoundItems
                    .CountAsync(x => x.Status != null && x.Status.Name == "Found"),
                ClaimedCount = await _context.LostFoundItems
                    .CountAsync(x => x.Status != null && x.Status.Name == "Claimed"),
                StoredCount = await _context.LostFoundItems
                    .CountAsync(x => x.Status != null && x.Status.Name == "Stored"),
                DisposedCount = await _context.LostFoundItems
                    .CountAsync(x => x.Status != null && x.Status.Name == "Disposed"),
                TransferredCount = await _context.LostFoundItems
                    .CountAsync(x => x.Status != null && x.Status.Name == "Transferred"),
            };

            // --- Recent records ---
            var recentQuery = _context.LostFoundItems
                .Include(x => x.Item)
                .Include(x => x.Status)
                .OrderByDescending(x => x.CreatedDateTime)
                .Take(isSuperAdmin || isAdmin ? 15 : 10);

            var recentItems = await recentQuery
                .Select(x => new
                {
                    x.TrackingId,
                    x.DateFound,
                    ItemName = x.Item != null ? x.Item.Name : "",
                    x.LocationFound,
                    StatusName = x.Status != null ? x.Status.Name : "",
                    x.ClaimedBy,
                    x.CreatedBy
                })
                .ToListAsync();

            vm.RecentRecords = recentItems.Select(x => new DashboardRecentItem
            {
                TrackingId = x.TrackingId,
                DateFound = x.DateFound,
                ItemName = x.ItemName,
                LocationFound = x.LocationFound,
                StatusName = x.StatusName,
                DaysSinceFound = (DateTime.Today - x.DateFound.Date).Days,
                ClaimedBy = x.ClaimedBy,
                CreatedBy = x.CreatedBy
            }).ToList();

            // --- SuperAdmin / Admin: system overview data ---
            if (isSuperAdmin || isAdmin)
            {
                var allUsers = await _userManager.Users.ToListAsync();
                vm.TotalUsers = allUsers.Count;
                vm.ActiveUsers = allUsers.Count(u => u.IsActive);
                vm.InactiveUsers = allUsers.Count(u => !u.IsActive);
                vm.LocalUsers = allUsers.Count(u => !u.IsAdUser);
                vm.AdUsers = allUsers.Count(u => u.IsAdUser);
                vm.AdGroupCount = await _context.AdGroups.CountAsync();

                // Role distribution — single query instead of N+1
                var roleCounts = await _context.UserRoles
                    .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                    .GroupBy(roleName => roleName)
                    .Select(g => new { Role = g.Key, Count = g.Count() })
                    .ToListAsync();

                vm.SuperAdminCount = roleCounts.FirstOrDefault(r => r.Role == "SuperAdmin")?.Count ?? 0;
                vm.AdminCount = roleCounts.FirstOrDefault(r => r.Role == "Admin")?.Count ?? 0;
                vm.UserRoleCount = roleCounts.FirstOrDefault(r => r.Role == "User")?.Count ?? 0;

                // Time-based item stats
                var weekAgo = DateTime.UtcNow.AddDays(-7);
                var monthAgo = DateTime.UtcNow.AddDays(-30);
                vm.ItemsThisWeek = await _context.LostFoundItems
                    .CountAsync(x => x.CreatedDateTime >= weekAgo);
                vm.ItemsThisMonth = await _context.LostFoundItems
                    .CountAsync(x => x.CreatedDateTime >= monthAgo);

                // Items unclaimed for over 30 days
                var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
                vm.UnclaimedOver30Days = await _context.LostFoundItems
                    .CountAsync(x => x.DateFound <= thirtyDaysAgo
                        && x.Status != null && x.Status.Name != "Claimed"
                        && x.Status.Name != "Disposed"
                        && x.Status.Name != "Transferred");
            }

            // --- Admin + SuperAdmin: operational analytics ---
            if (isAdmin || isSuperAdmin)
            {
                vm.MasterItemCount = await _context.Items.CountAsync();
                vm.MasterRouteCount = await _context.Routes.CountAsync();
                vm.MasterVehicleCount = await _context.Vehicles.CountAsync();
                vm.MasterStorageLocationCount = await _context.StorageLocations.CountAsync();
                vm.MasterStatusCount = await _context.Statuses.CountAsync();
                vm.MasterFoundByNameCount = await _context.FoundByNames.CountAsync();

                // Items awaiting action
                vm.ItemsAwaitingAction = await _context.LostFoundItems
                    .CountAsync(x => x.Status != null &&
                        (x.Status.Name == "Found" || x.Status.Name == "Stored"));

                // Status breakdown for analytics
                var statusGroups = await _context.LostFoundItems
                    .Include(x => x.Status)
                    .Where(x => x.Status != null)
                    .GroupBy(x => x.Status!.Name)
                    .Select(g => new { StatusName = g.Key, Count = g.Count() })
                    .ToListAsync();

                var total = statusGroups.Sum(s => s.Count);
                vm.StatusBreakdown = statusGroups.Select(s => new StatusBreakdownItem
                {
                    StatusName = s.StatusName,
                    Count = s.Count,
                    CssClass = s.StatusName?.ToLower() ?? "unknown",
                    Percentage = total > 0 ? (int)Math.Round((double)s.Count / total * 100) : 0
                }).OrderByDescending(s => s.Count).ToList();

                // Top item types
                vm.TopItemTypes = await _context.LostFoundItems
                    .Include(x => x.Item)
                    .Where(x => x.Item != null)
                    .GroupBy(x => x.Item!.Name)
                    .Select(g => new TopItemType { ItemName = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(5)
                    .ToListAsync();
            }

            return View(vm);
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error(int? statusCode = null)
        {
            var viewModel = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                StatusCode = statusCode ?? Response.StatusCode
            };
            return View(viewModel);
        }
    }
}
