using ActivitiesApp.Services;

namespace ActivitiesApp.Pages;

public partial class LoginPage : ContentPage
{
    private readonly AuthService _authService;

    public LoginPage(AuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        try
        {
            var success = await _authService.SignInAsync();
            if (success)
            {
                Application.Current!.Windows[0].Page = new AppShell();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Sign In Failed", ex.Message, "OK");
        }
    }
}
