using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Serilog.Core;

namespace AutoBIMFusion.Common.Functions;

/// <summary>
///  Выполняет очистку чертежа от неиспользуемых объектов и служебного мусора.
///  Перед удалением временно снимает блокировку со слоёв и восстанавливает её после завершения.
/// </summary>
public static class DrawingPurger
{
    /// <summary>
    /// Запускает очистку для указанной базы данных чертежа.
    /// </summary>
    public static void Purge(Database db)
    {
        Dictionary<string, int> purgeReport = CorePurge(db);
        int TotalDeletedCount = purgeReport.Values.Sum();

        // Выводим отчёт пользователю.
        if (TotalDeletedCount == 0)
        {
            Generic.WriteMessage("Le dessin est déjà purgé.");
        }
        else
        {
            int maxLength = purgeReport.Max(p => p.Key.Length);
            foreach (KeyValuePair<string, int> entry in purgeReport)
            {
                Generic.WriteMessage($" - {entry.Key.PadRight(maxLength)} : {entry.Value} supprimés");
            }

            Generic.WriteMessage($"Total : {TotalDeletedCount} éléments supprimés dans le dessin");
        }

        ViewportLock.DoLockUnlock(true);
    }

    /// <summary>
    /// Выполняет глубокую программную очистку (Purge) базы данных DWG
    /// перед сохранением итогового файла.
    /// Заменяет класс DwgOptimizer.
    /// </summary>
    public static void Optimize(Database db, Logger log)
    {
        Dictionary<string, int> purgeReport = CorePurge(db);
        int totalDeletedCount = purgeReport.Values.Sum();

        if (totalDeletedCount == 0)
        {
            log.Information("Чертеж уже очищен, удалять нечего.");
            return;
        }

        foreach (KeyValuePair<string, int> entry in purgeReport)
        {
            log.Information("Purge: {ObjectType} - {Count} удалено", entry.Key, entry.Value);
        }
        log.Information("Purge: Всего удалено {TotalCount} элементов", totalDeletedCount);
    }

    private static Dictionary<string, int> CorePurge(Database db)
    {
        Dictionary<string, int> purgeReport = [];
        int totalDeletedCount = 0;

        using (Transaction trx = db.TransactionManager.StartTransaction())
        {
            // Временно снимаем блокировку со всех слоёв, чтобы выполнить очистку.
            List<LayerTableRecord> list = [];

            foreach (ObjectId objectId in (LayerTable)trx.GetObject(db.LayerTableId, OpenMode.ForRead))
            {
                LayerTableRecord layerTableRecord = (LayerTableRecord)trx.GetObject(objectId, OpenMode.ForRead);
                if (layerTableRecord.IsLocked)
                {
                    _ = trx.GetObject(layerTableRecord.ObjectId, OpenMode.ForWrite);
                    layerTableRecord.IsLocked = false;
                    list.Add(layerTableRecord);
                }
            }

            void AddToReport(string key, int count)
            {
                if (count == 0)
                {
                    return;
                }

                if (purgeReport.ContainsKey(key))
                {
                    purgeReport[key] += count;
                }
                else
                {
                    purgeReport[key] = count;
                }

                totalDeletedCount += count;
            }

            // Базовая очистка.
            AddToReport(nameof(PurgeMethods.CurvesZeroLength), PurgeMethods.CurvesZeroLength(db));
            AddToReport(nameof(PurgeMethods.EmptyText), PurgeMethods.EmptyText(db));
            AddToReport(nameof(PurgeMethods.XREF), PurgeMethods.XREF(db));

            // Повторяем очистку, пока появляются новые удаляемые элементы.
            int previousPassTotalDeletedCount = -1;
            int passCount = 0;
            while (previousPassTotalDeletedCount != totalDeletedCount && passCount < 10)
            {
                passCount++;
                previousPassTotalDeletedCount = totalDeletedCount;

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

            // Возвращаем блокировку слоёв в исходное состояние.
            foreach (LayerTableRecord layerTableRecord2 in list)
            {
                layerTableRecord2.IsLocked = true;
            }

            trx.Commit();
        }

        return purgeReport;
    }

    private static class PurgeMethods
    {
        /// <summary>
        ///     Удаляет неиспользуемые записи из таблиц и словарей базы данных.
        /// </summary>
        public static int Database(Database db)
        {
            ObjectIdCollection tableIds = [];
            ObjectIdCollection dictIds = [];

            _ = tableIds.Add(db.BlockTableId);
            _ = tableIds.Add(db.LayerTableId);
            _ = tableIds.Add(db.DimStyleTableId);
            _ = tableIds.Add(db.TextStyleTableId);
            _ = tableIds.Add(db.LinetypeTableId);
            _ = tableIds.Add(db.RegAppTableId);

            _ = dictIds.Add(db.MLStyleDictionaryId);
            _ = dictIds.Add(db.TableStyleDictionaryId);
            _ = dictIds.Add(db.PlotStyleNameDictionaryId);

            ObjectIdCollection objectIdCollection = [];
            using Transaction trx = db.TransactionManager.StartTransaction();
            foreach (object? obj in tableIds)
            {
                ObjectId objectId = (ObjectId)obj;
                foreach (ObjectId objectId2 in (SymbolTable)objectId.GetDBObject())
                {
                    if (!((SymbolTableRecord)objectId2.GetDBObject()).IsDependent)
                    {
                        _ = objectIdCollection.Add(objectId2);
                    }
                }
            }

            foreach (object? obj2 in dictIds)
            {
                ObjectId objectId3 = (ObjectId)obj2;
                foreach (DBDictionaryEntry dbdictionaryEntry in (DBDictionary)objectId3.GetDBObject())
                {
                    if (dbdictionaryEntry.Value.IsValid && !dbdictionaryEntry.Value.GetDBObject().IsAProxy)
                    {
                        _ = objectIdCollection.Add(dbdictionaryEntry.Value);
                    }
                }
            }

            db.Purge(objectIdCollection);
            foreach (ObjectId objid in objectIdCollection)
            {
                objid.GetDBObject(OpenMode.ForWrite).Erase();
            }

            trx.Commit();
            return objectIdCollection.Count;
        }

        /// <summary>
        ///     Удаляет вырожденные кривые и регионы с нулевой площадью.
        /// </summary>
        public static int CurvesZeroLength(Database db)
        {
            RXClass CurveRXClass = RXObject.GetClass(typeof(Curve));
            using Transaction trx = db.TransactionManager.StartTransaction();
            int NumberZeroLengthCurvesDeleted = 0;
            foreach (ObjectId objectId2 in (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead))
            {
                if (!objectId2.IsErased)
                {
                    foreach (ObjectId objectId3 in (BlockTableRecord)trx.GetObject(objectId2, OpenMode.ForRead))
                    {
                        if (objectId3.ObjectClass.IsDerivedFrom(CurveRXClass))
                        {
                            Curve curve = (Curve)trx.GetObject(objectId3, OpenMode.ForRead);
                            if (curve is not Xline && curve is not Ray &&
                                curve.GetDistanceAtParameter(curve.EndParam) == 0.0)
                            {
                                objectId3.GetDBObject(OpenMode.ForWrite).Erase();
                                NumberZeroLengthCurvesDeleted++;
                            }
                        }
                        else if (objectId3.ObjectClass.Name == "AcDbRegion" &&
                                 ((Region)trx.GetObject(objectId3, OpenMode.ForRead)).Area == 0.0)
                        {
                            objectId3.GetDBObject(OpenMode.ForWrite).Erase();
                            NumberZeroLengthCurvesDeleted++;
                        }
                    }
                }
            }

            trx.Commit();
            return NumberZeroLengthCurvesDeleted;
        }

        /// <summary>
        ///     Удаляет пустые текстовые объекты.
        /// </summary>
        public static int EmptyText(Database db)
        {
            RXClass DBTextRXClass = RXObject.GetClass(typeof(DBText));
            RXClass MTextRXClass = RXObject.GetClass(typeof(MText));
            using Transaction trx = db.TransactionManager.StartTransaction();
            int NumberEmptyTextDeleted = 0;
            foreach (ObjectId objectId2 in (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead))
            {
                if (!objectId2.IsErased)
                {
                    foreach (ObjectId objectId3 in (BlockTableRecord)trx.GetObject(objectId2, OpenMode.ForRead))
                    {
                        if (objectId3.ObjectClass == DBTextRXClass)
                        {
                            DBText dbtext = (DBText)trx.GetObject(objectId3, OpenMode.ForRead);
                            if (dbtext.TextString.Trim()?.Length == 0)
                            {
                                objectId3.GetDBObject(OpenMode.ForWrite).Erase();
                                dbtext.Erase();
                                NumberEmptyTextDeleted++;
                            }
                        }
                        else if (objectId3.ObjectClass == MTextRXClass &&
                                 ((MText)trx.GetObject(objectId3, OpenMode.ForRead)).Text.Trim()?.Length == 0)
                        {
                            objectId3.GetDBObject(OpenMode.ForWrite).Erase();
                            NumberEmptyTextDeleted++;
                        }
                    }
                }
            }

            trx.Commit();
            return NumberEmptyTextDeleted;
        }

        /// <summary>
        ///     Отсоединяет неиспользуемые внешние ссылки.
        /// </summary>
        public static int XREF(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            int NumberDetachedXREF = 0;
            foreach (ObjectId XrefId in (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead))
            {
                if (!XrefId.IsErased)
                {
                    BlockTableRecord blockTableRecord = (BlockTableRecord)trx.GetObject(XrefId, OpenMode.ForRead);
                    if (!blockTableRecord.IsLayout && blockTableRecord.IsFromExternalReference &&
                        blockTableRecord.GetBlockReferenceIds(true, false).Count == 0)
                    {
                        db.DetachXref(blockTableRecord.ObjectId);
                        NumberDetachedXREF++;
                    }
                }
            }

            trx.Commit();
            return NumberDetachedXREF;
        }

        /// <summary>
        ///     Очищает неиспользуемые стили мультивыносок.
        /// </summary>
        public static int MLeaderStyle(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            ObjectIdCollection objectIdCollection = [];
            DBDictionary dbdictionary = (DBDictionary)db.NamedObjectsDictionaryId.GetDBObject();
            if (dbdictionary.Contains("ACAD_MLEADERSTYLE"))
            {
                ObjectIdCollection MLeaderStyleObjectIdCollection = [];
                foreach (DBDictionaryEntry MLeaderStyleEntry in (DBDictionary)dbdictionary.GetAt("ACAD_MLEADERSTYLE")
                             .GetDBObject())
                {
                    _ = MLeaderStyleObjectIdCollection.Add(MLeaderStyleEntry.Value);
                }

                int count = MLeaderStyleObjectIdCollection.Count;
                int[] array = new int[count];
                db.CountHardReferences(MLeaderStyleObjectIdCollection, array);
                for (int i = 0; i < count; i++)
                {
                    if (array[i] == 0)
                    {
                        _ = objectIdCollection.Add(MLeaderStyleObjectIdCollection[i]);
                    }
                }

                db.Purge(objectIdCollection);
                foreach (ObjectId objid in objectIdCollection)
                {
                    objid.GetDBObject(OpenMode.ForWrite).Erase();
                }
            }

            trx.Commit();
            return objectIdCollection.Count;
        }

        /// <summary>
        ///     Очищает неиспользуемые определения DWF.
        /// </summary>
        public static int DWF(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            ObjectIdCollection objectIdCollection = [];
            DBDictionary dbdictionary = (DBDictionary)db.NamedObjectsDictionaryId.GetDBObject();
            if (dbdictionary.Contains("ACAD_DWFDEFINITIONS"))
            {
                ObjectIdCollection DwfDefinitionsObjectIdCollection = [];
                foreach (DBDictionaryEntry DwfEntry in (DBDictionary)dbdictionary.GetAt("ACAD_DWFDEFINITIONS").GetDBObject())
                {
                    _ = DwfDefinitionsObjectIdCollection.Add(DwfEntry.Value);
                }

                int count = DwfDefinitionsObjectIdCollection.Count;
                int[] array = new int[count];
                db.CountHardReferences(DwfDefinitionsObjectIdCollection, array);
                for (int i = 0; i < count; i++)
                {
                    if (array[i] == 0)
                    {
                        _ = objectIdCollection.Add(DwfDefinitionsObjectIdCollection[i]);
                    }
                }

                db.Purge(objectIdCollection);
                foreach (ObjectId objid in objectIdCollection)
                {
                    objid.GetDBObject(OpenMode.ForWrite).Erase();
                }
            }

            trx.Commit();
            return objectIdCollection.Count;
        }

        /// <summary>
        ///     Очищает неиспользуемые определения PDF.
        /// </summary>
        public static int PDF(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            ObjectIdCollection objectIdCollection = [];
            DBDictionary dbdictionary = (DBDictionary)db.NamedObjectsDictionaryId.GetDBObject();
            if (dbdictionary.Contains("ACAD_PDFDEFINITIONS"))
            {
                ObjectIdCollection PdfDefinitionsObjectIdCollection = [];
                foreach (DBDictionaryEntry PdfEntry in (DBDictionary)dbdictionary.GetAt("ACAD_PDFDEFINITIONS").GetDBObject())
                {
                    _ = PdfDefinitionsObjectIdCollection.Add(PdfEntry.Value);
                }

                int count = PdfDefinitionsObjectIdCollection.Count;
                int[] array = new int[count];
                db.CountHardReferences(PdfDefinitionsObjectIdCollection, array);
                for (int i = 0; i < count; i++)
                {
                    if (array[i] == 0)
                    {
                        _ = objectIdCollection.Add(PdfDefinitionsObjectIdCollection[i]);
                    }
                }

                db.Purge(objectIdCollection);
                foreach (ObjectId objid in objectIdCollection)
                {
                    objid.GetDBObject(OpenMode.ForWrite).Erase();
                }
            }

            trx.Commit();
            return objectIdCollection.Count;
        }

        /// <summary>
        ///     Очищает неиспользуемые определения DGN.
        /// </summary>
        public static int DGN(Database db)
        {
            ObjectIdCollection objectIdCollection = [];
            using Transaction trx = db.TransactionManager.StartTransaction();
            DBDictionary dbdictionary = (DBDictionary)db.NamedObjectsDictionaryId.GetDBObject();
            if (dbdictionary.Contains("ACAD_DGNDEFINITIONS"))
            {
                ObjectIdCollection DgnDefinitionsObjectIdCollection = [];
                foreach (DBDictionaryEntry DgnEntry in (DBDictionary)dbdictionary.GetAt("ACAD_DGNDEFINITIONS").GetDBObject())
                {
                    _ = DgnDefinitionsObjectIdCollection.Add(DgnEntry.Value);
                }

                int count = DgnDefinitionsObjectIdCollection.Count;
                int[] array = new int[count];
                db.CountHardReferences(DgnDefinitionsObjectIdCollection, array);
                for (int i = 0; i < count; i++)
                {
                    if (array[i] == 0)
                    {
                        _ = objectIdCollection.Add(DgnDefinitionsObjectIdCollection[i]);
                    }
                }

                db.Purge(objectIdCollection);
                foreach (ObjectId objid in objectIdCollection)
                {
                    objid.GetDBObject(OpenMode.ForWrite).Erase();
                }
            }

            trx.Commit();
            return objectIdCollection.Count;
        }

        /// <summary>
        ///     Очищает неиспользуемые ссылки на растровые изображения.
        /// </summary>
        public static int RasterImages(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            ObjectIdCollection objectIdCollection = [];
            DBDictionary dbdictionary = (DBDictionary)db.NamedObjectsDictionaryId.GetDBObject();
            if (dbdictionary.Contains("ACAD_IMAGE_DICT"))
            {
                foreach (DBDictionaryEntry ImageEntry in (DBDictionary)dbdictionary.GetAt("ACAD_IMAGE_DICT").GetDBObject())
                {
                    if (ImageEntry.Value.IsValid)
                    {
                        RasterImageDef? rasterImageDef = trx.GetObject(ImageEntry.Value, OpenMode.ForRead) as RasterImageDef;
                        if (rasterImageDef?.IsAProxy == false && rasterImageDef.GetEntityCount(out _) == 0)
                        {
                            _ = objectIdCollection.Add(ImageEntry.Value);
                        }
                    }
                }

                db.Purge(objectIdCollection);
                foreach (ObjectId objid in objectIdCollection)
                {
                    objid.GetDBObject(OpenMode.ForWrite).Erase();
                }
            }

            trx.Commit();
            return objectIdCollection.Count;
        }

        /// <summary>
        ///     Очищает список масштабов чертежа.
        /// </summary>
        public static int ScaleList(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            ObjectIdCollection objectIdCollection = [];
            DBDictionary dbdictionary = (DBDictionary)db.NamedObjectsDictionaryId.GetDBObject();
            if (dbdictionary.Contains("ACAD_SCALELIST"))
            {
                foreach (DBDictionaryEntry ScaleEntry in (DBDictionary)dbdictionary.GetAt("ACAD_SCALELIST").GetDBObject())
                {
                    if (ScaleEntry.Key != "A0" && !trx.GetObject(ScaleEntry.Value, OpenMode.ForRead).IsAProxy)
                    {
                        _ = objectIdCollection.Add(ScaleEntry.Value);
                    }
                }

                db.Purge(objectIdCollection);
                foreach (ObjectId objid in objectIdCollection)
                {
                    objid.GetDBObject(OpenMode.ForWrite).Erase();
                }
            }

            trx.Commit();
            return objectIdCollection.Count;
        }

        /// <summary>
        ///     Очищает пользовательские визуальные стили.
        /// </summary>
        public static int VisualStyle(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            ObjectIdCollection objectIdCollection = [];
            foreach (DBDictionaryEntry VisualStyleEntry in (DBDictionary)trx.GetObject(db.VisualStyleDictionaryId,
                         OpenMode.ForRead))
            {
                if ((trx.GetObject(VisualStyleEntry.Value, OpenMode.ForRead) as DBVisualStyle).Type ==
                    VisualStyleType.Custom && !trx.GetObject(VisualStyleEntry.Value, OpenMode.ForRead).IsAProxy)
                {
                    _ = objectIdCollection.Add(VisualStyleEntry.Value);
                }
            }

            db.Purge(objectIdCollection);
            foreach (ObjectId objid in objectIdCollection)
            {
                objid.GetDBObject(OpenMode.ForWrite).Erase();
            }

            trx.Commit();
            return objectIdCollection.Count;
        }

        /// <summary>
        ///     Очищает неиспользуемые материалы.
        /// </summary>
        public static int Material(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            ObjectIdCollection objectIdCollection = [];
            foreach (DBDictionaryEntry MaterialEntry in (DBDictionary)trx.GetObject(db.MaterialDictionaryId, OpenMode.ForRead,
                         false))
            {
                string key = MaterialEntry.Key;
                if (key != "ByBlock" && key != "ByLayer" && key != "Global" &&
                    !trx.GetObject(MaterialEntry.Value, OpenMode.ForRead).IsAProxy)
                {
                    _ = objectIdCollection.Add(MaterialEntry.Value);
                }
            }

            db.Purge(objectIdCollection);
            foreach (ObjectId objid in objectIdCollection)
            {
                objid.GetDBObject(OpenMode.ForWrite).Erase();
            }

            trx.Commit();
            return objectIdCollection.Count;
        }

        /// <summary>
        ///     Очищает неиспользуемые текстовые стили.
        /// </summary>
        public static int TextStyle(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            ObjectIdCollection objectIdCollection = [];
            foreach (ObjectId TextStyleTableId in (TextStyleTable)trx.GetObject(db.TextStyleTableId, OpenMode.ForRead))
            {
                TextStyleTableRecord textStyleTableRecord = (TextStyleTableRecord)trx.GetObject(TextStyleTableId, OpenMode.ForRead);
                if (textStyleTableRecord.IsShapeFile && textStyleTableRecord.Name != "" &&
                    !textStyleTableRecord.IsAProxy &&
                    !textStyleTableRecord.IsDependent)
                {
                    _ = objectIdCollection.Add(TextStyleTableId);
                }
            }

            db.Purge(objectIdCollection);
            foreach (ObjectId objid in objectIdCollection)
            {
                objid.GetDBObject(OpenMode.ForWrite).Erase();
            }

            trx.Commit();
            return objectIdCollection.Count;
        }

        /// <summary>
        ///     Удаляет группы, в которых меньше двух объектов.
        /// </summary>
        public static int Groups(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            int CountDeleted = 0;
            foreach (DBDictionaryEntry GroupEntry in (DBDictionary)trx.GetObject(db.GroupDictionaryId, OpenMode.ForRead, false))
            {
                Group group = (Group)trx.GetObject(GroupEntry.Value, OpenMode.ForRead, false);
                if (group.NumEntities < 2)
                {
                    group.ObjectId.GetDBObject(OpenMode.ForWrite).Erase();
                    CountDeleted++;
                }
            }

            trx.Commit();
            return CountDeleted;
        }
    }
}
