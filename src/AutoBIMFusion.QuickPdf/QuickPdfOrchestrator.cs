using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using AutoBIMFusion.QuickPdf.Naming;
using AutoBIMFusion.QuickPdf.Plotting;
using System.Globalization;
using Exception = System.Exception;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using PlotAreaType = Autodesk.AutoCAD.DatabaseServices.PlotType;

namespace AutoBIMFusion.QuickPdf;

/// <summary>
///     Экспорт рамки модели в PDF: наименьший ISO (full bleed / expand) или точный custom-лист, масштаб 1:100.
/// </summary>
public static class QuickPdfOrchestrator
{
    private const string BootstrapMedia = "ISO_A4_(210.00_x_297.00_MM)";

    private static readonly string[] PreferredDevices =
    [
        "AutoCAD PDF (General Documentation).pc3",
        "DWG To PDF.pc3"
    ];

    public static void ExportFrame(Document document, Point3d first, Point3d second)
    {
        Editor editor = document.Editor;
        Extents2d window = PlotWindowConverter.ToPlotWindow(editor, first, second);
        double width = Abs(window.MaxPoint.X - window.MinPoint.X);
        double height = Abs(window.MaxPoint.Y - window.MinPoint.Y);
        if (width < 1e-6 || height < 1e-6)
        {
            throw new QuickPdfException("Рамка слишком мала.");
        }

        double paperWidth = width / 100.0;
        double paperHeight = height / 100.0;
        string prefix = QuickPdfNaming.SafeName(
            AcadApp.GetSystemVariable("DWGNAME") is string { Length: > 0 } name ? name : document.Name);
        string folder = Path.Combine(QuickPdfNaming.ResolveDesktop(), prefix);
        try
        {
            _ = Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new QuickPdfException("Не удалось создать папку: " + folder, ex);
        }

        int sheet = QuickPdfNaming.NextSheetIndex(folder, prefix);
        string pdfPath = Path.Combine(
            folder, prefix + "_" + sheet.ToString("D3", CultureInfo.InvariantCulture) + ".pdf");

        using (document.LockDocument())
        using (new SystemVariableScope(("CMDECHO", (short)0), ("BACKGROUNDPLOT", (short)0), ("FILEDIA", (short)0)))
        using (PlotSettings settings = CreateModelPlotSettings(document))
        {
            PlotSettingsValidator validator = PlotSettingsValidator.Current;
            List<string> devices = [.. PlotLists.Names(validator.GetPlotDeviceList())];
            string? device = null;
            foreach (string wanted in PreferredDevices)
            {
                device = devices.Find(candidate =>
                    string.Equals(candidate, wanted, StringComparison.OrdinalIgnoreCase));
                if (device is not null)
                {
                    break;
                }
            }

            if (device is null)
            {
                throw new QuickPdfException("PDF-плоттер (pc3) не найден.");
            }

            BindPdfDevice(settings, validator, device);

            IsoMediaChoice? iso = IsoMediaPicker.Pick(settings, validator, paperWidth, paperHeight);
            string mediaLabel;
            MatchingPolicy policy;
            Action<PlotSettings> verify;
            if (iso is { } choice)
            {
                validator.SetPlotRotation(settings, choice.Rotation);
                mediaLabel = choice.CanonicalName;
                policy = MatchingPolicy.MatchEnabled;
                verify = validated => PlotEngineRunner.VerifyIso(validated, choice, paperWidth, paperHeight);
            }
            else
            {
                validator.SetPlotRotation(settings, PlotRotation.Degrees000);
                mediaLabel = "Custom " + paperWidth.ToString("0.000", CultureInfo.InvariantCulture) +
                             " x " + paperHeight.ToString("0.000", CultureInfo.InvariantCulture) + " mm";
                policy = MatchingPolicy.MatchEnabledTemporaryCustom;
                verify = validated => PlotEngineRunner.VerifyCustom(
                    validated, paperWidth, paperHeight, window, settings.CurrentStyleSheet);
                editor.WriteMessage(
                    $"\nISO-формат {paperWidth:0.00} x {paperHeight:0.00} мм не найден. Печать на пользовательском листе.");
            }

            validator.SetPlotPaperUnits(settings, PlotPaperUnit.Millimeters);
            ApplyPlotOptions(settings, validator, window);

            using (iso is null ? CustomPaper.Bind(document.Database, settings, paperWidth, paperHeight) : null)
            {
                editor.WriteMessage(
                    "\nПринтер: " + device +
                    "\nБумага: " + mediaLabel + (paperWidth >= paperHeight ? " landscape" : " portrait") +
                    " | Стандартный масштаб 1:100 | Сдвиг X=0 Y=0 | Центрирование выкл | Рамка на бумаге: " +
                    paperWidth.ToString("0.0", CultureInfo.InvariantCulture) + " x " +
                    paperHeight.ToString("0.0", CultureInfo.InvariantCulture) +
                    " мм (1 мм = 100 единиц чертежа)" +
                    "\nФайл: " + pdfPath);
                PlotEngineRunner.Publish(document, settings, pdfPath, policy, verify);
            }
        }

        editor.WriteMessage(
            "\nЛист " + sheet.ToString("D3", CultureInfo.InvariantCulture) + " сохранён: " + pdfPath);
    }

    private static PlotSettings CreateModelPlotSettings(Document document)
    {
        using Transaction tr = document.Database.TransactionManager.StartTransaction();
        Layout layout = (Layout)tr.GetObject(
            LayoutManager.Current.GetLayoutId(LayoutManager.Current.CurrentLayout), OpenMode.ForRead);
        if (!layout.ModelType)
        {
            throw new QuickPdfException("Команда доступна только в пространстве модели.");
        }

        PlotSettings settings = new(true);
        settings.CopyFrom(layout);
        tr.Commit();
        return settings;
    }

    private static void BindPdfDevice(PlotSettings settings, PlotSettingsValidator validator, string device)
    {
        try
        {
            validator.SetPlotConfigurationName(settings, device, BootstrapMedia);
        }
        catch (Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or ArgumentException)
        {
            string fallback = PlotLists.Names(validator.GetCanonicalMediaNameList(settings)).FirstOrDefault()
                ?? throw new QuickPdfException("Не удалось назначить PDF-плоттер " + device + ".", ex);
            validator.SetPlotConfigurationName(settings, device, fallback);
        }

        validator.RefreshLists(settings);
    }

    private static void ApplyPlotOptions(PlotSettings settings, PlotSettingsValidator validator, Extents2d window)
    {
        validator.SetPlotWindowArea(settings, window);
        validator.SetPlotType(settings, PlotAreaType.Window);
        validator.SetUseStandardScale(settings, true);
        validator.SetStdScaleType(settings, StdScaleType.StdScale1To100);
        validator.SetPlotCentered(settings, false);
        validator.SetPlotOrigin(settings, new Point2d(0, 0));
        validator.SetCurrentStyleSheet(settings, FindStyleSheet(validator));
        settings.PlotPlotStyles = true;
        settings.PrintLineweights = true;
        settings.PlotHidden = false;
        settings.ShadePlot = PlotSettingsShadePlotType.AsDisplayed;
        settings.ShadePlotResLevel = ShadePlotResLevel.Normal;
    }

    private static string FindStyleSheet(PlotSettingsValidator validator)
    {
        string wanted = Convert.ToInt16(AcadApp.GetSystemVariable("PSTYLEMODE"), CultureInfo.InvariantCulture) == 1
            ? "acad.ctb"
            : "acad.stb";
        return PlotLists.Names(validator.GetPlotStyleSheetList())
            .FirstOrDefault(name => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new QuickPdfException("Стандартная таблица стилей печати не найдена: " + wanted);
    }
}
