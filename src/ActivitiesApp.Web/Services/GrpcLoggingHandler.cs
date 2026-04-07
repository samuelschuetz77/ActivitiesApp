using System.Diagnostics;

namespace ActivitiesApp.Web.Services;

public sealed class GrpcLoggingHandler : DelegatingHandler
{
    private readonly ILogger<GrpcLoggingHandler> _logger;

    public GrpcLoggingHandler(HttpMessageHandler innerHandler, ILogger<GrpcLoggingHandler> logger)
        : base(innerHandler)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var method = request.Method.Method;
        var uri = request.RequestUri?.ToString() ?? "(null)";
        var contentType = request.Content?.Headers.ContentType?.ToString() ?? "(none)";

        _logger.LogInformation("Outgoing API request starting: {Method} {Uri} content-type={ContentType}",
            method, uri, contentType);

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            _logger.LogInformation(
                "Outgoing API request completed: {Method} {Uri} status={StatusCode} version={Version} in {DurationMs}ms",
                method, uri, (int)response.StatusCode, response.Version, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Outgoing API request failed: {Method} {Uri} after {DurationMs}ms",
                method, uri, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
