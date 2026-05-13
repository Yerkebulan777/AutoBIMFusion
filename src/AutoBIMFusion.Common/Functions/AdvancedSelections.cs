using Autodesk.AutoCAD.Colors;
using SioForgeCAD.Commun;
using SioForgeCAD.Commun.Extensions;

namespace AutoBIMFusion.Common.Functions;

public static class AdvancedSelections
{
    /// <summary>
    /// Выбирает объекты, пересекающие или находящиеся внутри выбранной полилинии.
    /// </summary>
    public static void InsideCrossingPolyline()
    {
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();
        using (var Boundary = ed.GetPolyline(out var EntObjectId,
                   "\nSélectionnez une polyligne qui delimite / croise les objects à selectionner", false))
        {
            if (Boundary is null) return;
            Boundary.Cleanup();
            var collection = Boundary.GetPoints().Distinct().ToPoint3dCollection().ConvertToUCS();
            var SavedView = ed.GetCurrentView();
            Boundary?.GetExtents().ZoomExtents();
            var SelectCrossingPolygonResult = ed.SelectCrossingPolygon(collection);
            if (SelectCrossingPolygonResult.Status != PromptStatus.OK)
            {
                Generic.WriteMessage("Une erreur s'est produite lors de la selection");
                ed.SetCurrentView(SavedView);
                return;
            }

            var Objects = SelectCrossingPolygonResult?.Value?.GetObjectIds()?.ToList();
            using (var trx = db.TransactionManager.StartTransaction())
            {
                Objects.Remove(EntObjectId);
                ed.SetImpliedSelection(Objects.ToArray());
                ed.SetCurrentView(SavedView);
                trx.Commit();
            }
        }
    }

    /// <summary>
    /// Выбирает только объекты, полностью находящиеся внутри выбранной полилинии.
    /// </summary>
    public static void InsideStrictPolyline()
    {
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();
        using (var Boundary =
               ed.GetPolyline("\nSélectionnez une polyligne qui delimite les objects à selectionner", false))
        {
            if (Boundary is null) return;
            Boundary.Cleanup();
            var collection = Boundary.GetPoints().Distinct().ToPoint3dCollection().ConvertToUCS();
            var SavedView = ed.GetCurrentView();
            Boundary?.GetExtents().ZoomExtents();

            var SelectWindowPolygonResult = ed.SelectWindowPolygon(collection);

            if (SelectWindowPolygonResult.Status != PromptStatus.OK)
            {
                Generic.WriteMessage("Une erreur s'est produite lors de la selection");
                ed.SetCurrentView(SavedView);
                return;
            }

            var Objects = SelectWindowPolygonResult?.Value;
            using (var trx = db.TransactionManager.StartTransaction())
            {
                ed.SetImpliedSelection(Objects);
                ed.SetCurrentView(SavedView);
                trx.Commit();
            }
        }
    }



}
