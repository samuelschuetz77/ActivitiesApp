using ActivitiesApp.Shared.Services;
using Microsoft.JSInterop;

namespace ActivitiesApp.Web.Services;

public class JsLocationProvider(IJSRuntime js) : ILocationProvider
{
    public async Task<(double Latitude, double Longitude)> GetLocationAsync()
    {
        var coords = await js.InvokeAsync<double[]>("getUserLocation");
        if (coords.Length < 2)
            throw new InvalidOperationException("Browser location response was missing coordinates.");

        return (coords[0], coords[1]);
    }
}
