using ActivitiesApp.Shared.Models;

public interface IUserProfileService
{
    Task<UserProfile?> GetMeAsync();
    Task<UserProfile?> SaveSettingsAsync(string? profilePictureUrl);
    Task<List<Activity>> GetMyActivitiesAsync();
}

public record UserProfile(string UserId, string Email, string DisplayName, string? ProfilePictureUrl);
