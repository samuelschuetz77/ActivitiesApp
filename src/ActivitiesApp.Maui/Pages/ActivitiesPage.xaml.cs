using ActivitiesApp.ViewModels;

namespace ActivitiesApp.Pages;

public partial class ActivitiesPage : ContentPage
{
    public ActivitiesPage(ActivitiesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
