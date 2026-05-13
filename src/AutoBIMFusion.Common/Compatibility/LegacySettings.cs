using System.Resources;

namespace AutoBIMFusion.Common.Compatibility;

public static class Settings
{
    public static int TransientPrimaryColorIndex { get; set; } = 1;
    public static int TransientSecondaryColorIndex { get; set; } = 3;
    public static int MultithreadingMaxNumberOfThread { get; set; } = Environment.ProcessorCount;
}

public static class Settings
{
    public static int TransientPrimaryColorIndex
    {
        get => Commun.Settings.TransientPrimaryColorIndex;
        set => Commun.Settings.TransientPrimaryColorIndex = value;
    }

    public static int TransientSecondaryColorIndex
    {
        get => Commun.Settings.TransientSecondaryColorIndex;
        set => Commun.Settings.TransientSecondaryColorIndex = value;
    }
}

public sealed class Settings
{
    public static Settings Default { get; } = new();

    public string SelectionPointsType { get; set; } = "Points";

    public void Save()
    {
    }
}

public static class Resources
{
    public static ResourceManager ResourceManager { get; } = new(typeof(Resources));
}