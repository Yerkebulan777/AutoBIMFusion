using Autodesk.Windows;

namespace AutoBIMFusion.Application.Ribbon;

/// <summary>
/// Строит вкладку AutoBIMFusion на Ribbon AutoCAD.
/// </summary>
internal static class RibbonBuilder
{
    public static void CreateTab()
    {
        RibbonControl? ribbon = ComponentManager.Ribbon;
        if (ribbon is null || ribbon.Tabs.Any(t => t.Id == "AutoBIMFusion.RibbonTab"))
        {
            return;
        }

        RibbonButton button = new()
        {
            Size = RibbonItemSize.Large,
            Id = "AutoBIMFusionBtn1",
            Text = "Объединить DWG",
            Image = RibbonIconLoader.Load("icon-merge-dwg-16.png"),
            LargeImage = RibbonIconLoader.Load("icon-merge-dwg-32.png"),
            CommandParameter = "MERGEDWG ",
            CommandHandler = new ButtonCommandHandler()
        };

        RibbonPanelSource panelSource = new()
        {
            Title = "Panel",
            Id = "AutoBIMFusion.MainPanel"
        };
        panelSource.Items.Add(button);

        RibbonTab tab = new() { Id = "AutoBIMFusion.RibbonTab", Title = "AutoBIMFusion" };
        tab.Panels.Add(new RibbonPanel { Source = panelSource });
        ribbon.Tabs.Add(tab);
    }
}
