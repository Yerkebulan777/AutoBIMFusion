namespace SioForgeCAD.Commun.Extensions;

public static class CircularArc2dExtensions
{
    public static double GetArea(this CircularArc2d arc)
    {
        var rad = arc.Radius;
        var ang = arc.IsClockWise ? arc.StartAngle - arc.EndAngle : arc.EndAngle - arc.StartAngle;
        return rad * rad * (ang - Sin(ang)) / 2.0;
    }
}
