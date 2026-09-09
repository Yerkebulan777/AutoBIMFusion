using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AutoBIMFusion.QuickPdf.Plotting;

/// <summary>
///     Рамка печати: UCS-ось как в LISP getpoint/getcorner, затем DCS для окна плоттера.
/// </summary>
internal static class PlotWindowConverter
{
    public static Extents2d ToPlotWindow(Editor editor, Point3d first, Point3d second)
    {
        Matrix3d ucs = editor.CurrentUserCoordinateSystem;
        Point3d ucs1 = first.TransformBy(ucs.Inverse());
        Point3d ucs2 = second.TransformBy(ucs.Inverse());
        Point3d[] ucsCorners =
        [
            ucs1,
            ucs2,
            new Point3d(ucs1.X, ucs2.Y, ucs1.Z),
            new Point3d(ucs2.X, ucs1.Y, ucs1.Z)
        ];

        Matrix3d ucsToDcs = UcsToDcs(editor, ucs);
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        foreach (Point3d corner in ucsCorners)
        {
            Point3d dcs = corner.TransformBy(ucsToDcs);
            minX = Min(minX, dcs.X);
            minY = Min(minY, dcs.Y);
            maxX = Max(maxX, dcs.X);
            maxY = Max(maxY, dcs.Y);
        }

        return new Extents2d(minX, minY, maxX, maxY);
    }

    private static Matrix3d UcsToDcs(Editor editor, Matrix3d ucs)
    {
        ViewTableRecord view = editor.GetCurrentView();
        Vector3d viewDir = view.ViewDirection.GetNormal();
        Point3d target = view.Target;
        Matrix3d dcsToWcs =
            Matrix3d.Rotation(-view.ViewTwist, viewDir, target) *
            Matrix3d.Displacement(target - Point3d.Origin) *
            Matrix3d.PlaneToWorld(viewDir);
        return dcsToWcs.Inverse() * ucs;
    }
}
