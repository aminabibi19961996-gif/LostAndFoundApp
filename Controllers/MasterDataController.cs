using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LostAndFoundApp.Data;
using LostAndFoundApp.Models;
using LostAndFoundApp.Services;
using LostAndFoundApp.ViewModels;

namespace LostAndFoundApp.Controllers
{
    /// <summary>
    /// Handles full CRUD for all six master data tables and
    /// AJAX inline creation endpoints for dynamic dropdown population.
    /// SuperAdmin, Admin, and Supervisor can manage master data.
    /// User role has no access.
    /// </summary>
    [Authorize]
    public class MasterDataController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ActivityLogService _activityLogService;
        private readonly ILogger<MasterDataController> _logger;

        public MasterDataController(ApplicationDbContext context, ActivityLogService activityLogService, ILogger<MasterDataController> logger)
        {
            _context = context;
            _activityLogService = activityLogService;
            _logger = logger;
        }

        // =====================================================================
        // ITEMS
        // =====================================================================

        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> Items(int pageIndex = 1, int pageSize = PaginationParams.DefaultPageSize, string sortBy = "Name", string sortOrder = "asc")
        {
            pageIndex = Math.Max(1, pageIndex);
            pageSize = Math.Clamp(pageSize, 1, PaginationParams.MaxPageSize);
            var totalCount = await _context.Items.CountAsync();
            var query = _context.Items.AsQueryable();
            query = ApplySort(query, sortBy, sortOrder);
            var items = await query
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            var vm = new PaginatedList<Item>
            {
                Items = items,
                TotalCount = totalCount,
                PageIndex = pageIndex,
                PageSize = pageSize,
                SortBy = sortBy,
                SortOrder = sortOrder
            };
            return View(vm);
        }

        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public IActionResult CreateItem() => View(new Item());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> CreateItem(Item model)
        {
            if (!ModelState.IsValid) return View(model);
            if (await _context.Items.AnyAsync(x => x.Name == model.Name))
            {
                ModelState.AddModelError("Name", "An item with this name already exists.");
                return View(model);
            }
            _context.Items.Add(model);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Item '{model.Name}' created successfully.";
            await _activityLogService.LogAsync(HttpContext, "Create Item", $"Created item '{model.Name}'.", "MasterData");
            return RedirectToAction(nameof(Items));
        }

        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> EditItem(int id)
        {
            var item = await _context.Items.FindAsync(id);
            if (item == null) return NotFound();
            return View(item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> EditItem(Item model)
        {
            if (!ModelState.IsValid) return View(model);
            var existing = await _context.Items.FindAsync(model.Id);
            if (existing == null) return NotFound();
            if (await _context.Items.AnyAsync(x => x.Name == model.Name && x.Id != model.Id))
            {
                ModelState.AddModelError("Name", "An item with this name already exists.");
                return View(model);
            }
            existing.Name = model.Name;
            existing.IsActive = model.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Item '{existing.Name}' updated successfully.";
            await _activityLogService.LogAsync(HttpContext, "Edit Item", $"Updated item '{existing.Name}'.", "MasterData");
            return RedirectToAction(nameof(Items));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> DeleteItem(int id)
        {
            var item = await _context.Items.FindAsync(id);
            if (item == null) return NotFound();
            // Check if in use
            if (await _context.LostFoundItems.AnyAsync(x => x.ItemId == id))
            {
                TempData["ErrorMessage"] = $"Cannot delete '{item.Name}' because it is in use by existing records. Deactivate it instead.";
                return RedirectToAction(nameof(Items));
            }
            _context.Items.Remove(item);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Item '{item.Name}' deleted successfully.";
            await _activityLogService.LogAsync(HttpContext, "Delete Item", $"Deleted item '{item.Name}'.", "MasterData");
            return RedirectToAction(nameof(Items));
        }

        // =====================================================================
        // ROUTES
        // =====================================================================

        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> Routes(int pageIndex = 1, int pageSize = PaginationParams.DefaultPageSize, string sortBy = "Name", string sortOrder = "asc")
        {
            pageIndex = Math.Max(1, pageIndex);
            pageSize = Math.Clamp(pageSize, 1, PaginationParams.MaxPageSize);
            var totalCount = await _context.Routes.CountAsync();
            var query = _context.Routes.AsQueryable();
            query = ApplySort(query, sortBy, sortOrder);
            var routes = await query
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            var vm = new PaginatedList<Models.Route>
            {
                Items = routes,
                TotalCount = totalCount,
                PageIndex = pageIndex,
                PageSize = pageSize,
                SortBy = sortBy,
                SortOrder = sortOrder
            };
            return View(vm);
        }

        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public IActionResult CreateRoute() => View(new Models.Route());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> CreateRoute(Models.Route model)
        {
            if (!ModelState.IsValid) return View(model);
            if (await _context.Routes.AnyAsync(x => x.Name == model.Name))
            {
                ModelState.AddModelError("Name", "A route with this name already exists.");
                return View(model);
            }
            _context.Routes.Add(model);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Route '{model.Name}' created successfully.";
            await _activityLogService.LogAsync(HttpContext, "Create Route", $"Created route '{model.Name}'.", "MasterData");
            return RedirectToAction(nameof(Routes));
        }

        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> EditRoute(int id)
        {
            var route = await _context.Routes.FindAsync(id);
            if (route == null) return NotFound();
            return View(route);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> EditRoute(Models.Route model)
        {
            if (!ModelState.IsValid) return View(model);
            var existing = await _context.Routes.FindAsync(model.Id);
            if (existing == null) return NotFound();
            if (await _context.Routes.AnyAsync(x => x.Name == model.Name && x.Id != model.Id))
            {
                ModelState.AddModelError("Name", "A route with this name already exists.");
                return View(model);
            }
            existing.Name = model.Name;
            existing.IsActive = model.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Route '{existing.Name}' updated successfully.";
            await _activityLogService.LogAsync(HttpContext, "Edit Route", $"Updated route '{existing.Name}'.", "MasterData");
            return RedirectToAction(nameof(Routes));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> DeleteRoute(int id)
        {
            var route = await _context.Routes.FindAsync(id);
            if (route == null) return NotFound();
            if (await _context.LostFoundItems.AnyAsync(x => x.RouteId == id))
            {
                TempData["ErrorMessage"] = $"Cannot delete '{route.Name}' because it is in use.";
                return RedirectToAction(nameof(Routes));
            }
            _context.Routes.Remove(route);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Route '{route.Name}' deleted successfully.";
            await _activityLogService.LogAsync(HttpContext, "Delete Route", $"Deleted route '{route.Name}'.", "MasterData");
            return RedirectToAction(nameof(Routes));
        }

        // =====================================================================
        // VEHICLES
        // =====================================================================

        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> Vehicles(int pageIndex = 1, int pageSize = PaginationParams.DefaultPageSize, string sortBy = "Name", string sortOrder = "asc")
        {
            pageIndex = Math.Max(1, pageIndex);
            pageSize = Math.Clamp(pageSize, 1, PaginationParams.MaxPageSize);
            var totalCount = await _context.Vehicles.CountAsync();
            var query = _context.Vehicles.AsQueryable();
            query = ApplySort(query, sortBy, sortOrder);
            var vehicles = await query
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            var vm = new PaginatedList<Vehicle>
            {
                Items = vehicles,
                TotalCount = totalCount,
                PageIndex = pageIndex,
                PageSize = pageSize,
                SortBy = sortBy,
                SortOrder = sortOrder
            };
            return View(vm);
        }

        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public IActionResult CreateVehicle() => View(new Vehicle());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> CreateVehicle(Vehicle model)
        {
            if (!ModelState.IsValid) return View(model);
            if (await _context.Vehicles.AnyAsync(x => x.Name == model.Name))
            {
                ModelState.AddModelError("Name", "A vehicle with this name already exists.");
                return View(model);
            }
            _context.Vehicles.Add(model);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Vehicle '{model.Name}' created successfully.";
            await _activityLogService.LogAsync(HttpContext, "Create Vehicle", $"Created vehicle '{model.Name}'.", "MasterData");
            return RedirectToAction(nameof(Vehicles));
        }

        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> EditVehicle(int id)
        {
            var vehicle = await _context.Vehicles.FindAsync(id);
            if (vehicle == null) return NotFound();
            return View(vehicle);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> EditVehicle(Vehicle model)
        {
            if (!ModelState.IsValid) return View(model);
            var existing = await _context.Vehicles.FindAsync(model.Id);
            if (existing == null) return NotFound();
            if (await _context.Vehicles.AnyAsync(x => x.Name == model.Name && x.Id != model.Id))
            {
                ModelState.AddModelError("Name", "A vehicle with this name already exists.");
                return View(model);
            }
            existing.Name = model.Name;
            existing.IsActive = model.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Vehicle '{existing.Name}' updated successfully.";
            await _activityLogService.LogAsync(HttpContext, "Edit Vehicle", $"Updated vehicle '{existing.Name}'.", "MasterData");
            return RedirectToAction(nameof(Vehicles));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> DeleteVehicle(int id)
        {
            var vehicle = await _context.Vehicles.FindAsync(id);
            if (vehicle == null) return NotFound();
            if (await _context.LostFoundItems.AnyAsync(x => x.VehicleId == id))
            {
                TempData["ErrorMessage"] = $"Cannot delete '{vehicle.Name}' because it is in use.";
                return RedirectToAction(nameof(Vehicles));
            }
            _context.Vehicles.Remove(vehicle);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Vehicle '{vehicle.Name}' deleted successfully.";
            await _activityLogService.LogAsync(HttpContext, "Delete Vehicle", $"Deleted vehicle '{vehicle.Name}'.", "MasterData");
            return RedirectToAction(nameof(Vehicles));
        }

        // =====================================================================
        // STORAGE LOCATIONS
        // =====================================================================

        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> StorageLocations(int pageIndex = 1, int pageSize = PaginationParams.DefaultPageSize, string sortBy = "Name", string sortOrder = "asc")
        {
            pageIndex = Math.Max(1, pageIndex);
            pageSize = Math.Clamp(pageSize, 1, PaginationParams.MaxPageSize);
            var totalCount = await _context.StorageLocations.CountAsync();
            var query = _context.StorageLocations.AsQueryable();
            query = ApplySort(query, sortBy, sortOrder);
            var locations = await query
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            var vm = new PaginatedList<StorageLocation>
            {
                Items = locations,
                TotalCount = totalCount,
                PageIndex = pageIndex,
                PageSize = pageSize,
                SortBy = sortBy,
                SortOrder = sortOrder
            };
            return View(vm);
        }

        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public IActionResult CreateStorageLocation() => View(new StorageLocation());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> CreateStorageLocation(StorageLocation model)
        {
            if (!ModelState.IsValid) return View(model);
            if (await _context.StorageLocations.AnyAsync(x => x.Name == model.Name))
            {
                ModelState.AddModelError("Name", "A storage location with this name already exists.");
                return View(model);
            }
            _context.StorageLocations.Add(model);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Storage Location '{model.Name}' created successfully.";
            await _activityLogService.LogAsync(HttpContext, "Create Storage Location", $"Created storage location '{model.Name}'.", "MasterData");
            return RedirectToAction(nameof(StorageLocations));
        }

        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> EditStorageLocation(int id)
        {
            var location = await _context.StorageLocations.FindAsync(id);
            if (location == null) return NotFound();
            return View(location);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> EditStorageLocation(StorageLocation model)
        {
            if (!ModelState.IsValid) return View(model);
            var existing = await _context.StorageLocations.FindAsync(model.Id);
            if (existing == null) return NotFound();
            if (await _context.StorageLocations.AnyAsync(x => x.Name == model.Name && x.Id != model.Id))
            {
                ModelState.AddModelError("Name", "A storage location with this name already exists.");
                return View(model);
            }
            existing.Name = model.Name;
            existing.IsActive = model.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Storage Location '{existing.Name}' updated successfully.";
            await _activityLogService.LogAsync(HttpContext, "Edit Storage Location", $"Updated storage location '{existing.Name}'.", "MasterData");
            return RedirectToAction(nameof(StorageLocations));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> DeleteStorageLocation(int id)
        {
            var location = await _context.StorageLocations.FindAsync(id);
            if (location == null) return NotFound();
            if (await _context.LostFoundItems.AnyAsync(x => x.StorageLocationId == id))
            {
                TempData["ErrorMessage"] = $"Cannot delete '{location.Name}' because it is in use.";
                return RedirectToAction(nameof(StorageLocations));
            }
            _context.StorageLocations.Remove(location);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Storage Location '{location.Name}' deleted successfully.";
            await _activityLogService.LogAsync(HttpContext, "Delete Storage Location", $"Deleted storage location '{location.Name}'.", "MasterData");
            return RedirectToAction(nameof(StorageLocations));
        }

        // =====================================================================
        // STATUSES
        // =====================================================================

        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> Statuses(int pageIndex = 1, int pageSize = PaginationParams.DefaultPageSize, string sortBy = "Name", string sortOrder = "asc")
        {
            pageIndex = Math.Max(1, pageIndex);
            pageSize = Math.Clamp(pageSize, 1, PaginationParams.MaxPageSize);
            var totalCount = await _context.Statuses.CountAsync();
            var query = _context.Statuses.AsQueryable();
            query = ApplySort(query, sortBy, sortOrder);
            var statuses = await query
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            var vm = new PaginatedList<Status>
            {
                Items = statuses,
                TotalCount = totalCount,
                PageIndex = pageIndex,
                PageSize = pageSize,
                SortBy = sortBy,
                SortOrder = sortOrder
            };
            return View(vm);
        }

        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public IActionResult CreateStatus() => View(new Status());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> CreateStatus(Status model)
        {
            if (!ModelState.IsValid) return View(model);
            if (await _context.Statuses.AnyAsync(x => x.Name == model.Name))
            {
                ModelState.AddModelError("Name", "A status with this name already exists.");
                return View(model);
            }
            _context.Statuses.Add(model);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Status '{model.Name}' created successfully.";
            await _activityLogService.LogAsync(HttpContext, "Create Status", $"Created status '{model.Name}'.", "MasterData");
            return RedirectToAction(nameof(Statuses));
        }

        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> EditStatus(int id)
        {
            var status = await _context.Statuses.FindAsync(id);
            if (status == null) return NotFound();
            return View(status);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> EditStatus(Status model)
        {
            if (!ModelState.IsValid) return View(model);
            var existing = await _context.Statuses.FindAsync(model.Id);
            if (existing == null) return NotFound();
            if (await _context.Statuses.AnyAsync(x => x.Name == model.Name && x.Id != model.Id))
            {
                ModelState.AddModelError("Name", "A status with this name already exists.");
                return View(model);
            }
            existing.Name = model.Name;
            existing.IsActive = model.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Status '{existing.Name}' updated successfully.";
            await _activityLogService.LogAsync(HttpContext, "Edit Status", $"Updated status '{existing.Name}'.", "MasterData");
            return RedirectToAction(nameof(Statuses));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> DeleteStatus(int id)
        {
            var status = await _context.Statuses.FindAsync(id);
            if (status == null) return NotFound();
            if (await _context.LostFoundItems.AnyAsync(x => x.StatusId == id))
            {
                TempData["ErrorMessage"] = $"Cannot delete '{status.Name}' because it is in use.";
                return RedirectToAction(nameof(Statuses));
            }
            _context.Statuses.Remove(status);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Status '{status.Name}' deleted successfully.";
            await _activityLogService.LogAsync(HttpContext, "Delete Status", $"Deleted status '{status.Name}'.", "MasterData");
            return RedirectToAction(nameof(Statuses));
        }

        // =====================================================================
        // FOUND BY NAMES
        // =====================================================================

        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> FoundByNames(int pageIndex = 1, int pageSize = PaginationParams.DefaultPageSize, string sortBy = "Name", string sortOrder = "asc")
        {
            pageIndex = Math.Max(1, pageIndex);
            pageSize = Math.Clamp(pageSize, 1, PaginationParams.MaxPageSize);
            var totalCount = await _context.FoundByNames.CountAsync();
            var query = _context.FoundByNames.AsQueryable();
            query = ApplySort(query, sortBy, sortOrder);
            var names = await query
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            var vm = new PaginatedList<FoundByName>
            {
                Items = names,
                TotalCount = totalCount,
                PageIndex = pageIndex,
                PageSize = pageSize,
                SortBy = sortBy,
                SortOrder = sortOrder
            };
            return View(vm);
        }

        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public IActionResult CreateFoundByName() => View(new FoundByName());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> CreateFoundByName(FoundByName model)
        {
            if (!ModelState.IsValid) return View(model);
            if (await _context.FoundByNames.AnyAsync(x => x.Name == model.Name))
            {
                ModelState.AddModelError("Name", "This name already exists.");
                return View(model);
            }
            _context.FoundByNames.Add(model);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Found By Name '{model.Name}' created successfully.";
            await _activityLogService.LogAsync(HttpContext, "Create Found By Name", $"Created found by name '{model.Name}'.", "MasterData");
            return RedirectToAction(nameof(FoundByNames));
        }

        [HttpGet]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> EditFoundByName(int id)
        {
            var name = await _context.FoundByNames.FindAsync(id);
            if (name == null) return NotFound();
            return View(name);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> EditFoundByName(FoundByName model)
        {
            if (!ModelState.IsValid) return View(model);
            var existing = await _context.FoundByNames.FindAsync(model.Id);
            if (existing == null) return NotFound();
            if (await _context.FoundByNames.AnyAsync(x => x.Name == model.Name && x.Id != model.Id))
            {
                ModelState.AddModelError("Name", "This name already exists.");
                return View(model);
            }
            existing.Name = model.Name;
            existing.IsActive = model.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Found By Name '{existing.Name}' updated successfully.";
            await _activityLogService.LogAsync(HttpContext, "Edit Found By Name", $"Updated found by name '{existing.Name}'.", "MasterData");
            return RedirectToAction(nameof(FoundByNames));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> DeleteFoundByName(int id)
        {
            var name = await _context.FoundByNames.FindAsync(id);
            if (name == null) return NotFound();
            if (await _context.LostFoundItems.AnyAsync(x => x.FoundById == id))
            {
                TempData["ErrorMessage"] = $"Cannot delete '{name.Name}' because it is in use.";
                return RedirectToAction(nameof(FoundByNames));
            }
            _context.FoundByNames.Remove(name);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Found By Name '{name.Name}' deleted successfully.";
            await _activityLogService.LogAsync(HttpContext, "Delete Found By Name", $"Deleted found by name '{name.Name}'.", "MasterData");
            return RedirectToAction(nameof(FoundByNames));
        }

        // =====================================================================
        // TOGGLE ACTIVE (SOFT DEACTIVATION) FOR ALL MASTER DATA
        // Enterprise pattern: deactivate instead of hard-delete for referential safety
        // =====================================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> ToggleItemActive(int id)
        {
            var item = await _context.Items.FindAsync(id);
            if (item == null) return NotFound();
            item.IsActive = !item.IsActive;
            await _context.SaveChangesAsync();
            var status = item.IsActive ? "activated" : "deactivated";
            TempData["SuccessMessage"] = $"Item '{item.Name}' has been {status}.";
            await _activityLogService.LogAsync(HttpContext, "Toggle Item", $"Item '{item.Name}' has been {status}.", "MasterData");
            return RedirectToAction(nameof(Items));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> ToggleRouteActive(int id)
        {
            var route = await _context.Routes.FindAsync(id);
            if (route == null) return NotFound();
            route.IsActive = !route.IsActive;
            await _context.SaveChangesAsync();
            var status = route.IsActive ? "activated" : "deactivated";
            TempData["SuccessMessage"] = $"Route '{route.Name}' has been {status}.";
            await _activityLogService.LogAsync(HttpContext, "Toggle Route", $"Route '{route.Name}' has been {status}.", "MasterData");
            return RedirectToAction(nameof(Routes));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> ToggleVehicleActive(int id)
        {
            var vehicle = await _context.Vehicles.FindAsync(id);
            if (vehicle == null) return NotFound();
            vehicle.IsActive = !vehicle.IsActive;
            await _context.SaveChangesAsync();
            var status = vehicle.IsActive ? "activated" : "deactivated";
            TempData["SuccessMessage"] = $"Vehicle '{vehicle.Name}' has been {status}.";
            await _activityLogService.LogAsync(HttpContext, "Toggle Vehicle", $"Vehicle '{vehicle.Name}' has been {status}.", "MasterData");
            return RedirectToAction(nameof(Vehicles));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> ToggleStorageLocationActive(int id)
        {
            var location = await _context.StorageLocations.FindAsync(id);
            if (location == null) return NotFound();
            location.IsActive = !location.IsActive;
            await _context.SaveChangesAsync();
            var status = location.IsActive ? "activated" : "deactivated";
            TempData["SuccessMessage"] = $"Storage Location '{location.Name}' has been {status}.";
            await _activityLogService.LogAsync(HttpContext, "Toggle Storage Location", $"Storage location '{location.Name}' has been {status}.", "MasterData");
            return RedirectToAction(nameof(StorageLocations));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> ToggleStatusActive(int id)
        {
            var status = await _context.Statuses.FindAsync(id);
            if (status == null) return NotFound();
            status.IsActive = !status.IsActive;
            await _context.SaveChangesAsync();
            var state = status.IsActive ? "activated" : "deactivated";
            TempData["SuccessMessage"] = $"Status '{status.Name}' has been {state}.";
            await _activityLogService.LogAsync(HttpContext, "Toggle Status", $"Status '{status.Name}' has been {state}.", "MasterData");
            return RedirectToAction(nameof(Statuses));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> ToggleFoundByNameActive(int id)
        {
            var name = await _context.FoundByNames.FindAsync(id);
            if (name == null) return NotFound();
            name.IsActive = !name.IsActive;
            await _context.SaveChangesAsync();
            var status = name.IsActive ? "activated" : "deactivated";
            TempData["SuccessMessage"] = $"Found By Name '{name.Name}' has been {status}.";
            await _activityLogService.LogAsync(HttpContext, "Toggle Found By Name", $"Found by name '{name.Name}' has been {status}.", "MasterData");
            return RedirectToAction(nameof(FoundByNames));
        }

        // =====================================================================
        // AJAX ENDPOINTS FOR INLINE DROPDOWN CREATION
        // Supervisor, Admin, and SuperAdmin can create new values inline from the item form.
        // User role cannot create inline master data.
        // =====================================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> AddItemAjax([FromBody] MasterDataAjaxRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Name))
                    return Json(new { success = false, message = "Name is required." });
                var trimmed = request.Name.Trim();
                // Case-insensitive comparison to prevent DbUpdateException on duplicates differing only by case
                var existing = await _context.Items.FirstOrDefaultAsync(x => x.Name.ToLower() == trimmed.ToLower());
                if (existing != null)
                    return Json(new { success = true, id = existing.Id, name = existing.Name });
                var entity = new Item { Name = trimmed };
                _context.Items.Add(entity);
                await _context.SaveChangesAsync();
                await _activityLogService.LogAsync(HttpContext, "Inline Create Item", $"Created item '{trimmed}' via inline dropdown.", "MasterData");
                return Json(new { success = true, id = entity.Id, name = entity.Name });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating item via AJAX");
                return Json(new { success = false, message = "An error occurred while creating the item. Please try again." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> AddRouteAjax([FromBody] MasterDataAjaxRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Name))
                    return Json(new { success = false, message = "Name is required." });
                var trimmed = request.Name.Trim();
                // Case-insensitive comparison to prevent DbUpdateException on duplicates differing only by case
                var existing = await _context.Routes.FirstOrDefaultAsync(x => x.Name.ToLower() == trimmed.ToLower());
                if (existing != null)
                    return Json(new { success = true, id = existing.Id, name = existing.Name });
                var entity = new Models.Route { Name = trimmed };
                _context.Routes.Add(entity);
                await _context.SaveChangesAsync();
                await _activityLogService.LogAsync(HttpContext, "Inline Create Route", $"Created route '{trimmed}' via inline dropdown.", "MasterData");
                return Json(new { success = true, id = entity.Id, name = entity.Name });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating route via AJAX");
                return Json(new { success = false, message = "An error occurred while creating the route. Please try again." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> AddVehicleAjax([FromBody] MasterDataAjaxRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Name))
                    return Json(new { success = false, message = "Name is required." });
                var trimmed = request.Name.Trim();
                // Case-insensitive comparison to prevent DbUpdateException on duplicates differing only by case
                var existing = await _context.Vehicles.FirstOrDefaultAsync(x => x.Name.ToLower() == trimmed.ToLower());
                if (existing != null)
                    return Json(new { success = true, id = existing.Id, name = existing.Name });
                var entity = new Vehicle { Name = trimmed };
                _context.Vehicles.Add(entity);
                await _context.SaveChangesAsync();
                await _activityLogService.LogAsync(HttpContext, "Inline Create Vehicle", $"Created vehicle '{trimmed}' via inline dropdown.", "MasterData");
                return Json(new { success = true, id = entity.Id, name = entity.Name });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating vehicle via AJAX");
                return Json(new { success = false, message = "An error occurred while creating the vehicle. Please try again." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> AddStorageLocationAjax([FromBody] MasterDataAjaxRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Name))
                    return Json(new { success = false, message = "Name is required." });
                var trimmed = request.Name.Trim();
                // Case-insensitive comparison to prevent DbUpdateException on duplicates differing only by case
                var existing = await _context.StorageLocations.FirstOrDefaultAsync(x => x.Name.ToLower() == trimmed.ToLower());
                if (existing != null)
                    return Json(new { success = true, id = existing.Id, name = existing.Name });
                var entity = new StorageLocation { Name = trimmed };
                _context.StorageLocations.Add(entity);
                await _context.SaveChangesAsync();
                await _activityLogService.LogAsync(HttpContext, "Inline Create Storage Location", $"Created storage location '{trimmed}' via inline dropdown.", "MasterData");
                return Json(new { success = true, id = entity.Id, name = entity.Name });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating storage location via AJAX");
                return Json(new { success = false, message = "An error occurred while creating the storage location. Please try again." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> AddStatusAjax([FromBody] MasterDataAjaxRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Name))
                    return Json(new { success = false, message = "Name is required." });
                var trimmed = request.Name.Trim();
                // Case-insensitive comparison to prevent DbUpdateException on duplicates differing only by case
                var existing = await _context.Statuses.FirstOrDefaultAsync(x => x.Name.ToLower() == trimmed.ToLower());
                if (existing != null)
                    return Json(new { success = true, id = existing.Id, name = existing.Name });
                var entity = new Status { Name = trimmed };
                _context.Statuses.Add(entity);
                await _context.SaveChangesAsync();
                await _activityLogService.LogAsync(HttpContext, "Inline Create Status", $"Created status '{trimmed}' via inline dropdown.", "MasterData");
                return Json(new { success = true, id = entity.Id, name = entity.Name });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating status via AJAX");
                return Json(new { success = false, message = "An error occurred while creating the status. Please try again." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireSupervisorOrAbove")]
        public async Task<IActionResult> AddFoundByNameAjax([FromBody] MasterDataAjaxRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Name))
                    return Json(new { success = false, message = "Name is required." });
                var trimmed = request.Name.Trim();
                // Case-insensitive comparison to prevent DbUpdateException on duplicates differing only by case
                var existing = await _context.FoundByNames.FirstOrDefaultAsync(x => x.Name.ToLower() == trimmed.ToLower());
                if (existing != null)
                    return Json(new { success = true, id = existing.Id, name = existing.Name });
                var entity = new FoundByName { Name = trimmed };
                _context.FoundByNames.Add(entity);
                await _context.SaveChangesAsync();
                await _activityLogService.LogAsync(HttpContext, "Inline Create Found By Name", $"Created found by name '{trimmed}' via inline dropdown.", "MasterData");
                return Json(new { success = true, id = entity.Id, name = entity.Name });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating found by name via AJAX");
                return Json(new { success = false, message = "An error occurred while creating the name. Please try again." });
            }
        }

        // =====================================================================
        // BULK CSV IMPORT — Admin / SuperAdmin only
        // =====================================================================

        /// <summary>
        /// Accepts a CSV file and bulk-imports records into the specified master data table.
        /// The CSV must have a header row followed by one Name per row.
        /// Duplicates (case-insensitive) are skipped. All imported records are set to Active.
        /// Supported entityType values: Items, Routes, Vehicles, StorageLocations, Statuses, FoundByNames
        /// </summary>
        [HttpPost]
        [Authorize(Policy = "RequireAdminOrAbove")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportCsv(string entityType, IFormFile csvFile)
        {
            if (csvFile == null || csvFile.Length == 0)
                return Json(new { success = false, message = "No file was uploaded." });

            if (!csvFile.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Only .csv files are supported." });

            // ── Parse CSV ──────────────────────────────────────────────────────
            var lines = new List<string>();
            using (var reader = new System.IO.StreamReader(csvFile.OpenReadStream()))
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                    lines.Add(line);
            }

            if (lines.Count < 2)
                return Json(new { success = false, message = "The CSV file must have a header row and at least one data row." });

            // bug fix #7: handle RFC 4180 quoted fields — e.g. "Smith, John" must not be split at the comma
            var names = lines.Skip(1)
                .Select(l => ParseFirstCsvField(l))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            if (names.Count == 0)
                return Json(new { success = false, message = "No valid names found in the CSV file." });

            // ── Load existing names for duplicate check ─────────────────────────
            int inserted = 0, skipped = 0;
            var entityLabel = entityType;

            try
            {
                switch (entityType)
                {
                    case "Items":
                    {
                        var existing = await _context.Items.Select(x => x.Name.ToLower()).ToListAsync();
                        var existingSet = new HashSet<string>(existing);
                        foreach (var name in names)
                        {
                            if (existingSet.Contains(name.ToLower())) { skipped++; continue; }
                            _context.Items.Add(new Item { Name = name, IsActive = true });
                            existingSet.Add(name.ToLower());
                            inserted++;
                        }
                        break;
                    }
                    case "Routes":
                    {
                        var existing = await _context.Routes.Select(x => x.Name.ToLower()).ToListAsync();
                        var existingSet = new HashSet<string>(existing);
                        foreach (var name in names)
                        {
                            if (existingSet.Contains(name.ToLower())) { skipped++; continue; }
                            _context.Routes.Add(new Models.Route { Name = name, IsActive = true });
                            existingSet.Add(name.ToLower());
                            inserted++;
                        }
                        break;
                    }
                    case "Vehicles":
                    {
                        var existing = await _context.Vehicles.Select(x => x.Name.ToLower()).ToListAsync();
                        var existingSet = new HashSet<string>(existing);
                        foreach (var name in names)
                        {
                            if (existingSet.Contains(name.ToLower())) { skipped++; continue; }
                            _context.Vehicles.Add(new Vehicle { Name = name, IsActive = true });
                            existingSet.Add(name.ToLower());
                            inserted++;
                        }
                        break;
                    }
                    case "StorageLocations":
                    {
                        var existing = await _context.StorageLocations.Select(x => x.Name.ToLower()).ToListAsync();
                        var existingSet = new HashSet<string>(existing);
                        foreach (var name in names)
                        {
                            if (existingSet.Contains(name.ToLower())) { skipped++; continue; }
                            _context.StorageLocations.Add(new StorageLocation { Name = name, IsActive = true });
                            existingSet.Add(name.ToLower());
                            inserted++;
                        }
                        break;
                    }
                    case "Statuses":
                    {
                        var existing = await _context.Statuses.Select(x => x.Name.ToLower()).ToListAsync();
                        var existingSet = new HashSet<string>(existing);
                        foreach (var name in names)
                        {
                            if (existingSet.Contains(name.ToLower())) { skipped++; continue; }
                            _context.Statuses.Add(new Status { Name = name, IsActive = true });
                            existingSet.Add(name.ToLower());
                            inserted++;
                        }
                        break;
                    }
                    case "FoundByNames":
                    {
                        var existing = await _context.FoundByNames.Select(x => x.Name.ToLower()).ToListAsync();
                        var existingSet = new HashSet<string>(existing);
                        foreach (var name in names)
                        {
                            if (existingSet.Contains(name.ToLower())) { skipped++; continue; }
                            _context.FoundByNames.Add(new FoundByName { Name = name, IsActive = true });
                            existingSet.Add(name.ToLower());
                            inserted++;
                        }
                        break;
                    }
                    default:
                        return Json(new { success = false, message = $"Unknown entity type: {entityType}" });
                }

                if (inserted > 0)
                    await _context.SaveChangesAsync();

                await _activityLogService.LogAsync(HttpContext, "Bulk CSV Import",
                    $"Imported {inserted} record(s) into {entityType} (skipped {skipped} duplicate(s)) via CSV.", "MasterData");

                return Json(new
                {
                    success = true,
                    inserted,
                    skipped,
                    message = inserted == 0
                        ? $"No new records were imported — all {skipped} row(s) were duplicates already in the system."
                        : $"Successfully imported {inserted} record(s). {(skipped > 0 ? $"{skipped} duplicate(s) were skipped." : "")}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during CSV import for {EntityType}", entityType);
                return Json(new { success = false, message = "An unexpected error occurred during import. Please try again." });
            }
        }

        /// <summary>
        /// Extracts the first field from a CSV line, correctly handling RFC 4180 quoted fields
        /// that may contain commas (e.g. "Smith, John" → "Smith, John", not "Smith").
        /// </summary>
        private static string ParseFirstCsvField(string line)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;
            if (line[0] == '"')
            {
                // Quoted field: find closing quote (doubled quotes are escaped quotes inside)
                var end = line.IndexOf('"', 1);
                while (end >= 0 && end + 1 < line.Length && line[end + 1] == '"')
                    end = line.IndexOf('"', end + 2);
                return end > 0 ? line.Substring(1, end - 1).Replace("\"\"", "\"") : line.Trim('"');
            }
            // Unquoted field: everything up to the first comma
            var commaIdx = line.IndexOf(',');
            return (commaIdx >= 0 ? line[..commaIdx] : line).Trim();
        }

        /// <summary>
        /// Generic sort helper for any master data entity with Name and IsActive properties.
        /// Supports sorting by "Name" or "Status" (IsActive) in "asc" or "desc" order.
        /// </summary>
        private static IQueryable<T> ApplySort<T>(IQueryable<T> query, string sortBy, string sortOrder) where T : class
        {
            var isDesc = string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase);

            // Build expression dynamically for the property
            var param = System.Linq.Expressions.Expression.Parameter(typeof(T), "x");

            if (string.Equals(sortBy, "Status", StringComparison.OrdinalIgnoreCase))
            {
                var prop = System.Linq.Expressions.Expression.Property(param, "IsActive");
                var lambda = System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(prop, param);
                return isDesc ? query.OrderByDescending(lambda) : query.OrderBy(lambda);
            }
            else // Default: sort by Name
            {
                var prop = System.Linq.Expressions.Expression.Property(param, "Name");
                var lambda = System.Linq.Expressions.Expression.Lambda<Func<T, string>>(prop, param);
                return isDesc ? query.OrderByDescending(lambda) : query.OrderBy(lambda);
            }
        }
    }
}

