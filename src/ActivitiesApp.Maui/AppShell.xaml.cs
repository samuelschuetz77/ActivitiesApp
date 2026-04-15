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

        if (CurrentPage is HomePage homePage &&
            args.Source is ShellNavigationSource.ShellItemChanged
                or ShellNavigationSource.ShellSectionChanged
                or ShellNavigationSource.ShellContentChanged)
        {
            homePage.ResetToRoot();
        }
    }
}
