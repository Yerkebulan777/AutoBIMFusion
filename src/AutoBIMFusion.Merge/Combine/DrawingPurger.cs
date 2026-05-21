using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.AcadSupport;
using Autodesk.AutoCAD.GraphicsInterface;
using Serilog.Core;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Выполняет очистку чертежа от неиспользуемых объектов и служебного мусора.
///     Перед удалением временно снимает блокировку со слоёв и восстанавливает её после завершения.
/// </summary>
public static class DrawingPurger
{
    /// <summary>
    ///     Запускает очистку для указанной базы данных чертежа.
    /// </summary>
    public static void Purge(Database db)
    {
        var purgeReport = CorePurge(db);
        var TotalDeletedCount = purgeReport.Values.Sum();

        // Выводим отчёт пользователю.
        if (TotalDeletedCount == 0)
        {
            AcadContext.WriteMessage("Чертёж уже очищен.");
        }
        else
        {
            var maxLength = purgeReport.Max(p => p.Key.Length);
            foreach (var entry in purgeReport)
                AcadContext.WriteMessage($" - {entry.Key.PadRight(maxLength)} : удалено {entry.Value}");

            AcadContext.WriteMessage($"Итого: {TotalDeletedCount} элементов удалено из чертежа");
        }

        ViewportLock.DoLockUnlock(true);
    }

    /// <summary>
    ///     Выполняет глубокую программную очистку (Purge) базы данных DWG
    ///     перед сохранением итогового файла.
    ///     Основной путь оптимизации итогового DWG.
    /// </summary>
    public static void Optimize(Database db, Logger log)
    {
        var purgeReport = CorePurge(db);
        var totalDeletedCount = purgeReport.Values.Sum();

        if (totalDeletedCount == 0) return;

        log.Information("Purge: удалено объектов: {TotalCount}", totalDeletedCount);
    }

    private static Dictionary<string, int> CorePurge(Database db)
    {
        Dictionary<string, int> purgeReport = [];
        var totalDeletedCount = 0;

        using (var trx = db.TransactionManager.StartTransaction())
        {
            // Временно снимаем блокировку со всех слоёв, чтобы выполнить очистку.
            List<LayerTableRecord> list = [];

            foreach (var objectId in (LayerTable)trx.GetObject(db.LayerTableId, OpenMode.ForRead))
            {
                var layerTableRecord = (LayerTableRecord)trx.GetObject(objectId, OpenMode.ForRead);
                if (layerTableRecord.IsLocked)
                {
                    _ = trx.GetObject(layerTableRecord.ObjectId, OpenMode.ForWrite);
                    layerTableRecord.IsLocked = false;
                    list.Add(layerTableRecord);
                }
            }

            // Базовая очистка.
            AddToReport(purgeReport, nameof(PurgeMethods.CurvesZeroLength), PurgeMethods.CurvesZeroLength(db),
                ref totalDeletedCount);
            AddToReport(purgeReport, nameof(PurgeMethods.EmptyText), PurgeMethods.EmptyText(db), ref totalDeletedCount);
            AddToReport(purgeReport, nameof(PurgeMethods.XREF), PurgeMethods.XREF(db), ref totalDeletedCount);

            // Повторяем очистку, пока появляются новые удаляемые элементы.
            var previousPassTotalDeletedCount = -1;
            var passCount = 0;
            while (previousPassTotalDeletedCount != totalDeletedCount && passCount < 10)
            {
                passCount++;
                previousPassTotalDeletedCount = totalDeletedCount;

                AddToReport(purgeReport, nameof(PurgeMethods.Database), PurgeMethods.Database(db),
                    ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.DWF), PurgeMethods.DWF(db), ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.PDF), PurgeMethods.PDF(db), ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.DGN), PurgeMethods.DGN(db), ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.RasterImages), PurgeMethods.RasterImages(db),
                    ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.MLeaderStyle), PurgeMethods.MLeaderStyle(db),
                    ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.VisualStyle), PurgeMethods.VisualStyle(db),
                    ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.Material), PurgeMethods.Material(db),
                    ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.TextStyle), PurgeMethods.TextStyle(db),
                    ref totalDeletedCount);
                AddToReport(purgeReport, nameof(PurgeMethods.Groups), PurgeMethods.Groups(db), ref totalDeletedCount);
            }

            // Возвращаем блокировку слоёв в исходное состояние.
            foreach (var layerTableRecord2 in list) layerTableRecord2.IsLocked = true;

            trx.Commit();
        }

        return purgeReport;
    }

    private static void AddToReport(Dictionary<string, int> purgeReport, string key, int count,
        ref int totalDeletedCount)
    {
        if (count == 0) return;

        if (!purgeReport.TryAdd(key, count)) purgeReport[key] += count;

        totalDeletedCount += count;
    }

    private static class PurgeMethods
    {
        private static int PurgeAndErase(Database db, ObjectIdCollection objectIds)
        {
            if (objectIds.Count == 0) return 0;

            db.Purge(objectIds);

            foreach (ObjectId objectId in objectIds)
                if (objectId.IsValid && !objectId.IsErased)
                    objectId.GetDBObject(OpenMode.ForWrite).Erase();

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

            using var trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection objectIdCollection = [];

            foreach (var obj in tableIds)
            {
                var objectId = (ObjectId)obj;
                var symbolTable = (SymbolTable)trx.GetObject(objectId, OpenMode.ForRead);
                foreach (var objectId2 in symbolTable)
                {
                    var record = (SymbolTableRecord)trx.GetObject(objectId2, OpenMode.ForRead);
                    if (!record.IsDependent) _ = objectIdCollection.Add(objectId2);
                }
            }

            foreach (var obj2 in dictIds)
            {
                var objectId3 = (ObjectId)obj2;
                var dictionary = (DBDictionary)trx.GetObject(objectId3, OpenMode.ForRead);
                foreach (var dbdictionaryEntry in dictionary)
                {
                    if (!dbdictionaryEntry.Value.IsValid) continue;

                    var dbObject = trx.GetObject(dbdictionaryEntry.Value, OpenMode.ForRead);
                    if (!dbObject.IsAProxy) _ = objectIdCollection.Add(dbdictionaryEntry.Value);
                }
            }

            var deletedCount = PurgeAndErase(db, objectIdCollection);
            trx.Commit();
            return deletedCount;
        }

        /// <summary>
        ///     Удаляет вырожденные кривые и регионы с нулевой площадью.
        /// </summary>
        public static int CurvesZeroLength(Database db)
        {
            var CurveRXClass = RXObject.GetClass(typeof(Curve));
            using var trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection candidates = [];

            foreach (var objectId2 in (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead))
            {
                if (objectId2.IsErased) continue;

                foreach (var objectId3 in (BlockTableRecord)trx.GetObject(objectId2, OpenMode.ForRead))
                    if (objectId3.ObjectClass.IsDerivedFrom(CurveRXClass))
                    {
                        var curve = (Curve)trx.GetObject(objectId3, OpenMode.ForRead);
                        if (curve is not Xline && curve is not Ray &&
                            curve.GetDistanceAtParameter(curve.EndParam) == 0.0)
                            _ = candidates.Add(objectId3);
                    }
                    else if (objectId3.ObjectClass.Name == "AcDbRegion" &&
                             trx.GetObject(objectId3, OpenMode.ForRead) is Region { Area: 0.0 })
                    {
                        _ = candidates.Add(objectId3);
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
            var DBTextRXClass = RXObject.GetClass(typeof(DBText));
            var MTextRXClass = RXObject.GetClass(typeof(MText));
            using var trx = db.TransactionManager.StartTransaction();
            var deletedCount = 0;

            foreach (var objectId2 in (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead))
            {
                if (objectId2.IsErased) continue;

                foreach (var objectId3 in (BlockTableRecord)trx.GetObject(objectId2, OpenMode.ForRead))
                    if (objectId3.ObjectClass == DBTextRXClass)
                    {
                        var dbtext = (DBText)trx.GetObject(objectId3, OpenMode.ForRead);
                        if (dbtext.TextString.Trim().Length == 0)
                        {
                            dbtext.UpgradeOpen();
                            dbtext.Erase();
                            deletedCount++;
                        }
                    }
                    else if (objectId3.ObjectClass == MTextRXClass
                             && trx.GetObject(objectId3, OpenMode.ForRead) is MText { Text: not null } mtext
                             && mtext.Text.Trim().Length == 0)
                    {
                        mtext.UpgradeOpen();
                        mtext.Erase();
                        deletedCount++;
                    }
            }

            trx.Commit();
            return deletedCount;
        }

        /// <summary>
        ///     Отсоединяет неиспользуемые внешние ссылки.
        /// </summary>
        public static int XREF(Database db)
        {
            using var trx = db.TransactionManager.StartTransaction();
            var NumberDetachedXREF = 0;
            foreach (var XrefId in (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead))
                if (!XrefId.IsErased)
                {
                    var blockTableRecord = (BlockTableRecord)trx.GetObject(XrefId, OpenMode.ForRead);
                    if (!blockTableRecord.IsLayout && blockTableRecord.IsFromExternalReference &&
                        blockTableRecord.GetBlockReferenceIds(true, false).Count == 0)
                    {
                        db.DetachXref(blockTableRecord.ObjectId);
                        NumberDetachedXREF++;
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
            using var trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection toErase = [];
            var rootDict = (DBDictionary)trx.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            if (rootDict.Contains(dictKey))
            {
                using ObjectIdCollection candidates = [];
                var dictionary = (DBDictionary)trx.GetObject(rootDict.GetAt(dictKey), OpenMode.ForRead);

                foreach (var entry in dictionary) _ = candidates.Add(entry.Value);

                var refs = new int[candidates.Count];
                db.CountHardReferences(candidates, refs);

                for (var i = 0; i < candidates.Count; i++)
                    if (refs[i] == 0)
                        _ = toErase.Add(candidates[i]);
            }

            var deletedCount = PurgeAndErase(db, toErase);
            trx.Commit();
            return deletedCount;
        }

        /// <summary>
        ///     Очищает неиспользуемые ссылки на растровые изображения.
        /// </summary>
        public static int RasterImages(Database db)
        {
            using var trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection objectIdCollection = [];
            var dbdictionary = (DBDictionary)trx.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            if (dbdictionary.Contains("ACAD_IMAGE_DICT"))
            {
                var imageDictionary =
                    (DBDictionary)trx.GetObject(dbdictionary.GetAt("ACAD_IMAGE_DICT"), OpenMode.ForRead);

                foreach (var ImageEntry in imageDictionary)
                    if (ImageEntry.Value.IsValid)
                    {
                        var rasterImageDef = trx.GetObject(ImageEntry.Value, OpenMode.ForRead) as RasterImageDef;
                        if (rasterImageDef?.IsAProxy == false && rasterImageDef.GetEntityCount(out _) == 0)
                            _ = objectIdCollection.Add(ImageEntry.Value);
                    }
            }

            var deletedCount = PurgeAndErase(db, objectIdCollection);
            trx.Commit();
            return deletedCount;
        }

        /// <summary>
        ///     Очищает пользовательские визуальные стили.
        /// </summary>
        public static int VisualStyle(Database db)
        {
            using var trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection objectIdCollection = [];
            foreach (var VisualStyleEntry in (DBDictionary)trx.GetObject(db.VisualStyleDictionaryId,
                         OpenMode.ForRead))
            {
                var visualStyle = (DBVisualStyle)trx.GetObject(VisualStyleEntry.Value, OpenMode.ForRead);
                if (visualStyle.Type == VisualStyleType.Custom && !visualStyle.IsAProxy)
                    _ = objectIdCollection.Add(VisualStyleEntry.Value);
            }

            var deletedCount = PurgeAndErase(db, objectIdCollection);
            trx.Commit();
            return deletedCount;
        }

        /// <summary>
        ///     Очищает неиспользуемые материалы.
        /// </summary>
        public static int Material(Database db)
        {
            using var trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection objectIdCollection = [];
            foreach (var MaterialEntry in (DBDictionary)trx.GetObject(db.MaterialDictionaryId, OpenMode.ForRead,
                         false))
            {
                var key = MaterialEntry.Key;
                var material = trx.GetObject(MaterialEntry.Value, OpenMode.ForRead);
                if (key != "ByBlock" && key != "ByLayer" && key != "Global" && !material.IsAProxy)
                    _ = objectIdCollection.Add(MaterialEntry.Value);
            }

            var deletedCount = PurgeAndErase(db, objectIdCollection);
            trx.Commit();
            return deletedCount;
        }

        /// <summary>
        ///     Очищает неиспользуемые текстовые стили.
        /// </summary>
        public static int TextStyle(Database db)
        {
            using var trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection objectIdCollection = [];
            foreach (var TextStyleTableId in (TextStyleTable)trx.GetObject(db.TextStyleTableId, OpenMode.ForRead))
            {
                var textStyleTableRecord = (TextStyleTableRecord)trx.GetObject(TextStyleTableId, OpenMode.ForRead);
                if (textStyleTableRecord.IsShapeFile && textStyleTableRecord.Name != "" &&
                    !textStyleTableRecord.IsAProxy &&
                    !textStyleTableRecord.IsDependent)
                    _ = objectIdCollection.Add(TextStyleTableId);
            }

            var deletedCount = PurgeAndErase(db, objectIdCollection);
            trx.Commit();
            return deletedCount;
        }

        /// <summary>
        ///     Удаляет группы, в которых меньше двух объектов.
        /// </summary>
        public static int Groups(Database db)
        {
            using var trx = db.TransactionManager.StartTransaction();
            using ObjectIdCollection candidates = [];

            foreach (var GroupEntry in (DBDictionary)trx.GetObject(db.GroupDictionaryId, OpenMode.ForRead, false))
            {
                var group = (Group)trx.GetObject(GroupEntry.Value, OpenMode.ForRead, false);
                if (group.NumEntities < 2) _ = candidates.Add(GroupEntry.Value);
            }

            var deletedCount = PurgeAndErase(db, candidates);
            trx.Commit();
            return deletedCount;
        }
    }
}
