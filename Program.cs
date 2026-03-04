using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using LostAndFoundApp.Data;
using LostAndFoundApp.Models;
using LostAndFoundApp.Services;
using LostAndFoundApp.Middleware;
using Serilog;
using ServerLog = Serilog.Log; // Alias to avoid confusion if needed
using System.IO;
using System.Text.RegularExpressions;

// Pre-sanitize appsettings.json to fix common true/false boolean capitalization bugs 
// which break the .NET strict JSON parser and crash IIS/Kestrel on boot permanently
try
{
    foreach (var file in Directory.GetFiles(Directory.GetCurrentDirectory(), "appsettings*.json"))
    {
        var content = File.ReadAllText(file);
        // Look for values that start with a capital T or F after a colon (meaning a boolean value)
        var sanitized = Regex.Replace(content, @"(?<=\:\s*)True\b", "true");
        sanitized = Regex.Replace(sanitized, @"(?<=\:\s*)False\b", "false");
        if (content != sanitized)
        {
            File.WriteAllText(file, sanitized);
        }
    }
}
catch { /* Ignore read/write locks, let builder handle failures normally */ }

var builder = WebApplication.CreateBuilder(args);

// --- Serilog Configuration ---
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine("Logs", "log-.txt"),
        rollingInterval: RollingInterval.Day,
        encoding: System.Text.Encoding.UTF8,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
builder.Host.UseSerilog();

// --- Database (MSSQL via Entity Framework Core) ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found in appsettings.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null);
    }));

Log.Information("Using MSSQL database.");
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// --- ASP.NET Core Identity ---
var lockoutAttempts = builder.Configuration.GetValue<int>("Identity:MaxFailedAccessAttempts", 5);
var lockoutMinutes = builder.Configuration.GetValue<int>("Identity:LockoutMinutes", 15);

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Password validation is now handled by DatabasePasswordValidator which reads from DB.
    // Set Identity's built-in rules to minimum so they don't conflict with the DB-driven policy.
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 1; // DatabasePasswordValidator enforces the real minimum

    // Lockout policy — configurable via appsettings
    options.Lockout.MaxFailedAccessAttempts = lockoutAttempts;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(lockoutMinutes);
    options.Lockout.AllowedForNewUsers = true;

    // User settings
    options.User.RequireUniqueEmail = false; // AD users may share email patterns
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// --- Cookie configuration ---
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// --- Authorization policies for role-based access control ---
// 4 roles: SuperAdmin, Admin, Supervisor, User
//
// Permission Matrix:
//   Feature                            SuperAdmin  Admin  Supervisor     User
//   ─────────────────────────────────────────────────────────────────────────
//   Dashboard (Full Analytics)           ✓          ✓       Partial     Basic
//   Dashboard (System Health)            ✓          ✗          ✗          ✗
//   Dashboard (Team Overview)            ✓          ✓          ✓          ✗
//   Dashboard (Admin Analytics)          ✓          ✓          ✗          ✗
//   Create Lost & Found Records          ✓          ✓          ✓          ✓
//   Edit Any Record                      ✓          ✓          ✓     Own Only*
//   Delete Records (non-protected)       ✓          ✓          ✗          ✗
//   [NOTE] Claimed/Disposed records      ✗          ✗          ✗          ✗
//          are PERMANENTLY PROTECTED     (no role can delete these — audit guard)
//   Export Records CSV                   ✓          ✓          ✓          ✗
//   Print Search (full results)          ✓          ✓          ✓          ✗
//   Bulk Delete / Bulk Status Update     ✓          ✓          ✗          ✗
//   Manage Master Data (full CRUD)       ✓          ✓          ✓          ✗
//   Inline AJAX Master Data              ✓          ✓          ✓          ✗
//   View All Activity Logs               ✓          ✓          ✗          ✗
//   View User+Supervisor Logs            ✓          ✓          ✓          ✗
//   Export Logs (CSV)                    ✓          ✓          ✗          ✗
//   Clear All Logs                       ✓          ✗          ✗          ✗
//   View User List                       ✓          ✓     ReadOnly        ✗
//   Create/Edit/Delete Users             ✓          ✓          ✗          ✗
//   Change User Roles                    ✓          ✓          ✗          ✗
//   Activate/Deactivate Users            ✓          ✓          ✗          ✗
//   Manage AD Groups / AD Users          ✓          ✓          ✗          ✗
//   Trigger AD Sync                      ✓          ✓          ✗          ✗
//   Configure Password Policy            ✓          ✗          ✗          ✗
//   Manage Announcements (Create/Delete) ✓          ✓          ✓          ✗
//   Receive Announcement Popups          ✗**        ✓          ✓          ✓
//   Personal Announcement Inbox          ✗**        ✗**        ✗**        ✓
//
// * User role (Own Only edit): enforced in LostFoundItemController.Edit().
//   Supervisor, Admin, and SuperAdmin can edit any record.
// ** SuperAdmin/Admin/Supervisor manage announcements via /Announcement/Index
//    rather than the personal inbox. SuperAdmin never receives popups by design
//    (they are the operators, not the target audience).
//
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireSuperAdmin", policy => policy.RequireRole("SuperAdmin"));
    options.AddPolicy("RequireAdminOrAbove", policy => policy.RequireRole("SuperAdmin", "Admin"));
    options.AddPolicy("RequireSupervisorOrAbove", policy => policy.RequireRole("SuperAdmin", "Admin", "Supervisor"));
    options.AddPolicy("RequireAnyRole", policy => policy.RequireRole("SuperAdmin", "Admin", "Supervisor", "User"));
});

// --- Application services ---
builder.Services.AddScoped<FileService>();
builder.Services.AddScoped<AdSyncService>();
builder.Services.AddScoped<ActivityLogService>();
builder.Services.AddSingleton<AdLoginRateLimiter>();
builder.Services.AddSingleton<TimeZoneService>();
builder.Services.AddScoped<ItemDiffService>();

// Custom password validator that reads policy from database (Gap 1 fix)
// Replaces Identity's hardcoded password rules with dynamic, SuperAdmin-configurable policy
builder.Services.AddScoped<IPasswordValidator<ApplicationUser>, DatabasePasswordValidator>();

// --- Daily AD Sync Background Service (only if AD integration is enabled) ---
if (builder.Configuration.GetValue<bool>("ActiveDirectory:Enabled", false))
{
    builder.Services.AddHostedService<AdSyncHostedService>();
    Log.Information("Active Directory sync is enabled.");
}
else
{
    Log.Information("Active Directory sync is disabled. Set ActiveDirectory:Enabled to true in appsettings to enable.");
}

// --- Configure antiforgery to accept token from AJAX header ---
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

// --- MVC ---
// --- MVC with Razor runtime compilation (for development) ---
var mvcBuilder = builder.Services.AddControllersWithViews();
if (builder.Environment.IsDevelopment())
{
    mvcBuilder.AddRazorRuntimeCompilation();
}

var app = builder.Build();

// --- Apply Database Schema on Startup (MSSQL migrations) ---
try
{
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();
        Log.Information("Database migrations applied successfully.");
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Database initialization failed. Application cannot start.");
    throw; // Let it crash — can't run without a database
}

// --- Seed database on startup ---
if (Environment.GetEnvironmentVariable("SEED_DATABASE") == "true" || app.Environment.IsDevelopment())
{
    await DbInitializer.SeedAsync(app.Services);
}

// --- HTTP pipeline ---
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios.
    app.UseHsts();
}
// Handle 404, 403, etc. with a friendly error page instead of blank/browser-default pages
app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseRouting();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

// MustChangePassword middleware runs after auth so we know who the user is
app.UseMustChangePassword();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
