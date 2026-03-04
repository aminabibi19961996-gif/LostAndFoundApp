using LostAndFoundApp.Models;
using LostAndFoundApp.Data;
using Microsoft.EntityFrameworkCore;

namespace LostAndFoundApp.Services
{
    /// <summary>
    /// Compares a before/after LostFoundItem snapshot and returns a
    /// human-readable diff string suitable for storage in ActivityLog.Details.
    ///
    /// Format stored in Details:
    ///   [Summary line]
    ///   CHANGES:
    ///   - Field Name: "Old Value" -> "New Value"
    ///   - Field Name: "Old Value" -> "New Value"
    ///
    /// The CHANGES: sentinel lets the Logs view detect and render diffs specially.
    /// </summary>
    public class ItemDiffService
    {
        private readonly ApplicationDbContext _context;

        public ItemDiffService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Build a diff string by comparing a before-snapshot (loaded before changes)
        /// with the after-state of the same entity.
        /// Navigation property names (Item, Status, etc.) are resolved to display names.
        /// </summary>
        public async Task<string> BuildEditDiffAsync(
            LostFoundItem before,
            LostFoundItem after,
            string summaryLine)
        {
            var changes = new List<string>();

            // --- Scalar field comparisons ---
            CompareField(changes, "Date Found",
                before.DateFound.ToString("MM/dd/yyyy"),
                after.DateFound.ToString("MM/dd/yyyy"));

            CompareField(changes, "Description",
                before.Description,
                after.Description);

            CompareField(changes, "Location Found",
                before.LocationFound,
                after.LocationFound);

            CompareField(changes, "Notes",
                before.Notes,
                after.Notes);

            CompareField(changes, "Claimed By",
                before.ClaimedBy,
                after.ClaimedBy);

            // --- FK fields — resolve display names ---
            if (before.ItemId != after.ItemId)
            {
                var oldName = await _context.Items
                    .Where(x => x.Id == before.ItemId)
                    .Select(x => x.Name).FirstOrDefaultAsync() ?? before.ItemId.ToString();
                var newName = await _context.Items
                    .Where(x => x.Id == after.ItemId)
                    .Select(x => x.Name).FirstOrDefaultAsync() ?? after.ItemId.ToString();
                changes.Add($"- Item: \"{oldName}\" -> \"{newName}\"");
            }

            if (before.StatusId != after.StatusId)
            {
                var oldName = await _context.Statuses
                    .Where(x => x.Id == before.StatusId)
                    .Select(x => x.Name).FirstOrDefaultAsync() ?? before.StatusId.ToString();
                var newName = await _context.Statuses
                    .Where(x => x.Id == after.StatusId)
                    .Select(x => x.Name).FirstOrDefaultAsync() ?? after.StatusId.ToString();
                changes.Add($"- Status: \"{oldName}\" -> \"{newName}\"");
            }

            if (before.StatusDate != after.StatusDate)
            {
                var oldVal = before.StatusDate?.ToString("MM/dd/yyyy") ?? "(none)";
                var newVal = after.StatusDate?.ToString("MM/dd/yyyy") ?? "(none)";
                changes.Add($"- Status Date: \"{oldVal}\" -> \"{newVal}\"");
            }

            if (before.RouteId != after.RouteId)
            {
                var oldName = before.RouteId.HasValue
                    ? await _context.Routes.Where(x => x.Id == before.RouteId).Select(x => x.Name).FirstOrDefaultAsync() ?? before.RouteId.ToString()
                    : "(none)";
                var newName = after.RouteId.HasValue
                    ? await _context.Routes.Where(x => x.Id == after.RouteId).Select(x => x.Name).FirstOrDefaultAsync() ?? after.RouteId.ToString()
                    : "(none)";
                changes.Add($"- Route #: \"{oldName}\" -> \"{newName}\"");
            }

            if (before.VehicleId != after.VehicleId)
            {
                var oldName = before.VehicleId.HasValue
                    ? await _context.Vehicles.Where(x => x.Id == before.VehicleId).Select(x => x.Name).FirstOrDefaultAsync() ?? before.VehicleId.ToString()
                    : "(none)";
                var newName = after.VehicleId.HasValue
                    ? await _context.Vehicles.Where(x => x.Id == after.VehicleId).Select(x => x.Name).FirstOrDefaultAsync() ?? after.VehicleId.ToString()
                    : "(none)";
                changes.Add($"- Vehicle #: \"{oldName}\" -> \"{newName}\"");
            }

            if (before.StorageLocationId != after.StorageLocationId)
            {
                var oldName = before.StorageLocationId.HasValue
                    ? await _context.StorageLocations.Where(x => x.Id == before.StorageLocationId).Select(x => x.Name).FirstOrDefaultAsync() ?? before.StorageLocationId.ToString()
                    : "(none)";
                var newName = after.StorageLocationId.HasValue
                    ? await _context.StorageLocations.Where(x => x.Id == after.StorageLocationId).Select(x => x.Name).FirstOrDefaultAsync() ?? after.StorageLocationId.ToString()
                    : "(none)";
                changes.Add($"- Storage Location: \"{oldName}\" -> \"{newName}\"");
            }

            if (before.FoundById != after.FoundById)
            {
                var oldName = before.FoundById.HasValue
                    ? await _context.FoundByNames.Where(x => x.Id == before.FoundById).Select(x => x.Name).FirstOrDefaultAsync() ?? before.FoundById.ToString()
                    : "(none)";
                var newName = after.FoundById.HasValue
                    ? await _context.FoundByNames.Where(x => x.Id == after.FoundById).Select(x => x.Name).FirstOrDefaultAsync() ?? after.FoundById.ToString()
                    : "(none)";
                changes.Add($"- Found By: \"{oldName}\" -> \"{newName}\"");
            }

            // --- Photo changes ---
            ComparePhotoField(changes, "Photo 1", before.PhotoPath, after.PhotoPath);
            ComparePhotoField(changes, "Photo 2", before.PhotoPath2, after.PhotoPath2);
            ComparePhotoField(changes, "Photo 3", before.PhotoPath3, after.PhotoPath3);
            ComparePhotoField(changes, "Photo 4", before.PhotoPath4, after.PhotoPath4);
            ComparePhotoField(changes, "Attachment", before.AttachmentPath, after.AttachmentPath);

            // Build the final string
            if (changes.Count == 0)
            {
                return $"{summaryLine} (No field changes detected)";
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(summaryLine);
            sb.AppendLine("CHANGES:");
            foreach (var c in changes)
                sb.AppendLine(c);

            var result = sb.ToString().TrimEnd();
            // Truncate to fit the DB column (2000 chars)
            return result.Length > 1990 ? result[..1990] + "..." : result;
        }

        private static void CompareField(List<string> changes, string label, string? before, string? after)
        {
            var b = (before ?? "").Trim();
            var a = (after ?? "").Trim();
            if (b == a) return;

            // Truncate long values for readability in the log
            if (b.Length > 80) b = b[..77] + "...";
            if (a.Length > 80) a = a[..77] + "...";
            changes.Add($"- {label}: \"{b}\" -> \"{a}\"");
        }

        private static void ComparePhotoField(List<string> changes, string label, string? before, string? after)
        {
            var hadBefore = !string.IsNullOrEmpty(before);
            var hasAfter = !string.IsNullOrEmpty(after);

            if (hadBefore == hasAfter) return; // No change

            if (!hadBefore && hasAfter)
                changes.Add($"- {label}: (none) -> [file uploaded]");
            else if (hadBefore && !hasAfter)
                changes.Add($"- {label}: [file removed]");
        }
    }
}
