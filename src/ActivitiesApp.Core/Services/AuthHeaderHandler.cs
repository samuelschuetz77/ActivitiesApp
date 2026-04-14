namespace ActivitiesApp.Services;

public class AuthHeaderHandler : DelegatingHandler
{
    private readonly IAccessTokenProvider _tokenProvider;

    public AuthHeaderHandler(IAccessTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
        InnerHandler = new HttpClientHandler();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
