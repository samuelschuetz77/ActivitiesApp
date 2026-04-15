using ActivitiesApp.Shared.Services;

namespace ActivitiesApp.Core.Tests;

public class GeoMathTests
{
    [Fact]
    public void HaversineMiles_ReturnsExpectedDistanceForKnownRoute()
    {
        var miles = GeoMath.HaversineMiles(39.7392, -104.9903, 39.8561, -104.6737);

        Assert.InRange(miles, 18.5, 18.8);
    }

    [Fact]
    public void BoundingBox_ContainsOriginalPoint()
    {
        var bounds = GeoMath.GetBoundingBox(39.7392, -104.9903, 8046.72);

        Assert.InRange(39.7392, bounds.MinLatitude, bounds.MaxLatitude);
        Assert.InRange(-104.9903, bounds.MinLongitude, bounds.MaxLongitude);
    }
}
