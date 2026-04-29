namespace AutoBIMFusion.Application.Utils;

internal static class LayoutUtil
{
    /// <summary>
    /// Находит первый Paper Space layout (с наименьшим TabOrder).
    /// ModelType=true — служебный псевдо-layout, пропускается.
    /// </summary>
    internal static bool TryFindFirstLayout(Database db, out string layoutName)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();
        DBDictionary layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        layoutName = string.Empty;
        int bestOrder = int.MaxValue;

        foreach (DBDictionaryEntry entry in layoutDict)
        {
            Layout layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);

            if (layout.ModelType || layout.TabOrder >= bestOrder)
            {
                continue;
            }

            bestOrder = layout.TabOrder;
            layoutName = layout.LayoutName;
        }

        tr.Commit();
        return !string.IsNullOrEmpty(layoutName);
    }

    /// <summary>
    /// Возвращает ObjectId BlockTableRecord'а указанного layout'а (paper space btr).
    /// </summary>
    internal static ObjectId GetLayoutBtrId(Database db, string layoutName)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();
        DBDictionary dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        if (!dict.Contains(layoutName))
        {
            tr.Commit();
            return ObjectId.Null;
        }

        ObjectId layoutId = dict.GetAt(layoutName);
        Layout layout = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
        ObjectId btrId = layout.BlockTableRecordId;
        
        tr.Commit();
        return btrId;
    }

    /// <summary>
    /// Перечисляет сущности Paper Space указанного листа.
    /// Viewport'ы (включая служебный Number==1) исключаются, если excludeViewports=true.
    /// </summary>
    internal static ObjectIdCollection GetPaperSpaceEntities(
        Database db, string layoutName, bool excludeViewports)
    {
        RXClass viewportClass = RXObject.GetClass(typeof(Viewport));
        ObjectIdCollection result = [];

        using Transaction tr = db.TransactionManager.StartTransaction();
        ObjectId btrId = GetLayoutBtrId(db, layoutName);

        if (btrId.IsNull)
        {
            tr.Commit();
            return result;
        }

        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

        foreach (ObjectId id in btr)
        {
            if (excludeViewports && id.ObjectClass.IsDerivedFrom(viewportClass))
            {
                continue;
            }

            _ = result.Add(id);
        }

        tr.Commit();
        return result;
    }
}
