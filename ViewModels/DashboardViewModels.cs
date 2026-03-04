namespace LostAndFoundApp.ViewModels
{
    public class DashboardViewModel
    {
        // ── User info ──
        public string UserDisplayName { get; set; } = string.Empty;
        public string UserRole { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;

        // ── Common item stats ──
        public int TotalItems { get; set; }
        public int FoundCount { get; set; }
        public int ClaimedCount { get; set; }
        public int StoredCount { get; set; }
        public int DisposedCount { get; set; }
        public int TransferredCount { get; set; }

        // ── KPIs (ratios, not just counts) ──
        /// <summary>Claimed / Total × 100 — system effectiveness</summary>
        public double ClaimRatePercent { get; set; }
        /// <summary>Average days from DateFound to Claimed status</summary>
        public double AvgDaysToClaim { get; set; }
        /// <summary>Average days items stay in Stored status</summary>
        public double AvgStorageDuration { get; set; }
        /// <summary>Disposed / Total × 100 — waste analysis</summary>
        public double DisposalRatePercent { get; set; }
        /// <summary>Number of transfers this month</summary>
        public int TransferFrequencyThisMonth { get; set; }

        // ── Trends (comparison data) ──
        public int ItemsThisWeek { get; set; }
        public int ItemsLastWeek { get; set; }
        public int ItemsThisMonth { get; set; }
        public int ItemsLastMonth { get; set; }
        /// <summary>Positive = growth, Negative = decline</summary>
        public double WeekOverWeekChangePercent { get; set; }
        public double MonthOverMonthChangePercent { get; set; }

        // ── Critical alerts ──
        /// <summary>Items unclaimed for over 30 days</summary>
        public int UnclaimedOver30Days { get; set; }

        /// <summary>Items in the system for over 7 days from CreatedDateTime, not yet resolved (not Claimed/Disposed/Transferred)</summary>
        public int ItemsOverdue7Days { get; set; }

        /// <summary>Items awaiting action (Found or Stored)</summary>
        public int ItemsAwaitingAction { get; set; }
        /// <summary>Percentage of total active items that are awaiting action</summary>
        public double AwaitingActionPercent { get; set; }

        // ── User-specific (My Work) ──
        /// <summary>Items created by the current logged-in user</summary>
        public int MyItemsCount { get; set; }
        public int MyItemsThisWeek { get; set; }
        public int MyItemsAwaitingAction { get; set; }

        // ── Recent records (role-filtered) ──
        public List<DashboardRecentItem> RecentRecords { get; set; } = new();

        // ── Supervisor: team performance ──
        public List<UserPerformanceItem> TopContributors { get; set; } = new();

        // ── Admin/SuperAdmin: user stats ──
        public int TotalUsers { get; set; }
        public int ActiveUsers { get; set; }
        public int InactiveUsers { get; set; }
        public int LocalUsers { get; set; }
        public int AdUsers { get; set; }
        public int AdGroupCount { get; set; }
        public int SuperAdminCount { get; set; }
        public int AdminCount { get; set; }
        public int SupervisorCount { get; set; }
        public int UserRoleCount { get; set; }

        // ── Admin: Master data health ──
        public int MasterItemCount { get; set; }
        public int MasterRouteCount { get; set; }
        public int MasterVehicleCount { get; set; }
        public int MasterStorageLocationCount { get; set; }
        public int MasterStatusCount { get; set; }
        public int MasterFoundByNameCount { get; set; }
        /// <summary>Master data items with IsActive = false</summary>
        public int InactiveMasterDataCount { get; set; }
        public List<StatusBreakdownItem> StatusBreakdown { get; set; } = new();
        public List<TopItemType> TopItemTypes { get; set; } = new();
        /// <summary>Storage locations with item count for utilization</summary>
        public List<StorageUtilItem> StorageUtilization { get; set; } = new();

        // ── SuperAdmin: System health ──
        public bool AdSyncEnabled { get; set; }
        public DateTime? LastAdSyncTime { get; set; }
        public bool LastAdSyncSuccess { get; set; }
        public string? LastAdSyncError { get; set; }
        /// <summary>Failed login attempts in the last 24 hours</summary>
        public int RecentFailedLogins { get; set; }
        /// <summary>Total audit log entries</summary>
        public int TotalActivityLogs { get; set; }
        /// <summary>Activity logs in the last 24 hours</summary>
        public int ActivityLogs24h { get; set; }
    }

    public class DashboardRecentItem
    {
        public int TrackingId { get; set; }
        public string? CustomTrackingId { get; set; }
        public DateTime DateFound { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string LocationFound { get; set; } = string.Empty;
        public string StatusName { get; set; } = string.Empty;
        public int DaysSinceFound { get; set; }
        public string? ClaimedBy { get; set; }
        public string? CreatedBy { get; set; }
    }

    public class StatusBreakdownItem
    {
        public string StatusName { get; set; } = string.Empty;
        public int Count { get; set; }
        public string CssClass { get; set; } = string.Empty;
        public int Percentage { get; set; }
    }

    public class TopItemType
    {
        public string ItemName { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class UserPerformanceItem
    {
        public string UserName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int ItemsCreated { get; set; }
        public int ItemsThisWeek { get; set; }
    }

    public class StorageUtilItem
    {
        public string LocationName { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public int Capacity { get; set; } // For future use
    }
}
