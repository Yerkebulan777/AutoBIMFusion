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

        RibbonButton mergeDwgButton = CreateLargeButton("AutoBIMFusionBtn1", "Объединить DWG", "MERGEDWG ");
        RibbonButton mergeTextButton = CreateLargeButton("AutoBIMFusionBtn2", "Склеить TEXT", "SMART_MERGE_TEXT ");

        RibbonPanelSource panelSource = new()
        {
            Title = "Panel",
            Id = "AutoBIMFusion.MainPanel"
        };
        panelSource.Items.Add(mergeDwgButton);
        panelSource.Items.Add(mergeTextButton);

        RibbonTab tab = new() { Id = "AutoBIMFusion.RibbonTab", Title = "AutoBIMFusion" };
        tab.Panels.Add(new RibbonPanel { Source = panelSource });
        ribbon.Tabs.Add(tab);
    }

    private static RibbonButton CreateLargeButton(string id, string text, string command)
    {
        return new RibbonButton
        {
            Size = RibbonItemSize.Large,
            Id = id,
            Text = text,
            Image = RibbonIconLoader.Load("icon-merge-dwg-16.png"),
            LargeImage = RibbonIconLoader.Load("icon-merge-dwg-32.png"),
            CommandParameter = command,
            CommandHandler = new ButtonCommandHandler()
        };
    }
}
