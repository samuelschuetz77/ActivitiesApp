using Microsoft.AspNetCore.Identity;

namespace ActivitiesApp.Infrastructure.Models;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public string? ProfilePictureUrl { get; set; }
}
