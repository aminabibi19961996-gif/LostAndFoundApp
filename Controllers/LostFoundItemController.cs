using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using LostAndFoundApp.Data;
using LostAndFoundApp.Models;
using LostAndFoundApp.Services;
using LostAndFoundApp.ViewModels;

namespace LostAndFoundApp.Controllers
{
    [Authorize]
    public class LostFoundItemController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly FileService _fileService;
        private readonly ActivityLogService _activityLogService;
        private readonly ILogger<LostFoundItemController> _logger;

        public LostFoundItemController(ApplicationDbContext context, FileService fileService, ActivityLogService activityLogService, ILogger<LostFoundItemController> logger)
        {
            _context = context;
            _fileService = fileService;
            _activityLogService = activityLogService;
            _logger = logger;
        }

        // GET: /LostFoundItem/Create
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var vm = new LostFoundItemCreateViewModel
            {
                DateFound = DateTime.Today,
                StatusDate = DateTime.Today
            };
            await PopulateDropdowns(vm);
            return View(vm);
        }

        // POST: /LostFoundItem/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestFormLimits(MultipartBodyLengthLimit = 10_485_760)] // 10MB — matches FileUpload:MaxFileSizeBytes
        [RequestSizeLimit(10_485_760)]
        public async Task<IActionResult> Create(LostFoundItemCreateViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                await PopulateDropdowns(vm);
                return View(vm);
            }

            var item = new LostFoundItem
            {
                DateFound = vm.DateFound,
                ItemId = vm.ItemId,
                Description = vm.Description,
                LocationFound = vm.LocationFound,
                RouteId = vm.RouteId,
                VehicleId = vm.VehicleId,
                StorageLocationId = vm.StorageLocationId,
                StatusId = vm.StatusId,
                StatusDate = vm.StatusDate,
                FoundById = vm.FoundById,
                ClaimedBy = vm.ClaimedBy,
                Notes = vm.Notes,
                CreatedBy = User.Identity?.Name ?? "Unknown",
                CreatedDateTime = DateTime.UtcNow
            };

            // Handle photo upload
            if (vm.PhotoFile != null && vm.PhotoFile.Length > 0)
            {
                var photoName = await _fileService.SavePhotoAsync(vm.PhotoFile);
                if (photoName == null)
                {
                    ModelState.AddModelError("PhotoFile", "Invalid photo file. Allowed types: jpg, jpeg, png, gif. Max size: 10MB.");
                    await PopulateDropdowns(vm);
                    return View(vm);
                }
                item.PhotoPath = photoName;
            }

            // Handle attachment upload
            if (vm.AttachmentFile != null && vm.AttachmentFile.Length > 0)
            {
                var attachmentName = await _fileService.SaveAttachmentAsync(vm.AttachmentFile);
                if (attachmentName == null)
                {
                    _fileService.DeletePhoto(item.PhotoPath);
                    ModelState.AddModelError("AttachmentFile", "Invalid attachment file. Allowed types: pdf, doc, docx, xls, xlsx, txt, jpg, jpeg, png. Max size: 10MB.");
                    await PopulateDropdowns(vm);
                    return View(vm);
                }
                item.AttachmentPath = attachmentName;
            }

            _context.LostFoundItems.Add(item);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save new LostFoundItem");
                // Clean up orphaned files that were saved to disk before the DB failure
                _fileService.DeletePhoto(item.PhotoPath);
                _fileService.DeleteAttachment(item.AttachmentPath);
                ModelState.AddModelError(string.Empty, "An error occurred while saving the record. Please try again.");
                await PopulateDropdowns(vm);
                return View(vm);
            }

            _logger.LogInformation("LostFoundItem {TrackingId} created by {User}", item.TrackingId, item.CreatedBy);
            await _activityLogService.LogAsync(HttpContext, "Create Record",
                $"Created lost & found record #{item.TrackingId} (Item: {vm.ItemId}, Location: {vm.LocationFound}).", "Items");
            TempData["SuccessMessage"] = $"Item record #{item.TrackingId} created successfully.";
            return RedirectToAction(nameof(Details), new { id = item.TrackingId });
        }

        // GET: /LostFoundItem/Details/5
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var item = await _context.LostFoundItems
                .Include(x => x.Item)
                .Include(x => x.Route)
                .Include(x => x.Vehicle)
                .Include(x => x.StorageLocation)
                .Include(x => x.Status)
                .Include(x => x.FoundBy)
                .FirstOrDefaultAsync(x => x.TrackingId == id);

            if (item == null)
                return NotFound();

            var vm = new LostFoundItemDetailViewModel
            {
                TrackingId = item.TrackingId,
                DateFound = item.DateFound,
                ItemName = item.Item?.Name,
                Description = item.Description,
                LocationFound = item.LocationFound,
                RouteName = item.Route?.Name,
                VehicleName = item.Vehicle?.Name,
                PhotoPath = item.PhotoPath,
                StorageLocationName = item.StorageLocation?.Name,
                StatusName = item.Status?.Name,
                StatusDate = item.StatusDate,
                DaysSinceFound = item.DaysSinceFound,
                FoundByName = item.FoundBy?.Name,
                ClaimedBy = item.ClaimedBy,
                CreatedBy = item.CreatedBy,
                CreatedDateTime = item.CreatedDateTime,
                ModifiedBy = item.ModifiedBy,
                ModifiedDateTime = item.ModifiedDateTime,
                Notes = item.Notes,
                AttachmentPath = item.AttachmentPath
            };

            return View(vm);
        }

        // GET: /LostFoundItem/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var item = await _context.LostFoundItems.FindAsync(id);
            if (item == null)
                return NotFound();

            // Ownership check: User role can only edit their own records
            if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin")
                && item.CreatedBy != User.Identity?.Name)
            {
                return Forbid();
            }

            var vm = new LostFoundItemEditViewModel
            {
                TrackingId = item.TrackingId,
                DateFound = item.DateFound,
                ItemId = item.ItemId,
                Description = item.Description,
                LocationFound = item.LocationFound,
                RouteId = item.RouteId,
                VehicleId = item.VehicleId,
                StorageLocationId = item.StorageLocationId,
                StatusId = item.StatusId,
                StatusDate = item.StatusDate,
                FoundById = item.FoundById,
                ClaimedBy = item.ClaimedBy,
                Notes = item.Notes,
                ExistingPhotoPath = item.PhotoPath,
                ExistingAttachmentPath = item.AttachmentPath,
                CreatedBy = item.CreatedBy,
                CreatedDateTime = item.CreatedDateTime
            };

            await PopulateDropdowns(vm);
            return View(vm);
        }

        // POST: /LostFoundItem/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestFormLimits(MultipartBodyLengthLimit = 10_485_760)] // 10MB — matches FileUpload:MaxFileSizeBytes
        [RequestSizeLimit(10_485_760)]
        public async Task<IActionResult> Edit(LostFoundItemEditViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                await PopulateDropdowns(vm);
                return View(vm);
            }

            var item = await _context.LostFoundItems.FindAsync(vm.TrackingId);
            if (item == null)
                return NotFound();

            // Ownership check: User role can only edit their own records
            if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin")
                && item.CreatedBy != User.Identity?.Name)
            {
                return Forbid();
            }

            // P0 FIX: Audit field tampering prevention - never trust client-sent values
            // Hidden inputs (CreatedBy, CreatedDateTime) can be manipulated; always preserve DB values
            var originalCreatedBy = item.CreatedBy;
            var originalCreatedDateTime = item.CreatedDateTime;

            item.DateFound = vm.DateFound;
            item.ItemId = vm.ItemId;
            item.Description = vm.Description;
            item.LocationFound = vm.LocationFound;
            item.RouteId = vm.RouteId;
            item.VehicleId = vm.VehicleId;
            item.StorageLocationId = vm.StorageLocationId;
            item.StatusId = vm.StatusId;
            item.StatusDate = vm.StatusDate;
            item.FoundById = vm.FoundById;
            item.ClaimedBy = vm.ClaimedBy;
            item.Notes = vm.Notes;
            // Audit trail — auto-populated, never user-editable
            item.CreatedBy = originalCreatedBy;
            item.CreatedDateTime = originalCreatedDateTime;
            item.ModifiedBy = User.Identity?.Name ?? "Unknown";
            item.ModifiedDateTime = DateTime.UtcNow;

            // Handle photo removal checkbox
            if (Request.Form["RemovePhoto"] == "true" && !string.IsNullOrEmpty(item.PhotoPath))
            {
                _fileService.DeletePhoto(item.PhotoPath);
                item.PhotoPath = null;
            }

            // Handle attachment removal checkbox
            if (Request.Form["RemoveAttachment"] == "true" && !string.IsNullOrEmpty(item.AttachmentPath))
            {
                _fileService.DeleteAttachment(item.AttachmentPath);
                item.AttachmentPath = null;
            }

            // Handle photo replacement
            if (vm.PhotoFile != null && vm.PhotoFile.Length > 0)
            {
                var photoName = await _fileService.SavePhotoAsync(vm.PhotoFile);
                if (photoName == null)
                {
                    ModelState.AddModelError("PhotoFile", "Invalid photo file. Allowed types: jpg, jpeg, png, gif. Max size: 10MB.");
                    vm.ExistingPhotoPath = item.PhotoPath;
                    vm.ExistingAttachmentPath = item.AttachmentPath;
                    await PopulateDropdowns(vm);
                    return View(vm);
                }
                // Delete old photo
                _fileService.DeletePhoto(item.PhotoPath);
                item.PhotoPath = photoName;
            }

            // Handle attachment replacement
            if (vm.AttachmentFile != null && vm.AttachmentFile.Length > 0)
            {
                var attachmentName = await _fileService.SaveAttachmentAsync(vm.AttachmentFile);
                if (attachmentName == null)
                {
                    ModelState.AddModelError("AttachmentFile", "Invalid attachment file.");
                    vm.ExistingPhotoPath = item.PhotoPath;
                    vm.ExistingAttachmentPath = item.AttachmentPath;
                    await PopulateDropdowns(vm);
                    return View(vm);
                }
                _fileService.DeleteAttachment(item.AttachmentPath);
                item.AttachmentPath = attachmentName;
            }

            _context.LostFoundItems.Update(item);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update LostFoundItem {TrackingId}", vm.TrackingId);
                ModelState.AddModelError(string.Empty, "An error occurred while saving changes. Please try again.");
                vm.ExistingPhotoPath = item.PhotoPath;
                vm.ExistingAttachmentPath = item.AttachmentPath;
                await PopulateDropdowns(vm);
                return View(vm);
            }

            _logger.LogInformation("LostFoundItem {TrackingId} updated by {User}", item.TrackingId, User.Identity?.Name);
            await _activityLogService.LogAsync(HttpContext, "Edit Record",
                $"Updated lost & found record #{item.TrackingId}.", "Items");
            TempData["SuccessMessage"] = $"Item record #{item.TrackingId} updated successfully.";
            return RedirectToAction(nameof(Details), new { id = item.TrackingId });
        }

        // GET: /LostFoundItem/Search
        [HttpGet]
        public async Task<IActionResult> Search(SearchViewModel? vm)
        {
            vm ??= new SearchViewModel();

            await PopulateSearchDropdowns(vm);

            // Build query
            var query = _context.LostFoundItems.AsQueryable();

            // Build filter summary for print
            var filters = new List<string>();

            // Apply filters — empty/null filters are ignored
            if (vm.TrackingId.HasValue)
            {
                query = query.Where(x => x.TrackingId == vm.TrackingId.Value);
                filters.Add($"Tracking ID: {vm.TrackingId.Value}");
            }
            if (vm.DateFoundFrom.HasValue)
            {
                query = query.Where(x => x.DateFound >= vm.DateFoundFrom.Value);
                filters.Add($"Date Found From: {vm.DateFoundFrom.Value:MM/dd/yyyy}");
            }
            if (vm.DateFoundTo.HasValue)
            {
                query = query.Where(x => x.DateFound <= vm.DateFoundTo.Value);
                filters.Add($"Date Found To: {vm.DateFoundTo.Value:MM/dd/yyyy}");
            }
            if (vm.ItemId.HasValue)
            {
                query = query.Where(x => x.ItemId == vm.ItemId.Value);
                var itemName = vm.Items?.FirstOrDefault(i => i.Value == vm.ItemId.Value.ToString())?.Text ?? vm.ItemId.Value.ToString();
                filters.Add($"Item: {itemName}");
            }
            if (vm.StatusId.HasValue)
            {
                query = query.Where(x => x.StatusId == vm.StatusId.Value);
                var statusName = vm.Statuses?.FirstOrDefault(i => i.Value == vm.StatusId.Value.ToString())?.Text ?? vm.StatusId.Value.ToString();
                filters.Add($"Status: {statusName}");
            }
            if (vm.RouteId.HasValue)
            {
                query = query.Where(x => x.RouteId == vm.RouteId.Value);
                var routeName = vm.Routes?.FirstOrDefault(i => i.Value == vm.RouteId.Value.ToString())?.Text ?? vm.RouteId.Value.ToString();
                filters.Add($"Route #: {routeName}");
            }
            if (vm.VehicleId.HasValue)
            {
                query = query.Where(x => x.VehicleId == vm.VehicleId.Value);
                var vehicleName = vm.Vehicles?.FirstOrDefault(i => i.Value == vm.VehicleId.Value.ToString())?.Text ?? vm.VehicleId.Value.ToString();
                filters.Add($"Vehicle #: {vehicleName}");
            }
            if (vm.StorageLocationId.HasValue)
            {
                query = query.Where(x => x.StorageLocationId == vm.StorageLocationId.Value);
                var locName = vm.StorageLocations?.FirstOrDefault(i => i.Value == vm.StorageLocationId.Value.ToString())?.Text ?? vm.StorageLocationId.Value.ToString();
                filters.Add($"Storage Location: {locName}");
            }
            if (vm.FoundById.HasValue)
            {
                query = query.Where(x => x.FoundById == vm.FoundById.Value);
                var foundName = vm.FoundByNames?.FirstOrDefault(i => i.Value == vm.FoundById.Value.ToString())?.Text ?? vm.FoundById.Value.ToString();
                filters.Add($"Found By: {foundName}");
            }

            vm.FilterSummary = filters.Any() ? string.Join(" | ", filters) : "All Records (No Filters Applied)";

            // Sort
            query = vm.SortField switch
            {
                "DateFound" => vm.SortOrder == "asc" ? query.OrderBy(x => x.DateFound) : query.OrderByDescending(x => x.DateFound),
                "ItemName" => vm.SortOrder == "asc" ? query.OrderBy(x => x.Item != null ? x.Item.Name : "") : query.OrderByDescending(x => x.Item != null ? x.Item.Name : ""),
                "StatusName" => vm.SortOrder == "asc" ? query.OrderBy(x => x.Status != null ? x.Status.Name : "") : query.OrderByDescending(x => x.Status != null ? x.Status.Name : ""),
                "LocationFound" => vm.SortOrder == "asc" ? query.OrderBy(x => x.LocationFound) : query.OrderByDescending(x => x.LocationFound),
                _ => vm.SortOrder == "asc" ? query.OrderBy(x => x.TrackingId) : query.OrderByDescending(x => x.TrackingId),
            };

            // Pagination
            vm.TotalRecords = await query.CountAsync();
            vm.TotalPages = (int)Math.Ceiling((double)vm.TotalRecords / vm.PageSize);
            if (vm.CurrentPage < 1) vm.CurrentPage = 1;
            if (vm.CurrentPage > vm.TotalPages && vm.TotalPages > 0) vm.CurrentPage = vm.TotalPages;

            var rawResults = await query
                .Skip((vm.CurrentPage - 1) * vm.PageSize)
                .Take(vm.PageSize)
                .Select(x => new
                {
                    x.TrackingId,
                    x.DateFound,
                    ItemName = x.Item != null ? x.Item.Name : "",
                    x.Description,
                    x.LocationFound,
                    RouteName = x.Route != null ? x.Route.Name : "",
                    VehicleName = x.Vehicle != null ? x.Vehicle.Name : "",
                    StorageLocationName = x.StorageLocation != null ? x.StorageLocation.Name : "",
                    StatusName = x.Status != null ? x.Status.Name : "",
                    FoundByName = x.FoundBy != null ? x.FoundBy.Name : "",
                    x.ClaimedBy
                })
                .ToListAsync();

            vm.Results = rawResults.Select(x => new SearchResultItem
            {
                TrackingId = x.TrackingId,
                DateFound = x.DateFound,
                ItemName = x.ItemName,
                Description = x.Description,
                LocationFound = x.LocationFound,
                RouteName = x.RouteName,
                VehicleName = x.VehicleName,
                StorageLocationName = x.StorageLocationName,
                StatusName = x.StatusName,
                DaysSinceFound = (DateTime.Today - x.DateFound.Date).Days,
                FoundByName = x.FoundByName,
                ClaimedBy = x.ClaimedBy
            }).ToList();

            return View(vm);
        }

        // GET: /LostFoundItem/PrintSearch — full results for print (no pagination)
        [HttpGet]
        public async Task<IActionResult> PrintSearch(SearchViewModel? vm)
        {
            vm ??= new SearchViewModel();

            var query = _context.LostFoundItems.AsQueryable();

            // Use in-memory dropdown lists to populate filter strings to avoid N+1 DB roundtrips.
            await PopulateSearchDropdowns(vm);

            var filters = new List<string>();

            if (vm.TrackingId.HasValue)
            {
                query = query.Where(x => x.TrackingId == vm.TrackingId.Value);
                filters.Add($"Tracking ID: {vm.TrackingId.Value}");
            }
            if (vm.DateFoundFrom.HasValue)
            {
                query = query.Where(x => x.DateFound >= vm.DateFoundFrom.Value);
                filters.Add($"Date Found From: {vm.DateFoundFrom.Value:MM/dd/yyyy}");
            }
            if (vm.DateFoundTo.HasValue)
            {
                query = query.Where(x => x.DateFound <= vm.DateFoundTo.Value);
                filters.Add($"Date Found To: {vm.DateFoundTo.Value:MM/dd/yyyy}");
            }
            if (vm.ItemId.HasValue)
            {
                query = query.Where(x => x.ItemId == vm.ItemId.Value);
                var itemName = vm.Items?.FirstOrDefault(i => i.Value == vm.ItemId.Value.ToString())?.Text ?? vm.ItemId.Value.ToString();
                filters.Add($"Item: {itemName}");
            }
            if (vm.StatusId.HasValue)
            {
                query = query.Where(x => x.StatusId == vm.StatusId.Value);
                var statusName = vm.Statuses?.FirstOrDefault(i => i.Value == vm.StatusId.Value.ToString())?.Text ?? vm.StatusId.Value.ToString();
                filters.Add($"Status: {statusName}");
            }
            if (vm.RouteId.HasValue)
            {
                query = query.Where(x => x.RouteId == vm.RouteId.Value);
                var routeName = vm.Routes?.FirstOrDefault(i => i.Value == vm.RouteId.Value.ToString())?.Text ?? vm.RouteId.Value.ToString();
                filters.Add($"Route #: {routeName}");
            }
            if (vm.VehicleId.HasValue)
            {
                query = query.Where(x => x.VehicleId == vm.VehicleId.Value);
                var vehicleName = vm.Vehicles?.FirstOrDefault(i => i.Value == vm.VehicleId.Value.ToString())?.Text ?? vm.VehicleId.Value.ToString();
                filters.Add($"Vehicle #: {vehicleName}");
            }
            if (vm.StorageLocationId.HasValue)
            {
                query = query.Where(x => x.StorageLocationId == vm.StorageLocationId.Value);
                var locName = vm.StorageLocations?.FirstOrDefault(i => i.Value == vm.StorageLocationId.Value.ToString())?.Text ?? vm.StorageLocationId.Value.ToString();
                filters.Add($"Storage Location: {locName}");
            }
            if (vm.FoundById.HasValue)
            {
                query = query.Where(x => x.FoundById == vm.FoundById.Value);
                var foundName = vm.FoundByNames?.FirstOrDefault(i => i.Value == vm.FoundById.Value.ToString())?.Text ?? vm.FoundById.Value.ToString();
                filters.Add($"Found By: {foundName}");
            }

            vm.FilterSummary = filters.Any() ? string.Join(" | ", filters) : "All Records (No Filters Applied)";

            // Calculate dates using a standard mapped value (EF Core will compute 'Days' safely after pull or map it)
            // SQL Server / SQLite can struggle mapping DateTime.Today natively over EF depending on translation, 
            // but we fetch what we need and materialize cleanly.
            var rawResults = await query
                .OrderByDescending(x => x.TrackingId)
                .Select(x => new
                {
                    x.TrackingId,
                    x.DateFound,
                    ItemName = x.Item != null ? x.Item.Name : "",
                    x.Description,
                    x.LocationFound,
                    RouteName = x.Route != null ? x.Route.Name : "",
                    VehicleName = x.Vehicle != null ? x.Vehicle.Name : "",
                    StorageLocationName = x.StorageLocation != null ? x.StorageLocation.Name : "",
                    StatusName = x.Status != null ? x.Status.Name : "",
                    FoundByName = x.FoundBy != null ? x.FoundBy.Name : "",
                    x.ClaimedBy
                })
                .ToListAsync();

            var results = rawResults.Select(x => new SearchResultItem
            {
                TrackingId = x.TrackingId,
                DateFound = x.DateFound,
                ItemName = x.ItemName,
                Description = x.Description,
                LocationFound = x.LocationFound,
                RouteName = x.RouteName,
                VehicleName = x.VehicleName,
                StorageLocationName = x.StorageLocationName,
                StatusName = x.StatusName,
                DaysSinceFound = (DateTime.Today - x.DateFound.Date).Days,
                FoundByName = x.FoundByName,
                ClaimedBy = x.ClaimedBy
            }).ToList();

            vm.Results = results;
            vm.TotalRecords = results.Count;
            return View(vm);
        }

        // GET: /LostFoundItem/Photo/{fileName}  — authenticated file streaming
        [HttpGet]
        public IActionResult Photo(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            try
            {
                var result = _fileService.GetPhoto(id);
                if (result == null || result.Value.Stream == null)
                {
                    // Return a 1x1 transparent PNG so <img> tags don't show broken icons
                    // This prevents UseStatusCodePagesWithReExecute from turning 404 into HTML
                    var placeholder = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
                    return File(placeholder, "image/png");
                }

                return File(result.Value.Stream, result.Value.ContentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serving photo '{FileName}'", id);
                return StatusCode(500);
            }
        }

        // GET: /LostFoundItem/Attachment/{fileName} — authenticated file streaming
        [HttpGet]
        public IActionResult Attachment(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            try
            {
                var result = _fileService.GetAttachment(id);
                if (result == null || result.Value.Stream == null)
                {
                    TempData["ErrorMessage"] = "The requested attachment was not found. It may have been deleted.";
                    return RedirectToAction(nameof(Search));
                }

                var fileName = Path.GetFileName(id);
                return File(result.Value.Stream, result.Value.ContentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serving attachment '{FileName}'", id);
                TempData["ErrorMessage"] = "Could not retrieve the attachment. Please try again.";
                return RedirectToAction(nameof(Search));
            }
        }

        // GET: /LostFoundItem/Export — Export search results to CSV
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Export(SearchViewModel? vm)
        {
            vm ??= new SearchViewModel();

            // Build the same query as Search but without pagination
            var query = _context.LostFoundItems.AsQueryable();

            if (vm.TrackingId.HasValue)
                query = query.Where(x => x.TrackingId == vm.TrackingId.Value);
            if (vm.DateFoundFrom.HasValue)
                query = query.Where(x => x.DateFound >= vm.DateFoundFrom.Value);
            if (vm.DateFoundTo.HasValue)
                query = query.Where(x => x.DateFound <= vm.DateFoundTo.Value);
            if (vm.ItemId.HasValue)
                query = query.Where(x => x.ItemId == vm.ItemId.Value);
            if (vm.StatusId.HasValue)
                query = query.Where(x => x.StatusId == vm.StatusId.Value);
            if (vm.RouteId.HasValue)
                query = query.Where(x => x.RouteId == vm.RouteId.Value);
            if (vm.VehicleId.HasValue)
                query = query.Where(x => x.VehicleId == vm.VehicleId.Value);
            if (vm.StorageLocationId.HasValue)
                query = query.Where(x => x.StorageLocationId == vm.StorageLocationId.Value);
            if (vm.FoundById.HasValue)
                query = query.Where(x => x.FoundById == vm.FoundById.Value);

            var results = await query
                .OrderByDescending(x => x.TrackingId)
                .Select(x => new
                {
                    x.TrackingId,
                    x.DateFound,
                    ItemName = x.Item != null ? x.Item.Name : "",
                    x.Description,
                    x.LocationFound,
                    RouteName = x.Route != null ? x.Route.Name : "",
                    VehicleName = x.Vehicle != null ? x.Vehicle.Name : "",
                    StorageLocationName = x.StorageLocation != null ? x.StorageLocation.Name : "",
                    StatusName = x.Status != null ? x.Status.Name : "",
                    x.StatusDate,
                    FoundByName = x.FoundBy != null ? x.FoundBy.Name : "",
                    x.ClaimedBy,
                    x.CreatedBy,
                    x.CreatedDateTime,
                    x.ModifiedBy,
                    x.ModifiedDateTime,
                    x.Notes
                })
                .ToListAsync();

            // Generate CSV
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("TrackingId,DateFound,ItemType,Description,LocationFound,Route,Vehicle,StorageLocation,Status,StatusDate,FoundBy,ClaimedBy,CreatedBy,CreatedDateTime,ModifiedBy,ModifiedDateTime,Notes");

            foreach (var r in results)
            {
                sb.AppendLine($"{r.TrackingId},{r.DateFound:yyyy-MM-dd},\"{EscapeCsv(r.ItemName)}\",\"{EscapeCsv(r.Description)}\",\"{EscapeCsv(r.LocationFound)}\",\"{EscapeCsv(r.RouteName)}\",\"{EscapeCsv(r.VehicleName)}\",\"{EscapeCsv(r.StorageLocationName)}\",\"{r.StatusName}\",{r.StatusDate?.ToString("yyyy-MM-dd")},\"{EscapeCsv(r.FoundByName)}\",\"{EscapeCsv(r.ClaimedBy)}\",\"{r.CreatedBy}\",{r.CreatedDateTime:yyyy-MM-dd HH:mm:ss},\"{r.ModifiedBy}\",{r.ModifiedDateTime?.ToString("yyyy-MM-dd HH:mm:ss")},\"{EscapeCsv(r.Notes)}\"");
            }

            await _activityLogService.LogAsync(HttpContext, "Export Records",
                $"Exported {results.Count} records to CSV.", "Items");

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"lost-found-export-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sanitized = value.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
            // Prevent CSV formula injection — prefix with single quote if value starts with a formula character
            if (sanitized.Length > 0 && "=+-@\t".Contains(sanitized[0]))
            {
                sanitized = "'" + sanitized;
            }
            return sanitized;
        }

        // POST: /LostFoundItem/Delete/5 — Admin+ only
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _context.LostFoundItems.FindAsync(id);
            if (item == null)
                return NotFound();

            // Capture file paths before removing entity so we can clean up after successful DB delete
            var photoPath = item.PhotoPath;
            var attachmentPath = item.AttachmentPath;

            _context.LostFoundItems.Remove(item);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete LostFoundItem {TrackingId}", id);
                TempData["ErrorMessage"] = "An error occurred while deleting the record. Please try again.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Clean up associated files AFTER successful DB commit to prevent orphaned records
            _fileService.DeletePhoto(photoPath);
            _fileService.DeleteAttachment(attachmentPath);

            _logger.LogInformation("LostFoundItem {TrackingId} deleted by {User}", id, User.Identity?.Name);
            await _activityLogService.LogAsync(HttpContext, "Delete Record",
                $"Deleted lost & found record #{id}.", "Items");
            TempData["SuccessMessage"] = $"Record #{id} has been deleted.";
            return RedirectToAction(nameof(Search));
        }

        // POST: /LostFoundItem/BulkDelete — Admin+ only
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> BulkDelete([FromForm] string ids)
        {
            // Parse comma-separated IDs from the hidden input (JS joins selected values)
            var idArray = ParseIds(ids);
            if (idArray.Length == 0)
            {
                TempData["ErrorMessage"] = "No records selected for deletion.";
                return RedirectToAction(nameof(Search));
            }

            var items = await _context.LostFoundItems
                .Where(x => idArray.Contains(x.TrackingId))
                .ToListAsync();

            if (!items.Any())
            {
                TempData["ErrorMessage"] = "No matching records found.";
                return RedirectToAction(nameof(Search));
            }

            var deletedCount = items.Count;

            // Capture file paths BEFORE removing entities
            var filesToDelete = items
                .Select(i => (Photo: i.PhotoPath, Attachment: i.AttachmentPath))
                .ToList();

            _context.LostFoundItems.RemoveRange(items);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to bulk delete {Count} records", deletedCount);
                TempData["ErrorMessage"] = "An error occurred while deleting records. Please try again.";
                return RedirectToAction(nameof(Search));
            }

            // Clean up files AFTER successful DB commit to prevent orphaned records
            foreach (var (photo, attachment) in filesToDelete)
            {
                _fileService.DeletePhoto(photo);
                _fileService.DeleteAttachment(attachment);
            }

            _logger.LogInformation("Bulk deleted {Count} records by {User}", deletedCount, User.Identity?.Name);
            await _activityLogService.LogAsync(HttpContext, "Bulk Delete",
                $"Bulk deleted {deletedCount} lost & found records.", "Items");
            TempData["SuccessMessage"] = $"{deletedCount} record(s) deleted successfully.";
            return RedirectToAction(nameof(Search));
        }

        // POST: /LostFoundItem/BulkStatusUpdate — Admin+ only
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> BulkStatusUpdate([FromForm] string ids, [FromForm] int statusId)
        {
            // Parse comma-separated IDs from the hidden input (JS joins selected values)
            var idArray = ParseIds(ids);
            if (idArray.Length == 0)
            {
                TempData["ErrorMessage"] = "No records selected for status update.";
                return RedirectToAction(nameof(Search));
            }

            var items = await _context.LostFoundItems
                .Where(x => idArray.Contains(x.TrackingId))
                .ToListAsync();

            if (!items.Any())
            {
                TempData["ErrorMessage"] = "No matching records found.";
                return RedirectToAction(nameof(Search));
            }

            var status = await _context.Statuses.FindAsync(statusId);
            var updatedCount = items.Count;

            foreach (var item in items)
            {
                item.StatusId = statusId;
                item.StatusDate = DateTime.Today; // Date-only field — use date-only value
                item.ModifiedBy = User.Identity?.Name ?? "Unknown";
                item.ModifiedDateTime = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Bulk updated status to '{Status}' for {Count} records by {User}", 
                status?.Name, updatedCount, User.Identity?.Name);
            await _activityLogService.LogAsync(HttpContext, "Bulk Status Update",
                $"Bulk updated status to '{status?.Name}' for {updatedCount} records.", "Items");
            TempData["SuccessMessage"] = $"{updatedCount} record(s) updated to '{status?.Name}'.";
            return RedirectToAction(nameof(Search));
        }

        // =====================================================================
        // HELPER METHODS
        // =====================================================================

        /// <summary>
        /// Parses a comma-separated string of IDs into an int array.
        /// Used by BulkDelete and BulkStatusUpdate to handle JS-generated hidden input values.
        /// </summary>
        private static int[] ParseIds(string? ids)
        {
            if (string.IsNullOrWhiteSpace(ids)) return Array.Empty<int>();
            return ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToArray();
        }

        /// <summary>
        /// Populate dropdowns for Create — only active master data items.
        /// </summary>
        private async Task PopulateDropdowns(LostFoundItemCreateViewModel vm)
        {
            vm.Items = new SelectList(await _context.Items.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(), "Id", "Name", vm.ItemId);
            vm.Routes = new SelectList(await _context.Routes.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(), "Id", "Name", vm.RouteId);
            vm.Vehicles = new SelectList(await _context.Vehicles.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(), "Id", "Name", vm.VehicleId);
            vm.StorageLocations = new SelectList(await _context.StorageLocations.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(), "Id", "Name", vm.StorageLocationId);
            vm.Statuses = new SelectList(await _context.Statuses.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(), "Id", "Name", vm.StatusId);
            vm.FoundByNames = new SelectList(await _context.FoundByNames.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(), "Id", "Name", vm.FoundById);
        }

        /// <summary>
        /// Populate dropdowns for Edit — active items PLUS the currently-selected value
        /// even if it has been deactivated, so the dropdown retains the existing selection.
        /// </summary>
        private async Task PopulateDropdowns(LostFoundItemEditViewModel vm)
        {
            vm.Items = new SelectList(await _context.Items.Where(x => x.IsActive || x.Id == vm.ItemId).OrderBy(x => x.Name).ToListAsync(), "Id", "Name", vm.ItemId);
            vm.Routes = new SelectList(await _context.Routes.Where(x => x.IsActive || x.Id == vm.RouteId).OrderBy(x => x.Name).ToListAsync(), "Id", "Name", vm.RouteId);
            vm.Vehicles = new SelectList(await _context.Vehicles.Where(x => x.IsActive || x.Id == vm.VehicleId).OrderBy(x => x.Name).ToListAsync(), "Id", "Name", vm.VehicleId);
            vm.StorageLocations = new SelectList(await _context.StorageLocations.Where(x => x.IsActive || x.Id == vm.StorageLocationId).OrderBy(x => x.Name).ToListAsync(), "Id", "Name", vm.StorageLocationId);
            vm.Statuses = new SelectList(await _context.Statuses.Where(x => x.IsActive || x.Id == vm.StatusId).OrderBy(x => x.Name).ToListAsync(), "Id", "Name", vm.StatusId);
            vm.FoundByNames = new SelectList(await _context.FoundByNames.Where(x => x.IsActive || x.Id == vm.FoundById).OrderBy(x => x.Name).ToListAsync(), "Id", "Name", vm.FoundById);
        }

        private async Task PopulateSearchDropdowns(SearchViewModel vm)
        {
            vm.Items = new SelectList(await _context.Items.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
            vm.Statuses = new SelectList(await _context.Statuses.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
            vm.Routes = new SelectList(await _context.Routes.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
            vm.Vehicles = new SelectList(await _context.Vehicles.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
            vm.StorageLocations = new SelectList(await _context.StorageLocations.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
            vm.FoundByNames = new SelectList(await _context.FoundByNames.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
        }
    }
}
