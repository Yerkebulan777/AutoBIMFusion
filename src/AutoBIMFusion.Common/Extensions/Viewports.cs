using Viewport = Autodesk.AutoCAD.DatabaseServices.Viewport;

namespace AutoBIMFusion.Common.Extensions;

public static class ViewportsExtensions
{
    public static Point3d GetViewCenterWcs(this Viewport vp)
    {
        return new Point3d(vp.ViewCenter.X, vp.ViewCenter.Y, 0).TransformBy(vp.GetDcsToWcsMatrix());
    }

    public static double ResolveCustomScale(this Viewport vp)
    {
        return vp.CustomScale > 0 ? vp.CustomScale : vp.Height / Max(vp.ViewHeight, 1e-9);
    }

    public static Extents3d ComputeModelWindow(this Viewport vp)
    {
        var aspectRatio = vp.Width / Max(vp.Height, 1e-9);
        var widthModel = vp.ViewHeight * aspectRatio;
        var heightModel = vp.ViewHeight;

        var halfW = widthModel / 2.0;
        var halfH = heightModel / 2.0;

        Matrix3d dcsToWcs = vp.GetDcsToWcsMatrix();
        Point2d vc = vp.ViewCenter;

        Point3d[] corners =
        [
            new Point3d(vc.X - halfW, vc.Y - halfH, 0).TransformBy(dcsToWcs),
            new Point3d(vc.X + halfW, vc.Y - halfH, 0).TransformBy(dcsToWcs),
            new Point3d(vc.X + halfW, vc.Y + halfH, 0).TransformBy(dcsToWcs),
            new Point3d(vc.X - halfW, vc.Y + halfH, 0).TransformBy(dcsToWcs)
        ];

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;

        foreach (Point3d p in corners)
        {
            minX = Min(minX, p.X);
            maxX = Max(maxX, p.X);
            minY = Min(minY, p.Y);
            maxY = Max(maxY, p.Y);
            minZ = Min(minZ, p.Z);
            maxZ = Max(maxZ, p.Z);
        }

        return new Extents3d(new Point3d(minX, minY, minZ), new Point3d(maxX, maxY, maxZ));
    }

    private static Matrix3d GetDcsToWcsMatrix(this Viewport vp)
    {
        return Matrix3d.Displacement(vp.ViewTarget.GetAsVector()) *
               Matrix3d.PlaneToWorld(vp.ViewDirection) *
               Matrix3d.Rotation(vp.TwistAngle, Vector3d.ZAxis, Point3d.Origin);
    }
}
