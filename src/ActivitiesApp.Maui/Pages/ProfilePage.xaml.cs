using ActivitiesApp.Services;
using ActivitiesApp.ViewModels;

namespace ActivitiesApp.Pages;

public partial class ProfilePage : ContentPage
{
    private readonly AuthService _authService;
    private readonly ProfileViewModel _viewModel;

    public ProfilePage(AuthService authService, ProfileViewModel viewModel)
    {
        InitializeComponent();
        _authService = authService;
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        WelcomeLabel.Text = $"Hello, {_authService.UserName}";

        await _viewModel.LoadAsync();
        SetProfileImageSource(_viewModel.ProfilePictureUrl);
    }

    private void SetProfileImageSource(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (url.StartsWith("data:"))
        {
            var comma = url.IndexOf(',');
            if (comma >= 0)
            {
                var bytes = Convert.FromBase64String(url[(comma + 1)..]);
                ProfileImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            }
        }
        else
        {
            ProfileImage.Source = ImageSource.FromUri(new Uri(url));
        }
    }

    private async void OnChoosePhotoClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await MediaPicker.PickPhotoAsync();
            if (result is null) return;
            using var stream = await result.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());
            var contentType = result.ContentType ?? "image/jpeg";
            var dataUrl = $"data:{contentType};base64,{base64}";
            _viewModel.SetPendingPhoto(dataUrl);
            var bytes = ms.ToArray();
            ProfileImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch { /* user cancelled or permission denied */ }
    }

    private async void OnSignOutClicked(object sender, EventArgs e)
    {
        await _authService.SignOutAsync();
        Application.Current!.Windows[0].Page = new LoginPage(_authService);
    }
}
