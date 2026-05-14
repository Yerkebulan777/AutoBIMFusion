using AutoBIMFusion.Common.Extensions;
using System.Diagnostics;

namespace AutoBIMFusion.Common.Mist.Geometry.PolygonOperations;

public static partial class PolygonOperation
{
    public static bool Substraction(PolyHole BasePolygon, IEnumerable<Polyline> SubstractionPolygonsArg,
        out List<PolyHole> UnionResult)
    {
        List<Curve> NewBoundaryHoles = [];
        List<Polyline> CuttedPolyline = [BasePolygon.Boundary];

        //Add existing hole to the substraction if not present
        List<Polyline> SubstractionPolygons = SubstractionPolygonsArg.AddRangeUnique(BasePolygon.Holes);

        foreach (Curve SubstractionPolygonCurve in SubstractionPolygons.ToArray())
        {
            if (SubstractionPolygonCurve?.IsDisposed == true)
            {
                Debug.WriteLine("Error : SubstractionPolygonCurve was null or disposed");
                continue;
            }

            using Polyline SimplifiedSubstractionPolygonCurve = SubstractionPolygonCurve.ToPolyline();
            if (SimplifiedSubstractionPolygonCurve != null)
            {
                foreach (Polyline NewBoundary in CuttedPolyline.ToArray())
                {
                    if (NewBoundary.IsSegmentIntersecting(SimplifiedSubstractionPolygonCurve, out _,
                            Intersect.OnBothOperands))
                    {
                        //pts.AddToDrawing(5);
                        List<Polyline> Cuts = Slice(NewBoundary, SimplifiedSubstractionPolygonCurve);
                        //if the boundary was cuted 
                        if (Cuts.Count > 0)
                        {
                            _ = CuttedPolyline.Remove(NewBoundary);
                            if (NewBoundary != BasePolygon.Boundary)
                            {
                                //dont dispose item that we don't own
                                NewBoundary.Dispose();
                            }
                        }

                        foreach (Polyline CuttedNewBoundary in Cuts)
                        {
                            //If cutted is inside a substraction polygon, we ignore it,
                            //we check if Cuts.Count > 1, if is inside and Cuts.Count == 1, mean that IsSegmentIntersecting have false result
                            if (CuttedNewBoundary.GetInnerCentroid()
                                    .IsInsidePolyline(SimplifiedSubstractionPolygonCurve) &&
                                Cuts.Count > 1)
                            {
                                continue;
                            }

                            CuttedPolyline.Add(CuttedNewBoundary);
                        }

                        Cuts.RemoveCommun(CuttedPolyline).DeepDispose();
                    }
                    else
                    {
                        //If the substraction is not cutting the edge, then the subs is inside hole
                        if (SimplifiedSubstractionPolygonCurve.IsInside(NewBoundary, false))
                        {
                            NewBoundaryHoles.Add(SubstractionPolygonCurve);
                        }
                    }
                }
            }
        }

        //Merge overlaping hole polyline
        _ = Union(PolyHole.CreateFromList(NewBoundaryHoles.Cast<Polyline>()), out List<PolyHole>? HoleUnionResult);
        NewBoundaryHoles.RemoveCommun(SubstractionPolygonsArg).RemoveCommun(BasePolygon.Holes).DeepDispose();
        UnionResult = PolyHole.CreateFromList(CuttedPolyline, HoleUnionResult.GetBoundaries());
        return true;
    }
}
