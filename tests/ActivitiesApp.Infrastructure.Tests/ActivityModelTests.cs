using ActivitiesApp.Infrastructure.Models;
using Xunit;

namespace ActivitiesApp.Infrastructure.Tests;

public class ActivityModelTests
{
    [Fact]
    public void NewActivity_HasNonEmptyDefaultId()
    {
        var activity = new Activity();

        Assert.NotEqual(Guid.Empty, activity.Id);
    }

    [Fact]
    public void NewActivity_HasCorrectStringDefaults()
    {
        var activity = new Activity();

        Assert.Equal("", activity.Name);
        Assert.Equal("", activity.City);
        Assert.Equal("", activity.Description);
    }

    [Fact]
    public void NewActivity_HasZeroCostDefault()
    {
        var activity = new Activity();

        Assert.Equal(0, activity.Cost);
    }

    [Fact]
    public void NewActivity_IsNotSoftDeletedByDefault()
    {
        var activity = new Activity();

        Assert.False(activity.IsDeleted);
    }

    [Fact]
    public void NewActivity_NullableFieldsAreNull()
    {
        var activity = new Activity();

        Assert.Null(activity.ImageUrl);
        Assert.Null(activity.PlaceId);
        Assert.Null(activity.Category);
        Assert.Null(activity.CreatedByUserId);
    }

    [Fact]
    public void Activity_AgeRange_CanBeSetAndRetrieved()
    {
        var activity = new Activity
        {
            MinAge = 18,
            MaxAge = 65
        };

        Assert.Equal(18, activity.MinAge);
        Assert.Equal(65, activity.MaxAge);
    }

    [Fact]
    public void Activity_Coordinates_CanBeSet()
    {
        var activity = new Activity
        {
            Latitude = 40.7128,
            Longitude = -74.0060
        };

        Assert.Equal(40.7128, activity.Latitude);
        Assert.Equal(-74.0060, activity.Longitude);
    }

    [Fact]
    public void TwoNewActivities_HaveDifferentIds()
    {
        var a1 = new Activity();
        var a2 = new Activity();

        Assert.NotEqual(a1.Id, a2.Id);
    }
}
