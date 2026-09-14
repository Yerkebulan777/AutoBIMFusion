namespace AutoBIMFusion.Common.Helpers;

public static class LayoutUtil
{
    /// <summary>
    ///     Находит первый Paper Space layout (с наименьшим TabOrder).
    ///     ModelType=true — служебный псевдо-layout, пропускается.
    /// </summary>
    public static bool TryFindFirstLayout(Database db, out string layoutName)
    {
        using Transaction trx = db.TransactionManager.StartTransaction();
        DBDictionary layoutDict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        layoutName = string.Empty;
        var bestOrder = int.MaxValue;

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
}
