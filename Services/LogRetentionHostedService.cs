namespace LostAndFoundApp.Services
{
    /// <summary>
    /// Background hosted service that purges activity log records older than
    /// the configured retention period. Runs once per day at 3 AM.
    /// The retention period (30, 60, or 90 days) is read from the LogRetentionSettings
    /// table in the database — the same pattern used by OverdueSettings and PasswordPolicy.
    /// </summary>
    public class LogRetentionHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LogRetentionHostedService> _logger;

        public LogRetentionHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<LogRetentionHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Log Retention Hosted Service started. Will purge expired logs daily at 03:00.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Calculate delay until next run (3 AM daily)
                    var now = DateTime.UtcNow;
                    var nextRun = now.Date.AddHours(3);
                    if (nextRun <= now)
                        nextRun = nextRun.AddDays(1);

                    var delay = nextRun - now;
                    _logger.LogInformation(
                        "Next log retention purge scheduled at {NextRun} (in {Hours:F1} hours).",
                        nextRun, delay.TotalHours);

                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    // Execute the purge
                    await RunPurgeAsync();
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Log Retention Hosted Service. Will retry in 60 minutes.");
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(60), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            _logger.LogInformation("Log Retention Hosted Service stopped.");
        }

        /// <summary>
        /// Public method to manually trigger a log retention purge.
        /// Used by the "Run Purge Now" button on the settings page.
        /// </summary>
        public async Task RunPurgeNowAsync()
        {
            await RunPurgeAsync();
        }

        private async Task RunPurgeAsync()
        {
            _logger.LogInformation("Starting scheduled activity log retention purge...");

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider
                .GetRequiredService<Data.ApplicationDbContext>();

            // Read the configured retention period from the database
            var settings = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(context.LogRetentionSettings);
            var retentionDays = settings?.RetentionDays ?? 30;

            // Calculate cutoff date
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

            // Delete all activity logs older than the cutoff
            var deletedCount = await Microsoft.EntityFrameworkCore.RelationalQueryableExtensions
                .ExecuteDeleteAsync(
                    context.ActivityLogs.Where(l => l.Timestamp < cutoff));

            // Record purge results in the settings row
            if (settings != null)
            {
                settings.LastPurgedAt = DateTime.UtcNow;
                settings.LastPurgedCount = deletedCount;
                await context.SaveChangesAsync();
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation(
                    "Log retention purge completed: deleted {Count} activity log record(s) older than {Days} days (before {Cutoff:yyyy-MM-dd HH:mm}).",
                    deletedCount, retentionDays, cutoff);
            }
            else
            {
                _logger.LogInformation(
                    "Log retention purge completed: no records older than {Days} days found.",
                    retentionDays);
            }
        }
    }
}
