using AutoBIMFusion.Common.AcadSupport;
using System.Diagnostics;

namespace AutoBIMFusion.Common.Extensions;

public static class CurvesExtensions
{
    /// <summary>
    ///     Получает параметр в указанной точке на кривой.
    /// </summary>
    /// <param name="cv">Кривая.</param>
    /// <param name="point">Точка.</param>
    /// <returns>Параметр.</returns>
    public static double GetParamAtPointX(this Curve cv, Point3d point)
    {
        if (point.DistanceTo(cv.StartPoint) < AcadContext.MediumTolerance.EqualPoint)
        {
            return 0.0;
        }

        if (point.DistanceTo(cv.EndPoint) < AcadContext.MediumTolerance.EqualPoint)
        {
            return cv.GetParameterAtPoint(cv.EndPoint);
        }

        try
        {
            return cv.GetParameterAtPoint(point);
        }
        catch
        {
            return cv.GetParameterAtPoint(cv.GetClosestPointTo(point, false));
        }
    }

    /// <summary>
    ///     Получает точку по указанному параметру на кривой.
    /// </summary>
    /// <param name="cv">Кривая.</param>
    /// <param name="param">Параметр.</param>
    /// <returns>Точка.</returns>
    public static Point3d GetPointAtParam(this Curve cv, double param)
    {
        if (param < 0)
        {
            param = 0;
        }
        else if (param > cv.EndParam)
        {
            param = cv.EndParam;
        }

        return cv.GetPointAtParameter(param);
    }

    /// <summary>
    ///     Получает все точки на кривой, параметры которых образуют арифметическую последовательность, начиная с 0.
    /// </summary>
    /// <param name="cv">Кривая.</param>
    /// <param name="paramDelta">
    ///     Приращение параметра. По умолчанию 1, в этом случае метод возвращает все точки на кривой,
    ///     параметры которых являются целыми числами.
    /// </param>
    /// <returns>Точки.</returns>
    public static IEnumerable<Point3d> GetPoints(this Curve cv, double paramDelta = 1)
    {
        for (double param = 0d; param <= cv.EndParam; param += paramDelta)
        {
            yield return cv.GetPointAtParam(param);
        }
    }

    /// <summary>
    ///     Упорядочивает коллекцию по смежным кривым ([n].EndPoint равно [n+1].StartPoint)
    /// </summary>
    /// <param name="source">Коллекция, к которой применяется метод.</param>
    /// <returns>Упорядоченный массив Curve3d.</returns>
    public static Curve3d[] ToOrderedArray(this IEnumerable<Curve3d> source)
    {
        List<Curve3d> list = source.ToList();
        int count = list.Count;
        Curve3d[] array = new Curve3d[count];
        int i = 0;
        array[0] = list[0];
        list.RemoveAt(0);
        int index;
        while (i < count - 1)
        {
            var pt = array[i++].EndPoint;
            if ((index = list.FindIndex(c => c.StartPoint.IsEqualTo(pt))) != -1)
            {
                array[i] = list[index];
            }
            else if ((index = list.FindIndex(c => c.EndPoint.IsEqualTo(pt))) != -1)
            {
                array[i] = list[index].GetReverseParameterCurve();
            }
            else
            {
                Debug.WriteLine("Кривые не являются смежными.");
                return Array.Empty<Curve3d>();
            }

            list.RemoveAt(index);
        }

        return array;
    }

    public static List<Curve> OffsetPolyline(this IEnumerable<Curve> Curves, double OffsetDistance,
        bool UseOffsetGapTypeCurrentValue = true)
    {
        List<Curve> OffsetCurves = [];

        foreach (var ent in Curves)
        {
            OffsetCurves.AddRange(ent.OffsetPolyline(OffsetDistance, UseOffsetGapTypeCurrentValue).ToList()
                .Cast<Curve>());
        }

        return OffsetCurves;
    }

    public static DBObjectCollection OffsetPolyline(this Curve Curve, double OffsetDistance,
        bool UseOffsetGapTypeCurrentValue = true)
    {
        object OffsetGapType =
            AcadContext.GetSystemVariable(
                "OFFSETGAPTYPE"); //Controls how potential gaps between segments are treated when polylines are offset. 
        if (!UseOffsetGapTypeCurrentValue)
        {
            AcadContext.SetSystemVariable("OFFSETGAPTYPE", 0,
                false); //Extends line segments to their projected intersections.
        }

        try
        {
            if (Curve is Polyline)
            {
                return Curve.GetOffsetCurves((Curve as Polyline).GetArea() < 0.0 ? -OffsetDistance : OffsetDistance);
            }
            else if (Curve is Ellipse or Circle)
            {
                return Curve.GetOffsetCurves(OffsetDistance);
            }

            return [];
        }
        finally
        {
            AcadContext.SetSystemVariable("OFFSETGAPTYPE", OffsetGapType, false);
        }
    }

    public static bool IsSelfIntersecting(this Curve poly, out Point3dCollection IntersectionFound)
    {
        IntersectionFound = [];
        DBObjectCollection entities = [];
        poly.Explode(entities);
        for (int i = 0; i < entities.Count; ++i)
        {
            for (int j = i + 1; j < entities.Count; ++j)
            {
                Curve? curve1 = entities[i] as Curve;
                Curve? curve2 = entities[j] as Curve;
                Point3dCollection points = [];
                curve1.IntersectWith(curve2, Intersect.OnBothOperands, points, IntPtr.Zero, IntPtr.Zero);

                foreach (Point3d point in points)
                {
                    // Делаем проверку для пропуска начальных/конечных точек
                    // так как это соединённые вершины
                    if (point == curve1.StartPoint || point == curve1.EndPoint)
                    {
                        if (point == curve2.StartPoint || point == curve2.EndPoint)
                        {
                            continue;
                        }
                    }

                    // Если два последовательных сегмента, пропускаем
                    if (j == i + 1)
                    {
                        continue;
                    }

                    if (curve1.GetClosestPointTo(point, false).DistanceTo(point) < AcadContext.MediumTolerance.EqualPoint &&
                        curve2.GetClosestPointTo(point, false).DistanceTo(point) < AcadContext.MediumTolerance.EqualPoint)
                    {
                        _ = IntersectionFound.Add(point);
                    }
                }
            }

            // Нужно явно dispose
            // так как сущности не находятся в базе данных
            entities[i].Dispose();
        }

        return IntersectionFound.Count != 0;
    }

    public static bool CanBeJoinWith(this Curve A, Curve B)
    {
        if (A == B)
        {
            return false;
        }

        if (A.Closed || B.Closed)
        {
            return false;
        }

        if (A.IsCurveCanClose(B))
        {
            // Проверяем, уже ли соединена полилиния
            var PAPoint = A.GetPoints();
            List<Point3d> PAPointList = PAPoint.ToList();
            if (A.StartPoint.DistanceTo(A.EndPoint) > AcadContext.MediumTolerance.EqualPoint)
            {
                _ = PAPointList.Remove(A.StartPoint);
                _ = PAPointList.Remove(A.EndPoint);
            }

            var PBPoint = B.GetPoints();

            if (PAPointList.ContainsAll(PBPoint))
            {
                return false;
            }
        }

        return A.HasEndPointOrStartPointInCommun(B);
    }

    public static bool IsCurveCanClose(this Curve PolyA, Curve PolyB)
    {
        var StartPointA = PolyA.StartPoint.Flatten();
        var EndPointA = PolyA.EndPoint.Flatten();

        var StartPointB = PolyB.StartPoint.Flatten();
        var EndPointB = PolyB.EndPoint.Flatten();
        return (StartPointA.IsEqualTo(StartPointB, AcadContext.LowTolerance) &&
                EndPointA.IsEqualTo(EndPointB, AcadContext.LowTolerance)) ||
               (StartPointA.IsEqualTo(EndPointB, AcadContext.LowTolerance) &&
                EndPointA.IsEqualTo(StartPointB, AcadContext.LowTolerance));
    }

    public static bool HasEndPointOrStartPointInCommun(this Curve A, Curve B)
    {
        return A != null && B != null && (A.EndPoint.IsEqualTo(B.EndPoint, AcadContext.LowTolerance) ||
                                          A.EndPoint.IsEqualTo(B.StartPoint, AcadContext.LowTolerance) ||
                                          A.StartPoint.IsEqualTo(B.EndPoint, AcadContext.LowTolerance) ||
                                          A.StartPoint.IsEqualTo(B.StartPoint, AcadContext.LowTolerance));
    }

    public static Polyline ToPolyline(this Curve curve)
    {
        if (curve.IsDisposed)
        {
            return null;
        }

        Type PreviousTryConvertCurve;
        Entity LastCurveConverted = curve.Clone() as Curve;

        do //Do once to Clone polyline
        {
            //Save previous
            PreviousTryConvertCurve = LastCurveConverted?.GetType();
            //Get new
            var NewCurveConverted = TryGetPolyligne(LastCurveConverted);
            LastCurveConverted.Dispose();
            LastCurveConverted = NewCurveConverted;
        } while
            (LastCurveConverted?.GetType() !=
             PreviousTryConvertCurve); //Avoid while infinite loop, if curve after TryGetPolyligne is the same as the previous, that not working

        return LastCurveConverted as Polyline;


        static Entity TryGetPolyligne(Entity curv)
        {
            // Преобразуем все кривые в обычную полилинию
            if (curv is Polyline ProjectionTargetPolyLine)
            {
                return ProjectionTargetPolyLine.Clone() as Polyline;
            }

            if (curv is Ellipse ProjectionTargetEllipse)
            {
                return ProjectionTargetEllipse.ToPolyline();
            }

            if (curv is Helix ProjectionTargetHelix)
            {
                Helix FlattenProjectionTargetHelix = (Helix)ProjectionTargetHelix.Clone();
                _ = FlattenProjectionTargetHelix.Flatten();
                var Converted = FlattenProjectionTargetHelix.ToPolyline(true, true);
                return Converted as Polyline;
            }

            return curv is Spline ProjectionTargetSpline
                ? ProjectionTargetSpline.ToPolyline(true, true)
                : curv is Line ProjectionTargetLine
                    ? ProjectionTargetLine.ToPolyline()
                    : curv is Circle ProjectionTargetCircle
                        ? ProjectionTargetCircle.ToPolyline()
                        : curv is Arc ProjectionTargetArc
                            ? ProjectionTargetArc.ToPolyline()
                            : curv is Polyline2d ProjectionTargetPolyline2d
                                ? ProjectionTargetPolyline2d.ToPolyline()
                                : curv is Polyline3d ProjectionTargetPolyline3d
                                    ? ProjectionTargetPolyline3d.ToPolyline()
                                    : (Entity?)null;
        }
    }

    public static Curve2d Reverse(this Curve2d segment)
    {
        return segment is LineSegment2d line
            ? new LineSegment2d(line.EndPoint, line.StartPoint)
            : segment is CircularArc2d
                ? segment.GetReverseParameterCurve()
                : segment.Clone() as Curve2d;
    }

    public static Curve ConvertToCurve(this Curve2d curve2d)
    {
        switch (curve2d)
        {
            case LineSegment2d lineSegment:
                Line line = new(
                    new Point3d(lineSegment.StartPoint.X, lineSegment.StartPoint.Y, 0.0),
                    new Point3d(lineSegment.EndPoint.X, lineSegment.EndPoint.Y, 0.0));
                return line;
            case CircularArc2d circularArc:
                if (circularArc.EndPoint.IsEqualTo(circularArc.StartPoint) && circularArc.Radius > 0)
                {
                    return new Circle(circularArc.Center.ToPoint3d(), Vector3d.YAxis, circularArc.Radius);
                }

                double startAngle = circularArc.IsClockWise ? -circularArc.EndAngle : circularArc.StartAngle;
                double endAngle = circularArc.IsClockWise ? -circularArc.StartAngle : circularArc.EndAngle;
                return new Arc(
                    new Point3d(circularArc.Center.X, circularArc.Center.Y, 0.0),
                    circularArc.Radius,
                    circularArc.ReferenceVector.Angle + startAngle,
                    circularArc.ReferenceVector.Angle + endAngle);
            case EllipticalArc2d ellipticalArc:
                double ratio = ellipticalArc.MinorRadius / ellipticalArc.MajorRadius;
                double startParam = ellipticalArc.IsClockWise ? -ellipticalArc.EndAngle : ellipticalArc.StartAngle;
                double endParam = ellipticalArc.IsClockWise ? -ellipticalArc.StartAngle : ellipticalArc.EndAngle;
                Ellipse ellipse = new(
                    new Point3d(ellipticalArc.Center.X, ellipticalArc.Center.Y, 0.0),
                    Vector3d.ZAxis,
                    new Vector3d(ellipticalArc.MajorAxis.X, ellipticalArc.MajorAxis.Y, 0.0) * ellipticalArc.MajorRadius,
                    ratio,
                    Atan2(Sin(startParam) * ellipticalArc.MinorRadius, Cos(startParam) * ellipticalArc.MajorRadius),
                    Atan2(Sin(endParam) * ellipticalArc.MinorRadius, Cos(endParam) * ellipticalArc.MajorRadius));
                return ellipse;
            case NurbCurve2d nurbCurve:
                Point3dCollection points = [];
                for (int j = 0; j < nurbCurve.NumControlPoints; j++)
                {
                    var pt = nurbCurve.GetControlPointAt(j);
                    _ = points.Add(new Point3d(pt.X, pt.Y, 0.0));
                }

                DoubleCollection knots = [];
                for (int k = 0; k < nurbCurve.NumKnots; k++)
                {
                    _ = knots.Add(nurbCurve.GetKnotAt(k));
                }

                DoubleCollection weights = [];
                for (int l = 0; l < nurbCurve.NumWeights; l++)
                {
                    _ = weights.Add(nurbCurve.GetWeightAt(l));
                }

                Spline spline = new(nurbCurve.Degree, nurbCurve.IsRational, nurbCurve.IsClosed(), false, points,
                    knots, weights, 0.0, 0.0);
                return spline;
        }

        return null;
    }


    public static List<Curve> JoinMerge(this IEnumerable<Curve> Curves)
    {
        List<Curve> entities = Curves.ToList();
        if (entities.Count <= 1)
        {
            // Нет геометрии для объединения
            return entities.Clone();
        }

        for (int i = 0; i < entities.Count; i++)
        {
            var JoignableEnt = entities[i].GetJoinableCurve();
            //entities[i].CopyPropertiesTo(JoignableEnt);
            entities[i] = JoignableEnt;
        }

        for (int i = entities.Count - 1; i >= 0; i--)
        {
            for (int j = i - 1; j >= 0; j--)
            {
                try
                {
                    // проверяем, совпадают ли начальные/конечные точки
                    // если да, соединяем их и сбрасываем циклы, начиная заново
                    var srcCurve = entities[i];
                    var addCurve = entities[j];

                    if (srcCurve.CanBeJoinWith(addCurve))
                    {
                        if (addCurve is Spline && srcCurve is not Spline)
                        {
                            addCurve.JoinEntity(srcCurve);
                            entities.RemoveAt(i);
                            srcCurve.Dispose();
                        }
                        else
                        {
                            srcCurve.JoinEntity(addCurve);
                            entities.RemoveAt(j);
                            addCurve.Dispose();
                        }

                        // сбрасываем i в начало (так как оно изменилось)
                        i = entities.Count;
                        j = 0;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("\nОшибка: {0}", ex.Message);
                }
            }
        }

        return entities;
    }

    private static Curve GetJoinableCurve(this Curve srcCurve)
    {
        return srcCurve is Line srcPolylineType
            ? srcPolylineType.ToPolyline()
            : srcCurve is Arc srcPArcType
                ? srcPArcType.ToPolyline()
                : srcCurve is Ellipse srcPEllipseType
                    ? srcPEllipseType.Spline
                    : srcCurve.Clone() as Curve;
    }

    public static List<Curve> RegionMerge(this IEnumerable<Curve> Curves)
    {
        DBObjectCollection reg;
        try
        {
            DBObjectCollection CurvesCollection = Curves.ToDBObjectCollection();
            foreach (var ent in Curves.ToArray())
            {
                if (ent is Polyline polyline && polyline.IsSelfIntersecting(out var IntersectionFound))
                {
                    AcadContext.WriteMessage(
                        "Неверный набор выбора: одна или несколько полилиний пересекают сами себя");
                }
            }

            reg = Region.CreateFromCurves(CurvesCollection);
        }
        catch (Exception e)
        {
            AcadContext.WriteMessage("Невозможно объединить штриховки");
            Debug.WriteLine(e);
            return [];
        }

        if (reg.Count > 0)
        {
            Region? RegionZero = reg[0] as Region;
            for (int i = 1; i < reg.Count; i++)
            {
                RegionZero.BooleanOperation(BooleanOperationType.BoolUnite, reg[i] as Region);
            }

            var MergedCurves = RegionZero.GetPolylines();
            return MergedCurves.Cast<Curve>().ToList();
        }

        return Curves.ToList();
    }
}
