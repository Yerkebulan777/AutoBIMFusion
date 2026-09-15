using AutoBIMFusion.Common.Extensions;
using Serilog.Core;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Нормализует масштаб определения блока и всех его вставок.
/// </summary>
public static class BlockScaleApplier
{
    private const double ScaleEpsilon = 1e-3;

    /// <summary>
    ///     Нормализует масштаб определения блока и всех его вставок в переданной базе.
    /// </summary>
    public static void NormalizeBlockScale(Database db, Transaction trx, BlockReference blockRef,
        HashSet<string> processedBlocks, Logger log)
    {
        ArgumentNullException.ThrowIfNull(blockRef);
        ArgumentNullException.ThrowIfNull(log);

        var blockDefinitionId = GetBlockDefinitionId(blockRef);
        if (!blockDefinitionId.IsValid || blockDefinitionId.IsNull)
        {
            log.Warning("Block {Handle}: insert has no valid definition", blockRef.Handle);
            return;
        }

        var btr = (BlockTableRecord)trx.GetObject(blockDefinitionId, OpenMode.ForWrite);

        var blockName = btr.Name;

        if (!processedBlocks.Add(blockName)) return;

        if (btr.IsFromExternalReference || btr.IsDependent)
        {
            log.Debug("Block {BlockName}: skipped xref or dependent definition", blockName);
            return;
        }

        if (!IsUniformScaleAllowNegative(blockRef))
        {
            log.Warning("Block {BlockName}: non-uniform scale, normalization skipped", blockName);
            return;
        }

        var refScale = Abs(blockRef.ScaleFactors.X);
        if (refScale < ScaleEpsilon)
        {
            log.Warning("Block {BlockName}: near-zero scale, normalization skipped", blockName);
            return;
        }

        if (Abs(refScale - 1.0) < ScaleEpsilon && btr.Units == db.Insunits) return;

        if (btr.Units != db.Insunits) btr.Units = db.Insunits;

        var scaleMatrix = Matrix3d.Scaling(refScale, Point3d.Origin);

        foreach (var entId in btr)
            try
            {
                if (trx.GetObject(entId, OpenMode.ForWrite, false, true) is Entity ent) ent.TransformBy(scaleMatrix);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Block {BlockName}: failed to scale nested entity", blockName);
            }

        var scaleFactor = 1.0 / refScale;

        foreach (ObjectId blockRefId in GetBlockReferenceIds(trx, btr))
        {
            if (trx.GetObject(blockRefId, OpenMode.ForWrite, false, true) is not BlockReference otherBlockRef) continue;

            var oldScale = otherBlockRef.ScaleFactors;

            otherBlockRef.ScaleFactors = new Scale3d(
                oldScale.X * scaleFactor,
                oldScale.Y * scaleFactor,
                oldScale.Z * scaleFactor
            );
            otherBlockRef.RecordGraphicsModified(true);
        }

        btr.UpdateAnonymousBlocks();
    }

    /// <summary>
    ///     Проверяет, что масштаб блока одинаков по всем осям.
    ///     Отрицательные значения допускаются, если модуль масштаба совпадает.
    /// </summary>
    private static bool IsUniformScaleAllowNegative(BlockReference br)
    {
        return Abs(Abs(br.ScaleFactors.X) - Abs(br.ScaleFactors.Y)) < ScaleEpsilon &&
               Abs(Abs(br.ScaleFactors.X) - Abs(br.ScaleFactors.Z)) < ScaleEpsilon;
    }

    private static ObjectId GetBlockDefinitionId(BlockReference blockRef)
    {
        return blockRef.IsDynamicBlock ? blockRef.DynamicBlockTableRecord : blockRef.BlockTableRecord;
    }

    private static ObjectIdCollection GetBlockReferenceIds(Transaction trx, BlockTableRecord btr)
    {
        ObjectIdCollection result = [];
        result.Join(btr.GetBlockReferenceIds(true, true));

        if (!btr.IsDynamicBlock) return result;

        foreach (ObjectId anonymousBtrId in btr.GetAnonymousBlockIds())
        {
            var anonymousBtr = (BlockTableRecord)trx.GetObject(anonymousBtrId, OpenMode.ForRead);
            result.Join(anonymousBtr.GetBlockReferenceIds(true, true));
        }

        return result;
    }
}
