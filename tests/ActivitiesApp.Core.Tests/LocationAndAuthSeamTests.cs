using ActivitiesApp.Services;
using ActivitiesApp.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace ActivitiesApp.Core.Tests;

public class LocationAndAuthSeamTests
{
    [Fact]
    public async Task LocationService_RaisesOnlyWhenMovementCrossesThreshold()
    {
        var provider = new SequenceLocationProvider();
        provider.Enqueue(39.7392, -104.9903);
        provider.Enqueue(39.73925, -104.99035);
        provider.Enqueue(39.7505, -105.0015);
        var service = new LocationService(NullLogger<LocationService>.Instance, provider);
        var changed = 0;
        service.LocationChanged += () => changed++;

        await service.EnableTrackingAsync();
        await service.RefreshAsync();
        await service.RefreshAsync();

        Assert.Equal(3, changed);
        Assert.True(service.HasLocation);
    }

    [Fact]
    public async Task LocationService_ManualOverrideTakesPriority_AndErrorClearsGpsState()
    {
        var provider = new SequenceLocationProvider();
        provider.Enqueue(39.7392, -104.9903);
        provider.EnqueueError("gps failed");
        var service = new LocationService(NullLogger<LocationService>.Instance, provider);

        await service.EnableTrackingAsync();
        service.SetManualLocation(40, -105, "Manual");
        await service.RefreshAsync();

        Assert.True(service.HasManualLocation);
        Assert.False(service.HasLocation);
        Assert.Equal(40, service.ActiveLatitude);
        Assert.Equal("gps failed", service.LastError);
    }

    [Fact]
    public async Task LocationService_DisableTrackingTurnsOffCurrentLocation_ButAllowsManualOverride()
    {
        var provider = new SequenceLocationProvider();
        provider.Enqueue(39.7392, -104.9903);
        var service = new LocationService(NullLogger<LocationService>.Instance, provider);

        await service.EnableTrackingAsync();
        service.SetManualLocation(40, -105, "Manual");
        service.DisableTracking();

        Assert.False(service.IsTrackingEnabled);
        Assert.False(service.HasLocation);
        Assert.True(service.HasManualLocation);
        Assert.Equal(40, service.ActiveLatitude);
    }

    [Fact]
    public async Task AuthHeaderHandler_AddsBearerHeader_WhenTokenExists()
    {
        var inner = new CaptureHandler();
        var handler = new AuthHeaderHandler(new StubTokenProvider("abc")) { InnerHandler = inner };
        var client = new HttpClient(handler);

        await client.GetAsync("https://example.test");

        Assert.NotNull(inner.LastRequest);
        Assert.Equal("Bearer", inner.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("abc", inner.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task AuthHeaderHandler_SkipsHeader_WhenTokenMissing()
    {
        var inner = new CaptureHandler();
        var handler = new AuthHeaderHandler(new StubTokenProvider(null)) { InnerHandler = inner };
        var client = new HttpClient(handler);

        await client.GetAsync("https://example.test");

        Assert.NotNull(inner.LastRequest);
        Assert.Null(inner.LastRequest!.Headers.Authorization);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
