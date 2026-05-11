using Autodesk.AutoCAD.Runtime;

namespace AutoBIMFusion.Common.Helpers;

public static class LayoutUtil
{
    /// <summary>
    ///     Находит первый Paper Space layout (с наименьшим TabOrder).
    ///     ModelType=true — служебный псевдо-layout, пропускается.
    /// </summary>
    public static bool TryFindFirstLayout(Database db, out string layoutName)
    {
        using var trx = db.TransactionManager.StartTransaction();
        var layoutDict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        layoutName = string.Empty;
        var bestOrder = int.MaxValue;

        foreach (var entry in layoutDict)
        {
            var layout = (Layout)trx.GetObject(entry.Value, OpenMode.ForRead);

            if (layout.ModelType || layout.TabOrder >= bestOrder) continue;

            bestOrder = layout.TabOrder;
            layoutName = layout.LayoutName;
        }

        trx.Commit();
        return !string.IsNullOrEmpty(layoutName);
    }

    /// <summary>
    ///     Возвращает ObjectId BlockTableRecord'а указанного layout'а (paper space btr).
    /// </summary>
    public static ObjectId GetLayoutBtrId(Database db, string layoutName)
    {
        using var trx = db.TransactionManager.StartTransaction();
        var dict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        if (!dict.Contains(layoutName))
        {
            trx.Commit();
            return ObjectId.Null;
        }

        var layoutId = dict.GetAt(layoutName);
        var layout = (Layout)trx.GetObject(layoutId, OpenMode.ForRead);
        var btrId = layout.BlockTableRecordId;

        trx.Commit();
        return btrId;
    }

    /// <summary>
    ///     Перечисляет сущности Paper Space указанного листа в одной транзакции.
    ///     Viewport'ы исключаются, если excludeViewports=true.
    /// </summary>
    public static ObjectIdCollection GetPaperSpaceEntities(
        Database db, string layoutName, bool excludeViewports)
    {
        var viewportClass = RXObject.GetClass(typeof(Viewport));
        ObjectIdCollection result = [];

        using var trx = db.TransactionManager.StartTransaction();
        var layoutDict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        if (!layoutDict.Contains(layoutName))
        {
            trx.Commit();
            return result;
        }

        var layoutId = layoutDict.GetAt(layoutName);
        var layout = (Layout)trx.GetObject(layoutId, OpenMode.ForRead);
        var btr = (BlockTableRecord)trx.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);

        foreach (var id in btr)
        {
            if (excludeViewports && id.ObjectClass.IsDerivedFrom(viewportClass)) continue;

            _ = result.Add(id);
        }

        trx.Commit();
        return result;
    }
}
