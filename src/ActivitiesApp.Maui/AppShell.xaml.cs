using ActivitiesApp.Pages;

namespace ActivitiesApp;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);

        // Reset Home tab's BlazorWebView to "/" whenever Shell navigates
        // to it, regardless of source. Covers tab switches, pop-to-root,
        // and back-navigation that lands on the Home tab.
        if (CurrentPage is HomePage homePage)
        {
            homePage.ResetToRoot();
        }
    }
}
