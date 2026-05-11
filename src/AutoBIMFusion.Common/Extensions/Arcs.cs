namespace SioForgeCAD.Commun.Extensions;

public static class ArcsExtensions
{
    /// <summary>
    ///     Gets arc bulge.
    /// </summary>
    /// <param name="arc">The arc.</param>
    /// <param name="start">The start point.</param>
    /// <returns>The bulge.</returns>
    public static double GetArcBulge(this Arc arc, Point3d start)
    {
        double bulge;
        var angle = arc.EndAngle - arc.StartAngle;
        if (angle < 0) angle += PI * 2;
        if (arc.Normal.Z > 0)
            bulge = Tan(angle / 4);
        else
            bulge = -Tan(angle / 4);
        if (start == arc.EndPoint) bulge = -bulge;
        return bulge;
    }


    public static CircularArc2d ToCircularArc2d(this Arc arc)
    {
        var start = new Point2d(arc.StartPoint.X, arc.StartPoint.Y);
        var end = new Point2d(arc.EndPoint.X, arc.EndPoint.Y);

        // Clockwise : invert
        var isClockwise = arc.Normal.Z < 0;

        var deltaAngle = isClockwise ? arc.StartAngle - arc.EndAngle : arc.EndAngle - arc.StartAngle;
        if (deltaAngle <= 0) deltaAngle += 2 * PI;

        var midAngle = isClockwise
            ? arc.StartAngle - deltaAngle / 2
            : arc.StartAngle + deltaAngle / 2;

        // Convert to [0, 2π] 
        midAngle = (midAngle + 2 * PI) % (2 * PI);

        // Arc median point
        var midX = arc.Center.X + arc.Radius * Cos(midAngle);
        var midY = arc.Center.Y + arc.Radius * Sin(midAngle);
        var mid = new Point2d(midX, midY);

        return new CircularArc2d(start, mid, end);
    }
}
