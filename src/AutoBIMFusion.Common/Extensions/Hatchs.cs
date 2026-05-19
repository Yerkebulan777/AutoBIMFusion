using AutoBIMFusion.Common.Mist;
using AutoBIMFusion.Common.Mist.AutoCAD;
using AutoBIMFusion.Common.Mist.Geometry.PolygonOperations;

namespace AutoBIMFusion.Common.Extensions;

public static class HatchsExtensions
{
    public static bool GetPolyHole(this Hatch Hachure, out PolyHole polyHole)
    {
        polyHole = null;
        if (!Hachure.GetHatchPolyline(out List<Curve>? ExternalCurves, out List<(Curve curve, HatchLoopTypes looptype)>? OtherCurves))
        {
            return false;
        }

        List<Curve>? ExternalMergedCurves = ExternalCurves.JoinMerge();
        try
        {
            ExternalCurves.RemoveCommun(ExternalMergedCurves).DeepDispose();
            List<Curve> InnerCurves = OtherCurves.ConvertAll(tuple => tuple.curve);
            if (Hachure.HatchStyle == HatchStyle.Ignore)
            {
                InnerCurves.DeepDispose();
                InnerCurves.Clear();
            }

            List<Curve> InnerMergedCurves = InnerCurves.JoinMerge();
            InnerCurves.RemoveCommun(InnerMergedCurves).DeepDispose();

            if (Hachure is null || ExternalMergedCurves is null || ExternalMergedCurves.Count == 0)
            {
                Generic.WriteMessage("Impossible de découpper cette hachure.");
                return false;
            }

            if (ExternalMergedCurves.Count > 1)
            {
                Generic.WriteMessage("Impossible de découpper une hachure combinée.");
                return false;
            }

            Polyline Boundary = ExternalMergedCurves[0].ToPolyline();
            if (Boundary.TryGetArea() == 0)
            {
                Generic.WriteMessage(
                    "Erreur, ompossible de découpper cette hachure pour le moment. Réouvrir le dessin peux aider à résoudre ce soucis");
                Boundary.Dispose();
                return false;
            }

            polyHole = new PolyHole(Boundary, InnerMergedCurves.Cast<Polyline>());
            return true;
        }
        finally
        {
            ExternalMergedCurves.DeepDispose();
        }
    }

    public static double GetAssociatedBoundary(this Hatch Hachure, out Curve Boundary)
    {
        ObjectIdCollection objectIdCollection = Hachure.GetAssociatedObjectIds();
        Boundary = null;
        if (objectIdCollection.Count >= 1)
        {
            Boundary = objectIdCollection[0].GetNoTransactionDBObject(OpenMode.ForWrite) as Curve;
            //If boundary is on a locked layer, we cannot give it back
            if (Boundary.IsEntityOnLockedLayer())
            {
                return 0;
            }
        }

        return objectIdCollection.Count;
    }

    public static bool GetHatchPolyline(this Hatch Hachure, out List<Curve> ExternalCurves,
        out List<(Curve curve, HatchLoopTypes looptype)> OtherCurves)
    {
        ExternalCurves = [];
        OtherCurves = [];

        foreach ((Curve? curve, HatchLoopTypes looptype) in GetHatchBoundary(Hachure))
        {
            Hachure.CopyPropertiesTo(curve);
            if (looptype.HasFlag(HatchLoopTypes.External))
            {
                ExternalCurves.Add(curve);
            }
            else
            {
                OtherCurves.Add((curve, looptype));
            }
        }

        return true;
    }

    private static List<(Curve, HatchLoopTypes)> GetHatchBoundary(Hatch hatch)
    {
        var numberOfLoops = hatch.NumberOfLoops;
        List<(Curve, HatchLoopTypes)> result = new(numberOfLoops);
        for (var i = 0; i < numberOfLoops; i++)
        {
            HatchLoop loop = hatch.GetLoopAt(i);
            if (loop.IsPolyline)
            {
                BulgeVertexCollection bulges = loop.Polyline;
                Polyline pline = new(bulges.Count);
                for (var j = 0; j < bulges.Count; j++)
                {
                    BulgeVertex vertex = bulges[j];
                    pline.AddVertexAt(j, vertex.Vertex, vertex.Bulge, 0.0, 0.0);
                }

                pline.Elevation = hatch.Elevation;
                pline.Normal = hatch.Normal;
                result.Add((pline, loop.LoopType));
            }
            else
            {
                Plane plane = hatch.GetPlane();
                Matrix3d xform = Matrix3d.PlaneToWorld(plane);
                Curve2dCollection curves = loop.Curves;
                foreach (Curve2d curve in curves)
                {
                    Curve spline = curve.ConvertToCurve();
                    spline.TransformBy(xform);
                    result.Add((spline, loop.LoopType));
                }
            }
        }

        return result;
    }

    public static Hatch HatchRegion(this Region region, Transaction trx, bool Associative = true)
    {
        // Create a hatch and set its properties
        Hatch hatch = new();
        _ = Generic.GetCurrentSpaceBlockTableRecord(trx).AppendEntity(hatch);
        trx.AddNewlyCreatedDBObject(hatch, true);

        hatch.Associative = Associative;

        // Add the hatch loops and complete the hatch
        foreach ((HatchLoopTypes loopType, Curve2dCollection edgePtrs, IntegerCollection edgeTypes) item in
                 region.GetLoops())
        {
            hatch.AppendLoop(item.loopType, item.edgePtrs, item.edgeTypes);
        }

        hatch.EvaluateHatch(true);
        return hatch;
    }

    public static void RemoveAllLoops(this Hatch hatch)
    {
        for (var i = 0; i < hatch.NumberOfLoops; i++)
        {
            hatch.RemoveLoopAt(i);
        }
    }
}
