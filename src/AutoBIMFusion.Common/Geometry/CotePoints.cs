using AutoBIMFusion.Common.AcadSupport;
using AutoBIMFusion.Common.Configuration;
using AutoBIMFusion.Common.Extensions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace AutoBIMFusion.Common.Geometry;

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
        this.Altitude = double.TryParse(Altitude, out double AltitudeDbl) ? AltitudeDbl : 0;
    }

    public Points Points { get; }
    public double Altitude { get; }

    private static SelectionPointsType GetSelectionPointsType()
    {
        return Enum.TryParse(LegacyAppSettings.Default.SelectionPointsType, out SelectionPointsType SelectionPointsType)
            ? SelectionPointsType
            : SelectionPointsType.Points;
    }

    public static string FormatAltitude(double? Altitude, int NumberOfDecimal = 2)
    {
        Altitude ??= 0;
        return Altitude?.ToString($"#.{new string('0', NumberOfDecimal)}");
    }

    private static void SaveSelectionPointsType(SelectionPointsType SelectionPointsType)
    {
        LegacyAppSettings.Default.SelectionPointsType = SelectionPointsType.ToString();
        LegacyAppSettings.Save();
    }

    public static bool NullPointExit(CotePoints cotePoints)
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        Database db = doc.Database;
        if (cotePoints is null)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            EntityHighlighter.UnhighlightAll();
            trx.Commit();
            return true;
        }

        return false;
    }

    private static double GetCote(Points Origin)
    {
        Database db = AcadContext.GetDatabase();
        Editor ed = AcadContext.GetEditor();

        // Укажите место, где требуется указать точку
        DBPoint PointDrawingEntity = new(Origin.SCG);

        ObjectId PointDrawingEntityObjectId;

        using (Transaction trx = db.TransactionManager.StartTransaction())
        {
            var acBlkTbl = trx.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
            BlockTableRecord acBlkTblRec = AcadContext.GetCurrentSpaceBlockTableRecord(trx);

            _ = acBlkTblRec.AppendEntity(PointDrawingEntity);
            trx.AddNewlyCreatedDBObject(PointDrawingEntity, true);
            PointDrawingEntityObjectId = PointDrawingEntity.ObjectId;
            trx.Commit();
        }

        PromptDoubleOptions PromptDoubleAltitudeOptions = new("Введите отметку\n")
        {
            AllowNegative = false,
            AllowNone = false
        };

        PromptDoubleResult PromptDoubleAltitudeResult = ed.GetDouble(PromptDoubleAltitudeOptions);

        //remove the point where the altitude is asked
        if (!PointDrawingEntityObjectId.IsErased)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            DBObject obj = PointDrawingEntityObjectId.GetObject(OpenMode.ForWrite);
            obj.Erase(true);
            trx.Commit();
        }

        return PromptDoubleAltitudeResult.Value;
    }

    public static double? GetAltitudeFromBloc(ObjectId BlocObjectId)
    {
        Database db = AcadContext.GetDatabase();
        Autodesk.AutoCAD.DatabaseServices.TransactionManager trx = db.TransactionManager;
        DBObject BlocObject = trx.GetObject(BlocObjectId, OpenMode.ForRead);
        return BlocObject is BlockReference blkRef ? GetAltitudeFromBloc(blkRef) : null;
    }

    public static double? GetAltitudeFromBloc(BlockReference blkRef)
    {
        Database db = AcadContext.GetDatabase();
        Autodesk.AutoCAD.DatabaseServices.TransactionManager trx = db.TransactionManager;

        foreach (ObjectId AttributeObjectId in blkRef.AttributeCollection)
        {
            var Attribute = (AttributeReference)trx.GetObject(AttributeObjectId, OpenMode.ForRead, true);

            string textString = Attribute.TextString.Trim();

            if (textString.Contains('.'))
            {
                if (double.TryParse(textString, out double Altimetrie))
                {
                    AcadContext.WriteMessage($"Выбрана отметка: {FormatAltitude(Altimetrie)}");
                    blkRef.RegisterHighlight();
                    return Altimetrie;
                }

                double? ExtractedAltitude = ExtractDoubleInStringFromPoint(textString);

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
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        Editor ed = doc.Editor;

        if (OriginalString.Contains('%'))
        {
            AcadContext.WriteMessage(
                "В целях безопасности тексты, содержащие %, не могут быть преобразованы в отметки.");
            return null;
        }

        int[] StringPointPosition = OriginalString.AllIndexesOf(".").ToArray();
        string NumberValueBeforePoint = "";
        string NumberValueAfterPoint = "";

        foreach (int index in StringPointPosition)
        {
            int n = index;
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
            {
                //Not sure if this is a cote
                return null;
            }

            string FinalNumberString = $"{NumberValueBeforePoint}.{NumberValueAfterPoint}";
            bool IsValidNumber = double.TryParse(FinalNumberString, out double FinalNumberDouble);
            if (IsValidNumber)
            {
                ed.WriteMessage($"Обнаружена отметка: {FinalNumberString}\n");
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
        Editor ed = AcadContext.GetEditor();
        BlockReference blkRef = null;
        List<ObjectId> XrefObjectId;

        (ObjectId[] XrefObjectId, ObjectId SelectedObjectId, PromptStatus PromptStatus) XrefSelection = SelectInXref.Select(Message, NonInterractivePickedPoint);
        XrefObjectId = XrefSelection.XrefObjectId.ToList();
        PromptStatus = XrefSelection.PromptStatus;
        if (XrefSelection.PromptStatus != PromptStatus.OK)
        {
            return Null;
        }

        if (XrefSelection.SelectedObjectId == ObjectId.Null)
        {
            return Null;
        }

        EntityHighlighter.UnhighlightAll();
        XrefSelection.SelectedObjectId.RegisterHighlight();
        DBObject XrefObject = XrefSelection.SelectedObjectId.GetDBObject();

        if (XrefObject is AttributeReference blkChildAttribute)
        {
            DBObject DbObj = blkChildAttribute.OwnerId.GetDBObject();
            blkRef = DbObj as BlockReference;
        }
        else if (XrefObject is BlockReference XrefObjectBlkRef)
        {
            blkRef = XrefObjectBlkRef;
        }
        else
        {
            foreach (ObjectId objId in XrefSelection.XrefObjectId)
            {
                _ = XrefObjectId.Remove(objId);
                XrefObject = objId.GetDBObject();

                if (XrefObject is BlockReference ParentBlkRef && !ParentBlkRef.IsXref())
                {
                    blkRef = XrefObject as BlockReference;
                    break;
                }
            }
        }

        if (blkRef is null)
        {
            return Null;
        }

        double? Altimetrie = GetAltitudeFromBloc(blkRef);
        Points BlockPosition = SelectInXref.TransformPointInXrefsToCurrent(blkRef.Position, XrefObjectId.ToArray());

        if (Altimetrie == null)
        {
            Altimetrie = blkRef.Position.Z;
            if (Altimetrie == 0)
            {
                return new CotePoints(BlockPosition, 0);
            }

            PromptKeywordOptions options = new(
                $"Для этого блока не найдена отметка, однако задана высота Z = {FormatAltitude(Altimetrie)}. Хотите использовать это значение?\n");
            options.Keywords.Add("OUI");
            options.Keywords.Add("NON");
            options.AllowNone = true;
            PromptResult result = ed.GetKeywords(options);
            if (result.Status == PromptStatus.OK && result.StringResult != "OUI")
            {
                return new CotePoints(BlockPosition, 0);
            }
        }

        return new CotePoints(BlockPosition, Altimetrie ?? 0);
    }

    private static CotePoints GetBloc(string Message)
    {
        Database db = AcadContext.GetDatabase();
        Editor ed = AcadContext.GetEditor();

        while (true)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            //list of available SelectionFilter : https://help.autodesk.com/view/ACD/2018/ENU/?guid=GUID-7D07C886-FD1D-4A0C-A7AB-B4D21F18E484
            var EntitiesGroupCodesList = new TypedValue[1] { new((int)DxfCode.Start, "INSERT") };
            SelectionFilter SelectionEntitiesFilter = new(EntitiesGroupCodesList);
            string PromptSelectionKeyWordString = nameof(SelectionPointsType.Points).CapitalizeFirstLetters(2);
            PromptEntityOptions PromptBlocSelectionOptions = new($"{Message} [{PromptSelectionKeyWordString}]")
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
                    trx.Commit();
                    return null;
                }

                if (PromptBlocSelectionResult.Status == PromptStatus.Keyword)
                {
                    trx.Commit();
                    throw new Exception(ErrorStatus.OK, PromptBlocSelectionResult.StringResult);
                }

                SelectedObject = PromptBlocSelectionResult.ObjectId.GetEntity();
            } while (SelectedObject is not BlockReference);

            var blockReference = SelectedObject as BlockReference;
            ObjectId SelectedBlocObjectId = blockReference.ObjectId;
            double? Altitude = GetAltitudeFromBloc(SelectedBlocObjectId);
            Points CoteLocation = new(blockReference.Position);

            if (blockReference.IsXref())
            {
                CotePoints? CotePoint = GetBlockInXref(string.Empty, PromptBlocSelectionResult.PickedPoint, out _);
                bool IsCotePointNotNull = CotePoint != Null;
                bool IsAltimetrieDefined = (CotePoint?.Altitude ?? 0) != 0;
                if (IsCotePointNotNull && IsAltimetrieDefined)
                {
                    PromptKeywordOptions AskKeepXREFCoteValuesOptions = new(
                        $"Отметка {FormatAltitude(CotePoint.Altitude)} найдена во внешней ссылке. Хотите использовать это значение?\n");
                    AskKeepXREFCoteValuesOptions.Keywords.Add("Oui");
                    AskKeepXREFCoteValuesOptions.Keywords.Add("Non");
                    AskKeepXREFCoteValuesOptions.Keywords.Default = "Oui";
                    AskKeepXREFCoteValuesOptions.AllowNone = true;
                    PromptResult AskKeepXREFCoteValues = ed.GetKeywords(AskKeepXREFCoteValuesOptions);
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
                AcadContext.WriteMessage("Отметка не обнаружена");
                trx.Commit();
                continue;
            }

            trx.Commit();
            return new CotePoints(CoteLocation, Altitude ?? 0);
        }
    }

    public static CotePoints GetCotePoints(string Message, Points Origin)
    {
        Editor ed = AcadContext.GetEditor();
        PromptPointOptions PromptPointOptions =
            new($"{Message} [{SelectionPointsType.Bloc}]\n", nameof(SelectionPointsType.Bloc));

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
            SelectionPointsType SelectionPointsType = GetSelectionPointsType();

            if (SelectionPointsType == SelectionPointsType.Points)
            {
                PromptPointResult PromptPointResult = ed.GetPoint(PromptPointOptions);
                if (PromptPointResult.Status == PromptStatus.Keyword)
                {
                    SaveSelectionPointsType(SelectionPointsType.Bloc);
                    IsLooping = true;
                    continue;
                }

                if (PromptPointResult.Status == PromptStatus.OK)
                {
                    var CotePoint = Points.GetFromPromptPointResult(PromptPointResult);
                    double Altitude = GetCote(CotePoint);
                    return new CotePoints(CotePoint, Altitude);
                }
            }

            if (SelectionPointsType == SelectionPointsType.Bloc)
            {
                try
                {
                    return GetBloc(Message);
                }
                catch (Exception ex)
                {
                    Exception AutEx = ex;
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
            }
        } while (IsLooping);

        return Null;
    }
}
