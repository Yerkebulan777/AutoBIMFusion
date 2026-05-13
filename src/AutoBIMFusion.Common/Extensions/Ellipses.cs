namespace AutoBIMFusion.Common.Extensions;

public static class EllipsesExtensions
{
    public static bool IsClockwise(this Ellipse ellipse)
    {
        var Start = ellipse.StartParam;
        var End = ellipse.EndParam;
        var Dif = End - Start;
        if (Dif == 0) return false;
        var Step = Dif / 4;

        var pt1 = ellipse.GetPointAtParam(Start + Step * 1);
        var pt2 = ellipse.GetPointAtParam(Start + Step * 2);
        var pt3 = ellipse.GetPointAtParam(Start + Step * 3);

        return Clockwise(pt1, pt2, pt3);
    }

    private static bool Clockwise(Point3d p1, Point3d p2, Point3d p3)
    {
        return (p2.X - p1.X) * (p3.Y - p1.Y) - (p2.Y - p1.Y) * (p3.X - p1.X) < 1e-8;
    }

    public static Polyline ToPolyline(this Ellipse ellipse, int NumberOfVertices = 36)
    {
        var poly = new Polyline();
        if (ellipse.StartAngle == ellipse.EndAngle) return poly;
        var angle = ellipse.StartAngle;
        double angleSum = 0;
        var angleStep = PI / (NumberOfVertices / 2);

        var vertexIndex = 0;

        var stop = false;

        while (true)
        {
            var vector = ellipse.MajorAxis * Cos(angle) + ellipse.MinorAxis * Sin(angle);
            var CurrentPt = new Point3d(ellipse.Center.X + vector.X, ellipse.Center.Y + vector.Y, ellipse.Center.Z);

            if (vertexIndex > 0)
            {
                var PreviousPt = poly.GetPoint3dAt(vertexIndex - 1);
                Vector3d LineVector;
                if (ellipse.IsClockwise())
                    LineVector = CurrentPt.GetVectorTo(PreviousPt);
                else
                    LineVector = PreviousPt.GetVectorTo(CurrentPt);

                var PtOnCurve = ellipse.GetClosestPointTo(PreviousPt.GetMiddlePoint(CurrentPt),
                    new Vector3d(-LineVector.Y, LineVector.X, 0), false);
                poly.SetBulgeAt(poly.NumberOfVertices - 1, PtOnCurve.GetPassingThroughBulgeFrom(PreviousPt, CurrentPt));
            }

            poly.AddVertex(CurrentPt);
            vertexIndex++;

            if (stop) break;

            angle += angleStep;
            if (angle >= 2 * PI) angle -= 2 * PI;
            angleSum += angleStep;

            if (ellipse.StartAngle < ellipse.EndAngle && angleSum >= ellipse.EndAngle - ellipse.StartAngle)
            {
                angle = ellipse.EndAngle;
                stop = true;
            }

            if (ellipse.StartAngle > ellipse.EndAngle && angleSum >= 2 * PI + ellipse.EndAngle - ellipse.StartAngle)
            {
                angle = ellipse.EndAngle;
                stop = true;
            }
        }

        return poly;
    }
}
