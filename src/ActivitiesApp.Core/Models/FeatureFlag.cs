public class FeatureFlag
{
    public string Name { get; set; }
    public bool IsEnabled { get; set; }

    public FeatureFlag(string name, bool isEnabled)
    {
        Name = name;
        IsEnabled = isEnabled;
    }
}

