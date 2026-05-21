using System.Resources;

namespace AutoBIMFusion.Common.Configuration;

public static class Settings
{
    public static int TransientPrimaryColorIndex { get; set; } = 1;
    public static int TransientSecondaryColorIndex { get; set; } = 3;
    public static int MultithreadingMaxNumberOfThread { get; } = Environment.ProcessorCount;
}

public sealed class LegacyAppSettings
{
    public static LegacyAppSettings Default { get; } = new();

    public string SelectionPointsType { get; set; } = "Points";

    public static void Save()
    {
    }
}

public static class Resources
{
    public static ResourceManager ResourceManager { get; } = new(typeof(Resources));
}
