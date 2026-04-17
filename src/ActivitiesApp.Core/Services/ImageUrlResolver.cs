namespace ActivitiesApp.Shared.Services;

public static class ImageUrlResolver
{
    public static string? Resolve(string? imageUrl, string? apiBaseAddress)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        if (imageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            imageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return imageUrl;
        }

        if (string.IsNullOrWhiteSpace(apiBaseAddress) || !imageUrl.StartsWith("/"))
        {
            return imageUrl;
        }

        return apiBaseAddress.TrimEnd('/') + imageUrl;
    }
}
