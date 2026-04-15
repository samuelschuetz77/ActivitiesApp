namespace ActivitiesApp.Core.Helpers;

public static class AddressBuilder
{
    public static string BuildFullAddress(string? street, string? apt, string? city, string? state, string? zip)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(street)) parts.Add(street.Trim());
        if (!string.IsNullOrWhiteSpace(apt))    parts.Add(apt.Trim());
        if (!string.IsNullOrWhiteSpace(city))   parts.Add(city.Trim());
        if (!string.IsNullOrWhiteSpace(state))  parts.Add(state.Trim());
        if (!string.IsNullOrWhiteSpace(zip))    parts.Add(zip.Trim());
        return string.Join(", ", parts);
    }
}
