using System.Diagnostics;
using SioForgeCAD.Commun;
using SioForgeCAD.Commun.Extensions;

namespace SioForgeCAD.Functions;

public static class BLKAPPLYSCALE
{
    public static void ApplyBlockScale()
    {
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();

        if (!ed.GetBlocks(out var perObjIds, "Selectionnez un bloc")) return;

        var AlreadyAppliedScale = new HashSet<string>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (var perObjId in perObjIds)
            {
                if (!(perObjId.GetDBObject() is BlockReference blockRef)) continue;

                var BlkName = blockRef.GetBlockReferenceName();

                if (AlreadyAppliedScale.Contains(BlkName))
                    //We have multiple instances of the blk in the array, ignore if already parsed
                    continue;

                if (!IsUniformScaleAllowNegative(blockRef))
                {
                    Generic.WriteMessage($"Le bloc \"{BlkName}\" n'a pas une échelle uniforme.");
                    continue;
                }

                AlreadyAppliedScale.Add(BlkName);

                var refScale = Abs(blockRef.ScaleFactors.X);

                var btr = blockRef.GetBlocDefinition(OpenMode.ForWrite);

                if (Abs(refScale - 1.0) < Generic.LowTolerance.EqualVector && btr.Units == db.Insunits)
                {
                    Generic.WriteMessage($"Le bloc \"{BlkName}\" est déjà à l'échelle 1.");
                    continue;
                }

                if (btr.Units != db.Insunits) btr.Units = db.Insunits;

                var scaleMatrix = Matrix3d.Scaling(refScale, Point3d.Origin);
                foreach (var entId in btr)
                    try
                    {
                        var ent = entId.GetDBObject(OpenMode.ForWrite) as Entity;
                        ent?.TransformBy(scaleMatrix);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.ToString());
                    }


                // Fix all ref blk
                var differentScalesFound = false;
                foreach (ObjectId item in blockRef.GetAllBlkDefinition())
                {
                    var ent = item.GetDBObject(OpenMode.ForWrite) as BlockReference;

                    var oldScale = ent.ScaleFactors;

                    if (Abs(oldScale.X - refScale) > Generic.LowTolerance.EqualVector) differentScalesFound = true;

                    var scaleFactor = 1.0 / refScale;

                    ent.ScaleFactors = new Scale3d(
                        oldScale.X * scaleFactor,
                        oldScale.Y * scaleFactor,
                        oldScale.Z * scaleFactor
                    );
                }

                if (differentScalesFound)
                    Generic.WriteMessage(
                        $"⚠ Certaines références du bloc \"{BlkName}\" avaient une échelle différente. Les proportions ont été conservées.");
                blockRef.RegenAllBlkDefinition();
            }

            tr.Commit();
        }
    }

    private static bool IsUniformScaleAllowNegative(BlockReference br)
    {
        return Abs(Abs(br.ScaleFactors.X) - Abs(br.ScaleFactors.Y)) < Generic.LowTolerance.EqualVector &&
               Abs(Abs(br.ScaleFactors.X) - Abs(br.ScaleFactors.Z)) < Generic.LowTolerance.EqualVector;
    }
}
