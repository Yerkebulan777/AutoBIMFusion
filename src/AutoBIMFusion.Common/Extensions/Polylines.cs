using AutoBIMFusion.Common.Mist;
using AutoBIMFusion.Common.Mist.Geometry;
using AutoBIMFusion.Common.Mist.Geometry.PolygonOperations;
using System.Diagnostics;

namespace AutoBIMFusion.Common.Extensions;

public static class PolylinesExtensions
{
    public enum PolylineSide
    {
        Right,
        Left,
        Collinear
    }

    public static int GetReelNumberOfVertices(this Polyline TargetPolyline)
    {
        if (TargetPolyline?.IsDisposed == true)
        {
            return 0;
        }

        int NumberOfVertices = TargetPolyline.NumberOfVertices - 1;
        if (TargetPolyline.Closed)
        {
            NumberOfVertices++;
        }

        return NumberOfVertices;
    }

    public static Polyline GetPolylineFromPoints(this IEnumerable<Points> listOfPoints)
    {
        Polyline polyline = new();
        foreach (Points point in listOfPoints)
        {
            polyline.AddVertexAt(polyline.NumberOfVertices, point.SCG.ToPoint2d(), 0, 0, 0);
        }

        return polyline;
    }

    public static PolylineSide CheckPointSide(this Polyline BasePolyline, Point3d TargetPoint)
    {
        for (int segmentIndex = 0; segmentIndex < BasePolyline.NumberOfVertices - 1; segmentIndex++)
        {
            Point3d startPoint = BasePolyline.GetPoint3dAt(segmentIndex);
            Point3d endPoint = BasePolyline.GetPoint3dAt(segmentIndex + 1);

            Vector2d polylineVector = new(endPoint.X - startPoint.X, endPoint.Y - startPoint.Y);
            Vector2d pointVector = new(TargetPoint.X - startPoint.X, TargetPoint.Y - startPoint.Y);

            //cross product
            double crossProduct = (polylineVector.X * pointVector.Y) - (polylineVector.Y * pointVector.X);

            if (crossProduct < 0)
            {
                //left
                return PolylineSide.Left;
            }

            if (crossProduct > 0)
            {
                // Right
                return PolylineSide.Right;
            }
        }

        //collinear
        return PolylineSide.Collinear;
    }

    public static bool IsAtLeftSide(this Polyline BasePolyline, Point3d TargetPoint)
    {
        return BasePolyline.CheckPointSide(TargetPoint) == PolylineSide.Left;
    }

    public static bool IsAtRightSide(this Polyline BasePolyline, Point3d TargetPoint)
    {
        return BasePolyline.CheckPointSide(TargetPoint) == PolylineSide.Right;
    }

    public static bool FixNormals(this Polyline polyline)
    {
        //Fix normals : should be 0,0,1 and its 0,0,-1. This happen when the polyline was drawn with the bottom up
        if (polyline.Normal == Vector3d.ZAxis.MultiplyBy(-1))
        {
            Debug.WriteLine("Correction de la normal d'une polyline");
            for (int i = 0; i < polyline.NumberOfVertices; i++)
            {
                //var acPlArc = acPlLwObj.GetArcSegmentAt(i);
                Point3d acPl3DPoint = polyline.GetPoint3dAt(i);
                Point2d acPl2DPointNew = new(acPl3DPoint.X, acPl3DPoint.Y);
                polyline.SetPointAt(i, acPl2DPointNew);
                polyline.SetBulgeAt(i, -polyline.GetBulgeAt(i));
            }

            polyline.Normal = Vector3d.ZAxis;
            return true;
        }

        return false;
    }

    public static void Flatten(this Polyline polyline)
    {
        for (int i = 0; i < polyline.NumberOfVertices; i++)
        {
            Point3d acPl3DPoint = polyline.GetPoint3dAt(i);
            Point2d acPl2DPointNew = new(acPl3DPoint.X, acPl3DPoint.Y);
            polyline.SetPointAt(i, acPl2DPointNew);
        }
    }

    public static bool HasAngle(this Polyline TargetPolyline, double DegreesTolerance)
    {
        for (int i = 0; i < TargetPolyline.NumberOfVertices - 2; i++)
        {
            Point2d pt1 = TargetPolyline.GetPoint2dAt(i);
            Point2d pt2 = TargetPolyline.GetPoint2dAt(i + 1);
            Point2d pt3 = TargetPolyline.GetPoint2dAt(i + 2);

            Vector2d v1 = pt2 - pt1;
            Vector2d v2 = pt3 - pt2;

            double angle = v1.GetAngleTo(v2) * (180.0 / PI);

            if (Abs(angle - 180) > DegreesTolerance)
            {
                return true;
            }
        }

        return false;
    }

    public static (Point3d StartPoint, Point3d EndPoint, double Bulge) GetSegmentAt(this Polyline TargetPolyline,
        int Index)
    {
        int NumberOfVertices = TargetPolyline.NumberOfVertices;
        double Bulge = TargetPolyline.GetBulgeAt(Index);
        Point3d PolylineSegmentStart = TargetPolyline.GetPoint3dAt(Index);
        Index++;
        if (Index >= NumberOfVertices)
        {
            Index = 0;
        }

        Point3d PolylineSegmentEnd = TargetPolyline.GetPoint3dAt(Index);
        return (PolylineSegmentStart, PolylineSegmentEnd, Bulge);
    }

    public static double GetArea(this Polyline pline)
    {
        double area = 0.0;
        if (pline.NumberOfVertices == 0)
        {
            return area;
        }

        int last = pline.NumberOfVertices - 1;
        Point2d p0 = pline.GetPoint2dAt(0);

        if (pline.GetBulgeAt(0) != 0.0)
        {
            area += pline.GetArcSegment2dAt(0).GetArea();
        }

        for (int i = 1; i < last; i++)
        {
            area += p0.GetArea(pline.GetPoint2dAt(i), pline.GetPoint2dAt(i + 1));
            if (pline.GetBulgeAt(i) != 0.0)
            {
                area += pline.GetArcSegment2dAt(i).GetArea();
            }
        }

        if (pline.GetBulgeAt(last) != 0.0 && pline.Closed)
        {
            area += pline.GetArcSegment2dAt(last).GetArea();
        }

        return area;
    }

    public static DBObjectCollection BreakAt(this Polyline poly, params Point3d[] points)
    {
        DoubleCollection DblCollection = [];
        foreach (Point3d point in points)
        {
            double param = poly.GetParamAtPointX(point);
            _ = DblCollection.Add(param);
            _ = DblCollection.Add(param);
        }

        return poly.GetSplitCurves(DblCollection);
    }

    public static void CleanupPolylines(this IEnumerable<Polyline> ListOfPolyline)
    {
        foreach (Polyline Line in ListOfPolyline)
        {
            Line.Cleanup();
        }
    }

    public static void Cleanup(this Polyline polyline)
    {
        int InverseCount = 0;

        void InversePoly()
        {
            InverseCount++;
            polyline.Inverse();
        }

        if (polyline == null)
        {
            return;
        }

        int vertexCount = polyline.NumberOfVertices;
        if (vertexCount <= 2)
        {
            return;
        }

        bool HasAVertexRemoved = true;
        while (HasAVertexRemoved)
        {
            InversePoly();
            HasAVertexRemoved = false;
            int index = 1;
            while (polyline.GetReelNumberOfVertices() > index)
            {
                Point3d lastPoint = polyline.GetPoint3dAt(index - 1);
                Point3d currentPoint = polyline.GetPoint3dAt(index);
                Point3d nextPoint = polyline.NumberOfVertices <= index + 1 ? polyline.StartPoint : polyline.GetPoint3dAt(index + 1);
                Vector2d vector1 = currentPoint.GetVectorTo(lastPoint).ToVector2d();
                Vector2d vector2 = nextPoint.GetVectorTo(currentPoint).ToVector2d();

                bool IsColinear = vector1.IsColinear(vector2, Generic.MediumTolerance) && vector1.Length > 0;
                bool HasBulgeLast = polyline.GetSegmentType(index - 1) == SegmentType.Arc;
                bool HasBulge = polyline.GetSegmentType(index) == SegmentType.Arc;
                bool IsDuplicateVertex = currentPoint.IsEqualTo(nextPoint, Generic.LowTolerance);
                if (IsColinear || IsDuplicateVertex)
                {
                    if (HasBulge && HasBulgeLast)
                    {
                        double lastBulge = polyline.GetBulgeAt(index - 1);
                        double curBulge = polyline.GetBulgeAt(index);
                        if (Abs(Abs(lastBulge) - Abs(curBulge)) < Generic.MediumTolerance.EqualVector)
                        {
                            if (index == 1 && IsColinear && Abs(vector1.Angle - vector2.Angle) >= PI)
                            {
                                polyline.RemoveVertexAt(index - 1);
                            }
                            else
                            {
                                polyline.RemoveVertexAt(index);
                            }

                            HasAVertexRemoved = true;
                        }
                        else
                        {
                            index++;
                        }
                    }
                    else
                    {
                        polyline.RemoveVertexAt(index);
                        HasAVertexRemoved = true;
                    }
                }
                else
                {
                    index++;
                }
            }

            index = 0;
            while (index < polyline.GetReelNumberOfVertices())
            {
                try
                {
                    (Point3d StartPoint, Point3d EndPoint, double Bulge) = polyline.GetSegmentAt(index);
                    if (StartPoint.IsEqualTo(EndPoint, Generic.LowTolerance))
                    {
                        polyline.RemoveVertexAt(index);
                        HasAVertexRemoved = true;
                    }
                    else
                    {
                        index++;
                    }
                }
                catch (Exception)
                {
                    index++;
                }
            }
        }

        if (InverseCount % 2 != 0)
        {
            InversePoly();
        }

        if (!polyline.Closed && polyline.StartPoint.IsEqualTo(polyline.EndPoint, Generic.LowTolerance))
        {
            if (polyline.NumberOfVertices > 3)
            {
                polyline.RemoveVertexAt(polyline.NumberOfVertices - 1);
                polyline.Closed = true;
            }
        }
    }

    public static void Inverse(this Polyline poly)
    {
        //https://www.keanw.com/2012/09/reversing-the-direction-of-an-autocad-polyline-using-net.html
        try
        {
            poly.ReverseCurve();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    public static IEnumerable<Point2d> GetPolyPoints(this Polyline poly)
    {
        for (int i = 0; i < poly.NumberOfVertices; i++)
        {
            yield return poly.GetPoint2dAt(i);
        }
    }

    public static Spline GetSpline(this Polyline pline)
    {
        Spline spline = null;

        void CreateSpline(NurbCurve3d nurb)
        {
            if (spline is null)
            {
                spline = (Spline)Curve.CreateFromGeCurve(nurb);
            }
            else
            {
                using Spline spl = (Spline)Curve.CreateFromGeCurve(nurb);
                try
                {
                    spline.JoinEntity(spl);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"GetSpline : Impossible to Join a Entity : {ex.Message}");
                }
            }
        }

        for (int i = 0; i < pline.NumberOfVertices; i++)
        {
            switch (pline.GetSegmentType(i))
            {
                case SegmentType.Line:
                    CreateSpline(new NurbCurve3d(pline.GetLineSegmentAt(i)));
                    break;
                case SegmentType.Arc:
                    CreateSpline(new NurbCurve3d(pline.GetArcSegmentAt(i).GetEllipticalArc()));
                    break;
            }
        }

        return spline;
    }

    public static Polyline ToPolygon(this Polyline poly, uint NumberOfVertexPerArc = 15)
    {
        if (poly.HasBulges)
        {
            uint NumberOfVertex = (uint)poly.GetReelNumberOfVertices();
            for (int i = 0; i < poly.GetReelNumberOfVertices(); i++)
            {
                if (poly.GetSegmentType(i) == SegmentType.Arc)
                {
                    NumberOfVertex += NumberOfVertexPerArc;
                }
            }

            Polyline NewPoly = new();

            for (int VerticeIndex = 0; VerticeIndex < poly.NumberOfVertices; VerticeIndex++)
            {
                Point3d CurrentPoint = poly.GetPoint3dAt(VerticeIndex);
                NewPoly.AddVertex(CurrentPoint);
                if (poly.GetSegmentType(VerticeIndex) == SegmentType.Line)
                {
                }
                else if (poly.GetSegmentType(VerticeIndex) == SegmentType.Arc)
                {
                    CircularArc3d Segment = poly.GetArcSegmentAt(VerticeIndex);
                    using Curve Arc = Segment.ToCircleOrArc();
                    double ReelNumberOfVertex = NumberOfVertexPerArc * Max(Abs(poly.GetBulgeAt(VerticeIndex)), 1);
                    double Interval = (Arc.EndParam - Arc.StartParam) / (ReelNumberOfVertex + 1);
                    for (int NumberOfInterval = 1; NumberOfInterval < ReelNumberOfVertex + 1; NumberOfInterval++)
                    {
                        Point3d Pt = Arc.GetPointAtParam(Arc.StartParam + (Interval * NumberOfInterval));
                        NewPoly.AddVertex(Pt);
                    }
                }
            }

            NewPoly.Closed = poly.Closed;
            return NewPoly;
        }

        return poly.Clone() as Polyline;
    }

    /// <summary>
    ///     Gets the bulge between two parameters within the same arc segment of a polyline.
    /// </summary>
    /// <param name="poly">The polyline.</param>
    /// <param name="startParam">The start parameter.</param>
    /// <param name="endParam">The end parameter.</param>
    /// <returns>The bulge.</returns>
    public static double GetBulgeBetween(this Polyline poly, double startParam, double endParam)
    {
        double total = poly.GetBulgeAt((int)Floor(startParam));
        return (endParam - startParam) * total;
    }

    public static void AddVertex(this Polyline Poly, Point3d point, double bulge = 0, double startWidth = 0,
        double endWidth = 0)
    {
        Poly.AddVertex(point.ToPoint2d(), bulge, startWidth, endWidth);
    }

    public static void AddVertex(this Polyline Poly, Point2d point, double bulge = 0, double startWidth = 0,
        double endWidth = 0)
    {
        Poly.AddVertexAt(Poly.NumberOfVertices, point, bulge, startWidth, endWidth);
    }

    public static void AddVertex(this Polyline3d Poly, Point3d point)
    {
        using PolylineVertex3d Vertex = new(point);
        _ = Poly.AppendVertex(Vertex);
    }

    public static void AddVertexIfNotExist(this Polyline Poly, Point3d point, double bulge = 0, double startWidth = 0,
        double endWidth = 0)
    {
        for (int i = 0; i < Poly.NumberOfVertices; i++)
        {
            if (Poly.GetPoint3dAt(i) == point)
            {
                return;
            }
        }

        Poly.AddVertex(point, bulge, startWidth, endWidth);
    }

    public static bool IsClockwise(this Polyline poly)
    {
        if (poly.NumberOfVertices < 2)
        {
            return false;
        }

        double area = 0.0;
        for (int i = 0; i < poly.NumberOfVertices; i++)
        {
            Point2d p1 = poly.GetPoint2dAt(i);
            Point2d p2 = poly.GetPoint2dAt((i + 1) % poly.NumberOfVertices);
            area += (p2.X - p1.X) * (p2.Y + p1.Y);
        }

        return area > 0; // horaire si aire > 0 (repère AutoCAD)
    }

    /// <summary>
    ///     Connects polylines.
    /// </summary>
    /// <param name="poly">The base polyline.</param>
    /// <param name="poly1">The other polyline.</param>
    public static void JoinPolyline(this Polyline poly, Polyline poly1)
    {
        int index = poly.GetPolyPoints().Count();
        int index1 = 0;
        IEnumerable<Point3d> Points = poly1.GetPoints();
        if (!poly.IsWriteEnabled)
        {
            poly.UpgradeOpen();
        }

        foreach (Point3d point in Points)
        {
            poly.AddVertexAt(index, point.ToPoint2d(), poly1.GetBulgeAt(index1), 0, 0);
            index++;
            index1++;
        }
    }

    public static Polyline ToPolyline(this Polyline3d poly3d)
    {
        if (poly3d.PolyType == Poly3dType.SimplePoly)
        {
            Polyline poly2d = new();
            foreach (PolylineVertex3d vertex in poly3d)
            {
                if (vertex != null)
                {
                    Point2d point = new(vertex.Position.X, vertex.Position.Y);
                    poly2d.AddVertexAt(poly2d.NumberOfVertices, point, 0, 0, 0);
                }
            }

            poly2d.Closed = poly3d.Closed;
            return poly2d;
        }

        return poly3d.Spline.ToPolyline() as Polyline;
    }

    public static Entity ToLWPolylineOrSpline(this Polyline3d poly3d)
    {
        return poly3d.PolyType == Poly3dType.SimplePoly ? poly3d.ToPolyline() : poly3d.Spline;
    }

    public static Polyline ToPolyline(this Polyline2d poly2d)
    {
        if (poly2d.PolyType is Poly2dType.QuadSplinePoly or Poly2dType.CubicSplinePoly)
        {
            Spline Spline = poly2d.Spline;
            return Spline.ToPolyline() as Polyline;
        }

        Polyline poly = new();
        poly.ConvertFrom(poly2d, false);
        return poly;
    }

    public static Entity ToLWPolylineOrSpline(this Polyline2d poly2d)
    {
        return poly2d.PolyType == Poly2dType.SimplePoly ? poly2d.ToPolyline() : poly2d.Spline;
    }

    public static IEnumerable<Polyline> SmartOffset(this Polyline ArgPoly, double ShrinkDistance)
    {
        using Polyline? poly = ArgPoly.Clone() as Polyline;
        if (poly.Area <= Generic.MediumTolerance.EqualPoint)
        {
            return Array.Empty<Polyline>();
        }

        poly.Closed = true;

        //Forcing close can result in weird point, we need to cleanup these before executing a offset
        poly.Cleanup();

        IEnumerable<Polyline> OffsetResult = InternalSmartOffset(poly);
        if (!OffsetResult.Any())
        {
            poly.Inverse();
            OffsetResult = InternalSmartOffset(poly);
        }

        return OffsetResult;

        IEnumerable<Polyline> InternalSmartOffset(Polyline InternalPoly)
        {
            // UseOffsetGapTypeCurrentValue need to be 0 to avoid rouded corners
            List<Polyline> OffsetPolylineResult = InternalPoly.OffsetPolyline(ShrinkDistance, false).Cast<Polyline>().ToList();

            if (OffsetPolylineResult.Count == 0)
            {
                //If OffsetPolyline result in no geometry, we need to fix the polyline first : custom cleanup
                bool HasVertexRemoved = true;
                while (HasVertexRemoved)
                {
                    HasVertexRemoved = false;
                    int index = 0;
                    while (index < InternalPoly.GetReelNumberOfVertices())
                    {
                        Point2d CurrentPoint = InternalPoly.GetPoint2dAt(index);
                        int nextPoint = index + 1;
                        if (nextPoint >= InternalPoly.GetReelNumberOfVertices())
                        {
                            nextPoint = 0;
                        }

                        Point2d NextPoint = InternalPoly.GetPoint2dAt(nextPoint);
                        double DistanceBetween = CurrentPoint.GetDistanceTo(NextPoint);
                        if (InternalPoly.GetSegmentType(index) == SegmentType.Line)
                        {
                            //Small line that we cant offset;
                            if (DistanceBetween <= Abs(ShrinkDistance))
                            {
                                InternalPoly.RemoveVertexAt(index);
                                continue;
                            }
                        }
                        else if (InternalPoly.GetSegmentType(index) == SegmentType.Arc)
                        {
                            //If there is 0.2 with gap, that mean previous offset generated Arc, we need to remove those.
                            CircularArc3d Segment = InternalPoly.GetArcSegmentAt(index);
                            //Multiply by 2 + 5% of error margin
                            if (DistanceBetween <= Abs(ShrinkDistance) * 2.05)
                            {
                                using Curve Arc = Segment.ToCircleOrArc();
                                Point3d ArcMidPoint = Arc.GetPointAtParam((Arc.StartParam + Arc.EndParam) / 2);
                                Point3d SegMidPoint = CurrentPoint.GetMiddlePoint(NextPoint);

                                Point3d NewPoint = ArcMidPoint.TransformBy(Matrix3d.Displacement(SegMidPoint
                                    .GetVectorTo(ArcMidPoint).SetLength(Abs(ShrinkDistance * 100))));

                                InternalPoly.SetBulgeAt(index, 0);
                                InternalPoly.AddVertexAt(index + 1, NewPoint.ToPoint2d(), 0, 0, 0);
                                continue;
                            }
                        }

                        index++;
                    }
                }

                //Cleanup the line (NEEDED ! if not in futur please explain why)
                InternalPoly.Cleanup();
                // UseOffsetGapTypeCurrentValue need to be 0 to avoid rouded corners
                OffsetPolylineResult = InternalPoly.OffsetPolyline(ShrinkDistance, false).Cast<Polyline>().ToList();
            }

            List<Curve> OffsetMergedPolylineResult = OffsetPolylineResult.JoinMerge();
            OffsetPolylineResult.DeepDispose();
            List<Polyline> ReturnOffsetMergedPolylineResult = OffsetMergedPolylineResult.Cast<Polyline>()
                .Where(p => p?.Closed == true && p.NumberOfVertices >= 2).ToList();
            OffsetMergedPolylineResult.RemoveCommun(ReturnOffsetMergedPolylineResult).DeepDispose();
            foreach (Polyline item in ReturnOffsetMergedPolylineResult)
            {
                item.Cleanup();
            }

            return ReturnOffsetMergedPolylineResult;
        }
    }

    public static Point3d GetInnerCentroid(this Polyline poly)
    {
        Polyline polygon = poly.ToPolygon(10);
        Point3d pt = PolygonOperation.GetInnerCentroid(polygon);
        if (polygon != poly)
        {
            polygon?.Dispose();
        }

        return pt;
    }

    public static Point3d GetCentroid(this Polyline pl)
    {
        int count = pl.NumberOfVertices;
        if (count == 0)
        {
            throw new ArgumentException("Polyline vide.");
        }

        double sumX = 0, sumY = 0, sumZ = 0;
        for (int i = 0; i < count; i++)
        {
            Point3d pt = pl.GetPoint3dAt(i);
            sumX += pt.X;
            sumY += pt.Y;
            sumZ += pt.Z;
        }

        return new Point3d(sumX / count, sumY / count, sumZ / count);
    }

    public static bool IsOverlaping(this Polyline LineA, Polyline LineB)
    {
        int NumberOfVertices = LineA.GetReelNumberOfVertices();
        for (int PolylineSegmentIndex = 0; PolylineSegmentIndex < NumberOfVertices; PolylineSegmentIndex++)
        {
            (Point3d StartPoint, Point3d EndPoint, double _) = LineA.GetSegmentAt(PolylineSegmentIndex);
            Point3d MiddlePoint = StartPoint.GetMiddlePoint(EndPoint);

            if (StartPoint.DistanceTo(EndPoint) / 2 >
                Generic.MediumTolerance.EqualPoint)
            {
                if (MiddlePoint.IsOnPolyline(LineB))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsInside(this Polyline LineA, Polyline LineB, bool CheckEach = true)
    {
        int NumberOfVertices = 1;
        int ReelNumberOfVertices = LineA.GetReelNumberOfVertices();
        if (CheckEach)
        {
            NumberOfVertices = ReelNumberOfVertices;
        }

        for (int PolylineSegmentIndex = 0; PolylineSegmentIndex < NumberOfVertices; PolylineSegmentIndex++)
        {
            (Point3d StartPoint, Point3d EndPoint, double _) = LineA.GetSegmentAt(PolylineSegmentIndex);
            if (StartPoint.DistanceTo(EndPoint) / 2 >
                Generic.MediumTolerance.EqualPoint)
            {
                Point3d MiddlePoint;
                if (LineA.GetSegmentType(PolylineSegmentIndex) == SegmentType.Arc)
                {
                    double Startparam = LineA.GetParameterAtPoint(StartPoint);
                    double Endparam = LineA.GetParameterAtPoint(EndPoint);
                    MiddlePoint = LineA.GetPointAtParam(Startparam + ((Endparam - Startparam) / 2));
                }
                else
                {
                    MiddlePoint = StartPoint.GetMiddlePoint(EndPoint);
                }

                if (!MiddlePoint.IsInsidePolyline(LineB))
                {
                    return false;
                }
            }
            else
            {
                //No good point found, we run back the function
                if (NumberOfVertices < ReelNumberOfVertices - 1)
                {
                    NumberOfVertices++;
                }
            }
        }

        return true;
    }

    public static bool IsSameAs(this Polyline polylineA, Polyline polylineB)
    {
        if (polylineA.IsDisposed || polylineB.IsDisposed)
        {
            return false;
        }

        if (polylineA.NumberOfVertices != polylineB.NumberOfVertices)
        {
            return false;
        }

        Tolerance tol = Generic.MediumTolerance;

        bool IsClockwisePolyA = polylineA.IsClockwise();
        bool IsClockwisePolyB = polylineB.IsClockwise();
        if (IsClockwisePolyA != IsClockwisePolyB)
        {
            if (IsClockwisePolyA)
            {
                polylineB.Inverse();
            }
            else
            {
                polylineB.Inverse();
            }
        }

        for (int i = 0; i < polylineA.GetReelNumberOfVertices(); i++)
        {
            (Point3d StartPoint, Point3d EndPoint, double Bulge) = polylineA.GetSegmentAt(i);
            (Point3d StartPoint, Point3d EndPoint, double Bulge) SegB = polylineB.GetSegmentAt(i);
            if (!StartPoint.IsEqualTo(SegB.StartPoint, tol))
            {
                return false;
            }

            if (!EndPoint.IsEqualTo(SegB.EndPoint, tol))
            {
                return false;
            }

            if (Bulge != SegB.Bulge)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsSegmentIntersecting(this Polyline polyline, Polyline CutLine,
        out Point3dCollection IntersectionPointsFounds, Intersect intersect)
    {
        IntersectionPointsFounds = [];
        if (polyline?.IsDisposed != false || CutLine?.IsDisposed != false)
        {
            return false;
        }

        polyline.IntersectWith(CutLine, intersect, IntersectionPointsFounds, IntPtr.Zero, IntPtr.Zero);
        return IntersectionPointsFounds.Count > 0;
    }

    public static bool ContainsSegment(this Polyline poly, Point3d Start, Point3d End)
    {
        Tolerance tol = Generic.MediumTolerance;
        for (int i = 0; i < poly.GetReelNumberOfVertices(); i++)
        {
            (Point3d StartPoint, Point3d EndPoint, double _) = poly.GetSegmentAt(i);
            if (StartPoint.IsEqualTo(Start, tol) && EndPoint.IsEqualTo(End, tol))
            {
                return true;
            }

            if (StartPoint.IsEqualTo(End, tol) && EndPoint.IsEqualTo(Start, tol))
            {
                return true;
            }
        }

        return false;
    }

    public static double GetPassingThroughBulgeFrom(this Point3d Through, Point3d Start, Point3d End)
    {
        Point3d MiddlePoint = Start.GetMiddlePoint(End);
        double D1 = MiddlePoint.DistanceTo(Through);
        double D2 = MiddlePoint.DistanceTo(Start);
        return D1 / D2;
    }
}
