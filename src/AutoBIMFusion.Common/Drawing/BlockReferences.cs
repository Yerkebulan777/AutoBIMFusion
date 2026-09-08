using AutoBIMFusion.Common.Helpers;

namespace AutoBIMFusion.Common.Drawing;

public static class BlockReferences
{
    /// <summary>
    ///     Находит границы BlockReference с максимальной площадью среди переданных ObjectId.
    /// </summary>
    public static Extents3d? FindLargestBlockReferenceBoundsByArea(Transaction trx, IEnumerable<ObjectId> ids)
    {
        Extents3d? bestExtents = null;
        double bestArea = 0.0;

        foreach (ObjectId id in ids)
        {
            if (trx.GetObject(id, OpenMode.ForRead) is not BlockReference br)
                continue;

            Extents3d? ext = ExtentsUtils.TryGetExtents(br);
            if (!ext.HasValue)
                continue;

            double width = ext.Value.MaxPoint.X - ext.Value.MinPoint.X;
            double height = ext.Value.MaxPoint.Y - ext.Value.MinPoint.Y;
            double area = width * height;

            if (area > bestArea)
            {
                bestArea = area;
                bestExtents = ext.Value;
            }
        }

        return bestExtents;
    }

    /// <summary>
    ///     Стирает все сущности внутри BlockTableRecord.
    /// </summary>
    public static void EraseBlockContents(Database db, ObjectId btrId)
    {
        if (btrId.IsNull)
            return;

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForRead);

        foreach (ObjectId id in btr)
        {
            if (trx.GetObject(id, OpenMode.ForWrite) is Entity entity && !entity.IsErased)
                entity.Erase();
        }

        trx.Commit();
    }
}
