using System.ComponentModel.DataAnnotations;

namespace LostAndFoundApp.Models
{
    /// <summary>
    /// Master data table for types of lost/found items
    /// </summary>
    public class Item
    {
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Master data table for route numbers
    /// </summary>
    public class Route
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Master data table for vehicle numbers
    /// </summary>
    public class Vehicle
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Master data table for storage locations
    /// </summary>
    public class StorageLocation
    {
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Master data table for item status values (e.g. Found, Claimed, Disposed)
    /// </summary>
    public class Status
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Master data table for names of people who found items
    /// </summary>
    public class FoundByName
    {
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}
