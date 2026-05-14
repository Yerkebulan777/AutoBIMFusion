namespace AutoBIMFusion.Common.Extensions;

public static class CircularArcExtensions
{
    public static Curve ToCircleOrArc(this CircularArc2d circularArc)
    {
        if (circularArc.EndPoint.IsEqualTo(circularArc.StartPoint) && circularArc.Radius > 0)
        {
            Circle Circle = new(circularArc.Center.ToPoint3d(), Vector3d.YAxis, circularArc.Radius);
            return Circle;
        }

        double startAngle = circularArc.IsClockWise ? -circularArc.EndAngle : circularArc.StartAngle;
        double endAngle = circularArc.IsClockWise ? -circularArc.StartAngle : circularArc.EndAngle;
        Arc arc = new(
            new Point3d(circularArc.Center.X, circularArc.Center.Y, 0.0),
            circularArc.Radius,
            circularArc.ReferenceVector.Angle + startAngle,
            circularArc.ReferenceVector.Angle + endAngle);
        return arc;
    }

    public static Curve ToCircleOrArc(this CircularArc3d circArc)
    {
        Point3d center = circArc.Center;
        Vector3d normal = circArc.Normal;
        Vector3d referenceVector = circArc.ReferenceVector;
        Plane plane = new(center, normal);
        double num = referenceVector.AngleOnPlane(plane);

        return circArc.EndPoint.IsEqualTo(circArc.StartPoint) && circArc.Radius > 0
            ? new Circle(circArc.Center, Vector3d.YAxis, circArc.Radius)
            : new Arc(center, normal, circArc.Radius, circArc.StartAngle + num, circArc.EndAngle + num);
    }
}
