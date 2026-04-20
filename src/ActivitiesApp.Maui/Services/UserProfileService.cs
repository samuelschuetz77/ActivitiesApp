using ActivitiesApp.Shared.Models;
using System.Net.Http.Json;

namespace ActivitiesApp.Services;

public class UserProfileService : IUserProfileService
{
    private readonly HttpClient _http;

    public UserProfileService(HttpClient http) => _http = http;

    public Task<UserProfile?> GetMeAsync() =>
        _http.GetFromJsonAsync<UserProfile>("/api/auth/me");

    public async Task<UserProfile?> SaveSettingsAsync(string? profilePictureUrl)
    {
        var response = await _http.PutAsJsonAsync("/api/auth/me/settings", new { profilePictureUrl });
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UserProfile>();
    }

    public async Task<List<Activity>> GetMyActivitiesAsync() =>
        await _http.GetFromJsonAsync<List<Activity>>("/api/auth/my-activities") ?? [];
}
