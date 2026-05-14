namespace AutoBIMFusion.Common.Extensions;

public static class EllipsesExtensions
{
    public static bool IsClockwise(this Ellipse ellipse)
    {
        double Start = ellipse.StartParam;
        double End = ellipse.EndParam;
        double Dif = End - Start;
        if (Dif == 0)
        {
            return false;
        }

        double Step = Dif / 4;

        Point3d pt1 = ellipse.GetPointAtParam(Start + (Step * 1));
        Point3d pt2 = ellipse.GetPointAtParam(Start + (Step * 2));
        Point3d pt3 = ellipse.GetPointAtParam(Start + (Step * 3));

        return Clockwise(pt1, pt2, pt3);
    }

    private static bool Clockwise(Point3d p1, Point3d p2, Point3d p3)
    {
        return ((p2.X - p1.X) * (p3.Y - p1.Y)) - ((p2.Y - p1.Y) * (p3.X - p1.X)) < 1e-8;
    }

    public static Polyline ToPolyline(this Ellipse ellipse, int NumberOfVertices = 36)
    {
        Polyline poly = new();
        if (ellipse.StartAngle == ellipse.EndAngle)
        {
            return poly;
        }

        double angle = ellipse.StartAngle;
        double angleSum = 0;
        double angleStep = PI / (NumberOfVertices / 2);

        int vertexIndex = 0;

        bool stop = false;

        while (true)
        {
            Vector3d vector = (ellipse.MajorAxis * Cos(angle)) + (ellipse.MinorAxis * Sin(angle));
            Point3d CurrentPt = new(ellipse.Center.X + vector.X, ellipse.Center.Y + vector.Y, ellipse.Center.Z);

            if (vertexIndex > 0)
            {
                Point3d PreviousPt = poly.GetPoint3dAt(vertexIndex - 1);
                Vector3d LineVector = ellipse.IsClockwise() ? CurrentPt.GetVectorTo(PreviousPt) : PreviousPt.GetVectorTo(CurrentPt);
                Point3d PtOnCurve = ellipse.GetClosestPointTo(PreviousPt.GetMiddlePoint(CurrentPt),
                    new Vector3d(-LineVector.Y, LineVector.X, 0), false);
                poly.SetBulgeAt(poly.NumberOfVertices - 1, PtOnCurve.GetPassingThroughBulgeFrom(PreviousPt, CurrentPt));
            }

            poly.AddVertex(CurrentPt);
            vertexIndex++;

            if (stop)
            {
                break;
            }

            angle += angleStep;
            if (angle >= 2 * PI)
            {
                angle -= 2 * PI;
            }

            angleSum += angleStep;

            if (ellipse.StartAngle < ellipse.EndAngle && angleSum >= ellipse.EndAngle - ellipse.StartAngle)
            {
                angle = ellipse.EndAngle;
                stop = true;
            }

            if (ellipse.StartAngle > ellipse.EndAngle && angleSum >= (2 * PI) + ellipse.EndAngle - ellipse.StartAngle)
            {
                angle = ellipse.EndAngle;
                stop = true;
            }
        }

        return poly;
    }
}
