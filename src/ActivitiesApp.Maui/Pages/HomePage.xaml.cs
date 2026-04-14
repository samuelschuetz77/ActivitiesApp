using ActivitiesApp.ViewModels;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActivitiesApp.Pages;

public partial class HomePage : ContentPage
{
    private const string HostPage = "wwwroot/index.html";
    private const string HomeRoute = "/";
    private const string RootComponentSelector = "#app";

    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    public void ResetToRoot()
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(ResetToRoot);
            return;
        }

        var replacement = CreateBlazorWebView();

        BlazorHost.Children.Clear();
        BlazorHost.Children.Add(replacement);
        HomeBlazorWebView = replacement;
    }

    private static BlazorWebView CreateBlazorWebView()
    {
        var view = new BlazorWebView
        {
            HostPage = HostPage,
            StartPath = HomeRoute
        };

        view.RootComponents.Add(new RootComponent
        {
            Selector = RootComponentSelector,
            ComponentType = typeof(Shared.Routes)
        });

        return view;
    }
}
