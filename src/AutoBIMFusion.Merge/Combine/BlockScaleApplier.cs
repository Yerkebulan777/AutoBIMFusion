using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Logging;
using AutoBIMFusion.Common.Mist;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Нормализует масштаб определения блока и всех его вставок.
/// </summary>
public static class BlockScaleApplier
{
    /// <summary>
    ///     Нормализует масштаб определения блока и всех его вставок в переданной базе.
    /// </summary>
    public static void NormalizeBlockScale(Database db, Transaction trx, BlockReference blockRef,
        HashSet<string> processedBlocks)
    {
        ArgumentNullException.ThrowIfNull(blockRef);
        var log = LoggerFactory.GetSharedLogger();

        var blockDefinitionId = GetBlockDefinitionId(blockRef);
        if (!blockDefinitionId.IsValid || blockDefinitionId.IsNull)
        {
            log.Warning("BlockScaleApplier: у вставки блока нет корректного определения.");
            return;
        }

        var btr = (BlockTableRecord)trx.GetObject(blockDefinitionId, OpenMode.ForWrite);

        var blockName = btr.Name;

        if (!processedBlocks.Add(blockName)) return;

        if (btr.IsFromExternalReference || btr.IsDependent)
        {
            log.Warning(
                $"BlockScaleApplier: блок \"{blockName}\" является внешним или зависимым, нормализация пропущена.");
            return;
        }

        if (!IsUniformScaleAllowNegative(blockRef))
        {
            log.Warning($"BlockScaleApplier: блок \"{blockName}\" не имеет равномерного масштаба.");
            return;
        }

        var refScale = Abs(blockRef.ScaleFactors.X);
        if (refScale < Generic.LowTolerance.EqualVector)
        {
            log.Warning($"BlockScaleApplier: блок \"{blockName}\" имеет нулевой масштаб, нормализация пропущена.");
            return;
        }

        if (Abs(refScale - 1.0) < Generic.LowTolerance.EqualVector && btr.Units == db.Insunits) return;

        if (btr.Units != db.Insunits) btr.Units = db.Insunits;

        var scaleMatrix = Matrix3d.Scaling(refScale, Point3d.Origin);

        foreach (var entId in btr)
            try
            {
                if (trx.GetObject(entId, OpenMode.ForWrite, false, true) is Entity ent) ent.TransformBy(scaleMatrix);
            }
            catch (Exception ex)
            {
                log.Warning(ex, $"BlockScaleApplier: не удалось масштабировать объект блока \"{blockName}\".");
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
        return Abs(Abs(br.ScaleFactors.X) - Abs(br.ScaleFactors.Y)) < Generic.LowTolerance.EqualVector &&
               Abs(Abs(br.ScaleFactors.X) - Abs(br.ScaleFactors.Z)) < Generic.LowTolerance.EqualVector;
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
