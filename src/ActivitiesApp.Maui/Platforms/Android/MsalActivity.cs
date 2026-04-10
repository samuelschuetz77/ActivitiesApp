using Android.App;
using Android.Content;
using Microsoft.Identity.Client;

namespace ActivitiesApp.Platforms.Android;

[Activity(Exported = true)]
[IntentFilter([Intent.ActionView],
    Categories = [Intent.CategoryBrowsable, Intent.CategoryDefault],
    DataScheme = "msal6d3dc4ee-33ce-4656-95c8-702a38464687",
    DataHost = "auth")]
public class MsalActivity : BrowserTabActivity
{
}
