namespace ActivitiesApp.Shared.Services;

public interface IFormFactor
{
    public string GetFormFactor();
    public string GetPlatform();
    public bool IsNative => false;
}
