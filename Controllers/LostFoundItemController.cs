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
        private readonly ItemDiffService _diffService;
        private readonly ILogger<LostFoundItemController> _logger;

        public LostFoundItemController(ApplicationDbContext context, FileService fileService, ActivityLogService activityLogService, ItemDiffService diffService, ILogger<LostFoundItemController> logger)
        {
            _context = context;
            _fileService = fileService;
            _activityLogService = activityLogService;
            _diffService = diffService;
            _logger = logger;
        }

        // GET: /LostFoundItem/Create
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // Default status to "Found" since that's the most common starting status
            var foundStatus = await _context.Statuses
                .Where(s => s.Name == "Found" && s.IsActive)
                .Select(s => s.Id)
                .FirstOrDefaultAsync();

            var vm = new LostFoundItemCreateViewModel
            {
                DateFound = DateTime.Today,
                StatusDate = DateTime.Today,
                StatusId = foundStatus,
                PreviewTrackingId = await GenerateCustomTrackingIdAsync(),
                CreatedByPreview = User.Identity?.Name ?? "Unknown"
            };
            await PopulateDropdowns(vm);
            return View(vm);
        }

        // POST: /LostFoundItem/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestFormLimits(MultipartBodyLengthLimit = 52_428_800)] // 50MB — supports 4 photos + attachment
        [RequestSizeLimit(52_428_800)]
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
                CreatedDateTime = DateTime.UtcNow,
                CustomTrackingId = await GenerateCustomTrackingIdAsync()
            };

            // Handle photo uploads (up to 4)
            var photoFiles = new[] { vm.PhotoFile, vm.PhotoFile2, vm.PhotoFile3, vm.PhotoFile4 };
            var photoFieldNames = new[] { "PhotoFile", "PhotoFile2", "PhotoFile3", "PhotoFile4" };
            var savedPhotoPaths = new string?[4];

            for (int i = 0; i < photoFiles.Length; i++)
            {
                if (photoFiles[i] != null && photoFiles[i]!.Length > 0)
                {
                    var photoName = await _fileService.SavePhotoAsync(photoFiles[i]!);
                    if (photoName == null)
                    {
                        // Clean up any previously saved photos in this batch
                        for (int j = 0; j < i; j++)
                            _fileService.DeletePhoto(savedPhotoPaths[j]);
                        ModelState.AddModelError(photoFieldNames[i], "Invalid photo file. Allowed types: jpg, jpeg, png, gif. Max size: 10MB.");
                        await PopulateDropdowns(vm);
                        return View(vm);
                    }
                    savedPhotoPaths[i] = photoName;
                }
            }
            item.PhotoPath = savedPhotoPaths[0];
            item.PhotoPath2 = savedPhotoPaths[1];
            item.PhotoPath3 = savedPhotoPaths[2];
            item.PhotoPath4 = savedPhotoPaths[3];

            // Handle attachment upload
            if (vm.AttachmentFile != null && vm.AttachmentFile.Length > 0)
            {
                var attachmentName = await _fileService.SaveAttachmentAsync(vm.AttachmentFile);
                if (attachmentName == null)
                {
                    foreach (var p in savedPhotoPaths) _fileService.DeletePhoto(p);
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
                _fileService.DeletePhoto(item.PhotoPath2);
                _fileService.DeletePhoto(item.PhotoPath3);
                _fileService.DeletePhoto(item.PhotoPath4);
                _fileService.DeleteAttachment(item.AttachmentPath);
                ModelState.AddModelError(string.Empty, "An error occurred while saving the record. Please try again.");
                await PopulateDropdowns(vm);
                return View(vm);
            }

            _logger.LogInformation("LostFoundItem {TrackingId} created by {User}", item.TrackingId, item.CreatedBy);

            // Log with key initial field values so the audit trail shows what was submitted
            var itemNameForLog = (await _context.Items.FindAsync(item.ItemId))?.Name ?? item.ItemId.ToString();
            var statusNameForLog = (await _context.Statuses.FindAsync(item.StatusId))?.Name ?? item.StatusId.ToString();
            var createDetails = $"Created lost & found record #{item.TrackingId}\n" +
                                $"CHANGES:\n" +
                                $"- Item: \"{itemNameForLog}\"\n" +
                                $"- Date Found: \"{item.DateFound:MM/dd/yyyy}\"\n" +
                                $"- Location Found: \"{item.LocationFound}\"\n" +
                                $"- Status: \"{statusNameForLog}\"";
            await _activityLogService.LogAsync(HttpContext, "Create Record", createDetails, "Items");
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

            // All authenticated users can view any record — no ownership restriction

            var vm = new LostFoundItemDetailViewModel
            {
                TrackingId = item.TrackingId,
                CustomTrackingId = item.CustomTrackingId,
                DateFound = item.DateFound,
                ItemName = item.Item?.Name,
                Description = item.Description,
                LocationFound = item.LocationFound,
                RouteName = item.Route?.Name,
                VehicleName = item.Vehicle?.Name,
                PhotoPath = item.PhotoPath,
                PhotoPath2 = item.PhotoPath2,
                PhotoPath3 = item.PhotoPath3,
                PhotoPath4 = item.PhotoPath4,
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

            // All authenticated users can edit any record — no ownership restriction

            var vm = new LostFoundItemEditViewModel
            {
                TrackingId = item.TrackingId,
                CustomTrackingId = item.CustomTrackingId,
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
                ExistingPhotoPath2 = item.PhotoPath2,
                ExistingPhotoPath3 = item.PhotoPath3,
                ExistingPhotoPath4 = item.PhotoPath4,
                ExistingAttachmentPath = item.AttachmentPath,
                CreatedBy = item.CreatedBy,
                CreatedDateTime = item.CreatedDateTime,
                ModifiedBy = item.ModifiedBy,
                ModifiedDateTime = item.ModifiedDateTime,
                DaysSinceFound = item.DaysSinceFound
            };

            await PopulateDropdowns(vm);
            return View(vm);
        }

        // POST: /LostFoundItem/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestFormLimits(MultipartBodyLengthLimit = 52_428_800)] // 50MB — supports 4 photos + attachment
        [RequestSizeLimit(52_428_800)]
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

            // All authenticated users can edit any record — no ownership restriction

            // Snapshot before-state for diff logging BEFORE any fields are mutated
            var beforeSnapshot = new LostFoundItem
            {
                TrackingId   = item.TrackingId,
                DateFound    = item.DateFound,
                ItemId       = item.ItemId,
                Description  = item.Description,
                LocationFound= item.LocationFound,
                RouteId      = item.RouteId,
                VehicleId    = item.VehicleId,
                StorageLocationId = item.StorageLocationId,
                StatusId     = item.StatusId,
                StatusDate   = item.StatusDate,
                FoundById    = item.FoundById,
                ClaimedBy    = item.ClaimedBy,
                Notes        = item.Notes,
                PhotoPath    = item.PhotoPath,
                PhotoPath2   = item.PhotoPath2,
                PhotoPath3   = item.PhotoPath3,
                PhotoPath4   = item.PhotoPath4,
                AttachmentPath = item.AttachmentPath
            };

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

            // Handle photo removals (checkboxes)
            var removePhotoKeys = new[] { "RemovePhoto", "RemovePhoto2", "RemovePhoto3", "RemovePhoto4" };
            var itemPhotoPaths = new[] { item.PhotoPath, item.PhotoPath2, item.PhotoPath3, item.PhotoPath4 };
            for (int i = 0; i < removePhotoKeys.Length; i++)
            {
                if (Request.Form[removePhotoKeys[i]] == "true" && !string.IsNullOrEmpty(itemPhotoPaths[i]))
                {
                    _fileService.DeletePhoto(itemPhotoPaths[i]);
                    switch (i)
                    {
                        case 0: item.PhotoPath = null; break;
                        case 1: item.PhotoPath2 = null; break;
                        case 2: item.PhotoPath3 = null; break;
                        case 3: item.PhotoPath4 = null; break;
                    }
                }
            }

            // Handle attachment removal checkbox
            if (Request.Form["RemoveAttachment"] == "true" && !string.IsNullOrEmpty(item.AttachmentPath))
            {
                _fileService.DeleteAttachment(item.AttachmentPath);
                item.AttachmentPath = null;
            }

            // Handle photo replacements (up to 4)
            var editPhotoFiles = new IFormFile?[] { vm.PhotoFile, vm.PhotoFile2, vm.PhotoFile3, vm.PhotoFile4 };
            var editPhotoFieldNames = new[] { "PhotoFile", "PhotoFile2", "PhotoFile3", "PhotoFile4" };
            var currentPaths = new[] { item.PhotoPath, item.PhotoPath2, item.PhotoPath3, item.PhotoPath4 };

            for (int i = 0; i < editPhotoFiles.Length; i++)
            {
                if (editPhotoFiles[i] != null && editPhotoFiles[i]!.Length > 0)
                {
                    var photoName = await _fileService.SavePhotoAsync(editPhotoFiles[i]!);
                    if (photoName == null)
                    {
                        ModelState.AddModelError(editPhotoFieldNames[i], "Invalid photo file. Allowed types: jpg, jpeg, png, gif. Max size: 10MB.");
                        vm.ExistingPhotoPath = item.PhotoPath;
                        vm.ExistingPhotoPath2 = item.PhotoPath2;
                        vm.ExistingPhotoPath3 = item.PhotoPath3;
                        vm.ExistingPhotoPath4 = item.PhotoPath4;
                        vm.ExistingAttachmentPath = item.AttachmentPath;
                        await PopulateDropdowns(vm);
                        return View(vm);
                    }
                    // Delete old photo
                    _fileService.DeletePhoto(currentPaths[i]);
                    switch (i)
                    {
                        case 0: item.PhotoPath = photoName; break;
                        case 1: item.PhotoPath2 = photoName; break;
                        case 2: item.PhotoPath3 = photoName; break;
                        case 3: item.PhotoPath4 = photoName; break;
                    }
                }
            }

            // Handle attachment replacement
            if (vm.AttachmentFile != null && vm.AttachmentFile.Length > 0)
            {
                var attachmentName = await _fileService.SaveAttachmentAsync(vm.AttachmentFile);
                if (attachmentName == null)
                {
                    ModelState.AddModelError("AttachmentFile", "Invalid attachment file.");
                    vm.ExistingPhotoPath = item.PhotoPath;
                    vm.ExistingPhotoPath2 = item.PhotoPath2;
                    vm.ExistingPhotoPath3 = item.PhotoPath3;
                    vm.ExistingPhotoPath4 = item.PhotoPath4;
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
                vm.ExistingPhotoPath2 = item.PhotoPath2;
                vm.ExistingPhotoPath3 = item.PhotoPath3;
                vm.ExistingPhotoPath4 = item.PhotoPath4;
                vm.ExistingAttachmentPath = item.AttachmentPath;
                await PopulateDropdowns(vm);
                return View(vm);
            }

            _logger.LogInformation("LostFoundItem {TrackingId} updated by {User}", item.TrackingId, User.Identity?.Name);

            // Build a field-level diff and store it in the activity log
            var editDiff = await _diffService.BuildEditDiffAsync(
                beforeSnapshot, item,
                $"Updated lost & found record #{item.TrackingId}.");
            await _activityLogService.LogAsync(HttpContext, "Edit Record", editDiff, "Items");
            TempData["SuccessMessage"] = $"Item record #{item.TrackingId} updated successfully.";
            return RedirectToAction(nameof(Details), new { id = item.TrackingId });
        }

        // GET: /LostFoundItem/Search
        [HttpGet]
        public async Task<IActionResult> Search(SearchViewModel? vm)
        {
            vm ??= new SearchViewModel();

            await PopulateSearchDropdowns(vm);

            // All authenticated users can see all records — no role-based filtering
            var query = _context.LostFoundItems.AsQueryable();

            // Build filter summary for print
            var filters = new List<string>();

            // Apply filters — empty/null filters are ignored
            if (vm.TrackingId.HasValue)
            {
                query = query.Where(x => x.TrackingId == vm.TrackingId.Value);
                filters.Add($"Tracking ID: {vm.TrackingId.Value}");
            }
            if (!string.IsNullOrEmpty(vm.CustomTrackingId))
            {
                query = query.Where(x => x.CustomTrackingId != null && x.CustomTrackingId.Contains(vm.CustomTrackingId));
                filters.Add($"Custom Tracking ID: {vm.CustomTrackingId}");
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
                    x.CustomTrackingId,
                    x.DateFound,
                    ItemName = x.Item != null ? x.Item.Name : "",
                    x.Description,
                    x.LocationFound,
                    RouteName = x.Route != null ? x.Route.Name : "",
                    VehicleName = x.Vehicle != null ? x.Vehicle.Name : "",
                    StorageLocationName = x.StorageLocation != null ? x.StorageLocation.Name : "",
                    StatusName = x.Status != null ? x.Status.Name : "",
                    FoundByName = x.FoundBy != null ? x.FoundBy.Name : "",
                    x.ClaimedBy,
                    x.CreatedBy
                })
                .ToListAsync();

            vm.Results = rawResults.Select(x => new SearchResultItem
            {
                TrackingId = x.TrackingId,
                CustomTrackingId = x.CustomTrackingId,
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
                ClaimedBy = x.ClaimedBy,
                CreatedBy = x.CreatedBy
            }).ToList();

            return View(vm);
        }

        // GET: /LostFoundItem/PrintSearch — full results for print (no pagination)
        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
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
                    x.CustomTrackingId,
                    x.DateFound,
                    ItemName = x.Item != null ? x.Item.Name : "",
                    x.Description,
                    x.LocationFound,
                    RouteName = x.Route != null ? x.Route.Name : "",
                    VehicleName = x.Vehicle != null ? x.Vehicle.Name : "",
                    StorageLocationName = x.StorageLocation != null ? x.StorageLocation.Name : "",
                    StatusName = x.Status != null ? x.Status.Name : "",
                    FoundByName = x.FoundBy != null ? x.FoundBy.Name : "",
                    x.ClaimedBy,
                    x.CreatedBy
                })
                .ToListAsync();

            var results = rawResults.Select(x => new SearchResultItem
            {
                TrackingId = x.TrackingId,
                CustomTrackingId = x.CustomTrackingId,
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
                ClaimedBy = x.ClaimedBy,
                CreatedBy = x.CreatedBy
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
                if (result == null || result.Value.Bytes == null)
                {
                    // Return a 1x1 transparent PNG so <img> tags don't show broken icons
                    // This prevents UseStatusCodePagesWithReExecute from turning 404 into HTML
                    var placeholder = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
                    return File(placeholder, "image/png");
                }

                return File(result.Value.Bytes, result.Value.ContentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serving photo '{FileName}'", id);
                return StatusCode(500);
            }
        }

        // GET: /LostFoundItem/PrintLabel/5
        [HttpGet]
        public async Task<IActionResult> PrintLabel(int id)
        {
            var item = await _context.LostFoundItems
                .Include(x => x.Item)
                .FirstOrDefaultAsync(x => x.TrackingId == id);

            if (item == null) return NotFound();

            // Encode: TrackingID, Date Found, Item Name
            // Plus the direct URL to the item details
            var callbackUrl = Url.Action("Details", "LostFoundItem", new { id = item.TrackingId }, Request.Scheme);
            var qrText = $"ID: {item.CustomTrackingId}\nItem: {item.Item?.Name}\nDate: {item.DateFound:MM/dd/yyyy}\nURL: {callbackUrl}";

            using (var qrGenerator = new QRCoder.QRCodeGenerator())
            using (var qrCodeData = qrGenerator.CreateQrCode(qrText, QRCoder.QRCodeGenerator.ECCLevel.Q))
            using (var qrCode = new QRCoder.PngByteQRCode(qrCodeData))
            {
                byte[] qrCodeImage = qrCode.GetGraphic(20);
                ViewBag.QrCodeBase64 = Convert.ToBase64String(qrCodeImage);
            }

            var vm = new LostFoundItemDetailViewModel
            {
                TrackingId = item.TrackingId,
                CustomTrackingId = item.CustomTrackingId,
                DateFound = item.DateFound,
                ItemName = item.Item?.Name
            };

            return View(vm);
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
                if (result == null || result.Value.Bytes == null)
                {
                    TempData["ErrorMessage"] = "The requested attachment was not found. It may have been deleted.";
                    return RedirectToAction(nameof(Search));
                }

                var fileName = Path.GetFileName(id);
                return File(result.Value.Bytes, result.Value.ContentType, fileName);
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
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> Export(SearchViewModel? vm)
        {
            vm ??= new SearchViewModel();

            // Build the same query as Search but without pagination
            var query = _context.LostFoundItems.AsQueryable();

            if (vm.TrackingId.HasValue)
                query = query.Where(x => x.TrackingId == vm.TrackingId.Value);
            if (!string.IsNullOrEmpty(vm.CustomTrackingId))
                query = query.Where(x => x.CustomTrackingId != null && x.CustomTrackingId.Contains(vm.CustomTrackingId));
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

            var csvContent = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            // Prepend UTF-8 BOM so Windows Excel opens the file with correct encoding
            // Without BOM, Excel on Windows defaults to ANSI and garbles non-ASCII characters
            var bom = System.Text.Encoding.UTF8.GetPreamble();
            var bytes = new byte[bom.Length + csvContent.Length];
            bom.CopyTo(bytes, 0);
            csvContent.CopyTo(bytes, bom.Length);
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
            var item = await _context.LostFoundItems
                .Include(x => x.Status)
                .FirstOrDefaultAsync(x => x.TrackingId == id);

            if (item == null)
                return NotFound();

            // Guard: Claimed / Disposed records are permanent audit records
            var protectedStatuses = new[] { "Claimed", "Disposed" };
            if (item.Status != null && protectedStatuses.Contains(item.Status.Name, StringComparer.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = $"Record #{item.CustomTrackingId ?? id.ToString()} cannot be deleted because its status is \"{item.Status.Name}\". " +
                    "Claimed and Disposed records are permanent audit records and cannot be removed.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Capture file paths before removing entity so we can clean up after successful DB delete
            var photoPath = item.PhotoPath;
            var photoPath2 = item.PhotoPath2;
            var photoPath3 = item.PhotoPath3;
            var photoPath4 = item.PhotoPath4;
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
            _fileService.DeletePhoto(photoPath2);
            _fileService.DeletePhoto(photoPath3);
            _fileService.DeletePhoto(photoPath4);
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
                .Include(x => x.Status)
                .Where(x => idArray.Contains(x.TrackingId))
                .ToListAsync();

            if (!items.Any())
            {
                TempData["ErrorMessage"] = "No matching records found.";
                return RedirectToAction(nameof(Search));
            }

            // Guard: split into deletable vs protected
            var protectedStatuses = new[] { "Claimed", "Disposed" };
            var protectedItems = items
                .Where(x => x.Status != null && protectedStatuses.Contains(x.Status.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var deletableItems = items.Except(protectedItems).ToList();

            if (!deletableItems.Any())
            {
                TempData["ErrorMessage"] = $"None of the {items.Count} selected record(s) could be deleted. " +
                    "Claimed and Disposed records are permanent audit records and cannot be removed.";
                return RedirectToAction(nameof(Search));
            }

            var deletedCount = deletableItems.Count;
            var skippedCount = protectedItems.Count;

            // Capture file paths BEFORE removing entities
            var filesToDelete = deletableItems
                .Select(i => (Photo: i.PhotoPath, Attachment: i.AttachmentPath))
                .ToList();

            _context.LostFoundItems.RemoveRange(deletableItems);

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
                $"Bulk deleted {deletedCount} lost & found records." +
                (skippedCount > 0 ? $" {skippedCount} Claimed/Disposed record(s) were skipped (protected)." : ""),
                "Items");

            if (skippedCount > 0)
                TempData["WarningMessage"] = $"{deletedCount} record(s) deleted. {skippedCount} record(s) with status Claimed or Disposed were skipped - those are permanent audit records.";
            else
                TempData["SuccessMessage"] = $"{deletedCount} record(s) deleted successfully.";

            return RedirectToAction(nameof(Search));
        }

        // POST: /LostFoundItem/BulkStatusUpdate — Admin+ only
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> BulkStatusUpdate([FromForm] string ids, [FromForm] int statusId, [FromForm] DateTime? statusDate)
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

            // Validate status FK exists before saving — prevents DbUpdateException on invalid statusId
            var status = await _context.Statuses.FindAsync(statusId);
            if (status == null)
            {
                TempData["ErrorMessage"] = "Invalid status selected.";
                return RedirectToAction(nameof(Search));
            }

            var updatedCount = items.Count;

            foreach (var item in items)
            {
                item.StatusId = statusId;
                item.StatusDate = statusDate ?? DateTime.UtcNow.Date;
                item.ModifiedBy = User.Identity?.Name ?? "Unknown";
                item.ModifiedDateTime = DateTime.UtcNow;
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to bulk update status for {Count} records", updatedCount);
                TempData["ErrorMessage"] = "An error occurred while updating records. Please try again.";
                return RedirectToAction(nameof(Search));
            }

            _logger.LogInformation("Bulk updated status to '{Status}' for {Count} records by {User}",
                status.Name, updatedCount, User.Identity?.Name);
            await _activityLogService.LogAsync(HttpContext, "Bulk Status Update",
                $"Bulk updated status to '{status.Name}' for {updatedCount} records.", "Items");
            TempData["SuccessMessage"] = $"{updatedCount} record(s) updated to '{status.Name}'.";
            return RedirectToAction(nameof(Search));
        }

        // =====================================================================
        // HELPER METHODS
        // =====================================================================

        /// <summary>
        /// Parses a comma-separated string of IDs into an int array.
        /// Used by BulkDelete and BulkStatusUpdate to handle JS-generated hidden input values.
        /// </summary>

        private async Task<string> GenerateCustomTrackingIdAsync()
        {
            var today = DateTime.Today;
            var datePart = today.ToString("yyMMdd");
            var prefix = $"LF-{datePart}-";

            // Find the highest suffix for today
            var lastId = await _context.LostFoundItems
                .Where(x => x.CustomTrackingId != null && x.CustomTrackingId.StartsWith(prefix))
                .OrderByDescending(x => x.CustomTrackingId)
                .Select(x => x.CustomTrackingId)
                .FirstOrDefaultAsync();

            int nextNumber = 1;
            if (lastId != null)
            {
                var suffixStr = lastId.Substring(prefix.Length);
                if (int.TryParse(suffixStr, out int lastNumber))
                {
                    nextNumber = lastNumber + 1;
                }
            }

            return $"{prefix}{nextNumber:D4}";
        }

        private static int[] ParseIds(string ids)
        {
            if (string.IsNullOrWhiteSpace(ids)) return Array.Empty<int>();
            return ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => int.TryParse(s.Trim(), out int id) ? id : 0)
                      .Where(id => id > 0)
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
