namespace LostAndFoundApp.Services
{
    /// <summary>
    /// Background hosted service that runs AD sync automatically once every day.
    /// Configurable sync time via appsettings "ActiveDirectory:DailySyncHourUtc" (default: 2 AM UTC).
    /// </summary>
    public class AdSyncHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AdSyncHostedService> _logger;
        private readonly IConfiguration _config;

        public AdSyncHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<AdSyncHostedService> logger,
            IConfiguration config)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_config.GetValue<bool>("ActiveDirectory:Enabled", false))
            {
                _logger.LogInformation("AD Sync Hosted Service will not run — Active Directory integration is disabled.");
                return;
            }

            _logger.LogInformation("AD Sync Hosted Service started. Will sync daily.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Calculate delay until next sync time
                    var syncHour = _config.GetValue<int>("ActiveDirectory:DailySyncHourUtc", 2);
                    var now = DateTime.UtcNow;
                    var nextSync = now.Date.AddHours(syncHour);
                    if (nextSync <= now)
                        nextSync = nextSync.AddDays(1);

                    var delay = nextSync - now;
                    _logger.LogInformation("Next AD sync scheduled at {NextSync} UTC (in {Hours:F1} hours).",
                        nextSync, delay.TotalHours);

                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    // Execute the sync
                    await RunSyncAsync();
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in AD Sync Hosted Service. Will retry in 1 hour.");
                    try
                    {
                        await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            _logger.LogInformation("AD Sync Hosted Service stopped.");
        }

        private async Task RunSyncAsync()
        {
            _logger.LogInformation("Starting scheduled daily AD sync...");

            using var scope = _scopeFactory.CreateScope();
            var adSyncService = scope.ServiceProvider.GetRequiredService<AdSyncService>();
            var activityLogService = scope.ServiceProvider.GetRequiredService<ActivityLogService>();

            var result = await adSyncService.SyncUsersAsync();

            await activityLogService.LogAsync(
                "Scheduled AD Sync",
                result.Summary,
                "System (Scheduled)",
                "ADSync",
                null,
                result.Success ? "Success" : "Failed"
            );

            _logger.LogInformation("Scheduled AD sync completed: {Summary}", result.Summary);
        }
    }
}
