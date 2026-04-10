using ActivitiesApp.Services;

namespace ActivitiesApp.Pages;

public partial class ProfilePage : ContentPage
{
    private readonly AuthService _authService;

    public ProfilePage(AuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        WelcomeLabel.Text = $"Hello, you are logged in as {_authService.UserEmail}";
    }

    private async void OnSignOutClicked(object sender, EventArgs e)
    {
        await _authService.SignOutAsync();
        Application.Current!.Windows[0].Page = new LoginPage(_authService);
    }
}
