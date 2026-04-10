using Android.App;
using Android.Content;
using Microsoft.Identity.Client;

namespace ActivitiesApp.Platforms.Android;

[Activity(Exported = true)]
[IntentFilter([Intent.ActionView],
    Categories = [Intent.CategoryBrowsable, Intent.CategoryDefault],
    DataScheme = "msalYOUR_CLIENT_ID_HERE",
    DataHost = "auth")]
public class MsalActivity : BrowserTabActivity
{
}
