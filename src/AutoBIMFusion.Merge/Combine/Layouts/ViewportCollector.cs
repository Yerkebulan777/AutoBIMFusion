using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Helpers;

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

        var viewportClass = RXObject.GetClass(typeof(Viewport));

        foreach (var id in btr)
        {
            if (!id.ObjectClass.IsDerivedFrom(viewportClass)) continue;

            var vp = (Viewport)trx.GetObject(id, OpenMode.ForRead);

            if (vp.Number == 1 || !vp.On) continue;

            var scale = vp.ResolveCustomScale();

            if (scale <= 0) continue;

            var window = vp.ComputeModelWindow();
            var viewCenterWcs = vp.GetViewCenterWcs();

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
