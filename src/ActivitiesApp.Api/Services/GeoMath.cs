namespace ActivitiesApp.Api.Services;

public static class GeoMath
{
    private const double EarthRadiusMeters = 6_371_000d;

    public static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

        return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static (double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude) GetBoundingBox(
        double latitude,
        double longitude,
        double radiusMeters)
    {
        var latitudeDelta = radiusMeters / 111_320d;
        var longitudeDivisor = Math.Max(Math.Cos(ToRadians(latitude)), 0.01d);
        var longitudeDelta = radiusMeters / (111_320d * longitudeDivisor);

        return (
            latitude - latitudeDelta,
            latitude + latitudeDelta,
            longitude - longitudeDelta,
            longitude + longitudeDelta);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
