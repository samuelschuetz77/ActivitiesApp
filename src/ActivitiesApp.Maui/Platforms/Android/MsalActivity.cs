using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Identity.Client;

namespace ActivitiesApp.Platforms.Android;

[Activity(
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    NoHistory = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
[IntentFilter([Intent.ActionView],
    Categories = [Intent.CategoryBrowsable, Intent.CategoryDefault],
    DataScheme = "msal6d3dc4ee-33ce-4656-95c8-702a38464687",
    DataHost = "auth")]
public class MsalActivity : BrowserTabActivity
{
}
