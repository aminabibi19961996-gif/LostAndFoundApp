namespace LostAndFoundApp.Services
{
    /// <summary>
    /// Background hosted service that purges lost-and-found case records older than
    /// the configured retention period. Runs once per day at 4 AM UTC.
    /// The retention period (365 or 730 days) is read from the ItemRetentionSettings
    /// table in the database — the same pattern used by LogRetentionSettings.
    /// Master data tables (Items, Routes, Vehicles, etc.) are never touched.
    /// </summary>
    public class ItemRetentionHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ItemRetentionHostedService> _logger;

        public ItemRetentionHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<ItemRetentionHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Item Retention Hosted Service started. Will purge expired case records daily at 04:00 UTC.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Calculate delay until next run (4 AM UTC daily)
                    var now = DateTime.Now;
                    var nextRun = now.Date.AddHours(4);
                    if (nextRun <= now)
                        nextRun = nextRun.AddDays(1);

                    var delay = nextRun - now;
                    _logger.LogInformation(
                        "Next item retention purge scheduled at {NextRun} UTC (in {Hours:F1} hours).",
                        nextRun, delay.TotalHours);

                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    await RunPurgeAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Item Retention Hosted Service. Will retry in 60 minutes.");
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

            _logger.LogInformation("Item Retention Hosted Service stopped.");
        }

        /// <summary>
        /// Public method to manually trigger an item retention purge.
        /// Used by the "Run Purge Now" button on the settings page.
        /// </summary>
        public async Task RunPurgeNowAsync()
        {
            await RunPurgeAsync();
        }

        private async Task RunPurgeAsync()
        {
            _logger.LogInformation("Starting scheduled item retention purge...");

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider
                .GetRequiredService<Data.ApplicationDbContext>();
            var fileService = scope.ServiceProvider
                .GetRequiredService<FileService>();

            // Read the configured retention period from the database
            var settings = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(context.ItemRetentionSettings);
            var retentionDays = settings?.RetentionDays ?? 365;

            // Calculate cutoff date
            var cutoff = DateTime.Now.AddDays(-retentionDays);

            // Query records to be purged so we can clean up physical files first.
            // Only select the file path columns to minimise memory usage.
            var expiredItems = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(
                    context.LostFoundItems
                        .Where(i => i.CreatedDateTime < cutoff)
                        .Select(i => new
                        {
                            i.PhotoPath,
                            i.PhotoPath2,
                            i.PhotoPath3,
                            i.PhotoPath4,
                            i.AttachmentPath
                        }));

            if (expiredItems.Count == 0)
            {
                // Record purge results even when nothing was deleted
                if (settings != null)
                {
                    settings.LastPurgedAt = DateTime.Now;
                    settings.LastPurgedCount = 0;
                    await context.SaveChangesAsync();
                }

                _logger.LogInformation(
                    "Item retention purge completed: no case records older than {Days} days found.",
                    retentionDays);
                return;
            }

            // Delete physical photo and attachment files from disk
            var filesDeleted = 0;
            foreach (var item in expiredItems)
            {
                foreach (var path in new[] { item.PhotoPath, item.PhotoPath2, item.PhotoPath3, item.PhotoPath4 })
                {
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        fileService.DeletePhoto(path);
                        filesDeleted++;
                    }
                }
                if (!string.IsNullOrWhiteSpace(item.AttachmentPath))
                {
                    fileService.DeleteAttachment(item.AttachmentPath);
                    filesDeleted++;
                }
            }

            // Now delete the database records
            var deletedCount = await Microsoft.EntityFrameworkCore.RelationalQueryableExtensions
                .ExecuteDeleteAsync(
                    context.LostFoundItems.Where(i => i.CreatedDateTime < cutoff));

            // Record purge results in the settings row
            if (settings != null)
            {
                settings.LastPurgedAt = DateTime.Now;
                settings.LastPurgedCount = deletedCount;
                await context.SaveChangesAsync();
            }

            _logger.LogInformation(
                "Item retention purge completed: deleted {Count} case record(s) and {Files} file(s) older than {Days} days (before {Cutoff:yyyy-MM-dd HH:mm} UTC).",
                deletedCount, filesDeleted, retentionDays, cutoff);
        }
    }
}
