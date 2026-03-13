using System.Collections.Concurrent;

namespace LostAndFoundApp.Services
{
    /// <summary>
    /// In-memory rate limiter for Active Directory login attempts.
    /// Tracks failed attempts per username and blocks further attempts after the threshold is exceeded.
    /// This provides defense-in-depth since AD login bypasses ASP.NET Identity's lockout mechanism.
    /// Registered as a singleton so state persists across requests.
    /// </summary>
    public class AdLoginRateLimiter
    {
        private readonly ConcurrentDictionary<string, LoginAttemptInfo> _attempts = new(StringComparer.OrdinalIgnoreCase);

        // Configurable via constructor or appsettings
        private readonly int _maxAttempts;
        private readonly TimeSpan _lockoutDuration;
        private readonly TimeSpan _cleanupInterval;
        private DateTime _lastCleanup = DateTime.Now;

        public AdLoginRateLimiter(IConfiguration config)
        {
            _maxAttempts = config.GetValue<int>("ActiveDirectory:MaxFailedLoginAttempts", 5);
            _lockoutDuration = TimeSpan.FromMinutes(config.GetValue<int>("ActiveDirectory:LoginLockoutMinutes", 15));
            _cleanupInterval = TimeSpan.FromMinutes(30);
        }

        /// <summary>
        /// Check if the user is currently locked out from AD login attempts.
        /// Returns true if the user is blocked, along with the remaining lockout time.
        /// </summary>
        public (bool IsLocked, TimeSpan? RemainingTime) IsLockedOut(string username)
        {
            CleanupIfNeeded();

            if (_attempts.TryGetValue(username, out var info))
            {
                if (info.LockedUntil.HasValue && info.LockedUntil.Value > DateTime.Now)
                {
                    var remaining = info.LockedUntil.Value - DateTime.Now;
                    return (true, remaining);
                }

                // Lockout expired — reset
                if (info.LockedUntil.HasValue && info.LockedUntil.Value <= DateTime.Now)
                {
                    _attempts.TryRemove(username, out _);
                }
            }

            return (false, null);
        }

        /// <summary>
        /// Record a failed AD login attempt. If the threshold is exceeded, the user is locked out.
        /// </summary>
        public void RecordFailedAttempt(string username)
        {
            var info = _attempts.GetOrAdd(username, _ => new LoginAttemptInfo());

            lock (info)
            {
                info.FailedCount++;
                info.LastAttempt = DateTime.Now;

                if (info.FailedCount >= _maxAttempts)
                {
                    info.LockedUntil = DateTime.Now.Add(_lockoutDuration);
                }
            }
        }

        /// <summary>
        /// Clear the failed attempt counter for a user after a successful login.
        /// </summary>
        public void RecordSuccessfulLogin(string username)
        {
            _attempts.TryRemove(username, out _);
        }

        /// <summary>
        /// Get the number of remaining attempts before lockout.
        /// </summary>
        public int GetRemainingAttempts(string username)
        {
            if (_attempts.TryGetValue(username, out var info))
            {
                return Math.Max(0, _maxAttempts - info.FailedCount);
            }
            return _maxAttempts;
        }

        /// <summary>
        /// Periodically remove expired entries to prevent unbounded memory growth.
        /// </summary>
        private void CleanupIfNeeded()
        {
            if (DateTime.Now - _lastCleanup < _cleanupInterval) return;
            _lastCleanup = DateTime.Now;

            var expiredKeys = _attempts
                .Where(kvp => kvp.Value.LockedUntil.HasValue && kvp.Value.LockedUntil.Value < DateTime.Now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _attempts.TryRemove(key, out _);
            }

            // Also remove stale entries with no lockout that haven't been used in a while
            var staleKeys = _attempts
                .Where(kvp => !kvp.Value.LockedUntil.HasValue && kvp.Value.LastAttempt < DateTime.Now.AddHours(-1))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                _attempts.TryRemove(key, out _);
            }
        }

        private class LoginAttemptInfo
        {
            public int FailedCount { get; set; }
            public DateTime LastAttempt { get; set; } = DateTime.Now;
            public DateTime? LockedUntil { get; set; }
        }
    }
}
