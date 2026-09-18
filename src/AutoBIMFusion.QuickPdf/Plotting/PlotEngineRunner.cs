using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using AutoBIMFusion.QuickPdf.Media;
using Serilog;
using PlotAreaType = Autodesk.AutoCAD.DatabaseServices.PlotType;
using Exception = System.Exception;

namespace AutoBIMFusion.QuickPdf.Plotting;

/// <summary>
///     Печать через PlotEngine (рекомендуемый Autodesk путь вместо COM PlotToFile).
/// </summary>
internal static class PlotEngineRunner
{
    public static void Publish(
        Document document,
        PlotSettings settings,
        string pdfPath,
        MatchingPolicy matchingPolicy,
        Action<PlotSettings> verify,
        ILogger log)
    {
        if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
        {
            throw new QuickPdfException("AutoCAD уже печатает. Подождите и повторите.");
        }

        string directory = Path.GetDirectoryName(pdfPath)
            ?? throw new QuickPdfException("Некорректный путь PDF.");

        string tempPath = Path.Combine(directory, "QuickPDF_" + Guid.NewGuid().ToString("N") + ".pdf");
        using PlotInfo plotInfo = new()
        {
            Layout = LayoutManager.Current.GetLayoutId(LayoutManager.Current.CurrentLayout),
            OverrideSettings = settings
        };

        ValidatePlotInfo(plotInfo, matchingPolicy, log);
        verify(plotInfo.ValidatedSettings ?? settings);
        log.Debug("QUICKPDF validated settings verified");

        try
        {
            using PlotEngine engine = PlotFactory.CreatePublishEngine();
            using PlotPageInfo pageInfo = new();
            log.Debug("QUICKPDF plot engine starting: {TemporaryPdfPath}", tempPath);
            engine.BeginPlot(null, null);
            engine.BeginDocument(plotInfo, document.Name, null, 1, true, tempPath);
            engine.BeginPage(pageInfo, plotInfo, true, null);
            engine.BeginGenerateGraphics(null);
            engine.EndGenerateGraphics(null);
            engine.EndPage(null);
            engine.EndDocument(null);
            engine.EndPlot(null);

            PdfFilePublication.Commit(tempPath, pdfPath);
            log.Debug("QUICKPDF plot engine completed: {PdfPath}", pdfPath);
        }
        finally
        {
            TryDelete(tempPath, document.Editor);
        }
    }

    private static void ValidatePlotInfo(PlotInfo plotInfo, MatchingPolicy matchingPolicy, ILogger log)
    {
        using PlotInfoValidator validator = new()
        {
            MediaMatchingPolicy = matchingPolicy,
            MediaMatchingThreshold = 0
        };
        if (matchingPolicy is MatchingPolicy.MatchEnabledCustom or MatchingPolicy.MatchEnabledTemporaryCustom)
        {
            int customResult = validator.IsCustomPossible(plotInfo);
            log.Debug("QUICKPDF custom media capability result: {CustomMediaResult}", customResult);
            if (customResult != 0)
            {
                throw new QuickPdfException(
                    "Плоттер не может создать пользовательский формат (код " + customResult + ").");
            }
        }

        log.Debug("QUICKPDF validating plot info: matching={MatchingPolicy}", matchingPolicy);
        try
        {
            validator.Validate(plotInfo);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
            when (matchingPolicy == MatchingPolicy.MatchEnabledTemporaryCustom &&
                  ex.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.InvalidInput)
        {
            log.Warning(
                "QUICKPDF temporary custom media validation failed ({ErrorStatus}); retrying with persistent custom media",
                ex.ErrorStatus);
            validator.MediaMatchingPolicy = MatchingPolicy.MatchEnabledCustom;
            validator.Validate(plotInfo);
        }

        log.Debug("QUICKPDF plot info validated");
    }

    public static void VerifyIso(
        PlotSettings settings,
        IsoMediaChoice expected,
        double needWidth,
        double needHeight,
        QuickPdfOptions options)
    {
        if (!string.Equals(settings.CanonicalMediaName, expected.CanonicalName, StringComparison.OrdinalIgnoreCase) ||
            settings.PlotRotation != expected.Rotation ||
            settings.PlotPaperUnits != PlotPaperUnit.Millimeters ||
            settings.PlotType != PlotAreaType.Window)
        {
            throw new QuickPdfException("Плоттер изменил формат, ориентацию или режим масштаба.");
        }

        IsoMediaPicker.GetPrintable(settings, out double printableWidth, out double printableHeight);
        if (settings.PlotRotation == PlotRotation.Degrees090)
        {
            (printableWidth, printableHeight) = (printableHeight, printableWidth);
        }

        if (!IsoMedia.FitsMm(printableWidth, needWidth) || !IsoMedia.FitsMm(printableHeight, needHeight))
        {
            throw new QuickPdfException("Плоттер изменил формат: рамка не помещается в " + options.ScaleLabel + ".");
        }

        VerifyPlacement(settings, options);
    }

    public static void VerifyCustom(
        PlotSettings settings,
        double width,
        double height,
        Extents2d expectedWindow,
        string expectedStyle,
        QuickPdfOptions options)
    {
        Point2d paper = settings.PlotPaperSize;
        Extents2d margins = settings.PlotPaperMargins;
        CustomScale scale = settings.CustomPrintScale;
        double actualScale = scale.Denominator == 0 ? 0 : scale.Numerator / scale.Denominator;
        ExactPaper.Validate(
            width,
            height,
            paper.X,
            paper.Y,
            [margins.MinPoint.X, margins.MinPoint.Y, margins.MaxPoint.X, margins.MaxPoint.Y],
            options.ScaleRatio,
            actualScale,
            options.ScaleLabel);

        if (settings.PlotPaperUnits != PlotPaperUnit.Millimeters ||
            settings.PlotRotation != PlotRotation.Degrees000 ||
            settings.PlotType != PlotAreaType.Window ||
            !settings.PlotPlotStyles ||
            settings.ShadePlot != PlotSettingsShadePlotType.AsDisplayed ||
            settings.ShadePlotResLevel != ShadePlotResLevel.Normal)
        {
            throw new QuickPdfException(
                "Ожидались масштаб " + options.ScaleModeLabel +
                ", нулевой сдвиг, без центрирования и неизменные единицы, поворот, стиль и качество.");
        }

        VerifyPlacement(settings, options);

        if (settings.PlotWindowArea != expectedWindow ||
            !string.Equals(settings.CurrentStyleSheet, expectedStyle, StringComparison.OrdinalIgnoreCase))
        {
            throw new QuickPdfException("Драйвер изменил окно печати или таблицу стилей. Печать отменена.");
        }
    }

    private static void VerifyPlacement(PlotSettings settings, QuickPdfOptions options)
    {
        if (options.StdScaleOrNull is { } stdScale)
        {
            if (!settings.UseStandardScale || settings.StdScaleType != stdScale)
            {
                throw new QuickPdfException("Плоттер изменил стандартный масштаб " + options.ScaleLabel + ".");
            }

            return;
        }

        // Произвольный масштаб 1:N (1:200, 1:500): проверяем отношение напрямую.
        if (settings.UseStandardScale)
        {
            throw new QuickPdfException("Плоттер изменил масштаб " + options.ScaleLabel + " на стандартный.");
        }

        CustomScale scale = settings.CustomPrintScale;
        double actualScale = scale.Denominator == 0 ? 0 : scale.Numerator / scale.Denominator;
        if (!ExactPaper.IsFinitePositive(actualScale) ||
            Abs(actualScale / options.ScaleRatio - 1.0) > ExactPaper.ScaleRatioTolerance)
        {
            throw new QuickPdfException("Плоттер изменил масштаб " + options.ScaleLabel + ".");
        }

        if (settings.PlotCentered ||
            Abs(settings.PlotOrigin.X) > ExactPaper.OriginTolerance ||
            Abs(settings.PlotOrigin.Y) > ExactPaper.OriginTolerance)
        {
            throw new QuickPdfException("Плоттер изменил нулевой сдвиг или включил центрирование.");
        }
    }

    private static void TryDelete(string path, Autodesk.AutoCAD.EditorInput.Editor editor)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.WriteMessage("\nQuickPDF cleanup: " + ex.Message);
        }
    }
}
