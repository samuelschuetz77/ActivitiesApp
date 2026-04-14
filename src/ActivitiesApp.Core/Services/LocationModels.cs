using Microsoft.Extensions.Logging;

namespace ActivitiesApp.Shared.Services;

public sealed class LocationFetchResult
{
    public bool IsSuccess { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string Source { get; init; } = "unknown";
    public string? PermissionState { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Diagnostics { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public static LocationFetchResult Success(
        double latitude,
        double longitude,
        string source,
        string? permissionState = null)
    {
        return new LocationFetchResult
        {
            IsSuccess = true,
            Latitude = latitude,
            Longitude = longitude,
            Source = source,
            PermissionState = permissionState
        };
    }

    public static LocationFetchResult Failure(
        string errorCode,
        string errorMessage,
        string source,
        string? permissionState = null,
        string? diagnostics = null)
    {
        return new LocationFetchResult
        {
            IsSuccess = false,
            Source = source,
            PermissionState = permissionState,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Diagnostics = diagnostics
        };
    }
}

public sealed class LocationLogEntry
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public LogLevel Level { get; init; }
    public string Message { get; init; } = "";
    public string? ErrorCode { get; init; }
    public string? Details { get; init; }
}
