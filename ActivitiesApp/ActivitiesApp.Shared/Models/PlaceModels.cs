namespace ActivitiesApp.Shared.Models;

public class NearbyPlace
{
    public string PlaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Vicinity { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Rating { get; set; }
    public int UserRatingsTotal { get; set; }
    public string PhotoUrl { get; set; } = "";
    public bool IsOpenNow { get; set; }
    public List<string> Types { get; set; } = [];
    public int PriceLevel { get; set; }
}

public class PlaceDetails
{
    public string PlaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string FormattedAddress { get; set; } = "";
    public string FormattedPhone { get; set; } = "";
    public string Website { get; set; } = "";
    public double Rating { get; set; }
    public int UserRatingsTotal { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public List<string> PhotoUrls { get; set; } = [];
    public List<PlaceReviewData> Reviews { get; set; } = [];
    public string OpeningHoursSummary { get; set; } = "";
    public int PriceLevel { get; set; }
    public List<string> Types { get; set; } = [];
}

public class PlaceReviewData
{
    public string AuthorName { get; set; } = "";
    public double Rating { get; set; }
    public string Text { get; set; } = "";
    public string RelativeTime { get; set; } = "";
}
