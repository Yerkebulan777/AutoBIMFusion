using AutoBIMFusion.Common.Extensions;
using System.Diagnostics;

namespace AutoBIMFusion.Common.AcadSupport;

public static class ViewportLock
{
    public static void DoLockUnlock(bool @lock)
    {
        Database db = AcadContext.GetDatabase();
        Editor ed = AcadContext.GetEditor();

        TypedValue[] viewportFilter = [new((int)DxfCode.Start, "Viewport")];

        try
        {
            PromptSelectionResult viewportSelection = ed.SelectAll(new SelectionFilter(viewportFilter));
            SelectionSet? selectionSet = viewportSelection.Value;
            if (selectionSet is null)
            {
                return;
            }

            using Transaction trx = db.TransactionManager.StartTransaction();
            foreach (ObjectId objectId in selectionSet.GetObjectIds())
            {
                Viewport viewport = (Viewport)objectId.GetDBObject(OpenMode.ForWrite);
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
