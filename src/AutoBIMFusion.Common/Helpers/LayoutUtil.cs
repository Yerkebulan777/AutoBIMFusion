using Autodesk.AutoCAD.Runtime;

namespace AutoBIMFusion.Common.Helpers;

public static class LayoutUtil
{
    /// <summary>
    /// Находит первый Paper Space layout (с наименьшим TabOrder).
    /// ModelType=true — служебный псевдо-layout, пропускается.
    /// </summary>
    public static bool TryFindFirstLayout(Database db, out string layoutName)
    {
        using Transaction trx = db.TransactionManager.StartTransaction();
        DBDictionary layoutDict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        layoutName = string.Empty;
        int bestOrder = int.MaxValue;

        foreach (DBDictionaryEntry entry in layoutDict)
        {
            Layout layout = (Layout)trx.GetObject(entry.Value, OpenMode.ForRead);

            if (layout.ModelType || layout.TabOrder >= bestOrder)
            {
                continue;
            }

            bestOrder = layout.TabOrder;
            layoutName = layout.LayoutName;
        }

        trx.Commit();
        return !string.IsNullOrEmpty(layoutName);
    }

    /// <summary>
    /// Возвращает ObjectId BlockTableRecord'а указанного layout'а (paper space btr).
    /// </summary>
    public static ObjectId GetLayoutBtrId(Database db, string layoutName)
    {
        using Transaction trx = db.TransactionManager.StartTransaction();
        DBDictionary dict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        if (!dict.Contains(layoutName))
        {
            trx.Commit();
            return ObjectId.Null;
        }

        ObjectId layoutId = dict.GetAt(layoutName);
        Layout layout = (Layout)trx.GetObject(layoutId, OpenMode.ForRead);
        ObjectId btrId = layout.BlockTableRecordId;

        trx.Commit();
        return btrId;
    }

    /// <summary>
    /// Перечисляет сущности Paper Space указанного листа в одной транзакции.
    /// Viewport'ы исключаются, если excludeViewports=true.
    /// </summary>
    public static ObjectIdCollection GetPaperSpaceEntities(
        Database db, string layoutName, bool excludeViewports)
    {
        RXClass viewportClass = RXObject.GetClass(typeof(Viewport));
        ObjectIdCollection result = [];

        using Transaction trx = db.TransactionManager.StartTransaction();
        DBDictionary layoutDict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        if (!layoutDict.Contains(layoutName))
        {
            trx.Commit();
            return result;
        }

        ObjectId layoutId = layoutDict.GetAt(layoutName);
        Layout layout = (Layout)trx.GetObject(layoutId, OpenMode.ForRead);
        BlockTableRecord btr = (BlockTableRecord)trx.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);

        foreach (ObjectId id in btr)
        {
            if (excludeViewports && id.ObjectClass.IsDerivedFrom(viewportClass))
            {
                continue;
            }

            _ = result.Add(id);
        }

        trx.Commit();
        return result;
    }
}


