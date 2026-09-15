using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using AutoBIMFusion.QuickPdf.Frames;
using AutoBIMFusion.QuickPdf.Naming;
using AutoBIMFusion.QuickPdf.Plotting;
using Serilog;
using System.Globalization;
using Exception = System.Exception;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using PlotAreaType = Autodesk.AutoCAD.DatabaseServices.PlotType;

namespace AutoBIMFusion.QuickPdf;

/// <summary>
///     Экспорт рамок FRAMELIST в PDF: участок модели или все рамки, имя файла, ISO / custom, масштаб 1:100.
/// </summary>
public static class QuickPdfOrchestrator
{
    private const string BootstrapMedia = "ISO_A4_(210.00_x_297.00_MM)";

    private static readonly string[] PreferredDevices =
    [
        "AutoCAD PDF (General Documentation).pc3",
        "DWG To PDF.pc3"
    ];

    public static void ExportFrameList(Document document, ILogger log)
    {
        Run(document, area: null, outputPath: null, log);
    }

    public static bool TryExportInteractively(Document document, ILogger log)
    {
        if (!QuickPdfPrompts.TryCollect(document.Editor, DrawingName(document), out QuickPdfInput input))
        {
            return false;
        }

        Run(document, input.Area, input.OutputPath, log);
        return true;
    }

    private static void Run(Document document, FrameWindow? area, string? outputPath, ILogger log)
    {
        Editor editor = document.Editor;
        using (document.LockDocument())
        {
            IReadOnlyList<DetectedFrame> frames = FrameSheetOrder.Sort(
                FrameListInitializer.Load(document.Database, area));

            if (frames.Count == 0)
            {
                throw new QuickPdfException(area is null
                    ? "На слое " + FrameListInitializer.LayerName + " рамки не найдены."
                    : "В указанном участке рамки не найдены.");
            }

            PdfDestination destination = QuickPdfNaming.Resolve(outputPath, DrawingName(document));
            try
            {
                _ = Directory.CreateDirectory(destination.Folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new QuickPdfException("Не удалось создать папку: " + destination.Folder, ex);
            }

            using (new SystemVariableScope(("CMDECHO", (short)0), ("BACKGROUNDPLOT", (short)0), ("FILEDIA", (short)0)))
            using (SilentPdfDevice plotter = SilentPdfDevice.Open(ResolvePdfDevice()))
            {
                string device = ResolveBoundDevice(document, plotter, log);
                log.Information(
                    "QUICKPDF started: drawing={Drawing}; frames={FrameCount}; device={Device}",
                    document.Name, frames.Count, device);
                editor.WriteMessage(
                    "\nQuickPDF: рамок " + frames.Count.ToString(CultureInfo.InvariantCulture) +
                    ". Порядок: справа налево, сверху вниз.");
                int index = 0;
                foreach (DetectedFrame frame in frames)
                {
                    index++;
                    log.Debug(
                        "QUICKPDF frame {Index}/{Count}: {MinX:0.###},{MinY:0.###} - {MaxX:0.###},{MaxY:0.###}",
                        index, frames.Count, frame.MinX, frame.MinY, frame.MaxX, frame.MaxY);
                    ExportFrame(
                        document,
                        device,
                        new Point3d(frame.MinX, frame.MinY, 0),
                        new Point3d(frame.MaxX, frame.MaxY, 0),
                        destination.Folder,
                        destination.Prefix,
                        log);
                }

                log.Information("QUICKPDF completed: sheets={Count}; folder={Folder}", frames.Count, destination.Folder);
            }
        }
    }

    private static string DrawingName(Document document)
    {
        return QuickPdfNaming.DrawingName(AcadApp.GetSystemVariable("DWGNAME") as string, document.Name);
    }

    private static void ExportFrame(
        Document document,
        string device,
        Point3d first,
        Point3d second,
        string folder,
        string prefix,
        ILogger log)
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
        log.Debug(
            "QUICKPDF frame: drawing={Drawing}; frame={FrameWidth:0.###}x{FrameHeight:0.###}; paper={PaperWidth:0.###}x{PaperHeight:0.###} mm",
            document.Name, width, height, paperWidth, paperHeight);

        int sheet = QuickPdfNaming.NextSheetIndex(folder, prefix);
        string pdfPath = Path.Combine(
            folder, prefix + "_" + sheet.ToString("D3", CultureInfo.InvariantCulture) + ".pdf");
        log.Debug("QUICKPDF output: {PdfPath}", pdfPath);

        using PlotSettings settings = CreateModelPlotSettings(document);
        PlotSettingsValidator validator = PlotSettingsValidator.Current;
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
        log.Debug(
            "QUICKPDF media: mode={Mode}; media={Media}; rotation={Rotation}; matching={MatchingPolicy}",
            iso is null ? "Custom" : "ISO", mediaLabel, settings.PlotRotation, policy);

        using (iso is null ? CustomPaper.Bind(document.Database, settings, paperWidth, paperHeight) : null)
        {
            log.Debug(
                "QUICKPDF settings: media={CanonicalMedia}; paper={PaperWidth:0.###}x{PaperHeight:0.###} mm; " +
                "margins=({MarginLeft:0.###},{MarginBottom:0.###})-({MarginRight:0.###},{MarginTop:0.###})",
                settings.CanonicalMediaName,
                settings.PlotPaperSize.X,
                settings.PlotPaperSize.Y,
                settings.PlotPaperMargins.MinPoint.X,
                settings.PlotPaperMargins.MinPoint.Y,
                settings.PlotPaperMargins.MaxPoint.X,
                settings.PlotPaperMargins.MaxPoint.Y);
            editor.WriteMessage(
                "\nПринтер: " + device +
                "\nБумага: " + mediaLabel + (paperWidth >= paperHeight ? " landscape" : " portrait") +
                " | Стандартный масштаб 1:100 | Сдвиг X=0 Y=0 | Центрирование выкл | Рамка на бумаге: " +
                paperWidth.ToString("0.0", CultureInfo.InvariantCulture) + " x " +
                paperHeight.ToString("0.0", CultureInfo.InvariantCulture) +
                " мм (1 мм = 100 единиц чертежа)" +
                "\nФайл: " + pdfPath);
            PlotEngineRunner.Publish(document, settings, pdfPath, policy, verify, log);
        }

        log.Debug("QUICKPDF sheet saved: {PdfPath}", pdfPath);
        editor.WriteMessage(
            "\nЛист " + sheet.ToString("D3", CultureInfo.InvariantCulture) + " сохранён: " + pdfPath);
    }

    private static string ResolveBoundDevice(Document document, SilentPdfDevice plotter, ILogger log)
    {
        if (string.Equals(plotter.Name, plotter.Source, StringComparison.OrdinalIgnoreCase))
        {
            return plotter.Source;
        }

        using PlotSettings probe = CreateModelPlotSettings(document);
        try
        {
            BindPdfDevice(probe, PlotSettingsValidator.Current, plotter.Name);
            return plotter.Name;
        }
        catch (Exception ex) when (ex is QuickPdfException or Autodesk.AutoCAD.Runtime.Exception)
        {
            log.Warning(ex, "QUICKPDF silent device failed; using {Device}", plotter.Source);
            return plotter.Source;
        }
    }

    private static string ResolvePdfDevice()
    {
        List<string> devices = [.. PlotLists.Names(PlotSettingsValidator.Current.GetPlotDeviceList())];
        foreach (string wanted in PreferredDevices)
        {
            string? device = devices.Find(candidate =>
                string.Equals(candidate, wanted, StringComparison.OrdinalIgnoreCase));
            if (device is not null)
            {
                return device;
            }
        }

        throw new QuickPdfException("PDF-плоттер (pc3) не найден.");
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
