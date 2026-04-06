namespace ActivitiesApp.Shared.Services;

public static class ImageUrlResolver
{
    public static string? Resolve(string? imageUrl, string? apiBaseAddress)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
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
