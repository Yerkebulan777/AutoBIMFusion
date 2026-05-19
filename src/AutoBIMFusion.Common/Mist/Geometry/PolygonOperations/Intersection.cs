using AutoBIMFusion.Common.Extensions;

namespace AutoBIMFusion.Common.Mist.Geometry.PolygonOperations;

public static partial class PolygonOperation
{
    public static bool Intersection(PolyHole PolyHoleA, PolyHole PolyHoleB, out List<PolyHole> IntersectionResult)
    {
        IntersectionResult = [];
        List<PolyHole> BoundaryIntersectionResult = [];

        if (PolyHoleA.Boundary.IsSegmentIntersecting(PolyHoleB.Boundary, out _, Intersect.OnBothOperands))
        {
            var SliceResult = Slice(PolyHoleA.Boundary, PolyHoleB.Boundary);
            foreach (var item in SliceResult)
                if (item.GetInnerCentroid().IsInsidePolyline(PolyHoleA.Boundary) &&
                    item.GetInnerCentroid().IsInsidePolyline(PolyHoleB.Boundary))
                    BoundaryIntersectionResult.Add(new PolyHole(item, null));
                else
                    item.Dispose();
        }
        else
        {
            if (PolyHoleA.Boundary.IsInside(PolyHoleB.Boundary, false))
                BoundaryIntersectionResult.Add(PolyHoleA);
            else if (PolyHoleB.Boundary.IsInside(PolyHoleA.Boundary, false)) BoundaryIntersectionResult.Add(PolyHoleB);
        }

        List<Polyline> PolyHoleHoles = [.. PolyHoleA.Holes, .. PolyHoleB.Holes];

        if (PolyHoleHoles.Count == 0)
        {
            //If there is no hole
            IntersectionResult.AddRange(BoundaryIntersectionResult);
            return true;
        }

        //if there is hole, we substract them from the boundary
        foreach (var boundary in BoundaryIntersectionResult.ToList())
        {
            _ = Substraction(boundary, PolyHoleHoles, out var TempIntersectionResult);
            IntersectionResult.AddRange(TempIntersectionResult);
        }

        return true;
    }
}
