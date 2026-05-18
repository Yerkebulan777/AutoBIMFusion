using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist;
using AutoBIMFusion.Common.Mist.AutoCAD;
using Autodesk.AutoCAD.GraphicsInterface;
using Serilog.Core;

namespace AutoBIMFusion.Merge.Combine;

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
    /// Основной путь оптимизации итогового DWG.
    /// </summary>
    public static void Optimize(Database db, Logger log)
    {
        Dictionary<string, int> purgeReport = CorePurge(db);
        int totalDeletedCount = purgeReport.Values.Sum();

        if (totalDeletedCount == 0)
        {
            return;
        }

        log.Information("Purge: удалено объектов: {TotalCount}", totalDeletedCount);
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

            // Базовая очистка.
            AddToReport(purgeReport, nameof(PurgeMethods.CurvesZeroLength), PurgeMethods.CurvesZeroLength(db), ref totalDeletedCount);
            AddToReport(purgeReport, nameof(PurgeMethods.EmptyText), PurgeMethods.EmptyText(db), ref totalDeletedCount);
            AddToReport(purgeReport, nameof(PurgeMethods.XREF), PurgeMethods.XREF(db), ref totalDeletedCount);

            // Повторяем очистку, пока появляются новые удаляемые элементы.
            int previousPassTotalDeletedCount = -1;
            int passCount = 0;
            while (previousPassTotalDeletedCount != totalDeletedCount && passCount < 10)
            {
                passCount++;
                previousPassTotalDeletedCount = totalDeletedCount;

                AddToReport(purgeReport, nameof(PurgeMethods.Database), PurgeMethods.Database(db), ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.DWF), PurgeMethods.DWF(db), ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.PDF), PurgeMethods.PDF(db), ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.DGN), PurgeMethods.DGN(db), ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.RasterImages), PurgeMethods.RasterImages(db), ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.MLeaderStyle), PurgeMethods.MLeaderStyle(db), ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.VisualStyle), PurgeMethods.VisualStyle(db), ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.Material), PurgeMethods.Material(db), ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.TextStyle), PurgeMethods.TextStyle(db), ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.Groups), PurgeMethods.Groups(db), ref totalDeletedCount);
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

    private static void AddToReport(Dictionary<string, int> purgeReport, string key, int count, ref int totalDeletedCount)
    {
        if (count == 0)
        {
            return;
        }

        if (!purgeReport.TryAdd(key, count))
        {
            purgeReport[key] += count;
        }

        totalDeletedCount += count;
    }

    private static class PurgeMethods
    {
        private static int PurgeAndErase(Database db, ObjectIdCollection objectIds)
        {
            if (objectIds.Count == 0)
            {
                return 0;
            }

            db.Purge(objectIds);

            foreach (ObjectId objectId in objectIds)
            {
                if (objectId.IsValid && !objectId.IsErased)
                {
                    objectId.GetDBObject(OpenMode.ForWrite).Erase();
                }
            }

            return objectIds.Count;
        }

        /// <summary>
        ///     Удаляет неиспользуемые записи из таблиц и словарей базы данных.
        /// </summary>
        public static int Database(Database db)
        {
            using ObjectIdCollection tableIds = [];
            using ObjectIdCollection dictIds = [];

            _ = tableIds.Add(db.BlockTableId);
            _ = tableIds.Add(db.LayerTableId);
            _ = tableIds.Add(db.DimStyleTableId);
            _ = tableIds.Add(db.TextStyleTableId);
            _ = tableIds.Add(db.LinetypeTableId);
            _ = tableIds.Add(db.RegAppTableId);

            _ = dictIds.Add(db.MLStyleDictionaryId);
            _ = dictIds.Add(db.TableStyleDictionaryId);
            _ = dictIds.Add(db.PlotStyleNameDictionaryId);

            using Transaction trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection objectIdCollection = [];

            foreach (object? obj in tableIds)
            {
                ObjectId objectId = (ObjectId)obj;
                SymbolTable symbolTable = (SymbolTable)trx.GetObject(objectId, OpenMode.ForRead);
                foreach (ObjectId objectId2 in symbolTable)
                {
                    SymbolTableRecord record = (SymbolTableRecord)trx.GetObject(objectId2, OpenMode.ForRead);
                    if (!record.IsDependent)
                    {
                        _ = objectIdCollection.Add(objectId2);
                    }
                }
            }

            foreach (object? obj2 in dictIds)
            {
                ObjectId objectId3 = (ObjectId)obj2;
                DBDictionary dictionary = (DBDictionary)trx.GetObject(objectId3, OpenMode.ForRead);
                foreach (DBDictionaryEntry dbdictionaryEntry in dictionary)
                {
                    if (!dbdictionaryEntry.Value.IsValid)
                    {
                        continue;
                    }

                    DBObject dbObject = trx.GetObject(dbdictionaryEntry.Value, OpenMode.ForRead);
                    if (!dbObject.IsAProxy)
                    {
                        _ = objectIdCollection.Add(dbdictionaryEntry.Value);
                    }
                }
            }

            int deletedCount = PurgeAndErase(db, objectIdCollection);
            trx.Commit();
            return deletedCount;
        }

        /// <summary>
        ///     Удаляет вырожденные кривые и регионы с нулевой площадью.
        /// </summary>
        public static int CurvesZeroLength(Database db)
        {
            RXClass CurveRXClass = RXObject.GetClass(typeof(Curve));
            using Transaction trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection candidates = [];

            foreach (ObjectId objectId2 in (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead))
            {
                if (objectId2.IsErased)
                {
                    continue;
                }

                foreach (ObjectId objectId3 in (BlockTableRecord)trx.GetObject(objectId2, OpenMode.ForRead))
                {
                    if (objectId3.ObjectClass.IsDerivedFrom(CurveRXClass))
                    {
                        Curve curve = (Curve)trx.GetObject(objectId3, OpenMode.ForRead);
                        if (curve is not Xline && curve is not Ray &&
                            curve.GetDistanceAtParameter(curve.EndParam) == 0.0)
                        {
                            _ = candidates.Add(objectId3);
                        }
                    }
                    else if (objectId3.ObjectClass.Name == "AcDbRegion" &&
                             trx.GetObject(objectId3, OpenMode.ForRead) is Region { Area: 0.0 })
                    {
                        _ = candidates.Add(objectId3);
                    }
                }
            }

            candidates.EraseObjects(trx);
            trx.Commit();
            return candidates.Count;
        }

        /// <summary>
        ///     Удаляет пустые текстовые объекты.
        /// </summary>
        public static int EmptyText(Database db)
        {
            RXClass DBTextRXClass = RXObject.GetClass(typeof(DBText));
            RXClass MTextRXClass = RXObject.GetClass(typeof(MText));
            using Transaction trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection candidates = [];

            foreach (ObjectId objectId2 in (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead))
            {
                if (objectId2.IsErased)
                {
                    continue;
                }

                foreach (ObjectId objectId3 in (BlockTableRecord)trx.GetObject(objectId2, OpenMode.ForRead))
                {
                    if (objectId3.ObjectClass == DBTextRXClass)
                    {
                        DBText dbtext = (DBText)trx.GetObject(objectId3, OpenMode.ForRead);
                        if (dbtext.TextString.Trim().Length == 0)
                        {
                            _ = candidates.Add(objectId3);
                        }
                    }
                    else if (objectId3.ObjectClass == MTextRXClass
                             && trx.GetObject(objectId3, OpenMode.ForRead) is MText { Text: not null } mtext
                             && mtext.Text.Trim().Length == 0)
                    {
                        _ = candidates.Add(objectId3);
                    }
                }
            }

            candidates.EraseObjects(trx);
            trx.Commit();
            return candidates.Count;
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

        public static int MLeaderStyle(Database db)
        {
            return PurgeDictionaryByRefCount(db, "ACAD_MLEADERSTYLE");
        }

        public static int DWF(Database db)
        {
            return PurgeDictionaryByRefCount(db, "ACAD_DWFDEFINITIONS");
        }

        public static int PDF(Database db)
        {
            return PurgeDictionaryByRefCount(db, "ACAD_PDFDEFINITIONS");
        }

        public static int DGN(Database db)
        {
            return PurgeDictionaryByRefCount(db, "ACAD_DGNDEFINITIONS");
        }

        private static int PurgeDictionaryByRefCount(Database db, string dictKey)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection toErase = [];
            DBDictionary rootDict = (DBDictionary)trx.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            if (rootDict.Contains(dictKey))
            {
                using ObjectIdCollection candidates = [];
                DBDictionary dictionary = (DBDictionary)trx.GetObject(rootDict.GetAt(dictKey), OpenMode.ForRead);

                foreach (DBDictionaryEntry entry in dictionary)
                {
                    _ = candidates.Add(entry.Value);
                }

                int[] refs = new int[candidates.Count];
                db.CountHardReferences(candidates, refs);

                for (int i = 0; i < candidates.Count; i++)
                {
                    if (refs[i] == 0)
                    {
                        _ = toErase.Add(candidates[i]);
                    }
                }
            }

            int deletedCount = PurgeAndErase(db, toErase);
            trx.Commit();
            return deletedCount;
        }

        /// <summary>
        ///     Очищает неиспользуемые ссылки на растровые изображения.
        /// </summary>
        public static int RasterImages(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection objectIdCollection = [];
            DBDictionary dbdictionary = (DBDictionary)trx.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            if (dbdictionary.Contains("ACAD_IMAGE_DICT"))
            {
                DBDictionary imageDictionary = (DBDictionary)trx.GetObject(dbdictionary.GetAt("ACAD_IMAGE_DICT"), OpenMode.ForRead);

                foreach (DBDictionaryEntry ImageEntry in imageDictionary)
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
            }

            int deletedCount = PurgeAndErase(db, objectIdCollection);
            trx.Commit();
            return deletedCount;
        }

        /// <summary>
        ///     Очищает пользовательские визуальные стили.
        /// </summary>
        public static int VisualStyle(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection objectIdCollection = [];
            foreach (DBDictionaryEntry VisualStyleEntry in (DBDictionary)trx.GetObject(db.VisualStyleDictionaryId,
                         OpenMode.ForRead))
            {
                DBVisualStyle visualStyle = (DBVisualStyle)trx.GetObject(VisualStyleEntry.Value, OpenMode.ForRead);
                if (visualStyle.Type == VisualStyleType.Custom && !visualStyle.IsAProxy)
                {
                    _ = objectIdCollection.Add(VisualStyleEntry.Value);
                }
            }

            int deletedCount = PurgeAndErase(db, objectIdCollection);
            trx.Commit();
            return deletedCount;
        }

        /// <summary>
        ///     Очищает неиспользуемые материалы.
        /// </summary>
        public static int Material(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection objectIdCollection = [];
            foreach (DBDictionaryEntry MaterialEntry in (DBDictionary)trx.GetObject(db.MaterialDictionaryId, OpenMode.ForRead,
                         false))
            {
                string key = MaterialEntry.Key;
                DBObject material = trx.GetObject(MaterialEntry.Value, OpenMode.ForRead);
                if (key != "ByBlock" && key != "ByLayer" && key != "Global" && !material.IsAProxy)
                {
                    _ = objectIdCollection.Add(MaterialEntry.Value);
                }
            }

            int deletedCount = PurgeAndErase(db, objectIdCollection);
            trx.Commit();
            return deletedCount;
        }

        /// <summary>
        ///     Очищает неиспользуемые текстовые стили.
        /// </summary>
        public static int TextStyle(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection objectIdCollection = [];
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

            int deletedCount = PurgeAndErase(db, objectIdCollection);
            trx.Commit();
            return deletedCount;
        }

        /// <summary>
        ///     Удаляет группы, в которых меньше двух объектов.
        /// </summary>
        public static int Groups(Database db)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection candidates = [];

            foreach (DBDictionaryEntry GroupEntry in (DBDictionary)trx.GetObject(db.GroupDictionaryId, OpenMode.ForRead, false))
            {
                Group group = (Group)trx.GetObject(GroupEntry.Value, OpenMode.ForRead, false);
                if (group.NumEntities < 2)
                {
                    _ = candidates.Add(GroupEntry.Value);
                }
            }

            int deletedCount = PurgeAndErase(db, candidates);
            trx.Commit();
            return deletedCount;
        }
    }
}
