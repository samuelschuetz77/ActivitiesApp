namespace ActivitiesApp.Services;

public sealed class MauiNetworkStatus : INetworkStatus, IDisposable
{
    private readonly IConnectivity _connectivity;

    public MauiNetworkStatus(IConnectivity connectivity)
    {
        _connectivity = connectivity;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    public bool HasInternet => _connectivity.NetworkAccess == NetworkAccess.Internet;

    public event Action<bool>? ConnectivityChanged;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        ConnectivityChanged?.Invoke(e.NetworkAccess == NetworkAccess.Internet);
    }

    public void Dispose()
    {
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
    }
}
