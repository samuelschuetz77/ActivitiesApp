namespace ActivitiesApp.Services;

public class AuthHeaderHandler : DelegatingHandler
{
    private readonly AuthService _authService;

    public AuthHeaderHandler(AuthService authService)
    {
        _authService = authService;
        InnerHandler = new HttpClientHandler();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _authService.GetTokenAsync();
        if (token != null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
