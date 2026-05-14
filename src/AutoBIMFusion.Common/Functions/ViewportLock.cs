using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist;
using System.Diagnostics;

namespace AutoBIMFusion.Common.Functions;

public static class ViewportLock
{
    public static void Menu()
    {
        Editor ed = Generic.GetEditor();

        const string lockAllKeyword = "Заблокировать все";
        const string unlockAllKeyword = "Разблокировать все";

        PromptKeywordOptions promptKeywordOptions = new("Выберите операцию:")
        {
            AllowArbitraryInput = false,
            AppendKeywordsToMessage = true
        };
        promptKeywordOptions.Keywords.Add(lockAllKeyword);
        promptKeywordOptions.Keywords.Default = lockAllKeyword;
        promptKeywordOptions.Keywords.Add(unlockAllKeyword);

        PromptResult keyResult = ed.GetKeywords(promptKeywordOptions);
        if (!keyResult.Status.HasFlag(PromptStatus.OK) && !keyResult.Status.HasFlag(PromptStatus.Keyword))
        {
            return;
        }

        DoLockUnlock(keyResult.StringResult == lockAllKeyword);
    }

    public static void DoLockUnlock(bool @lock)
    {
        Database db = Generic.GetDatabase();
        Editor ed = Generic.GetEditor();

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
