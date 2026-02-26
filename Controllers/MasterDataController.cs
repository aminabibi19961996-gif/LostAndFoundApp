using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LostAndFoundApp.Data;
using LostAndFoundApp.Models;
using LostAndFoundApp.Services;

namespace LostAndFoundApp.Controllers
{
    /// <summary>
    /// Handles full CRUD for all six master data tables (Admin+) and
    /// AJAX inline creation endpoints for dynamic dropdown population (all authenticated users).
    /// </summary>
    [Authorize]
    public class MasterDataController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ActivityLogService _activityLogService;

        public MasterDataController(ApplicationDbContext context, ActivityLogService activityLogService)
        {
            _context = context;
            _activityLogService = activityLogService;
        }

        // =====================================================================
        // ITEMS
        // =====================================================================

        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> Items()
        {
            var items = await _context.Items.OrderBy(x => x.Name).ToListAsync();
            return View(items);
        }

        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public IActionResult CreateItem() => View(new Item());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
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
            await _activityLogService.LogAsync(HttpContext, "Create Item", $"Created item type '{model.Name}'.", "MasterData");
            return RedirectToAction(nameof(Items));
        }

        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> EditItem(int id)
        {
            var item = await _context.Items.FindAsync(id);
            if (item == null) return NotFound();
            return View(item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
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
            await _activityLogService.LogAsync(HttpContext, "Edit Item", $"Updated item type '{existing.Name}'.", "MasterData");
            return RedirectToAction(nameof(Items));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
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
            await _activityLogService.LogAsync(HttpContext, "Delete Item", $"Deleted item type '{item.Name}'.", "MasterData");
            return RedirectToAction(nameof(Items));
        }

        // =====================================================================
        // ROUTES
        // =====================================================================

        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> Routes()
        {
            var routes = await _context.Routes.OrderBy(x => x.Name).ToListAsync();
            return View(routes);
        }

        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public IActionResult CreateRoute() => View(new Models.Route());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
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
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> EditRoute(int id)
        {
            var route = await _context.Routes.FindAsync(id);
            if (route == null) return NotFound();
            return View(route);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
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
        [Authorize(Policy = "RequireAdminOrAbove")]
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

        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> Vehicles()
        {
            var vehicles = await _context.Vehicles.OrderBy(x => x.Name).ToListAsync();
            return View(vehicles);
        }

        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public IActionResult CreateVehicle() => View(new Vehicle());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
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
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> EditVehicle(int id)
        {
            var vehicle = await _context.Vehicles.FindAsync(id);
            if (vehicle == null) return NotFound();
            return View(vehicle);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
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
        [Authorize(Policy = "RequireAdminOrAbove")]
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

        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> StorageLocations()
        {
            var locations = await _context.StorageLocations.OrderBy(x => x.Name).ToListAsync();
            return View(locations);
        }

        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public IActionResult CreateStorageLocation() => View(new StorageLocation());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
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
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> EditStorageLocation(int id)
        {
            var location = await _context.StorageLocations.FindAsync(id);
            if (location == null) return NotFound();
            return View(location);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
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
        [Authorize(Policy = "RequireAdminOrAbove")]
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

        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> Statuses()
        {
            var statuses = await _context.Statuses.OrderBy(x => x.Name).ToListAsync();
            return View(statuses);
        }

        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public IActionResult CreateStatus() => View(new Status());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
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
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> EditStatus(int id)
        {
            var status = await _context.Statuses.FindAsync(id);
            if (status == null) return NotFound();
            return View(status);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
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
        [Authorize(Policy = "RequireAdminOrAbove")]
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

        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> FoundByNames()
        {
            var names = await _context.FoundByNames.OrderBy(x => x.Name).ToListAsync();
            return View(names);
        }

        [HttpGet]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public IActionResult CreateFoundByName() => View(new FoundByName());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
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
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> EditFoundByName(int id)
        {
            var name = await _context.FoundByNames.FindAsync(id);
            if (name == null) return NotFound();
            return View(name);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
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
        [Authorize(Policy = "RequireAdminOrAbove")]
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
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> ToggleItemActive(int id)
        {
            var item = await _context.Items.FindAsync(id);
            if (item == null) return NotFound();
            item.IsActive = !item.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Item '{item.Name}' has been {(item.IsActive ? "activated" : "deactivated")}.";
            return RedirectToAction(nameof(Items));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> ToggleRouteActive(int id)
        {
            var route = await _context.Routes.FindAsync(id);
            if (route == null) return NotFound();
            route.IsActive = !route.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Route '{route.Name}' has been {(route.IsActive ? "activated" : "deactivated")}.";
            return RedirectToAction(nameof(Routes));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> ToggleVehicleActive(int id)
        {
            var vehicle = await _context.Vehicles.FindAsync(id);
            if (vehicle == null) return NotFound();
            vehicle.IsActive = !vehicle.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Vehicle '{vehicle.Name}' has been {(vehicle.IsActive ? "activated" : "deactivated")}.";
            return RedirectToAction(nameof(Vehicles));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> ToggleStorageLocationActive(int id)
        {
            var location = await _context.StorageLocations.FindAsync(id);
            if (location == null) return NotFound();
            location.IsActive = !location.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Storage Location '{location.Name}' has been {(location.IsActive ? "activated" : "deactivated")}.";
            return RedirectToAction(nameof(StorageLocations));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> ToggleStatusActive(int id)
        {
            var status = await _context.Statuses.FindAsync(id);
            if (status == null) return NotFound();
            status.IsActive = !status.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Status '{status.Name}' has been {(status.IsActive ? "activated" : "deactivated")}.";
            return RedirectToAction(nameof(Statuses));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> ToggleFoundByNameActive(int id)
        {
            var name = await _context.FoundByNames.FindAsync(id);
            if (name == null) return NotFound();
            name.IsActive = !name.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Found By Name '{name.Name}' has been {(name.IsActive ? "activated" : "deactivated")}.";
            return RedirectToAction(nameof(FoundByNames));
        }

        // =====================================================================
        // AJAX ENDPOINTS FOR INLINE DROPDOWN CREATION
        // All authenticated users can create new values inline from the item form
        // =====================================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> AddItemAjax([FromBody] MasterDataAjaxRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Name))
                return Json(new { success = false, message = "Name is required." });
            var trimmed = request.Name.Trim();
            var existing = await _context.Items.FirstOrDefaultAsync(x => x.Name == trimmed);
            if (existing != null)
                return Json(new { success = true, id = existing.Id, name = existing.Name });
            var entity = new Item { Name = trimmed };
            _context.Items.Add(entity);
            await _context.SaveChangesAsync();
            return Json(new { success = true, id = entity.Id, name = entity.Name });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> AddRouteAjax([FromBody] MasterDataAjaxRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Name))
                return Json(new { success = false, message = "Name is required." });
            var trimmed = request.Name.Trim();
            var existing = await _context.Routes.FirstOrDefaultAsync(x => x.Name == trimmed);
            if (existing != null)
                return Json(new { success = true, id = existing.Id, name = existing.Name });
            var entity = new Models.Route { Name = trimmed };
            _context.Routes.Add(entity);
            await _context.SaveChangesAsync();
            return Json(new { success = true, id = entity.Id, name = entity.Name });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> AddVehicleAjax([FromBody] MasterDataAjaxRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Name))
                return Json(new { success = false, message = "Name is required." });
            var trimmed = request.Name.Trim();
            var existing = await _context.Vehicles.FirstOrDefaultAsync(x => x.Name == trimmed);
            if (existing != null)
                return Json(new { success = true, id = existing.Id, name = existing.Name });
            var entity = new Vehicle { Name = trimmed };
            _context.Vehicles.Add(entity);
            await _context.SaveChangesAsync();
            return Json(new { success = true, id = entity.Id, name = entity.Name });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> AddStorageLocationAjax([FromBody] MasterDataAjaxRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Name))
                return Json(new { success = false, message = "Name is required." });
            var trimmed = request.Name.Trim();
            var existing = await _context.StorageLocations.FirstOrDefaultAsync(x => x.Name == trimmed);
            if (existing != null)
                return Json(new { success = true, id = existing.Id, name = existing.Name });
            var entity = new StorageLocation { Name = trimmed };
            _context.StorageLocations.Add(entity);
            await _context.SaveChangesAsync();
            return Json(new { success = true, id = entity.Id, name = entity.Name });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> AddStatusAjax([FromBody] MasterDataAjaxRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Name))
                return Json(new { success = false, message = "Name is required." });
            var trimmed = request.Name.Trim();
            var existing = await _context.Statuses.FirstOrDefaultAsync(x => x.Name == trimmed);
            if (existing != null)
                return Json(new { success = true, id = existing.Id, name = existing.Name });
            var entity = new Status { Name = trimmed };
            _context.Statuses.Add(entity);
            await _context.SaveChangesAsync();
            return Json(new { success = true, id = entity.Id, name = entity.Name });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireAdminOrAbove")]
        public async Task<IActionResult> AddFoundByNameAjax([FromBody] MasterDataAjaxRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Name))
                return Json(new { success = false, message = "Name is required." });
            var trimmed = request.Name.Trim();
            var existing = await _context.FoundByNames.FirstOrDefaultAsync(x => x.Name == trimmed);
            if (existing != null)
                return Json(new { success = true, id = existing.Id, name = existing.Name });
            var entity = new FoundByName { Name = trimmed };
            _context.FoundByNames.Add(entity);
            await _context.SaveChangesAsync();
            return Json(new { success = true, id = entity.Id, name = entity.Name });
        }
    }

    /// <summary>
    /// Simple request model for AJAX inline master data creation
    /// </summary>
    public class MasterDataAjaxRequest
    {
        public string Name { get; set; } = string.Empty;
    }
}

