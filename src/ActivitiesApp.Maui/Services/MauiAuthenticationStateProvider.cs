using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace ActivitiesApp.Services;

public class MauiAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly AuthService _authService;

    public MauiAuthenticationStateProvider(AuthService authService)
    {
        _authService = authService;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var principal = _authService.IsSignedIn && _authService.Principal != null
            ? _authService.Principal
            : new ClaimsPrincipal(new ClaimsIdentity());

        return Task.FromResult(new AuthenticationState(principal));
    }
}
