using Autodesk.AutoCAD.Colors;
using SioForgeCAD.Commun;
using SioForgeCAD.Commun.Extensions;

namespace SioForgeCAD.Functions;

public static class SPECIALSSELECTIONS
{
    public static void AllOnCurrentLayer()
    {
        var ed = Generic.GetEditor();
        var tvs = new[]
        {
            new TypedValue((int)DxfCode.LayerName, Layers.GetCurrentLayerName())
        };
        var sf = new SelectionFilter(tvs);
        var psr = ed.SelectAll(sf);
        ed.SetImpliedSelection(psr.Value);
    }

    public static void AllOnSelectedEntitiesLayers()
    {
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();

        var SelRedraw = ed.GetSelectionRedraw();
        if (SelRedraw.Status != PromptStatus.OK) return;

        using (var tr = db.TransactionManager.StartTransaction())
        {
            var LayerNames = new List<string>();
            foreach (var SelItem in SelRedraw.Value.GetObjectIds())
            {
                var LayerName = SelItem.GetEntity().Layer;
                if (!LayerNames.Contains(LayerName)) LayerNames.Add(LayerName);
            }

            var typedValues = new List<TypedValue>
            {
                new((int)DxfCode.Operator, "<or")
            };
            foreach (var LayerName in LayerNames)
            {
                Generic.WriteMessage($"Selection des entités sur le calque \"{LayerName}\"");
                typedValues.Add(new TypedValue((int)DxfCode.LayerName, LayerName));
            }

            typedValues.Add(new TypedValue((int)DxfCode.Operator, "or>"));
            var sf = new SelectionFilter(typedValues.ToArray());
            var psr = ed.SelectAll(sf);
            ed.SetImpliedSelection(psr.Value);
            tr.Commit();
        }
    }

    public static void AllWithSelectedEntitiesColors()
    {
        //    (entget (car (entsel)))
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();

        var SelRedraw = ed.GetSelectionRedraw();
        if (SelRedraw.Status != PromptStatus.OK) return;

        using (var tr = db.TransactionManager.StartTransaction())
        {
            var EntColors = new List<Color>();
            foreach (var SelItem in SelRedraw.Value.GetObjectIds())
            {
                var EntColor = SelItem.GetEntity().Color;
                if (!EntColors.Contains(EntColor)) EntColors.Add(EntColor);
            }

            var psr = ed.SelectAll();
            if (psr.Status != PromptStatus.OK)
            {
                tr.Commit();
                return;
            }

            var SameColorEntsObjId = new HashSet<ObjectId>();
            foreach (var SelItem in psr.Value.GetObjectIds())
            {
                var EntColor = SelItem.GetEntity().Color;
                if (EntColors.Contains(EntColor)) SameColorEntsObjId.Add(SelItem);
            }

            ed.SetImpliedSelection(SameColorEntsObjId.ToArray());
            tr.Commit();
        }
    }

    public static void AllWithSelectedEntitiesTypes()
    {
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();

        var SelRedraw = ed.GetSelectionRedraw();
        if (SelRedraw.Status != PromptStatus.OK) return;


        Type GetTypeFromObjectId(ObjectId ObjId)
        {
            var SelItemEnt = ObjId.GetDBObject();
            if (AssocArray.IsAssociativeArray(ObjId)) return typeof(AssocArray);

            if (SelItemEnt is Viewport) return typeof(Viewport);

            return SelItemEnt.GetType();
        }


        using (var tr = db.TransactionManager.StartTransaction())
        {
            var EntityTypes = new List<Type>();
            foreach (var SelItemObjId in SelRedraw.Value.GetObjectIds())
            {
                var SelItemType = GetTypeFromObjectId(SelItemObjId);
                if (!EntityTypes.Contains(SelItemType)) EntityTypes.Add(SelItemType);
            }

            //Check each object, we can't use SelectionFilter because ACAD_PROXY_ENTITY is ""
            var psr = ed.SelectAll();
            if (psr.Status != PromptStatus.OK)
            {
                tr.Commit();
                return;
            }

            var SameTypeEntsObjId = new HashSet<ObjectId>();

            foreach (var SelItemObjId in psr.Value.GetObjectIds())
            {
                var SelItemType = GetTypeFromObjectId(SelItemObjId);
                if (EntityTypes.Contains(SelItemType)) SameTypeEntsObjId.Add(SelItemObjId);
            }


            ed.SetImpliedSelection(SameTypeEntsObjId.ToArray());
            tr.Commit();
        }
    }

    public static void AllWithSelectedEntitiesTransparency()
    {
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();

        var SelRedraw = ed.GetSelectionRedraw();
        if (SelRedraw.Status != PromptStatus.OK) return;

        using (var tr = db.TransactionManager.StartTransaction())
        {
            var EntitiesTransparency = new List<string>();
            foreach (var SelItem in SelRedraw.Value.GetObjectIds())
            {
                var SelItemEnt = SelItem.GetEntity();

                var EntTransparency = SelItemEnt.Transparency.ToString().RemoveNonNumeric();
                if (!EntitiesTransparency.Contains(EntTransparency)) EntitiesTransparency.Add(EntTransparency);
            }

            var typedValues = new List<TypedValue>
            {
                new((int)DxfCode.Operator, "<or")
            };
            foreach (var EntTransparency in EntitiesTransparency)
            {
                Generic.WriteMessage($"Selection des objets ayant \"{EntTransparency}\" de transparence");
                typedValues.Add(new TypedValue((int)DxfCode.Alpha, int.Parse(EntTransparency)));
            }

            typedValues.Add(new TypedValue((int)DxfCode.Operator, "or>"));
            var sf = new SelectionFilter(typedValues.ToArray());
            var psr = ed.SelectAll(sf);
            ed.SetImpliedSelection(psr.Value);
            tr.Commit();
        }
    }

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
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Objects.Remove(EntObjectId);
                ed.SetImpliedSelection(Objects.ToArray());
                ed.SetCurrentView(SavedView);
                tr.Commit();
            }
        }
    }

    public static void InsideStrictPolyline()
    {
        //https://forums.autodesk.com/t5/net/cannot-get-the-entities-using-selectcrossingpolygon-and/td-p/6384137
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
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ed.SetImpliedSelection(Objects);
                ed.SetCurrentView(SavedView);
                tr.Commit();
            }
        }
    }

    public static void AllBlockWithSelectedBlocksNames()
    {
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();

        var SelRedraw = ed.GetSelectionRedraw();
        if (SelRedraw.Status != PromptStatus.OK) return;

        using (var tr = db.TransactionManager.StartTransaction())
        {
            var BlocksNames = new List<string>();
            foreach (var SelItem in SelRedraw.Value.GetObjectIds())
                if (SelItem.GetDBObject() is BlockReference SelBlkRef)
                {
                    var BlkName = SelBlkRef.GetBlockReferenceName();
                    if (!BlocksNames.Contains(BlkName)) BlocksNames.Add(BlkName);
                }

            var psr = ed.SelectAll();
            if (psr.Status != PromptStatus.OK)
            {
                tr.Commit();
                return;
            }

            var BlocksWithSameName = new HashSet<ObjectId>();
            foreach (var SelItem in psr.Value.GetObjectIds())
                if (SelItem.GetDBObject() is BlockReference SelBlkRef)
                {
                    var BlkName = SelBlkRef.GetBlockReferenceName();
                    if (BlocksNames.Contains(BlkName)) BlocksWithSameName.Add(SelItem);
                }

            ed.SetImpliedSelection(BlocksWithSameName.ToArray());
            tr.Commit();
        }
    }
}
