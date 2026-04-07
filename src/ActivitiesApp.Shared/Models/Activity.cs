using System;

using System.ComponentModel.DataAnnotations;

namespace ActivitiesApp.Shared.Models
{
    public class Activity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        [Required(ErrorMessage = "City is required.")]
        public string City { get; set; } = "";

        [Required(ErrorMessage = "Name is required.")]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Description is required.")]
        public string Description { get; set; } = "";

        [Range(0, double.MaxValue, ErrorMessage = "Cost must be zero or greater.")]
        public double Cost { get; set; }
        public DateTime Activitytime { get; set; }
        [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90.")]
        public double Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180.")]
        public double Longitude { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Minimum age must be zero or greater.")]
        public int MinAge { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Maximum age must be zero or greater.")]
        public int MaxAge { get; set; }

        [Required(ErrorMessage = "Category is required.")]
        public string? Category { get; set; }
        public string? ImageUrl { get; set; }
        public string? PlaceId { get; set; }
        public double Rating { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }
        public string? CreatedByUserId { get; set; }
    }
}
