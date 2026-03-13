namespace LostAndFoundApp.Services
{
    /// <summary>
    /// Simple timestamp formatting service. All timestamps use the server's
    /// local time — no conversion needed.
    /// </summary>
    public class TimeZoneService
    {
        /// <summary>
        /// Format a DateTime as a display string.
        /// Default format: MM/dd/yyyy hh:mm tt (e.g. 03/03/2026 10:15 AM)
        /// </summary>
        public string Format(DateTime dateTime, string format = "MM/dd/yyyy hh:mm tt")
        {
            return dateTime.ToString(format);
        }

        public string Format(DateTime? dateTime, string format = "MM/dd/yyyy hh:mm tt")
        {
            if (!dateTime.HasValue) return "—";
            return Format(dateTime.Value, format);
        }
    }
}
