using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist;
using System.Diagnostics;

namespace AutoBIMFusion.Common.Functions;

/// <summary>
///     Применяет масштаб выбранного блока к его определению.
///     Затем синхронизирует все ссылки на этот блок, чтобы сохранить их визуальные пропорции.
/// </summary>
public static class BlockScaleApplier
{
    /// <summary>
    ///     Выбирает блок, проверяет равномерность его масштаба,
    ///     масштабирует геометрию определения блока и обновляет все вставки.
    /// </summary>
    public static void ApplyBlockScale()
    {
        Database db = Generic.GetDatabase();
        Editor ed = Generic.GetEditor();

        if (!ed.GetBlocks(out ObjectId[]? perObjIds, "Выберите блок"))
        {
            return;
        }

        HashSet<string> AlreadyAppliedScale = [];

        using Transaction trx = db.TransactionManager.StartTransaction();

        foreach (ObjectId perObjId in perObjIds)
        {
            if (perObjId.GetDBObject() is BlockReference blockRef)
            {
                string BlkName = blockRef.GetBlockReferenceName();

                if (AlreadyAppliedScale.Contains(BlkName))
                {
                    // Несколько вставок одного и того же блока уже обработаны, повторно их пропускаем.
                    continue;
                }

                if (!IsUniformScaleAllowNegative(blockRef))
                {
                    Generic.WriteMessage($"Блок \"{BlkName}\" не имеет равномерного масштаба.");
                    continue;
                }

                _ = AlreadyAppliedScale.Add(BlkName);

                double refScale = Abs(blockRef.ScaleFactors.X);

                BlockTableRecord btr = blockRef.GetBlocDefinition(OpenMode.ForWrite);

                if (Abs(refScale - 1.0) < Generic.LowTolerance.EqualVector && btr.Units == db.Insunits)
                {
                    Generic.WriteMessage($"Блок \"{BlkName}\" уже имеет масштаб 1.");
                    continue;
                }

                if (btr.Units != db.Insunits)
                {
                    btr.Units = db.Insunits;
                }

                Matrix3d scaleMatrix = Matrix3d.Scaling(refScale, Point3d.Origin);

                foreach (ObjectId entId in btr)
                {
                    try
                    {
                        Entity? ent = entId.GetDBObject(OpenMode.ForWrite) as Entity;
                        ent?.TransformBy(scaleMatrix);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.ToString());
                    }
                }

                // Корректируем все ссылки на блок.
                bool differentScalesFound = false;
                foreach (ObjectId item in blockRef.GetAllBlkDefinition())
                {
                    BlockReference? ent = item.GetDBObject(OpenMode.ForWrite) as BlockReference;

                    Scale3d oldScale = ent.ScaleFactors;

                    if (Abs(oldScale.X - refScale) > Generic.LowTolerance.EqualVector)
                    {
                        differentScalesFound = true;
                    }

                    double scaleFactor = 1.0 / refScale;

                    ent.ScaleFactors = new Scale3d(
                        oldScale.X * scaleFactor,
                        oldScale.Y * scaleFactor,
                        oldScale.Z * scaleFactor
                    );
                }

                if (differentScalesFound)
                {
                    Generic.WriteMessage($"⚠ Некоторые ссылки на блок \"{BlkName}\" имели другой масштаб. Пропорции были сохранены.");
                }

                blockRef.RegenAllBlkDefinition();
            }
        }

        trx.Commit();
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
}
