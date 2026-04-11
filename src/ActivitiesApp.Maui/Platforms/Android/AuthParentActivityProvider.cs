using Android.App;

namespace ActivitiesApp.Platforms.Android;

internal static class AuthParentActivityProvider
{
    public static Activity? CurrentActivity { get; private set; }

    public static void SetCurrentActivity(Activity activity)
    {
        CurrentActivity = activity;
    }
}
