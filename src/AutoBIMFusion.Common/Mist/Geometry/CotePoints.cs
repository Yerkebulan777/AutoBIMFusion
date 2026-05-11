using Autodesk.AutoCAD.Runtime;
using SioForgeCAD.Commun.Extensions;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace SioForgeCAD.Commun;

public class CotePoints
{
    public enum SelectionPointsType
    {
        Points = 0,
        Bloc = 1
    }

    public static readonly CotePoints Null = null;

    public CotePoints(Points Points, double Altitude)
    {
        this.Points = Points;
        this.Altitude = Altitude;
    }

    public CotePoints(Points Points, string Altitude)
    {
        this.Points = Points;
        if (double.TryParse(Altitude, out var AltitudeDbl))
            this.Altitude = AltitudeDbl;
        else
            this.Altitude = 0;
    }

    public Points Points { get; }
    public double Altitude { get; }

    private static SelectionPointsType GetSelectionPointsType()
    {
        if (Enum.TryParse(Properties.Settings.Default.SelectionPointsType, out SelectionPointsType SelectionPointsType))
            return SelectionPointsType;

        return SelectionPointsType.Points;
    }

    public static string FormatAltitude(double? Altitude, int NumberOfDecimal = 2)
    {
        if (Altitude == null) Altitude = 0;
        return Altitude?.ToString($"#.{new string('0', NumberOfDecimal)}");
    }

    private static void SaveSelectionPointsType(SelectionPointsType SelectionPointsType)
    {
        Properties.Settings.Default.SelectionPointsType = SelectionPointsType.ToString();
        Properties.Settings.Default.Save();
    }

    public static bool NullPointExit(CotePoints cotePoints)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        if (cotePoints is null)
            using (var tr = db.TransactionManager.StartTransaction())
            {
                HightLighter.UnhighlightAll();
                tr.Commit();
                return true;
            }

        return false;
    }

    private static double GetCote(Points Origin)
    {
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();

        //Write a point where the altitude is asked
        var PointDrawingEntity = new DBPoint(Origin.SCG);
        ObjectId PointDrawingEntityObjectId;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var acBlkTbl = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
            var acBlkTblRec = Generic.GetCurrentSpaceBlockTableRecord(tr);
            acBlkTblRec.AppendEntity(PointDrawingEntity);
            tr.AddNewlyCreatedDBObject(PointDrawingEntity, true);
            PointDrawingEntityObjectId = PointDrawingEntity.ObjectId;
            tr.Commit();
        }

        var PromptDoubleAltitudeOptions = new PromptDoubleOptions("Saississez la cote\n")
        {
            AllowNegative = false,
            AllowNone = false
        };
        var PromptDoubleAltitudeResult = ed.GetDouble(PromptDoubleAltitudeOptions);

        //remove the point where the altitude is asked
        if (!PointDrawingEntityObjectId.IsErased)
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var obj = PointDrawingEntityObjectId.GetObject(OpenMode.ForWrite);
                obj.Erase(true);
                tr.Commit();
            }

        return PromptDoubleAltitudeResult.Value;
    }

    public static double? GetAltitudeFromBloc(ObjectId BlocObjectId)
    {
        var db = Generic.GetDatabase();
        var tr = db.TransactionManager;
        var BlocObject = tr.GetObject(BlocObjectId, OpenMode.ForRead);
        if (BlocObject is BlockReference blkRef) return GetAltitudeFromBloc(blkRef);
        return null;
    }

    public static double? GetAltitudeFromBloc(BlockReference blkRef)
    {
        var db = Generic.GetDatabase();
        var tr = db.TransactionManager;

        foreach (ObjectId AttributeObjectId in blkRef.AttributeCollection)
        {
            var Attribute = (AttributeReference)tr.GetObject(AttributeObjectId, OpenMode.ForRead, true);
            if (Attribute.TextString.Contains("."))
            {
                var IsDouble = double.TryParse(Attribute.TextString.Trim(), out var Altimetrie);
                if (IsDouble)
                {
                    Generic.WriteMessage($"Cote sélectionnée : {FormatAltitude(Altimetrie)}");
                    blkRef.RegisterHighlight();
                    return Altimetrie;
                }

                var ExtractedAltitude = ExtractDoubleInStringFromPoint(Attribute.TextString.Trim());
                if (ExtractedAltitude.HasValue)
                {
                    blkRef.RegisterHighlight();
                    return ExtractedAltitude;
                }
            }
        }

        return null;
    }

    public static double? ExtractDoubleInStringFromPoint(string OriginalString)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;

        if (OriginalString.Contains("%"))
        {
            Generic.WriteMessage(
                "Par mesure de sécurité, les textes contenant des % ne peuvent être convertis en cote.");
            return null;
        }

        var StringPointPosition = OriginalString.AllIndexesOf(".").ToArray();
        var NumberValueBeforePoint = "";
        var NumberValueAfterPoint = "";

        foreach (var index in StringPointPosition)
        {
            var n = index;
            while (n > 0 && char.IsDigit(OriginalString[n - 1]))
            {
                NumberValueBeforePoint = OriginalString[n - 1] + NumberValueBeforePoint;
                n--;
            }

            n = index;
            while (OriginalString.Length > n + 1 && char.IsDigit(OriginalString[n + 1]))
            {
                NumberValueAfterPoint += OriginalString[n + 1].ToString();
                n++;
            }

            if (string.IsNullOrWhiteSpace(NumberValueBeforePoint) || string.IsNullOrWhiteSpace(NumberValueAfterPoint))
                //Not sure if this is a cote
                return null;

            var FinalNumberString = $"{NumberValueBeforePoint}.{NumberValueAfterPoint}";
            var IsValidNumber = double.TryParse(FinalNumberString, out var FinalNumberDouble);
            if (IsValidNumber)
            {
                ed.WriteMessage($"Côte détéctée : {FinalNumberString}\n");
                return FinalNumberDouble;
            }

            //No number found
            return null;
        }

        //Foreach return 0 element
        return null;
    }

    public static CotePoints GetBlockInXref(string Message, Point3d? NonInterractivePickedPoint,
        out PromptStatus PromptStatus)
    {
        var ed = Generic.GetEditor();
        BlockReference blkRef = null;
        List<ObjectId> XrefObjectId;

        var XrefSelection = SelectInXref.Select(Message, NonInterractivePickedPoint);
        XrefObjectId = XrefSelection.XrefObjectId.ToList();
        PromptStatus = XrefSelection.PromptStatus;
        if (XrefSelection.PromptStatus != PromptStatus.OK) return Null;
        if (XrefSelection.SelectedObjectId == ObjectId.Null) return Null;

        HightLighter.UnhighlightAll();
        XrefSelection.SelectedObjectId.RegisterHighlight();
        var XrefObject = XrefSelection.SelectedObjectId.GetDBObject();

        if (XrefObject is AttributeReference blkChildAttribute)
        {
            var DbObj = blkChildAttribute.OwnerId.GetDBObject();
            blkRef = DbObj as BlockReference;
        }
        else if (XrefObject is BlockReference XrefObjectBlkRef)
        {
            blkRef = XrefObjectBlkRef;
        }
        else
        {
            foreach (var objId in XrefSelection.XrefObjectId)
            {
                XrefObjectId.Remove(objId);
                XrefObject = objId.GetDBObject();

                if (XrefObject is BlockReference ParentBlkRef && !ParentBlkRef.IsXref())
                {
                    blkRef = XrefObject as BlockReference;
                    break;
                }
            }
        }

        if (blkRef is null) return Null;

        var Altimetrie = GetAltitudeFromBloc(blkRef);
        var BlockPosition = SelectInXref.TransformPointInXrefsToCurrent(blkRef.Position, XrefObjectId.ToArray());

        if (Altimetrie == null)
        {
            Altimetrie = blkRef.Position.Z;
            if (Altimetrie == 0) return new CotePoints(BlockPosition, 0);
            var options = new PromptKeywordOptions(
                $"Aucune cote n'a été trouvée pour ce bloc, cependant une altitude Z a été définie à {FormatAltitude(Altimetrie)}. Voulez-vous utiliser cette valeur ?\n");
            options.Keywords.Add("OUI");
            options.Keywords.Add("NON");
            options.AllowNone = true;
            var result = ed.GetKeywords(options);
            if (result.Status == PromptStatus.OK && result.StringResult != "OUI")
                return new CotePoints(BlockPosition, 0);
        }

        return new CotePoints(BlockPosition, Altimetrie ?? 0);
    }

    private static CotePoints GetBloc(string Message)
    {
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();

        while (true)
            using (var tr = db.TransactionManager.StartTransaction())
            {
                //list of available SelectionFilter : https://help.autodesk.com/view/ACD/2018/ENU/?guid=GUID-7D07C886-FD1D-4A0C-A7AB-B4D21F18E484
                var EntitiesGroupCodesList = new TypedValue[1] { new((int)DxfCode.Start, "INSERT") };
                var SelectionEntitiesFilter = new SelectionFilter(EntitiesGroupCodesList);
                var PromptSelectionKeyWordString = nameof(SelectionPointsType.Points).CapitalizeFirstLetters(2);
                var PromptBlocSelectionOptions = new PromptEntityOptions($"{Message} [{PromptSelectionKeyWordString}]")
                {
                    AllowNone = false,
                    AllowObjectOnLockedLayer = true
                };
                PromptBlocSelectionOptions.Keywords.Add(PromptSelectionKeyWordString);
                Entity SelectedObject;
                PromptEntityResult PromptBlocSelectionResult;
                do
                {
                    PromptBlocSelectionResult = ed.GetEntity(PromptBlocSelectionOptions);

                    if (PromptBlocSelectionResult.Status == PromptStatus.Cancel)
                    {
                        tr.Commit();
                        return null;
                    }

                    if (PromptBlocSelectionResult.Status == PromptStatus.Keyword)
                    {
                        tr.Commit();
                        throw new Exception(ErrorStatus.OK, PromptBlocSelectionResult.StringResult);
                    }

                    SelectedObject = PromptBlocSelectionResult.ObjectId.GetEntity();
                } while (!(SelectedObject is BlockReference));

                var blockReference = SelectedObject as BlockReference;
                var SelectedBlocObjectId = blockReference.ObjectId;
                var Altitude = GetAltitudeFromBloc(SelectedBlocObjectId);
                var CoteLocation = new Points(blockReference.Position);

                if (blockReference.IsXref())
                {
                    var CotePoint = GetBlockInXref(string.Empty, PromptBlocSelectionResult.PickedPoint, out _);
                    var IsCotePointNotNull = CotePoint != Null;
                    var IsAltimetrieDefined = (CotePoint?.Altitude ?? 0) != 0;
                    if (IsCotePointNotNull && IsAltimetrieDefined)
                    {
                        var AskKeepXREFCoteValuesOptions = new PromptKeywordOptions(
                            $"La cote {FormatAltitude(CotePoint.Altitude)} a été trouvée dans une XREF. Voulez-vous utiliser cette valeur ?\n");
                        AskKeepXREFCoteValuesOptions.Keywords.Add("Oui");
                        AskKeepXREFCoteValuesOptions.Keywords.Add("Non");
                        AskKeepXREFCoteValuesOptions.Keywords.Default = "Oui";
                        AskKeepXREFCoteValuesOptions.AllowNone = true;
                        var AskKeepXREFCoteValues = ed.GetKeywords(AskKeepXREFCoteValuesOptions);
                        if ((AskKeepXREFCoteValues.Status == PromptStatus.OK &&
                             AskKeepXREFCoteValues.StringResult == "Oui") ||
                            AskKeepXREFCoteValues.Status == PromptStatus.None)
                        {
                            Altitude = CotePoint.Altitude;
                            CoteLocation = CotePoint.Points;
                        }
                    }
                }

                if (Altitude == null)
                {
                    Generic.WriteMessage("Aucune côte détéctée");
                    tr.Commit();
                    continue;
                }

                tr.Commit();
                return new CotePoints(CoteLocation, Altitude ?? 0);
            }
    }

    public static CotePoints GetCotePoints(string Message, Points Origin)
    {
        var ed = Generic.GetEditor();
        var PromptPointOptions =
            new PromptPointOptions($"{Message} [{SelectionPointsType.Bloc}]\n", nameof(SelectionPointsType.Bloc));

        if (Origin != null)
        {
            PromptPointOptions.UseBasePoint = true;
            PromptPointOptions.BasePoint = Origin.SCG.Flatten();
            PromptPointOptions.UseDashedLine = true;
            PromptPointOptions.AppendKeywordsToMessage = true;
        }

        bool IsLooping;
        do
        {
            IsLooping = false;
            var SelectionPointsType = GetSelectionPointsType();

            if (SelectionPointsType == SelectionPointsType.Points)
            {
                var PromptPointResult = ed.GetPoint(PromptPointOptions);
                if (PromptPointResult.Status == PromptStatus.Keyword)
                {
                    SaveSelectionPointsType(SelectionPointsType.Bloc);
                    IsLooping = true;
                    continue;
                }

                if (PromptPointResult.Status == PromptStatus.OK)
                {
                    var CotePoint = Points.GetFromPromptPointResult(PromptPointResult);
                    var Altitude = GetCote(CotePoint);
                    return new CotePoints(CotePoint, Altitude);
                }
            }

            if (SelectionPointsType == SelectionPointsType.Bloc)
                try
                {
                    return GetBloc(Message);
                }
                catch (Exception ex)
                {
                    var AutEx = ex;
                    if (AutEx.ErrorStatus == ErrorStatus.OK &&
                        ex.Message.IgnoreCaseEquals(nameof(SelectionPointsType.Points)))
                    {
                        SaveSelectionPointsType(SelectionPointsType.Points);
                        IsLooping = true;
                    }
                    else
                    {
                        throw;
                    }
                }
        } while (IsLooping);

        return Null;
    }
}
