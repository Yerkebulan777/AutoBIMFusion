using Autodesk.AutoCAD.DatabaseServices;
using AutoBIMFusion.QuickPdf.Media;

namespace AutoBIMFusion.QuickPdf.Plotting;

internal readonly record struct IsoMediaChoice(string CanonicalName, PlotRotation Rotation);

/// <summary>
///     Наименьший ISO-лист, в который рамка влезает на 1:100.
/// </summary>
internal static class IsoMediaPicker
{
    public static IsoMediaChoice? Pick(PlotSettings settings, PlotSettingsValidator validator, double needWidth, double needHeight)
    {
        bool landscape = needWidth >= needHeight;
        IsoMediaSelection? selection = IsoMediaSelector.Pick(
            PlotLists.Names(validator.GetCanonicalMediaNameList(settings)),
            needWidth,
            needHeight,
            mediaName =>
            {
                if (!TryProbe(settings, validator, mediaName, landscape, needWidth, needHeight,
                        out IsoMediaChoice choice, out double area))
                {
                    return null;
                }

                return new IsoMediaProbeResult(choice.Rotation == PlotRotation.Degrees090, area);
            });
        if (selection is not { } selected)
        {
            return null;
        }

        PlotRotation rotation = selected.Rotated ? PlotRotation.Degrees090 : PlotRotation.Degrees000;
        validator.SetCanonicalMediaName(settings, selected.CanonicalName);
        validator.SetPlotPaperUnits(settings, PlotPaperUnit.Millimeters);
        validator.SetPlotRotation(settings, rotation);
        return new IsoMediaChoice(selected.CanonicalName, rotation);
    }

    internal static void GetPrintable(PlotSettings settings, out double width, out double height)
    {
        Point2d paper = settings.PlotPaperSize;
        Extents2d margins = settings.PlotPaperMargins;
        width = paper.X - margins.MinPoint.X - margins.MaxPoint.X;
        height = paper.Y - margins.MinPoint.Y - margins.MaxPoint.Y;
    }

    private static bool TryProbe(
        PlotSettings settings,
        PlotSettingsValidator validator,
        string mediaName,
        bool landscape,
        double needWidth,
        double needHeight,
        out IsoMediaChoice choice,
        out double area)
    {
        choice = default;
        area = 0;
        try
        {
            validator.SetCanonicalMediaName(settings, mediaName);
            validator.SetPlotPaperUnits(settings, PlotPaperUnit.Millimeters);
            validator.SetPlotRotation(settings, PlotRotation.Degrees000);

            Point2d paper = settings.PlotPaperSize;
            GetPrintable(settings, out double printableWidth, out double printableHeight);
            PlotRotation rotation = PlotRotation.Degrees000;
            double paperWidth = paper.X;
            double paperHeight = paper.Y;
            if (landscape != paperWidth >= paperHeight)
            {
                rotation = PlotRotation.Degrees090;
                (paperWidth, paperHeight) = (paperHeight, paperWidth);
                (printableWidth, printableHeight) = (printableHeight, printableWidth);
            }

            if (!IsoMedia.PaperMatches(paperWidth, paperHeight, needWidth, needHeight) ||
                !IsoMedia.FitsMm(printableWidth, needWidth) ||
                !IsoMedia.FitsMm(printableHeight, needHeight))
            {
                return false;
            }

            choice = new IsoMediaChoice(mediaName, rotation);
            area = paperWidth * paperHeight;
            return true;
        }
        catch (Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
