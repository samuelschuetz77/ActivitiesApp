using ActivitiesApp.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace ActivitiesApp.ViewModels;

public partial class ProfileViewModel : BaseViewModel
{
    private readonly IUserProfileService _profileService;

    [ObservableProperty] private string? _profilePictureUrl;
    [ObservableProperty] private string? _saveStatusMessage;
    [ObservableProperty] private bool _hasSaveMessage;
    [ObservableProperty] private bool _myActivitiesVisible;
    [ObservableProperty] private bool _hasPendingPhoto;

    private string? _pendingPhotoDataUrl;

    public ObservableCollection<Activity> MyActivities { get; } = [];

    public ProfileViewModel(IUserProfileService profileService)
    {
        Title = "Profile";
        _profileService = profileService;
    }

    public async Task LoadAsync()
    {
        try
        {
            var me = await _profileService.GetMeAsync();
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
            var result = await _profileService.SaveSettingsAsync(_pendingPhotoDataUrl);
            if (result is not null)
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
            var activities = await _profileService.GetMyActivitiesAsync();
            MyActivities.Clear();
            foreach (var a in activities)
                MyActivities.Add(a);
        }
        catch { /* offline */ }
    }
}
