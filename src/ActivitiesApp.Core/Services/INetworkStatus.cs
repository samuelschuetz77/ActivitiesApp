namespace ActivitiesApp.Services;

public interface INetworkStatus
{
    bool HasInternet { get; }
    event Action<bool>? ConnectivityChanged;
}
