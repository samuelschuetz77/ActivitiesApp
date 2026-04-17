using ActivitiesApp.Services;
using ActivitiesApp.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Net.Http.Json;

namespace ActivitiesApp.ViewModels;

public partial class ProfileViewModel : BaseViewModel
{
    private readonly AuthService _authService;
    private readonly HttpClient _http;

    [ObservableProperty] private string? _profilePictureUrl;
    [ObservableProperty] private string? _saveStatusMessage;
    [ObservableProperty] private bool _hasSaveMessage;
    [ObservableProperty] private bool _myActivitiesVisible;
    [ObservableProperty] private bool _hasPendingPhoto;

    private string? _pendingPhotoDataUrl;

    public ObservableCollection<Activity> MyActivities { get; } = [];

    public ProfileViewModel(AuthService authService, HttpClient http)
    {
        Title = "Profile";
        _authService = authService;
        _http = http;
    }

    public async Task LoadAsync()
    {
        try
        {
            var me = await _http.GetFromJsonAsync<UserProfile>("/api/auth/me");
            ProfilePictureUrl = me?.ProfilePictureUrl;
        }
        catch { /* not signed in or offline */ }
    }

    public void SetPendingPhoto(string dataUrl)
    {
        _pendingPhotoDataUrl = dataUrl;
        HasPendingPhoto = true;
    }

    [RelayCommand]
    private async Task SaveProfilePictureAsync()
    {
        if (_pendingPhotoDataUrl is null) return;
        SaveStatusMessage = null;
        try
        {
            var response = await _http.PutAsJsonAsync("/api/auth/me/settings", new { profilePictureUrl = _pendingPhotoDataUrl });
            if (response.IsSuccessStatusCode)
            {
                ProfilePictureUrl = _pendingPhotoDataUrl;
                _pendingPhotoDataUrl = null;
                HasPendingPhoto = false;
                SaveStatusMessage = "Saved.";
            }
            else
            {
                SaveStatusMessage = "Failed to save.";
            }
        }
        catch
        {
            SaveStatusMessage = "Error saving settings.";
        }
        HasSaveMessage = true;
    }

    [RelayCommand]
    private async Task ToggleMyActivitiesAsync()
    {
        MyActivitiesVisible = !MyActivitiesVisible;
        if (MyActivitiesVisible && MyActivities.Count == 0)
            await LoadMyActivitiesAsync();
    }

    private async Task LoadMyActivitiesAsync()
    {
        try
        {
            var activities = await _http.GetFromJsonAsync<List<Activity>>("/api/auth/my-activities") ?? [];
            MyActivities.Clear();
            foreach (var a in activities)
                MyActivities.Add(a);
        }
        catch { /* offline */ }
    }

}
