namespace ActivitiesApp.Core.Filters;

public record ActivityFilterCriteria
{
    public string? Cost { get; init; }
    public string? Category { get; init; }
    public string? AgeRange { get; init; }
    public int RadiusMiles { get; init; }
    public bool HasActiveLocation { get; init; }
    public double ActiveLatitude { get; init; }
    public double ActiveLongitude { get; init; }
}
