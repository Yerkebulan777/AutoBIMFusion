namespace AutoBIMFusion.Common.Extensions;

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
        double angle = arc.EndAngle - arc.StartAngle;
        if (angle < 0)
        {
            angle += PI * 2;
        }

        bulge = arc.Normal.Z > 0 ? Tan(angle / 4) : -Tan(angle / 4);
        if (start == arc.EndPoint)
        {
            bulge = -bulge;
        }

        return bulge;
    }


    public static CircularArc2d ToCircularArc2d(this Arc arc)
    {
        Point2d start = new(arc.StartPoint.X, arc.StartPoint.Y);
        Point2d end = new(arc.EndPoint.X, arc.EndPoint.Y);

        // По часовой стрелке: инвертируем
        bool isClockwise = arc.Normal.Z < 0;

        double deltaAngle = isClockwise ? arc.StartAngle - arc.EndAngle : arc.EndAngle - arc.StartAngle;
        if (deltaAngle <= 0)
        {
            deltaAngle += 2 * PI;
        }

        double midAngle = isClockwise
            ? arc.StartAngle - (deltaAngle / 2)
            : arc.StartAngle + (deltaAngle / 2);

        // Преобразуем в [0, 2π]
        midAngle = (midAngle + (2 * PI)) % (2 * PI);

        // Arc median point
        double midX = arc.Center.X + (arc.Radius * Cos(midAngle));
        double midY = arc.Center.Y + (arc.Radius * Sin(midAngle));
        Point2d mid = new(midX, midY);

        return new CircularArc2d(start, mid, end);
    }
}
