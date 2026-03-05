namespace LostAndFoundApp.Services
{
    /// <summary>
    /// Converts UTC timestamps (as stored in the database) to the configured
    /// application timezone for display. The timezone is set via "AppTimeZone"
    /// in appsettings.json using Windows timezone IDs (e.g. "Eastern Standard Time").
    /// This keeps all storage in UTC while showing local time to users.
    /// </summary>
    public class TimeZoneService
    {
        private readonly TimeZoneInfo _tz;

        public TimeZoneService(IConfiguration configuration)
        {
            // Read configured timezone, try IANA then Windows ID, with America/New_York as default
            var tzId = configuration.GetValue<string>("AppTimeZone");
            _tz = (!string.IsNullOrWhiteSpace(tzId) ? TryGetTz(tzId) : null)
               ?? TryGetTz("America/New_York")
               ?? TryGetTz("Eastern Standard Time")
               ?? TimeZoneInfo.Utc;
        }

        private static TimeZoneInfo? TryGetTz(string id)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { return null; }
        }

        /// <summary>The configured display timezone (e.g. Eastern Time).</summary>
        public TimeZoneInfo TimeZone => _tz;

        /// <summary>
        /// Convert a UTC DateTime to the configured application timezone.
        /// </summary>
        public DateTime ToAppTime(DateTime utcDateTime)
        {
            // If the kind is Unspecified (which EF Core returns for SQL Server datetime2),
            // treat it as UTC before converting to avoid double-conversion.
            if (utcDateTime.Kind == DateTimeKind.Unspecified)
                utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime.ToUniversalTime(), _tz);
        }

        /// <summary>
        /// Convert a nullable UTC DateTime to the configured timezone. Returns null if input is null.
        /// </summary>
        public DateTime? ToAppTime(DateTime? utcDateTime)
        {
            if (!utcDateTime.HasValue) return null;
            return ToAppTime(utcDateTime.Value);
        }

        /// <summary>
        /// Format a UTC DateTime as a display string in the configured timezone.
        /// Default format: MM/dd/yyyy hh:mm tt (e.g. 03/03/2026 10:15 AM ET)
        /// </summary>
        public string Format(DateTime utcDateTime, string format = "MM/dd/yyyy hh:mm tt")
        {
            return ToAppTime(utcDateTime).ToString(format);
        }

        /// <summary>
        /// Format a nullable UTC DateTime. Returns "—" if null.
        /// </summary>
        public string Format(DateTime? utcDateTime, string format = "MM/dd/yyyy hh:mm tt")
        {
            if (!utcDateTime.HasValue) return "—";
            return Format(utcDateTime.Value, format);
        }

        /// <summary>Short display name for the timezone, e.g. "ET".</summary>
        public string ShortName
        {
            get
            {
                // Use the abbreviation from the current moment (handles EST vs EDT automatically)
                var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);
                return _tz.IsDaylightSavingTime(now) ? "EDT" : "EST";
            }
        }
    }
}
