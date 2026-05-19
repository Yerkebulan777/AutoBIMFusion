using System.Diagnostics;
using AutoBIMFusion.Common.Extensions;

namespace AutoBIMFusion.Common.Mist.AutoCAD;

public static class ViewportLock
{
    public static void DoLockUnlock(bool @lock)
    {
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();

        TypedValue[] viewportFilter = [new((int)DxfCode.Start, "Viewport")];

        try
        {
            var viewportSelection = ed.SelectAll(new SelectionFilter(viewportFilter));
            var selectionSet = viewportSelection.Value;
            if (selectionSet is null) return;

            using var trx = db.TransactionManager.StartTransaction();
            foreach (var objectId in selectionSet.GetObjectIds())
            {
                var viewport = (Viewport)objectId.GetDBObject(OpenMode.ForWrite);
                viewport.Locked = @lock;
            }

            trx.Commit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
}
