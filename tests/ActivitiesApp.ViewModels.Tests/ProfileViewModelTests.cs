using ActivitiesApp.Shared.Models;

namespace ActivitiesApp.ViewModels.Tests;

public class ProfileViewModelTests
{
    private static ProfileViewModel Create(FakeUserProfileService? svc = null) =>
        new(svc ?? new FakeUserProfileService());

    // ── LoadAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_SetsProfilePictureUrl_WhenServiceReturnsProfile()
    {
        var svc = new FakeUserProfileService
        {
            ProfileToReturn = new UserProfile("u1", "a@b.com", "Alice", "https://cdn/pic.jpg")
        };
        var vm = Create(svc);

        await vm.LoadAsync();

        Assert.Equal("https://cdn/pic.jpg", vm.ProfilePictureUrl);
    }

    [Fact]
    public async Task LoadAsync_LeavesProfilePictureUrlNull_WhenServiceReturnsNull()
    {
        var svc = new FakeUserProfileService { ProfileToReturn = null };
        var vm = Create(svc);

        await vm.LoadAsync();

        Assert.Null(vm.ProfilePictureUrl);
    }

    [Fact]
    public async Task LoadAsync_DoesNotThrow_WhenServiceThrows()
    {
        var svc = new FakeUserProfileService { GetMeThrows = true };
        var vm = Create(svc);

        var ex = await Record.ExceptionAsync(() => vm.LoadAsync());

        Assert.Null(ex);
        Assert.Null(vm.ProfilePictureUrl);
    }

    // ── SetPendingPhoto ────────────────────────────────────────────────────

    [Fact]
    public void SetPendingPhoto_SetsHasPendingPhotoTrue()
    {
        var vm = Create();

        vm.SetPendingPhoto("data:image/png;base64,abc");

        Assert.True(vm.HasPendingPhoto);
    }

    // ── SaveProfilePictureCommand ──────────────────────────────────────────

    [Fact]
    public async Task SaveProfilePicture_DoesNothing_WhenNoPendingPhoto()
    {
        var svc = new FakeUserProfileService();
        var vm = Create(svc);

        await vm.SaveProfilePictureCommand.ExecuteAsync(null);

        Assert.False(vm.HasSaveMessage);
        Assert.Null(vm.SaveStatusMessage);
    }

    [Fact]
    public async Task SaveProfilePicture_OnSuccess_UpdatesUrlAndClearsPending()
    {
        var svc = new FakeUserProfileService
        {
            SaveResult = new UserProfile("u1", "a@b.com", "Alice", "https://cdn/new.jpg")
        };
        var vm = Create(svc);
        vm.SetPendingPhoto("data:image/png;base64,NEW");

        await vm.SaveProfilePictureCommand.ExecuteAsync(null);

        Assert.Equal("data:image/png;base64,NEW", vm.ProfilePictureUrl);
        Assert.False(vm.HasPendingPhoto);
        Assert.Equal("Saved.", vm.SaveStatusMessage);
        Assert.True(vm.HasSaveMessage);
    }

    [Fact]
    public async Task SaveProfilePicture_OnServiceReturnsNull_SetsFailedMessage()
    {
        var svc = new FakeUserProfileService { SaveResult = null };
        var vm = Create(svc);
        vm.SetPendingPhoto("data:image/png;base64,NEW");

        await vm.SaveProfilePictureCommand.ExecuteAsync(null);

        Assert.Null(vm.ProfilePictureUrl);
        Assert.True(vm.HasPendingPhoto);
        Assert.Equal("Failed to save.", vm.SaveStatusMessage);
        Assert.True(vm.HasSaveMessage);
    }

    [Fact]
    public async Task SaveProfilePicture_OnServiceThrows_SetsErrorMessage()
    {
        var svc = new FakeUserProfileService { SaveThrows = true };
        var vm = Create(svc);
        vm.SetPendingPhoto("data:image/png;base64,NEW");

        await vm.SaveProfilePictureCommand.ExecuteAsync(null);

        Assert.Null(vm.ProfilePictureUrl);
        Assert.Equal("Error saving settings.", vm.SaveStatusMessage);
        Assert.True(vm.HasSaveMessage);
    }

    // ── ToggleMyActivitiesCommand ──────────────────────────────────────────

    [Fact]
    public async Task ToggleMyActivities_TogglesVisibility()
    {
        var vm = Create();
        Assert.False(vm.MyActivitiesVisible);

        await vm.ToggleMyActivitiesCommand.ExecuteAsync(null);
        Assert.True(vm.MyActivitiesVisible);

        await vm.ToggleMyActivitiesCommand.ExecuteAsync(null);
        Assert.False(vm.MyActivitiesVisible);
    }

    [Fact]
    public async Task ToggleMyActivities_LoadsActivities_OnFirstToggleOn()
    {
        var svc = new FakeUserProfileService
        {
            ActivitiesToReturn =
            [
                new Activity { Name = "Hike", Category = "Outdoors", City = "Boise" }
            ]
        };
        var vm = Create(svc);

        await vm.ToggleMyActivitiesCommand.ExecuteAsync(null);

        Assert.Single(vm.MyActivities);
        Assert.Equal("Hike", vm.MyActivities[0].Name);
        Assert.Equal(1, svc.GetMyActivitiesCallCount);
    }

    [Fact]
    public async Task ToggleMyActivities_DoesNotReload_WhenToggledOffThenOn()
    {
        var svc = new FakeUserProfileService
        {
            ActivitiesToReturn = [new Activity { Name = "Hike", Category = "Outdoors", City = "Boise" }]
        };
        var vm = Create(svc);

        await vm.ToggleMyActivitiesCommand.ExecuteAsync(null); // on  → loads
        await vm.ToggleMyActivitiesCommand.ExecuteAsync(null); // off → no load
        await vm.ToggleMyActivitiesCommand.ExecuteAsync(null); // on  → already has items, no reload

        Assert.Equal(1, svc.GetMyActivitiesCallCount);
    }

    [Fact]
    public async Task ToggleMyActivities_DoesNotThrow_WhenServiceThrows()
    {
        var svc = new FakeUserProfileService { GetActivitiesThrows = true };
        var vm = Create(svc);

        var ex = await Record.ExceptionAsync(() => vm.ToggleMyActivitiesCommand.ExecuteAsync(null));

        Assert.Null(ex);
        Assert.Empty(vm.MyActivities);
    }
}
