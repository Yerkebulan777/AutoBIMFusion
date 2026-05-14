using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist;

namespace AutoBIMFusion.Common.Functions;

public static class AdvancedSelections
{
    /// <summary>
    /// Выбирает объекты, пересекающие или находящиеся внутри выбранной полилинии.
    /// </summary>
    public static void InsideCrossingPolyline()
    {
        Database db = Generic.GetDatabase();
        Editor ed = Generic.GetEditor();

        using Polyline? Boundary = ed.GetPolyline(out ObjectId EntObjectId, "\nSélectionnez une polyligne qui delimite / croise les objects à selectionner", false);
        if (Boundary is null)
        {
            return;
        }

        Boundary.Cleanup();
        Point3dCollection collection = Boundary.GetPoints().Distinct().ToPoint3dCollection().ConvertToUCS();
        ViewTableRecord SavedView = ed.GetCurrentView();
        Boundary?.GetExtents().ZoomExtents();
        PromptSelectionResult SelectCrossingPolygonResult = ed.SelectCrossingPolygon(collection);
        if (SelectCrossingPolygonResult.Status != PromptStatus.OK)
        {
            Generic.WriteMessage("Une erreur s'est produite lors de la selection");
            ed.SetCurrentView(SavedView);
            return;
        }

        List<ObjectId>? Objects = SelectCrossingPolygonResult?.Value?.GetObjectIds()?.ToList();
        using Transaction trx = db.TransactionManager.StartTransaction();
        _ = Objects.Remove(EntObjectId);
        ed.SetImpliedSelection(Objects.ToArray());
        ed.SetCurrentView(SavedView);
        trx.Commit();
    }

    /// <summary>
    /// Выбирает только объекты, полностью находящиеся внутри выбранной полилинии.
    /// </summary>
    public static void InsideStrictPolyline()
    {
        Database db = Generic.GetDatabase();
        Editor ed = Generic.GetEditor();
        using Polyline? Boundary =
               ed.GetPolyline("\nSélectionnez une polyligne qui delimite les objects à selectionner", false);
        if (Boundary is null)
        {
            return;
        }

        Boundary.Cleanup();
        Point3dCollection collection = Boundary.GetPoints().Distinct().ToPoint3dCollection().ConvertToUCS();
        ViewTableRecord SavedView = ed.GetCurrentView();
        Boundary?.GetExtents().ZoomExtents();

        PromptSelectionResult SelectWindowPolygonResult = ed.SelectWindowPolygon(collection);

        if (SelectWindowPolygonResult.Status != PromptStatus.OK)
        {
            Generic.WriteMessage("Une erreur s'est produite lors de la selection");
            ed.SetCurrentView(SavedView);
            return;
        }

        SelectionSet? Objects = SelectWindowPolygonResult?.Value;
        using Transaction trx = db.TransactionManager.StartTransaction();
        ed.SetImpliedSelection(Objects);
        ed.SetCurrentView(SavedView);
        trx.Commit();
    }



}
