using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LostAndFoundApp.Data;
using LostAndFoundApp.Models;
using LostAndFoundApp.Services;
using LostAndFoundApp.ViewModels;

namespace LostAndFoundApp.Controllers
{
    /// <summary>
    /// Announcements management.
    /// SuperAdmin can create, toggle, and delete announcements targeted at Admin and/or User roles.
    /// All authenticated users can view their messages inbox, dismiss announcements, and receive popups.
    /// </summary>
    [Authorize]
    public class AnnouncementController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ActivityLogService _activityLogService;
        private readonly ILogger<AnnouncementController> _logger;

        public AnnouncementController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ActivityLogService activityLogService,
            ILogger<AnnouncementController> logger)
        {
            _context = context;
            _userManager = userManager;
            _activityLogService = activityLogService;
            _logger = logger;
        }

        // =====================================================================
        // SUPERADMIN — ANNOUNCEMENT MANAGEMENT
        // =====================================================================

        // GET: /Announcement
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> Index(int page = 1)
        {
            var pageSize = 100;
            var query = _context.Announcements.AsQueryable();

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var announcements = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var announcementIds = announcements.Select(a => a.Id).ToList();
            var readCounts = await _context.AnnouncementReads
                .Where(r => announcementIds.Contains(r.AnnouncementId))
                .GroupBy(r => r.AnnouncementId)
                .Select(g => new { AnnouncementId = g.Key, Count = g.Count() })
                .ToListAsync();

            var vm = new AnnouncementListViewModel
            {
                Announcements = announcements.Select(a => new AnnouncementListItem
                {
                    Id = a.Id,
                    Title = a.Title,
                    Message = a.Message,
                    TargetRole = a.TargetRole,
                    CreatedBy = a.CreatedBy,
                    CreatedAt = a.CreatedAt,
                    ExpiresAt = a.ExpiresAt,
                    IsActive = a.IsActive,
                    ReadCount = readCounts.FirstOrDefault(r => r.AnnouncementId == a.Id)?.Count ?? 0
                }).ToList(),
                TotalCount = totalCount,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = totalPages
            };

            return View(vm);
        }

        // GET: /Announcement/Create
        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public IActionResult Create()
        {
            return View(new CreateAnnouncementViewModel());
        }

        // POST: /Announcement/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> Create(CreateAnnouncementViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Past-date validation: ExpiresAt cannot be in the past
            if (model.ExpiresAt.HasValue && model.ExpiresAt.Value.Date < DateTime.Today)
            {
                ModelState.AddModelError("ExpiresAt", "Expiry date cannot be in the past.");
                return View(model);
            }

            var validTargets = new[] { "Admin", "Supervisor", "User", "All", "AdminAndAbove", "SupervisorAndAbove" };
            if (!validTargets.Contains(model.TargetRole))
            {
                ModelState.AddModelError("TargetRole", "Invalid target audience.");
                return View(model);
            }

            var announcement = new Announcement
            {
                Title = model.Title.Trim(),
                Message = model.Message.Trim(),
                TargetRole = model.TargetRole,
                CreatedBy = User.Identity?.Name ?? "System",
                CreatedAt = DateTime.Now,
                ExpiresAt = model.ExpiresAt,
                IsActive = true
            };

            _context.Announcements.Add(announcement);
            await _context.SaveChangesAsync();

            await _activityLogService.LogAsync(HttpContext, "Create Announcement",
                $"Created announcement '{announcement.Title}' targeting {announcement.TargetRole} roles.", "Announcement");
            _logger.LogInformation("Announcement '{Title}' created by '{User}' targeting {Target}.",
                announcement.Title, User.Identity?.Name, announcement.TargetRole);
            TempData["SuccessMessage"] = $"Announcement '{announcement.Title}' sent to {announcement.TargetRole} users.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Announcement/ToggleActive/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var announcement = await _context.Announcements.FindAsync(id);
            if (announcement == null) return NotFound();

            announcement.IsActive = !announcement.IsActive;
            await _context.SaveChangesAsync();

            var status = announcement.IsActive ? "activated" : "deactivated";
            await _activityLogService.LogAsync(HttpContext, "Toggle Announcement",
                $"Announcement '{announcement.Title}' has been {status}.", "Announcement");
            TempData["SuccessMessage"] = $"Announcement '{announcement.Title}' has been {status}.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Announcement/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> Delete(int id)
        {
            var announcement = await _context.Announcements.FindAsync(id);
            if (announcement == null) return NotFound();

            var title = announcement.Title;
            _context.Announcements.Remove(announcement);
            await _context.SaveChangesAsync();

            await _activityLogService.LogAsync(HttpContext, "Delete Announcement",
                $"Deleted announcement '{title}'.", "Announcement");
            _logger.LogInformation("Announcement '{Title}' deleted by '{User}'.", title, User.Identity?.Name);
            TempData["SuccessMessage"] = $"Announcement '{title}' has been deleted.";
            return RedirectToAction(nameof(Index));
        }

        // =====================================================================
        // ALL USERS — MESSAGE INBOX
        // =====================================================================

        // GET: /Announcement/Messages
        public async Task<IActionResult> Messages()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var userRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "User";

            // SuperAdmin, Admin, and Supervisor manage announcements from Index, not Messages
            if (userRole == "SuperAdmin" || userRole == "Admin" || userRole == "Supervisor")
                return RedirectToAction(nameof(Index));

            var now = DateTime.Now;

            var announcements = await _context.Announcements
                .Where(a => a.IsActive)
                .Where(a => a.ExpiresAt == null || a.ExpiresAt > now)
                .Where(a => a.TargetRole == "All"
                    || a.TargetRole == userRole
                    || (a.TargetRole == "AdminAndAbove" && (userRole == "Admin"))
                    || (a.TargetRole == "SupervisorAndAbove" && (userRole == "Admin" || userRole == "Supervisor")))
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            var announcementIds = announcements.Select(a => a.Id).ToList();
            var reads = await _context.AnnouncementReads
                .Where(r => r.UserId == user.Id && announcementIds.Contains(r.AnnouncementId))
                .ToListAsync();

            var messages = announcements.Select(a =>
            {
                var read = reads.FirstOrDefault(r => r.AnnouncementId == a.Id);
                return new UserMessageItem
                {
                    Id = read?.Id ?? 0,
                    AnnouncementId = a.Id,
                    Title = a.Title,
                    Message = a.Message,
                    CreatedBy = a.CreatedBy,
                    CreatedAt = a.CreatedAt,
                    IsDismissed = read?.DismissedAt != null,
                    DismissedAt = read?.DismissedAt
                };
            }).ToList();

            var vm = new UserMessageViewModel
            {
                Messages = messages,
                UnreadCount = messages.Count(m => !m.IsDismissed)
            };

            // NOTE: Removed logging on page view to prevent audit trail flooding
            // Logging every inbox visit creates too much noise

            return View(vm);
        }

        // POST: /Announcement/Dismiss/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Dismiss(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var announcement = await _context.Announcements.FindAsync(id);
            if (announcement == null) return NotFound();

            var userRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "User";
            if (announcement.TargetRole != "All"
                && announcement.TargetRole != userRole
                && !(announcement.TargetRole == "AdminAndAbove" && userRole == "Admin")
                && !(announcement.TargetRole == "SupervisorAndAbove" && (userRole == "Admin" || userRole == "Supervisor")))
                return NotFound();

            var existing = await _context.AnnouncementReads
                .FirstOrDefaultAsync(r => r.AnnouncementId == id && r.UserId == user.Id);

            if (existing != null)
            {
                existing.DismissedAt = DateTime.Now;
            }
            else
            {
                _context.AnnouncementReads.Add(new AnnouncementRead
                {
                    AnnouncementId = id,
                    UserId = user.Id,
                    PopupShownCount = 1,
                    FirstReadAt = DateTime.Now,
                    DismissedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            await _activityLogService.LogAsync(HttpContext, "Dismiss Announcement",
                $"User dismissed announcement '{announcement.Title}'.", "Announcement");
            TempData["SuccessMessage"] = "Announcement marked as read.";
            return RedirectToAction(nameof(Messages));
        }

        // POST: /Announcement/DismissAll — marks all unread announcements as read
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DismissAll()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var userRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "User";
            var now = DateTime.Now;

            var announcements = await _context.Announcements
                .Where(a => a.IsActive)
                .Where(a => a.ExpiresAt == null || a.ExpiresAt > now)
                .Where(a => a.TargetRole == "All"
                    || a.TargetRole == userRole
                    || (a.TargetRole == "AdminAndAbove" && (userRole == "Admin"))
                    || (a.TargetRole == "SupervisorAndAbove" && (userRole == "Admin" || userRole == "Supervisor")))
                .ToListAsync();

            var announcementIds = announcements.Select(a => a.Id).ToList();
            var existingReads = await _context.AnnouncementReads
                .Where(r => r.UserId == user.Id && announcementIds.Contains(r.AnnouncementId))
                .ToListAsync();

            foreach (var a in announcements)
            {
                var existing = existingReads.FirstOrDefault(r => r.AnnouncementId == a.Id);
                if (existing != null)
                {
                    if (existing.DismissedAt == null)
                        existing.DismissedAt = now;
                }
                else
                {
                    _context.AnnouncementReads.Add(new AnnouncementRead
                    {
                        AnnouncementId = a.Id,
                        UserId = user.Id,
                        PopupShownCount = 1,
                        FirstReadAt = now,
                        DismissedAt = now
                    });
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"All {announcements.Count} announcements marked as read.";
            return RedirectToAction(nameof(Messages));
        }

        // POST: /Announcement/DismissAjax/5 — AJAX endpoint for popup "Mark as Read"
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DismissAjax(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Json(new { success = false });

            var announcement = await _context.Announcements.FindAsync(id);
            if (announcement == null) return Json(new { success = false });

            var existing = await _context.AnnouncementReads
                .FirstOrDefaultAsync(r => r.AnnouncementId == id && r.UserId == user.Id);

            if (existing != null)
            {
                existing.DismissedAt = DateTime.Now;
            }
            else
            {
                _context.AnnouncementReads.Add(new AnnouncementRead
                {
                    AnnouncementId = id,
                    UserId = user.Id,
                    PopupShownCount = 1,
                    FirstReadAt = DateTime.Now,
                    DismissedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // =====================================================================
        // AJAX ENDPOINTS — POPUP SUPPORT
        // =====================================================================

        // GET: /Announcement/GetPopupAnnouncements
        [HttpGet]
        public async Task<IActionResult> GetPopupAnnouncements()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Json(new object[0]);

            var userRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "User";

            // SuperAdmin does not receive announcements
            if (userRole == "SuperAdmin")
                return Json(new object[0]);

            var now = DateTime.Now;

            var announcements = await _context.Announcements
                .Where(a => a.IsActive)
                .Where(a => a.ExpiresAt == null || a.ExpiresAt > now)
                .Where(a => a.TargetRole == "All"
                    || a.TargetRole == userRole
                    || (a.TargetRole == "AdminAndAbove" && (userRole == "Admin"))
                    || (a.TargetRole == "SupervisorAndAbove" && (userRole == "Admin" || userRole == "Supervisor")))
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            var announcementIds = announcements.Select(a => a.Id).ToList();
            var reads = await _context.AnnouncementReads
                .Where(r => r.UserId == user.Id && announcementIds.Contains(r.AnnouncementId))
                .ToListAsync();

            var popups = announcements
                .Where(a =>
                {
                    var read = reads.FirstOrDefault(r => r.AnnouncementId == a.Id);
                    return read == null || (read.PopupShownCount < 3 && read.DismissedAt == null);
                })
                .Select(a => new
                {
                    id = a.Id,
                    title = a.Title,
                    message = a.Message,
                    createdBy = a.CreatedBy,
                    createdAt = a.CreatedAt.ToString("MMM dd, yyyy")
                })
                .ToList();

            return Json(popups);
        }

        // POST: /Announcement/MarkPopupShown
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkPopupShown([FromBody] MarkPopupShownRequest request)
        {
            if (request?.AnnouncementIds == null || request.AnnouncementIds.Length == 0)
                return Json(new { success = false });

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Json(new { success = false });

            var existingReads = await _context.AnnouncementReads
                .Where(r => r.UserId == user.Id && request.AnnouncementIds.Contains(r.AnnouncementId))
                .ToListAsync();

            foreach (var announcementId in request.AnnouncementIds)
            {
                var existing = existingReads.FirstOrDefault(r => r.AnnouncementId == announcementId);
                if (existing != null)
                {
                    existing.PopupShownCount++;
                }
                else
                {
                    _context.AnnouncementReads.Add(new AnnouncementRead
                    {
                        AnnouncementId = announcementId,
                        UserId = user.Id,
                        PopupShownCount = 1,
                        FirstReadAt = DateTime.Now
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // GET: /Announcement/UnreadCount — returns JSON count for navbar badge
        [HttpGet]
        public async Task<IActionResult> UnreadCount()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Json(new { count = 0 });

            var userRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "User";
            if (userRole == "SuperAdmin")
                return Json(new { count = 0 });

            var now = DateTime.Now;

            var count = await _context.Announcements
                .Where(a => a.IsActive)
                .Where(a => a.ExpiresAt == null || a.ExpiresAt > now)
                .Where(a => a.TargetRole == "All"
                    || a.TargetRole == userRole
                    || (a.TargetRole == "AdminAndAbove" && (userRole == "Admin"))
                    || (a.TargetRole == "SupervisorAndAbove" && (userRole == "Admin" || userRole == "Supervisor")))
                .CountAsync(a => !_context.AnnouncementReads
                    .Any(r => r.AnnouncementId == a.Id && r.UserId == user.Id && r.DismissedAt != null));

            return Json(new { count });
        }
    }
}
