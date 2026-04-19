using ActivitiesApp.Shared.Models;

namespace ActivitiesApp.ViewModels.Tests;

internal sealed class FakeUserProfileService : IUserProfileService
{
    public UserProfile? ProfileToReturn { get; set; }
    public UserProfile? SaveResult { get; set; }
    public List<Activity> ActivitiesToReturn { get; set; } = [];

    public bool GetMeThrows { get; set; }
    public bool SaveThrows { get; set; }
    public bool GetActivitiesThrows { get; set; }

    public int GetMyActivitiesCallCount { get; private set; }

    public Task<UserProfile?> GetMeAsync()
    {
        if (GetMeThrows) throw new HttpRequestException("offline");
        return Task.FromResult(ProfileToReturn);
    }

    public Task<UserProfile?> SaveSettingsAsync(string? profilePictureUrl)
    {
        if (SaveThrows) throw new HttpRequestException("server error");
        return Task.FromResult(SaveResult);
    }

    public Task<List<Activity>> GetMyActivitiesAsync()
    {
        GetMyActivitiesCallCount++;
        if (GetActivitiesThrows) throw new HttpRequestException("offline");
        return Task.FromResult(ActivitiesToReturn);
    }
}
