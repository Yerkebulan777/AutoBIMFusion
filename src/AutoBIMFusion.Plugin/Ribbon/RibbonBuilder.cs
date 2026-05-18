using Autodesk.Windows;
using System.Runtime.Versioning;

namespace AutoBIMFusion.Plugin.Ribbon;

/// <summary>
///     Строит вкладку AutoBIMFusion на Ribbon AutoCAD.
/// </summary>
[SupportedOSPlatform("Windows")]
internal static class RibbonBuilder
{
    public static void CreateTab()
    {
        RibbonControl? ribbon = ComponentManager.Ribbon;
        if (ribbon is null || ribbon.Tabs.Any(t => t.Id == "AutoBIMFusion.RibbonTab"))
        {
            return;
        }

        RibbonPanelSource panelSource = new()
        {
            Title = "Panel",
            Id = "AutoBIMFusion.MainPanel"
        };

        panelSource.Items.Add(CreateLargeButton("AutoBIMFusionBtn1", "Объединить DWG", "MERGEDWG "));

        RibbonTab tab = new() { Id = "AutoBIMFusion.RibbonTab", Title = "AutoBIMFusion" };
        tab.Panels.Add(new RibbonPanel { Source = panelSource });
        ribbon.Tabs.Add(tab);
    }

    private static RibbonButton CreateLargeButton(string id, string text, string command)
    {
        return new RibbonButton
        {
            Id = id,
            Text = text,
            Size = RibbonItemSize.Large,
            CommandParameter = command,
            CommandHandler = new ButtonCommandHandler(),
            Image = RibbonIconLoader.Load("icon-merge-dwg-16.png"),
            LargeImage = RibbonIconLoader.Load("icon-merge-dwg-32.png")
        };
    }
}
