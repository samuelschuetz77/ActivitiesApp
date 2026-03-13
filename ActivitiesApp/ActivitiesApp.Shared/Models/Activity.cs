using System;

namespace ActivitiesApp.Shared.Models
{
    public class Activity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string City { get; set; } 
        public string Name { get; set; }
        public string Description { get; set; }
        public double Cost { get; set; }
        public DateTime Activitytime { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int MinAge { get; set; }
        public int MaxAge { get; set; }    
    }
}
