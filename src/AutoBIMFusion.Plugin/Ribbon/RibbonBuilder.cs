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

        panelSource.Items.Add(CreateLargeButton(
            "AutoBIMFusionBtn1",
            "Merge DWG",
            "MERGEDWG ",
            "icon-merge-dwg-16.png",
            "icon-merge-dwg-32.png",
            "Объединение DWG"));
        panelSource.Items.Add(CreateLargeButton(
            "AutoBIMFusionBtnQuickPdf",
            "Quick PDF",
            "QUICKPDF ",
            "icon-quick-pdf-16.png",
            "icon-quick-pdf-32.png",
            "Экспорт рамки модели в PDF (ISO / custom, 1:100)"));

        RibbonTab tab = new() { Id = "AutoBIMFusion.RibbonTab", Title = "AutoBIMFusion" };
        tab.Panels.Add(new RibbonPanel { Source = panelSource });
        ribbon.Tabs.Add(tab);
    }

    private static RibbonButton CreateLargeButton(
        string id,
        string text,
        string command,
        string smallIcon,
        string largeIcon,
        string description)
    {
        return new RibbonButton
        {
            Id = id,
            Text = text.Replace(" ", "\n"),
            Description = description,
            ShowText = true,
            ShowImage = true,
            Size = RibbonItemSize.Large,
            Orientation = System.Windows.Controls.Orientation.Vertical,
            Width = 80,
            Height = 80,
            CommandParameter = command,
            CommandHandler = new ButtonCommandHandler(),
            Image = RibbonIconLoader.Load(smallIcon),
            LargeImage = RibbonIconLoader.Load(largeIcon)
        };
    }
}
