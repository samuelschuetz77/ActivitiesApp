namespace ActivitiesApp.Core.Helpers;

public static class ZipCodeValidator
{
    public static bool IsValid(string? input) =>
        input is { Length: 5 } && input.All(char.IsDigit);
}
