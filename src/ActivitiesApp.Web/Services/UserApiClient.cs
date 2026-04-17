using System.Net.Http.Headers;
using System.Net.Http.Json;
using ActivitiesApp.Shared.Models;
using Microsoft.Identity.Web;

namespace ActivitiesApp.Web.Services;

public class UserApiClient : IUserProfileService
{
    private const string ApiScope = "api://6d3dc4ee-33ce-4656-95c8-702a38464687/access_as_user";

    private readonly HttpClient _http;
    private readonly ITokenAcquisition _tokenAcquisition;

    public UserApiClient(HttpClient http, ITokenAcquisition tokenAcquisition)
    {
        _http = http;
        _tokenAcquisition = tokenAcquisition;
    }

    public async Task<UserProfile?> GetMeAsync()
    {
        using var request = await AuthorizedRequest(HttpMethod.Get, "/api/auth/me");
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UserProfile>();
    }

    public async Task<UserProfile?> SaveSettingsAsync(string? profilePictureUrl)
    {
        using var request = await AuthorizedRequest(HttpMethod.Put, "/api/auth/me/settings");
        request.Content = JsonContent.Create(new { profilePictureUrl });
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UserProfile>();
    }

    public async Task<List<Activity>> GetMyActivitiesAsync()
    {
        using var request = await AuthorizedRequest(HttpMethod.Get, "/api/auth/my-activities");
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return [];
        return await response.Content.ReadFromJsonAsync<List<Activity>>() ?? [];
    }

    private async Task<HttpRequestMessage> AuthorizedRequest(HttpMethod method, string url)
    {
        var token = await _tokenAcquisition.GetAccessTokenForUserAsync([ApiScope]);
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
