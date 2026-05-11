using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using SioForgeCAD.Commun;

namespace SioForgeCAD.Functions;

public static class PURGEALL
{
    public static void Purge()
    {
        var db = Generic.GetDatabase();
        Purge(db);
    }


    public static void Purge(Database db)
    {
        using (var tr = db.TransactionManager.StartTransaction())
        {
            //unlock all layers but keep trace
            var list = new List<LayerTableRecord>();
            foreach (var objectId in (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))
            {
                var layerTableRecord = (LayerTableRecord)tr.GetObject(objectId, OpenMode.ForRead);
                if (layerTableRecord.IsLocked)
                {
                    tr.GetObject(layerTableRecord.ObjectId, OpenMode.ForWrite);
                    layerTableRecord.IsLocked = false;
                    list.Add(layerTableRecord);
                }
            }

            var purgeReport = new Dictionary<string, int>();
            var TotalDeletedCount = 0;

            void AddToReport(string key, int count)
            {
                if (purgeReport.ContainsKey(key))
                    purgeReport[key] += count;
                else
                    purgeReport[key] = count;

                TotalDeletedCount += count;
            }

            // Purge de base

            AddToReport(nameof(PurgeMethods.CurvesZeroLength), PurgeMethods.CurvesZeroLength(db));
            AddToReport(nameof(PurgeMethods.EmptyText), PurgeMethods.EmptyText(db));
            AddToReport(nameof(PurgeMethods.XREF), PurgeMethods.XREF(db));

            // Purges répétées
            var PreviousPassTotalDeletedCount = -1;
            var passCount = 0;
            while (PreviousPassTotalDeletedCount != TotalDeletedCount && passCount < 10)
            {
                passCount++;
                PreviousPassTotalDeletedCount = TotalDeletedCount;
                AddToReport(nameof(PurgeMethods.Database), PurgeMethods.Database(db));
                AddToReport(nameof(PurgeMethods.DWF), PurgeMethods.DWF(db));
                AddToReport(nameof(PurgeMethods.PDF), PurgeMethods.PDF(db));
                AddToReport(nameof(PurgeMethods.DGN), PurgeMethods.DGN(db));
                AddToReport(nameof(PurgeMethods.RasterImages), PurgeMethods.RasterImages(db));
                AddToReport(nameof(PurgeMethods.MLeaderStyle), PurgeMethods.MLeaderStyle(db));
                //AddToReport(nameof(PurgeMethods.ScaleList), PurgeMethods.ScaleList(db));
                AddToReport(nameof(PurgeMethods.VisualStyle), PurgeMethods.VisualStyle(db));
                AddToReport(nameof(PurgeMethods.Material), PurgeMethods.Material(db));
                AddToReport(nameof(PurgeMethods.TextStyle), PurgeMethods.TextStyle(db));
                AddToReport(nameof(PurgeMethods.Groups), PurgeMethods.Groups(db));
            }

            // Affichage du rapport
            if (TotalDeletedCount == 0)
            {
                Generic.WriteMessage("Le dessin est déjà purgé.");
            }
            else
            {
                var maxLength = purgeReport.Max(p => p.Key.Length);
                foreach (var entry in purgeReport)
                    Generic.WriteMessage($" - {entry.Key.PadRight(maxLength)} : {entry.Value} supprimés");

                Generic.WriteMessage($"Total : {TotalDeletedCount} éléments supprimés dans le dessin");
            }

            //relock all layers
            foreach (var layerTableRecord2 in list) layerTableRecord2.IsLocked = true;

            tr.Commit();
        }

        VIEWPORTLOCK.DoLockUnlock(true);
    }

    private static class PurgeMethods
    {
        public static int Database(Database db)
        {
            var tableIds = new ObjectIdCollection();
            var dictIds = new ObjectIdCollection();

            tableIds.Add(db.BlockTableId);
            tableIds.Add(db.LayerTableId);
            tableIds.Add(db.DimStyleTableId);
            tableIds.Add(db.TextStyleTableId);
            tableIds.Add(db.LinetypeTableId);
            tableIds.Add(db.RegAppTableId);

            dictIds.Add(db.MLStyleDictionaryId);
            dictIds.Add(db.TableStyleDictionaryId);
            dictIds.Add(db.PlotStyleNameDictionaryId);

            var objectIdCollection = new ObjectIdCollection();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var obj in tableIds)
                {
                    var objectId = (ObjectId)obj;
                    foreach (var objectId2 in (SymbolTable)objectId.GetDBObject())
                        if (!((SymbolTableRecord)objectId2.GetDBObject()).IsDependent)
                            objectIdCollection.Add(objectId2);
                }

                foreach (var obj2 in dictIds)
                {
                    var objectId3 = (ObjectId)obj2;
                    foreach (var dbdictionaryEntry in (DBDictionary)objectId3.GetDBObject())
                        if (dbdictionaryEntry.Value.IsValid && !dbdictionaryEntry.Value.GetDBObject().IsAProxy)
                            objectIdCollection.Add(dbdictionaryEntry.Value);
                }

                db.Purge(objectIdCollection);
                foreach (ObjectId objid in objectIdCollection) objid.GetDBObject(OpenMode.ForWrite).Erase();
                tr.Commit();
                return objectIdCollection.Count;
            }
        }

        public static int CurvesZeroLength(Database db)
        {
            var CurveRXClass = RXObject.GetClass(typeof(Curve));
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var NumberZeroLengthCurvesDeleted = 0;
                foreach (var objectId2 in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))
                    if (!objectId2.IsErased)
                        foreach (var objectId3 in (BlockTableRecord)tr.GetObject(objectId2, OpenMode.ForRead))
                            if (objectId3.ObjectClass.IsDerivedFrom(CurveRXClass))
                            {
                                var curve = (Curve)tr.GetObject(objectId3, OpenMode.ForRead);
                                if (!(curve is Xline) && !(curve is Ray) &&
                                    curve.GetDistanceAtParameter(curve.EndParam) == 0.0)
                                {
                                    objectId3.GetDBObject(OpenMode.ForWrite).Erase();
                                    NumberZeroLengthCurvesDeleted++;
                                }
                            }
                            else if (objectId3.ObjectClass.Name == "AcDbRegion" &&
                                     ((Region)tr.GetObject(objectId3, OpenMode.ForRead)).Area == 0.0)
                            {
                                objectId3.GetDBObject(OpenMode.ForWrite).Erase();
                                NumberZeroLengthCurvesDeleted++;
                            }

                tr.Commit();
                return NumberZeroLengthCurvesDeleted;
            }
        }

        public static int EmptyText(Database db)
        {
            var DBTextRXClass = RXObject.GetClass(typeof(DBText));
            var MTextRXClass = RXObject.GetClass(typeof(MText));
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var NumberEmptyTextDeleted = 0;
                foreach (var objectId2 in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))
                    if (!objectId2.IsErased)
                        foreach (var objectId3 in (BlockTableRecord)tr.GetObject(objectId2, OpenMode.ForRead))
                            if (objectId3.ObjectClass == DBTextRXClass)
                            {
                                var dbtext = (DBText)tr.GetObject(objectId3, OpenMode.ForRead);
                                if (dbtext.TextString.Trim()?.Length == 0)
                                {
                                    objectId3.GetDBObject(OpenMode.ForWrite).Erase();
                                    dbtext.Erase();
                                    NumberEmptyTextDeleted++;
                                }
                            }
                            else if (objectId3.ObjectClass == MTextRXClass &&
                                     ((MText)tr.GetObject(objectId3, OpenMode.ForRead)).Text.Trim()?.Length == 0)
                            {
                                objectId3.GetDBObject(OpenMode.ForWrite).Erase();
                                NumberEmptyTextDeleted++;
                            }

                tr.Commit();
                return NumberEmptyTextDeleted;
            }
        }

        public static int XREF(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var NumberDetachedXREF = 0;
                foreach (var XrefId in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))
                    if (!XrefId.IsErased)
                    {
                        var blockTableRecord = (BlockTableRecord)tr.GetObject(XrefId, OpenMode.ForRead);
                        if (!blockTableRecord.IsLayout && blockTableRecord.IsFromExternalReference &&
                            blockTableRecord.GetBlockReferenceIds(true, false).Count == 0)
                        {
                            db.DetachXref(blockTableRecord.ObjectId);
                            NumberDetachedXREF++;
                        }
                    }

                tr.Commit();
                return NumberDetachedXREF;
            }
        }

        public static int MLeaderStyle(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var objectIdCollection = new ObjectIdCollection();
                var dbdictionary = (DBDictionary)db.NamedObjectsDictionaryId.GetDBObject();
                if (dbdictionary.Contains("ACAD_MLEADERSTYLE"))
                {
                    var MLeaderStyleObjectIdCollection = new ObjectIdCollection();
                    foreach (var MLeaderStyleEntry in (DBDictionary)dbdictionary.GetAt("ACAD_MLEADERSTYLE")
                                 .GetDBObject()) MLeaderStyleObjectIdCollection.Add(MLeaderStyleEntry.Value);
                    var count = MLeaderStyleObjectIdCollection.Count;
                    var array = new int[count];
                    db.CountHardReferences(MLeaderStyleObjectIdCollection, array);
                    for (var i = 0; i < count; i++)
                        if (array[i] == 0)
                            objectIdCollection.Add(MLeaderStyleObjectIdCollection[i]);

                    db.Purge(objectIdCollection);
                    foreach (ObjectId objid in objectIdCollection) objid.GetDBObject(OpenMode.ForWrite).Erase();
                }

                tr.Commit();
                return objectIdCollection.Count;
            }
        }

        public static int DWF(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var objectIdCollection = new ObjectIdCollection();
                var dbdictionary = (DBDictionary)db.NamedObjectsDictionaryId.GetDBObject();
                if (dbdictionary.Contains("ACAD_DWFDEFINITIONS"))
                {
                    var DwfDefinitionsObjectIdCollection = new ObjectIdCollection();
                    foreach (var DwfEntry in (DBDictionary)dbdictionary.GetAt("ACAD_DWFDEFINITIONS").GetDBObject())
                        DwfDefinitionsObjectIdCollection.Add(DwfEntry.Value);
                    var count = DwfDefinitionsObjectIdCollection.Count;
                    var array = new int[count];
                    db.CountHardReferences(DwfDefinitionsObjectIdCollection, array);
                    for (var i = 0; i < count; i++)
                        if (array[i] == 0)
                            objectIdCollection.Add(DwfDefinitionsObjectIdCollection[i]);

                    db.Purge(objectIdCollection);
                    foreach (ObjectId objid in objectIdCollection) objid.GetDBObject(OpenMode.ForWrite).Erase();
                }

                tr.Commit();
                return objectIdCollection.Count;
            }
        }

        public static int PDF(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var objectIdCollection = new ObjectIdCollection();
                var dbdictionary = (DBDictionary)db.NamedObjectsDictionaryId.GetDBObject();
                if (dbdictionary.Contains("ACAD_PDFDEFINITIONS"))
                {
                    var PdfDefinitionsObjectIdCollection = new ObjectIdCollection();
                    foreach (var PdfEntry in (DBDictionary)dbdictionary.GetAt("ACAD_PDFDEFINITIONS").GetDBObject())
                        PdfDefinitionsObjectIdCollection.Add(PdfEntry.Value);
                    var count = PdfDefinitionsObjectIdCollection.Count;
                    var array = new int[count];
                    db.CountHardReferences(PdfDefinitionsObjectIdCollection, array);
                    for (var i = 0; i < count; i++)
                        if (array[i] == 0)
                            objectIdCollection.Add(PdfDefinitionsObjectIdCollection[i]);

                    db.Purge(objectIdCollection);
                    foreach (ObjectId objid in objectIdCollection) objid.GetDBObject(OpenMode.ForWrite).Erase();
                }

                tr.Commit();
                return objectIdCollection.Count;
            }
        }

        public static int DGN(Database db)
        {
            var objectIdCollection = new ObjectIdCollection();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var dbdictionary = (DBDictionary)db.NamedObjectsDictionaryId.GetDBObject();
                if (dbdictionary.Contains("ACAD_DGNDEFINITIONS"))
                {
                    var DgnDefinitionsObjectIdCollection = new ObjectIdCollection();
                    foreach (var DgnEntry in (DBDictionary)dbdictionary.GetAt("ACAD_DGNDEFINITIONS").GetDBObject())
                        DgnDefinitionsObjectIdCollection.Add(DgnEntry.Value);
                    var count = DgnDefinitionsObjectIdCollection.Count;
                    var array = new int[count];
                    db.CountHardReferences(DgnDefinitionsObjectIdCollection, array);
                    for (var i = 0; i < count; i++)
                        if (array[i] == 0)
                            objectIdCollection.Add(DgnDefinitionsObjectIdCollection[i]);

                    db.Purge(objectIdCollection);
                    foreach (ObjectId objid in objectIdCollection) objid.GetDBObject(OpenMode.ForWrite).Erase();
                }

                tr.Commit();
                return objectIdCollection.Count;
            }
        }

        public static int RasterImages(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var objectIdCollection = new ObjectIdCollection();
                var dbdictionary = (DBDictionary)db.NamedObjectsDictionaryId.GetDBObject();
                if (dbdictionary.Contains("ACAD_IMAGE_DICT"))
                {
                    foreach (var ImageEntry in (DBDictionary)dbdictionary.GetAt("ACAD_IMAGE_DICT").GetDBObject())
                        if (ImageEntry.Value.IsValid)
                        {
                            var rasterImageDef = tr.GetObject(ImageEntry.Value, OpenMode.ForRead) as RasterImageDef;
                            if (rasterImageDef?.IsAProxy == false && rasterImageDef.GetEntityCount(out _) == 0)
                                objectIdCollection.Add(ImageEntry.Value);
                        }

                    db.Purge(objectIdCollection);
                    foreach (ObjectId objid in objectIdCollection) objid.GetDBObject(OpenMode.ForWrite).Erase();
                }

                tr.Commit();
                return objectIdCollection.Count;
            }
        }

        public static int ScaleList(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var objectIdCollection = new ObjectIdCollection();
                var dbdictionary = (DBDictionary)db.NamedObjectsDictionaryId.GetDBObject();
                if (dbdictionary.Contains("ACAD_SCALELIST"))
                {
                    foreach (var ScaleEntry in (DBDictionary)dbdictionary.GetAt("ACAD_SCALELIST").GetDBObject())
                        if (ScaleEntry.Key != "A0" && !tr.GetObject(ScaleEntry.Value, OpenMode.ForRead).IsAProxy)
                            objectIdCollection.Add(ScaleEntry.Value);

                    db.Purge(objectIdCollection);
                    foreach (ObjectId objid in objectIdCollection) objid.GetDBObject(OpenMode.ForWrite).Erase();
                }

                tr.Commit();
                return objectIdCollection.Count;
            }
        }

        public static int VisualStyle(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var objectIdCollection = new ObjectIdCollection();
                foreach (var VisualStyleEntry in (DBDictionary)tr.GetObject(db.VisualStyleDictionaryId,
                             OpenMode.ForRead))
                    if ((tr.GetObject(VisualStyleEntry.Value, OpenMode.ForRead) as DBVisualStyle).Type ==
                        VisualStyleType.Custom && !tr.GetObject(VisualStyleEntry.Value, OpenMode.ForRead).IsAProxy)
                        objectIdCollection.Add(VisualStyleEntry.Value);

                db.Purge(objectIdCollection);
                foreach (ObjectId objid in objectIdCollection) objid.GetDBObject(OpenMode.ForWrite).Erase();
                tr.Commit();
                return objectIdCollection.Count;
            }
        }

        public static int Material(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var objectIdCollection = new ObjectIdCollection();
                foreach (var MaterialEntry in (DBDictionary)tr.GetObject(db.MaterialDictionaryId, OpenMode.ForRead,
                             false))
                {
                    var key = MaterialEntry.Key;
                    if (key != "ByBlock" && key != "ByLayer" && key != "Global" &&
                        !tr.GetObject(MaterialEntry.Value, OpenMode.ForRead).IsAProxy)
                        objectIdCollection.Add(MaterialEntry.Value);
                }

                db.Purge(objectIdCollection);
                foreach (ObjectId objid in objectIdCollection) objid.GetDBObject(OpenMode.ForWrite).Erase();
                tr.Commit();
                return objectIdCollection.Count;
            }
        }

        public static int TextStyle(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var objectIdCollection = new ObjectIdCollection();
                foreach (var TextStyleTableId in (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead))
                {
                    var textStyleTableRecord = (TextStyleTableRecord)tr.GetObject(TextStyleTableId, OpenMode.ForRead);
                    if (textStyleTableRecord.IsShapeFile && textStyleTableRecord.Name != "" &&
                        !textStyleTableRecord.IsAProxy &&
                        !textStyleTableRecord.IsDependent) objectIdCollection.Add(TextStyleTableId);
                }

                db.Purge(objectIdCollection);
                foreach (ObjectId objid in objectIdCollection) objid.GetDBObject(OpenMode.ForWrite).Erase();
                tr.Commit();
                return objectIdCollection.Count;
            }
        }

        public static int Groups(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var CountDeleted = 0;
                foreach (var GroupEntry in (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead, false))
                {
                    var group = (Group)tr.GetObject(GroupEntry.Value, OpenMode.ForRead, false);
                    if (group.NumEntities < 2)
                    {
                        group.ObjectId.GetDBObject(OpenMode.ForWrite).Erase();
                        CountDeleted++;
                    }
                }

                tr.Commit();
                return CountDeleted;
            }
        }
    }
}
