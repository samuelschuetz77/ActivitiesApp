namespace ActivitiesApp.Shared.Services;

public interface IFormFactor
{
    string GetFormFactor();
    string GetPlatform();
    bool IsNative { get; }
}
