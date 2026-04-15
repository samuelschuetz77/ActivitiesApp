using ActivitiesApp.Shared.Services;
using Microsoft.JSInterop;

namespace ActivitiesApp.Web.Services;

public class JsLocationProvider(IJSRuntime js) : ILocationProvider
{
    public async Task<LocationFetchResult> GetLocationAsync(CancellationToken cancellationToken = default)
    {
        var result = await js.InvokeAsync<BrowserLocationResult>("activitiesLocation.getCurrentLocation", cancellationToken);
        if (result is null)
        {
            return LocationFetchResult.Failure(
                "browser_location_missing",
                "Browser location response was empty.",
                "browser");
        }

        if (!result.Success)
        {
            return LocationFetchResult.Failure(
                result.ErrorCode ?? "browser_location_failed",
                result.Message ?? "Browser could not fetch your current location.",
                "browser",
                result.PermissionState,
                result.Diagnostics);
        }

        return LocationFetchResult.Success(
            result.Latitude,
            result.Longitude,
            "browser",
            result.PermissionState);
    }

    private sealed class BrowserLocationResult
    {
        public bool Success { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? PermissionState { get; set; }
        public string? ErrorCode { get; set; }
        public string? Message { get; set; }
        public string? Diagnostics { get; set; }
    }
}
