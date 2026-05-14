using Autodesk.AutoCAD.GraphicsInterface;
using static Autodesk.AutoCAD.DatabaseServices.GripData;

namespace AutoBIMFusion.Common.Extensions;

public static class ViewportDrawExtensions
{
    public static bool DrawGrip(this ViewportDraw worldDraw, Point3dCollection points, DrawType type)
    {
        worldDraw.SubEntityTraits.FillType = FillType.FillAlways;
        if (type == DrawType.HoverGrip)
        {
            //GRIPHOVER (System Variable) -> Obsolete
            worldDraw.SubEntityTraits.Color = 11;
        }
        else if (type == DrawType.HotGrip)
        {
            //GRIPHOT (System Variable)
            worldDraw.SubEntityTraits.Color = 12;
        }
        else
        {
            //GRIPCONTOUR
            worldDraw.SubEntityTraits.Color = 150;
        }

        _ = worldDraw.Geometry.Polygon(points);

        if (type == DrawType.WarmGrip)
        {
            worldDraw.SubEntityTraits.FillType = FillType.FillNever;
            worldDraw.SubEntityTraits.Color = 251;
            _ = worldDraw.Geometry.Polygon(points);
        }

        return true;
    }

    public static double GetGripHeight(this ViewportDraw worldDraw, Point3d point, int gripSizeInPixels)
    {
        Point2d unit = worldDraw.Viewport.GetNumPixelsInUnitSquare(point);
        return 2.5 * gripSizeInPixels / unit.X;
    }
}
