using System.DirectoryServices.AccountManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using LostAndFoundApp.Data;
using LostAndFoundApp.Models;

namespace LostAndFoundApp.Services
{
    /// <summary>
    /// Service responsible for synchronizing users from configured Active Directory groups
    /// into the local application database. Each AD group maps to a specific application role.
    /// Also handles runtime AD credential validation.
    /// </summary>
    public class AdSyncService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AdSyncService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        // Valid roles that can be mapped from AD groups
        private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Admin", "User"
        };

        public AdSyncService(IConfiguration config, ILogger<AdSyncService> logger, IServiceScopeFactory scopeFactory)
        {
            _config = config;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Validates user credentials against Active Directory in real time.
        /// Returns true if the credentials are valid, false otherwise.
        /// AD credentials are never stored locally.
        /// </summary>
        public bool ValidateAdCredentials(string username, string password)
        {
            if (!_config.GetValue<bool>("ActiveDirectory:Enabled", false))
            {
                _logger.LogWarning("AD credential validation skipped — Active Directory integration is disabled.");
                return false;
            }

            try
            {
                var domain = _config["ActiveDirectory:Domain"];
                var container = _config["ActiveDirectory:Container"];
                var useSsl = _config.GetValue<bool>("ActiveDirectory:UseSSL", false);

                if (string.IsNullOrEmpty(domain))
                {
                    _logger.LogError("Active Directory domain is not configured in appsettings.");
                    return false;
                }

                var options = useSsl ? ContextOptions.Negotiate | ContextOptions.SecureSocketLayer : ContextOptions.Negotiate;
                using var context = new PrincipalContext(ContextType.Domain, domain, container, options);
                
                var isValid = context.ValidateCredentials(username, password);
                _logger.LogInformation("AD credential validation for user '{User}': {Result}", username, isValid ? "Success" : "Failed");
                return isValid;
            }
            catch (PrincipalServerDownException ex)
            {
                _logger.LogError(ex, "Active Directory server is not reachable for credential validation.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating AD credentials for user '{User}'.", username);
                return false;
            }
        }

        /// <summary>
        /// Synchronizes users from all configured AD groups into the local database.
        /// New members are created with IsAdUser=true and assigned the role mapped to their AD group.
        /// Members no longer in any group are flagged inactive.
        /// Role mapping: Each AD group has a MappedRole — users get the highest-priority role
        /// if they appear in multiple groups.
        /// Returns a summary of the sync operation.
        /// </summary>
        public async Task<AdSyncResult> SyncUsersAsync()
        {
            var result = new AdSyncResult();

            if (!_config.GetValue<bool>("ActiveDirectory:Enabled", false))
            {
                result.Errors.Add("Active Directory integration is disabled. Set ActiveDirectory:Enabled to true in appsettings to enable.");
                return result;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

                var domain = _config["ActiveDirectory:Domain"];
                var container = _config["ActiveDirectory:Container"];
                var useSsl = _config.GetValue<bool>("ActiveDirectory:UseSSL", false);

                if (string.IsNullOrEmpty(domain))
                {
                    result.Errors.Add("Active Directory domain is not configured.");
                    return result;
                }

                var adGroups = await context.AdGroups.Where(g => g.IsActive).ToListAsync();
                if (!adGroups.Any())
                {
                    result.Errors.Add("No active AD groups configured for synchronization.");
                    return result;
                }

                // Track which users were found and their highest-priority role
                // Priority: Admin > User
                // NOTE: We store only extracted strings (not UserPrincipal references) because
                // UserPrincipal objects are disposed at the end of each iteration's `using` block.
                var userRoleMap = new Dictionary<string, (string Role, string DisplayName, string? Email)>(StringComparer.OrdinalIgnoreCase);
                bool hadGroupProcessingErrors = false;

                var options = useSsl ? ContextOptions.Negotiate | ContextOptions.SecureSocketLayer : ContextOptions.Negotiate;
                using var principalContext = new PrincipalContext(ContextType.Domain, domain, container, options);

                foreach (var adGroup in adGroups)
                {
                    try
                    {
                        using var group = GroupPrincipal.FindByIdentity(principalContext, adGroup.GroupName);
                        if (group == null)
                        {
                            hadGroupProcessingErrors = true;
                            result.Errors.Add($"AD group '{adGroup.GroupName}' not found in directory.");
                            _logger.LogWarning("AD group '{GroupName}' not found in directory — skipping. User deactivation will be skipped for safety.", adGroup.GroupName);
                            continue;
                        }

                        var mappedRole = ValidRoles.Contains(adGroup.MappedRole) ? adGroup.MappedRole : "User";

                        using var members = group.GetMembers(true);
                        foreach (var member in members)
                        {
                            using (member)
                            {
                                if (member is UserPrincipal userPrincipal)
                                {
                                    var samAccountName = userPrincipal.SamAccountName;
                                    if (string.IsNullOrEmpty(samAccountName))
                                        continue;

                                    // Extract all needed values NOW, before the using block disposes the principal
                                    var displayName = userPrincipal.DisplayName ?? samAccountName;
                                    var email = userPrincipal.EmailAddress;

                                    // If user is in multiple groups, assign the highest-priority role
                                    if (userRoleMap.TryGetValue(samAccountName, out var existing))
                                    {
                                        if (GetRolePriority(mappedRole) > GetRolePriority(existing.Role))
                                        {
                                            userRoleMap[samAccountName] = (mappedRole, displayName, email);
                                        }
                                    }
                                    else
                                    {
                                        userRoleMap[samAccountName] = (mappedRole, displayName, email);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        hadGroupProcessingErrors = true;
                        result.Errors.Add($"Error processing AD group '{adGroup.GroupName}': {ex.Message}");
                        _logger.LogError(ex, "Error processing AD group '{GroupName}'", adGroup.GroupName);
                    }
                }

                // Process all discovered users
                var syncedSamAccountNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var (samAccountName, userData) in userRoleMap)
                {
                    syncedSamAccountNames.Add(samAccountName);

                    var existingUser = await userManager.FindByNameAsync(samAccountName);
                    if (existingUser == null)
                    {
                        // Create new AD-synced user
                        var newUser = new ApplicationUser
                        {
                            UserName = samAccountName,
                            Email = userData.Email ?? $"{samAccountName}@{domain}",
                            EmailConfirmed = true,
                            DisplayName = userData.DisplayName,
                            IsAdUser = true,
                            MustChangePassword = false,
                            IsActive = true,
                            SamAccountName = samAccountName
                        };

                        var createResult = await userManager.CreateAsync(newUser);
                        if (createResult.Succeeded)
                        {
                            // Assign the mapped role from AD group
                            await userManager.AddToRoleAsync(newUser, userData.Role);
                            result.UsersCreated++;
                            _logger.LogInformation("Created AD user '{User}' with role '{Role}'.", samAccountName, userData.Role);
                        }
                        else
                        {
                            result.Errors.Add($"Failed to create user '{samAccountName}': {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
                        }
                    }
                    else
                    {
                        bool updated = false;

                        // Update display name and email from AD
                        if (existingUser.DisplayName != userData.DisplayName && !string.IsNullOrEmpty(userData.DisplayName))
                        {
                            existingUser.DisplayName = userData.DisplayName;
                            updated = true;
                        }
                        if (existingUser.Email != userData.Email && !string.IsNullOrEmpty(userData.Email))
                        {
                            existingUser.Email = userData.Email;
                            updated = true;
                        }
                        // Reactivate if previously deactivated
                        if (!existingUser.IsActive)
                        {
                            existingUser.IsActive = true;
                            updated = true;
                        }

                        // Update role if it changed
                        var currentRoles = await userManager.GetRolesAsync(existingUser);
                        var currentRole = currentRoles.FirstOrDefault();
                        if (currentRole != userData.Role)
                        {
                            if (currentRoles.Any())
                                await userManager.RemoveFromRolesAsync(existingUser, currentRoles);
                            await userManager.AddToRoleAsync(existingUser, userData.Role);
                            result.RolesUpdated++;
                            _logger.LogInformation("Updated role for AD user '{User}' from '{OldRole}' to '{NewRole}'.",
                                samAccountName, currentRole ?? "None", userData.Role);
                            updated = true;
                        }

                        if (updated)
                        {
                            await userManager.UpdateAsync(existingUser);
                            result.UsersUpdated++;
                        }
                    }
                }

                // Flag AD users no longer in any configured group as inactive.
                // IMPORTANT: Only deactivate when ALL groups were processed successfully.
                // If any group had a processing error, we cannot safely determine which
                // users should be deactivated — a user might belong to a group that failed.
                if (hadGroupProcessingErrors)
                {
                    _logger.LogWarning("Skipping user deactivation because one or more AD groups had processing errors.");
                }
                else
                {
                    var allAdUsers = await context.Users
                        .Where(u => u.IsAdUser && u.IsActive)
                        .ToListAsync();

                    foreach (var adUser in allAdUsers)
                    {
                        if (!string.IsNullOrEmpty(adUser.SamAccountName) &&
                            !syncedSamAccountNames.Contains(adUser.SamAccountName))
                        {
                            adUser.IsActive = false;
                            result.UsersDeactivated++;
                        }
                    }
                }

                await context.SaveChangesAsync();
                result.Success = true;
                _logger.LogInformation("AD sync completed. Created: {Created}, Updated: {Updated}, Deactivated: {Deactivated}, Roles Updated: {RolesUpdated}",
                    result.UsersCreated, result.UsersUpdated, result.UsersDeactivated, result.RolesUpdated);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"AD sync failed: {ex.Message}");
                _logger.LogError(ex, "AD sync failed with unexpected error.");
            }

            return result;
        }

        /// <summary>
        /// Returns a numeric priority for role comparison. Higher = more privileged.
        /// </summary>
        private static int GetRolePriority(string role) => role switch
        {
            "Admin" => 2,
            "User" => 1,
            _ => 0
        };
    }

    /// <summary>
    /// Result object for AD synchronization operations
    /// </summary>
    public class AdSyncResult
    {
        public bool Success { get; set; }
        public int UsersCreated { get; set; }
        public int UsersUpdated { get; set; }
        public int UsersDeactivated { get; set; }
        public int RolesUpdated { get; set; }
        public List<string> Errors { get; set; } = new();

        public string Summary =>
            $"Sync {(Success ? "completed" : "failed")}. " +
            $"Created: {UsersCreated}, Updated: {UsersUpdated}, Deactivated: {UsersDeactivated}, Roles Updated: {RolesUpdated}." +
            (Errors.Any() ? $" Errors: {string.Join("; ", Errors)}" : "");
    }
}
