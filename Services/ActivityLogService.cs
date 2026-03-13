using Microsoft.EntityFrameworkCore;
using LostAndFoundApp.Data;
using LostAndFoundApp.Models;

namespace LostAndFoundApp.Services
{
    /// <summary>
    /// Centralized service for logging all application activities to the database.
    /// Used across all controllers to maintain a complete audit trail.
    /// </summary>
    public class ActivityLogService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ActivityLogService> _logger;

        public ActivityLogService(ApplicationDbContext context, ILogger<ActivityLogService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Log an activity to the database
        /// </summary>
        public async Task LogAsync(string action, string details, string performedBy, string category, string? ipAddress = null, string status = "Success")
        {
            try
            {
                // FIX: Add null check for details parameter to prevent NullReferenceException
                var safeDetails = details ?? string.Empty;
                var log = new ActivityLog
                {
                    Timestamp = DateTime.Now,
                    Action = action ?? string.Empty,
                    Details = safeDetails.Length > 2000 ? safeDetails[..2000] : safeDetails,
                    PerformedBy = performedBy ?? "System",
                    Category = category ?? "General",
                    IpAddress = ipAddress,
                    Status = status ?? "Success"
                };

                _context.ActivityLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Never let logging failures crash the application
                _logger.LogError(ex, "Failed to write activity log: {Action} by {User}", action, performedBy);
            }
        }

        /// <summary>
        /// Log an activity with HttpContext for automatic IP extraction
        /// </summary>
        public async Task LogAsync(HttpContext httpContext, string action, string details, string category, string status = "Success")
        {
            var username = httpContext.User?.Identity?.Name ?? "System";
            var ip = httpContext.Connection.RemoteIpAddress?.ToString();
            await LogAsync(action, details, username, category, ip, status);
        }

        /// <summary>
        /// Clear all activity logs — only callable by SuperAdmin
        /// </summary>
        public async Task<int> ClearAllLogsAsync()
        {
            try
            {
                var count = await _context.ActivityLogs.ExecuteDeleteAsync();
                _logger.LogInformation("All activity logs cleared ({Count} records).", count);
                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear activity logs.");
                throw;
            }
        }
    }
}
