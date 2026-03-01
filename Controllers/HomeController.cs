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
        private readonly IConfiguration _configuration;

        public HomeController(
            ILogger<HomeController> logger,
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration)
        {
            _logger = logger;
            _context = context;
            _userManager = userManager;
            _configuration = configuration;
        }

        public async Task<IActionResult> Index()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var roles = currentUser != null ? await _userManager.GetRolesAsync(currentUser) : new List<string>();
            var primaryRole = roles.FirstOrDefault() ?? "User";

            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var isAdmin = User.IsInRole("Admin");
            var isSupervisor = User.IsInRole("Supervisor");
            var isSupervisorOrAbove = isSuperAdmin || isAdmin || isSupervisor;
            var currentUserName = currentUser?.UserName ?? "";

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // COMMON DATA — All roles need this
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

            // Single query for all status counts (avoids N+1)
            var statusGroups = await _context.LostFoundItems
                .Include(x => x.Status)
                .Where(x => x.Status != null)
                .GroupBy(x => x.Status!.Name)
                .Select(g => new { StatusName = g.Key, Count = g.Count() })
                .ToListAsync();

            var totalItems = statusGroups.Sum(s => s.Count);
            var claimedCount = statusGroups.FirstOrDefault(s => s.StatusName == "Claimed")?.Count ?? 0;
            var disposedCount = statusGroups.FirstOrDefault(s => s.StatusName == "Disposed")?.Count ?? 0;
            var foundCount = statusGroups.FirstOrDefault(s => s.StatusName == "Found")?.Count ?? 0;
            var storedCount = statusGroups.FirstOrDefault(s => s.StatusName == "Stored")?.Count ?? 0;
            var transferredCount = statusGroups.FirstOrDefault(s => s.StatusName == "Transferred")?.Count ?? 0;

            var vm = new DashboardViewModel
            {
                UserDisplayName = currentUser?.DisplayName ?? currentUser?.UserName ?? "User",
                UserRole = primaryRole,
                UserName = currentUserName,
                TotalItems = totalItems,
                FoundCount = foundCount,
                ClaimedCount = claimedCount,
                StoredCount = storedCount,
                DisposedCount = disposedCount,
                TransferredCount = transferredCount,
            };

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // KPIs — Ratios (not just raw counts)
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

            vm.ClaimRatePercent = totalItems > 0 ? Math.Round((double)claimedCount / totalItems * 100, 1) : 0;
            vm.DisposalRatePercent = totalItems > 0 ? Math.Round((double)disposedCount / totalItems * 100, 1) : 0;

            // Average days to claim — using StatusDate (when status changed to Claimed)
            var claimedItemDates = await _context.LostFoundItems
                .Include(x => x.Status)
                .Where(x => x.Status != null && x.Status.Name == "Claimed" && x.StatusDate != null)
                .Select(x => new { x.DateFound, StatusDate = x.StatusDate!.Value })
                .ToListAsync();
            if (claimedItemDates.Any())
                vm.AvgDaysToClaim = Math.Round(claimedItemDates.Average(x => (x.StatusDate - x.DateFound).TotalDays), 1);

            // Average storage duration — days items in Stored status have been sitting
            var storedItemDates = await _context.LostFoundItems
                .Include(x => x.Status)
                .Where(x => x.Status != null && x.Status.Name == "Stored")
                .Select(x => x.DateFound)
                .ToListAsync();
            if (storedItemDates.Any())
                vm.AvgStorageDuration = Math.Round(storedItemDates.Average(x => (DateTime.UtcNow - x).TotalDays), 1);

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // TRENDS — Week-over-week, month-over-month
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

            var now = DateTime.UtcNow;
            var weekAgo = now.AddDays(-7);
            var twoWeeksAgo = now.AddDays(-14);
            var monthAgo = now.AddDays(-30);
            var twoMonthsAgo = now.AddDays(-60);

            vm.ItemsThisWeek = await _context.LostFoundItems.CountAsync(x => x.CreatedDateTime >= weekAgo);
            vm.ItemsLastWeek = await _context.LostFoundItems.CountAsync(x => x.CreatedDateTime >= twoWeeksAgo && x.CreatedDateTime < weekAgo);
            vm.ItemsThisMonth = await _context.LostFoundItems.CountAsync(x => x.CreatedDateTime >= monthAgo);
            vm.ItemsLastMonth = await _context.LostFoundItems.CountAsync(x => x.CreatedDateTime >= twoMonthsAgo && x.CreatedDateTime < monthAgo);

            vm.WeekOverWeekChangePercent = vm.ItemsLastWeek > 0
                ? Math.Round((double)(vm.ItemsThisWeek - vm.ItemsLastWeek) / vm.ItemsLastWeek * 100, 1)
                : (vm.ItemsThisWeek > 0 ? 100 : 0);
            vm.MonthOverMonthChangePercent = vm.ItemsLastMonth > 0
                ? Math.Round((double)(vm.ItemsThisMonth - vm.ItemsLastMonth) / vm.ItemsLastMonth * 100, 1)
                : (vm.ItemsThisMonth > 0 ? 100 : 0);

            // Transfer frequency this month
            vm.TransferFrequencyThisMonth = await _context.LostFoundItems
                .Include(x => x.Status)
                .CountAsync(x => x.Status != null && x.Status.Name == "Transferred" && x.CreatedDateTime >= monthAgo);

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // CRITICAL ALERTS
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

            var thirtyDaysAgo = now.AddDays(-30);

            vm.UnclaimedOver30Days = await _context.LostFoundItems
                .CountAsync(x => x.DateFound <= thirtyDaysAgo
                    && x.Status != null && x.Status.Name != "Claimed"
                    && x.Status.Name != "Disposed"
                    && x.Status.Name != "Transferred");

            vm.ItemsAwaitingAction = await _context.LostFoundItems
                .CountAsync(x => x.Status != null &&
                    (x.Status.Name == "Found" || x.Status.Name == "Stored"));

            var activeItems = totalItems - claimedCount - disposedCount - transferredCount;
            vm.AwaitingActionPercent = activeItems > 0
                ? Math.Round((double)vm.ItemsAwaitingAction / activeItems * 100, 1) : 0;

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // USER-SPECIFIC: My Work
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

            vm.MyItemsCount = await _context.LostFoundItems
                .CountAsync(x => x.CreatedBy == currentUserName);
            vm.MyItemsThisWeek = await _context.LostFoundItems
                .CountAsync(x => x.CreatedBy == currentUserName && x.CreatedDateTime >= weekAgo);
            vm.MyItemsAwaitingAction = await _context.LostFoundItems
                .CountAsync(x => x.CreatedBy == currentUserName
                    && x.Status != null
                    && (x.Status.Name == "Found" || x.Status.Name == "Stored"));

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // RECENT RECORDS — role-filtered
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

            IQueryable<LostFoundItem> recentQuery = _context.LostFoundItems
                .Include(x => x.Item)
                .Include(x => x.Status);

            // User role: show only their own records
            if (!isSupervisorOrAbove)
            {
                recentQuery = recentQuery.Where(x => x.CreatedBy == currentUserName);
            }

            var recentItems = await recentQuery
                .OrderByDescending(x => x.CreatedDateTime)
                .Take(isSuperAdmin || isAdmin ? 15 : 10)
                .Select(x => new
                {
                    x.TrackingId,
                    x.CustomTrackingId,
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
                CustomTrackingId = x.CustomTrackingId,
                DateFound = x.DateFound,
                ItemName = x.ItemName,
                LocationFound = x.LocationFound,
                StatusName = x.StatusName,
                DaysSinceFound = (DateTime.Today - x.DateFound.Date).Days,
                ClaimedBy = x.ClaimedBy,
                CreatedBy = x.CreatedBy
            }).ToList();

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // SUPERVISOR+: Team & user overview
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

            if (isSupervisorOrAbove)
            {
                var allUsers = await _userManager.Users.ToListAsync();
                vm.TotalUsers = allUsers.Count;
                vm.ActiveUsers = allUsers.Count(u => u.IsActive);
                vm.InactiveUsers = allUsers.Count(u => !u.IsActive);
                vm.LocalUsers = allUsers.Count(u => !u.IsAdUser);
                vm.AdUsers = allUsers.Count(u => u.IsAdUser);
                vm.AdGroupCount = await _context.AdGroups.CountAsync();

                // Role distribution
                var roleCounts = await _context.UserRoles
                    .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                    .GroupBy(roleName => roleName)
                    .Select(g => new { Role = g.Key, Count = g.Count() })
                    .ToListAsync();

                vm.SuperAdminCount = roleCounts.FirstOrDefault(r => r.Role == "SuperAdmin")?.Count ?? 0;
                vm.AdminCount = roleCounts.FirstOrDefault(r => r.Role == "Admin")?.Count ?? 0;
                vm.SupervisorCount = roleCounts.FirstOrDefault(r => r.Role == "Supervisor")?.Count ?? 0;
                vm.UserRoleCount = roleCounts.FirstOrDefault(r => r.Role == "User")?.Count ?? 0;

                // Top contributors — users ranked by items created this month
                vm.TopContributors = await _context.LostFoundItems
                    .Where(x => x.CreatedDateTime >= monthAgo && x.CreatedBy != null)
                    .GroupBy(x => x.CreatedBy!)
                    .Select(g => new UserPerformanceItem
                    {
                        UserName = g.Key,
                        DisplayName = g.Key,
                        ItemsCreated = g.Count(),
                        ItemsThisWeek = g.Count(x => x.CreatedDateTime >= weekAgo)
                    })
                    .OrderByDescending(x => x.ItemsCreated)
                    .Take(5)
                    .ToListAsync();

                // Resolve display names
                foreach (var contributor in vm.TopContributors)
                {
                    var user = allUsers.FirstOrDefault(u => u.UserName == contributor.UserName);
                    if (user != null && !string.IsNullOrEmpty(user.DisplayName))
                        contributor.DisplayName = user.DisplayName;
                }
            }

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // ADMIN+: Master data health & analytics
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

            if (isAdmin || isSuperAdmin)
            {
                vm.MasterItemCount = await _context.Items.CountAsync();
                vm.MasterRouteCount = await _context.Routes.CountAsync();
                vm.MasterVehicleCount = await _context.Vehicles.CountAsync();
                vm.MasterStorageLocationCount = await _context.StorageLocations.CountAsync();
                vm.MasterStatusCount = await _context.Statuses.CountAsync();
                vm.MasterFoundByNameCount = await _context.FoundByNames.CountAsync();

                // Inactive master data (data hygiene)
                var inactiveItems = await _context.Items.CountAsync(x => !x.IsActive);
                var inactiveRoutes = await _context.Routes.CountAsync(x => !x.IsActive);
                var inactiveVehicles = await _context.Vehicles.CountAsync(x => !x.IsActive);
                var inactiveLocations = await _context.StorageLocations.CountAsync(x => !x.IsActive);
                vm.InactiveMasterDataCount = inactiveItems + inactiveRoutes + inactiveVehicles + inactiveLocations;

                // Status breakdown with percentages
                vm.StatusBreakdown = statusGroups.Select(s => new StatusBreakdownItem
                {
                    StatusName = s.StatusName,
                    Count = s.Count,
                    CssClass = s.StatusName?.ToLower() ?? "unknown",
                    Percentage = totalItems > 0 ? (int)Math.Round((double)s.Count / totalItems * 100) : 0
                }).OrderByDescending(s => s.Count).ToList();

                // Top items
                vm.TopItemTypes = await _context.LostFoundItems
                    .Include(x => x.Item)
                    .Where(x => x.Item != null)
                    .GroupBy(x => x.Item!.Name)
                    .Select(g => new TopItemType { ItemName = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(5)
                    .ToListAsync();

                // Storage utilization
                vm.StorageUtilization = await _context.LostFoundItems
                    .Include(x => x.StorageLocation)
                    .Where(x => x.StorageLocation != null
                        && x.Status != null
                        && x.Status.Name != "Claimed"
                        && x.Status.Name != "Disposed"
                        && x.Status.Name != "Transferred")
                    .GroupBy(x => x.StorageLocation!.Name)
                    .Select(g => new StorageUtilItem
                    {
                        LocationName = g.Key,
                        ItemCount = g.Count()
                    })
                    .OrderByDescending(x => x.ItemCount)
                    .ToListAsync();
            }

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // SUPERADMIN: System health
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

            if (isSuperAdmin)
            {
                // AD sync status
                vm.AdSyncEnabled = _configuration.GetValue<bool>("ActiveDirectory:Enabled");
                var lastSync = await _context.AdSyncLogs
                    .OrderByDescending(x => x.Timestamp)
                    .FirstOrDefaultAsync();
                if (lastSync != null)
                {
                    vm.LastAdSyncTime = lastSync.Timestamp;
                    vm.LastAdSyncSuccess = lastSync.Success;
                    vm.LastAdSyncError = lastSync.ErrorSummary;
                }

                // Failed logins in last 24 hours
                var yesterday = now.AddDays(-1);
                vm.RecentFailedLogins = await _context.ActivityLogs
                    .CountAsync(x => x.Category == "Auth"
                        && x.Status == "Failed"
                        && x.Timestamp >= yesterday);

                // Audit log stats
                vm.TotalActivityLogs = await _context.ActivityLogs.CountAsync();
                vm.ActivityLogs24h = await _context.ActivityLogs
                    .CountAsync(x => x.Timestamp >= yesterday);
            }

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // ROUTE TO ROLE-SPECIFIC VIEW
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

            if (isSuperAdmin)
                return View("DashboardSuperAdmin", vm);
            if (isAdmin)
                return View("DashboardAdmin", vm);
            if (isSupervisor)
                return View("DashboardSupervisor", vm);
            return View("DashboardUser", vm);
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
