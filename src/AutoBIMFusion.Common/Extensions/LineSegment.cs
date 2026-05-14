namespace AutoBIMFusion.Common.Extensions;

public static class LineSegmentExtensions
{
    public static Line ToLine(this LineSegment2d lineSegment2D)
    {
        Line line = new(new Point3d(lineSegment2D.StartPoint.X, lineSegment2D.StartPoint.Y, 0.0),
            new Point3d(lineSegment2D.EndPoint.X, lineSegment2D.EndPoint.Y, 0.0));
        return line;
    }

    public static Line ToLine(this LineSegment3d lineSegment3D)
    {
        Line line = new(
            new Point3d(lineSegment3D.StartPoint.X, lineSegment3D.StartPoint.Y, lineSegment3D.StartPoint.Z),
            new Point3d(lineSegment3D.EndPoint.X, lineSegment3D.EndPoint.Y, lineSegment3D.StartPoint.Z));
        return line;
    }
}
