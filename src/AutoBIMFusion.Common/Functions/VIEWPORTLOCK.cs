using System.Diagnostics;
using SioForgeCAD.Commun;

namespace AutoBIMFusion.Common.Functions;

public static class VIEWPORTLOCK
{
    public static void Menu()
    {
        var ed = Generic.GetEditor();
        var promptKeywordOptions = new PromptKeywordOptions("Veuillez selectionner une opération :")
        {
            AllowArbitraryInput = false,
            AppendKeywordsToMessage = true
        };
        promptKeywordOptions.Keywords.Add("Lock All");
        promptKeywordOptions.Keywords.Default = "Lock All";
        promptKeywordOptions.Keywords.Add("Unlock All");

        var KeyResult = ed.GetKeywords(promptKeywordOptions);
        if (!KeyResult.Status.HasFlag(PromptStatus.OK) && !KeyResult.Status.HasFlag(PromptStatus.Keyword)) return;

        DoLockUnlock(KeyResult.StringResult == "Lock");
    }

    public static void DoLockUnlock(bool Lock)
    {
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();
        TypedValue[] viewportFilter = { new((int)DxfCode.Start, "Viewport") };
        try
        {
            var viewportSelection = ed.SelectAll(new SelectionFilter(viewportFilter));
            var selectionSet = viewportSelection.Value;
            if (selectionSet is null) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var objectId in selectionSet?.GetObjectIds())
                {
                    var viewport = (Viewport)objectId.GetDBObject(OpenMode.ForWrite);
                    viewport.Locked = Lock;
                }

                tr.Commit();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
}
