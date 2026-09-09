using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using AutoBIMFusion.QuickPdf.Media;
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
        Action<PlotSettings> verify)
    {
        if (File.Exists(pdfPath))
        {
            throw new QuickPdfException("Не удалось опубликовать PDF: файл назначения уже существует.");
        }

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

        using PlotInfoValidator validator = new()
        {
            MediaMatchingPolicy = matchingPolicy,
            MediaMatchingThreshold = 0
        };
        validator.Validate(plotInfo);
        verify(plotInfo.ValidatedSettings ?? settings);

        try
        {
            using PlotEngine engine = PlotFactory.CreatePublishEngine();
            using PlotPageInfo pageInfo = new();
            engine.BeginPlot(null, null);
            engine.BeginDocument(plotInfo, document.Name, null, 1, true, tempPath);
            engine.BeginPage(pageInfo, plotInfo, true, null);
            engine.BeginGenerateGraphics(null);
            engine.EndGenerateGraphics(null);
            engine.EndPage(null);
            engine.EndDocument(null);
            engine.EndPlot(null);

            if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            {
                throw new QuickPdfException("Плоттер не создал непустой PDF.");
            }

            File.Move(tempPath, pdfPath);
        }
        finally
        {
            TryDelete(tempPath, document.Editor);
        }
    }

    public static void VerifyIso(PlotSettings settings, IsoMediaChoice expected, double needWidth, double needHeight)
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
            throw new QuickPdfException("Плоттер изменил формат: рамка не помещается в 1:100.");
        }

        VerifyPlacement(settings);
    }

    public static void VerifyCustom(PlotSettings settings, double width, double height, Extents2d expectedWindow, string expectedStyle)
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
            ExactPaper.WantedScale,
            actualScale);

        if (settings.PlotPaperUnits != PlotPaperUnit.Millimeters ||
            settings.PlotRotation != PlotRotation.Degrees000 ||
            settings.PlotType != PlotAreaType.Window ||
            !settings.PlotPlotStyles ||
            settings.ShadePlot != PlotSettingsShadePlotType.AsDisplayed ||
            settings.ShadePlotResLevel != ShadePlotResLevel.Normal)
        {
            throw new QuickPdfException("Ожидались стандарт 1:100, нулевой сдвиг, без центрирования и неизменные единицы, поворот, стиль и качество.");
        }

        VerifyPlacement(settings);

        if (settings.PlotWindowArea != expectedWindow ||
            !string.Equals(settings.CurrentStyleSheet, expectedStyle, StringComparison.OrdinalIgnoreCase))
        {
            throw new QuickPdfException("Драйвер изменил окно печати или таблицу стилей. Печать отменена.");
        }
    }

    private static void VerifyPlacement(PlotSettings settings)
    {
        if (!settings.UseStandardScale || settings.StdScaleType != StdScaleType.StdScale1To100)
        {
            throw new QuickPdfException("Плоттер изменил стандартный масштаб 1:100.");
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
