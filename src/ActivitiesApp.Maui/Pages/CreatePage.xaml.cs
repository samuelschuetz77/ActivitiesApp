using ActivitiesApp.ViewModels;

namespace ActivitiesApp.Pages;

public partial class CreatePage : ContentPage
{
    public CreatePage(CreateViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
