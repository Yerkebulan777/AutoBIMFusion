using AutoBIMFusion.Common.Extensions;

namespace AutoBIMFusion.Common.Mist.Geometry;

public static class PerpendicularPoint
{
    // Fonction pour trouver le point d'intersection entre une ligne et un vecteur
    public static Point3d FindIntersection(Point3d startPoint, Vector3d vector, Line line)
    {
        var t = ((line.EndPoint.X - line.StartPoint.X) * (startPoint.Y - line.StartPoint.Y) -
                 (line.EndPoint.Y - line.StartPoint.Y) * (startPoint.X - line.StartPoint.X)) /
                ((line.EndPoint.Y - line.StartPoint.Y) * vector.X - (line.EndPoint.X - line.StartPoint.X) * vector.Y);

        // Calculer le point d'intersection
        var intersectionPoint = startPoint + t * vector;
        return intersectionPoint;
    }

    public static Vector3d GetPerpendicularLinePointProjectionVector(Point3d LineStartPointSCG, Point3d LineEndPointSCG,
        Point3d PerpendicularPointCurrentSCU)
    {
        var PolyStart = new Points(LineStartPointSCG).SCG;
        var PolyEnd = new Points(LineEndPointSCG).SCG;
        // Calculer la pente de la polyligne
        var m_AB = PolyEnd.X != PolyStart.X
            ? (PolyEnd.Y - PolyStart.Y) / (PolyEnd.X - PolyStart.X)
            : double.PositiveInfinity;
        // Calculer la pente de la ligne perpendiculaire
        var m_perp = m_AB != 0 ? -1 / m_AB : double.PositiveInfinity;
        // Appliquer la transformation au vecteur directeur de la ligne perpendiculaire
        var perpVector = m_perp != double.PositiveInfinity ? new Vector3d(1, m_perp, 0) : new Vector3d(0, 1, 0);
        return PerpendicularPointCurrentSCU - (PerpendicularPointCurrentSCU + perpVector);
    }

    public static List<Line> GetListOfPerpendicularLinesFromPoint(Points BasePoint, Polyline TargetPolyline,
        bool CheckForSegmentIntersections = true)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        var ed = doc.Editor;

        var PerpendicularLinesCollection = new List<Line>();
        for (var PolylineSegmentIndex = 0;
             PolylineSegmentIndex < TargetPolyline.GetReelNumberOfVertices();
             PolylineSegmentIndex++)
        {
            var PolylineSegment = TargetPolyline.GetSegmentAt(PolylineSegmentIndex);

            var PerpendicularVectorLine = GetPerpendicularLinePointProjectionVector(PolylineSegment.StartPoint,
                PolylineSegment.EndPoint, BasePoint.SCG);

            using (var SegmentLine = new Line(PolylineSegment.StartPoint, PolylineSegment.EndPoint))
            {
                var IntersectionPoint = FindIntersection(BasePoint.SCG, PerpendicularVectorLine, SegmentLine);
                if (IntersectionPoint == Point3d.Origin) continue;
                var PerpendicularLine = new Line(BasePoint.SCG, IntersectionPoint);
                var IsLineIsIntersectingOtherSegments = CheckForSegmentIntersections &&
                                                        CheckIfLineIsIntersectingOtherSegments(TargetPolyline,
                                                            PerpendicularLine, PolylineSegmentIndex);
                if (!IsLineIsIntersectingOtherSegments && SegmentLine.IsLinePassesThroughPoint(IntersectionPoint))
                    PerpendicularLinesCollection.Add(PerpendicularLine);
                else
                    PerpendicularLine.Dispose();
            }
        }

        return PerpendicularLinesCollection.OrderBy(line => line.Length).ToList();
    }

    public static bool CheckIfLineIsIntersectingOtherSegments(Polyline TargetPolyline, Line PerpendicularLine,
        int CurrentIndex = -1)
    {
        for (var PolylineSegmentIndex = 0;
             PolylineSegmentIndex < TargetPolyline.GetReelNumberOfVertices();
             PolylineSegmentIndex++)
        {
            if (PolylineSegmentIndex == CurrentIndex) continue;
            var PolylineSegment = TargetPolyline.GetSegmentAt(PolylineSegmentIndex);
            using (var SegmentLineIntersectTest = new Line(PolylineSegment.StartPoint, PolylineSegment.EndPoint))
            {
                return SegmentLineIntersectTest.IsCutting(PerpendicularLine);
            }
        }

        return false;
    }
}
