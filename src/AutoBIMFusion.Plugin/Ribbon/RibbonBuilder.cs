using System.Runtime.Versioning;
using Autodesk.Windows;

namespace AutoBIMFusion.Plugin.Ribbon;

/// <summary>
///     Строит вкладку AutoBIMFusion на Ribbon AutoCAD.
/// </summary>
[SupportedOSPlatform("Windows")]
internal static class RibbonBuilder
{
    public static void CreateTab()
    {
        var ribbon = ComponentManager.Ribbon;
        if (ribbon is null || ribbon.Tabs.Any(t => t.Id == "AutoBIMFusion.RibbonTab")) return;

        RibbonPanelSource panelSource = new()
        {
            Title = "Panel",
            Id = "AutoBIMFusion.MainPanel"
        };

        panelSource.Items.Add(CreateLargeButton("AutoBIMFusionBtn1", "Merge DWG", "MERGEDWG "));

        RibbonTab tab = new() { Id = "AutoBIMFusion.RibbonTab", Title = "AutoBIMFusion" };
        tab.Panels.Add(new RibbonPanel { Source = panelSource });
        ribbon.Tabs.Add(tab);
    }

    private static RibbonButton CreateLargeButton(string id, string text, string command)
    {
        return new RibbonButton
        {
            Id = id,
            Text = text.Replace(" ", "\n"),
            ShowText = true,
            ShowImage = true,
            Size = RibbonItemSize.Large,
            Orientation = System.Windows.Controls.Orientation.Vertical,
            Width = 80,
            Height = 80,
            CommandParameter = command,
            CommandHandler = new ButtonCommandHandler(),
            Image = RibbonIconLoader.Load("icon-merge-dwg-16.png"),
            LargeImage = RibbonIconLoader.Load("icon-merge-dwg-32.png")
        };
    }
}
