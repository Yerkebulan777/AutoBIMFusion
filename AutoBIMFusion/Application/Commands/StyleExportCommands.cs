using AutoBIMFusion.Application.Utils;
using Autodesk.AutoCAD.ApplicationServices;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Commands;

/// <summary>
/// Класс с командами AutoCAD для экспорта таблиц стилей.
/// </summary>
public class StyleExportCommands
{
    [CommandMethod("ExportTextStylesToMd", CommandFlags.Modal)]
    public static void ExportTextStyles()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        ArgumentNullException.ThrowIfNull(doc, nameof(doc));

        // Вызываем утилиту, передавая ей ID таблицы текстовых стилей и настройки отчета
        StyleExportUtils.ExportSymbolTableToMd(
            doc.Database,
            doc.Database.TextStyleTableId,
            "TextStylesReport.md",
            "Отчет по текстовым стилям (Text Styles)",
            "Текстовый стиль",
            doc.Editor);
    }

    [CommandMethod("ExportDimStylesToMd", CommandFlags.Modal)]
    public static void ExportDimStyles()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        ArgumentNullException.ThrowIfNull(doc, nameof(doc));

        // Вызываем ту же утилиту, но передаем ID таблицы размерных стилей
        StyleExportUtils.ExportSymbolTableToMd(
            doc.Database,
            doc.Database.DimStyleTableId,
            "DimStylesReport.md",
            "Отчет по размерным стилям (Dimension Styles)",
            "Размерный стиль",
            doc.Editor);
    }
}
