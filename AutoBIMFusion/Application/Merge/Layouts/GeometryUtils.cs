namespace AutoBIMFusion.Application.Merge.Layouts;

internal static class GeometryUtils
{
    internal static Extents3d? TryGetExtents(Entity ent)
    {
        try
        {
            return ent.GeometricExtents;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return null;
        }
    }

    internal static bool AabbIntersect(Extents3d a, Extents3d b)
    {
        return a.MinPoint.X <= b.MaxPoint.X
            && a.MaxPoint.X >= b.MinPoint.X
            && a.MinPoint.Y <= b.MaxPoint.Y
            && a.MaxPoint.Y >= b.MinPoint.Y;
    }

    internal static string FormatExtents(Extents3d ext)
    {
        return $"[{FormatPoint(ext.MinPoint)} -> {FormatPoint(ext.MaxPoint)}]";
    }

    internal static string FormatPoint(Point3d p)
    {
        return $"({p.X:F3}, {p.Y:F3}, {p.Z:F3})";
    }

    internal static Extents3d Union(Extents3d a, Extents3d b)
    {
        Point3d min = new(
            Min(a.MinPoint.X, b.MinPoint.X),
            Min(a.MinPoint.Y, b.MinPoint.Y),
            Min(a.MinPoint.Z, b.MinPoint.Z));
        Point3d max = new(
            Max(a.MaxPoint.X, b.MaxPoint.X),
            Max(a.MaxPoint.Y, b.MaxPoint.Y),
            Max(a.MaxPoint.Z, b.MaxPoint.Z));
        return new Extents3d(min, max);
    }
}
