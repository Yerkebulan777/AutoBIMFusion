using System.Resources;

namespace AutoBIMFusion.Common.Compatibility
{
    public static class Settings
    {
        public static int TransientPrimaryColorIndex { get; set; } = 1;
        public static int TransientSecondaryColorIndex { get; set; } = 3;
        public static int MultithreadingMaxNumberOfThread { get; } = Environment.ProcessorCount;
    }
}

namespace AutoBIMFusion.Common.Compatibility
{
    public static class Settings
    {
        public static int TransientPrimaryColorIndex
        {
            get => global::AutoBIMFusion.Common.Compatibility.Settings.TransientPrimaryColorIndex;
            set => global::AutoBIMFusion.Common.Compatibility.Settings.TransientPrimaryColorIndex = value;
        }

        public static int TransientSecondaryColorIndex
        {
            get => global::AutoBIMFusion.Common.Compatibility.Settings.TransientSecondaryColorIndex;
            set => global::AutoBIMFusion.Common.Compatibility.Settings.TransientSecondaryColorIndex = value;
        }

        public static int MultithreadingMaxNumberOfThread => global::AutoBIMFusion.Common.Compatibility.Settings.MultithreadingMaxNumberOfThread;
    }

    public sealed class LegacyAppSettings
    {
        public static LegacyAppSettings Default { get; } = new();

        public string SelectionPointsType { get; set; } = "Points";

        public void Save()
        {
        }
    }

    public static class Resources
    {
        public static ResourceManager ResourceManager { get; } = new(typeof(Resources));
    }
}
