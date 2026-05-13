using System.Diagnostics;
using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist;

namespace AutoBIMFusion.Common.Drawing;

public static class Hatchs
{
    public static Hatch ApplyHatchV2(Polyline OutsidePolyline, IEnumerable<Curve> OuterMostCurves, Hatch hachure)
    {
        var db = Generic.GetDatabase();
        var ed = Generic.GetEditor();
        using (var trx = db.TransactionManager.TopTransaction)
        {
            //Define USC

            var PreviousUSCMatrix = ed.CurrentUserCoordinateSystem;


            if (hachure.NumberOfPatternDefinitions >= 1)
            {
                var OriginHatchPatternDefinition = hachure.GetPatternDefinitionAt(0);
                var oHatchOrigin = new Point3d(OriginHatchPatternDefinition.BaseX, OriginHatchPatternDefinition.BaseY,
                    0);

                var vtr = (ViewportTableRecord)trx.GetObject(ed.ActiveViewportId, OpenMode.ForWrite);

                if (!vtr.Ucs.Origin.IsEqualTo(oHatchOrigin))
                    ed.CurrentUserCoordinateSystem = Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis,
                        Vector3d.YAxis, Vector3d.ZAxis, oHatchOrigin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis);
            }

            try
            {
                var btr = Generic.GetCurrentSpaceBlockTableRecord(trx);
                var orderTable = trx.GetObject(btr.DrawOrderTableId, OpenMode.ForWrite) as DrawOrderTable;
                var DrawOrderCollection = new ObjectIdCollection();
                if (OutsidePolyline.IsNewObject)
                {
                    OutsidePolyline.Closed = true;
                    OutsidePolyline.AddToDrawingCurrentTransaction();
                }

                var Inside = new List<ObjectId>();
                foreach (var InsidePolyline in OuterMostCurves)
                {
                    if (InsidePolyline == OutsidePolyline) continue;
                    OutsidePolyline.CopyPropertiesTo(InsidePolyline);
                    var polylineObjectId = InsidePolyline.ObjectId;
                    if (InsidePolyline.IsNewObject)
                    {
                        (InsidePolyline as Polyline).Closed = true;
                        polylineObjectId = btr.AppendEntity(InsidePolyline);
                        trx.AddNewlyCreatedDBObject(InsidePolyline, true);
                    }

                    if (!DrawOrderCollection.Contains(polylineObjectId)) DrawOrderCollection.Add(polylineObjectId);
                    Inside.Add(polylineObjectId);
                }

                if (!DrawOrderCollection.Contains(OutsidePolyline.ObjectId))
                    DrawOrderCollection.Add(OutsidePolyline.ObjectId);
                var OutsideObjId = new ObjectIdCollection { OutsidePolyline.ObjectId };

                var oHatch = new Hatch();
                var oHatchObjectId = btr.AppendEntity(oHatch);
                trx.AddNewlyCreatedDBObject(oHatch, true);
                oHatch.Associative = true;
                try
                {
                    oHatch.AppendLoop(HatchLoopTypes.External, OutsideObjId);
                }
                catch (Exception ex)
                {
                    (OutsidePolyline.Clone() as Entity).AddToDrawing(5);
                    Generic.WriteMessage("Une erreur est survenue lors de la création de la hachure");
                    Debug.WriteLine(ex.ToString());
                    return null;
                }

                foreach (var item in Inside)
                {
                    var InsideObjId = new ObjectIdCollection { item };

                    bool TryAppendLoop(HatchLoopTypes hatchLoopTypes)
                    {
                        try
                        {
                            oHatch.AppendLoop(hatchLoopTypes, InsideObjId);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex + " hatchLoopTypes : " + hatchLoopTypes);
                        }

                        return false;
                    }

                    _ = TryAppendLoop(HatchLoopTypes.Default & HatchLoopTypes.Polyline) ||
                        TryAppendLoop(HatchLoopTypes.Outermost & HatchLoopTypes.Polyline) ||
                        TryAppendLoop(HatchLoopTypes.Derived & HatchLoopTypes.Polyline) ||
                        TryAppendLoop(HatchLoopTypes.Default);
                }


                oHatch.EvaluateHatch(true);
                hachure.CopyPropertiesTo(oHatch);
                oHatch.HatchStyle = HatchStyle.Normal;

                //Keep same draw order as old hatch
                orderTable.MoveAbove(DrawOrderCollection, oHatchObjectId);
                DrawOrderCollection.Insert(0, oHatchObjectId);
                try
                {
                    orderTable.MoveBelow(DrawOrderCollection, hachure.ObjectId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                }

                return oHatch;
            }
            finally
            {
                if (ed.CurrentUserCoordinateSystem != PreviousUSCMatrix)
                    ed.CurrentUserCoordinateSystem = PreviousUSCMatrix;
            }
        }
    }
}
