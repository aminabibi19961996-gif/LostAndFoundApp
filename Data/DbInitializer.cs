using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LostAndFoundApp.Models;

namespace LostAndFoundApp.Data
{
    /// <summary>
    /// Seeds the database with roles, default accounts for all three roles,
    /// default master data, and default AD security groups on first run.
    /// </summary>
    public static class DbInitializer
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

            // ─── Seed Roles ────────────────────────────────────────
            // SuperAdmin: full system control (not from AD, local-only)
            // Admin: administrative access (from AD group "LostandFound Admin")
            // User: standard data entry (from AD group "LostandFound User")
            string[] roles = { "SuperAdmin", "Admin", "User" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                    logger.LogInformation("Seeded role '{Role}'.", role);
                }
            }

            // ─── Seed Default Accounts ─────────────────────────────
            // Account 1: Super Admin — full system control
            await SeedUserAsync(userManager, logger, new SeedAccount
            {
                UserName = "sadmin",
                Email = "sadmin@lostandfound.local",
                DisplayName = "System Administrator",
                Password = "Rider@2025",
                Role = "SuperAdmin",
                MustChangePassword = true
            });

            // Account 2: Admin — administrative access
            await SeedUserAsync(userManager, logger, new SeedAccount
            {
                UserName = "admin",
                Email = "admin@lostandfound.local",
                DisplayName = "Admin User",
                Password = "Rider@2025",
                Role = "Admin",
                MustChangePassword = true
            });

            // Account 3: User — standard data entry operator
            await SeedUserAsync(userManager, logger, new SeedAccount
            {
                UserName = "user",
                Email = "user@lostandfound.local",
                DisplayName = "Staff User",
                Password = "Rider@2025",
                Role = "User",
                MustChangePassword = true
            });

            // ─── Seed Default Status Values ────────────────────────
            if (!await context.Statuses.AnyAsync())
            {
                context.Statuses.AddRange(
                    new Status { Name = "Found" },
                    new Status { Name = "Claimed" },
                    new Status { Name = "Disposed" },
                    new Status { Name = "Stored" },
                    new Status { Name = "Transferred" }
                );
                await context.SaveChangesAsync();
            }

            // ─── Seed Default Item Types ───────────────────────────
            if (!await context.Items.AnyAsync())
            {
                context.Items.AddRange(
                    new Item { Name = "Wallet" },
                    new Item { Name = "Phone" },
                    new Item { Name = "Keys" },
                    new Item { Name = "Bag" },
                    new Item { Name = "Umbrella" },
                    new Item { Name = "Clothing" },
                    new Item { Name = "Electronics" },
                    new Item { Name = "Jewelry" },
                    new Item { Name = "Documents" },
                    new Item { Name = "Other" }
                );
                await context.SaveChangesAsync();
            }

            // ─── Seed Default Storage Locations ────────────────────
            if (!await context.StorageLocations.AnyAsync())
            {
                context.StorageLocations.AddRange(
                    new StorageLocation { Name = "Main Office" },
                    new StorageLocation { Name = "Storage Room A" },
                    new StorageLocation { Name = "Storage Room B" },
                    new StorageLocation { Name = "Security Desk" }
                );
                await context.SaveChangesAsync();
            }

            // ─── Seed Default Routes ──────────────────────────────
            if (!await context.Routes.AnyAsync())
            {
                context.Routes.AddRange(
                    new Models.Route { Name = "Route 1" },
                    new Models.Route { Name = "Route 2" },
                    new Models.Route { Name = "Route 5" },
                    new Models.Route { Name = "Route 10" },
                    new Models.Route { Name = "Route 15" },
                    new Models.Route { Name = "Express A" }
                );
                await context.SaveChangesAsync();
            }

            // ─── Seed Default Vehicles ─────────────────────────────
            if (!await context.Vehicles.AnyAsync())
            {
                context.Vehicles.AddRange(
                    new Vehicle { Name = "Bus 101" },
                    new Vehicle { Name = "Bus 102" },
                    new Vehicle { Name = "Bus 201" },
                    new Vehicle { Name = "Bus 202" },
                    new Vehicle { Name = "Bus 301" },
                    new Vehicle { Name = "Van 01" }
                );
                await context.SaveChangesAsync();
            }

            // ─── Seed Default Found By Names ──────────────────────
            if (!await context.FoundByNames.AnyAsync())
            {
                context.FoundByNames.AddRange(
                    new FoundByName { Name = "Driver" },
                    new FoundByName { Name = "Cleaning Staff" },
                    new FoundByName { Name = "Security" },
                    new FoundByName { Name = "Station Attendant" },
                    new FoundByName { Name = "Passenger" },
                    new FoundByName { Name = "Maintenance" }
                );
                await context.SaveChangesAsync();
            }

            // ─── Seed Default AD Security Groups ──────────────────
            // Two security groups with mapped application roles
            if (!await context.AdGroups.AnyAsync())
            {
                context.AdGroups.AddRange(
                    new AdGroup
                    {
                        GroupName = "LostandFound Admin",
                        MappedRole = "Admin",
                        IsActive = true,
                        DateAdded = DateTime.UtcNow
                    },
                    new AdGroup
                    {
                        GroupName = "LostandFound User",
                        MappedRole = "User",
                        IsActive = true,
                        DateAdded = DateTime.UtcNow
                    }
                );
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded 2 default AD security groups with role mappings.");
            }
        }

        /// <summary>
        /// Creates a user account if it does not already exist, then assigns the specified role.
        /// </summary>
        private static async Task SeedUserAsync(UserManager<ApplicationUser> userManager, ILogger logger, SeedAccount account)
        {
            var existing = await userManager.FindByNameAsync(account.UserName);
            if (existing != null) return;

            var user = new ApplicationUser
            {
                UserName = account.UserName,
                Email = account.Email,
                EmailConfirmed = true,
                DisplayName = account.DisplayName,
                IsAdUser = false,
                MustChangePassword = account.MustChangePassword,
                IsActive = true
            };

            var result = await userManager.CreateAsync(user, account.Password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, account.Role);
                logger.LogInformation("Seeded user '{UserName}' with role '{Role}'.", account.UserName, account.Role);
            }
            else
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                logger.LogError("Failed to seed user '{UserName}': {Errors}", account.UserName, errors);
            }
        }

        /// <summary>Helper class for seeding user accounts.</summary>
        private class SeedAccount
        {
            public string UserName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public bool MustChangePassword { get; set; }
        }
    }
}
