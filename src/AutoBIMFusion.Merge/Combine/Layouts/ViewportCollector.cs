using AutoBIMFusion.Common.Extensions;

namespace AutoBIMFusion.Merge.Combine.Layouts;

/// <summary>
///     Перечисляет активные Viewport'ы указанного листа и собирает ViewportInfo.
///     Служебный vpt с Number == 1 исключается.
///     Координатные вычисления делегируются к <see cref="ViewportsExtensions"/>.
/// </summary>
internal static class ViewportCollector
{
    internal static List<ViewportInfo> Collect(Database db, string layoutName)
    {
        List<ViewportInfo> result = [];

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

        RXClass viewportClass = RXObject.GetClass(typeof(Viewport));

        foreach (ObjectId id in btr)
        {
            if (!id.ObjectClass.IsDerivedFrom(viewportClass))
            {
                continue;
            }

            Viewport vp = (Viewport)trx.GetObject(id, OpenMode.ForRead);

            if (vp.Number == 1 || !vp.On)
            {
                continue;
            }

            double scale = vp.ResolveCustomScale();

            if (scale <= 0)
            {
                continue;
            }

            Extents3d window = vp.ComputeModelWindow();
            Point3d viewCenterWcs = vp.GetViewCenterWcs();

            ViewportInfo info = new(
                id,
                vp.Number,
                vp.CenterPoint,
                vp.Width,
                vp.Height,
                viewCenterWcs,
                vp.ViewHeight,
                vp.TwistAngle,
                scale,
                window);
            result.Add(info);
        }

        trx.Commit();
        return result;
    }
}
