using AutoBIMFusion.Common.Compatibility;
using AutoBIMFusion.Common.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace AutoBIMFusion.Common.Mist.Geometry.PolygonOperations;

public static partial class PolygonOperation
{
    public const double Margin = 0.01;

    public static bool Union(List<PolyHole> PolyHoleList, out List<PolyHole> UnionResult,
        bool RequestAllowMarginError = false)
    {
        //Don't run if we have no element to union
        if (PolyHoleList.Count == 0)
        {
            UnionResult = [];
            return false;
        }

        //We cant offset self-intersection curve in autocad, we need to disable this if this is the case
        bool AllowMarginError = RequestAllowMarginError && CheckAllowMarginError(PolyHoleList);

        List<Polyline> Holes = UnionHoles(PolyHoleList, AllowMarginError);

        Extents3d ExtendBeforeUnion = PolyHoleList.GetBoundaries().GetExtents();

        if (AllowMarginError)
        {
            //Offset the PolyHole boundary so you can merge a nearly touching polyline
            var PolyHoleListCopy = PolyHoleList.ToList();
            for (int i = 0; i < PolyHoleListCopy.Count; i++)
            {
                PolyHole PolyHole = PolyHoleListCopy[i];
                _ = PolyHoleList.Remove(PolyHole);
                PolyHoleList.AddRange(OffsetPolyHole(ref PolyHole, Margin));
            }
        }

        ConcurrentBag<(HashSet<Polyline> Splitted, Polyline GeometryOrigin)> SplittedCurvesOrigin = GetSplittedCurves(PolyHoleList.GetBoundaries());

        // SplittedCurvesOrigin.ForEach(curve => curve.Splitted.ForEach(ent => ent.AddToDrawing(5)));

        //Check if Cutted line IsInside -> if true remove
        List<Polyline> GlobalSplittedCurves = RemoveInsideCutLine(PolyHoleList, SplittedCurvesOrigin);


        var PossibleBoundary = GlobalSplittedCurves.JoinMerge().Cast<Polyline>().ToList();
        if (!(PossibleBoundary.Count == 1 && PossibleBoundary.First().Closed))
        {
            PossibleBoundary.DeepDispose();
            List<Polyline> FilteredSplittedCurves = RemoveOverlaping(GlobalSplittedCurves);
            FilteredSplittedCurves.CleanupPolylines();

            PossibleBoundary = FilteredSplittedCurves.JoinMerge().Cast<Polyline>().ToList();
            //remove from GlobalSplittedCurves old polyligne that was filtered
            GlobalSplittedCurves.RemoveCommun(FilteredSplittedCurves).DeepDispose();
            foreach (Polyline? item in PossibleBoundary.ToList())
            {
                if (item.TryGetArea() == 0 ||
                    item.NumberOfVertices < 2) //cannot keep 3 because circle is valid and is only 2
                {
                    Debug.WriteLine("Удалена недопустимая геометрия при операции ОБЪЕДИНЕНИЯ");
                    _ = PossibleBoundary.Remove(item);
                    item.Dispose();
                }
            }

            ///Dispose unused

            if (AllowMarginError)
            {
                FilteredSplittedCurves.DeepDispose();
            }
            else
            {
                FilteredSplittedCurves.RemoveCommun(PolyHoleList.GetBoundaries()).DeepDispose();
            }
        }
        else
        {
            GlobalSplittedCurves.RemoveCommun(PolyHoleList.GetBoundaries()).DeepDispose();
        }


        if (RequestAllowMarginError)
        {
            ///Check if generated union with boundary may result in hole,
            ///only usefull if RequireAllowMarginError is true for the moment because can cause issue with CUTHATCH if cuthole cause an another inner hole
            CheckBoundaryUnionResultInHole(PossibleBoundary, Holes, AllowMarginError);
        }

        if (AllowMarginError)
        {
            //PossibleBoundary.AddToDrawing(4, true);
            /// Этот блок позволяет попытаться удалить последний и первый сегмент
            /// if overlapping with previous on (usefull when JoinMerge has merged wrong Margin segments
            List<Polyline> clonedBoundaries = PossibleBoundary.ConvertAll(pe => (Polyline)pe.Clone());
            clonedBoundaries.ForEach(CleanPolylineSegments);
            //clonedBoundaries.AddToDrawing(5, true);
            var mergedBoundaries = clonedBoundaries.JoinMerge().Cast<Polyline>().ToList();
            //mergedBoundaries.AddToDrawing(6, true);

            clonedBoundaries.RemoveCommun(mergedBoundaries).DeepDispose();
            if (mergedBoundaries.Count < PossibleBoundary.Count)
            {
                PossibleBoundary.DeepDispose();
                PossibleBoundary = mergedBoundaries;
            }
            else
            {
                mergedBoundaries.DeepDispose();
            }
        }


        UnionResult = PolyHole.CreateFromList(PossibleBoundary, Holes);

        if (AllowMarginError)
        {
            var UnionResultCopy = UnionResult.ToList();
            //UnionResultCopy.GetBoundaries().AddToDrawing(1, true);

            if (UnionResultCopy.Count == 0)
            {
                return false;
            }

            //Undo offset PolyHole boundary 
            for (int i = 0; i < UnionResultCopy.Count; i++)
            {
                PolyHole PolyHole = UnionResultCopy[i];
                _ = UnionResult.Remove(PolyHole);
                List<PolyHole> UndoMargin = OffsetPolyHole(ref PolyHole, -Margin);
                if (UndoMargin.Count == 0)
                {
                    return false;
                }

                UnionResult.AddRange(UndoMargin);
            }
        }

        //UnionResult.GetBoundaries().AddToDrawing(1);
        Extents3d ExtendAfterUnion = UnionResult.GetBoundaries().GetExtents();
        ExtentsSize ExtendBeforeUnionSize = ExtendBeforeUnion.Size();
        ExtentsSize ExtendAfterUnionSize = ExtendAfterUnion.Size();

        //If size of the extend is different, that mean the union failled at some point
        return Abs(ExtendBeforeUnionSize.Width - ExtendAfterUnionSize.Width) < Generic.LowTolerance.EqualPoint
               && Abs(ExtendBeforeUnionSize.Height - ExtendAfterUnionSize.Height) < Generic.LowTolerance.EqualPoint;
    }


    public static void CleanPolylineSegments(Polyline pline)
    {
        if (pline == null || pline.NumberOfVertices < 3)
        {
            Debug.WriteLine("Недопустимая полилиния или недостаточно вершин.");
            return;
        }

        // === DEBUT : vérifier segments [0] et [1]
        if (pline.GetSegmentType(0) == SegmentType.Line &&
            pline.GetSegmentType(1) == SegmentType.Line)
        {
            LineSegment2d seg0 = pline.GetLineSegment2dAt(0);
            LineSegment2d seg1 = pline.GetLineSegment2dAt(1);

            Vector2d dir0 = seg0.EndPoint - seg0.StartPoint;
            Vector2d dir1 = seg1.EndPoint - seg1.StartPoint;

            bool IsParallelTo = dir0.IsParallelTo(dir1, Generic.MediumTolerance);
            if (IsParallelTo)
            {
                pline.RemoveVertexAt(0);
                Debug.WriteLine("Начальный сегмент удалён (обнаружено перекрытие).");
            }

            seg0.Dispose();
            seg1.Dispose();
        }

        // === FIN : vérifier segments [N-2] et [N-1]
        int count = pline.NumberOfVertices;
        if (count >= 3 &&
            pline.GetSegmentType(count - 2) == SegmentType.Line &&
            pline.GetSegmentType(count - 1) == SegmentType.Line)
        {
            LineSegment2d segA = pline.GetLineSegment2dAt(count - 2);
            LineSegment2d segB = pline.GetLineSegment2dAt(count - 1);

            Vector2d dirA = segA.EndPoint - segA.StartPoint;
            Vector2d dirB = segB.EndPoint - segB.StartPoint;

            bool IsParallelTo = dirA.IsParallelTo(dirB, Generic.MediumTolerance);
            if (IsParallelTo)
            {
                pline.RemoveVertexAt(count - 1);
                Debug.WriteLine("Конечный сегмент удалён (обнаружено перекрытие).");
            }

            segA.Dispose();
            segB.Dispose();
        }
    }


    private static void CheckBoundaryUnionResultInHole(List<Polyline> PossibleBoundary, List<Polyline> Holes,
        bool AllowMarginError)
    {
        foreach (Polyline? BoundaryA in PossibleBoundary.ToList())
        {
            foreach (Polyline? BoundaryB in PossibleBoundary.ToList())
            {
                if (BoundaryA == BoundaryB)
                {
                    continue;
                }

                if (BoundaryA.IsInside(BoundaryB))
                {
                    _ = PossibleBoundary.Remove(BoundaryA);
                    if (AllowMarginError)
                    {
                        //Because a hole is generated, the inner hole is reduced, we need to expand it back
                        IEnumerable<Polyline> OffsetBoundaryA = BoundaryA.SmartOffset(Margin);
                        BoundaryA.Dispose();
                        IEnumerable<Polyline> MergedOffsetBoundaryA = OffsetBoundaryA.JoinMerge().Cast<Polyline>();
                        Holes.AddRange(MergedOffsetBoundaryA);
                        OffsetBoundaryA.DeepDispose();
                    }
                    else
                    {
                        Holes.Add(BoundaryA);
                    }

                    break;
                }
            }
        }
    }

    private static List<Polyline> RemoveOverlaping(List<Polyline> Curves)
    {
        object _lock = new();
        List<Polyline> NoOverlapingCurves = [.. Curves];
        _ = Parallel.ForEach(Curves,
            new ParallelOptions { MaxDegreeOfParallelism = Settings.MultithreadingMaxNumberOfThread }, SplittedCurveA =>
            {
                foreach (Polyline SplittedCurveB in Curves.ToArray())
                {
                    if (SplittedCurveB != null && SplittedCurveA != SplittedCurveB)
                    {
                        if (SplittedCurveA.IsSameAs(SplittedCurveB))
                        {
                            lock (_lock)
                            {
                                if (NoOverlapingCurves.Contains(SplittedCurveB))
                                {
                                    _ = NoOverlapingCurves.Remove(SplittedCurveA);
                                }
                            }

                            break;
                        }
                    }
                }
            });

        return NoOverlapingCurves;
    }

    private static List<Polyline> RemoveInsideCutLine(List<PolyHole> PolyHoleList,
        ConcurrentBag<(HashSet<Polyline> Splitted, Polyline GeometryOrigin)> SplittedCurvesOrigin)
    {
        ConcurrentBag<Polyline> GlobalSplittedCurves = [];
        ConcurrentDictionary<Polyline, Polyline> NoArcPolygonCache = new();
        _ = Parallel.ForEach(SplittedCurvesOrigin.ToArray(),
            new ParallelOptions { MaxDegreeOfParallelism = Settings.MultithreadingMaxNumberOfThread },
            SplittedCurveOrigin =>
            {
                HashSet<Polyline> SplittedCurves = SplittedCurveOrigin.Splitted;

                foreach (PolyHole PolyBase in PolyHoleList)
                {
                    if (PolyBase.Boundary.IsDisposed)
                    {
                        continue;
                    }

                    _ = NoArcPolygonCache.TryGetValue(PolyBase.Boundary, out Polyline? NoArcPolyBase);
                    Extents3d PolyBaseExtend = PolyBase.Boundary.GetExtents();
                    foreach (Polyline? SplittedCurve in SplittedCurves.ToArray())
                    {
                        if (PolyBase.Boundary == SplittedCurveOrigin.GeometryOrigin)
                        {
                            continue;
                        }

                        if (!SplittedCurve.IsInside(PolyBaseExtend, false))
                        {
                            continue;
                        }

                        if (NoArcPolyBase == null)
                        {
                            NoArcPolyBase = PolyBase.Boundary.ToPolygon();
                            _ = NoArcPolygonCache.TryAdd(PolyBase.Boundary, NoArcPolyBase);
                        }

                        if (SplittedCurve.IsInside(NoArcPolyBase, false) &&
                            !SplittedCurve.IsOverlaping(PolyBase
                                .Boundary)) // need to add a check if it overlaping an another, we should remove it anyway
                        {
                            _ = SplittedCurves.Remove(SplittedCurve);
                            SplittedCurve.Dispose();
                        }
                    }
                }

                GlobalSplittedCurves.AddRange(SplittedCurves);
            });

        foreach (KeyValuePair<Polyline, Polyline> item in NoArcPolygonCache)
        {
            if (item.Key != item.Value)
            {
                item.Value?.Dispose();
            }
        }

        return GlobalSplittedCurves.ToList();
    }

    private static bool CheckAllowMarginError(List<PolyHole> PolyHoleList)
    {
        foreach (PolyHole PolyHole in PolyHoleList)
        {
            PolyHole.Boundary.Cleanup();
            if (PolyHole.Boundary.IsSelfIntersecting(out _))
            {
                Generic.WriteMessage("Обнаружено самопересечение. AllowMarginError отключен");
                return false;
            }
        }

        return true;
    }

    private static List<Polyline> UnionHoles(List<PolyHole> PolyHoleList, bool RequestAllowMarginError = false)
    {
        List<Polyline> HoleUnionResult = [];
        if (PolyHoleList.Count == 0)
        {
            return [];
        }

        foreach (Polyline Hole in PolyHoleList.GetAllHoles())
        {
            HoleUnionResult.Add(Hole.Clone() as Polyline);
        }

        //Substract Boundary from each hole if they intersect
        for (int PolyHoleListIndex = 0; PolyHoleListIndex < PolyHoleList.Count; PolyHoleListIndex++)
        {
            PolyHole polyHole = PolyHoleList[PolyHoleListIndex];

            Polyline PolyHoleBoundary = null;
            if (RequestAllowMarginError)
            {
                IEnumerable<Polyline> offseted = polyHole.Boundary.SmartOffset(Margin);
                if (offseted.Any())
                {
                    PolyHoleBoundary = offseted.First();
                    offseted.Skip(1).ForEach(el => el.Dispose());
                }
                else
                {
                    PolyHoleBoundary = polyHole.Boundary.Clone() as Polyline;
                }
            }
            else
            {
                PolyHoleBoundary = polyHole.Boundary.Clone() as Polyline;
            }

            using (PolyHoleBoundary)
            {
                var HoleUnionResultList = HoleUnionResult.ToList();
                for (int i = 0; i < HoleUnionResultList.Count; i++)
                {
                    Polyline ParsedHole = HoleUnionResultList[i];
                    if (RequestAllowMarginError)
                    {
                        var OffsetParsedHole = ParsedHole.SmartOffset(-Margin).ToList();
                        if (OffsetParsedHole.Count > 0)
                        {
                            ParsedHole = OffsetParsedHole.First();
                            _ = OffsetParsedHole.Remove(ParsedHole);
                            OffsetParsedHole.DeepDispose();
                        }
                    }

                    if (PolyHoleBoundary.IsDisposed || ParsedHole.IsDisposed)
                    {
                        continue;
                    }

                    if (ParsedHole.IsSegmentIntersecting(PolyHoleBoundary, out _, Intersect.OnBothOperands) ||
                        ParsedHole.IsInside(polyHole.Boundary, false))
                    {
                        _ = HoleUnionResult.Remove(HoleUnionResultList[i]);
                        if (Substraction(new PolyHole(ParsedHole, null), new[] { PolyHoleBoundary }, out List<PolyHole>? SubResult))
                        {
                            foreach (Polyline item in SubResult.GetBoundaries())
                            {
                                HoleUnionResult.AddRange(item.SmartOffset(Margin));
                            }

                            SubResult.DeepDispose();
                        }
                    }

                    ParsedHole.Dispose();
                }

                HoleUnionResultList.RemoveCommun(HoleUnionResult).DeepDispose();
            }
        }

        //Remove part that is leaving inside 2 polygon, they will be calculated after. 
        foreach (Polyline? Hole in HoleUnionResult.ToList())
        {
            int MaxNumberOfContainPolygon = 2;
            foreach (Polyline polyHole in PolyHoleList.GetBoundaries())
            {
                if (Hole?.IsDisposed != false)
                {
                    continue;
                }

                if (Hole.GetInnerCentroid().IsInsidePolyline(polyHole))
                {
                    MaxNumberOfContainPolygon--;
                }

                //if MaxNumberOfContainPolygon reach 0, that mean the hole is inside two or more boundary
                if (MaxNumberOfContainPolygon <= 0)
                {
                    _ = HoleUnionResult.Remove(Hole);
                    Hole.Dispose();
                }
            }
        }

        //Inner hole, get intersection
        foreach (PolyHole PolyHoleA in PolyHoleList)
        {
            foreach (PolyHole PolyHoleB in PolyHoleList)
            {
                if (PolyHoleB == PolyHoleA)
                {
                    continue;
                }

                foreach (Polyline HoleA in PolyHoleA.Holes)
                {
                    foreach (Polyline HoleB in PolyHoleB.Holes)
                    {
                        _ = Intersection(new PolyHole(HoleA, null), new PolyHole(HoleB, null), out List<PolyHole>? IntersectResult);
                        HoleUnionResult.AddRange(IntersectResult.GetBoundaries());
                    }
                }
            }
        }

        return HoleUnionResult;
    }

    private static List<PolyHole> OffsetPolyHole(ref PolyHole polyHole, double OffsetDistance)
    {
        List<PolyHole> polyHoles = [];
        List<Polyline> OffsetCurve;
        if (polyHole.Boundary.Area <= Generic.MediumTolerance.EqualPoint)
        {
            //degenrated geometry
            return polyHoles;
        }

        OffsetCurve = OffsetDistance < 0
            ? polyHole.Boundary.SmartOffset(OffsetDistance).ToList()
            : polyHole.Boundary.SmartOffset(OffsetDistance).ToList();
        if (OffsetCurve.Count == 0)
        {
            Generic.WriteMessage(
                $"Невозможно объединить кривые (ошибка при смещении контуров). Значение смещения: {OffsetDistance}.");
            return polyHoles;
            throw new Exception("Невозможно объединить кривые (ошибка при смещении контуров).");
        }

        polyHole.Boundary.Dispose();

        if (OffsetCurve.Count == 1)
        {
            polyHole.Boundary = OffsetCurve.First();
            polyHoles.Add(polyHole);
            return polyHoles;
        }

        return PolyHole.CreateFromList(OffsetCurve, polyHole.Holes);
    }

    private static ConcurrentBag<(HashSet<Polyline> Splitted, Polyline GeometryOrigin)> GetSplittedCurves(
        List<Polyline> Polylines)
    {
        //This function split each polygon by other polygon intersection points
        ConcurrentBag<(HashSet<Polyline> Splitted, Polyline GeometryOrigin)> SplittedCurvesOrigin = [];

        foreach (Polyline PolyBase in Polylines)
        {
            if (PolyBase.IsDisposed)
            {
                continue;
            }

            Point3dCollection GlobalIntersectionPointsFounds = [];
            Extents3d PolyBaseExtend = PolyBase.GetExtents();
            foreach (Polyline PolyCut in Polylines)
            {
                if (PolyCut == PolyBase)
                {
                    continue;
                }

                if (PolyCut.IsDisposed)
                {
                    continue;
                }

                if (PolyCut.GetExtents().CollideWithOrConnected(PolyBaseExtend))
                {
                    _ = PolyBase.IsSegmentIntersecting(PolyCut, out Point3dCollection? IntersectionPointsFounds,
                        Intersect.OnBothOperands);
                    _ = GlobalIntersectionPointsFounds.AddRange(IntersectionPointsFounds);
                }
            }

            if (GlobalIntersectionPointsFounds.Count > 0)
            {
                //Make sure all points are on the line because IntersectWith give not egnouht precise value (0.0001). This fix some cut
                Point3dCollection OnLineIntersectionPointsFounds = [.. GlobalIntersectionPointsFounds.ToArray()];
                foreach (Point3d item in GlobalIntersectionPointsFounds)
                {
                    Point3d newPt = PolyBase.GetClosestPointTo(item, false);
                    if (!OnLineIntersectionPointsFounds.Contains(newPt))
                    {
                        _ = OnLineIntersectionPointsFounds.Add(newPt);
                    }
                }

                DoubleCollection SplitDouble = PolyBase.GetSplitPoints(OnLineIntersectionPointsFounds);
                var Splitted = PolyBase.TryGetSplitCurves(SplitDouble).Cast<Polyline>().ToHashSet();

                //Remove zero length line
                foreach (Polyline? curv in Splitted.ToList())
                {
                    if (curv.Length <= Generic.LowTolerance.EqualPoint)
                    {
                        _ = Splitted.Remove(curv);
                        curv.Dispose();
                    }
                }

                SplittedCurvesOrigin.Add((Splitted, PolyBase));
            }
            else
            {
                SplittedCurvesOrigin.Add((new HashSet<Polyline> { PolyBase }, PolyBase));
            }
        }

        return SplittedCurvesOrigin;
    }
}
