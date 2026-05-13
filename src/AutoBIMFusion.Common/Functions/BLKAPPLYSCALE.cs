using SioForgeCAD.Commun;
using SioForgeCAD.Commun.Extensions;
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

        if (!ed.GetBlocks(out ObjectId[]? perObjIds, "Selectionnez un bloc"))
        {
            return;
        }

        var AlreadyAppliedScale = new HashSet<string>();

        using Transaction tr = db.TransactionManager.StartTransaction();

        foreach (ObjectId perObjId in perObjIds)
        {
            if (perObjId.GetDBObject() is BlockReference blockRef)
            {
                var BlkName = blockRef.GetBlockReferenceName();

                if (AlreadyAppliedScale.Contains(BlkName))
                {
                    // Несколько вставок одного и того же блока уже обработаны, повторно их пропускаем.
                    continue;
                }

                if (!IsUniformScaleAllowNegative(blockRef))
                {
                    Generic.WriteMessage($"Le bloc \"{BlkName}\" n'a pas une échelle uniforme.");
                    continue;
                }

                _ = AlreadyAppliedScale.Add(BlkName);

                var refScale = Abs(blockRef.ScaleFactors.X);

                BlockTableRecord btr = blockRef.GetBlocDefinition(OpenMode.ForWrite);

                if (Abs(refScale - 1.0) < Generic.LowTolerance.EqualVector && btr.Units == db.Insunits)
                {
                    Generic.WriteMessage($"Le bloc \"{BlkName}\" est déjà à l'échelle 1.");
                    continue;
                }

                if (btr.Units != db.Insunits)
                {
                    btr.Units = db.Insunits;
                }

                var scaleMatrix = Matrix3d.Scaling(refScale, Point3d.Origin);

                foreach (ObjectId entId in btr)
                {
                    try
                    {
                        var ent = entId.GetDBObject(OpenMode.ForWrite) as Entity;
                        ent?.TransformBy(scaleMatrix);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.ToString());
                    }
                }

                // Корректируем все ссылки на блок.
                var differentScalesFound = false;
                foreach (ObjectId item in blockRef.GetAllBlkDefinition())
                {
                    var ent = item.GetDBObject(OpenMode.ForWrite) as BlockReference;

                    Scale3d oldScale = ent.ScaleFactors;

                    if (Abs(oldScale.X - refScale) > Generic.LowTolerance.EqualVector)
                    {
                        differentScalesFound = true;
                    }

                    var scaleFactor = 1.0 / refScale;

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

        tr.Commit();
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
