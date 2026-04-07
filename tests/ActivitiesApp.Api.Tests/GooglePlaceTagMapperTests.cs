using ActivitiesApp.Api.Services;
using Xunit;

namespace ActivitiesApp.Api.Tests;

public class GooglePlaceTagMapperTests
{
    [Fact]
    public void GetTags_ReturnsCorrectTag_ForRestaurant()
    {
        var tags = GooglePlaceTagMapper.GetTags(["restaurant"]);

        Assert.Contains("Food & Drink", tags);
    }

    [Fact]
    public void GetTags_ReturnsMultipleTags_ForBar()
    {
        // A bar should map to both Food & Drink and Nightlife
        var tags = GooglePlaceTagMapper.GetTags(["bar"]);

        Assert.Contains("Food & Drink", tags);
        Assert.Contains("Nightlife", tags);
    }

    [Fact]
    public void GetTags_ReturnsEmpty_ForUnknownType()
    {
        var tags = GooglePlaceTagMapper.GetTags(["unknown_type_xyz"]);

        Assert.Empty(tags);
    }

    [Fact]
    public void GetTags_HandlesMultipleGoogleTypes()
    {
        var tags = GooglePlaceTagMapper.GetTags(["park", "museum"]);

        Assert.Contains("Outdoors", tags);
        Assert.Contains("Arts & Culture", tags);
        Assert.Contains("Education", tags); // museum is also Education
    }

    [Fact]
    public void TryGetDefinition_ReturnsTrue_ForValidTag()
    {
        var found = GooglePlaceTagMapper.TryGetDefinition("Outdoors", out var definition);

        Assert.True(found);
        Assert.NotNull(definition);
        Assert.Equal("Outdoors", definition.Name);
        Assert.Contains("park", definition.SearchTypes);
    }

    [Fact]
    public void TryGetDefinition_IsCaseInsensitive()
    {
        var found = GooglePlaceTagMapper.TryGetDefinition("food & drink", out var definition);

        Assert.True(found);
        Assert.NotNull(definition);
        Assert.Equal("Food & Drink", definition.Name);
    }

    [Fact]
    public void TryGetDefinition_ReturnsFalse_ForInvalidTag()
    {
        var found = GooglePlaceTagMapper.TryGetDefinition("Nonexistent Tag", out _);

        Assert.False(found);
    }

    [Fact]
    public void AllTags_ContainsExpectedCount()
    {
        // There are 9 tag categories defined
        //CHANGE BACK 
        //AJKLSDFHKLAJSDHFLKSADJH
        //AHSDKFGASDKLJFGASKDJFH
        Assert.Equal(10, GooglePlaceTagMapper.AllTags.Count);
    }

    [Fact]
    public void AllTags_EachHasNonEmptySearchTypes()
    {
        foreach (var tag in GooglePlaceTagMapper.AllTags)
        {
            Assert.False(string.IsNullOrWhiteSpace(tag.Name), "Tag name should not be empty");
            Assert.True(tag.SearchTypes.Count > 0, $"Tag '{tag.Name}' should have at least one SearchType");
            Assert.True(tag.GoogleTypes.Count > 0, $"Tag '{tag.Name}' should have at least one GoogleType");
        }
    }
}
