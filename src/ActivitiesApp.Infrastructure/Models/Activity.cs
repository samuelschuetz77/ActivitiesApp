using System;

namespace ActivitiesApp.Infrastructure.Models
{
    public class Activity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string City { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public double Cost { get; set; }
        public DateTime Activitytime { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int MinAge { get; set; }
        public int MaxAge { get; set; }
        public string? Category { get; set; }
        public string? ImageUrl { get; set; }
        public string? PlaceId { get; set; }
        public double Rating { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }
        public string? CreatedByUserId { get; set; }
        public string? CreatedByDisplayName { get; set; }
        public string? CreatedByProfilePictureUrl { get; set; }
    }
}
