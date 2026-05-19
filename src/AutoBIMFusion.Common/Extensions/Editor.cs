using AutoBIMFusion.Common.Mist;
using System.Drawing;

namespace AutoBIMFusion.Common.Extensions;

public enum AngleUnit
{
    Radians,
    Degrees
}

public static class EditorExtensions
{
    public static double GetUSCRotation(this Editor ed, AngleUnit angleUnit)
    {
        Matrix3d ucsCur = ed.CurrentUserCoordinateSystem;
        CoordinateSystem3d cs = ucsCur.CoordinateSystem3d;
        double ucs_rotAngle = cs.Xaxis.AngleOnPlane(new Plane(Point3d.Origin, Vector3d.ZAxis));
        if (angleUnit == AngleUnit.Radians)
        {
            return ucs_rotAngle;
        }

        return ucs_rotAngle * 180 / PI; //in egrees
    }

    public static SizeF GetCurrentViewSize(this Editor _)
    {
        //https://drive-cad-with-code.blogspot.com/2013/04/how-to-get-current-view-size.html
        //Get current view height
        double h = (double)Application.GetSystemVariable("VIEWSIZE");
        //Get current view width,
        //by calculate current view's width-height ratio
        var screen = (Point2d)Application.GetSystemVariable("SCREENSIZE");
        double w = h * (screen.X / screen.Y);
        return new SizeF((float)w, (float)h);
    }

    public static Extents3d GetCurrentViewBound(this Editor ed, double shrinkScale = 1.0)
    {
        //Get current view size
        SizeF vSize = ed.GetCurrentViewSize();

        double w = vSize.Width * shrinkScale;
        double h = vSize.Height * shrinkScale;

        //Get current view's centre.
        //Note, the centre point from VIEWCTR is in UCS and
        //need to be transformed back to World CS
        Point3d cent = ((Point3d)Application.GetSystemVariable("VIEWCTR")).TransformBy(ed.CurrentUserCoordinateSystem);

        Point3d minPoint = new(cent.X - (w / 2.0), cent.Y - (h / 2.0), 0);
        Point3d maxPoint = new(cent.X + (w / 2.0), cent.Y + (h / 2.0), 0);

        return new Extents3d(minPoint, maxPoint);
    }

    public static List<Layout> GetAllLayout(this Editor _)
    {
        Database db = Generic.GetDatabase();
        List<Layout> AllLayout = [];

        using (Transaction trx = db.TransactionManager.StartTransaction())
        {
            foreach (ObjectId btrId in db.BlockTableId.GetDBObject() as BlockTable)
            {
                var btr = btrId.GetDBObject() as BlockTableRecord;

                if (btr.IsLayout && !btr.Name.Equals(BlockTableRecord.ModelSpace,
                        StringComparison.InvariantCultureIgnoreCase))
                {
                    var layout = btr.LayoutId.GetDBObject() as Layout;
                    if (!layout.ModelType)
                    {
                        AllLayout.Add(layout);
                    }
                }
            }

            trx.Commit();
        }

        return AllLayout;
    }

    public static Layout GetModelLayout(this Editor _)
    {
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        try
        {
            foreach (ObjectId btrId in db.BlockTableId.GetDBObject() as BlockTable)
            {
                var btr = btrId.GetDBObject() as BlockTableRecord;

                if (btr.Name.Equals(BlockTableRecord.ModelSpace, StringComparison.InvariantCultureIgnoreCase))
                {
                    var layout = btr.LayoutId.GetDBObject() as Layout;
                    if (layout.ModelType)
                    {
                        return layout;
                    }
                }
            }
        }
        finally
        {
            trx.Commit();
        }

        return null;
    }


    public static Layout GetLayoutFromName(this Editor _, string Name)
    {
        List<Layout> Layouts = _.GetAllLayout();
        foreach (Layout item in Layouts)
        {
            if (item.LayoutName.Equals(Name, StringComparison.InvariantCultureIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    public static Viewport GetViewport(this Editor ed)
    {
        Database db = Generic.GetDatabase();

        using Transaction trx = db.TransactionManager.StartTransaction();
        try
        {
            while (true)
            {
                if (ed.IsInLayoutViewport())
                {
                    return (Viewport)trx.GetObject(ed.CurrentViewportObjectId, OpenMode.ForWrite);
                }

                if (!ed.IsInLayoutPaper())
                {
                    //In model space
                    return null;
                }

                if (ed.IsInModel())
                {
                    ed.SwitchToPaperSpace();
                }

                // Get the BlockTableRecord for the current layout
                var btr = trx.GetObject(Generic.GetDatabase().CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                List<ObjectId> Viewports = ed.GetAllViewportsInPaperSpace(btr);
                if (Viewports.Count == 1)
                {
                    ed.SwitchToModelSpace();
                    return (Viewport)trx.GetObject(Viewports.First(), OpenMode.ForWrite);
                }

                ed.SwitchToModelSpace();
                PromptPointOptions promptPointOptions =
                    new("Activez la fenêtre CIBLE et appuyez sur ENTREE pour continuer.")
                    {
                        AllowNone = true,
                        AllowArbitraryInput = true
                    };
                PromptPointResult Validate = ed.GetPoint(promptPointOptions);
                if (!Validate.Status.HasFlag(PromptStatus.OK) && !Validate.Status.HasFlag(PromptStatus.None))
                {
                    ed.SwitchToPaperSpace();
                    return null;
                }
            }
        }
        finally
        {
            trx.Commit();
        }
    }

    public static bool GetBlocks(this Editor ed, out ObjectId[] objectId, string Message, bool SingleOnly = true,
        bool RejectObjectsOnLockedLayers = true)
    {
        objectId = Array.Empty<ObjectId>();
        TypedValue[] filterList = new[] { new TypedValue((int)DxfCode.Start, "INSERT") };
        PromptSelectionOptions selectionOptions = new()
        {
            MessageForAdding = Message,
            SingleOnly = SingleOnly,
            SinglePickInSpace = true,
            RejectObjectsOnLockedLayers = RejectObjectsOnLockedLayers
        };

        while (true)
        {
            (PromptStatus Status, object? Value) = ed.GetSelectionRedraw(selectionOptions, new SelectionFilter(filterList));

            if (Status == PromptStatus.Cancel)
            {
                return false;
            }

            if (Status == PromptStatus.OK)
            {
                if (Value is SelectionSet selection && selection.Count > 0)
                {
                    objectId = selection.GetObjectIds();
                    return true;
                }
            }
        }
    }


    public static bool GetBlock(this Editor ed, out ObjectId objectId, string Message = "Selectionnez un bloc",
        bool RejectObjectsOnLockedLayers = true)
    {
        objectId = ObjectId.Null;
        PromptEntityOptions selectionOptions = new(Message)
        {
            AllowObjectOnLockedLayer = RejectObjectsOnLockedLayers,
            AllowNone = false
        };

        while (true)
        {
            PromptEntityResult promptResult = ed.GetEntity(selectionOptions);
            if (promptResult.Status == PromptStatus.Cancel)
            {
                return false;
            }

            if (promptResult.Status == PromptStatus.OK)
            {
                if (promptResult.ObjectId != ObjectId.Null &&
                    promptResult.ObjectId.IsDerivedFrom(typeof(BlockReference)))
                {
                    objectId = promptResult.ObjectId;
                    return true;
                }
            }
        }
    }

    public static (PromptStatus Status, string StringResult) GetOptions(this Editor ed, string Message,
        bool LowerCaseOptions, params string[] Keywords)
    {
        PromptKeywordOptions options = new("");

        Dictionary<string, string> optionsDic = [];
        foreach (string item in Keywords)
        {
            string SanitizedName = item.SanitizeToAlphanumericHyphens();
            if (LowerCaseOptions)
            {
                SanitizedName = SanitizedName.ToLowerInvariant();
            }

            optionsDic.Add(SanitizedName, item);
        }


        foreach (string item in optionsDic.Keys)
        {
            options.Keywords.Add(item);
        }

        options.Message = Message;
        options.AppendKeywordsToMessage = true;
        options.AllowArbitraryInput = false;
        options.Keywords.Default = options.Keywords[0].GlobalName;
        options.AllowNone = false;
        PromptResult Result = ed.GetKeywords(options);
        if (optionsDic.TryGetValue(Result.StringResult, out string? SelectedStringResult))
        {
            return (Result.Status, SelectedStringResult);
        }

        return (PromptStatus.Error, Result.StringResult);
    }


    public static PromptSelectionResult GetCurves(this Editor ed, string Message, bool SingleOnly = true,
        bool RejectObjectsOnLockedLayers = true)
    {
        PromptSelectionOptions promptSelectionOptions = new()
        {
            MessageForAdding = Message,
            SingleOnly = SingleOnly,
            SinglePickInSpace = true,
            RejectObjectsOnLockedLayers = RejectObjectsOnLockedLayers
        };
        return ed.GetCurves(promptSelectionOptions);
    }

    public static SelectionFilter GetCurvesFilter(this Editor _)
    {
        return new SelectionFilter(new[]
        {
            new TypedValue((int)DxfCode.Operator, "<or"),
            new TypedValue((int)DxfCode.Start, "LWPOLYLINE"), //Regular
            new TypedValue((int)DxfCode.Start, "POLYLINE"), // 2D + 3D
            new TypedValue((int)DxfCode.Start, "LINE"),
            new TypedValue((int)DxfCode.Start, "ARC"),
            new TypedValue((int)DxfCode.Start, "ELLIPSE"),
            new TypedValue((int)DxfCode.Start, "CIRCLE"),
            new TypedValue((int)DxfCode.Start, "SPLINE"),
            new TypedValue((int)DxfCode.Start, "HELIX"),
            new TypedValue((int)DxfCode.Operator, "or>")
        });
    }

    public static PromptSelectionResult GetCurves(this Editor ed, PromptSelectionOptions promptSelectionOptions)
    {
        SelectionFilter filterList = ed.GetCurvesFilter();
        return ed.GetSelection(promptSelectionOptions, filterList);
    }

    public static bool GetImpliedSelection(this Editor ed, out PromptSelectionResult SelectionResult)
    {
        //Try to get the selection if PickFirst / implied selection is defined if true, we clear it
        SelectionResult = ed.SelectImplied();
        ed.SetImpliedSelection(Array.Empty<ObjectId>());
        return SelectionResult.Status == PromptStatus.OK;
    }

    public static void AddToImpliedSelection(this Editor ed, ObjectId objectId)
    {
        if (ed.GetImpliedSelection(out PromptSelectionResult? CurrentSelectionResult) && CurrentSelectionResult.Status == PromptStatus.OK)
        {
            ObjectId[] CurrentSelectionSet = CurrentSelectionResult.Value.GetObjectIds().ToArray();
            _ = CurrentSelectionSet.Append(objectId);
            ed.SetImpliedSelection(CurrentSelectionSet.ToArray());
        }
        else
        {
            ed.SetImpliedSelection(new ObjectId[1] { objectId });
        }
    }

    public static Polyline GetPolyline(this Editor ed, string Message, bool RejectObjectsOnLockedLayers = true,
        bool Clone = true)
    {
        return ed.GetPolyline(out _, Message, RejectObjectsOnLockedLayers, Clone, false);
    }

    public static (PromptStatus Status, object Value) GetSelectionRedraw(this Editor ed, string Message = null,
        bool RejectObjectsOnLockedLayers = true, bool SingleOnly = false, SelectionFilter selectionFilter = null,
        string[] Options = null)
    {
        Message ??= SingleOnly ? "Veuillez selectionner des entités" : "Veuillez selectionner une entité";

        PromptSelectionOptions selectionOptions = new()
        {
            MessageForAdding = Message,
            SingleOnly = SingleOnly,
            SinglePickInSpace = SingleOnly,
            RejectObjectsOnLockedLayers = RejectObjectsOnLockedLayers
        };

        if (Options != null)
        {
            foreach (string opt in Options)
            {
                selectionOptions.Keywords.Add(opt);
            }
        }

        selectionOptions.MessageForAdding += selectionOptions.Keywords.GetDisplayString(true);

        return ed.GetSelectionRedraw(selectionOptions, selectionFilter);
    }

    public static (PromptStatus Status, object Value) GetSelectionRedraw(this Editor ed,
        PromptSelectionOptions selectionOptions, SelectionFilter selectionFilter = null)
    {
        PromptSelectionResult selectResult;
        selectionOptions.KeywordInput += (sender, e) => throw new PromptSelectionKeywordEntered("Keyword entered")
        { Keyword = e.Input };

        try
        {
            for (int index = 0; ; index++)
            {
                if (index == 0)
                {
                    if (!ed.GetImpliedSelection(out selectResult))
                    {
                        selectResult = ed.GetSelection(selectionOptions, selectionFilter);
                    }
                }
                else
                {
                    selectResult = ed.GetSelection(selectionOptions, selectionFilter);
                }

                if (selectResult.Status == PromptStatus.Cancel)
                {
                    return (selectResult.Status, selectResult.Value);
                }

                if (selectResult.Status == PromptStatus.OK)
                {
                    return (selectResult.Status, selectResult.Value);
                }

                Generic.WriteMessage("Sélection invalide.");
            }
        }
        catch (PromptSelectionKeywordEntered ex)
        {
            return (PromptStatus.Keyword, ex.Message);
        }
    }

    public static Polyline GetPolyline(this Editor ed, out ObjectId EntObjectId, string Message,
        bool RejectObjectsOnLockedLayers = true, bool Clone = true, bool AllowOtherCurveType = true)
    {
        EntObjectId = ObjectId.Null;
        for (int index = 0; ; index++)
        {
            PromptSelectionResult polyResult;
            if (index == 0)
            {
                if (!ed.GetImpliedSelection(out polyResult))
                {
                    polyResult = ed.GetCurves(Message, true, RejectObjectsOnLockedLayers);
                }
            }
            else
            {
                polyResult = ed.GetCurves(Message, true, RejectObjectsOnLockedLayers);
            }

            if (polyResult.Status == PromptStatus.Error)
            {
                continue;
            }

            if (polyResult.Status != PromptStatus.OK)
            {
                return null;
            }

            EntObjectId = polyResult.Value[0].ObjectId;
            var SelectedEntity = EntObjectId.GetNoTransactionDBObject() as Entity;
            if (SelectedEntity is Polyline ProjectionTargetPolyline)
            {
                return Clone ? (Polyline)ProjectionTargetPolyline.Clone() : ProjectionTargetPolyline;
            }

            if (AllowOtherCurveType && SelectedEntity is Curve SelectedCurve)
            {
                if (SelectedCurve.ToPolyline() is Polyline ConvertedCurveAsPoly)
                {
                    return ConvertedCurveAsPoly;
                }
            }

            Generic.WriteMessage("L'objet sélectionné n'est pas une polyligne. \n");
        }
    }

    public static bool GetHatch(this Editor ed, out ObjectId HatchObjectId, string AskText = null)
    {
        HatchObjectId = ObjectId.Null;
        SelectionSet? BaseSelection = ed.SelectImplied()?.Value;
        if (BaseSelection?.Count > 0)
        {
            foreach (ObjectId item in BaseSelection.GetObjectIds())
            {
                DBObject Obj = item.GetDBObject();
                if (Obj is Hatch)
                {
                    HatchObjectId = item;
                    break;
                }
            }
        }

        if (HatchObjectId == ObjectId.Null)
        {
            PromptEntityOptions option =
                new("\n" +
                    (string.IsNullOrWhiteSpace(AskText) ? "Selectionnez une hachure" : AskText))
                {
                    AllowNone = true,
                    AllowObjectOnLockedLayer = false
                };
            option.SetRejectMessage("\nVeuillez selectionner seulement des hachures");
            option.AddAllowedClass(typeof(Hatch), false);
            PromptEntityResult Result = ed.GetEntity(option);
            if (Result.Status == PromptStatus.None)
            {
                return true;
            }

            if (Result.Status != PromptStatus.OK)
            {
                return false;
            }

            HatchObjectId = Result.ObjectId;
        }

        return true;
    }

    public static bool GetHatch(this Editor ed, out Hatch Hachure, string AskText = null)
    {
        Hachure = null;
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        try
        {
            while (true)
            {
                if (!ed.GetHatch(out ObjectId HatchObjectId, AskText))
                {
                    return false;
                }

                if (HatchObjectId.GetDBObject() is Hatch hatch)
                {
                    Hachure = hatch;
                    break;
                }
            }
        }
        finally
        {
            trx.Commit();
        }

        return true;
    }

    public static bool IsInLockedViewport(this Editor ed)
    {
        Database db = Generic.GetDatabase();

        using Transaction trx = db.TransactionManager.StartTransaction();
        var viewport = ed.ActiveViewportId.GetDBObject() as Viewport;
        trx.Commit();
        return viewport?.Locked == true;
    }

    public static bool IsInPaperSpace(this Editor ed)
    {
        Database db = Generic.GetDatabase();

        using Transaction trx = db.TransactionManager.StartTransaction();
        var viewport = ed.ActiveViewportId.GetDBObject() as Viewport;
        trx.Commit();
        return viewport?.Number == 1;
    }

    public static void ViewPlan(this Editor ed)
    {
        //From https://cadxp.com/topic/61249-net-c-%C3%A9quivalents-des-commandes-lisp-ucs-dview-plan/?do=findComment&comment=349377
        CoordinateSystem3d ucs = ed.CurrentUserCoordinateSystem.CoordinateSystem3d;
        using ViewTableRecord view = ed.GetCurrentView();
        Matrix3d dcsToWcs =
            Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target) *
            Matrix3d.Displacement(view.Target.GetAsVector()) *
            Matrix3d.PlaneToWorld(view.ViewDirection);
        Point3d centerPoint =
            new Point3d(view.CenterPoint.X, view.CenterPoint.Y, 0.0)
                .TransformBy(dcsToWcs);
        view.ViewDirection = ucs.Zaxis;
        view.ViewTwist = ucs.Xaxis.GetAngleTo(Vector3d.XAxis, ucs.Zaxis);
        Matrix3d wcsToDcs =
            Matrix3d.WorldToPlane(view.ViewDirection) *
            Matrix3d.Displacement(view.Target.GetAsVector().Negate()) *
            Matrix3d.Rotation(view.ViewTwist, view.ViewDirection, view.Target);
        centerPoint = centerPoint.TransformBy(wcsToDcs);
        view.CenterPoint = new Point2d(centerPoint.X, centerPoint.Y);
        ed.SetCurrentView(view);
    }

    private class PromptSelectionKeywordEntered : Exception
    {
        public string Keyword = string.Empty;

        public PromptSelectionKeywordEntered()
        {
        }

        public PromptSelectionKeywordEntered(string message) : base(message)
        {
        }

        public PromptSelectionKeywordEntered(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
