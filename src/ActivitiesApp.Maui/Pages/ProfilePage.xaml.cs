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

        if (!string.IsNullOrWhiteSpace(_viewModel.ProfilePictureUrl))
            ProfileImage.Source = ImageSource.FromUri(new Uri(_viewModel.ProfilePictureUrl));

        await _viewModel.LoadAsync();

        if (!string.IsNullOrWhiteSpace(_viewModel.ProfilePictureUrl))
            ProfileImage.Source = ImageSource.FromUri(new Uri(_viewModel.ProfilePictureUrl));
    }

    private async void OnSignOutClicked(object sender, EventArgs e)
    {
        await _authService.SignOutAsync();
        Application.Current!.Windows[0].Page = new LoginPage(_authService);
    }
}
