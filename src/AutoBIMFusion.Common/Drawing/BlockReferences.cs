using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Common.AcadSupport;
using AutoBIMFusion.Common.Geometry;
using Autodesk.AutoCAD.ApplicationServices;
using System.Diagnostics;
using Exception = System.Exception;

namespace AutoBIMFusion.Common.Drawing;

public static class BlockReferences
{
    public static void ReplaceAllBlockReference(string OldBlockName, string NewBlockName, bool KeepScale = true, bool KeepRotation = true, bool PreserveProperties = true)
    {
        Database db = AcadContext.GetDatabase();

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable? bt = trx.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
        ObjectIdCollection objectIdCollection = [];

        // Проходим по всем BlockTableRecord (ModelSpace, PaperSpace, блоки и т.д.)
        foreach (ObjectId btrId in bt)
        {
            BlockTableRecord? btr = trx.GetObject(btrId, OpenMode.ForWrite) as BlockTableRecord;

            // Не обрабатывать анонимные блоки (например, созданные штриховками или внутренними динамическими блоками)
            if (btr.IsAnonymous || (!btr.IsLayout && !btr.IsFromExternalReference))
            {
                continue;
            }

            foreach (ObjectId entId in btr)
            {
                Entity? ent = entId.GetObject(OpenMode.ForRead) as Entity;
                if (ent is BlockReference br && br.GetBlockReferenceName() == OldBlockName)
                {
                    _ = objectIdCollection.Add(ent.ObjectId);
                }
            }
        }

        ReplaceAllBlockReference(objectIdCollection, NewBlockName, KeepScale, KeepRotation, PreserveProperties);
        Purge(OldBlockName);
        trx.Commit();
    }

    public static void ReplaceAllBlockReference(ObjectIdCollection objectIdCollection, string NewBlockName, bool KeepScale = true, bool KeepRotation = true, bool PreserveProperties = true)
    {
        Editor ed = AcadContext.GetEditor();
        Database db = AcadContext.GetDatabase();

        using Transaction trx = db.TransactionManager.StartTransaction();
        foreach (ObjectId entid in objectIdCollection)
        {
            Entity? ent = entid.GetObject(OpenMode.ForRead) as Entity;

            if (ent is BlockReference br)
            {
                ReplaceBlockReference(trx, br, NewBlockName, KeepScale, KeepRotation, PreserveProperties, ed);
            }
        }

        trx.Commit();
    }

    private static void ReplaceBlockReference(Transaction trx, BlockReference br, string NewBlockName, bool KeepScale, bool KeepRotation, bool PreserveProperties, Editor ed)
    {
        bool IsEntOnLockedLayer = br.IsEntityOnLockedLayer();
        Dictionary<string, string>? propertiesToCopy = PreserveProperties ? GetPropertiesFromBlock(trx, br) : null;

        if (IsEntOnLockedLayer)
        {
            Layers.SetLock(br.Layer, false);
        }

        br.UpgradeOpen();
        BlockTableRecord? ownerBtr = trx.GetObject(br.OwnerId, OpenMode.ForWrite) as BlockTableRecord;

        ObjectId newBrObjId = InsertFromName(NewBlockName, br.Position.ToPoints(),
            ed.GetUSCRotation(AngleUnit.Radians), propertiesToCopy, NewBlockName, ownerBtr);
        BlockReference? newBr = trx.GetObject(newBrObjId, OpenMode.ForWrite) as BlockReference;

        ApplyScaleAndRotation(br, newBr, KeepScale, KeepRotation);

        newBr.Color = br.Color;
        newBr.Layer = br.Layer;

        if (!br.IsErased)
        {
            br.Erase(true);
        }

        if (IsEntOnLockedLayer)
        {
            Layers.SetLock(br.Layer, true);
        }
    }

    private static void ApplyScaleAndRotation(BlockReference sourceBr, BlockReference targetBr, bool KeepScale, bool KeepRotation)
    {
        if (KeepScale)
        {
            targetBr.ScaleFactors = sourceBr.ScaleFactors;
        }

        if (KeepRotation)
        {
            targetBr.Rotation = sourceBr.Rotation;
        }
    }

    public static ObjectId Create(string Name, string Description, DBObjectCollection EntitiesDbObjectCollection, Points Origin, bool IsExplodable = true, BlockScaling BlockScaling = BlockScaling.Any)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable? bt = db.BlockTableId.GetDBObject(OpenMode.ForWrite) as BlockTable;
        string BlockName = Name;

        if (BlockName != "*U") // создание анонимного блока
        {
            BlockName = SymbolUtilityServices.RepairSymbolName(Name, false);
        }

        if (bt.Has(BlockName))
        {
            AcadContext.WriteMessage($"Блок {Name} уже существует в чертеже");
        }

        BlockTableRecord btr = new()
        {
            Name = BlockName,
            Comments = Description,
            Explodable = IsExplodable,
            Units = db.Insunits,
            BlockScaling = BlockScaling
        };
        if (Origin != Points.Null)
        {
            btr.Origin = Origin.SCG;
        }
        // Добавляем новый блок в таблицу блоков
        ObjectId btrId = bt.Add(btr);
        trx.AddNewlyCreatedDBObject(btr, true);

        foreach (Entity ent in EntitiesDbObjectCollection)
        {
            _ = btr.AppendEntity(ent);
            trx.AddNewlyCreatedDBObject(ent, true);
        }

        trx.Commit();
        return btrId;
    }

    public static ObjectId CreateFromExistingEnts(string Name, string Description, ObjectIdCollection SelectedIds, Points Origin, bool IsExplodable = true, BlockScaling BlockScaling = BlockScaling.Any, bool EraseOld = false)
    {
        // Этот метод даёт преимущество сохранения ассоциативных штриховок
        Editor ed = AcadContext.GetEditor();
        Database db = AcadContext.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable? bt = db.BlockTableId.GetDBObject(OpenMode.ForWrite) as BlockTable;
        string BlockName = Name;

        if (string.IsNullOrWhiteSpace(BlockName))
        {
            BlockName = "*U";
        }

        if (BlockName != "*U") // если создаём не анонимный блок
        {
            BlockName = SymbolUtilityServices.RepairSymbolName(Name, false);
        }

        if (bt.Has(BlockName))
        {
            AcadContext.WriteMessage($"Блок {Name} уже существует в чертеже");
        }

        IdMapping acIdMap = [];
        Database MemoryDatabase = new(true, false);

        using (Transaction MemoryTransaction = MemoryDatabase.TransactionManager.StartTransaction())
        {
            BlockTable? acBlkTblNewDoc = MemoryTransaction.GetObject(MemoryDatabase.BlockTableId, OpenMode.ForRead) as BlockTable;
            BlockTableRecord? acBlkTblRecNewDoc = MemoryTransaction.GetObject(acBlkTblNewDoc[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

            try
            {
                MemoryDatabase.WblockCloneObjects(SelectedIds, acBlkTblRecNewDoc.ObjectId, acIdMap, DuplicateRecordCloning.Replace, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return ObjectId.Null;
            }

            double UcsRotation = Vector3d.XAxis.GetAngleTo(ed.CurrentUserCoordinateSystem.CoordinateSystem3d.Xaxis, Vector3d.ZAxis);
            Matrix3d CounterRotation = Matrix3d.Rotation(-UcsRotation, Vector3d.ZAxis, new Point3d(0, 0, Origin.SCG.Z));
            Matrix3d DisplacementVector = Matrix3d.Displacement(Origin.SCG.Flatten().GetVectorTo(new Point3d(0, 0, Origin.SCG.Z)));

            foreach (ObjectId objId in acBlkTblRecNewDoc)
            {
                if (objId.IsValid && objId.GetObject(OpenMode.ForWrite) is Entity ent)
                {
                    ent.TransformBy(CounterRotation * DisplacementVector);
                }
            }

            MemoryTransaction.Commit();
        }


        ObjectId Id = db.Insert(BlockName, MemoryDatabase, false);

        BlockTableRecord? blockDef = Id.GetObject(OpenMode.ForWrite) as BlockTableRecord;
        blockDef.Comments = Description;
        blockDef.Explodable = IsExplodable;
        blockDef.BlockScaling = BlockScaling;
        blockDef.Units = db.Insunits;

        if (EraseOld)
        {
            SelectedIds.EraseObjects(trx);
        }

        trx.Commit();
        return Id;
    }


    public static ObjectId RenameBlockAndInsert(ObjectId BlockReferenceObjectId, string NewName)
    {
        if (!BlockReferenceObjectId.IsValid)
        {
            return ObjectId.Null;
        }

        ObjectIdCollection acObjIdColl = [BlockReferenceObjectId];

        Document ActualDocument = AcadContext.GetDocument();
        Database ActualDatabase = ActualDocument.Database;

        Database MemoryDatabase = new(true, false);
        IdMapping acIdMap = [];
        using (Transaction MemoryTransaction = MemoryDatabase.TransactionManager.StartTransaction())
        {
            BlockTable? acBlkTblNewDoc =
                MemoryTransaction.GetObject(MemoryDatabase.BlockTableId, OpenMode.ForRead) as BlockTable;
            BlockTableRecord? acBlkTblRecNewDoc =
                MemoryTransaction.GetObject(acBlkTblNewDoc[BlockTableRecord.ModelSpace], OpenMode.ForRead) as
                    BlockTableRecord;
            try
            {
                MemoryDatabase.WblockCloneObjects(acObjIdColl, acBlkTblRecNewDoc.ObjectId, acIdMap,
                    DuplicateRecordCloning.Replace, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return ObjectId.Null;
            }

            ObjectId NewBlkRefObjectIdInMemoryDB = acIdMap[BlockReferenceObjectId].Value;
            BlockReference? NewBlkRef =
                MemoryTransaction.GetObject(NewBlkRefObjectIdInMemoryDB, OpenMode.ForRead) as BlockReference;

            if (NewBlkRef.IsDynamicBlock)
            {
                BlockTableRecord dynbtr =
                    (BlockTableRecord)MemoryTransaction.GetObject(NewBlkRef.DynamicBlockTableRecord, OpenMode.ForWrite);
                dynbtr.Name = NewName;
            }
            else
            {
                BlockTableRecord btr = (BlockTableRecord)MemoryTransaction.GetObject(NewBlkRef.BlockTableRecord, OpenMode.ForWrite);
                btr.Name = NewName;
            }

            MemoryTransaction.Commit();
        }

        ObjectId newBlocRefenceId = acIdMap[BlockReferenceObjectId].Value;
        if (!newBlocRefenceId.IsValid)
        {
            return ObjectId.Null;
        }

        ObjectIdCollection acObjIdColl2 = [newBlocRefenceId];
        IdMapping acIdMap2 = [];

        using (AcadContext.GetLock())
        using (Transaction ActualTransaction = ActualDatabase.TransactionManager.StartTransaction())
        {
            BlockTable? acBlkTblNewDoc2 =
                ActualTransaction.GetObject(ActualDatabase.BlockTableId, OpenMode.ForRead) as BlockTable;
            BlockTableRecord acBlkTblRecNewDoc2 = AcadContext.GetCurrentSpaceBlockTableRecord(ActualTransaction);

            ActualDatabase.WblockCloneObjects(acObjIdColl2, acBlkTblRecNewDoc2.ObjectId, acIdMap2,
                DuplicateRecordCloning.Replace, false);
            ActualTransaction.Commit();
        }

        return acIdMap2[newBlocRefenceId].Value;
    }

    public static string GetUniqueBlockName(string oldName)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable? bt = trx.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
        string newName = oldName;
        int index = 1;

        while (bt.Has(newName))
        {
            newName = SymbolUtilityServices.RepairSymbolName($"{oldName}_{(index > 1 ? $" ({index})" : "")}", false);
            index++;
        }

        return newName;
    }

    public static bool IsBlockExist(string BlocName)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable? bt = db.BlockTableId.GetObject(OpenMode.ForRead) as BlockTable;
        return bt.Has(BlocName);
    }


    public static ObjectIdCollection GetDynamicBlockReferences(string BlockName)
    {
        if (string.IsNullOrEmpty(BlockName))
        {
            return [];
        }

        Database db = AcadContext.GetDatabase();
        ObjectIdCollection result = [];

        using (Transaction trx = db.TransactionManager.StartTransaction())
        {
            BlockTable bt = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(BlockName))
            {
                return result;
            }

            BlockTableRecord btr = (BlockTableRecord)trx.GetObject(bt[BlockName], OpenMode.ForRead);

            if (!btr.IsDynamicBlock)
            {
                return result;
            }

            foreach (ObjectId anonBtrId in btr.GetAnonymousBlockIds())
            {
                BlockTableRecord anonBtr = (BlockTableRecord)trx.GetObject(anonBtrId, OpenMode.ForRead);
                result.Join(anonBtr.GetBlockReferenceIds(true, true));
            }

            trx.Commit();
        }

        return result;
    }

    public static BlockReference GetBlockReference(string BlocName, Point3d PositionSCG)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable? bt = db.BlockTableId.GetObject(OpenMode.ForRead) as BlockTable;

        if (!bt.Has(BlocName))
        {
            throw new InvalidOperationException($"Блок {BlocName} не существует в чертеже");
        }

        BlockTableRecord? blockDef = bt[BlocName].GetObject(OpenMode.ForRead) as BlockTableRecord;
        trx.Commit();
        return new BlockReference(PositionSCG, blockDef.ObjectId);
    }

    public static ObjectId InsertFromName(string BlocName, Points BlocLocation, double Angle = 0, Dictionary<string, string> AttributesValues = null, string Layer = null, BlockTableRecord targetSpace = null)
    {
        Database db = AcadContext.GetDatabase();

        using Transaction trx = db.TransactionManager.StartTransaction();

        if (targetSpace == null)
        {
            targetSpace = AcadContext.GetCurrentSpaceBlockTableRecord(trx);
        }

        BlockTable? bt = db.BlockTableId.GetObject(OpenMode.ForRead) as BlockTable;
        BlockTableRecord? blockDef = bt[BlocName].GetObject(OpenMode.ForRead) as BlockTableRecord;
        using BlockReference blockRef = GetBlockReference(BlocName, BlocLocation.SCG);
        blockRef.Color = db.Cecolor;
        blockRef.Rotation = Angle;

        if (!string.IsNullOrEmpty(Layer) && Layers.CheckIfLayerExist(Layer))
        {
            blockRef.Layer = Layer;
        }

        _ = targetSpace.AppendEntity(blockRef);
        trx.AddNewlyCreatedDBObject(blockRef, true);

        ApplyPropertiesToBlock(trx, blockDef, blockRef, AttributesValues);

        trx.Commit();
        return blockRef.ObjectId;
    }

    private static Dictionary<string, string> GetPropertiesFromBlock(Transaction trx, BlockReference br)
    {
        Dictionary<string, string> propertiesToCopy = [];

        foreach (ObjectId attId in br.AttributeCollection)
        {
            AttributeReference? attRef = trx.GetObject(attId, OpenMode.ForRead) as AttributeReference;
            if (attRef != null)
            {
                propertiesToCopy[attRef.Tag.ToUpperInvariant()] = attRef.TextString;
            }
        }

        if (br.IsDynamicBlock)
        {
            foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
            {
                propertiesToCopy[prop.PropertyName.ToUpperInvariant()] = prop.Value.ToString();
            }
        }

        return propertiesToCopy;
    }

    private static void ApplyPropertiesToBlock(Transaction trx, BlockTableRecord btr, BlockReference blockRef,
        Dictionary<string, string> attributesValues)
    {
        if (attributesValues == null || attributesValues.Count == 0)
        {
            return;
        }

        ApplyAttributeDefinitions(trx, btr, blockRef, attributesValues);
        ApplyDynamicBlockProperties(blockRef, attributesValues);
    }

    private static void ApplyAttributeDefinitions(Transaction trx, BlockTableRecord btr, BlockReference blockRef, Dictionary<string, string> attributesValues)
    {
        foreach (ObjectId id in btr)
        {
            DBObject obj = id.GetObject(OpenMode.ForRead);
            AttributeDefinition? attDef = obj as AttributeDefinition;

            if (attDef?.Constant == false)
            {
                ApplyAttributeValue(trx, blockRef, attDef, attributesValues);
            }
        }
    }

    private static void ApplyAttributeValue(Transaction trx, BlockReference blockRef, AttributeDefinition attDef,
        Dictionary<string, string> attributesValues)
    {
        string PropertyName = attDef.Tag.ToUpperInvariant();

        if (!attributesValues.TryGetValue(PropertyName, out string? AttributeDefinitionTargetValue))
        {
            return;
        }

        using AttributeReference attRef = new();
        attRef.SetAttributeFromBlock(attDef, blockRef.BlockTransform);
        attRef.TextString = AttributeDefinitionTargetValue;
        _ = blockRef.AttributeCollection.AppendAttribute(attRef);
        trx.AddNewlyCreatedDBObject(attRef, true);
    }

    private static void ApplyDynamicBlockProperties(BlockReference blockRef, Dictionary<string, string> attributesValues)
    {
        Dictionary<string, DynamicBlockReferenceProperty> propertyDictionary = BuildDynamicPropertyDictionary(blockRef);

        foreach (string ValueKey in attributesValues.Keys)
        {
            if (propertyDictionary.TryGetValue(ValueKey, out DynamicBlockReferenceProperty? BlocProperty))
            {
                SetDynamicPropertyValue(BlocProperty, attributesValues[ValueKey]);
            }
        }
    }

    private static Dictionary<string, DynamicBlockReferenceProperty> BuildDynamicPropertyDictionary(BlockReference blockRef)
    {
        Dictionary<string, DynamicBlockReferenceProperty> propertyDictionary = [];
        DynamicBlockReferencePropertyCollection propertyCollection = blockRef.DynamicBlockReferencePropertyCollection;

        foreach (DynamicBlockReferenceProperty property in propertyCollection.OfType<DynamicBlockReferenceProperty>())
        {
            string propertyName = property.PropertyName.ToUpperInvariant();

            if (!propertyDictionary.ContainsKey(propertyName))
            {
                propertyDictionary.Add(propertyName, property);
            }
        }

        return propertyDictionary;
    }

    private static void SetDynamicPropertyValue(DynamicBlockReferenceProperty property, string valueToConvert)
    {
        object Value = ConvertValueToProperty((DwgDataType)property.PropertyTypeCode, valueToConvert);

        switch (Value)
        {
            case int ValueAsInt:
                property.Value = (short)ValueAsInt;
                break;
            case double ValueAsDbl:
                property.Value = ValueAsDbl;
                break;
            case string ValueAsStr:
                property.Value = ValueAsStr;
                break;
        }
    }


    public static ObjectId InsertFromNameImportIfNotExist(string BlocName, Points BlocLocation, double Angle = 0,
        Dictionary<string, string> AttributesValues = null, string Layer = null)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        ImportBlocFromBlocNameIfMissing(BlocName);
        ObjectId blockRefObjectId = InsertFromName(BlocName, BlocLocation, Angle, AttributesValues, Layer);
        trx.Commit();
        return blockRefObjectId;
    }

    public static void Purge(string BlocName)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trans = db.TransactionManager.StartTransaction();
        BlockTable? bt = trans.GetObject(db.BlockTableId, OpenMode.ForWrite) as BlockTable;

        foreach (ObjectId oid in bt)
        {
            BlockTableRecord? btr = trans.GetObject(oid, OpenMode.ForWrite) as BlockTableRecord;

            if (btr.Name.Equals(BlocName, StringComparison.OrdinalIgnoreCase))
            {
                var blockRefIds = btr.GetBlockReferenceIds(false, false);

                if (blockRefIds.Count == 0 && !btr.IsLayout)
                {
                    btr.Erase();
                }
            }
        }

        trans.Commit();
    }

    public static DBObjectCollection InitForTransient(string BlocName, Dictionary<string, string> InitAttributesValues,
        string Layer = null)
    {
        Database db = AcadContext.GetDatabase();
        Editor ed = AcadContext.GetEditor();
        DBObjectCollection ents = [];
        using (Transaction trx = db.TransactionManager.StartTransaction())
        {
            // Первый блок добавляется для инициализации процесса, а затем удаляется. Обязательно добавьте значение.
            ObjectId blockRef = InsertFromNameImportIfNotExist(BlocName, Points.Empty, ed.GetUSCRotation(AngleUnit.Radians),
                InitAttributesValues);
            DBObject dBObject = blockRef.GetDBObject();
            if (Layer != null && Layers.CheckIfLayerExist(Layer))
            {
                (dBObject as Entity).Layer = Layer;
            }

            blockRef.EraseObject();
            _ = ents.Add(dBObject);
            trx.Commit();
        }

        return ents;
    }

    public static void ImportBlocFromBlocNameIfMissing(string BlocName)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable bt = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);

        if (!bt.Has(BlocName))
        {
            string TempFolderPath = Path.GetTempPath();
            string TempFolderFilePath = Path.Combine(TempFolderPath, $"{BlocName}.dwg");
            AcadContext.ReadWriteToFileResource(BlocName, TempFolderFilePath);

            Database sourceDb = new(false, true); // Временная база данных для хранения данных импортируемого блока
            try
            {
                sourceDb.ReadDwgFile(TempFolderFilePath, FileShare.Read, true,
                    ""); // Чтение DWG во вспомогательную базу данных
                _ = db.Insert(TempFolderFilePath, sourceDb, false);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                AcadContext.WriteMessage("\nОшибка: " + ex.Message);
            }
            finally
            {
                sourceDb.Dispose();
            }
        }

        trx.Commit();
    }

    private static object ConvertValueToProperty(DwgDataType dataType, string valueToConvert)
    {
        switch (dataType)
        {
            case DwgDataType.Real:
                if (double.TryParse(valueToConvert, out double convertedValueDouble))
                {
                    return convertedValueDouble;
                }

                break;
            case DwgDataType.Int16:
            case DwgDataType.Int32:
                if (int.TryParse(valueToConvert, out int convertedValueInt))
                {
                    return convertedValueInt;
                }

                break;
            case DwgDataType.Text:
                return valueToConvert;
        }

        return null;
    }

    // --- Утилиты для работы с блоками (перенесены из Merge) ---

    /// <summary>
    ///     Находит границы BlockReference с максимальной площадью среди переданных ObjectId.
    ///     Возвращает габариты найденного блока или null, если ни одного блока нет.
    /// </summary>
    public static Extents3d? FindLargestBlockReferenceBoundsByArea(Transaction trx, IEnumerable<ObjectId> ids)
    {
        Extents3d? bestExtents = null;
        double bestArea = 0.0;

        foreach (ObjectId id in ids)
        {
            if (trx.GetObject(id, OpenMode.ForRead) is not BlockReference br)
            {
                continue;
            }

            Extents3d? ext = ExtentsUtils.TryGetExtents(br);
            if (!ext.HasValue)
            {
                continue;
            }

            double width = ext.Value.MaxPoint.X - ext.Value.MinPoint.X;
            double height = ext.Value.MaxPoint.Y - ext.Value.MinPoint.Y;
            double area = width * height;

            if (area > bestArea)
            {
                bestArea = area;
                bestExtents = ext.Value;
            }
        }

        return bestExtents;
    }

    /// <summary>
    ///     Стирает все сущности внутри BlockTableRecord.
    /// </summary>
    public static void EraseBlockContents(Database db, ObjectId btrId)
    {
        if (btrId.IsNull)
        {
            return;
        }

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForRead);

        foreach (ObjectId id in btr)
        {
            if (trx.GetObject(id, OpenMode.ForWrite) is Entity entity && !entity.IsErased)
            {
                entity.Erase();
            }
        }

        trx.Commit();
    }

    private enum DwgDataType : short
    {
        Null = 0,
        Real = 1,
        Int32 = 2,
        Int16 = 3,
        Int8 = 4,
        Text = 5,
        BChunk = 6,
        Handle = 7,
        HardOwnershipId = 8,
        SoftOwnershipId = 9,
        HardPointerId = 10,
        SoftPointerId = 11,
        Dwg3Real = 12,
        Int64 = 13,
        NotRecognized = 19
    }
}
