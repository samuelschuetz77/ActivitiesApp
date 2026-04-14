namespace ActivitiesApp.Services;

public interface IAccessTokenProvider
{
    Task<string?> GetTokenAsync();
}
