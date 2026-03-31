using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using LostAndFoundApp.Controllers;
using LostAndFoundApp.Data;
using LostAndFoundApp.Models;
using LostAndFoundApp.Services;
using Xunit;
using System.Security.Claims;

namespace LostAndFoundApp.Tests;

public class RetentionCountTests : IDisposable
{
    private static int GetCount(JsonResult json) =>
        (int)json.Value!.GetType().GetProperty("count")!.GetValue(json.Value)!;

    private readonly ApplicationDbContext _context;
    private readonly UserManagementController _controller;

    public RetentionCountTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);

        var mockUserManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null, null, null, null, null, null, null, null);

        var mockRoleManager = new Mock<RoleManager<IdentityRole>>(
            Mock.Of<IRoleStore<IdentityRole>>(),
            null, null, null, null);

        var adSyncService = new AdSyncService(
            Mock.Of<IConfiguration>(),
            Mock.Of<ILogger<AdSyncService>>(),
            Mock.Of<IServiceScopeFactory>());

        var activityLogService = new ActivityLogService(
            _context,
            Mock.Of<ILogger<ActivityLogService>>());

        _controller = new UserManagementController(
            mockUserManager.Object,
            mockRoleManager.Object,
            _context,
            adSyncService,
            activityLogService,
            Mock.Of<IConfiguration>(),
            Mock.Of<ILogger<UserManagementController>>());
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // =====================================================================
    // LogRetentionCount — valid days
    // =====================================================================

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    public async Task LogRetentionCount_WithAllowedDays_ReturnsJsonCount(int days)
    {
        _context.ActivityLogs.AddRange(
            new ActivityLog { Timestamp = DateTime.UtcNow.AddDays(-(days + 5)), Action = "Old", Details = "Old log", PerformedBy = "admin", Category = "Test" },
            new ActivityLog { Timestamp = DateTime.UtcNow.AddDays(-(days + 1)), Action = "Old", Details = "Old log", PerformedBy = "admin", Category = "Test" },
            new ActivityLog { Timestamp = DateTime.UtcNow.AddDays(-1),          Action = "New", Details = "New log", PerformedBy = "admin", Category = "Test" }
        );
        await _context.SaveChangesAsync();

        var result = await _controller.LogRetentionCount(days);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(2, GetCount(json));
    }

    [Fact]
    public async Task LogRetentionCount_WithNoOldRecords_ReturnsZero()
    {
        _context.ActivityLogs.Add(
            new ActivityLog { Timestamp = DateTime.UtcNow, Action = "Recent", Details = "Recent log", PerformedBy = "admin", Category = "Test" });
        await _context.SaveChangesAsync();

        var result = await _controller.LogRetentionCount(30);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(0, GetCount(json));
    }

    // =====================================================================
    // LogRetentionCount — invalid days
    // =====================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(45)]
    [InlineData(100)]
    [InlineData(-1)]
    [InlineData(365)]
    public async Task LogRetentionCount_WithDisallowedDays_ReturnsBadRequest(int days)
    {
        var result = await _controller.LogRetentionCount(days);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // =====================================================================
    // ItemRetentionCount — valid days
    // =====================================================================

    [Theory]
    [InlineData(365)]
    [InlineData(730)]
    public async Task ItemRetentionCount_WithAllowedDays_ReturnsJsonCount(int days)
    {
        _context.LostFoundItems.AddRange(
            new LostFoundItem { CustomTrackingId = "OLD-001", DateFound = DateTime.UtcNow.AddDays(-(days + 10)), CreatedDateTime = DateTime.UtcNow.AddDays(-(days + 10)), ItemId = 1, StatusId = 1, LocationFound = "Bus" },
            new LostFoundItem { CustomTrackingId = "OLD-002", DateFound = DateTime.UtcNow.AddDays(-(days + 2)),  CreatedDateTime = DateTime.UtcNow.AddDays(-(days + 2)),  ItemId = 1, StatusId = 1, LocationFound = "Bus" },
            new LostFoundItem { CustomTrackingId = "NEW-001", DateFound = DateTime.UtcNow.AddDays(-5),           CreatedDateTime = DateTime.UtcNow.AddDays(-5),            ItemId = 1, StatusId = 1, LocationFound = "Bus" }
        );
        await _context.SaveChangesAsync();

        var result = await _controller.ItemRetentionCount(days);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(2, GetCount(json));
    }

    [Fact]
    public async Task ItemRetentionCount_WithNoOldRecords_ReturnsZero()
    {
        _context.LostFoundItems.Add(
            new LostFoundItem { CustomTrackingId = "NEW-001", DateFound = DateTime.UtcNow, CreatedDateTime = DateTime.UtcNow, ItemId = 1, StatusId = 1, LocationFound = "Bus" });
        await _context.SaveChangesAsync();

        var result = await _controller.ItemRetentionCount(365);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(0, GetCount(json));
    }

    // =====================================================================
    // ItemRetentionCount — invalid days
    // =====================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(-1)]
    [InlineData(1000)]
    public async Task ItemRetentionCount_WithDisallowedDays_ReturnsBadRequest(int days)
    {
        var result = await _controller.ItemRetentionCount(days);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // =====================================================================
    // Edge case: counts only records strictly older than cutoff
    // =====================================================================

    [Fact]
    public async Task LogRetentionCount_CountsOnlyRecordsOlderThanCutoff()
    {
        _context.ActivityLogs.AddRange(
            new ActivityLog { Timestamp = DateTime.UtcNow.AddDays(-31), Action = "A", Details = "d", PerformedBy = "u", Category = "C" },
            new ActivityLog { Timestamp = DateTime.UtcNow.AddDays(-30).AddMinutes(-1), Action = "A", Details = "d", PerformedBy = "u", Category = "C" },
            new ActivityLog { Timestamp = DateTime.UtcNow.AddDays(-29), Action = "A", Details = "d", PerformedBy = "u", Category = "C" },
            new ActivityLog { Timestamp = DateTime.UtcNow.AddDays(-10), Action = "A", Details = "d", PerformedBy = "u", Category = "C" }
        );
        await _context.SaveChangesAsync();

        var result = await _controller.LogRetentionCount(30);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(2, GetCount(json));
    }

    [Fact]
    public async Task ItemRetentionCount_CountsOnlyRecordsOlderThanCutoff()
    {
        _context.LostFoundItems.AddRange(
            new LostFoundItem { CustomTrackingId = "A", DateFound = DateTime.UtcNow, CreatedDateTime = DateTime.UtcNow.AddDays(-366), ItemId = 1, StatusId = 1, LocationFound = "X" },
            new LostFoundItem { CustomTrackingId = "B", DateFound = DateTime.UtcNow, CreatedDateTime = DateTime.UtcNow.AddDays(-365).AddMinutes(-1), ItemId = 1, StatusId = 1, LocationFound = "X" },
            new LostFoundItem { CustomTrackingId = "C", DateFound = DateTime.UtcNow, CreatedDateTime = DateTime.UtcNow.AddDays(-364), ItemId = 1, StatusId = 1, LocationFound = "X" },
            new LostFoundItem { CustomTrackingId = "D", DateFound = DateTime.UtcNow, CreatedDateTime = DateTime.UtcNow.AddDays(-1), ItemId = 1, StatusId = 1, LocationFound = "X" }
        );
        await _context.SaveChangesAsync();

        var result = await _controller.ItemRetentionCount(365);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(2, GetCount(json));
    }

    // =====================================================================
    // Manual Purge Tests - RunLogPurgeNow and RunItemPurgeNow
    // =====================================================================

    [Fact]
    public async Task RunLogPurgeNow_DeletesOldLogsAndReturnsRedirect()
    {
        // Arrange: Create retention settings with 30-day retention
        _context.LogRetentionSettings.Add(new LogRetentionSettings { RetentionDays = 30 });
        await _context.SaveChangesAsync();

        // Add old logs that should be deleted (older than 30 days)
        _context.ActivityLogs.AddRange(
            new ActivityLog { Timestamp = DateTime.UtcNow.AddDays(-40), Action = "Old1", Details = "Old log", PerformedBy = "admin", Category = "Test" },
            new ActivityLog { Timestamp = DateTime.UtcNow.AddDays(-35), Action = "Old2", Details = "Old log", PerformedBy = "admin", Category = "Test" },
            new ActivityLog { Timestamp = DateTime.UtcNow.AddDays(-5), Action = "New", Details = "Recent log", PerformedBy = "admin", Category = "Test" }
        );
        await _context.SaveChangesAsync();

        // Set up mock HttpContext with IServiceProvider that SHARES the same DbContext
        var services = new ServiceCollection();
        services.AddScoped(sp => _context); // Share the SAME context
        services.AddScoped(sp => Mock.Of<ILogger<LogRetentionHostedService>>());
        services.AddScoped<LogRetentionHostedService>();
        var serviceProvider = services.BuildServiceProvider();

        var mockHttpContext = new Mock<HttpContext>();
        mockHttpContext.Setup(c => c.RequestServices).Returns(serviceProvider);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = mockHttpContext.Object
        };

        // Act
        var result = await _controller.RunLogPurgeNow();

        // Assert
        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("LogRetentionSettings", redirectResult.ActionName);
        Assert.Equal(1, await _context.ActivityLogs.CountAsync()); // Only 1 recent log remains
    }

    [Fact]
    public async Task RunItemPurgeNow_DeletesOldItemsAndReturnsRedirect()
    {
        // Arrange: Create retention settings with 365-day retention
        _context.ItemRetentionSettings.Add(new ItemRetentionSettings { RetentionDays = 365 });
        await _context.SaveChangesAsync();

        // Add old items that should be deleted (older than 365 days)
        _context.LostFoundItems.AddRange(
            new LostFoundItem { CustomTrackingId = "OLD-001", DateFound = DateTime.UtcNow.AddDays(-400), CreatedDateTime = DateTime.UtcNow.AddDays(-400), ItemId = 1, StatusId = 1, LocationFound = "Bus" },
            new LostFoundItem { CustomTrackingId = "OLD-002", DateFound = DateTime.UtcNow.AddDays(-370), CreatedDateTime = DateTime.UtcNow.AddDays(-370), ItemId = 1, StatusId = 1, LocationFound = "Train" },
            new LostFoundItem { CustomTrackingId = "NEW-001", DateFound = DateTime.UtcNow.AddDays(-5), CreatedDateTime = DateTime.UtcNow.AddDays(-5), ItemId = 1, StatusId = 1, LocationFound = "Office" }
        );
        await _context.SaveChangesAsync();

        // Set up mock HttpContext with IServiceProvider that SHARES the same DbContext
        var services = new ServiceCollection();
        services.AddScoped(sp => _context); // Share the SAME context
        services.AddScoped(sp => Mock.Of<ILogger<ItemRetentionHostedService>>());
        services.AddScoped(sp => Mock.Of<IConfiguration>());
        services.AddScoped<FileService>();
        services.AddScoped<ItemRetentionHostedService>();
        var serviceProvider = services.BuildServiceProvider();

        var mockHttpContext = new Mock<HttpContext>();
        mockHttpContext.Setup(c => c.RequestServices).Returns(serviceProvider);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = mockHttpContext.Object
        };

        // Act
        var result = await _controller.RunItemPurgeNow();

        // Assert
        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ItemRetentionSettings", redirectResult.ActionName);
        Assert.Equal(1, await _context.LostFoundItems.CountAsync()); // Only 1 recent item remains
    }

    [Fact]
    public async Task RunLogPurgeNow_WithNoOldRecords_StillReturnsRedirect()
    {
        // Arrange: Create retention settings with 30-day retention
        _context.LogRetentionSettings.Add(new LogRetentionSettings { RetentionDays = 30 });
        await _context.SaveChangesAsync();

        // Add only recent logs (none older than 30 days)
        _context.ActivityLogs.Add(
            new ActivityLog { Timestamp = DateTime.UtcNow.AddDays(-5), Action = "Recent", Details = "Recent log", PerformedBy = "admin", Category = "Test" }
        );
        await _context.SaveChangesAsync();

        // Set up mock HttpContext with IServiceProvider that shares the same DbContext
        var services = new ServiceCollection();
        services.AddScoped(sp => _context);
        services.AddScoped(sp => Mock.Of<ILogger<LogRetentionHostedService>>());
        services.AddScoped<LogRetentionHostedService>();
        var serviceProvider = services.BuildServiceProvider();

        var mockHttpContext = new Mock<HttpContext>();
        mockHttpContext.Setup(c => c.RequestServices).Returns(serviceProvider);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = mockHttpContext.Object
        };

        // Act
        var result = await _controller.RunLogPurgeNow();

        // Assert
        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("LogRetentionSettings", redirectResult.ActionName);
        Assert.Equal(1, await _context.ActivityLogs.CountAsync()); // Log still exists
    }
}
