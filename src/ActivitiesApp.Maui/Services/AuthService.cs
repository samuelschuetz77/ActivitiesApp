using Microsoft.Identity.Client;

namespace ActivitiesApp.Services;

public class AuthService
{
    private const string ClientId = "6d3dc4ee-33ce-4656-95c8-702a38464687";
    private const string TenantId = "dd619295-84ec-4c0d-a433-0076edc7c0d6";
    private const string Authority = $"https://login.microsoftonline.com/{TenantId}";
    private static readonly string[] Scopes = [$"api://{ClientId}/access_as_user"];

    private readonly IPublicClientApplication _pca;
    private AuthenticationResult? _authResult;

    public bool IsSignedIn => _authResult != null;
    public string UserEmail => _authResult?.Account?.Username ?? "";
    public string UserName => _authResult?.ClaimsPrincipal?.FindFirst("name")?.Value ?? UserEmail;

    public AuthService()
    {
        _pca = PublicClientApplicationBuilder
            .Create(ClientId)
            .WithAuthority(Authority)
            .WithRedirectUri($"msal{ClientId}://auth")
            .Build();
    }

    public async Task<bool> SignInAsync()
    {
        try
        {
            _authResult = await _pca.AcquireTokenInteractive(Scopes)
                .WithUseEmbeddedWebView(false)
                .ExecuteAsync();
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
}
