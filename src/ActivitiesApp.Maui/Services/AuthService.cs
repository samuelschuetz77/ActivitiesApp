using Microsoft.Identity.Client;

namespace ActivitiesApp.Services;

public class AuthService : IAccessTokenProvider
{
    private const string ClientId = "6d3dc4ee-33ce-4656-95c8-702a38464687";
    private const string TenantId = "common";
    private const string Authority = $"https://login.microsoftonline.com/{TenantId}";
    private static readonly string[] Scopes = [$"api://{ClientId}/access_as_user"];

    private readonly IPublicClientApplication _pca;
    private AuthenticationResult? _authResult;

    public event Action? AuthenticationStateChanged;

    public bool IsSignedIn => _authResult != null;
    public string UserEmail => _authResult?.Account?.Username ?? "";
    public string UserName => _authResult?.ClaimsPrincipal?.FindFirst("name")?.Value ?? UserEmail;
    public System.Security.Claims.ClaimsPrincipal? Principal => _authResult?.ClaimsPrincipal;

    public AuthService()
    {
        _pca = PublicClientApplicationBuilder
            .Create(ClientId)
            .WithAuthority(Authority)
            .WithRedirectUri(GetRedirectUri())
            .Build();
    }

    public async Task<bool> SignInAsync()
    {
        try
        {
            var interactiveRequest = _pca.AcquireTokenInteractive(Scopes);

#if ANDROID
            var activity = ActivitiesApp.Platforms.Android.AuthParentActivityProvider.CurrentActivity
                ?? throw new InvalidOperationException("Android sign-in requires the current Activity before launching the browser.");

            var useSystemBrowser = _pca.IsSystemWebViewAvailable();
            interactiveRequest = interactiveRequest
                .WithParentActivityOrWindow(activity)
                .WithUseEmbeddedWebView(!useSystemBrowser);
#else
            interactiveRequest = interactiveRequest.WithUseEmbeddedWebView(false);
#endif

            _authResult = await interactiveRequest.ExecuteAsync();
            AuthenticationStateChanged?.Invoke();
            return true;
        }
        catch (MsalClientException ex) when (ex.ErrorCode == "authentication_canceled")
        {
            return false;
        }
    }

    public async Task SignOutAsync()
    {
        var accounts = await _pca.GetAccountsAsync();
        foreach (var account in accounts)
        {
            await _pca.RemoveAsync(account);
        }
        _authResult = null;
        AuthenticationStateChanged?.Invoke();
    }

    public async Task<string?> GetTokenAsync()
    {
        var accounts = await _pca.GetAccountsAsync();
        var account = accounts.FirstOrDefault();

        if (account == null) return null;

        try
        {
            _authResult = await _pca.AcquireTokenSilent(Scopes, account).ExecuteAsync();
            return _authResult.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            _authResult = null;
            return null;
        }
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        var token = await GetTokenAsync();
        return token != null;
    }

    private static string GetRedirectUri()
    {
#if WINDOWS
        return "http://localhost";
#else
        return $"msal{ClientId}://auth";
#endif
    }
}
