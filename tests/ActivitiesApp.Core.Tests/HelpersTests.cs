using ActivitiesApp.Core.Helpers;
using ActivitiesApp.Shared.Models;
using ActivitiesApp.Shared.Services;

namespace ActivitiesApp.Core.Tests;

public class ActivityFormatterTests
{
    [Fact]
    public void FormatCost_Zero_ReturnsFree()
    {
        Assert.Equal("Free", ActivityFormatter.FormatCost(0));
    }

    [Fact]
    public void FormatCost_PositiveWhole_ReturnsDollarFormat()
    {
        Assert.Equal("$10.00", ActivityFormatter.FormatCost(10));
    }

    [Fact]
    public void FormatCost_PositiveDecimal_ReturnsTwoDecimalPlaces()
    {
        Assert.Equal("$4.99", ActivityFormatter.FormatCost(4.99));
    }
}

public class ZipCodeValidatorTests
{
    [Theory]
    [InlineData("12345", true)]
    [InlineData("00000", true)]
    [InlineData("99999", true)]
    [InlineData("1234",  false)]   // too short
    [InlineData("123456", false)]  // too long
    [InlineData("1234a", false)]   // non-digit
    [InlineData("abcde", false)]   // all letters
    [InlineData("",      false)]   // empty
    [InlineData(null,    false)]   // null
    public void IsValid_VariousInputs(string? input, bool expected)
    {
        Assert.Equal(expected, ZipCodeValidator.IsValid(input));
    }
}

public class CategoryHelperTests
{
    [Fact]
    public void GetTagList_NullInput_ReturnsEmpty()
    {
        Assert.Empty(CategoryHelper.GetTagList(null));
    }

    [Fact]
    public void GetTagList_WhitespaceInput_ReturnsEmpty()
    {
        Assert.Empty(CategoryHelper.GetTagList("   "));
    }

    [Fact]
    public void GetTagList_SingleTag_ReturnsSingleElement()
    {
        var result = CategoryHelper.GetTagList("Restaurant");
        Assert.Single(result);
        Assert.Equal("Restaurant", result[0]);
    }

    [Fact]
    public void GetTagList_CommaSeparated_ReturnsAllTrimmed()
    {
        var result = CategoryHelper.GetTagList("Restaurant, Outdoors , Nightlife");
        Assert.Equal(3, result.Count);
        Assert.Contains("Restaurant", result);
        Assert.Contains("Outdoors", result);
        Assert.Contains("Nightlife", result);
    }

    [Theory]
    [InlineData("Restaurant",             "Restaurant", true)]
    [InlineData("RESTAURANT",             "restaurant", true)]   // case-insensitive
    [InlineData("Restaurant, Outdoors",   "Outdoors",   true)]
    [InlineData("Restaurant",             "Outdoors",   false)]
    [InlineData(null,                     "Restaurant", false)]
    public void HasTag_VariousCases(string? category, string tagName, bool expected)
    {
        Assert.Equal(expected, CategoryHelper.HasTag(category, tagName));
    }

    [Fact]
    public void GetFirstTag_SingleTag_ReturnsTag()
    {
        Assert.Equal("Restaurant", CategoryHelper.GetFirstTag("Restaurant"));
    }

    [Fact]
    public void GetFirstTag_MultipleTags_ReturnsFirst()
    {
        Assert.Equal("Restaurant", CategoryHelper.GetFirstTag("Restaurant, Outdoors"));
    }

    [Fact]
    public void GetFirstTag_NullInput_ReturnsEmpty()
    {
        Assert.Equal("", CategoryHelper.GetFirstTag(null));
    }

    [Fact]
    public void GetTagIcon_KnownTag_ReturnsEmoji()
    {
        var icon = CategoryHelper.GetTagIcon("Outdoors");
        Assert.False(string.IsNullOrEmpty(icon));
        Assert.NotEqual("\ud83d\udccd", icon); // should NOT be the default pin emoji
    }

    [Fact]
    public void GetTagIcon_UnknownTag_ReturnsDefaultPin()
    {
        Assert.Equal("\ud83d\udccd", CategoryHelper.GetTagIcon("UnknownCategory"));
    }

    [Fact]
    public void GetTagIcon_NullInput_ReturnsDefaultPin()
    {
        Assert.Equal("\ud83d\udccd", CategoryHelper.GetTagIcon(null));
    }

    [Fact]
    public void GetFirstTagIcon_MultipleCategories_ReturnsIconOfFirstTag()
    {
        // "Restaurant" is a known tag — it has a real emoji
        var icon = CategoryHelper.GetFirstTagIcon("Restaurant, Outdoors");
        Assert.Equal(CategoryHelper.GetTagIcon("Restaurant"), icon);
    }
}

public class AddressBuilderTests
{
    [Fact]
    public void BuildFullAddress_AllParts_JoinsWithComma()
    {
        var result = AddressBuilder.BuildFullAddress("123 Main St", "Apt 4", "Denver", "CO", "80203");
        Assert.Equal("123 Main St, Apt 4, Denver, CO, 80203", result);
    }

    [Fact]
    public void BuildFullAddress_NullOptionals_SkipsThem()
    {
        var result = AddressBuilder.BuildFullAddress("123 Main St", null, "Denver", null, "80203");
        Assert.Equal("123 Main St, Denver, 80203", result);
    }

    [Fact]
    public void BuildFullAddress_AllNull_ReturnsEmpty()
    {
        var result = AddressBuilder.BuildFullAddress(null, null, null, null, null);
        Assert.Equal("", result);
    }

    [Fact]
    public void BuildFullAddress_WhitespaceValues_SkipsThem()
    {
        var result = AddressBuilder.BuildFullAddress("  ", null, "Denver", "  ", "80203");
        Assert.Equal("Denver, 80203", result);
    }
}

public class GeoCalculatorTests
{
    [Fact]
    public void HaversineDistance_SamePoint_ReturnsZero()
    {
        var dist = GeoCalculator.HaversineDistance(39.7392, -104.9903, 39.7392, -104.9903);
        Assert.Equal(0.0, dist, precision: 6);
    }

    [Fact]
    public void HaversineDistance_DenverToNyc_ApproximatelyCorrect()
    {
        // Denver (39.7392, -104.9903) to NYC (40.7128, -74.0060) ≈ 1621 miles
        var dist = GeoCalculator.HaversineDistance(39.7392, -104.9903, 40.7128, -74.0060);
        Assert.InRange(dist, 1600, 1650);
    }

    [Fact]
    public void HaversineDistance_IsSymmetric()
    {
        var d1 = GeoCalculator.HaversineDistance(39.7392, -104.9903, 40.7128, -74.0060);
        var d2 = GeoCalculator.HaversineDistance(40.7128, -74.0060, 39.7392, -104.9903);
        Assert.Equal(d1, d2, precision: 6);
    }

    [Theory]
    [InlineData(0,   0.0)]
    [InlineData(90,  Math.PI / 2)]
    [InlineData(180, Math.PI)]
    public void ToRad_ConvertsCorrectly(double degrees, double expected)
    {
        Assert.Equal(expected, GeoCalculator.ToRad(degrees), precision: 10);
    }
}

public class QuotaWarningHelperTests
{
    private static QuotaStatusResponse MakeStatus(int used, int limit)
    {
        var item = new QuotaItem { Used = used, Limit = limit };
        return new QuotaStatusResponse
        {
            NearbySearch = item,
            PlaceDetails = item,
            Photos = item,
            Geocoding = item,
        };
    }

    [Fact]
    public void GetWarning_BelowEightyPercent_ReturnsNull()
    {
        var status = MakeStatus(79, 100);
        Assert.Null(QuotaWarningHelper.GetWarning(status));
    }

    [Fact]
    public void GetWarning_AtEightyPercent_ReturnsWarning()
    {
        var status = MakeStatus(80, 100);
        var warning = QuotaWarningHelper.GetWarning(status);
        Assert.NotNull(warning);
        Assert.Equal("warning", warning!.Level);
    }

    [Fact]
    public void GetWarning_AtNinetyFivePercent_ReturnsCritical()
    {
        var status = MakeStatus(95, 100);
        var warning = QuotaWarningHelper.GetWarning(status);
        Assert.NotNull(warning);
        Assert.Equal("critical", warning!.Level);
    }

    [Fact]
    public void GetWarning_MixedUsage_ReportsWorstField()
    {
        // Geocoding at 97%, others low
        var status = new QuotaStatusResponse
        {
            NearbySearch = new QuotaItem { Used = 10, Limit = 100 },
            PlaceDetails = new QuotaItem { Used = 20, Limit = 100 },
            Photos        = new QuotaItem { Used = 5,  Limit = 100 },
            Geocoding     = new QuotaItem { Used = 97, Limit = 100 },
        };
        var warning = QuotaWarningHelper.GetWarning(status);
        Assert.NotNull(warning);
        Assert.Equal("critical", warning!.Level);
        Assert.Contains("Geocoding", warning.Message);
    }
}

public class ImageUrlResolverTests
{
    [Fact]
    public void Resolve_NullImageUrl_ReturnsNull()
    {
        Assert.Null(ImageUrlResolver.Resolve(null, "https://api.example.com"));
    }

    [Fact]
    public void Resolve_AbsoluteUrl_ReturnsUrlUnchanged()
    {
        const string url = "https://cdn.example.com/photo.jpg";
        Assert.Equal(url, ImageUrlResolver.Resolve(url, "https://api.example.com"));
    }

    [Fact]
    public void Resolve_RelativePath_PrependBaseAddress()
    {
        var result = ImageUrlResolver.Resolve("/images/photo.jpg", "https://api.example.com");
        Assert.Equal("https://api.example.com/images/photo.jpg", result);
    }

    [Fact]
    public void Resolve_RelativePath_TrimsTrailingSlashOnBase()
    {
        var result = ImageUrlResolver.Resolve("/images/photo.jpg", "https://api.example.com/");
        Assert.Equal("https://api.example.com/images/photo.jpg", result);
    }

    [Fact]
    public void Resolve_RelativePath_NullBase_ReturnsRelativeAsIs()
    {
        var result = ImageUrlResolver.Resolve("/images/photo.jpg", null);
        Assert.Equal("/images/photo.jpg", result);
    }
}
