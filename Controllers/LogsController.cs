using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LostAndFoundApp.Data;
using LostAndFoundApp.Services;
using LostAndFoundApp.ViewModels;

namespace LostAndFoundApp.Controllers
{
    /// <summary>
    /// Activity Logs management.
    /// All users can view their own logs.
    /// Admin and SuperAdmin can view all logs.
    /// Only SuperAdmin can clear or export logs.
    /// </summary>
    [Authorize]
    public class LogsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ActivityLogService _activityLogService;
        private readonly ILogger<LogsController> _logger;

        public LogsController(
            ApplicationDbContext context,
            ActivityLogService activityLogService,
            ILogger<LogsController> logger)
        {
            _context = context;
            _activityLogService = activityLogService;
            _logger = logger;
        }

        // GET: /Logs
        public async Task<IActionResult> Index(string? category, string? search, DateTime? dateFrom, DateTime? dateTo, int page = 1)
        {
            var query = _context.ActivityLogs.AsQueryable();

            var isAdminOrAbove = User.IsInRole("SuperAdmin") || User.IsInRole("Admin");

            // User role: can only see their own logs
            if (!isAdminOrAbove)
            {
                var currentUserName = User.Identity?.Name ?? "";
                query = query.Where(l => l.PerformedBy == currentUserName);
            }

            // Apply filters
            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(l => l.Category == category);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(l => l.Details.Contains(search) || l.Action.Contains(search) || l.PerformedBy.Contains(search));

            if (dateFrom.HasValue)
                query = query.Where(l => l.Timestamp >= dateFrom.Value);

            if (dateTo.HasValue)
                query = query.Where(l => l.Timestamp <= dateTo.Value.AddDays(1));

            var totalCount = await query.CountAsync();
            var pageSize = 50;
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var vm = new LogListViewModel
            {
                Logs = logs,
                TotalCount = totalCount,
                CanClearLogs = User.IsInRole("SuperAdmin"),
                CanViewAllLogs = isAdminOrAbove,
                CategoryFilter = category,
                SearchTerm = search,
                DateFrom = dateFrom,
                DateTo = dateTo,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = totalPages
            };

            return View(vm);
        }

        // POST: /Logs/Clear — SuperAdmin only
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSuperAdmin")]
        public async Task<IActionResult> Clear()
        {
            try
            {
                var count = await _activityLogService.ClearAllLogsAsync();

                // Log the clear action itself (this will be the only remaining log)
                await _activityLogService.LogAsync(
                    HttpContext, "Clear Logs",
                    $"All activity logs cleared ({count} records removed).",
                    "System");

                _logger.LogWarning("All activity logs cleared by '{User}' ({Count} records).", User.Identity?.Name, count);
                TempData["SuccessMessage"] = $"All activity logs have been cleared ({count} records removed).";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear activity logs.");
                TempData["ErrorMessage"] = "Failed to clear activity logs. Please try again.";
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: /Logs/Export — Export logs as CSV
        [HttpGet]
        [Authorize(Policy = "RequireSuperAdmin")]
        public async Task<IActionResult> Export(string? category, DateTime? dateFrom, DateTime? dateTo)
        {
            var query = _context.ActivityLogs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(l => l.Category == category);
            if (dateFrom.HasValue)
                query = query.Where(l => l.Timestamp >= dateFrom.Value);
            if (dateTo.HasValue)
                query = query.Where(l => l.Timestamp <= dateTo.Value.AddDays(1));

            var logs = await query.OrderByDescending(l => l.Timestamp).ToListAsync();

            var csv = "Timestamp,Action,Details,Performed By,Category,IP Address,Status\n";
            foreach (var log in logs)
            {
                csv += $"\"{log.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{EscapeCsv(log.Action)}\",\"{EscapeCsv(log.Details)}\",\"{EscapeCsv(log.PerformedBy)}\",\"{EscapeCsv(log.Category)}\",\"{log.IpAddress}\",\"{log.Status}\"\n";
            }

            await _activityLogService.LogAsync(HttpContext, "Export Logs", $"Exported {logs.Count} activity log records.", "System");

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
            return File(bytes, "text/csv", $"activity_logs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            // Escape embedded double-quotes
            var escaped = value.Replace("\"", "\"\"");
            // Neutralize formula injection: if value starts with =, +, -, or @,
            // Excel may interpret it as a formula. Prefix with a single quote to prevent this.
            if (escaped.Length > 0 && "=+-@".Contains(escaped[0]))
            {
                escaped = "'" + escaped;
            }
            return escaped;
        }
    }
}
