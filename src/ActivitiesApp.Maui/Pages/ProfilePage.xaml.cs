using ActivitiesApp.ViewModels;

namespace ActivitiesApp.Pages;

public partial class ProfilePage : ContentPage
{
    public ProfilePage(ProfileViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
